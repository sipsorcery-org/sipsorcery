//-----------------------------------------------------------------------------
// Filename: Vp9PacketiserUnitTest.cs
//
// Description: Unit tests for VP9 RTP packetisation and depacketisation.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 16 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class Vp9PacketiserUnitTest
    {
        // A VP9 uncompressed header byte with frame_marker=0b10, profile 0, show_existing_frame=0 and
        // frame_type=0 (KEY_FRAME).
        private const byte VP9_KEYFRAME_HEADER = 0x80;
        // As above but frame_type=1 (NON_KEY_FRAME).
        private const byte VP9_INTERFRAME_HEADER = 0x84;

        [Fact]
        public void PacketiseAndDepacketiseSingleVp9FrameUnitTest()
        {
            var frame = new byte[] { VP9_KEYFRAME_HEADER }
                .Concat(Enumerable.Range(0, 200).Select(x => (byte)(x % 251))).ToArray();

            var packets = Vp9Packetiser.Packetize(frame, 1200, isKeyFrame: true, pictureId: 7);

            Assert.Single(packets);

            var reconstructed = Depacketise(packets, timestamp: 90000);

            Assert.Equal(frame, reconstructed);
        }

        [Fact]
        public void PacketiseAndDepacketiseFragmentedVp9FrameUnitTest()
        {
            var frame = new byte[] { VP9_INTERFRAME_HEADER }
                .Concat(Enumerable.Range(0, 3000).Select(x => (byte)(x % 251))).ToArray();

            var packets = Vp9Packetiser.Packetize(frame, 200, isKeyFrame: false, pictureId: 42);

            Assert.True(packets.Count > 1);

            // The B (start) flag on the first fragment and the E (end) flag on the last.
            Assert.True((packets[0][0] & Vp9Packetiser.B_BIT) != 0);
            Assert.True((packets[0][0] & Vp9Packetiser.E_BIT) == 0);
            Assert.True((packets[^1][0] & Vp9Packetiser.E_BIT) != 0);

            var reconstructed = Depacketise(packets, timestamp: 180000);

            Assert.Equal(frame, reconstructed);
        }

        [Fact]
        public void Vp9KeyFrameDetectionUnitTest()
        {
            Assert.True(Vp9Packetiser.IsKeyFrame(new byte[] { VP9_KEYFRAME_HEADER, 0x49, 0x83, 0x42 }));
            Assert.False(Vp9Packetiser.IsKeyFrame(new byte[] { VP9_INTERFRAME_HEADER, 0x49, 0x83, 0x42 }));
        }

        /// <summary>
        /// Key frame detection on a span gives the same answer as on an array, wherever the frame is in its buffer.
        /// </summary>
        [Theory]
        [InlineData(new byte[] { VP9_KEYFRAME_HEADER, 0x49 }, true)]
        [InlineData(new byte[] { VP9_INTERFRAME_HEADER, 0x49 }, false)]
        [InlineData(new byte[] { 0xB0 }, true)] // Profile 3, with its reserved bit, key frame.
        [InlineData(new byte[] { 0x88 }, false)] // show_existing_frame.
        [InlineData(new byte[] { 0x40 }, false)] // Not a VP9 frame marker.
        [InlineData(new byte[0], false)]
        public void Vp9KeyFrameDetectionOnSpansUnitTest(byte[] frame, bool isKeyFrame)
        {
            var buffer = new byte[frame.Length + 8];
            buffer.AsSpan().Fill(0xFF);
            frame.CopyTo(buffer, 4);

            Assert.Equal(isKeyFrame, Vp9Packetiser.IsKeyFrame(frame));
            Assert.Equal(isKeyFrame, Vp9Packetiser.IsKeyFrame(new ReadOnlySpan<byte>(frame)));
            Assert.Equal(isKeyFrame, Vp9Packetiser.IsKeyFrame(new ReadOnlySpan<byte>(buffer, 4, frame.Length)));
            Assert.False(Vp9Packetiser.IsKeyFrame((byte[])null));
        }

        /// <summary>
        /// The buffer writer overload writes exactly the payloads the list overload returns, one after another, and
        /// their lengths in order, the picture ID cut to 15 bits as there.
        /// </summary>
        [Theory]
        [InlineData(1, 1200, true, 7)]
        [InlineData(199, 200, false, 0x7FFF)]
        [InlineData(197, 200, false, 1)]
        [InlineData(198, 200, true, 1)]
        [InlineData(3000, 200, false, 0x8005)]
        [InlineData(60_000, 1200, true, 300)]
        public void BufferWriterPayloadsAreTheListPayloads(int length, int maxPayloadSize, bool isKeyFrame, int pictureId)
        {
            var frame = Enumerable.Range(0, length).Select(x => (byte)(x * 7)).ToArray();
            var expected = Vp9Packetiser.Packetize(frame, maxPayloadSize, isKeyFrame, pictureId);
            var output = new RecordingBufferWriter();
            var payloadLengths = new List<int>();

            Vp9Packetiser.Packetize(frame, maxPayloadSize, isKeyFrame, pictureId, output, payloadLengths);

            Assert.Equal(expected.Select(p => p.Length), payloadLengths);
            Assert.Equal(expected.SelectMany(p => p).ToArray(), output.WrittenSpan.ToArray());

            // Each payload is written with one GetSpan of its length and one Advance.
            Assert.Equal(payloadLengths, output.SizeHints);
            Assert.Equal(payloadLengths, output.Advances);
        }

        /// <summary>
        /// The payloads go after what the writer already holds, and their lengths after those already in the list.
        /// </summary>
        [Fact]
        public void BufferWriterOutputIsAppendedToWhatIsAlreadyThere()
        {
            var frame = Enumerable.Range(0, 500).Select(x => (byte)x).ToArray();
            var expected = Vp9Packetiser.Packetize(frame, 200, false, 9);
            var output = new RecordingBufferWriter();
            new byte[] { 9, 9, 9 }.CopyTo(output.GetSpan(3));
            output.Advance(3);
            var payloadLengths = new List<int> { 42 };

            Vp9Packetiser.Packetize(frame, 200, false, 9, output, payloadLengths);

            Assert.Equal(new[] { 42 }.Concat(expected.Select(p => p.Length)), payloadLengths);
            Assert.Equal(new byte[] { 9, 9, 9 }.Concat(expected.SelectMany(p => p)).ToArray(), output.WrittenSpan.ToArray());
        }

        /// <summary>
        /// Null arguments are refused. An empty frame writes nothing, and only a frame with something in it has its
        /// maximum payload size checked, as with the list overload.
        /// </summary>
        [Fact]
        public void BufferWriterArgumentsAreChecked()
        {
            var frame = new byte[] { VP9_KEYFRAME_HEADER, 1, 2 };
            var output = new RecordingBufferWriter();
            var payloadLengths = new List<int>();

            var noOutput = Assert.Throws<ArgumentNullException>(() => Vp9Packetiser.Packetize(frame, 1200, true, 0, null, payloadLengths));
            var noLengths = Assert.Throws<ArgumentNullException>(() => Vp9Packetiser.Packetize(frame, 1200, true, 0, output, null));
            Assert.Equal("output", noOutput.ParamName);
            Assert.Equal("payloadLengths", noLengths.ParamName);

            Vp9Packetiser.Packetize(ReadOnlySpan<byte>.Empty, 3, true, 0, output, payloadLengths);
            Assert.Equal(0, output.WrittenCount);
            Assert.Empty(payloadLengths);

            Assert.Throws<ArgumentException>(() => Vp9Packetiser.Packetize(frame, 3, true, 0, output, payloadLengths));
            Assert.Throws<ArgumentException>(() => Vp9Packetiser.Packetize(frame, 3, true, 0));
        }

        private static byte[] Depacketise(System.Collections.Generic.List<byte[]> packets, uint timestamp)
        {
            var framer = new RtpVideoFramer(VideoCodecsEnum.VP9, 65536);
            byte[] reconstructed = null;

            for (ushort i = 0; i < packets.Count; i++)
            {
                var packet = new RTPPacket
                {
                    Header = new RTPHeader
                    {
                        SequenceNumber = i,
                        Timestamp = timestamp,
                        MarkerBit = (i == packets.Count - 1) ? 1 : 0
                    },
                    Payload = packets[i]
                };

                reconstructed = framer.GotRtpPacket(packet);
            }

            Assert.NotNull(reconstructed);
            return reconstructed;
        }
    }
}
