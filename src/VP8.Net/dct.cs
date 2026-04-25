//-----------------------------------------------------------------------------
// Filename: dct.cs
//
// Description: Forward DCT for the VP8 encoder. Port of:
//  - libvpx/vp8/encoder/dct.c
//
// This is the bit-exact 4x4 integer DCT used by libvpx's reference VP8
// encoder, paired with the inverse transform in idctllm.cs. The two
// functions are:
//   * vp8_short_fdct4x4_c   - forward DCT for residual blocks
//   * vp8_short_walsh4x4_c  - 4x4 Walsh-Hadamard for the Y2 (DC) block
//                             of a 16x16 prediction
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Apr 2026  Aaron Clauson   Ported from libvpx vp8/encoder/dct.c.
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

namespace Vpx.Net
{
    public static unsafe class dct
    {
        /// <summary>
        /// Forward 4x4 integer DCT used for residual transform blocks.
        /// Bit-exact port of libvpx vp8_short_fdct4x4_c.
        /// </summary>
        /// <param name="input">Pointer to 4 rows of 16-bit residuals; rows are
        /// separated by <paramref name="pitch"/> bytes (i.e. pitch/2 shorts).</param>
        /// <param name="output">Pointer to 16-short output buffer (row-major 4x4).</param>
        /// <param name="pitch">Input row pitch in bytes (typical: 8 for a contiguous 4-wide row).</param>
        public static void vp8_short_fdct4x4_c(short* input, short* output, int pitch)
        {
            int i;
            int a1, b1, c1, d1;
            short* ip = input;
            short* op = output;

            for (i = 0; i < 4; ++i)
            {
                a1 = ((ip[0] + ip[3]) * 8);
                b1 = ((ip[1] + ip[2]) * 8);
                c1 = ((ip[1] - ip[2]) * 8);
                d1 = ((ip[0] - ip[3]) * 8);

                op[0] = (short)(a1 + b1);
                op[2] = (short)(a1 - b1);

                op[1] = (short)((c1 * 2217 + d1 * 5352 + 14500) >> 12);
                op[3] = (short)((d1 * 2217 - c1 * 5352 + 7500) >> 12);

                ip += pitch / 2;
                op += 4;
            }

            ip = output;
            op = output;
            for (i = 0; i < 4; ++i)
            {
                a1 = ip[0] + ip[12];
                b1 = ip[4] + ip[8];
                c1 = ip[4] - ip[8];
                d1 = ip[0] - ip[12];

                op[0] = (short)((a1 + b1 + 7) >> 4);
                op[8] = (short)((a1 - b1 + 7) >> 4);

                op[4] = (short)(((c1 * 2217 + d1 * 5352 + 12000) >> 16) + (d1 != 0 ? 1 : 0));
                op[12] = (short)((d1 * 2217 - c1 * 5352 + 51000) >> 16);

                ip++;
                op++;
            }
        }

        /// <summary>
        /// Convenience: two consecutive 4x4 forward DCTs sharing input pitch.
        /// Bit-exact port of libvpx vp8_short_fdct8x4_c.
        /// </summary>
        public static void vp8_short_fdct8x4_c(short* input, short* output, int pitch)
        {
            vp8_short_fdct4x4_c(input, output, pitch);
            vp8_short_fdct4x4_c(input + 4, output + 16, pitch);
        }

        /// <summary>
        /// 4x4 Walsh-Hadamard transform applied to the 16 DC coefficients
        /// of a 16x16 macroblock predicted with one of the four whole-MB
        /// modes (DC_PRED, V_PRED, H_PRED, TM_PRED). Result is then quantized
        /// with the Y2 (second-order) quantizer.
        ///
        /// Bit-exact port of libvpx vp8_short_walsh4x4_c.
        /// </summary>
        public static void vp8_short_walsh4x4_c(short* input, short* output, int pitch)
        {
            int i;
            int a1, b1, c1, d1;
            int a2, b2, c2, d2;
            short* ip = input;
            short* op = output;

            for (i = 0; i < 4; ++i)
            {
                a1 = ((ip[0] + ip[2]) * 4);
                d1 = ((ip[1] + ip[3]) * 4);
                c1 = ((ip[1] - ip[3]) * 4);
                b1 = ((ip[0] - ip[2]) * 4);

                op[0] = (short)(a1 + d1 + (a1 != 0 ? 1 : 0));
                op[1] = (short)(b1 + c1);
                op[2] = (short)(b1 - c1);
                op[3] = (short)(a1 - d1);
                ip += pitch / 2;
                op += 4;
            }

            ip = output;
            op = output;

            for (i = 0; i < 4; ++i)
            {
                a1 = ip[0] + ip[8];
                d1 = ip[4] + ip[12];
                c1 = ip[4] - ip[12];
                b1 = ip[0] - ip[8];

                a2 = a1 + d1;
                b2 = b1 + c1;
                c2 = b1 - c1;
                d2 = a1 - d1;

                a2 += a2 < 0 ? 1 : 0;
                b2 += b2 < 0 ? 1 : 0;
                c2 += c2 < 0 ? 1 : 0;
                d2 += d2 < 0 ? 1 : 0;

                op[0] = (short)((a2 + 3) >> 3);
                op[4] = (short)((b2 + 3) >> 3);
                op[8] = (short)((c2 + 3) >> 3);
                op[12] = (short)((d2 + 3) >> 3);

                ip++;
                op++;
            }
        }
    }
}
