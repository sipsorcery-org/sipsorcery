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
    /// A Full Intra Request (RFC 5104) sets the media source SSRC to 0 and names the target in its FCI.
    /// It must still reach OnReceiveReport for the video stream, standalone or ahead of a REMB.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullIntraRequestTargetsVideoBySsrcInFci(bool appendRemb)
    {
        using var session = new RTPSession(true, true, false);
        session.addTrack(new MediaStreamTrack(new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            MediaStreamStatusEnum.SendOnly));
        session.addTrack(new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, 96),
            MediaStreamStatusEnum.SendOnly));
        var fir = RTCPFeedback.CreateFullIntraRequest(uint.MaxValue, session.VideoLocalTrack.Ssrc, 7);
        SDPMediaTypesEnum? receivedMedia = null;
        RTCPCompoundPacket received = null;
        session.OnReceiveReport += (_, media, packet) =>
        {
            receivedMedia = media;
            received = packet;
        };

        var receiveRtcp = typeof(RTPSession).GetMethod("OnReceiveRTCPPacket", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(receiveRtcp);
        byte[] bytes = fir.GetBytes();
        if (appendRemb)
        {
            bytes = bytes.Concat(CreateRemb(fir.SenderSSRC, session.VideoLocalTrack.Ssrc).GetBytes()).ToArray();
        }
        receiveRtcp.Invoke(session, new object[] { 0, new IPEndPoint(IPAddress.Loopback, 5005), bytes });

        Assert.Equal(SDPMediaTypesEnum.video, receivedMedia);
        var receivedFir = Assert.Single(received.FeedbackMessages, x => x.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.FIR);
        Assert.Equal(session.VideoLocalTrack.Ssrc, receivedFir.FeedbackSSRC);
        Assert.Equal(appendRemb ? 2 : 1, received.FeedbackMessages.Count);
    }

    /// <summary>
    /// FIR packets parse and serialise their FCI entries, so a compound packet is not misaligned.
    /// </summary>
    [Fact]
    public void FullIntraRequestRoundTripsFciEntries()
    {
        // V=2, FMT=4 (FIR), PT=206, length=6 (two FCI entries).
        byte[] bytes =
        {
            0x84, 206, 0x00, 0x06,
            0x00, 0x00, 0x00, 0x01,   // Sender SSRC.
            0x00, 0x00, 0x00, 0x00,   // Media source SSRC, 0 for FIR.
            0x00, 0x00, 0x01, 0xC8, 0x05, 0x00, 0x00, 0x00,   // Target 456, seq 5.
            0x00, 0x00, 0x03, 0x15, 0x09, 0x00, 0x00, 0x00,   // Target 789, seq 9.
        };

        var fir = new RTCPFeedback(bytes);

        Assert.Equal(PSFBFeedbackTypesEnum.FIR, fir.Header.PayloadFeedbackMessageType);
        Assert.Equal(0u, fir.MediaSSRC);
        Assert.Equal(new uint[] { 456, 789 }, fir.FeedbackSSRCs);
        Assert.Equal(new byte[] { 5, 9 }, fir.FirSequenceNumbers);
        Assert.Equal(bytes, fir.GetBytes());

        var compound = new RTCPCompoundPacket(bytes.Concat(CreateRemb(1, 456).GetBytes()).ToArray());
        Assert.Equal(2, compound.FeedbackMessages.Count);
        Assert.Equal("REMB", compound.FeedbackMessages[1].UniqueID);
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
