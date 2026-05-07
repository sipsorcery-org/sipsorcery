//-----------------------------------------------------------------------------
// Filename: pack_tokens_unittest.cs
//
// Description: Unit tests for the VP8 token bitstream writer (encoder side,
// bitstream.vp8_pack_tokens). Cross-checks the C# implementation
// bit-exactly against libvpx's vp8_pack_tokens for representative token
// streams, and verifies an end-to-end round-trip: tokenize a coefficient
// block, pack the tokens into a bitstream, then walk the resulting bool
// stream back through the existing decoder primitives to recover every
// (Token, Extra) pair.
//
// Reference output captured from a standalone build of libvpx's
// vp8_pack_tokens. Any divergence indicates a bug in the C# port.
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

using System;
using System.Collections.Generic;
using Xunit;

using vp8_prob = System.Byte;

namespace Vpx.Net.UnitTest
{
    public unsafe class pack_tokens_unittest
    {
        // All-128 coef table: every row matches the flat 11×128 reference
        // captures. Any CoefProbRowIndex 0..95 is valid for these tests.
        private static readonly vp8_prob[,,,] s_all128Coef = CreateAll128CoefTable();

        private static vp8_prob[,,,] CreateAll128CoefTable()
        {
            var t = new vp8_prob[4, 8, 3, 11];
            for (int i = 0; i < t.GetLength(0); i++)
                for (int j = 0; j < t.GetLength(1); j++)
                    for (int k = 0; k < t.GetLength(2); k++)
                        for (int n = 0; n < t.GetLength(3); n++)
                            t[i, j, k, n] = 128;
            return t;
        }

        // ---------- Bit-exact reference checks against libvpx ----------

        /// <summary>
        /// Single EOB token (the all-zero-block case): should write a tiny
        /// bitstream that ends in a 2-byte flush. Reference: 0x00 0x00.
        /// </summary>
        [Fact]
        public void PackTokens_EobOnly_MatchesLibvpxReference()
        {
            byte[] expected = { 0x00, 0x00 };
            AssertPackedBytes(expected, new[] { Eob() }, s_all128Coef);
        }

        /// <summary>
        /// One non-zero coefficient (token 1, ONE_TOKEN, no extra) followed
        /// by EOB. Reference: 0xBF 0x80.
        /// </summary>
        [Fact]
        public void PackTokens_OneThenEob_MatchesLibvpxReference()
        {
            byte[] expected = { 0xBF, 0x80 };
            AssertPackedBytes(expected, new[] {
                Tok(entropy.ONE_TOKEN, extra: 0),
                Eob(),
            }, s_all128Coef);
        }

        /// <summary>
        /// Scattered coefficients matching the "Scattered" test vector from
        /// tokenize_unittest, with the skip_eob_node bit set as it would be
        /// after a ZERO_TOKEN. Some tokens carry an extra-bit sign (Extra=1).
        /// Reference: 0xC9 0x1A 0x2F 0x6B 0x00.
        /// </summary>
        [Fact]
        public void PackTokens_ScatteredWithSigns_MatchesLibvpxReference()
        {
            byte[] expected = { 0xC9, 0x1A, 0x2F, 0x6B, 0x00 };
            AssertPackedBytes(expected, new[] {
                Tok(entropy.ONE_TOKEN,    0, skip: 0),
                Tok(entropy.ZERO_TOKEN,   0, skip: 0),
                Tok(entropy.ZERO_TOKEN,   0, skip: 1),
                Tok(entropy.THREE_TOKEN,  1, skip: 1),
                Tok(entropy.ZERO_TOKEN,   0, skip: 0),
                Tok(entropy.TWO_TOKEN,    0, skip: 1),
                Tok(entropy.ZERO_TOKEN,   0, skip: 0),
                Tok(entropy.ONE_TOKEN,    1, skip: 1),
                Eob(),
            }, s_all128Coef);
        }

