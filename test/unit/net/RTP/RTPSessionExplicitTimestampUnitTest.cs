//-----------------------------------------------------------------------------
// Filename: RTPSessionExplicitTimestampUnitTest.cs
//
// Description: The duration based send methods stamp a frame with the track's
// current timestamp and then add the duration, so the duration has to be the
// time until the next frame. Sources that know each frame's presentation time
// but not when the next frame arrives (live or relayed media) need to supply
// the RTP timestamp directly. These tests send with the explicit timestamp
// methods between two sessions over loopback and check the timestamps that
// arrive, and that the duration based methods are unchanged.
//
// History:
// 05 Oct 2026  Ryan Morris   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionExplicitTimestampUnitTest
    {
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(5);

        private readonly ILogger logger;

        public RTPSessionExplicitTimestampUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Every packet of each H264 access unit carries the supplied timestamp, including the FU-A
        /// fragments of a NAL larger than the MTU, and irregular intervals between frames are kept.
        /// </summary>
        [Fact]
        public async Task SendVideoAt_H264_StampsEveryPacketOfEachAccessUnit()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateVideo(96, "H264"))
            {
                uint[] timestamps = { 1_000, 7_000, 12_500, 450_000 };
                byte[] accessUnit = H264AccessUnit(smallNalLength: 20, largeNalLength: 3_000);

                foreach (var ts in timestamps)
                {
                    pair.Sender.SendVideoAt(ts, accessUnit);
                }

                // Per access unit: one packet for the small NAL, three FU-A fragments for the large one.
                var packets = await pair.WaitForPackets(timestamps.Length * 4);

                AssertFrames(packets, timestamps, packetsPerFrame: 4);
            }
        }

        /// <summary>
        /// The same for H265, which aggregates the small NALs into one packet and fragments the large one.
        /// </summary>
        [Fact]
        public async Task SendVideoAt_H265_StampsEveryPacketOfEachFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateVideo(96, "H265"))
            {
                uint[] timestamps = { 90_000, 96_000, 96_001 };
                byte[] frame = H265Frame(smallNalLengths: new[] { 20, 30 }, largeNalLength: 2_500);

                foreach (var ts in timestamps)
                {
                    pair.Sender.SendVideoAt(ts, frame);
                }

                // Per frame: one aggregation packet for the two small NALs, three fragments for the large one.
                var packets = await pair.WaitForPackets(timestamps.Length * 4);

                AssertFrames(packets, timestamps, packetsPerFrame: 4);
            }
        }

        /// <summary>
        /// VP8 frames spanning several packets all carry the supplied timestamp.
        /// </summary>
        [Fact]
        public async Task SendVideoAt_Vp8_StampsEveryPacketOfEachFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateVideo(96, "VP8"))
            {
                uint[] timestamps = { 3_000, 3_033, 9_000 };
                byte[] frame = Filled(2_500, 0x42); // three packets at the 1200 byte MTU

                foreach (var ts in timestamps)
                {
                    pair.Sender.SendVideoAt(ts, frame);
                }

                var packets = await pair.WaitForPackets(timestamps.Length * 3);

                AssertFrames(packets, timestamps, packetsPerFrame: 3);
            }
        }

        /// <summary>
        /// VP9 frames carry the supplied timestamp, and the picture ID still advances once per frame.
        /// </summary>
        [Fact]
        public async Task SendVideoAt_Vp9_StampsEachFrameAndAdvancesPictureId()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateVideo(98, "VP9"))
            {
                uint[] timestamps = { 500, 4_000, 4_100 };
                byte[] frame = Filled(2_000, 0x42);

                foreach (var ts in timestamps)
                {
                    pair.Sender.SendVideoAt(ts, frame);
                }

                var packets = await pair.WaitForPackets(timestamps.Length * 2);

                AssertFrames(packets, timestamps, packetsPerFrame: 2);

                // VP9 payload descriptor bytes 1-2 hold the 15 bit picture ID: same within a frame, +1 per frame.
                int PictureId(ReceivedPacket p) => ((p.Payload[1] & 0x7F) << 8) | p.Payload[2];
                var ids = packets.Select(PictureId).ToArray();
                Assert.Equal(ids[0], ids[1]);
                Assert.Equal(ids[0] + 1, ids[2]);
                Assert.Equal(ids[2], ids[3]);
                Assert.Equal(ids[2] + 1, ids[4]);
            }
        }

        /// <summary>
        /// AV1 temporal units spanning several packets carry the supplied timestamp on every packet.
        /// </summary>
        [Fact]
        public async Task SendVideoAt_Av1_StampsEveryPacketOfEachTemporalUnit()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateVideo(100, "AV1"))
            {
                uint[] timestamps = { 9_000, 18_000, 18_500 };
                byte[] temporalUnit = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, new byte[] { 0x01, 0x02, 0x03 })
                    .Concat(CreateObu(AV1Packetiser.AV1ObuType.Frame, Filled(3_000, 0x11)))
                    .ToArray();

                foreach (var ts in timestamps)
                {
                    pair.Sender.SendVideoAt(ts, temporalUnit);
                }

                var packets = await pair.WaitForPackets(timestamps.Length * 3);

                AssertFramesGrouped(packets, timestamps, checkMarkers: true);
            }
        }

        /// <summary>
        /// MJPEG through the top level SendVideoAt dispatch stamps every packet of each frame with the supplied
        /// timestamp. (Markers aren't checked: SendMJPEGFrame sets them as isLast ? 0 : 1, which predates this change.)
        /// </summary>
        [Fact]
        public async Task SendVideoAt_Mjpeg_StampsEveryPacketOfEachFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateVideo(26, "JPEG"))
            {
                uint[] timestamps = { 45_000, 48_000 };
                byte[] jpeg = CreateTestJpegFrame(entropyLength: 3_000);

                foreach (var ts in timestamps)
                {
                    pair.Sender.SendVideoAt(ts, jpeg);
                }

                var packets = await pair.WaitForPackets(timestamps.Length * 3);

                AssertFramesGrouped(packets, timestamps, checkMarkers: false);
                Assert.Equal(48_000u, pair.Sender.VideoStream.LocalTrack.Timestamp);
            }
        }

        /// <summary>
        /// The duration based method is unchanged: each frame is stamped with the track timestamp,
        /// which then advances by the duration.
        /// </summary>
        [Fact]
        public async Task SendVideo_Duration_BehaviourUnchanged()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateVideo(96, "VP8"))
            {
                uint start = pair.Sender.VideoStream.LocalTrack.Timestamp;
                byte[] frame = Filled(500, 0x42);

                pair.Sender.SendVideo(3_000, frame);
                pair.Sender.SendVideo(3_000, frame);
                pair.Sender.SendVideo(6_000, frame);

                var packets = await pair.WaitForPackets(3);

                Assert.Equal(new[] { start, start + 3_000, start + 6_000 }, packets.Select(p => p.Timestamp));
                Assert.Equal(start + 12_000, pair.Sender.VideoStream.LocalTrack.Timestamp);
            }
        }

        /// <summary>
        /// After an explicit video send the track timestamp is the timestamp just sent, so a duration based
        /// send straight after reuses it (which is why the two styles shouldn't be mixed on one track).
        /// </summary>
        [Fact]
        public async Task SendVideoAt_LeavesTrackTimestampAtFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateVideo(96, "VP8"))
            {
                pair.Sender.SendVideoAt(123_456, Filled(100, 0x42));
                Assert.Equal(123_456u, pair.Sender.VideoStream.LocalTrack.Timestamp);

                pair.Sender.SendVideo(3_000, Filled(100, 0x42));

                var packets = await pair.WaitForPackets(2);

                Assert.Equal(new uint[] { 123_456, 123_456 }, packets.Select(p => p.Timestamp));
                Assert.Equal(126_456u, pair.Sender.VideoStream.LocalTrack.Timestamp);
            }
        }

        /// <summary>
        /// Audio frames carry the supplied timestamp; the track timestamp is left where the next frame starts.
        /// </summary>
        [Fact]
        public async Task SendAudioAt_StampsEachFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateAudio())
            {
                uint[] timestamps = { 160, 320, 800, 960 };
                foreach (var ts in timestamps)
                {
                    pair.Sender.SendAudioAt(ts, 160, Filled(160, 0x7F));
                }

                var packets = await pair.WaitForPackets(timestamps.Length);

                Assert.Equal(timestamps, packets.Select(p => p.Timestamp));
                Assert.Equal(960u + 160u, pair.Sender.AudioStream.LocalTrack.Timestamp);
            }
        }

        /// <summary>
        /// An audio frame larger than the MTU is split, and each packet is stamped by its sample position
        /// within the frame (G.711: one byte per sample).
        /// </summary>
        [Fact]
        public async Task SendAudioAt_SplitsOversizedFrameBySamplePosition()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateAudio())
            {
                pair.Sender.SendAudioAt(10_000, 3_000, Filled(3_000, 0x7F));

                var packets = await pair.WaitForPackets(3);

                Assert.Equal(new uint[] { 10_000, 11_200, 12_400 }, packets.Select(p => p.Timestamp));
                Assert.Equal(new[] { 1_200, 1_200, 600 }, packets.Select(p => p.PayloadLength));
                Assert.Equal(13_000u, pair.Sender.AudioStream.LocalTrack.Timestamp);
            }
        }

        /// <summary>
        /// The duration based audio method is unchanged.
        /// </summary>
        [Fact]
        public async Task SendAudio_Duration_BehaviourUnchanged()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pair = await LoopbackPair.CreateAudio())
            {
                uint start = pair.Sender.AudioStream.LocalTrack.Timestamp;

                pair.Sender.SendAudio(160, Filled(160, 0x7F));
                pair.Sender.SendAudio(3_000, Filled(3_000, 0x7F));

                var packets = await pair.WaitForPackets(4);

                Assert.Equal(new[] { start, start + 160, start + 1_360, start + 2_560 }, packets.Select(p => p.Timestamp));
                Assert.Equal(start + 3_160, pair.Sender.AudioStream.LocalTrack.Timestamp);
            }
        }

        /// <summary>
        /// Asserts the packets (in send order) are <paramref name="packetsPerFrame"/> per frame, every packet of a
        /// frame carries that frame's timestamp, and only the last packet of each frame has the marker bit.
        /// </summary>
        private static void AssertFrames(List<ReceivedPacket> packets, uint[] timestamps, int packetsPerFrame)
        {
            Assert.Equal(timestamps.Length * packetsPerFrame, packets.Count);
            Assert.Equal(
                timestamps.SelectMany(ts => Enumerable.Repeat(ts, packetsPerFrame)),
                packets.Select(p => p.Timestamp));
            Assert.Equal(
                Enumerable.Range(0, packets.Count).Select(i => i % packetsPerFrame == packetsPerFrame - 1 ? 1 : 0),
                packets.Select(p => p.Marker));
        }

        /// <summary>
        /// For frames whose packet count depends on header sizes: the packets (in send order) form one consecutive
        /// run per frame, every run carries its frame's timestamp, and every frame spans the same number of packets
        /// (more than one, so per-packet stamping is actually exercised).
        /// </summary>
        private static void AssertFramesGrouped(List<ReceivedPacket> packets, uint[] timestamps, bool checkMarkers)
        {
            var runs = new List<List<ReceivedPacket>>();
            foreach (var packet in packets)
            {
                if (runs.Count == 0 || runs[runs.Count - 1][0].Timestamp != packet.Timestamp)
                {
                    runs.Add(new List<ReceivedPacket>());
                }

                runs[runs.Count - 1].Add(packet);
            }

            Assert.Equal(timestamps, runs.Select(r => r[0].Timestamp));
            Assert.All(runs, r => Assert.True(r.Count > 1, "each frame should span several packets"));
            Assert.Single(runs.Select(r => r.Count).Distinct());

            if (checkMarkers)
            {
                Assert.All(runs, r => Assert.Equal(
                    Enumerable.Range(0, r.Count).Select(i => i == r.Count - 1 ? 1 : 0),
                    r.Select(p => p.Marker)));
            }
        }

        /// <summary>
        /// Minimal synthetic baseline JPEG (as in MJPEGPacketiserUnitTest): SOI, DQT, SOF0 16x16, SOS, entropy data
        /// containing no 0xFF bytes, EOI.
        /// </summary>
        private static byte[] CreateTestJpegFrame(int entropyLength)
        {
            var frame = new List<byte>();
            frame.AddRange(new byte[] { 0xFF, 0xD8 });
            frame.AddRange(new byte[] { 0xFF, 0xDB, 0x00, 0x43, 0x00 });
            frame.AddRange(Enumerable.Range(1, 64).Select(x => (byte)x));
            frame.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x10, 0x03,
                0x01, 0x21, 0x00,
                0x02, 0x11, 0x00,
                0x03, 0x11, 0x00 });
            frame.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00 });
            frame.AddRange(Enumerable.Range(0, entropyLength).Select(i => (byte)(0x10 + (i % 0x60))));
            frame.AddRange(new byte[] { 0xFF, 0xD9 });
            return frame.ToArray();
        }

        private static byte[] Filled(int length, byte value)
        {
            return Enumerable.Repeat(value, length).ToArray();
        }

        /// <summary>
        /// Annex B access unit: a small non-IDR NAL followed by a large IDR NAL (fragmented as FU-A).
        /// </summary>
        private static byte[] H264AccessUnit(int smallNalLength, int largeNalLength)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0, 0, 0, 1, 0x41 });
            bytes.AddRange(Filled(smallNalLength - 1, 0xAB));
            bytes.AddRange(new byte[] { 0, 0, 0, 1, 0x65 });
            bytes.AddRange(Filled(largeNalLength - 1, 0xAB));
            return bytes.ToArray();
        }

        /// <summary>
        /// Annex B H265 frame: small NALs (aggregated) and one large IDR NAL (fragmented).
        /// </summary>
        private static byte[] H265Frame(int[] smallNalLengths, int largeNalLength)
        {
            var bytes = new List<byte>();
            foreach (var length in smallNalLengths)
            {
                bytes.AddRange(new byte[] { 0, 0, 0, 1, 0x02, 0x01 }); // TRAIL_R
                bytes.AddRange(Filled(length - 2, 0xAB));
            }

            bytes.AddRange(new byte[] { 0, 0, 0, 1, 0x26, 0x01 }); // IDR_W_RADL
            bytes.AddRange(Filled(largeNalLength - 2, 0xAB));
            return bytes.ToArray();
        }

        private static byte[] CreateObu(AV1Packetiser.AV1ObuType obuType, byte[] payload)
        {
            byte obuHeader = (byte)(((byte)obuType << 3) | 0x02);
            var leb128Size = AV1Packetiser.WriteLeb128(payload.Length);
            return new[] { obuHeader }.Concat(leb128Size).Concat(payload).ToArray();
        }

        private sealed record ReceivedPacket(ushort SequenceNumber, uint Timestamp, int Marker, int PayloadLength, byte[] Payload);

        /// <summary>
        /// A send-only and a receive-only session negotiated with each other over loopback.
        /// </summary>
        private sealed class LoopbackPair : IDisposable
        {
            private readonly ConcurrentQueue<ReceivedPacket> received = new ConcurrentQueue<ReceivedPacket>();

            private LoopbackPair(RTPSession sender, RTPSession receiver)
            {
                Sender = sender;
                Receiver = receiver;
                Receiver.OnRtpPacketReceived += (ep, mediaType, pkt) =>
                    received.Enqueue(new ReceivedPacket(pkt.Header.SequenceNumber, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Payload.Length, pkt.Payload.ToArray()));
            }

            public RTPSession Sender { get; }

            public RTPSession Receiver { get; }

            public static Task<LoopbackPair> CreateVideo(int payloadId, string codecName)
            {
                return Create(
                    new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithVideoTrack(payloadId, codecName, 90000, MediaStreamStatusEnum.SendOnly),
                    new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithVideoTrack(payloadId, codecName, 90000, MediaStreamStatusEnum.RecvOnly));
            }

            public static Task<LoopbackPair> CreateAudio()
            {
                return Create(
                    new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU, MediaStreamStatusEnum.SendOnly),
                    new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU, MediaStreamStatusEnum.RecvOnly));
            }

            /// <summary>
            /// Waits for at least <paramref name="expected"/> packets and returns them in send order (by RTP
            /// sequence number, not arrival order, which UDP doesn't guarantee).
            /// </summary>
            public async Task<List<ReceivedPacket>> WaitForPackets(int expected)
            {
                var deadline = DateTime.UtcNow + ReceiveTimeout;
                while (DateTime.UtcNow < deadline && received.Count < expected)
                {
                    await Task.Delay(20);
                }

                // Let any stragglers in so an over-count is caught by the assertions.
                await Task.Delay(100);

                var packets = received.ToList();
                if (packets.Count == 0)
                {
                    return packets;
                }

                // Order by sequence number relative to the first arrival, signed so a 16 bit wrap is handled.
                ushort reference = packets[0].SequenceNumber;
                return packets.OrderBy(p => (short)(p.SequenceNumber - reference)).ToList();
            }

            public void Dispose()
            {
                Close(Sender);
                Close(Receiver);
            }

            private static void Close(RTPSession session)
            {
                session.Close("test done");
                session.Dispose();
            }

            private static async Task<LoopbackPair> Create(RtpSessionBuilder senderBuilder, RtpSessionBuilder receiverBuilder)
            {
                RTPSession sender = null;
                RTPSession receiver = null;

                try
                {
                    sender = senderBuilder.Build();
                    receiver = receiverBuilder.Build();

                    SDP offer = sender.CreateOffer(IPAddress.Loopback);
                    Assert.Equal(SetDescriptionResultEnum.OK, receiver.SetRemoteDescription(SdpType.offer, offer));

                    SDP answer = receiver.CreateAnswer(IPAddress.Loopback);
                    Assert.Equal(SetDescriptionResultEnum.OK, sender.SetRemoteDescription(SdpType.answer, answer));

                    await sender.Start();
                    await receiver.Start();

                    return new LoopbackPair(sender, receiver);
                }
                catch
                {
                    if (sender != null)
                    {
                        Close(sender);
                    }

                    if (receiver != null)
                    {
                        Close(receiver);
                    }

                    throw;
                }
            }
        }
    }
}
