//-----------------------------------------------------------------------------
// Filename: MediaDiagnosticsUnitTest.cs
//
// Description: Tests for the media diagnostics: a stream's SRTP failure counts,
// the RTP end point switch event, the ICE checklist snapshot and the DTLS
// transport's profile names.
//
// Author(s):
// Aaron Clauson
//
// History:
// 17 Nov 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class MediaDiagnosticsUnitTest
    {
        private readonly Microsoft.Extensions.Logging.ILogger logger;

        public MediaDiagnosticsUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        private static AudioStream CreateStream(IPEndPoint destination, bool secure = false)
        {
            var stream = new AudioStream(new RtpSessionConfig
            {
                RtpSecureMediaOption = secure ? RtpSecureMediaOptionEnum.DtlsSrtp : RtpSecureMediaOptionEnum.None
            }, 0);
            stream.LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
            stream.RemoteTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, true,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
            stream.DestinationEndPoint = destination;
            stream.ControlDestinationEndPoint = new IPEndPoint(destination.Address, destination.Port + 1);
            return stream;
        }

        private static byte[] RtpPacket(ushort seq)
        {
            var packet = new RTPPacket(80);
            packet.Header.PayloadType = (int)SDPWellKnownMediaFormatsEnum.PCMU;
            packet.Header.SequenceNumber = seq;
            packet.Header.SyncSource = 1234;
            packet.Header.Timestamp = (uint)(seq * 160);
            return packet.GetBytes();
        }

        private static void Receive(MediaStream stream, IPEndPoint from, ushort seq)
        {
            var buffer = RtpPacket(seq);
            stream.OnReceiveRTPPacket(new RTPHeader(buffer), 0, from, buffer);
        }

        /// <summary>
        /// Each unprotect result counts against its reason: -3 authentication, -4 replay, anything else other.
        /// </summary>
        [Fact]
        public void SrtpErrorCountsByReason()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var counts = new SrtpErrorCounts();
            Assert.Equal(0, counts.Total);
            Assert.Null(counts.LastAt);

            counts.Record(-3);
            counts.Record(-3);
            counts.Record(-4);
            counts.Record(-1);
            counts.Record(-6);

            Assert.Equal(2, counts.Authentication);
            Assert.Equal(1, counts.Replay);
            Assert.Equal(2, counts.Other);
            Assert.Equal(5, counts.Total);
            Assert.NotNull(counts.LastAt);
        }

        /// <summary>
        /// An SRTP packet that fails to unprotect is counted on the stream it arrived on, not handed up.
        /// </summary>
        [Fact]
        public void StreamCountsSrtpUnprotectFailures()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var remote = new IPEndPoint(IPAddress.Loopback, 12000);
            var stream = CreateStream(remote, secure: true);
            int result = -3;
            stream.SetSecurityContext(Pass, (byte[] b, int l, out int o) => { o = result == 0 ? l : 0; return result; }, Pass, Pass);

            int delivered = 0;
            stream.OnRtpPacketReceivedByIndex += (i, ep, m, p) => delivered++;

            Receive(stream, remote, 1);
            Receive(stream, remote, 2);
            result = -4;
            Receive(stream, remote, 3);
            result = 0;
            Receive(stream, remote, 4);

            Assert.Equal(2, stream.SrtpErrors.Authentication);
            Assert.Equal(1, stream.SrtpErrors.Replay);
            Assert.Equal(0, stream.SrtpErrors.Other);
            Assert.Equal(0, stream.SrtcpErrors.Total);
            Assert.Equal(1, delivered);
        }

        private static int Pass(byte[] payload, int length, out int outLength)
        {
            outLength = length;
            return 0;
        }

        /// <summary>
        /// RTP from an address other than the SDP's, accepted (AcceptRtpFromAny), switches the stream
        /// and fires the event with the old and new addresses, after the control end point has moved too.
        /// </summary>
        [Fact]
        public void EndPointSwitchRaisesEvent()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var sdpEndPoint = new IPEndPoint(IPAddress.Parse("192.168.0.50"), 10000);
            var natEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 40000);
            var stream = CreateStream(sdpEndPoint);
            stream.AcceptRtpFromAny = true;

            var switches = new List<(int, SDPMediaTypesEnum, IPEndPoint, IPEndPoint, IPEndPoint)>();
            stream.OnRtpEndPointSwitchedByIndex += (i, m, from, to) => switches.Add((i, m, from, to, stream.ControlDestinationEndPoint));

            Receive(stream, natEndPoint, 1);

            var s = Assert.Single(switches);
            Assert.Equal(0, s.Item1);
            Assert.Equal(SDPMediaTypesEnum.audio, s.Item2);
            Assert.Equal(sdpEndPoint, s.Item3);
            Assert.Equal(natEndPoint, s.Item4);
            Assert.Equal(new IPEndPoint(natEndPoint.Address, natEndPoint.Port + 1), s.Item5);
            Assert.Equal(natEndPoint, stream.DestinationEndPoint);
        }

        /// <summary>
        /// RTP from the expected address, or from an address the stream turns away, fires no switch.
        /// </summary>
        [Fact]
        public void NoEndPointSwitchEventWhenNotSwitched()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var sdpEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 10000);
            int switches = 0;

            var same = CreateStream(sdpEndPoint);
            same.OnRtpEndPointSwitchedByIndex += (i, m, from, to) => switches++;
            Receive(same, sdpEndPoint, 1);

            // A public address in the SDP, RTP from another: not accepted without AcceptRtpFromAny.
            var other = CreateStream(sdpEndPoint);
            other.OnRtpEndPointSwitchedByIndex += (i, m, from, to) => switches++;
            Receive(other, new IPEndPoint(IPAddress.Parse("198.51.100.9"), 20000), 1);

            Assert.Equal(0, switches);
            Assert.Equal(sdpEndPoint, other.DestinationEndPoint);
        }

        /// <summary>
        /// A handler that throws doesn't stop the switch.
        /// </summary>
        [Fact]
        public void EndPointSwitchSurvivesThrowingHandler()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var natEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 40000);
            var stream = CreateStream(new IPEndPoint(IPAddress.Parse("192.168.0.50"), 10000));
            stream.OnRtpEndPointSwitchedByIndex += (i, m, from, to) => throw new System.InvalidOperationException("handler");

            int delivered = 0;
            stream.OnRtpPacketReceivedByIndex += (i, ep, m, p) => delivered++;
            Receive(stream, natEndPoint, 1);

            Assert.Equal(natEndPoint, stream.DestinationEndPoint);
            Assert.Equal(1, delivered);
        }

        /// <summary>
        /// The snapshot copies the entry's counters, which only count up, and says which pair is selected.
        /// </summary>
        [Fact]
        public void CandidatePairStatsCopiesEntry()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var local = new RTCIceCandidate(new RTCIceCandidateInit()) { priority = 200 };
            var remote = new RTCIceCandidate(new RTCIceCandidateInit()) { priority = 100 };
            var entry = new ChecklistEntry(local, remote, isLocalController: true);

            var before = new IceCandidatePairStats(entry, false);
            Assert.Equal(0, before.ChecksSent);
            Assert.Null(before.FirstCheckSentAt);
            Assert.Null(before.RoundTripTime);
            Assert.False(before.Selected);

            entry.FirstCheckSentAt = System.DateTime.Now;
            entry.MarkCheckSent();
            entry.MarkCheckSent();
            entry.BindingRequestsReceived++;
            var after = new IceCandidatePairStats(entry, true);

            Assert.Equal(2, after.ChecksSent);
            Assert.Equal(1, after.RequestsReceived);
            Assert.Equal(0, after.ResponsesReceived);
            Assert.NotNull(after.FirstCheckSentAt);
            Assert.True(after.Selected);
            Assert.Same(local, after.LocalCandidate);
            Assert.Same(remote, after.RemoteCandidate);
            Assert.Equal(entry.Priority, after.Priority);

            // The earlier copy doesn't move.
            Assert.Equal(0, before.ChecksSent);
        }

        /// <summary>
        /// A fresh channel's checklist is empty.
        /// </summary>
        [Fact]
        public void ChecklistSnapshotOfNewChannelIsEmpty()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = new RtpIceChannel();
            try
            {
                Assert.Empty(channel.GetChecklistSnapshot());
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// The SRTP protection profiles are named as RFC 5764 and RFC 7714 name them.
        /// </summary>
        [Theory]
        [InlineData(0x0001, "SRTP_AES128_CM_HMAC_SHA1_80")]
        [InlineData(0x0002, "SRTP_AES128_CM_HMAC_SHA1_32")]
        [InlineData(0x0007, "SRTP_AEAD_AES_128_GCM")]
        [InlineData(0x0008, "SRTP_AEAD_AES_256_GCM")]
        public void ProfileNames(int profile, string name)
        {
            Assert.Equal(name, DtlsSrtpTransport.ProfileName(profile));
        }

        [Fact]
        public void UnknownProfileHasNoName()
        {
            Assert.Null(DtlsSrtpTransport.ProfileName(-1));
            Assert.Null(DtlsSrtpTransport.ProfileName(1234));
        }
    }
}
