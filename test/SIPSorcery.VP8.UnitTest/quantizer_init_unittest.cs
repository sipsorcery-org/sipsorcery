//-----------------------------------------------------------------------------
// Filename: quantizer_init_unittest.cs
//
// Description: Unit tests for the VP8 quantizer-table builder (encoder side,
// quantizer_init.cs). Cross-checks each of the six 16-element tables
// (zbin, round, quant, quant_shift, dequant, zrun_zbin_boost) bit-exactly
// against libvpx's vp8cx_init_quantizer for every block type (Y1, Y2, UV).
//
// Reference output captured from a standalone build of libvpx's
// vp8cx_init_quantizer + invert_quant for two representative Q indices
// (32 = mid quality, 100 = low quality). Any divergence indicates a bug
// in the C# port.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public class quantizer_init_unittest
    {
        // ------- libvpx reference values for QIndex = 32, all deltas = 0 -------

        private static readonly short[] s_q32_y1_zbin   = { 19,24,24,24,24,24,24,24, 24,24,24,24,24,24,24,24 };
        private static readonly short[] s_q32_y1_round  = { 10,13,13,13,13,13,13,13, 13,13,13,13,13,13,13,13 };
        private static readonly short[] s_q32_y1_quant  = { -29378,-7281,-7281,-7281,-7281,-7281,-7281,-7281, -7281,-7281,-7281,-7281,-7281,-7281,-7281,-7281 };
        private static readonly short[] s_q32_y1_qshift = { 4096,2048,2048,2048,2048,2048,2048,2048, 2048,2048,2048,2048,2048,2048,2048,2048 };
        private static readonly short[] s_q32_y1_dequant= { 29,36,36,36,36,36,36,36, 36,36,36,36,36,36,36,36 };
        private static readonly short[] s_q32_y1_zrun   = { 0,0,2,2,3,3,4,5, 6,7,9,10,11,12,12,12 };

        private static readonly short[] s_q32_y2_zbin   = { 38,36,36,36,36,36,36,36, 36,36,36,36,36,36,36,36 };
        private static readonly short[] s_q32_y2_round  = { 21,20,20,20,20,20,20,20, 20,20,20,20,20,20,20,20 };
        private static readonly short[] s_q32_y2_quant  = { -29378,-27405,-27405,-27405,-27405,-27405,-27405,-27405, -27405,-27405,-27405,-27405,-27405,-27405,-27405,-27405 };
        private static readonly short[] s_q32_y2_qshift = { 2048,2048,2048,2048,2048,2048,2048,2048, 2048,2048,2048,2048,2048,2048,2048,2048 };
        private static readonly short[] s_q32_y2_dequant= { 58,55,55,55,55,55,55,55, 55,55,55,55,55,55,55,55 };
        private static readonly short[] s_q32_y2_zrun   = { 0,0,3,4,5,6,6,8, 10,12,13,15,17,18,18,18 };

        private static readonly short[] s_q32_uv_zbin   = { 19,24,24,24,24,24,24,24, 24,24,24,24,24,24,24,24 };
        private static readonly short[] s_q32_uv_round  = { 10,13,13,13,13,13,13,13, 13,13,13,13,13,13,13,13 };
        private static readonly short[] s_q32_uv_quant  = { -29378,-7281,-7281,-7281,-7281,-7281,-7281,-7281, -7281,-7281,-7281,-7281,-7281,-7281,-7281,-7281 };
        private static readonly short[] s_q32_uv_qshift = { 4096,2048,2048,2048,2048,2048,2048,2048, 2048,2048,2048,2048,2048,2048,2048,2048 };
        private static readonly short[] s_q32_uv_dequant= { 29,36,36,36,36,36,36,36, 36,36,36,36,36,36,36,36 };
        private static readonly short[] s_q32_uv_zrun   = { 0,0,2,2,3,3,4,5, 6,7,9,10,11,12,12,12 };

        // ------- libvpx reference values for QIndex = 100, all deltas = 0 -------

        private static readonly short[] s_q100_y1_zbin   = { 61,104,104,104,104,104,104,104, 104,104,104,104,104,104,104,104 };
        private static readonly short[] s_q100_y1_round  = { 36,62,62,62,62,62,62,62, 62,62,62,62,62,62,62,62 };
        private static readonly short[] s_q100_y1_quant  = { -22736,-15304,-15304,-15304,-15304,-15304,-15304,-15304, -15304,-15304,-15304,-15304,-15304,-15304,-15304,-15304 };
        private static readonly short[] s_q100_y1_qshift = { 1024,512,512,512,512,512,512,512, 512,512,512,512,512,512,512,512 };
        private static readonly short[] s_q100_y1_dequant= { 98,167,167,167,167,167,167,167, 167,167,167,167,167,167,167,167 };
        private static readonly short[] s_q100_y1_zrun   = { 0,0,10,13,15,18,20,26, 31,36,41,46,52,57,57,57 };

        private static readonly short[] s_q100_y2_zbin   = { 123,161,161,161,161,161,161,161, 161,161,161,161,161,161,161,161 };
        private static readonly short[] s_q100_y2_round  = { 73,96,96,96,96,96,96,96, 96,96,96,96,96,96,96,96 };
        private static readonly short[] s_q100_y2_quant  = { -22736,-508,-508,-508,-508,-508,-508,-508, -508,-508,-508,-508,-508,-508,-508,-508 };
        private static readonly short[] s_q100_y2_qshift = { 512,256,256,256,256,256,256,256, 256,256,256,256,256,256,256,256 };
        private static readonly short[] s_q100_y2_dequant= { 196,258,258,258,258,258,258,258, 258,258,258,258,258,258,258,258 };
        private static readonly short[] s_q100_y2_zrun   = { 0,0,16,20,24,28,32,40, 48,56,64,72,80,88,88,88 };

        private static readonly short[] s_q100_uv_zbin   = { 61,104,104,104,104,104,104,104, 104,104,104,104,104,104,104,104 };
        private static readonly short[] s_q100_uv_round  = { 36,62,62,62,62,62,62,62, 62,62,62,62,62,62,62,62 };
        private static readonly short[] s_q100_uv_quant  = { -22736,-15304,-15304,-15304,-15304,-15304,-15304,-15304, -15304,-15304,-15304,-15304,-15304,-15304,-15304,-15304 };
        private static readonly short[] s_q100_uv_qshift = { 1024,512,512,512,512,512,512,512, 512,512,512,512,512,512,512,512 };
        private static readonly short[] s_q100_uv_dequant= { 98,167,167,167,167,167,167,167, 167,167,167,167,167,167,167,167 };
        private static readonly short[] s_q100_uv_zrun   = { 0,0,10,13,15,18,20,26, 31,36,41,46,52,57,57,57 };

        // ---------- bit-exact reference checks ----------

        [Fact]
        public void BuildForQIndex_32_Y1_MatchesLibvpxReference()
        {
            var q = quantizer_init.BuildForQIndex(32);
            AssertTablesMatch("Y1@Q32", q.Y1, s_q32_y1_zbin, s_q32_y1_round, s_q32_y1_quant, s_q32_y1_qshift, s_q32_y1_dequant, s_q32_y1_zrun);
        }

        [Fact]
        public void BuildForQIndex_32_Y2_MatchesLibvpxReference()
        {
            var q = quantizer_init.BuildForQIndex(32);
            AssertTablesMatch("Y2@Q32", q.Y2, s_q32_y2_zbin, s_q32_y2_round, s_q32_y2_quant, s_q32_y2_qshift, s_q32_y2_dequant, s_q32_y2_zrun);
        }

        [Fact]
        public void BuildForQIndex_32_UV_MatchesLibvpxReference()
        {
            var q = quantizer_init.BuildForQIndex(32);
            AssertTablesMatch("UV@Q32", q.UV, s_q32_uv_zbin, s_q32_uv_round, s_q32_uv_quant, s_q32_uv_qshift, s_q32_uv_dequant, s_q32_uv_zrun);
        }

        [Fact]
        public void BuildForQIndex_100_Y1_MatchesLibvpxReference()
        {
            var q = quantizer_init.BuildForQIndex(100);
            AssertTablesMatch("Y1@Q100", q.Y1, s_q100_y1_zbin, s_q100_y1_round, s_q100_y1_quant, s_q100_y1_qshift, s_q100_y1_dequant, s_q100_y1_zrun);
        }

        [Fact]
        public void BuildForQIndex_100_Y2_MatchesLibvpxReference()
        {
            var q = quantizer_init.BuildForQIndex(100);
            AssertTablesMatch("Y2@Q100", q.Y2, s_q100_y2_zbin, s_q100_y2_round, s_q100_y2_quant, s_q100_y2_qshift, s_q100_y2_dequant, s_q100_y2_zrun);
        }

        [Fact]
        public void BuildForQIndex_100_UV_MatchesLibvpxReference()
        {
            var q = quantizer_init.BuildForQIndex(100);
            AssertTablesMatch("UV@Q100", q.UV, s_q100_uv_zbin, s_q100_uv_round, s_q100_uv_quant, s_q100_uv_qshift, s_q100_uv_dequant, s_q100_uv_zrun);
        }

        // ---------- integration check: produced tables drive the existing quantize ----------

        /// <summary>
        /// The whole point of these tables is that they feed
        /// quantize.vp8_regular_quantize_b_arrays. This test verifies that
        /// when we plug the tables built for Q=32 into the quantizer with a
        /// known coefficient block, the output qcoeff and dqcoeff are
        /// numerically consistent (every dqcoeff[i] = qcoeff[i] * dequant[i],
        /// and the quantize-then-dequantize round-trip is bounded by the
        /// quantization step).
        /// </summary>
        [Fact]
        public unsafe void Tables_DriveQuantizerCorrectly_ForKnownCoeffBlock()
        {
            var q = quantizer_init.BuildForQIndex(32);
            // Use Y1 for an AC-style block.
            var t = q.Y1;

            // A representative coefficient block (results of an fdct).
            short[] coeff = { 256, 1, 0, 0,  18, 0, -32, 0,  0, 0, 0, 0,  0, 0, 0, 0 };
            short[] qcoeff = new short[16];
            short[] dqcoeff = new short[16];

            int eob;
            fixed (short* coefP = coeff)
            fixed (short* zbinP = t.zbin)
            fixed (short* zboostP = t.zrun_zbin_boost)
            fixed (short* roundP = t.round)
            fixed (short* quantP = t.quant)
            fixed (short* quantShiftP = t.quant_shift)
            fixed (short* dequantP = t.dequant)
            fixed (short* qcoeffP = qcoeff)
            fixed (short* dqcoeffP = dqcoeff)
            {
                eob = quantize.vp8_regular_quantize_b_arrays(
                    coefP, zbinP, zboostP, roundP, quantP, quantShiftP, dequantP,
                    qcoeffP, dqcoeffP, zbin_extra: 0);
            }

            // Internal consistency: every dqcoeff[i] must equal qcoeff[i] * dequant[i].
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal(qcoeff[i] * t.dequant[i], dqcoeff[i]);
            }

            // The DC coefficient (256) is well above zbin[0] (=19) so it
            // must round-trip to within one dequantizer step.
            Assert.True(System.Math.Abs(dqcoeff[0] - coeff[0]) <= t.dequant[0],
                "DC reconstruction exceeded one quantizer step");
            // Block must have at least one non-zero coefficient.
            Assert.True(eob > 0);
        }

        // ---------- helper ----------

        private static void AssertTablesMatch(string label, QuantizerTables t,
            short[] zbin, short[] round, short[] quant, short[] qshift, short[] dequant, short[] zrun)
        {
            AssertArr($"{label}.zbin", zbin, t.zbin);
            AssertArr($"{label}.round", round, t.round);
            AssertArr($"{label}.quant", quant, t.quant);
            AssertArr($"{label}.quant_shift", qshift, t.quant_shift);
            AssertArr($"{label}.dequant", dequant, t.dequant);
            AssertArr($"{label}.zrun_zbin_boost", zrun, t.zrun_zbin_boost);
        }

        private static void AssertArr(string name, short[] expected, short[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i] == actual[i],
                    $"{name}[{i}]: expected {expected[i]} got {actual[i]}");
            }
        }
    }
}