        /// <summary>
        /// Category tokens (CAT4 with magnitude 12 = (25-19)*2, CAT2 with
        /// magnitude+sign 3 = ((8-7)*2)|1) followed by EOB. Exercises the
        /// per-category extra-bit tree walk + sign bit. Reference:
        /// 0xFA 0x39 0x18 0xA0.
        /// </summary>
        [Fact]
        public void PackTokens_CategoryTokens_MatchesLibvpxReference()
        {
            byte[] expected = { 0xFA, 0x39, 0x18, 0xA0 };
            AssertPackedBytes(expected, new[] {
                Tok(entropy.DCT_VAL_CATEGORY4, extra: 12),
                Tok(entropy.DCT_VAL_CATEGORY2, extra: 3),
                Eob(),
            }, s_all128Coef);
        }

        // ---------- Round-trip: tokenize -> pack -> bool-decode -> token reconstruction ----------

        /// <summary>
        /// Full round-trip: tokenize a known coefficient block via
        /// tokenize.vp8_tokenize_block, pack the resulting TOKENEXTRA stream
        /// with vp8_pack_tokens, then walk the produced byte stream back
        /// through the existing decoder's bool reader (dboolhuff) and
        /// reconstruct every (Token, Extra) pair using the same vp8_coef_tree
        /// and per-category extra-bit trees the encoder used. The reconstructed
        /// stream must equal the encoder's input.
        /// </summary>
        [Fact]
        public void PackTokens_TokenizeAndDecode_RoundTrip_RecoversTokens()
        {
            // Inputs: same scattered block from tokenize_unittest.cs, but
            // the round-trip must hold for any block.
            short[] q = new short[16];
            q[0] = 1; q[1] = 0; q[4] = 0; q[8] = -3; q[5] = 0; q[2] = 2; q[3] = 0; q[6] = -1;

            var tokens = new TokenStreamBuffer();
            tokenize.vp8_tokenize_block(
                q, firstCoeffIndex: 0, eob: 8,
                blockType: 3, initialContext: 0,
                coefProbs: default_coef_probs_c.default_coef_probs,
                output: tokens);

            byte[] packed = PackToBytes(tokens.AsSpan(), default_coef_probs_c.default_coef_probs);

            var decoded = DecodeTokens(packed, tokens, default_coef_probs_c.default_coef_probs);
            Assert.Equal(tokens.Count, decoded.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                Assert.Equal(tokens[i].Token, decoded[i].Token);
                Assert.Equal(tokens[i].Extra, decoded[i].Extra);
            }
        }

        /// <summary>
        /// GetCoefProbRowForPack uses a per-thread RowsFor cache; concurrent
        /// threads with different coef tables must not see cross-wired rows.
        /// </summary>
        [Fact]
        public void GetCoefProbRowForPack_TwoThreadsDistinctTables_RowMatchesTable()
        {
            var tableA = CreateAll128CoefTable();
            var tableB = CreateAll128CoefTable();
            tableB[0, 0, 0, 0] = 42;

            const byte rowIndex = 0;
            const int iterations = 8000;
            object gate = new object();
            Exception first = null;

            void Work(vp8_prob[,,,] t)
            {
                byte expect = t[0, 0, 0, 0];
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var row = tokenize.GetCoefProbRowForPack(t, rowIndex);
                        Assert.Equal(expect, row[0]);
                    }
                }
                catch (Exception e)
                {
                    lock (gate) { first ??= e; }
                }
            }

