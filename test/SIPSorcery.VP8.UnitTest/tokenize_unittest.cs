//-----------------------------------------------------------------------------
// Filename: tokenize_unittest.cs
//
// Description: Unit tests for the VP8 coefficient tokenizer (encoder side,
// src/tokenize.cs). Cross-checks vp8_tokenize_block bit-exactly against
// libvpx's tokenize1st_order_b inner loop with the same inputs, and
// verifies the dct_value_tokens lookup table built at static init by the
// fill_value_tokens algorithm port matches libvpx's reference output for
// every category boundary.
//
// Reference output captured from a standalone build of libvpx's
// fill_value_tokens + the inner loop of tokenize1st_order_b. Any
// divergence indicates a bug in the C# port.
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

using System.Collections.Generic;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class tokenize_unittest
    {
        // ---------- dct_value_tokens lookup table ----------

        /// <summary>
        /// Bit-exact reference test: every coefficient value across every
        /// token-category boundary must produce the exact (Token, Extra)
        /// pair libvpx's fill_value_tokens generates.
        /// </summary>
        [Theory]
        [InlineData(-2048, 10, 3963)]
        [InlineData(  -67, 10, 1)]
        [InlineData(  -35,  9, 1)]
        [InlineData(  -19,  8, 1)]
        [InlineData(  -11,  7, 1)]
        [InlineData(   -7,  6, 1)]
        [InlineData(   -5,  5, 1)]
        [InlineData(   -4,  4, 1)]
        [InlineData(   -3,  3, 1)]
        [InlineData(   -2,  2, 1)]
        [InlineData(   -1,  1, 1)]
        [InlineData(    0,  0, 0)]
        [InlineData(    1,  1, 0)]
        [InlineData(    2,  2, 0)]
        [InlineData(    3,  3, 0)]
        [InlineData(    4,  4, 0)]
        [InlineData(    5,  5, 0)]
        [InlineData(    7,  6, 0)]
        [InlineData(   11,  7, 0)]
        [InlineData(   19,  8, 0)]
        [InlineData(   35,  9, 0)]
        [InlineData(   67, 10, 0)]
        [InlineData( 2047, 10, 3960)]
        public void DctValueTokens_Lookup_MatchesLibvpxReference(int v, int expectedToken, int expectedExtra)
        {
            var tv = dct_value_tokens.Lookup(v);
            Assert.Equal(expectedToken, tv.Token);
            Assert.Equal(expectedExtra, tv.Extra);
        }

        // ---------- entropy.cs token table sanity ----------

        [Fact]
        public void Entropy_VP8CoefBands_HasExpectedShape()
        {
            byte[] expected = { 0,1,2,3,6,4,5,6, 6,6,6,6,6,6,6,7 };
            Assert.Equal(expected, entropy.vp8_coef_bands);
        }

        [Fact]
        public void Entropy_VP8PrevTokenClass_HasExpectedShape()
        {
            byte[] expected = { 0,1,2,2,2,2,2,2,2,2,2,0 };
            Assert.Equal(expected, entropy.vp8_prev_token_class);
        }

        [Fact]
        public void Entropy_VP8ExtraBits_BaseValuesMatchLibvpx()
        {
            int[] expectedBaseVals = { 0, 1, 2, 3, 4, 5, 7, 11, 19, 35, 67, 0 };
            for (int i = 0; i < 12; i++)
            {
                Assert.Equal(expectedBaseVals[i], entropy.vp8_extra_bits[i].base_val);
            }
            // Spot check: cat3 prob array
            Assert.Equal(new byte[] { 173, 148, 140 }, entropy.vp8_extra_bits[entropy.DCT_VAL_CATEGORY3].prob);
            Assert.Equal(3, entropy.vp8_extra_bits[entropy.DCT_VAL_CATEGORY3].Len);
        }

        // ---------- tokenize_block bit-exact reference tests ----------

        /// <summary>
        /// All-zero block (eob=0): the tokenizer should emit a single
        /// DCT_EOB_TOKEN and report no non-zero coefficients.
        /// </summary>
        [Fact]
        public void TokenizeBlock_AllZero_EmitsSingleEob()
        {
            var tokens = new List<TOKENEXTRA>();
            bool nonzero = tokenize.vp8_tokenize_block(
                new short[16], firstCoeffIndex: 0, eob: 0,
                blockType: 3, initialContext: 0,
                coefProbs: default_coef_probs_c.default_coef_probs,
                output: tokens);

            Assert.False(nonzero);
            AssertTokens(tokens, (entropy.DCT_EOB_TOKEN, 0, 0));
        }

        /// <summary>
        /// DC-only block: qcoeff[0] = 5, eob = 1.
        /// Reference: t=5, then t=11 (EOB).
        /// </summary>
        [Fact]
        public void TokenizeBlock_DcOnly_5_MatchesReference()
        {
            short[] q = new short[16]; q[0] = 5;
            var tokens = new List<TOKENEXTRA>();
            bool nonzero = tokenize.vp8_tokenize_block(
                q, firstCoeffIndex: 0, eob: 1,
                blockType: 3, initialContext: 0,
                coefProbs: default_coef_probs_c.default_coef_probs,
                output: tokens);

            Assert.True(nonzero);
            AssertTokens(tokens,
                (entropy.DCT_VAL_CATEGORY1, 0, 0),
                (entropy.DCT_EOB_TOKEN, 0, 0));
        }

        /// <summary>
        /// DC + zz[1]=2: qcoeff[0]=5, qcoeff[1]=2, eob=2.
        /// Reference: (t=5, e=0, s=0), (t=2, e=0, s=0), (t=11, e=0, s=0).
        /// </summary>
        [Fact]
        public void TokenizeBlock_DcAndOneAc_MatchesReference()
        {
            short[] q = new short[16]; q[0] = 5; q[1] = 2;
            var tokens = new List<TOKENEXTRA>();
            bool nonzero = tokenize.vp8_tokenize_block(
                q, firstCoeffIndex: 0, eob: 2,
                blockType: 3, initialContext: 0,
                coefProbs: default_coef_probs_c.default_coef_probs,
                output: tokens);

            Assert.True(nonzero);
            AssertTokens(tokens,
                (entropy.DCT_VAL_CATEGORY1, 0, 0),
                (entropy.TWO_TOKEN, 0, 0),
                (entropy.DCT_EOB_TOKEN, 0, 0));
        }

        /// <summary>
        /// Scattered coefficients exercising the zigzag scan, zero-runs, and
        /// the skip_eob_node bit. Inputs (raster order):
        ///   q[0]=1, q[1]=0, q[4]=0, q[8]=-3, q[5]=0, q[2]=2, q[3]=0, q[6]=-1
        /// (zigzag positions 0..7), eob=8.
        ///
        /// Reference stream:
        ///   (t=1, e=0, s=0)  (t=0, e=0, s=0)  (t=0, e=0, s=1)
        ///   (t=3, e=1, s=1)  (t=0, e=0, s=0)  (t=2, e=0, s=1)
        ///   (t=0, e=0, s=0)  (t=1, e=1, s=1)  (t=11, e=0, s=0)
        /// </summary>
        [Fact]
        public void TokenizeBlock_Scattered_MatchesReference()
        {
            short[] q = new short[16];
            q[0] = 1; q[1] = 0; q[4] = 0; q[8] = -3; q[5] = 0; q[2] = 2; q[3] = 0; q[6] = -1;
            var tokens = new List<TOKENEXTRA>();
            bool nonzero = tokenize.vp8_tokenize_block(
                q, firstCoeffIndex: 0, eob: 8,
                blockType: 3, initialContext: 0,
                coefProbs: default_coef_probs_c.default_coef_probs,
                output: tokens);

            Assert.True(nonzero);
            AssertTokens(tokens,
                (entropy.ONE_TOKEN, 0, 0),
                (entropy.ZERO_TOKEN, 0, 0),
                (entropy.ZERO_TOKEN, 0, 1),
                (entropy.THREE_TOKEN, 1, 1),
                (entropy.ZERO_TOKEN, 0, 0),
                (entropy.TWO_TOKEN, 0, 1),
                (entropy.ZERO_TOKEN, 0, 0),
                (entropy.ONE_TOKEN, 1, 1),
                (entropy.DCT_EOB_TOKEN, 0, 0));
        }

        /// <summary>
        /// Y-no-DC block (firstCoeffIndex = 1): qcoeff[1] = 2, eob = 2.
        /// </summary>
        [Fact]
        public void TokenizeBlock_YNoDc_OneAcCoefficient_MatchesReference()
        {
            short[] q = new short[16]; q[1] = 2;
            var tokens = new List<TOKENEXTRA>();
            bool nonzero = tokenize.vp8_tokenize_block(
                q, firstCoeffIndex: 1, eob: 2,
                blockType: 0, initialContext: 0,
                coefProbs: default_coef_probs_c.default_coef_probs,
                output: tokens);

            Assert.True(nonzero);
            AssertTokens(tokens,
                (entropy.TWO_TOKEN, 0, 0),
                (entropy.DCT_EOB_TOKEN, 0, 0));
        }

        /// <summary>
        /// Large coefficients triggering category tokens. q[0]=25, q[1]=-8.
        /// 25 -> CAT4 with extra = (25-19)<<1 = 12.
        /// -8 -> CAT2 with extra = ((8-7)<<1) | 1 (sign) = 3.
        /// </summary>
        [Fact]
        public void TokenizeBlock_LargeCoefs_CategoryAndSign_MatchesReference()
        {
            short[] q = new short[16]; q[0] = 25; q[1] = -8;
            var tokens = new List<TOKENEXTRA>();
            bool nonzero = tokenize.vp8_tokenize_block(
                q, firstCoeffIndex: 0, eob: 2,
                blockType: 3, initialContext: 0,
                coefProbs: default_coef_probs_c.default_coef_probs,
                output: tokens);

            Assert.True(nonzero);
            AssertTokens(tokens,
                (entropy.DCT_VAL_CATEGORY4, 12, 0),
                (entropy.DCT_VAL_CATEGORY2,  3, 0),
                (entropy.DCT_EOB_TOKEN,      0, 0));
        }

        // ---------- helpers ----------

        private static void AssertTokens(List<TOKENEXTRA> got, params (int token, int extra, int skipEob)[] expected)
        {
            Assert.Equal(expected.Length, got.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var e = expected[i];
                Assert.True(e.token == got[i].Token,
                    "[" + i + "] Token: expected " + e.token + " got " + got[i].Token);
                Assert.True(e.extra == got[i].Extra,
                    "[" + i + "] Extra: expected " + e.extra + " got " + got[i].Extra);
                Assert.True(e.skipEob == got[i].skip_eob_node,
                    "[" + i + "] skip_eob_node: expected " + e.skipEob + " got " + got[i].skip_eob_node);
            }
        }
    }
}
