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
