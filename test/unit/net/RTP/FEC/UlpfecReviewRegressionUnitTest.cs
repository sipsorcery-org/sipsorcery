using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class UlpfecReviewRegressionUnitTest
    {
        private const int MediaPt = 96;
        private const int RedPt = 122;
        private const int FecPt = 127;
        private const uint Ssrc = 0x1234;

        [Theory]
        [InlineData(SDPMediaTypesEnum.audio, false)]
        [InlineData(SDPMediaTypesEnum.audio, true)]
        [InlineData(SDPMediaTypesEnum.text, false)]
        [InlineData(SDPMediaTypesEnum.text, true)]
        public void NonVideoRedIsDeliveredUnchanged(SDPMediaTypesEnum kind, bool videoFec)
        {
            using var session = new RTPSession(new RtpSessionConfig { IsMediaMultiplexed = true });
            int clockRate = kind == SDPMediaTypesEnum.audio ? 48000 : 1000;
            var formats = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(kind, 98, kind == SDPMediaTypesEnum.audio ? "opus" : "t140", clockRate),
                new SDPAudioVideoMediaFormat(kind, RedPt, "red", clockRate, fmtp: "98/98/98")
            };
            session.addTrack(new MediaStreamTrack(kind, false, formats));
            session.addTrack(new MediaStreamTrack(kind, true, formats));
            MediaStream stream = kind == SDPMediaTypesEnum.audio ? (MediaStream)session.AudioStream : session.TextStream;
            var received = new List<RTPPacket>();
            stream.OnRtpPacketReceivedByIndex += (index, endpoint, mediaType, packet) =>
            {
                Assert.Equal(kind, mediaType);
                received.Add(packet);
            };

            var videoFrames = new List<byte[]>();
            if (videoFec)
            {
                session.addTrack(new MediaStreamTrack(SDPMediaTypesEnum.video, false,
                    new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, MediaPt, "VP8", 90000) })
                {
                    Redundancy = RtpRedundancyOptions.Red | RtpRedundancyOptions.Ulpfec
                });
                var clock = new FecTestClock();
                session.VideoStream.SetFecReceiveTiming(() => clock.Now, clock.CreateTimer);
                session.VideoStream.EnableUlpfec(RedPt, FecPt, MediaPt);
                session.VideoStream.OnVideoFrameReceivedByIndex += (index, endpoint, timestamp, frame, format) => videoFrames.Add(frame);
            }

            byte[] red = RtpRedPayload.Encode(new[]
            {
                new RtpRedBlock(98, 600, new byte[] { 1, 2 }),
                new RtpRedBlock(98, 300, new byte[] { 3, 4 })
            }, new RtpRedBlock(98, new byte[] { 5, 6 }));
            var wire = CreateMedia(500, 9000, red, true);
            wire.Header.PayloadType = RedPt;
            wire.Header.CSRCCount = 1;
            wire.Header.CSRCList = new[] { 17 };
            wire.Header.HeaderExtensionFlag = 1;
            wire.Header.ExtensionProfile = RTPHeader.ONE_BYTE_EXTENSION_PROFILE;
            wire.Header.ExtensionLength = 1;
            wire.Header.ExtensionPayload = new byte[] { 0x10, 0xEF, 0, 0 };
            Receive(stream, wire);

            var delivered = Assert.Single(received);
            Assert.Equal(wire.GetBytes(), delivered.GetBytes());
            Assert.Equal(red, delivered.GetPayloadBytes());
            Assert.Equal(1u, stream.RtcpSession.PacketsReceivedCount);
            Assert.Equal((uint)red.Length, stream.RtcpSession.OctetsReceivedCount);
            Assert.Equal(Ssrc, Assert.Single(stream.RtcpSession.GetRtcpReport().ReceiverReport.ReceptionReports).SSRC);
            if (videoFec)
            {
                foreach (var packet in CreateWire(Vp8Payloads(), 100, 3000).Where(packet => packet.MediaIndex != 1))
                {
                    Receive(session.VideoStream, packet.Packet);
                }
                Assert.Equal(new byte[] { 1, 1, 2, 2, 3, 3 }, Assert.Single(videoFrames));
                Assert.Equal(1, session.VideoStream.FecMetrics.PacketsRecovered);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NewSsrcContinuesVideoAndRecoveryWithoutBye(bool redPrimary)
        {
            using var receiver = new Receiver();
            receiver.Stream.RemoteTrack.Ssrc = Ssrc;
            var firstSource = CreateMedia(500, 3000, new byte[] { 0x10, 7, 8 }, true);
            receiver.Receive(firstSource);
            var encoder = receiver.Stream.FecEncoder;
            var config = receiver.Stream.FecConfig;
            const uint nextSsrc = 0x5678;
            var switched = CreateWire(Vp8Payloads(), 500, 3000, nextSsrc).Where(packet => packet.MediaIndex != 1).ToList();
            if (!redPrimary)
            {
                foreach (var packet in switched.Where(packet => packet.MediaIndex >= 0))
                {
                    RtpRedPayload.TryDecode(packet.Packet.GetPayloadBytes(), out _, out var primary);
                    packet.Packet = CreateMedia(packet.Packet.Header.SequenceNumber, 3000, primary.Data,
                        packet.Packet.Header.MarkerBit == 1, nextSsrc);
                }
            }
            foreach (var packet in switched) receiver.Receive(packet.Packet);

            Assert.Equal(2, receiver.Frames.Count);
            Assert.Equal(new byte[] { 1, 1, 2, 2, 3, 3 }, receiver.Frames[1]);
            Assert.Equal(new ushort[] { 500, 501, 502 }, receiver.Media.Skip(1).Select(packet => packet.Header.SequenceNumber));
            Assert.All(receiver.Media.Skip(1), packet => Assert.Equal(nextSsrc, packet.Header.SyncSource));
            Assert.Equal(nextSsrc, receiver.Stream.RemoteTrack.Ssrc);
            Assert.Equal((ushort)502, receiver.Stream.RemoteTrack.LastRemoteSeqNum);
            Assert.Equal(nextSsrc, Assert.Single(receiver.Stream.RtcpSession.GetRtcpReport().ReceiverReport.ReceptionReports).SSRC);
            Assert.Equal(4u, receiver.Stream.RtcpSession.PacketsReceivedCount);
            Assert.Equal((uint)(firstSource.GetPayloadLength() + switched.Sum(packet => (long)packet.Packet.GetPayloadLength())),
                receiver.Stream.RtcpSession.OctetsReceivedCount);
            Assert.Equal(1, receiver.Stream.FecMetrics.PacketsRecovered);
            Assert.Same(encoder, receiver.Stream.FecEncoder);
            Assert.Same(config, receiver.Stream.FecConfig);

            // Returning to an earlier SSRC also clears its old sequence and timestamp history.
            receiver.Receive(CreateMedia(7, 3000, new byte[] { 0x10, 9, 10 }, true));
            Assert.Equal(new byte[] { 9, 10 }, receiver.Frames[2]);
            Assert.Equal(Ssrc, receiver.Stream.RemoteTrack.Ssrc);
            Assert.Equal(Ssrc, Assert.Single(receiver.Stream.RtcpSession.GetRtcpReport().ReceiverReport.ReceptionReports).SSRC);
            Assert.Equal(5u, receiver.Stream.RtcpSession.PacketsReceivedCount);
            Assert.Equal(1, receiver.Stream.FecMetrics.PacketsRecovered);
        }

        [Fact]
        public void ForeignRepairAndMalformedRedDoNotSwitchSource()
        {
            using var receiver = new Receiver();
            receiver.Stream.RemoteTrack.Ssrc = Ssrc;
            var active = CreateWire(Vp8Payloads(), 100, 3000);
            receiver.Receive(active.Single(packet => packet.MediaIndex == 0).Packet);
            receiver.Receive(active.Single(packet => packet.MediaIndex == 2).Packet);
            const uint foreignSsrc = 0x5678;
            receiver.Receive(CreateWire(Vp8Payloads(), 100, 3000, foreignSsrc).Single(packet => packet.MediaIndex < 0).Packet);
            foreach (byte[] payload in new[]
            {
                new byte[] { 0x80 }, // Truncated RED header.
                RtpRedPayload.EncodeSingleBlock(new byte[] { 0x10, 99 }, 99), // Unnegotiated inner PT.
                RtpRedPayload.EncodeSingleBlock(Array.Empty<byte>(), MediaPt)
            })
            {
                var invalid = CreateMedia(100, 3000, payload, true, foreignSsrc);
                invalid.Header.PayloadType = RedPt;
                receiver.Receive(invalid);
            }
            Assert.Empty(receiver.Frames);
            Assert.Equal(2u, receiver.Stream.RtcpSession.PacketsReceivedCount);
            Assert.Equal(Ssrc, receiver.Stream.RemoteTrack.Ssrc);
            Assert.Equal(Ssrc, Assert.Single(receiver.Stream.RtcpSession.GetRtcpReport().ReceiverReport.ReceptionReports).SSRC);
            Assert.Single(receiver.Clock.Timers);
            Assert.False(receiver.Clock.Timers[0].Disposed);

            // The active source's pending group survives every rejected handover.
            receiver.Receive(active.Single(packet => packet.MediaIndex < 0).Packet);
            Assert.Equal(new byte[] { 1, 1, 2, 2, 3, 3 }, Assert.Single(receiver.Frames));
            Assert.All(receiver.Media, packet => Assert.Equal(Ssrc, packet.Header.SyncSource));
            Assert.Equal(1, receiver.Stream.FecMetrics.PacketsRecovered);
            Assert.Equal(3u, receiver.Stream.RtcpSession.PacketsReceivedCount);
        }

        [Fact]
        public void SsrcHandoverCancelsOldDeadline()
        {
            using var receiver = new Receiver();
            var active = CreateWire(Vp8Payloads(), 100, 3000);
            receiver.Receive(active.Single(packet => packet.MediaIndex == 0).Packet);
            receiver.Receive(active.Single(packet => packet.MediaIndex == 2).Packet);
            var oldTimer = Assert.Single(receiver.Clock.Timers);
            receiver.Clock.Advance(10);
            var next = CreateWire(Vp8Payloads(), 100, 3000, 0x5678);
            receiver.Receive(next.Single(packet => packet.MediaIndex == 0).Packet);
            receiver.Receive(next.Single(packet => packet.MediaIndex == 2).Packet);
            Assert.True(oldTimer.Disposed);
            Assert.Equal(2, receiver.Clock.Timers.Count);
            receiver.Clock.Advance(10);
            oldTimer.Callback(); // A callback already queued before disposal.
            Assert.Empty(receiver.Frames);
            Assert.Equal(1, receiver.Clock.PendingCount);
            receiver.Receive(next.Single(packet => packet.MediaIndex < 0).Packet);
            receiver.Clock.Advance(100);

            Assert.Equal(new byte[] { 1, 1, 2, 2, 3, 3 }, Assert.Single(receiver.Frames));
            Assert.All(receiver.Media, packet => Assert.Equal(0x5678u, packet.Header.SyncSource));
            Assert.Equal(5u, receiver.Stream.RtcpSession.PacketsReceivedCount);
            Assert.Equal(1, receiver.Stream.FecMetrics.PacketsRecovered);
            Assert.Equal(0, receiver.Clock.PendingCount);
        }

        [Fact]
        public void FailedUnprotectCannotSwitchSource()
        {
            using var receiver = new Receiver(secure: true);
            receiver.Stream.RemoteTrack.Ssrc = Ssrc;
            bool authenticated = true;
            int Unprotect(byte[] buffer, int length, out int resultLength)
            {
                resultLength = length;
                return authenticated ? 0 : -1;
            }
            receiver.Stream.SetSecurityContext(Unprotect, Unprotect, Unprotect, Unprotect);
            receiver.Receive(CreateMedia(500, 3000, new byte[] { 0x10, 1, 2 }, true));
            authenticated = false;
            receiver.Receive(CreateMedia(500, 3000, new byte[] { 0x10, 3, 4 }, true, 0x5678));
            Assert.Equal(new byte[] { 1, 2 }, Assert.Single(receiver.Frames));
            Assert.Single(receiver.Clock.Timers);
            Assert.Equal(Ssrc, receiver.Stream.RemoteTrack.Ssrc);
            Assert.Equal(1u, receiver.Stream.RtcpSession.PacketsReceivedCount);
            authenticated = true;
            receiver.Receive(CreateMedia(501, 6000, new byte[] { 0x10, 5, 6 }, true));
            Assert.Equal(new byte[] { 5, 6 }, receiver.Frames[1]);
            Assert.Equal(2u, receiver.Stream.RtcpSession.PacketsReceivedCount);
        }

        [Fact]
        public void LosslessStreamRecoversNothing()
        {
            var encoder = new RtpUlpfecEncoder(RedPt, FecPt, MediaPt);
            var decoder = new RtpUlpfecDecoder(RedPt, FecPt);
            var recovered = new List<RTPPacket>();
            ushort sequence = 0;

            for (int i = 0; i < 1000; i++)
            {
                var media = CreateMedia(sequence++, (uint)(i / 10 * 3000),
                    new byte[] { 0x10, (byte)i, 0xAA }, i % 10 == 9);
                encoder.AddMediaPacket(media, () => sequence++, out var repairs);
                decoder.AddReceivedMedia(new RTPPacket(media.GetBytes()), out var fromMedia);
                recovered.AddRange(fromMedia);
                foreach (var repair in repairs ?? new List<RTPPacket>())
                {
                    decoder.AddReceivedFec(new RTPPacket(repair.GetBytes()), out var fromRepair);
                    recovered.AddRange(fromRepair);
                }
            }

            Assert.Empty(recovered);
        }

        [Fact]
        public void Vp8FrameWithLostMiddlePacketIsDeliveredComplete()
        {
            AssertVp8Recovery(droppedIndex: 1, initialSequence: 500);
        }

        [Theory]
        [InlineData(0, 500)]
        [InlineData(2, 500)]
        [InlineData(1, 65534)]
        public void Vp8RecoveryRestoresFirstAndFinalPackets(int droppedIndex, int initialSequence)
        {
            AssertVp8Recovery(droppedIndex, (ushort)initialSequence);
        }

        private static void AssertVp8Recovery(int droppedIndex, ushort initialSequence)
        {
            using (var receiver = new Receiver())
            {
                var wire = CreateWire(Vp8Payloads(), initialSequence, 9000);
                foreach (var packet in wire.Where(x => x.MediaIndex != droppedIndex))
                {
                    receiver.Receive(packet.Packet);
                }

                Assert.Equal(new byte[] { 1, 1, 2, 2, 3, 3 }, Assert.Single(receiver.Frames));
                Assert.Equal(1, receiver.Stream.FecMetrics.PacketsRecovered);
                Assert.Equal(new[] { initialSequence, (ushort)(initialSequence + 1), (ushort)(initialSequence + 2) },
                    receiver.Media.Select(x => x.Header.SequenceNumber));
            }
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(0, false)]
        [InlineData(4, false)]
        [InlineData(10, false)]
        [InlineData(4, true)]
        public void H264FragmentsAcrossRepairGroupsDeliverOneCompleteNal(int droppedIndex, bool repairsFirst)
        {
            using (var receiver = new Receiver(codec: "H264"))
            {
                var payloads = Enumerable.Range(0, 11)
                    .Select(i => new byte[] { 0x7C, (byte)(i == 0 ? 0x85 : i == 10 ? 0x45 : 0x05), (byte)(i + 1), (byte)(i + 21) })
                    .ToArray();
                var wire = CreateWire(payloads, 100, 3000);
                var arrivals = wire.Where(x => x.MediaIndex < 0 || x.MediaIndex != droppedIndex);
                if (repairsFirst)
                {
                    arrivals = arrivals.OrderBy(x => x.MediaIndex < 0 ? 0 : 1);
                }
                foreach (var packet in arrivals)
                {
                    receiver.Receive(packet.Packet);
                }

                var expected = new List<byte> { 0, 0, 0, 1, 0x65 };
                for (int i = 0; i < 11; i++)
                {
                    expected.Add((byte)(i + 1));
                    expected.Add((byte)(i + 21));
                }

                Assert.Equal(expected.ToArray(), Assert.Single(receiver.Frames));
                Assert.Equal(wire.Where(x => x.MediaIndex >= 0).Select(x => x.Packet.Header.SequenceNumber),
                    receiver.Media.Select(x => x.Header.SequenceNumber));
                // With repairs first, both later groups reconstruct their final member before
                // its original arrives, in addition to the dropped first-group member.
                Assert.Equal(repairsFirst ? 3 : droppedIndex < 0 ? 0 : 1, receiver.Stream.FecMetrics.PacketsRecovered);
            }
        }

        [Fact]
        public void RecoveredMediaDoesNotCountAsAnotherRtcpArrival()
        {
            using (var receiver = new Receiver())
            {
                var receivedWire = CreateWire(Vp8Payloads(), 500, 9000)
                    .Where(x => x.MediaIndex != 1).ToList();
                foreach (var packet in receivedWire)
                {
                    receiver.Receive(packet.Packet);
                }

                Assert.Equal(3u, receiver.Stream.RtcpSession.PacketsReceivedCount);
                Assert.Equal((uint)receivedWire.Sum(x => (long)x.Packet.GetPayloadLength()), receiver.Stream.RtcpSession.OctetsReceivedCount);
                Assert.Equal(1, receiver.Stream.FecMetrics.PacketsRecovered);
                Assert.Equal(3, receiver.Media.Count);
                Assert.Equal(new byte[] { 1, 1, 2, 2, 3, 3 }, Assert.Single(receiver.Frames));
            }
        }

        [Fact]
        public void LateOriginalsAndRepeatedRepairsDoNotDeliverMediaTwice()
        {
            using (var receiver = new Receiver())
            {
                var wire = CreateWire(Vp8Payloads(), 500, 9000);
                foreach (var packet in wire.Where(x => x.MediaIndex != 1))
                {
                    receiver.Receive(packet.Packet);
                }

                receiver.Receive(wire.Single(x => x.MediaIndex == 1).Packet);
                receiver.Receive(wire.Single(x => x.MediaIndex < 0).Packet);
                receiver.Receive(wire.Single(x => x.MediaIndex == 0).Packet);

                Assert.Equal(new ushort[] { 500, 501, 502 }, receiver.Media.Select(x => x.Header.SequenceNumber));
                Assert.Equal(new byte[] { 1, 1, 2, 2, 3, 3 }, Assert.Single(receiver.Frames));
                Assert.Equal(1, receiver.Stream.FecMetrics.PacketsRecovered);
                Assert.Equal(6u, receiver.Stream.RtcpSession.PacketsReceivedCount);
            }
        }

        [Fact]
        public void RedHeaderCopyPreservesMetadataAndOwnsItsArrays()
        {
            var original = new RTPHeader
            {
                Version = 2,
                PaddingFlag = 1,
                HeaderExtensionFlag = 1,
                CSRCCount = 2,
                MarkerBit = 1,
                PayloadType = RedPt,
                SequenceNumber = 65535,
                Timestamp = 90000,
                SyncSource = Ssrc,
                CSRCList = new[] { 17, 29 },
                ExtensionProfile = RTPHeader.ONE_BYTE_EXTENSION_PROFILE,
                ExtensionLength = 1,
                ExtensionPayload = new byte[] { 0x10, 0xEF, 0, 0 },
                PayloadSize = 20,
                PaddingCount = 4,
                ReceivedTime = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc)
            };

            var copy = MediaStream.CloneRtpHeader(original);
            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Version);
            Assert.Equal(1, copy.PaddingFlag);
            Assert.Equal(1, copy.HeaderExtensionFlag);
            Assert.Equal(2, copy.CSRCCount);
            Assert.Equal(1, copy.MarkerBit);
            Assert.Equal(RedPt, copy.PayloadType);
            Assert.Equal(65535, copy.SequenceNumber);
            Assert.Equal(90000u, copy.Timestamp);
            Assert.Equal(Ssrc, copy.SyncSource);
            Assert.Equal(new[] { 17, 29 }, copy.CSRCList);
            Assert.Equal(RTPHeader.ONE_BYTE_EXTENSION_PROFILE, copy.ExtensionProfile);
            Assert.Equal(1, copy.ExtensionLength);
            Assert.Equal(new byte[] { 0x10, 0xEF, 0, 0 }, copy.ExtensionPayload);
            Assert.Equal(20, copy.PayloadSize);
            Assert.Equal(4, copy.PaddingCount);
            Assert.Equal(original.ReceivedTime, copy.ReceivedTime);

            copy.PayloadType = FecPt;
            copy.CSRCList[0] = 999;
            copy.ExtensionPayload[1] = 0;
            Assert.Equal(RedPt, original.PayloadType);
            Assert.Equal(new[] { 17, 29 }, original.CSRCList);
            Assert.Equal(new byte[] { 0x10, 0xEF, 0, 0 }, original.ExtensionPayload);
        }

        [Fact]
        public void ReceiveRepairWaitDefaultsToTwentyMillisecondsAndClonePreservesOverride()
        {
            var config = new RtpUlpfecConfig();
            Assert.Equal(TimeSpan.FromMilliseconds(20), config.MaxReceiveRepairWait);
            config.MaxReceiveRepairWait = TimeSpan.FromMilliseconds(47);

            var copy = config.Clone();
            config.MaxReceiveRepairWait = TimeSpan.FromMilliseconds(90);
            Assert.Equal(TimeSpan.FromMilliseconds(47), copy.MaxReceiveRepairWait);
        }

        [Theory]
        [InlineData(47, 0, 47)]
        [InlineData(20, 60, 60)]
        public void IncompleteFrameExpiresAtConfiguredOrExplicitBufferDeadline(int configuredMs, int bufferMs, int expectedMs)
        {
            using (var receiver = new Receiver(receiveWait: TimeSpan.FromMilliseconds(configuredMs),
                explicitBufferWait: bufferMs == 0 ? (TimeSpan?)null : TimeSpan.FromMilliseconds(bufferMs)))
            {
                var wire = CreateWire(Vp8Payloads(), 500, 9000);
                receiver.Receive(wire.Single(x => x.MediaIndex == 0).Packet);
                receiver.Receive(wire.Single(x => x.MediaIndex == 2).Packet);

                receiver.Clock.Advance(TimeSpan.FromMilliseconds(expectedMs - 1));
                Assert.Empty(receiver.Frames);
                Assert.True(receiver.Clock.PendingCount > 0);
                receiver.Clock.Advance(TimeSpan.FromMilliseconds(1));
                Assert.Equal(0, receiver.Clock.PendingCount);
                Assert.Empty(receiver.Media);

                // Expired timestamps cannot return when a delayed original or repair arrives.
                receiver.Receive(wire.Single(x => x.MediaIndex == 1).Packet);
                receiver.Receive(wire.Single(x => x.MediaIndex < 0).Packet);
                Assert.Empty(receiver.Frames);
                foreach (var packet in CreateWire(new[] { new byte[] { 0x10, 9, 8 } }, 504, 12000))
                {
                    receiver.Receive(packet.Packet);
                }
                Assert.Equal(new byte[] { 9, 8 }, Assert.Single(receiver.Frames));
                Assert.Equal(504, Assert.Single(receiver.Media).Header.SequenceNumber);
            }
        }

        [Fact]
        public void RemoveBufferRestoresConfiguredFecDeadline()
        {
            using (var receiver = new Receiver(receiveWait: TimeSpan.FromMilliseconds(47),
                explicitBufferWait: TimeSpan.FromMilliseconds(80)))
            {
                receiver.Stream.RemoveBuffer(TimeSpan.FromMilliseconds(80));
                var wire = CreateWire(Vp8Payloads(), 500, 9000);
                receiver.Receive(wire.Single(x => x.MediaIndex == 0).Packet);
                receiver.Receive(wire.Single(x => x.MediaIndex == 2).Packet);

                receiver.Clock.Advance(TimeSpan.FromMilliseconds(46));
                Assert.True(receiver.Clock.PendingCount > 0);
                receiver.Clock.Advance(TimeSpan.FromMilliseconds(1));
                Assert.Equal(0, receiver.Clock.PendingCount);
                Assert.Empty(receiver.Frames);
                Assert.Empty(receiver.Media);
            }
        }

        [Fact]
        public void ReenableFecCancelsPreviousDeadlineAndStartsFreshSequenceWindow()
        {
            using (var receiver = new Receiver())
            {
                var oldWire = CreateWire(Vp8Payloads(), 500, 9000);
                receiver.Receive(oldWire[0].Packet);
                receiver.Receive(oldWire.Single(x => x.MediaIndex == 2).Packet);

                receiver.Stream.EnableUlpfec(RedPt, FecPt, MediaPt);
                foreach (var packet in CreateWire(new[] { new byte[] { 0x10, 7, 8 } }, 7, 30000))
                {
                    receiver.Receive(packet.Packet);
                }
                receiver.Clock.Advance(TimeSpan.FromMilliseconds(100));

                Assert.Equal(new byte[] { 7, 8 }, Assert.Single(receiver.Frames));
                Assert.Equal(7, Assert.Single(receiver.Media).Header.SequenceNumber);
                Assert.Equal(0, receiver.Clock.PendingCount);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DisableOrCloseCancelsPendingFrameDelivery(bool close)
        {
            using (var receiver = new Receiver())
            {
                var wire = CreateWire(Vp8Payloads(), 500, 9000);
                receiver.Receive(wire[0].Packet);
                receiver.Receive(wire.Single(x => x.MediaIndex == 2).Packet);
                Assert.Empty(receiver.Frames);
                Assert.True(receiver.Clock.PendingCount > 0);

                if (close)
                {
                    receiver.Stream.IsClosed = true;
                }
                else
                {
                    receiver.Stream.DisableUlpfec();
                }
                receiver.Clock.Advance(TimeSpan.FromMilliseconds(100));

                Assert.Empty(receiver.Frames);
                Assert.Empty(receiver.Media);
                Assert.Equal(0, receiver.Clock.PendingCount);
            }
        }

        [Fact]
        public void UnprotectedVideoStillDeliversImmediately()
        {
            using (var receiver = new Receiver(enableFec: false))
            {
                receiver.Receive(CreateMedia(42, 3000, new byte[] { 0x10, 0xA1, 0xB2 }, true));

                Assert.Equal(new byte[] { 0xA1, 0xB2 }, Assert.Single(receiver.Frames));
                Assert.Equal(42, Assert.Single(receiver.Media).Header.SequenceNumber);
                Assert.Equal(1u, receiver.Stream.RtcpSession.PacketsReceivedCount);
                Assert.Equal(0, receiver.Clock.PendingCount);
            }
        }

        [Theory]
        [InlineData("VP9")]
        [InlineData("AV1")]
        [InlineData("H265")]
        [InlineData("JPEG")]
        public void OtherVideoCodecsRecognizeFrameStartsWithoutRepairAndHoldMissingStarts(string codec)
        {
            byte[] start;
            byte[] continuation;
            switch (codec)
            {
                case "VP9":
                    start = new byte[] { 0x08, 0x11, 0x22 }; // B: beginning of frame.
                    continuation = new byte[] { 0x00, 0x33, 0x44 };
                    break;
                case "AV1":
                    start = new byte[] { 0x00, 0x11, 0x22 }; // Z=0: first OBU element is not a continuation.
                    continuation = new byte[] { 0x80, 0x33, 0x44 };
                    break;
                case "H265":
                    start = new byte[] { 0x02, 0x01, 0x11 }; // Complete NAL, type 1.
                    continuation = new byte[] { 0x62, 0x01, 0x01, 0x33 }; // FU, type 49, without S.
                    break;
                default:
                    start = new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }; // JPEG fragment offset zero.
                    continuation = new byte[] { 0, 0, 0, 1, 1, 2, 3, 4 };
                    break;
            }

            // Observe ordered RTP events only: these payloads test start detection, not codec decoding.
            using (var receiver = new Receiver(codec: codec, captureFrames: false))
            {
                receiver.Receive(CreateWire(new[] { start }, 100, 3000).First(x => x.MediaIndex == 0).Packet);

                var delivered = Assert.Single(receiver.Media);
                Assert.Equal(100, delivered.Header.SequenceNumber);
                Assert.Equal(start, delivered.GetPayloadBytes());
                Assert.Equal(0, receiver.Clock.PendingCount);
            }

            using (var receiver = new Receiver(codec: codec, captureFrames: false))
            {
                receiver.Receive(CreateWire(new[] { continuation }, 100, 3000).First(x => x.MediaIndex == 0).Packet);
                receiver.Clock.Advance(TimeSpan.FromMilliseconds(19));
                Assert.Empty(receiver.Media);
                Assert.True(receiver.Clock.PendingCount > 0);
                receiver.Clock.Advance(TimeSpan.FromMilliseconds(1));
                Assert.Empty(receiver.Media);
                Assert.Equal(0, receiver.Clock.PendingCount);

                receiver.Receive(CreateWire(new[] { start }, 102, 6000).First(x => x.MediaIndex == 0).Packet);
                var delivered = Assert.Single(receiver.Media);
                Assert.Equal(102, delivered.Header.SequenceNumber);
                Assert.Equal(start, delivered.GetPayloadBytes());
            }
        }

        private static byte[][] Vp8Payloads() => new[]
        {
            new byte[] { 0x10, 1, 1 },
            new byte[] { 0x00, 2, 2 },
            new byte[] { 0x00, 3, 3 }
        };

        private static RTPPacket CreateMedia(ushort sequence, uint timestamp, byte[] payload, bool marker, uint ssrc = Ssrc)
        {
            var packet = new RTPPacket(new ArraySegment<byte>(payload), 0);
            packet.Header.SyncSource = ssrc;
            packet.Header.SequenceNumber = sequence;
            packet.Header.Timestamp = timestamp;
            packet.Header.PayloadType = MediaPt;
            packet.Header.MarkerBit = marker ? 1 : 0;
            return packet;
        }

        private static List<WirePacket> CreateWire(byte[][] payloads, ushort sequence, uint timestamp, uint ssrc = Ssrc)
        {
            var encoder = new RtpUlpfecEncoder(RedPt, FecPt, MediaPt);
            var wire = new List<WirePacket>();
            for (int i = 0; i < payloads.Length; i++)
            {
                var media = CreateMedia(sequence++, timestamp, payloads[i], i == payloads.Length - 1, ssrc);
                encoder.AddMediaPacket(media, () => sequence++, out var repairs);
                var red = CreateMedia(media.Header.SequenceNumber, timestamp,
                    RtpRedPayload.EncodeSingleBlock(payloads[i], MediaPt), i == payloads.Length - 1, ssrc);
                red.Header.PayloadType = RedPt;
                wire.Add(new WirePacket { Packet = red, MediaIndex = i });
                wire.AddRange((repairs ?? new List<RTPPacket>()).Select(x => new WirePacket { Packet = x, MediaIndex = -1 }));
            }
            return wire;
        }

        private sealed class WirePacket
        {
            public RTPPacket Packet;
            public int MediaIndex;
        }

        private sealed class Receiver : IDisposable
        {
            public readonly VideoStream Stream;
            public readonly FecTestClock Clock = new FecTestClock();
            public readonly List<byte[]> Frames = new List<byte[]>();
            public readonly List<RTPPacket> Media = new List<RTPPacket>();

            public Receiver(string codec = "VP8", bool enableFec = true, TimeSpan? receiveWait = null,
                TimeSpan? explicitBufferWait = null, bool captureFrames = true, bool secure = false)
            {
                Stream = new VideoStream(new RtpSessionConfig
                {
                    RtpSecureMediaOption = secure ? RtpSecureMediaOptionEnum.DtlsSrtp : RtpSecureMediaOptionEnum.None
                }, 0);
                var formats = new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, MediaPt, codec, 90000),
                    SDPAudioVideoMediaFormat.CreateRedFormat(RedPt),
                    SDPAudioVideoMediaFormat.CreateUlpfecFormat(FecPt)
                };
                Stream.LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, formats, MediaStreamStatusEnum.SendRecv);
                Stream.RemoteTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, true, formats, MediaStreamStatusEnum.SendRecv);
                Stream.RtcpSession = new RTCPSession(SDPMediaTypesEnum.video, 0x9876) { PayloadSampleRateHz = 90000 };
                Stream.SetFecReceiveTiming(() => Clock.Now, Clock.CreateTimer);
                if (explicitBufferWait.HasValue)
                {
                    Stream.AddBuffer(explicitBufferWait.Value);
                }
                if (enableFec)
                {
                    Stream.EnableUlpfec(RedPt, FecPt, MediaPt, new RtpUlpfecConfig
                    {
                        MaxReceiveRepairWait = receiveWait ?? TimeSpan.FromMilliseconds(20)
                    });
                }
                if (captureFrames)
                {
                    Stream.OnVideoFrameReceivedByIndex += (idx, ep, ts, frame, fmt) => Frames.Add(frame);
                }
                Stream.OnRtpPacketReceivedByIndex += (idx, ep, mediaType, packet) => Media.Add(packet);
            }

            public void Receive(RTPPacket packet)
            {
                UlpfecReviewRegressionUnitTest.Receive(Stream, packet);
            }

            public void Dispose()
            {
                Stream.IsClosed = true;
            }
        }

        private static void Receive(MediaStream stream, RTPPacket packet)
        {
            var bytes = packet.GetBytes();
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5004);
            stream.OnReceiveRTPPacket(new RTPHeader(bytes), endpoint.Port, endpoint, bytes);
        }

    }
}
