//-----------------------------------------------------------------------------
// Filename: RTPSessionNackRetransmissionUnitTest.cs
//
// Description: Unit tests for sending again the RTP packets a generic NACK
// (RFC 4585) reports lost.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests;

[Trait("Category", "unit")]
public class RTPSessionNackRetransmissionUnitTest
{
    private const int VIDEO_PAYLOAD_TYPE = 96;
    private const uint REMOTE_SSRC = 0x11111111;
    private const int FAKE_SRTP_TAG_LENGTH = 10;
    private const byte FAKE_SRTP_TAG = 0xAB;
    private const int PACKETS_SENT = 5;
    private const ushort FIRST_SEQUENCE_NUMBER = 1000;
    private const int RECEIVE_TIMEOUT_MILLISECONDS = 5000;
    private const int NOTHING_RECEIVED_TIMEOUT_MILLISECONDS = 200;
    private static readonly TimeSpan SenderReportAge = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan TruncationTolerance = TimeSpan.FromMilliseconds(1);

    private sealed class Sender : IDisposable
    {
        public RTPSession Session;
        public UdpClient Receiver;
        public int ProtectCalls;
        public readonly List<(IReadOnlyList<ushort> Lost, IReadOnlyList<ushort> NotKept)> Nacks = [];

        public byte[] Receive(int timeoutMilliseconds = RECEIVE_TIMEOUT_MILLISECONDS)
        {
            Receiver.Client.ReceiveTimeout = timeoutMilliseconds;
            var remote = new IPEndPoint(IPAddress.Any, 0);
            return Receiver.Receive(ref remote);
        }

        public void ReceiveRtcp(byte[] rtcp)
        {
            var receiveRtcp = typeof(RTPSession).GetMethod("OnReceiveRTCPPacket", BindingFlags.Instance | BindingFlags.NonPublic);
            receiveRtcp.Invoke(Session, [0, new IPEndPoint(IPAddress.Loopback, 5005), rtcp]);
        }

        public void Dispose()
        {
            Session.Close("test done");
            Session.Dispose();
            Receiver.Dispose();
        }
    }

