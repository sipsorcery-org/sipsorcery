//-----------------------------------------------------------------------------
// Filename: detokenize.cs
//
// Description: Port of:
//  - detokenize.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 07 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

/*
 *  Copyright (c) 2010 The WebM project authors. All Rights Reserved.
 *
 *  Use of this source code is governed by a BSD-style license
 *  that can be found in the LICENSE file in the root of the source
 *  tree. An additional intellectual property rights grant can be found
 *  in the file PATENTS.  All contributing project authors may
 *  be found in the AUTHORS file in the root of the source tree.
 */

using System;

using VP8_BD_VALUE = System.UInt64;
using ENTROPY_CONTEXT = System.SByte;

namespace Vpx.Net
{
    delegate int vp8dx_decode_bool_delegate(ref BOOL_DECODER br, int probability);

    public unsafe static class detokenize
    {
        public const int NUM_PROBAS = 11;
        public const int NUM_CTX = 3;

        static vp8dx_decode_bool_delegate VP8GetBit = dboolhuff.vp8dx_decode_bool;

        private static readonly byte[] kBands = new byte[16 + 1] {
          0, 1, 2, 3, 6, 4, 5, 6, 6,
          6, 6, 6, 6, 6, 6, 7, 0 /* extra entry as sentinel */
        };

        static byte[] kCat3 = { 173, 148, 140, 0 };
        static byte[] kCat4 = { 176, 155, 140, 135, 0 };
        static byte[] kCat5 = { 180, 157, 141, 134, 130, 0 };
        static byte[] kCat6 = { 254, 254, 243, 230, 196, 177,
                                 153, 140, 133, 130, 129, 0 };
        static byte[][] kCat3456 = new byte[][] { kCat3, kCat4, kCat5, kCat6 };
        static byte[] kZigzag = { 0, 1,  4,  8,  5, 2,  3,  6,
                                     9, 12, 13, 10, 7, 11, 14, 15 };

        public static void vp8_reset_mb_tokens_context(MACROBLOCKD x)
        {
            //ENTROPY_CONTEXT* a_ctx = ((ENTROPY_CONTEXT*)x->above_context);
            //ENTROPY_CONTEXT* l_ctx = ((ENTROPY_CONTEXT*)x->left_context);

            //memset(a_ctx, 0, sizeof(ENTROPY_CONTEXT_PLANES) - 1);
            //memset(l_ctx, 0, sizeof(ENTROPY_CONTEXT_PLANES) - 1);

            x.above_context.get().Clear(false);
            x.left_context.Clear(false);

            /* Clear entropy contexts for Y2 blocks */
            if (x.mode_info_context.get().mbmi.is_4x4 == 0)
            {
                //a_ctx[8] = l_ctx[8] = 0;
                x.above_context.get().y2 = x.left_context.y2 = default;
            }
        }

        // With corrupt / fuzzed streams the calculation of br->value may overflow. See
        // b/148271109.
        //static VPX_NO_UNSIGNED_OVERFLOW_CHECK int GetSigned(BOOL_DECODER* br,
        //                                                   int value_to_sign)
        static int GetSigned(BOOL_DECODER br, int value_to_sign)
        {
            int split = (int)((br.range + 1) >> 1);
            VP8_BD_VALUE bigsplit = (VP8_BD_VALUE)split << (dboolhuff.VP8_BD_VALUE_SIZE - 8);
            int v;

            if (br.count < 0)
            {
                dboolhuff.vp8dx_bool_decoder_fill(ref br);
            }

            if (br.value < bigsplit)
            {
                br.range = (uint)split;
                v = value_to_sign;
            }
            else
            {
                br.range = (uint)(br.range - split);
                br.value = br.value - bigsplit;
                v = -value_to_sign;
            }
            br.range += br.range;
            br.value += br.value;
            br.count--;

            return v;
        }

        /*
           Returns the position of the last non-zero coeff plus one
           (and 0 if there's no coeff at all)
        */
        /* for const-casting */
        //typedef const uint8_t (*ProbaArray)[NUM_CTX] [NUM_PROBAS];
        //static int GetCoeffs(BOOL_DECODER* br, ProbaArray prob, int ctx, int n,
        //             int16_t*out)
        static int GetCoeffs(BOOL_DECODER br, byte* prob, int ctx, int n, short* @out)
        {
            int bigSlice = NUM_CTX * NUM_PROBAS;
            int smallSlice = NUM_PROBAS;

            //const uint8_t *p = prob[n][ctx];
            byte* p = prob + n * bigSlice + ctx * smallSlice;

            if (VP8GetBit(ref br, p[0]) == 0)
            { /* first EOB is more a 'CBP' bit. */
                return 0;
            }
            while (true)
            {
                ++n;
                if (VP8GetBit(ref br, p[1]) == 0)
                {
                    //p = prob[kBands[n]][0];
                    p = prob + kBands[n] * bigSlice;
                }
                else
                { /* non zero coeff */
                    int v, j;
                    if (VP8GetBit(ref br, p[2]) == 0)
                    {
                        //p = prob[kBands[n]][1];
                        p = prob + kBands[n] * bigSlice + smallSlice;
                        v = 1;
                    }
                    else
                    {
                        if (VP8GetBit(ref br, p[3]) == 0)
                        {
                            if (VP8GetBit(ref br, p[4]) == 0)
                            {
                                v = 2;
                            }
                            else
                            {
                                v = 3 + VP8GetBit(ref br, p[5]);
                            }
                        }
                        else
                        {
                            if (VP8GetBit(ref br, p[6]) == 0)
                            {
                                if (VP8GetBit(ref br, p[7]) == 0)
                                {
                                    v = 5 + VP8GetBit(ref br, 159);
                                }
                                else
                                {
                                    v = 7 + 2 * VP8GetBit(ref br, 165);
                                    v += VP8GetBit(ref br, 145);
                                }
                            }
                            else
                            {
                                byte* tab;
                                int bit1 = VP8GetBit(ref br, p[8]);
                                int bit0 = VP8GetBit(ref br, p[9 + bit1]);
                                int cat = 2 * bit1 + bit0;
                                v = 0;
                                fixed (byte* ptab = kCat3456[cat])
                                {
                                    for (tab = ptab; *tab > 0; ++tab)
                                    {
                                        v += v + VP8GetBit(ref br, *tab);
                                    }
                                }
                                v += 3 + (8 << cat);
                            }
                        }
                        //p = prob[kBands[n]][2];
                        p = prob + kBands[n] * bigSlice + 2 * smallSlice;
                    }
                    j = kZigzag[n - 1];

                    @out[j] = (short)GetSigned(br, v);

                    if (n == 16 || VP8GetBit(ref br, p[0]) == 0)
                    { /* EOB */
                        return n;
                    }
                }
                if (n == 16)
                {
                    return 16;
                }
            }
        }

