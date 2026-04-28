//-----------------------------------------------------------------------------
// Filename: inter_frame_header_unittest.cs
//
// Description: Unit tests for the VP8 inter-frame (P-frame) header writer,
// added in PR 2 of the P-frame foundation series. The writer emits the
// uncompressed 3-byte frame tag (key_frame_flag = 1, no start code, no
// dimensions) plus the compressed first-partition prefix through
// refresh_last_frame.
//
// Tests cover:
//   - Frame tag round-trip: write a header, parse the 3-byte tag, verify
//     key_frame_flag = 1, show_frame, version, and first_partition_length
//     all decode to the values we wrote.
//   - No start code at byte 3: inter frames omit the 0x9D 0x01 0x2A
//     marker; bytes 3+ contain the bool-coded compressed first partition.
//   - Compressed first partition round-trip: feed the bool-coder bytes
//     back through the existing decoder's dboolhuff and verify every
//     field decodes to the value we wrote (segmentation=0, filter
//     params, log2_partitions, qindex, delta-q values, golden/altref
//     refresh flags, sign biases, refresh_entropy_probs,
//     refresh_last_frame).
//
// This is sufficient correctness coverage for PR 2 -- the existing C#
// decoder is the same one used in production and any divergence in the
// new writer's output would surface as a parse mismatch here.
// Bit-exact-against-libvpx tests (matching what bitstream_unittest.cs
// does for the keyframe path) can be added later if needed; the
// round-trip approach is more reliable for header-prefix code where
// every bit is read back through the decoder anyway.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 27 Apr 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class inter_frame_header_unittest
    {
        // ---------- Frame tag round-trip ----------

        [Fact]
        public void StartInterFrameHeader_FrameTag_KeyFrameFlagIsOne()
        {
            byte[] buf = new byte[256];
            BOOL_CODER bc = default;
            var cfg = new InterFrameHeaderConfig
            {
                BaseQindex = 32,
            };
            int total;
            fixed (byte* p = buf)
            {
                bitstream.StartInterFrameHeader(p, buf.Length, cfg, ref bc);
                total = bitstream.FinishInterFrameFirstPartition(p, cfg, ref bc);
            }

            // 3-byte frame tag, little-endian.
            int v = buf[0] | (buf[1] << 8) | (buf[2] << 16);
            int keyFrameFlag = v & 0x1;
            int version = (v >> 1) & 0x7;
            int showFrame = (v >> 4) & 0x1;
            int firstPartitionLength = v >> 5;

            Assert.Equal(1, keyFrameFlag);   // <-- 1 for inter (opposite of intuition)
            Assert.Equal(0, version);
            Assert.Equal(1, showFrame);
            // Inter frame prefix is just the 3-byte tag (no start code, no
            // dimensions), so first_partition_length covers everything from
            // byte 3 to the end.
            Assert.Equal(total - 3, firstPartitionLength);
        }

        [Fact]
        public void StartInterFrameHeader_NoStartCodeAtByte3()
        {
            byte[] buf = new byte[256];
            BOOL_CODER bc = default;
            var cfg = new InterFrameHeaderConfig { BaseQindex = 32 };
            fixed (byte* p = buf)
            {
                bitstream.StartInterFrameHeader(p, buf.Length, cfg, ref bc);
                bitstream.FinishInterFrameFirstPartition(p, cfg, ref bc);
            }

            // Byte 3 onwards is the bool-coder. There must NOT be the
            // keyframe start code 0x9D 0x01 0x2A there.
            //
            // (The bool-coder always emits a high-entropy first byte so
            // the chance of a coincidental match is low, but assert the
            // strict interpretation: byte 3 happens to be the SECOND
            // byte after the bool coder is initialised, and the
            // sequence is not the start code.)
            bool startCodePresent = buf[3] == 0x9D && buf[4] == 0x01 && buf[5] == 0x2A;
            Assert.False(startCodePresent, "Inter frame must not contain the keyframe start code at byte 3.");
        }

        // ---------- Compressed first-partition round-trip ----------

        [Fact]
        public void StartInterFrameHeader_CompressedFirstPartitionDecodesToInputs()
        {
            byte[] buf = new byte[256];
            BOOL_CODER bc = default;
            var cfg = new InterFrameHeaderConfig
            {
                BaseQindex = 24,
                FilterLevel = 7,
                SharpnessLevel = 3,
                FilterType = 1,
                Log2NumberOfTokenPartitions = 0,
            };
            int total;
            fixed (byte* p = buf)
            {
                bitstream.StartInterFrameHeader(p, buf.Length, cfg, ref bc);
                total = bitstream.FinishInterFrameFirstPartition(p, cfg, ref bc);
            }

            // Feed the bool-coder bytes (starting at byte 3 -- inter
            // frames have no start code or dimensions) back through the
            // existing decoder's bool reader.
            int boolStart = bitstream.INTER_FRAME_PREFIX_BYTES;
            byte[] partition = new byte[total - boolStart + 8];  // pad for decoder lookahead
            System.Array.Copy(buf, boolStart, partition, 0, total - boolStart);

            BOOL_DECODER br = new BOOL_DECODER();
            dboolhuff.vp8dx_start_decode(ref br, partition, (uint)partition.Length);

            // Read the same fields the writer emitted, in the same order.
            int segmentation       = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int filterType         = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int filterLevel        = dboolhuff.vp8_decode_value(ref br, 6);
            int sharpnessLevel     = dboolhuff.vp8_decode_value(ref br, 3);
            int modeRefLfDelta     = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int log2NumPartitions  = dboolhuff.vp8_decode_value(ref br, 2);
            int baseQindex         = dboolhuff.vp8_decode_value(ref br, 7);
            // 5x put_delta_q. With all delta-q values zero, each one
            // emits a single 0 bit (no delta).
            int dqY1Dc   = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int dqY2Dc   = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int dqY2Ac   = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int dqUvDc   = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int dqUvAc   = dboolhuff.vp8dx_decode_bool(ref br, 128);
            // Inter-only fields.
            int refreshGolden      = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int refreshAltRef      = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int copyBufferToGf     = dboolhuff.vp8_decode_value(ref br, 2);   // refreshGolden==0 -> read 2 bits
            int copyBufferToArf    = dboolhuff.vp8_decode_value(ref br, 2);   // refreshAltRef==0 -> read 2 bits
            int signBiasGolden     = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int signBiasAltRef     = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int refreshEntropy     = dboolhuff.vp8dx_decode_bool(ref br, 128);
            int refreshLastFrame   = dboolhuff.vp8dx_decode_bool(ref br, 128);

            Assert.Equal(0,                  segmentation);
            Assert.Equal(cfg.FilterType,     filterType);
            Assert.Equal(cfg.FilterLevel,    filterLevel);
            Assert.Equal(cfg.SharpnessLevel, sharpnessLevel);
            Assert.Equal(0,                  modeRefLfDelta);
            Assert.Equal(cfg.Log2NumberOfTokenPartitions, log2NumPartitions);
            Assert.Equal(cfg.BaseQindex,     baseQindex);
            Assert.Equal(0,                  dqY1Dc);
            Assert.Equal(0,                  dqY2Dc);
            Assert.Equal(0,                  dqY2Ac);
            Assert.Equal(0,                  dqUvDc);
            Assert.Equal(0,                  dqUvAc);
            Assert.Equal(0,                  refreshGolden);
            Assert.Equal(0,                  refreshAltRef);
            Assert.Equal(0,                  copyBufferToGf);
            Assert.Equal(0,                  copyBufferToArf);
            Assert.Equal(0,                  signBiasGolden);
            Assert.Equal(0,                  signBiasAltRef);
            Assert.Equal(1,                  refreshEntropy);
            Assert.Equal(1,                  refreshLastFrame);
        }

        [Fact]
        public void StartInterFrameHeader_WithDeltaQ_RoundTrips()
        {
            // Negative Y1DC delta + positive Y2AC delta: exercises the
            // put_delta_q sign+magnitude path (1 flag bit, then 4-bit
            // magnitude + 1 sign bit when non-zero).
            var cfg = new InterFrameHeaderConfig
            {
                BaseQindex = 48,
                Y1DcDeltaQ = -3,
                Y2AcDeltaQ = 5,
            };

            byte[] buf = new byte[256];
            BOOL_CODER bc = default;
            int total;
            fixed (byte* p = buf)
            {
                bitstream.StartInterFrameHeader(p, buf.Length, cfg, ref bc);
                total = bitstream.FinishInterFrameFirstPartition(p, cfg, ref bc);
            }

            // Frame tag should still be valid.
            int v = buf[0] | (buf[1] << 8) | (buf[2] << 16);
            int keyFrameFlag = v & 0x1;
            int firstPartitionLength = v >> 5;
            Assert.Equal(1, keyFrameFlag);
            Assert.Equal(total - 3, firstPartitionLength);

            // Decode the delta-q fields and verify they recover.
            int boolStart = bitstream.INTER_FRAME_PREFIX_BYTES;
            byte[] partition = new byte[total - boolStart + 8];
            System.Array.Copy(buf, boolStart, partition, 0, total - boolStart);

            BOOL_DECODER br = new BOOL_DECODER();
            dboolhuff.vp8dx_start_decode(ref br, partition, (uint)partition.Length);

            // Skip past the fields before delta-q.
            dboolhuff.vp8dx_decode_bool(ref br, 128);            // segmentation
            dboolhuff.vp8dx_decode_bool(ref br, 128);            // filterType
            dboolhuff.vp8_decode_value(ref br, 6);               // filterLevel
            dboolhuff.vp8_decode_value(ref br, 3);               // sharpness
            dboolhuff.vp8dx_decode_bool(ref br, 128);            // modeRefLfDelta
            dboolhuff.vp8_decode_value(ref br, 2);               // log2partitions
            dboolhuff.vp8_decode_value(ref br, 7);               // baseQindex

            // Delta-q fields. Each is: 1 bit "is non-zero?" then
            // (if non-zero) 4-bit magnitude + 1 bit sign.
            int y1dc = ReadDeltaQ(ref br);
            int y2dc = ReadDeltaQ(ref br);
            int y2ac = ReadDeltaQ(ref br);
            int uvdc = ReadDeltaQ(ref br);
            int uvac = ReadDeltaQ(ref br);

            Assert.Equal(cfg.Y1DcDeltaQ, y1dc);
            Assert.Equal(cfg.Y2DcDeltaQ, y2dc);
            Assert.Equal(cfg.Y2AcDeltaQ, y2ac);
            Assert.Equal(cfg.UvDcDeltaQ, uvdc);
            Assert.Equal(cfg.UvAcDeltaQ, uvac);
        }

        private static int ReadDeltaQ(ref BOOL_DECODER br)
        {
            int hasDelta = dboolhuff.vp8dx_decode_bool(ref br, 128);
            if (hasDelta == 0) { return 0; }
            int magnitude = dboolhuff.vp8_decode_value(ref br, 4);
            int sign      = dboolhuff.vp8dx_decode_bool(ref br, 128);
            return sign != 0 ? -magnitude : magnitude;
        }

        // ---------- ValidateConfig ----------

        [Fact]
        public void StartInterFrameHeader_BaseQindexOutOfRangeThrows()
        {
            var cfg = new InterFrameHeaderConfig { BaseQindex = 200 };
            byte[] buf = new byte[256];
            BOOL_CODER bc = default;
            fixed (byte* p = buf)
            {
                byte* pp = p;
                Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                    bitstream.StartInterFrameHeader(pp, buf.Length, cfg, ref bc));
            }
        }

        [Fact]
        public void StartInterFrameHeader_DeltaQOutOfRangeThrows()
        {
            var cfg = new InterFrameHeaderConfig { BaseQindex = 32, Y1DcDeltaQ = 50 };
            byte[] buf = new byte[256];
            BOOL_CODER bc = default;
            fixed (byte* p = buf)
            {
                byte* pp = p;
                Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                    bitstream.StartInterFrameHeader(pp, buf.Length, cfg, ref bc));
            }
        }
    }
}