    private static async Task<Sender> CreateSender(bool remoteSupportsNack)
    {
        var sender = new Sender { Receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)) };
        int port = ((IPEndPoint)sender.Receiver.Client.LocalEndPoint).Port;

        sender.Session = new RTPSession(false, false, false);
        sender.Session.addTrack(new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, VIDEO_PAYLOAD_TYPE), MediaStreamStatusEnum.SendOnly));
        sender.Session.VideoLocalTrack.SeqNum = FIRST_SEQUENCE_NUMBER;
        sender.Session.CreateOffer(IPAddress.Loopback);

        var answer =
            "v=0\r\n" +
            "o=- 1 1 IN IP4 127.0.0.1\r\n" +
            "s=-\r\n" +
            "c=IN IP4 127.0.0.1\r\n" +
            "t=0 0\r\n" +
            $"m=video {port} RTP/AVPF {VIDEO_PAYLOAD_TYPE}\r\n" +
            $"a=rtpmap:{VIDEO_PAYLOAD_TYPE} VP8/90000\r\n" +
            (remoteSupportsNack ? $"a=rtcp-fb:{VIDEO_PAYLOAD_TYPE} nack\r\n" : "") +
            $"a=rtcp-fb:{VIDEO_PAYLOAD_TYPE} nack pli\r\n" +
            "a=recvonly\r\n";
        Assert.Equal(SetDescriptionResultEnum.OK, sender.Session.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(answer)));

        sender.Session.VideoStream.SetSecurityContext(
            (byte[] packet, int length, out int protectedLength) =>
            {
                sender.ProtectCalls++;
                packet.AsSpan(length, FAKE_SRTP_TAG_LENGTH).Fill(FAKE_SRTP_TAG);
                protectedLength = length + FAKE_SRTP_TAG_LENGTH;
                return 0;
            },
            Unchanged, Unchanged, Unchanged);
        sender.Session.VideoStream.OnNackReceived += (lost, notKept) => sender.Nacks.Add((lost, notKept));

        await sender.Session.Start();
        return sender;
    }

    private static int Unchanged(byte[] packet, int length, out int outputLength)
    {
        outputLength = length;
        return 0;
    }

    private static ushort SequenceNumber(byte[] rtp) => BinaryPrimitives.ReadUInt16BigEndian(rtp.AsSpan(2));

    private static byte[] Nack(uint mediaSsrc, params (ushort Pid, ushort Blp)[] entries)
    {
        var nack = new byte[12 + 4 * entries.Length];
        nack[0] = 0x80 | (byte)RTCPFeedbackTypesEnum.NACK;
        nack[1] = (byte)RTCPReportTypesEnum.RTPFB;
        BinaryPrimitives.WriteUInt16BigEndian(nack.AsSpan(2), (ushort)(nack.Length / 4 - 1));
        BinaryPrimitives.WriteUInt32BigEndian(nack.AsSpan(4), REMOTE_SSRC);
        BinaryPrimitives.WriteUInt32BigEndian(nack.AsSpan(8), mediaSsrc);
        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(nack.AsSpan(12 + 4 * i), entries[i].Pid);
            BinaryPrimitives.WriteUInt16BigEndian(nack.AsSpan(14 + 4 * i), entries[i].Blp);
        }
        return nack;
    }

    private static Dictionary<ushort, byte[]> SendPackets(Sender sender)
    {
        Dictionary<ushort, byte[]> sent = [];
        for (int i = 0; i < PACKETS_SENT; i++)
        {
            sender.Session.SendRtpRaw(SDPMediaTypesEnum.video, [(byte)i, 1, 2, 3], (uint)i, 1, VIDEO_PAYLOAD_TYPE);
            var packet = sender.Receive();
            sent.Add(SequenceNumber(packet), packet);
        }
        return sent;
    }

    /// <summary>
    /// The packets every entry of a NACK reports lost are sent again exactly as they went the first time,
    /// SRTP tag included, without protecting them again.
    /// </summary>
    [Fact]
    public async Task RetransmitsTheProtectedPacketsANackReports()
    {
        using var sender = await CreateSender(true);
        var sent = SendPackets(sender);
        ushort first = sent.Keys.Min();
        var lost = new[] { (ushort)(first + 1), (ushort)(first + 3), (ushort)(first + 4) };

        sender.ReceiveRtcp(Nack(sender.Session.VideoLocalTrack.Ssrc, (lost[0], 0b10), (lost[2], 0)));

        for (int i = 0; i < lost.Length; i++)
        {
            var retransmitted = sender.Receive();
            Assert.Equal(sent[lost[i]], retransmitted);
            Assert.Equal(FAKE_SRTP_TAG, retransmitted[retransmitted.Length - 1]);
        }
        Assert.Equal(PACKETS_SENT, sender.ProtectCalls);
        var nack = Assert.Single(sender.Nacks);
        Assert.Equal(lost, nack.Lost);
        Assert.Empty(nack.NotKept);
        var metrics = sender.Session.VideoStream.RetransmissionMetrics;
        Assert.Equal(1, metrics.NacksReceived);
        Assert.Equal(lost.Length, metrics.RetransmissionsRequested);
        Assert.Equal(lost.Length, metrics.PacketsRetransmitted);
        Assert.Equal(lost.Sum(seq => sent[seq].Length), metrics.WireBytesRetransmitted);
        Assert.Equal((uint)(PACKETS_SENT + lost.Length), sender.Session.VideoStream.RtcpSession.PacketsSentCount);
    }

    /// <summary>
    /// A NACK is acted on even when the compound packet carrying it also says goodbye.
    /// </summary>
    [Fact]
    public async Task RetransmitsForANackSentWithABye()
    {
        using var sender = await CreateSender(true);
        var sent = SendPackets(sender);
        ushort first = sent.Keys.Min();

        sender.ReceiveRtcp([.. Nack(sender.Session.VideoLocalTrack.Ssrc, (first, 0)), .. new RTCPBye(REMOTE_SSRC, null).GetBytes()]);

        Assert.Equal(sent[first], sender.Receive());
    }

    /// <summary>
    /// A packet asked for again before its retransmission could have arrived is not sent a second time, and one
    /// that was never sent is reported as not kept.
    /// </summary>
    [Fact]
    public async Task SuppressesRepeatedRequestsAndReportsPacketsNotKept()
    {
        using var sender = await CreateSender(true);
        var sent = SendPackets(sender);
        ushort first = sent.Keys.Min();
        ushort neverSent = (ushort)(first + PACKETS_SENT);

        sender.ReceiveRtcp(Nack(sender.Session.VideoLocalTrack.Ssrc, (first, 0), (first, 0), (neverSent, 0)));

        Assert.Equal(sent[first], sender.Receive());
        Assert.Throws<SocketException>(() => sender.Receive(NOTHING_RECEIVED_TIMEOUT_MILLISECONDS));
        Assert.Equal(new[] { neverSent }, Assert.Single(sender.Nacks).NotKept);
        var metrics = sender.Session.VideoStream.RetransmissionMetrics;
        Assert.Equal(3, metrics.RetransmissionsRequested);
        Assert.Equal(1, metrics.PacketsRetransmitted);
        Assert.Equal(1, metrics.RetransmissionsSuppressed);
        Assert.Equal(1, metrics.PacketsNotKept);
    }

    /// <summary>
    /// Without generic NACK in the answer no packets are kept, so every one a NACK asks for is reported as not kept.
    /// </summary>
    [Fact]
    public async Task KeepsNoPacketsWhenNackIsNotNegotiated()
    {
        using var sender = await CreateSender(false);
        var sent = SendPackets(sender);
        ushort first = sent.Keys.Min();

        sender.ReceiveRtcp(Nack(sender.Session.VideoLocalTrack.Ssrc, (first, 0b1)));

        Assert.Throws<SocketException>(() => sender.Receive(NOTHING_RECEIVED_TIMEOUT_MILLISECONDS));
        Assert.Equal(new[] { first, (ushort)(first + 1) }, Assert.Single(sender.Nacks).NotKept);
        Assert.Equal(0, sender.Session.VideoStream.RetransmissionMetrics.PacketsRetransmitted);
    }

    /// <summary>
    /// Packets protected with SRTP keys that have since been replaced are not sent again.
    /// </summary>
    [Fact]
    public async Task ForgetsPacketsProtectedWithReplacedKeys()
    {
        using var sender = await CreateSender(true);
        var sent = SendPackets(sender);
        ushort first = sent.Keys.Min();
        sender.Session.VideoStream.SetSecurityContext(Unchanged, Unchanged, Unchanged, Unchanged);

        sender.ReceiveRtcp(Nack(sender.Session.VideoLocalTrack.Ssrc, (first, 0)));

        Assert.Throws<SocketException>(() => sender.Receive(NOTHING_RECEIVED_TIMEOUT_MILLISECONDS));
        Assert.Equal(new[] { first }, Assert.Single(sender.Nacks).NotKept);
    }

    /// <summary>
    /// Packets sent from a track that has since been replaced are not sent again, even for the new track's SSRC.
    /// </summary>
    [Fact]
    public async Task ForgetsPacketsSentFromAReplacedTrack()
    {
        using var sender = await CreateSender(true);
        var sent = SendPackets(sender);
        ushort first = sent.Keys.Min();
        sender.Session.VideoStream.LocalTrack = new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, VIDEO_PAYLOAD_TYPE), MediaStreamStatusEnum.SendOnly);

        sender.ReceiveRtcp(Nack(sender.Session.VideoLocalTrack.Ssrc, (first, 0)));

        Assert.Throws<SocketException>(() => sender.Receive(NOTHING_RECEIVED_TIMEOUT_MILLISECONDS));
        Assert.Equal(new[] { first }, Assert.Single(sender.Nacks).NotKept);
    }

    /// <summary>
    /// Packets sent to a previous destination are not sent again to a new one.
    /// </summary>
    [Fact]
    public async Task ForgetsPacketsSentToAPreviousDestination()
    {
        using var sender = await CreateSender(true);
        var sent = SendPackets(sender);
        ushort first = sent.Keys.Min();
        var newDestination = new IPEndPoint(IPAddress.Loopback, 5006);
        sender.Session.SetDestination(SDPMediaTypesEnum.video, newDestination, newDestination);

        sender.ReceiveRtcp(Nack(sender.Session.VideoLocalTrack.Ssrc, (first, 0)));

        Assert.Equal(new[] { first }, Assert.Single(sender.Nacks).NotKept);
    }

    /// <summary>
    /// The round trip time that limits resends comes from the report block about the stream wherever the remote
    /// party puts it: here in a receiver report following a sender report with no blocks (RFC 3550 section
    /// 6.4.2), both from an SSRC that matches no stream, as a receive-only browser sends them.
    /// </summary>
    [Fact]
    public async Task MeasuresRoundTripTimeFromTheReportBlockAboutTheStream()
    {
        using var sender = await CreateSender(true);
        var start = DateTime.Now;
        uint lastSenderReport = RTCPSession.DateTimeToNtpTimestamp32(start - SenderReportAge);
        var reportBlock = new ReceptionReportSample(sender.Session.VideoLocalTrack.Ssrc, 0, 0, 0, 0, lastSenderReport, 0);

        sender.ReceiveRtcp([
            .. new RTCPSenderReport(REMOTE_SSRC, 0, 0, 0, 0, null).GetBytes(),
            .. new RTCPReceiverReport(REMOTE_SSRC, [reportBlock]).GetBytes()]);

        var elapsed = DateTime.Now - start;
        Assert.InRange(sender.Session.VideoStream.RtcpSession.RoundTripTime, SenderReportAge - TruncationTolerance, SenderReportAge + elapsed + TruncationTolerance);
    }

    /// <summary>
    /// Once the stream may no longer send, NACKs are ignored rather than sending kept packets again.
    /// </summary>
    [Fact]
    public async Task IgnoresNacksOnceSendingStops()
    {
        using var sender = await CreateSender(true);
        var sent = SendPackets(sender);
        sender.Session.VideoLocalTrack.StreamStatus = MediaStreamStatusEnum.RecvOnly;

        sender.ReceiveRtcp(Nack(sender.Session.VideoLocalTrack.Ssrc, (sent.Keys.Min(), 0)));

        Assert.Throws<SocketException>(() => sender.Receive(NOTHING_RECEIVED_TIMEOUT_MILLISECONDS));
        Assert.Empty(sender.Nacks);
    }
}
