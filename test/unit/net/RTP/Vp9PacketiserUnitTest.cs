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