        public static int vp8_decode_mb_tokens(VP8D_COMP dx, MACROBLOCKD x)
        {
            BOOL_DECODER bc = x.current_bc;
            FRAME_CONTEXT fc = dx.common.fc;
            //char* eobs = x->eobs;
            sbyte[] eobs = x.eobs;

            int i;
            int nonzeros;
            int eobtotal = 0;

            //short* qcoeff_ptr;
            //ProbaArray coef_probs;
            //int coefIndex = entropy.COEF_BANDS * entropy.PREV_COEF_CONTEXTS * entropy.ENTROPY_NODES;
            //ENTROPY_CONTEXT* a_ctx = ((ENTROPY_CONTEXT*)x->above_context);
            //ENTROPY_CONTEXT* l_ctx = ((ENTROPY_CONTEXT*)x->left_context);
            //ENTROPY_CONTEXT* a;
            //ENTROPY_CONTEXT* l;

            int blockSlice = entropy.COEF_BANDS * entropy.PREV_COEF_CONTEXTS * entropy.ENTROPY_NODES;

            fixed (byte* pCoefProbs = fc.coef_probs)
            {
                byte* coef_probs = null;

                fixed (sbyte* pAboveCtx = x.above_context.get().y1, pLeftContext = x.left_context.y1)
                {
                    ENTROPY_CONTEXT* a_ctx = pAboveCtx;
                    ENTROPY_CONTEXT* l_ctx = pLeftContext;
                    ENTROPY_CONTEXT* a;
                    ENTROPY_CONTEXT* l;

                    int skip_dc = 0;

                    //qcoeff_ptr = x.qcoeff[0];
                    fixed (short* pQcoeff = x.qcoeff)
                    {
                        short* qcoeff_ptr = pQcoeff;

                        if (x.mode_info_context.get().mbmi.is_4x4 == 0)
                        {
                            a = a_ctx + 8;
                            l = l_ctx + 8;

                            //coef_probs = fc.coef_probs[1];
                            coef_probs = pCoefProbs + blockSlice;

                            nonzeros = GetCoeffs(bc, coef_probs, (*a + *l), 0, qcoeff_ptr + 24 * 16);
                            *a = *l = (sbyte)(nonzeros > 0 ? 1 : 0);

                            eobs[24] = (sbyte)nonzeros;
                            eobtotal += nonzeros - 16;

                            //coef_probs = fc.coef_probs[0];
                            coef_probs = pCoefProbs;

                            skip_dc = 1;
                        }
                        else
                        {
                            //coef_probs = fc.coef_probs[3];
                            coef_probs = pCoefProbs + 3 * blockSlice;

                            skip_dc = 0;
                        }

                        for (i = 0; i < 16; ++i)
                        {
                            a = a_ctx + (i & 3);
                            l = l_ctx + ((i & 0xc) >> 2);

                            nonzeros = GetCoeffs(bc, coef_probs, (*a + *l), skip_dc, qcoeff_ptr);
                            *a = *l = (sbyte)(nonzeros > 0 ? 1 : 0);

                            nonzeros += skip_dc;
                            eobs[i] = (sbyte)nonzeros;
                            eobtotal += nonzeros;
                            qcoeff_ptr += 16;
                        }

                        //coef_probs = fc.coef_probs[2];
                        coef_probs = pCoefProbs + 2 * blockSlice;

                        a_ctx += 4;
                        l_ctx += 4;
                        for (i = 16; i < 24; ++i)
                        {
                            a = a_ctx + ((i > 19 ? 1 : 0) << 1) + (i & 1);
                            l = l_ctx + ((i > 19 ? 1 : 0) << 1) + ((i & 3) > 1 ? 1 : 0);

                            nonzeros = GetCoeffs(bc, coef_probs, (*a + *l), 0, qcoeff_ptr);
                            *a = *l = (sbyte)(nonzeros > 0 ? 1 : 0);

                            eobs[i] = (sbyte)nonzeros;
                            eobtotal += nonzeros;
                            qcoeff_ptr += 16;
                        }
                    }
                }
            }

            return eobtotal;
        }
    }
}
