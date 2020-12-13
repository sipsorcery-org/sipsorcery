//-----------------------------------------------------------------------------
// Filename: intraped.cs
//
// Description: Port of:
//  - intraped.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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
using System.Runtime.CompilerServices;

using ptrdiff_t = System.Int64;

namespace Vpx.Net
{
    public unsafe static class intraped
    {
        // #define DST(x, y) dst[(x) + (y)*stride]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static byte* DST(byte* dst, int x, int y, ptrdiff_t stride)
            => &dst[x + y * stride];

        // #define AVG3(a, b, c) (((a) + 2 * (b) + (c) + 2) >> 2)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte AVG3(byte a, byte b, byte c)
            => (byte)((a + 2 * b + c + 2) >> 2);

        // #define AVG2(a, b) (((a) + (b) + 1) >> 1)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte AVG2(byte a, byte b)
            => (byte)((a + b + 1) >> 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void vpx_d207_predictor_4x4_c(byte* dst, ptrdiff_t stride,
                              byte* above, byte* left)
        {
            byte I = left[0];
            byte J = left[1];
            byte K = left[2];
            byte L = left[3];

            //(void) above;

            *DST(dst, 0, 0, stride) = AVG2(I, J);
            *DST(dst, 2, 0, stride) = *DST(dst, 0, 1, stride) = AVG2(J, K);
            *DST(dst, 2, 1, stride) = *DST(dst, 0, 2, stride) = AVG2(K, L);
            *DST(dst, 1, 0, stride) = AVG3(I, J, K);
            *DST(dst, 3, 0, stride) = *DST(dst, 1, 1, stride) = AVG3(J, K, L);
            *DST(dst, 3, 1, stride) = *DST(dst, 1, 2, stride) = AVG3(K, L, L);
            *DST(dst, 3, 2, stride) = *DST(dst, 2, 2, stride) = *DST(dst, 0, 3, stride)
                = *DST(dst, 1, 3, stride) = *DST(dst, 2, 3, stride) = *DST(dst, 3, 3, stride) = L;
        }

        public static void vpx_ve_predictor_4x4_c(byte* dst, ptrdiff_t stride,
                            byte* above, byte* left)
        {
            byte H = above[-1];
            byte I = above[0];
            byte J = above[1];
            byte K = above[2];
            byte L = above[3];
            byte M = above[4];
            //(void)left;

            //dst[0] = AVG3(H, I, J);
            //dst[1] = AVG3(I, J, K);
            //dst[2] = AVG3(J, K, L);
            //dst[3] = AVG3(K, L, M);
            //memcpy(dst + stride * 1, dst, 4);
            //memcpy(dst + stride * 2, dst, 4);
            //memcpy(dst + stride * 3, dst, 4);

            dst[0] = AVG3(H, I, J);
            dst[1] = AVG3(I, J, K);
            dst[2] = AVG3(J, K, L);
            dst[3] = AVG3(K, L, M);
            Buffer.MemoryCopy(dst, dst + stride * 1, 4, 4);
            Buffer.MemoryCopy(dst, dst + stride * 2, 4, 4);
            Buffer.MemoryCopy(dst, dst + stride * 3, 4, 4);
        }

        public static void vpx_he_predictor_4x4_c(byte* dst, ptrdiff_t stride,
                            byte* above, byte* left)
        {
            byte H = above[-1];
            byte I = left[0];
            byte J = left[1];
            byte K = left[2];
            byte L = left[3];

            //memset(dst + stride * 0, AVG3(H, I, J), 4);
            //memset(dst + stride * 1, AVG3(I, J, K), 4);
            //memset(dst + stride * 2, AVG3(J, K, L), 4);
            //memset(dst + stride * 3, AVG3(K, L, L), 4);

            for (int i = 0; i < 4; i++)
            {
                dst[i] = AVG3(H, I, J);
                dst[stride + i] = AVG3(I, J, K);
                dst[stride * 2 + i] = AVG3(J, K, L);
                dst[stride * 3 + i] = AVG3(K, L, L);
            }
        }

        public static void vpx_d45e_predictor_4x4_c(byte* dst, ptrdiff_t stride,
                              byte* above, byte* left)
        {
            byte A = above[0];
            byte B = above[1];
            byte C = above[2];
            byte D = above[3];
            byte E = above[4];
            byte F = above[5];
            byte G = above[6];
            byte H = above[7];
            //(void)stride;
            //(void)left;

            *DST(dst, 0, 0, stride) = AVG3(A, B, C);
            *DST(dst, 1, 0, stride) = *DST(dst, 0, 1, stride) = AVG3(B, C, D);
            *DST(dst, 2, 0, stride) = *DST(dst, 1, 1, stride) = *DST(dst, 0, 2, stride) = AVG3(C, D, E);
            *DST(dst, 3, 0, stride) = *DST(dst, 2, 1, stride) = *DST(dst, 1, 2, stride) = *DST(dst, 0, 3, stride) = AVG3(D, E, F);
            *DST(dst, 3, 1, stride) = *DST(dst, 2, 2, stride) = *DST(dst, 1, 3, stride) = AVG3(E, F, G);
            *DST(dst, 3, 2, stride) = *DST(dst, 2, 3, stride) = AVG3(F, G, H);
            *DST(dst, 3, 3, stride) = AVG3(G, H, H);
        }

        public static void vpx_d135_predictor_4x4_c(byte* dst, ptrdiff_t stride,
                              byte* above, byte* left)
        {
            byte I = left[0];
            byte J = left[1];
            byte K = left[2];
            byte L = left[3];
            byte X = above[-1];
            byte A = above[0];
            byte B = above[1];
            byte C = above[2];
            byte D = above[3];
            //(void)stride;

            *DST(dst, 0, 3, stride) = AVG3(J, K, L);
            *DST(dst, 1, 3, stride) = *DST(dst, 0, 2, stride) = AVG3(I, J, K);
            *DST(dst, 2, 3, stride) = *DST(dst, 1, 2, stride) = *DST(dst, 0, 1, stride) = AVG3(X, I, J);
            *DST(dst, 3, 3, stride) = *DST(dst, 2, 2, stride) = *DST(dst, 1, 1, stride) = *DST(dst, 0, 0, stride) = AVG3(A, X, I);
            *DST(dst, 3, 2, stride) = *DST(dst, 2, 1, stride) = *DST(dst, 1, 0, stride) = AVG3(B, A, X);
            *DST(dst, 3, 1, stride) = *DST(dst, 2, 0, stride) = AVG3(C, B, A);
            *DST(dst, 3, 0, stride) = AVG3(D, C, B);
        }

        public static void vpx_d117_predictor_4x4_c(byte* dst, ptrdiff_t stride,
                              byte* above, byte* left)
        {
            byte I = left[0];
            byte J = left[1];
            byte K = left[2];
            byte X = above[-1];
            byte A = above[0];
            byte B = above[1];
            byte C = above[2];
            byte D = above[3];

            *DST(dst, 0, 0, stride) = *DST(dst, 1, 2, stride) = AVG2(X, A);
            *DST(dst, 1, 0, stride) = *DST(dst, 2, 2, stride) = AVG2(A, B);
            *DST(dst, 2, 0, stride) = *DST(dst, 3, 2, stride) = AVG2(B, C);
            *DST(dst, 3, 0, stride) = AVG2(C, D);

            *DST(dst, 0, 3, stride) = AVG3(K, J, I);
            *DST(dst, 0, 2, stride) = AVG3(J, I, X);
            *DST(dst, 0, 1, stride) = *DST(dst, 1, 3, stride) = AVG3(I, X, A);
            *DST(dst, 1, 1, stride) = *DST(dst, 2, 3, stride) = AVG3(X, A, B);
            *DST(dst, 2, 1, stride) = *DST(dst, 3, 3, stride) = AVG3(A, B, C);
            *DST(dst, 3, 1, stride) = AVG3(B, C, D);
        }

        public static void vpx_d63e_predictor_4x4_c(byte* dst, ptrdiff_t stride,
                              byte* above, byte* left)
        {
            byte A = above[0];
            byte B = above[1];
            byte C = above[2];
            byte D = above[3];
            byte E = above[4];
            byte F = above[5];
            byte G = above[6];
            byte H = above[7];
            //(void)left;

            *DST(dst, 0, 0, stride) = AVG2(A, B);
            *DST(dst, 1, 0, stride) = *DST(dst, 0, 2, stride) = AVG2(B, C);
            *DST(dst, 2, 0, stride) = *DST(dst, 1, 2, stride) = AVG2(C, D);
            *DST(dst, 3, 0, stride) = *DST(dst, 2, 2, stride) = AVG2(D, E);
            *DST(dst, 3, 2, stride) = AVG3(E, F, G);

            *DST(dst, 0, 1, stride) = AVG3(A, B, C);
            *DST(dst, 1, 1, stride) = *DST(dst, 0, 3, stride) = AVG3(B, C, D);
            *DST(dst, 2, 1, stride) = *DST(dst, 1, 3, stride) = AVG3(C, D, E);
            *DST(dst, 3, 1, stride) = *DST(dst, 2, 3, stride) = AVG3(D, E, F);
            *DST(dst, 3, 3, stride) = AVG3(F, G, H);
        }

        public static void vpx_d153_predictor_4x4_c(byte* dst, ptrdiff_t stride,
                              byte* above, byte* left)
        {
            byte I = left[0];
            byte J = left[1];
            byte K = left[2];
            byte L = left[3];
            byte X = above[-1];
            byte A = above[0];
            byte B = above[1];
            byte C = above[2];

            *DST(dst, 0, 0, stride) = *DST(dst, 2, 1, stride) = AVG2(I, X);
            *DST(dst, 0, 1, stride) = *DST(dst, 2, 2, stride) = AVG2(J, I);
            *DST(dst, 0, 2, stride) = *DST(dst, 2, 3, stride) = AVG2(K, J);
            *DST(dst, 0, 3, stride) = AVG2(L, K);

            *DST(dst, 3, 0, stride) = AVG3(A, B, C);
            *DST(dst, 2, 0, stride) = AVG3(X, A, B);
            *DST(dst, 1, 0, stride) = *DST(dst, 3, 1, stride) = AVG3(I, X, A);
            *DST(dst, 1, 1, stride) = *DST(dst, 3, 2, stride) = AVG3(J, I, X);
            *DST(dst, 1, 2, stride) = *DST(dst, 3, 3, stride) = AVG3(K, J, I);
            *DST(dst, 1, 3, stride) = AVG3(L, K, J);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void v_predictor(byte* dst, ptrdiff_t stride, int bs,
                               in byte* above, in byte* left)
        {
            //int r;
            //(void)left;

            for (int r = 0; r < bs; r++)
            {
                //memcpy(dst, above, bs);
                Mem.memcpy(dst, above, bs);
                dst += stride;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void h_predictor(byte* dst, ptrdiff_t stride, int bs,
                               in byte* above, in byte* left)
        {
            //int r;
            //(void)above;

            for (int r = 0; r < bs; r++)
            {
                //memset(dst, left[r], bs);
                Mem.memset(dst, left[r], bs);
                dst += stride;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void tm_predictor(byte* dst, ptrdiff_t stride, int bs,
                                in byte* above, in byte* left)
        {
            //int r, c;
            int ytop_left = above[-1];

            for (int r = 0; r < bs; r++)
            {
                for (int c = 0; c < bs; c++)
                {
                    dst[c] = vpx_dsp_common.clip_pixel(left[r] + above[c] - ytop_left);
                }
                dst += stride;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void dc_128_predictor(byte* dst, ptrdiff_t stride, int bs,
                                    in byte* above, in byte* left)
        {
            int r;
            //(void) above;
            //(void) left;

            for (r = 0; r < bs; r++)
            {
                //memset(dst, 128, bs);
                Mem.memset<byte>(dst, 128, bs);
                dst += stride;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void dc_left_predictor(byte* dst, ptrdiff_t stride, int bs,
                                     in byte* above, in byte* left)
        {
            int i, r, expected_dc, sum = 0;
            //(void) above;

            for (i = 0; i < bs; i++) sum += left[i];
            expected_dc = (sum + (bs >> 1)) / bs;

            for (r = 0; r < bs; r++)
            {
                //memset(dst, expected_dc, bs);
                Mem.memset<byte>(dst, (byte)expected_dc, bs);
                dst += stride;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void dc_top_predictor(byte* dst, ptrdiff_t stride, int bs,
                                    in byte* above, in byte* left)
        {
            int i, r, expected_dc, sum = 0;
            //(void) left;

            for (i = 0; i < bs; i++) sum += above[i];
            expected_dc = (sum + (bs >> 1)) / bs;

            for (r = 0; r < bs; r++)
            {
                //memset(dst, expected_dc, bs);
                Mem.memset<byte>(dst, (byte)expected_dc, bs);
                dst += stride;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void dc_predictor(byte* dst, ptrdiff_t stride, int bs,
                                in byte* above, in byte* left)
        {
            int i, r, expected_dc, sum = 0;
            int count = 2 * bs;

            for (i = 0; i < bs; i++)
            {
                sum += above[i];
                sum += left[i];
            }

            expected_dc = (sum + (count >> 1)) / count;

            for (r = 0; r < bs; r++)
            {
                //memset(dst, expected_dc, bs);
                Mem.memset<byte>(dst, (byte)expected_dc, bs);
                dst += stride;
            }
        }

        #region From intra_pred_* macros in intraped.c.

        public static void vpx_v_predictor_4x4_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            v_predictor(dst, stride, 4, above, left);
        }

        public static void vpx_v_predictor_8x8_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            v_predictor(dst, stride, 8, above, left);
        }

        public static void vpx_v_predictor_16x16_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            v_predictor(dst, stride, 16, above, left);
        }

        public static void vpx_h_predictor_4x4_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            h_predictor(dst, stride, 4, above, left);
        }

        public static void vpx_h_predictor_8x8_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            h_predictor(dst, stride, 8, above, left);
        }

        public static void vpx_h_predictor_16x16_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            h_predictor(dst, stride, 16, above, left);
        }

        public static void vpx_tm_predictor_4x4_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            tm_predictor(dst, stride, 4, above, left);
        }

        public static void vpx_tm_predictor_8x8_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            tm_predictor(dst, stride, 8, above, left);
        }

        public static void vpx_tm_predictor_16x16_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            tm_predictor(dst, stride, 16, above, left);
        }

        public static void vpx_dc_128_predictor_4x4_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_128_predictor(dst, stride, 4, above, left);
        }

        public static void vpx_dc_128_predictor_8x8_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_128_predictor(dst, stride, 8, above, left);
        }

        public static void vpx_dc_128_predictor_16x16_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_128_predictor(dst, stride, 16, above, left);
        }

        public static void vpx_dc_left_predictor_4x4_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_left_predictor(dst, stride, 4, above, left);
        }

        public static void vpx_dc_left_predictor_8x8_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_left_predictor(dst, stride, 8, above, left);
        }

        public static void vpx_dc_left_predictor_16x16_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_left_predictor(dst, stride, 16, above, left);
        }

        public static void vpx_dc_top_predictor_4x4_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_top_predictor(dst, stride, 4, above, left);
        }

        public static void vpx_dc_top_predictor_8x8_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_top_predictor(dst, stride, 8, above, left);
        }

        public static void vpx_dc_top_predictor_16x16_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_top_predictor(dst, stride, 16, above, left);
        }

        public static void vpx_dc_predictor_4x4_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_predictor(dst, stride, 4, above, left);
        }

        public static void vpx_dc_predictor_8x8_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_predictor(dst, stride, 8, above, left);
        }

        public static void vpx_dc_predictor_16x16_c(byte* dst, ptrdiff_t stride, in byte* above, in byte* left)
        {
            dc_predictor(dst, stride, 16, above, left);
        }

        #endregion
    }
}
