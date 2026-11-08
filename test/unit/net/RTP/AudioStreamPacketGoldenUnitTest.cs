//-----------------------------------------------------------------------------
// Filename: AudioStreamPacketGoldenUnitTest.cs
//
// Description: Pins the exact RTP packets AudioStream puts on the wire for a set
// of deterministic audio samples, covering the array and array segment send
// methods, explicit timestamps and sequence numbers, samples split across
// packets, header extensions and SRTP protection. The audio send path shares the
// packet building code with video, so this shows that changes made to that code
// for video leave the audio packets unchanged.
//
// The expected digests were produced by running this file against the send path
// as it was at commit c331140ed, before the shared send code changed. It only
// uses API that existed then, so it can be re-run there. On .NET Framework that
// commit sent the wrong bytes for samples given as segments of a larger array,
// which SendAudio_SegmentOfLargerArray_SendsTheSegmentBytes covers, so there the
// digests hold from that fix on.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 08 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class AudioStreamPacketGoldenUnitTest
    {
        private const uint SSRC = 0x55667788;
        private const ushort FIRST_SEQUENCE_NUMBER = 65533; // Wraps during most scenarios.

        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public AudioStreamPacketGoldenUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// 20 ms PCMU samples, with a short, a long and an empty sample in between.
        /// </summary>
        [Fact]
        public void SendAudio_Pcmu_MatchesGoldenWireOutput()
        {
            var packets = Run(new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU), false, false, stream =>
            {
                for (int i = 0; i < 6; i++)
                {
                    stream.SendAudio(160, Sample(160, (uint)i));
                }
                stream.SendAudio(80, Sample(80, 10));
                stream.SendAudio(320, Sample(320, 11));
                stream.SendAudio(160, Array.Empty<byte>());
                stream.SendAudio(160, Sample(160, 12));
            });

            AssertDigest("PCMU", "4C3B77F81F74AB9D69919D37B8D9E57E8A954E3D60972102257EBCE4E5A6BFC2", packets);
        }

        /// <summary>
        /// Opus samples with the audio level and transport wide congestion control header extensions, protected with
        /// a stand in for SRTP.
        /// </summary>
        [Fact]
        public void SendAudio_Opus_HeaderExtensions_Srtp_MatchesGoldenWireOutput()
        {
            var opus = new AudioFormat(AudioCodecsEnum.OPUS, 111, 48000, 2, "minptime=10;useinbandfec=1");

            var packets = Run(opus, true, true, stream =>
            {
                for (int i = 0; i < 8; i++)
                {
                    stream.SendAudio(960, Sample(60 + i * 37, (uint)(20 + i)));
                }
            });

            AssertDigest("Opus_HeaderExtensions_Srtp", "77ADAEC77FFD8376DD2071325DAA91E2BBE2EAABE4AA8045F26B4FE54DE1DED7", packets);
        }

        /// <summary>
        /// Samples taken from the middle of larger buffers, samples too large for one packet, and explicit
        /// timestamps.
        /// </summary>
        [Fact]
        public void SendAudio_SegmentsLargeSamplesAndTimestamps_MatchesGoldenWireOutput()
        {
            var packets = Run(new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA), true, false, stream =>
            {
                byte[] buffer = Sample(2000, 30);
                stream.SendAudio(160, new ArraySegment<byte>(buffer, 7, 160));
                stream.SendAudio(160, new ArraySegment<byte>(buffer, 500, 160));
                stream.SendAudio(1600, Sample(1600, 31));
                stream.SendAudio(3000, Sample(3000, 32));
                stream.SendAudioAt(100_000, 160, Sample(160, 33));
                stream.SendAudioAt(100_160, 160, new ArraySegment<byte>(buffer, 900, 160));
                stream.SendAudioAt(200_000, 2400, Sample(2400, 34));
            });

            AssertDigest("SegmentsLargeSamplesAndTimestamps", "544C3F15141F6F51846B630B0ACCEECCBC6887D593F093947DEA114AA667D2E4", packets);
        }

        /// <summary>
        /// Packets sent with an explicit payload type, and with an explicit sequence number through the raw send
        /// method, which bypasses the track's sequence numbers.
        /// </summary>
        [Fact]
        public void SendAudioFrameAndRawWithSequenceNumber_MatchesGoldenWireOutput()
        {
            var packets = Run(new AudioFormat(SDPWellKnownMediaFormatsEnum.G722), true, true, stream =>
            {
                stream.SendAudioFrame(320, 9, Sample(160, 40));
                stream.SendAudioFrame(320, 9, new ArraySegment<byte>(Sample(400, 41), 100, 160));
                stream.SendRtpRaw(Sample(160, 42), 5000, 1, 9, (ushort)1234);
                stream.SendRtpRaw(Sample(80, 43), 5160, 0, 9);
                stream.SendRtpRaw(Sample(160, 44), 5320, 0, 9, (ushort)65535);
            });

            AssertDigest("SendAudioFrameAndRawWithSequenceNumber", "A6F120C831B794C856203DF55CCCD20F2A93F9F43E2571AA2CBBACA65885B202", packets);
        }

        /// <summary>
        /// A sample given as a segment of a larger array is sent from the segment's offset, also when it is split
        /// across several packets. The builds that don't target .NET Core used to take the packets from the start of
        /// the array instead.
        /// </summary>
        [Fact]
        public void SendAudio_SegmentOfLargerArray_SendsTheSegmentBytes()
        {
            byte[] buffer = Sample(4000, 50);
            var small = new ArraySegment<byte>(buffer, 7, 160);
            var large = new ArraySegment<byte>(buffer, 500, 3000);

            var packets = Run(new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA), false, false, stream =>
            {
                stream.SendAudio(160, small);
                stream.SendAudio(3000, large);
            });

            // No CSRCs or header extensions, so every header is the fixed 12 bytes.
            byte[] payloads = packets.SelectMany(p => p.Skip(12)).ToArray();
            byte[] expected = small.Concat(large).ToArray();

            Assert.True(packets.Count > 2, $"Expected the large sample to be split, got {packets.Count} packets.");
            Assert.Equal(expected, payloads);
        }

        private void AssertDigest(string name, string expectedDigest, List<byte[]> packets)
        {
            string digest = VideoRtpGoldenScenario.Digest(packets);
            _output.WriteLine($"{name}: {packets.Count} packets, {packets.Sum(p => p.Length)} bytes, digest {digest}");

            Assert.Equal(expectedDigest, digest);
        }

        /// <summary>
        /// Sends through a new audio stream and returns the packets that were put on the wire.
        /// </summary>
        private static List<byte[]> Run(AudioFormat audioFormat, bool headerExtensions, bool srtp, Action<AudioStream> send)
        {
            var format = new SDPAudioVideoMediaFormat(audioFormat);

            Dictionary<int, RTPHeaderExtension> extensions = null;
            if (headerExtensions)
            {
                extensions = new Dictionary<int, RTPHeaderExtension>
                {
                    { 1, new AudioLevelExtension(1) },
                    { 3, new TransportWideCCExtension(3) },
                };
            }

            var stream = new AudioStream(new RtpSessionConfig(), 0);
            stream.LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { format },
                MediaStreamStatusEnum.SendOnly, null, extensions);
            stream.LocalTrack.Ssrc = SSRC;
            stream.LocalTrack.SeqNum = FIRST_SEQUENCE_NUMBER;
            stream.DestinationEndPoint = new IPEndPoint(IPAddress.Loopback, 5004);

            if (srtp)
            {
                stream.SetSecurityContext(VideoRtpGoldenScenario.FakeProtect, VideoRtpGoldenScenario.FakeProtect,
                    VideoRtpGoldenScenario.FakeProtect, VideoRtpGoldenScenario.FakeProtect);
            }

            var channel = new CapturingRtpChannel();
            stream.AddRtpChannel(channel);

            try
            {
                send(stream);
                return channel.Packets;
            }
            finally
            {
                channel.Close("test complete");
            }
        }

        private static byte[] Sample(int length, uint seed)
        {
            var bytes = new byte[length];
            uint state = seed * 2654435761u + 1;
            for (int i = 0; i < length; i++)
            {
                // xorshift32
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                bytes[i] = (byte)state;
            }
            return bytes;
        }
    }
}
