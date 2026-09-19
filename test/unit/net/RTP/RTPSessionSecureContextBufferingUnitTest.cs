//-----------------------------------------------------------------------------
// Filename: RTPSessionSecureContextBufferingUnitTest.cs
//
// Description: Unit tests for the buffering of RTP and RTCP packets that arrive
// before the SRTP context is ready. A remote peer starts sending as soon as its
// own DTLS handshake completes which can precede this end finishing its SRTP set
// up. Those packets used to be logged and thrown away which, for a receiver,
// very often lost part or all of the opening key frame.
//
// History:
// 18 Sep 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionSecureContextBufferingUnitTest
    {
        private const int DUMMY_LOCAL_PORT = 10000;

        private readonly ILogger logger;

        public RTPSessionSecureContextBufferingUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Test harness that exposes the receive entry point and the secure context set up,
        /// both of which are called by the transport and DTLS layers in a live session.
        /// </summary>
        private class TestRTPSession : RTPSession
        {
            public TestRTPSession(RtpSessionConfig config) : base(config)
            { }

            public void Receive(byte[] buffer)
            {
                OnReceive(DUMMY_LOCAL_PORT, new IPEndPoint(IPAddress.Loopback, 12000), buffer);
            }

            /// <summary>
            /// Stands in for the DTLS handshake completing. The protect/unprotect delegates
            /// are pass throughs so the test packets are left untouched.
            /// </summary>
            public void CompleteSecureContext()
            {
                SetGlobalSecurityContext(PassThrough, PassThrough, PassThrough, PassThrough);
            }

            private static int PassThrough(byte[] payload, int length, out int outputBufferLength)
            {
                outputBufferLength = length;
                return 0;
            }
        }

        private static TestRTPSession CreateSecureSession()
        {
            var session = new TestRTPSession(new RtpSessionConfig
            {
                RtpSecureMediaOption = RtpSecureMediaOptionEnum.DtlsSrtp
            });

            session.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }));

            return session;
        }

        /// <summary>
        /// Builds a minimal PCMU RTP packet. The payload ID is what the session uses to
        /// match the packet to the audio stream.
        /// </summary>
        private static byte[] CreateRtpPacket(ushort seqNum)
        {
            var packet = new RTPPacket(80);
            packet.Header.PayloadType = (int)SDPWellKnownMediaFormatsEnum.PCMU;
            packet.Header.SequenceNumber = seqNum;
            packet.Header.SyncSource = 1234;
            packet.Header.Timestamp = (uint)(seqNum * 160);
            return packet.GetBytes();
        }

        /// <summary>
        /// The core of the fix. Packets that arrive while the secure context is still being
        /// set up must be held and then delivered, in arrival order, once it is ready.
        /// </summary>
        [Fact]
        public void PacketsBeforeSecureContextReady_AreDeliveredOnceContextReady()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = CreateSecureSession())
            {
                var received = new List<ushort>();
                session.OnRtpPacketReceived += (ep, mediaType, pkt) => received.Add(pkt.Header.SequenceNumber);

                session.Receive(CreateRtpPacket(1));
                session.Receive(CreateRtpPacket(2));
                session.Receive(CreateRtpPacket(3));

                Assert.Empty(received);

                session.CompleteSecureContext();

                Assert.Equal(new ushort[] { 1, 2, 3 }, received);
            }
        }

        /// <summary>
        /// Once the context is ready packets must flow straight through, and the drain must
        /// not replay anything a second time.
        /// </summary>
        [Fact]
        public void PacketsAfterSecureContextReady_AreDeliveredOnce()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = CreateSecureSession())
            {
                var received = new List<ushort>();
                session.OnRtpPacketReceived += (ep, mediaType, pkt) => received.Add(pkt.Header.SequenceNumber);

                session.Receive(CreateRtpPacket(1));
                session.CompleteSecureContext();
                session.Receive(CreateRtpPacket(2));
                session.Receive(CreateRtpPacket(3));

                Assert.Equal(new ushort[] { 1, 2, 3 }, received);
            }
        }

        /// <summary>
        /// A peer that never completes its handshake must not be able to grow the queue. The
        /// bound is on packet count and the oldest are the ones discarded.
        /// </summary>
        [Fact]
        public void PendingPackets_AreBoundedByCount()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = CreateSecureSession())
            {
                var received = new List<ushort>();
                session.OnRtpPacketReceived += (ep, mediaType, pkt) => received.Add(pkt.Header.SequenceNumber);

                int sendCount = RTPSession.PENDING_SECURE_PACKETS_MAX_COUNT + 10;

                for (ushort seqNum = 1; seqNum <= sendCount; seqNum++)
                {
                    session.Receive(CreateRtpPacket(seqNum));
                }

                session.CompleteSecureContext();

                Assert.Equal(RTPSession.PENDING_SECURE_PACKETS_MAX_COUNT, received.Count);
                Assert.Equal((ushort)(sendCount - RTPSession.PENDING_SECURE_PACKETS_MAX_COUNT + 1), received[0]);
                Assert.Equal((ushort)sendCount, received[received.Count - 1]);
            }
        }

        /// <summary>
        /// A session with no security has no window to buffer for. Packets must be delivered
        /// as they arrive.
        /// </summary>
        [Fact]
        public void NonSecureSession_DeliversPacketsImmediately()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var session = new TestRTPSession(new RtpSessionConfig
            {
                RtpSecureMediaOption = RtpSecureMediaOptionEnum.None
            });

            session.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }));

            using (session)
            {
                var received = new List<ushort>();
                session.OnRtpPacketReceived += (ep, mediaType, pkt) => received.Add(pkt.Header.SequenceNumber);

                session.Receive(CreateRtpPacket(1));

                Assert.Single(received);
            }
        }

        /// <summary>
        /// Closing the session must not replay buffered packets to an application that has
        /// already torn down.
        /// </summary>
        [Fact]
        public void PendingPackets_AreDiscardedOnClose()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = CreateSecureSession())
            {
                var received = new List<ushort>();
                session.OnRtpPacketReceived += (ep, mediaType, pkt) => received.Add(pkt.Header.SequenceNumber);

                session.Receive(CreateRtpPacket(1));
                session.Close("normal");
                session.CompleteSecureContext();

                Assert.Empty(received);
            }
        }
    }
}