            var thA = new System.Threading.Thread(() => Work(tableA));
            var thB = new System.Threading.Thread(() => Work(tableB));
            thA.Start();
            thB.Start();
            thA.Join();
            thB.Join();
            if (first != null)
                throw first;
        }

        // ---------- helpers ----------

        private static TOKENEXTRA Tok(int token, int extra, int skip = 0)
        {
            return new TOKENEXTRA
            {
                Token = (byte)token,
                Extra = (short)extra,
                skip_eob_node = (byte)skip,
                CoefProbRowIndex = 0,
            };
        }

        private static TOKENEXTRA Eob()
        {
            return Tok(entropy.DCT_EOB_TOKEN, 0);
        }

        private static byte[] PackToBytes(ReadOnlySpan<TOKENEXTRA> tokens, vp8_prob[,,,] coefProbs)
        {
            byte[] buf = new byte[256];
            BOOL_CODER bc = new BOOL_CODER();
            fixed (byte* p = buf)
            {
                boolhuff.vp8_start_encode(ref bc, p, p + buf.Length);
                bitstream.vp8_pack_tokens(ref bc, tokens, coefProbs);
                boolhuff.vp8_stop_encode(ref bc);
            }
            byte[] result = new byte[bc.pos];
            System.Array.Copy(buf, result, (int)bc.pos);
            return result;
        }

        private static void AssertPackedBytes(byte[] expected, ReadOnlySpan<TOKENEXTRA> tokens, vp8_prob[,,,] coefProbs)
        {
            byte[] got = PackToBytes(tokens, coefProbs);
            Assert.Equal(expected.Length, got.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i] == got[i],
                    "byte " + i + " mismatch: expected=0x" + expected[i].ToString("x2") + " actual=0x" + got[i].ToString("x2"));
            }
        }

        // Walk the same coef tree + per-category extra-bit trees the encoder
        // used, reading bits from the existing decoder's bool reader to
        // reconstruct each (Token, Extra) pair.
        private static List<(int Token, int Extra)> DecodeTokens(byte[] packed, TokenStreamBuffer reference, vp8_prob[,,,] coefProbs)
        {
            // Pad with zeros for the bool reader's lookahead.
            byte[] padded = new byte[packed.Length + 8];
            System.Array.Copy(packed, padded, packed.Length);

            BOOL_DECODER br = new BOOL_DECODER();
            var result = new List<(int, int)>(reference.Count);
            fixed (byte* pPad = padded)
            {
                dboolhuff.vp8dx_start_decode(ref br, pPad, (uint)padded.Length, null, null);

                for (int idx = 0; idx < reference.Count; idx++)
                {
                    var refTok = reference[idx];
                    byte[] probs = tokenize.GetCoefProbRowForPack(coefProbs, refTok.CoefProbRowIndex);

                    // Walk vp8_coef_tree until terminal (negative).
                    int i = (refTok.skip_eob_node != 0) ? 2 : 0;
                    int token;
                    while (true)
                    {
                        int bit = dboolhuff.vp8dx_decode_bool(ref br, probs[i >> 1]);
                        int next = entropy.vp8_coef_tree[i + bit];
                        if (next <= 0)
                        {
                            token = -next;
                            break;
                        }
                        i = next;
                    }

                    int extra = 0;
                    var b = entropy.vp8_extra_bits[token];
                    if (b.base_val != 0)
                    {
                        // Walk the per-category extra-bit tree to reconstruct
                        // the magnitude bits, then read the sign.
                        int magnitude = 0;
                        if (b.Len != 0)
                        {
                            int j = 0;
                            for (int step = 0; step < b.Len; step++)
                            {
                                int bit = dboolhuff.vp8dx_decode_bool(ref br, b.prob[j >> 1]);
                                magnitude = (magnitude << 1) | bit;
                                int next = b.tree[j + bit];
                                if (next == 0) break;
                                j = next;
                            }
                        }
                        int sign = dboolhuff.vp8dx_decode_bool(ref br, 128);
                        extra = (magnitude << 1) | sign;
                    }

                    result.Add((token, extra));

                    if (token == entropy.DCT_EOB_TOKEN)
                    {
                        // continue — the test feeds in everything after EOB too,
                        // which mirrors how the encoder serialised them.
                    }
                }
            }

            return result;
        }
    }
}
