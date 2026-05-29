//-----------------------------------------------------------------------------
// Filename: inter_mb_mode_bits_unittest.cs
//
// Description: Unit tests for the per-MB inter mode + reference frame bit
// writers added in PR 4 of the VP8 P-frame foundation series. These cover:
//
//   - vp8_treed_write: round-trip test against vp8_treed_read for the
//     vp8_mv_ref_tree (one path per inter mode).
//   - WriteInterMbAsIntra: writes is_inter=0.
//   - WriteInterMbRefAndMode: writes is_inter=1, the ref-frame bits, and
//     the inter-mode tree path. Decoded back through the existing C#
//     decoder primitives the bits round-trip to the same ref_frame/mode.
//   - WriteInterMbZeroMvLast: convenience wrapper for ZEROMV+LAST round
//     trips correctly with any plausible (probIntra, probLast, probGf)
//     triple.
//
// The round-trip approach (vp8dx_decode_bool + vp8_treed_read) is the
// same proof-of-correctness pattern used by the keyframe header tests
// (bitstream_unittest.cs) and inter_frame_header_unittest.cs.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 28 Apr 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class inter_mb_mode_bits_unittest
    {
        // ---------- Helpers ----------

        /// <summary>Initialises a bool coder over a fresh 64-byte buffer.</summary>
        private static byte[] NewCoderBuffer(out BOOL_CODER bc)
        {
            byte[] buf = new byte[64];
            bc = default;
            // boolhuff.vp8_start_encode requires a byte[] overload; use that.
            boolhuff.vp8_start_encode(ref bc, buf, buf.Length);
            return buf;
        }

        /// <summary>Closes a bool coder and returns the produced bytes as a
        /// trimmed-and-padded array suitable for vp8dx_start_decode.</summary>
        private static byte[] FinishAndCopy(ref BOOL_CODER bc, byte[] buf)
        {
            boolhuff.vp8_stop_encode(ref bc);
            int n = (int)bc.pos;
            byte[] copy = new byte[n + 16];   // padding for decoder lookahead
            System.Array.Copy(buf, 0, copy, 0, n);
            return copy;
        }

        // ---------- vp8_treed_write round-trip ----------

        public static System.Collections.Generic.IEnumerable<object[]> InterModes()
        {
            yield return new object[] { MB_PREDICTION_MODE.ZEROMV };
            yield return new object[] { MB_PREDICTION_MODE.NEARESTMV };
            yield return new object[] { MB_PREDICTION_MODE.NEARMV };
            yield return new object[] { MB_PREDICTION_MODE.NEWMV };
            yield return new object[] { MB_PREDICTION_MODE.SPLITMV };
        }

        [Theory]
        [MemberData(nameof(InterModes))]
        public void TreedWrite_VpMvRefTree_RoundTripsForEveryInterMode(MB_PREDICTION_MODE mode)
        {
            // Use row 0 of vp8_mode_contexts as the prob row.
            byte[] probs = new byte[4];
            for (int j = 0; j < 4; j++) probs[j] = (byte)modecont.vp8_mode_contexts[0, j];

            byte[] buf = NewCoderBuffer(out BOOL_CODER bc);
            bitstream.WriteInterMode(ref bc, probs, mode);
            byte[] partition = FinishAndCopy(ref bc, buf);

            BOOL_DECODER br = new BOOL_DECODER();
            dboolhuff.vp8dx_start_decode(ref br, partition, (uint)partition.Length);

            int decoded = treereader.vp8_treed_read(ref br, entropymode.vp8_mv_ref_tree, probs);
            Assert.Equal((int)mode, decoded);
        }

        // ---------- WriteInterMbAsIntra ----------

        [Fact]
        public void WriteInterMbAsIntra_EmitsZeroBitWithProbIntra()
        {
            const int probIntra = 60;
            byte[] buf = NewCoderBuffer(out BOOL_CODER bc);
            bitstream.WriteInterMbAsIntra(ref bc, probIntra);
            byte[] partition = FinishAndCopy(ref bc, buf);

            BOOL_DECODER br = new BOOL_DECODER();
            dboolhuff.vp8dx_start_decode(ref br, partition, (uint)partition.Length);

            int isInter = dboolhuff.vp8dx_decode_bool(ref br, probIntra);
            Assert.Equal(0, isInter);
        }

        // ---------- WriteInterMbRefAndMode round-trip ----------

        public static System.Collections.Generic.IEnumerable<object[]> RefMode()
        {
            // Cover all three inter ref frames and all five modes.
            foreach (var rf in new[] {
                MV_REFERENCE_FRAME.LAST_FRAME,
                MV_REFERENCE_FRAME.GOLDEN_FRAME,
                MV_REFERENCE_FRAME.ALTREF_FRAME })
            {
                foreach (var mode in new[] {
                    MB_PREDICTION_MODE.ZEROMV,
                    MB_PREDICTION_MODE.NEARESTMV,
                    MB_PREDICTION_MODE.NEARMV,
                    MB_PREDICTION_MODE.NEWMV,
                    MB_PREDICTION_MODE.SPLITMV })
                {
                    yield return new object[] { rf, mode };
                }
            }
        }

        [Theory]
        [MemberData(nameof(RefMode))]
        public void WriteInterMbRefAndMode_RoundTripsForEveryRefAndMode(
            MV_REFERENCE_FRAME refFrame,
            MB_PREDICTION_MODE mode)
        {
            // Plausible prob values straight from the libvpx defaults.
            const int probIntra = 63;   // vp8_kf_default_y_mode_probs would be 145 -- arbitrary here.
            const int probLast  = 255;
            const int probGf    = 128;

            byte[] modeProbs = new byte[4];
            for (int j = 0; j < 4; j++) modeProbs[j] = (byte)modecont.vp8_mode_contexts[0, j];

            byte[] buf = NewCoderBuffer(out BOOL_CODER bc);
            bitstream.WriteInterMbRefAndMode(
                ref bc, probIntra, probLast, probGf,
                refFrame, modeProbs, mode);
            byte[] partition = FinishAndCopy(ref bc, buf);

            BOOL_DECODER br = new BOOL_DECODER();
            dboolhuff.vp8dx_start_decode(ref br, partition, (uint)partition.Length);

            // Mirror decodemv.read_mb_modes_mv ref-frame parsing.
            int isInter = dboolhuff.vp8dx_decode_bool(ref br, probIntra);
            Assert.Equal(1, isInter);

            int notLast = dboolhuff.vp8dx_decode_bool(ref br, probLast);
            int decodedRef;
            if (notLast == 0)
            {
                decodedRef = (int)MV_REFERENCE_FRAME.LAST_FRAME;
            }
            else
            {
                int isAlt = dboolhuff.vp8dx_decode_bool(ref br, probGf);
                decodedRef = 2 + isAlt;
            }
            Assert.Equal((int)refFrame, decodedRef);

            int decodedMode = treereader.vp8_treed_read(
                ref br, entropymode.vp8_mv_ref_tree, modeProbs);
            Assert.Equal((int)mode, decodedMode);
        }

        [Fact]
        public void WriteInterMbRefAndMode_ThrowsForIntraRefFrame()
        {
            byte[] buf = new byte[64];
            BOOL_CODER bc = default;
            boolhuff.vp8_start_encode(ref bc, buf, buf.Length);
            byte[] modeProbs = new byte[4] { 7, 1, 1, 143 };

            Assert.Throws<System.ArgumentException>(() =>
                bitstream.WriteInterMbRefAndMode(
                    ref bc, 63, 255, 128,
                    MV_REFERENCE_FRAME.INTRA_FRAME,
                    modeProbs,
                    MB_PREDICTION_MODE.ZEROMV));
        }

        // ---------- WriteInterMbZeroMvLast convenience wrapper ----------

        [Theory]
        [InlineData(63, 255, 128)]
        [InlineData(120, 200, 100)]
        [InlineData(1, 1, 1)]
        [InlineData(255, 255, 255)]
        public void WriteInterMbZeroMvLast_RoundTripsToZeroMvLast(int probIntra, int probLast, int probGf)
        {
            byte[] buf = NewCoderBuffer(out BOOL_CODER bc);
            bitstream.WriteInterMbZeroMvLast(ref bc, probIntra, probLast, probGf);
            byte[] partition = FinishAndCopy(ref bc, buf);

            // The convenience wrapper uses row 0 of vp8_mode_contexts. Build
            // the same row for the decoder.
            byte[] modeProbs = new byte[4];
            for (int j = 0; j < 4; j++) modeProbs[j] = (byte)modecont.vp8_mode_contexts[0, j];

            BOOL_DECODER br = new BOOL_DECODER();
            dboolhuff.vp8dx_start_decode(ref br, partition, (uint)partition.Length);

            int isInter = dboolhuff.vp8dx_decode_bool(ref br, probIntra);
            Assert.Equal(1, isInter);

            int notLast = dboolhuff.vp8dx_decode_bool(ref br, probLast);
            Assert.Equal(0, notLast);   // LAST -> 0

            int decodedMode = treereader.vp8_treed_read(
                ref br, entropymode.vp8_mv_ref_tree, modeProbs);
            Assert.Equal((int)MB_PREDICTION_MODE.ZEROMV, decodedMode);
        }

        // ---------- vp8_treed_write byte-shape regression ----------

        /// <summary>
        /// Walking ZEROMV through vp8_treed_write writes a single bit at
        /// modeProbs[0]. Confirm by encoding the same value via
        /// vp8_encode_bool directly and verifying the byte sequences match.
        /// </summary>
        [Fact]
        public void TreedWrite_ZeroMv_MatchesDirectBoolEncode()
        {
            byte[] modeProbs = new byte[4] { 7, 1, 1, 143 };

            // Path A: vp8_treed_write to leaf ZEROMV (value=0, length=1).
            byte[] bufA = NewCoderBuffer(out BOOL_CODER bcA);
            bitstream.vp8_treed_write(ref bcA, entropymode.vp8_mv_ref_tree, modeProbs, 0, 1);
            byte[] outA = FinishAndCopy(ref bcA, bufA);

            // Path B: direct vp8_encode_bool(0, modeProbs[0]).
            byte[] bufB = NewCoderBuffer(out BOOL_CODER bcB);
            boolhuff.vp8_encode_bool(ref bcB, 0, modeProbs[0]);
            byte[] outB = FinishAndCopy(ref bcB, bufB);

            Assert.Equal(outB.Length, outA.Length);
            for (int i = 0; i < outA.Length; i++)
            {
                Assert.True(outA[i] == outB[i],
                    $"byte {i} mismatch: A=0x{outA[i].ToString("x2")} B=0x{outB[i].ToString("x2")}");
            }
        }

        /// <summary>
        /// Walking SPLITMV through vp8_treed_write writes four 1-bits at
        /// modeProbs[0..3]. Confirm by encoding the same four bits via
        /// vp8_encode_bool directly.
        /// </summary>
        [Fact]
        public void TreedWrite_SplitMv_MatchesDirectFourBitEncode()
        {
            byte[] modeProbs = new byte[4] { 7, 1, 1, 143 };

            byte[] bufA = NewCoderBuffer(out BOOL_CODER bcA);
            bitstream.vp8_treed_write(ref bcA, entropymode.vp8_mv_ref_tree, modeProbs, 15, 4);
            byte[] outA = FinishAndCopy(ref bcA, bufA);

            byte[] bufB = NewCoderBuffer(out BOOL_CODER bcB);
            boolhuff.vp8_encode_bool(ref bcB, 1, modeProbs[0]);
            boolhuff.vp8_encode_bool(ref bcB, 1, modeProbs[1]);
            boolhuff.vp8_encode_bool(ref bcB, 1, modeProbs[2]);
            boolhuff.vp8_encode_bool(ref bcB, 1, modeProbs[3]);
            byte[] outB = FinishAndCopy(ref bcB, bufB);

            Assert.Equal(outB.Length, outA.Length);
            for (int i = 0; i < outA.Length; i++)
            {
                Assert.True(outA[i] == outB[i],
                    $"byte {i} mismatch: A=0x{outA[i].ToString("x2")} B=0x{outB[i].ToString("x2")}");
            }
        }
    }
}
