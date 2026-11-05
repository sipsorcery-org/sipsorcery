using System;
using System.Linq;
using System.Net;
using System.Reflection;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests;

public class RTPSessionFeedbackRoutingUnitTest
{
    private const int RembPayloadLength = 20;
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PictureLossFeedbackTargetsVideoWithUnknownReceiverSsrc(bool includeAudio, bool appendRemb)
    {
        using var session = new RTPSession(true, true, false);
        if (includeAudio)
        {
            session.addTrack(new MediaStreamTrack(new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                MediaStreamStatusEnum.SendOnly));
        }
        session.addTrack(new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.AV1, 102),
            MediaStreamStatusEnum.SendOnly));
        var feedback = new RTCPFeedback(uint.MaxValue, session.VideoLocalTrack.Ssrc, PSFBFeedbackTypesEnum.PLI);
        SDPMediaTypesEnum? receivedMedia = null;
        session.OnReceiveReport += (_, media, packet) =>
        {
            Assert.Contains(packet.FeedbackMessages, message => message.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI);
            receivedMedia = media;
        };

        var receiveRtcp = typeof(RTPSession).GetMethod("OnReceiveRTCPPacket", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(receiveRtcp);
        byte[] bytes = feedback.GetBytes();
        if (appendRemb)
        {
            bytes = bytes.Concat(CreateRemb(feedback.SenderSSRC, feedback.MediaSSRC).GetBytes()).ToArray();
        }
        receiveRtcp.Invoke(session, new object[] { 0, new IPEndPoint(IPAddress.Loopback, 5005), bytes });

        Assert.Equal(SDPMediaTypesEnum.video, receivedMedia);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompoundPacketPreservesPictureLossFollowedByRemb(bool useTryParse)
    {
        const uint receiverSsrc = 123;
        const uint videoSsrc = 456;
        var pli = new RTCPFeedback(receiverSsrc, videoSsrc, PSFBFeedbackTypesEnum.PLI);
        byte[] bytes = pli.GetBytes().Concat(CreateRemb(receiverSsrc, videoSsrc).GetBytes()).ToArray();
        RTCPCompoundPacket packet;
        if (useTryParse)
        {
            Assert.True(RTCPCompoundPacket.TryParse(bytes.AsSpan(), out packet, out int consumed));
            Assert.Equal(bytes.Length, consumed);
        }
        else
        {
            packet = new RTCPCompoundPacket(bytes);
        }

        Assert.Equal(2, packet.FeedbackMessages.Count);
        Assert.Equal(PSFBFeedbackTypesEnum.PLI, packet.FeedbackMessages[0].Header.PayloadFeedbackMessageType);
        Assert.Equal(videoSsrc, packet.FeedbackMessages[0].MediaSSRC);
        Assert.Equal(PSFBFeedbackTypesEnum.AFB, packet.FeedbackMessages[1].Header.PayloadFeedbackMessageType);
        Assert.Same(packet.FeedbackMessages[1], packet.Feedback);
    }

    /// <summary>
    /// A standalone reduced-size PLI (RFC 5506) is exactly 12 bytes. It must get through the
    /// receive sanity check and be routed to the video stream by its media SSRC.
    /// </summary>
    [Fact]
    public void StandaloneTwelveBytePictureLossIsReceived()
    {
        using var session = new RTPSession(false, false, false);
        session.addTrack(new MediaStreamTrack(new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            MediaStreamStatusEnum.SendRecv));
        session.addTrack(new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, 96),
            MediaStreamStatusEnum.SendRecv));

        var pli = new RTCPFeedback(0x11223344, session.VideoLocalTrack.Ssrc, PSFBFeedbackTypesEnum.PLI);
        byte[] bytes = pli.GetBytes();
        Assert.Equal(12, bytes.Length);

        SDPMediaTypesEnum? receivedMedia = null;
        RTCPCompoundPacket receivedPacket = null;
        session.OnReceiveReport += (_, media, packet) =>
        {
            receivedMedia = media;
            receivedPacket = packet;
        };

        InvokeOnReceive(session, bytes);

        Assert.Equal(SDPMediaTypesEnum.video, receivedMedia);
        Assert.NotNull(receivedPacket);
        var received = Assert.Single(receivedPacket.FeedbackMessages);
        Assert.Equal(PSFBFeedbackTypesEnum.PLI, received.Header.PayloadFeedbackMessageType);
        Assert.Equal(session.VideoLocalTrack.Ssrc, received.MediaSSRC);
    }

    /// <summary>
    /// An empty receiver report is 8 bytes and must parse without throwing.
    /// </summary>
    [Fact]
    public void EightByteReceiverReportParses()
    {
        byte[] bytes = { 0x80, (byte)RTCPReportTypesEnum.RR, 0x00, 0x01, 0x01, 0x02, 0x03, 0x04 };

        var packet = new RTCPCompoundPacket(bytes);

        Assert.NotNull(packet.ReceiverReport);
        Assert.Equal(0x01020304u, packet.ReceiverReport.SSRC);
        Assert.Empty(packet.ReceiverReport.ReceptionReports);
    }

    /// <summary>
    /// Anything shorter than an RTCP header plus sender SSRC is still discarded.
    /// </summary>
    [Fact]
    public void SevenByteBufferIsIgnored()
    {
        using var session = new RTPSession(false, false, false);
        session.addTrack(new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, 96),
            MediaStreamStatusEnum.SendRecv));

        bool reportRaised = false;
        session.OnReceiveReport += (_, _, _) => reportRaised = true;

        byte[] bytes = { 0x81, (byte)RTCPReportTypesEnum.PSFB, 0x00, 0x02, 0x11, 0x22, 0x33 };
        InvokeOnReceive(session, bytes);

        Assert.False(reportRaised);
    }

    private static void InvokeOnReceive(RTPSession session, byte[] buffer)
    {
        var onReceive = typeof(RTPSession).GetMethod("OnReceive", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(onReceive);
        onReceive.Invoke(session, new object[] { 0, new IPEndPoint(IPAddress.Loopback, 5005), buffer });
    }

    private static RTCPFeedback CreateRemb(uint receiverSsrc, uint videoSsrc) =>
        new RTCPFeedback(receiverSsrc, 0, PSFBFeedbackTypesEnum.AFB)
        {
            SENDER_PAYLOAD_SIZE = RembPayloadLength,
            UniqueID = "REMB",
            NumSsrcs = 1,
            FeedbackSSRCs = new[] { videoSsrc },
            BitrateMantissa = 100000,
        };

}
