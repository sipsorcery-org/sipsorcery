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
// SIMD: this file is intentionally pure scalar so the Legacy encoder
// pipeline always runs the C-style reference. The Optimized pipeline
// uses the SIMD fast paths in FdctEncoderSimd.cs / WalshEncoderSimd.cs
// instead of these entry points.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Ported from libvpx vp8/encoder/dct.c.
// 05 May 2026  Claude          Reverted to pure scalar; SIMD now lives in FdctEncoderSimd.
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

using System.Runtime.CompilerServices;

namespace Vpx.Net
{
    public static unsafe class dct
    {
        /// <summary>First pass of <see cref="vp8_short_fdct4x4_c"/>: horizontal transform on each row.</summary>
        internal static void vp8_fdct4x4_row_pass(short* input, short* output, int pitch)
        {
            int stride = pitch / 2;
            short* ip = input;
            short* op = output;
            for (int i = 0; i < 4; ++i)
            {
                Fdct4x4RowOneScalar(ip, op);
                ip += stride;
                op += 4;
            }
        }

        /// <summary>Second pass (column): in-place on the 4×4 block produced by <see cref="vp8_fdct4x4_row_pass"/>.</summary>
        internal static void vp8_fdct4x4_column_pass_inplace(short* io)
        {
            for (int i = 0; i < 4; ++i)
            {
                Fdct4x4ColumnOneScalar(io);
                io++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Fdct4x4RowOneScalar(short* ip, short* op)
        {
            int a1 = ((ip[0] + ip[3]) * 8);
            int b1 = ((ip[1] + ip[2]) * 8);
            int c1 = ((ip[1] - ip[2]) * 8);
            int d1 = ((ip[0] - ip[3]) * 8);

            op[0] = (short)(a1 + b1);
            op[2] = (short)(a1 - b1);

            op[1] = (short)((c1 * 2217 + d1 * 5352 + 14500) >> 12);
            op[3] = (short)((d1 * 2217 - c1 * 5352 + 7500) >> 12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Fdct4x4ColumnOneScalar(short* io)
        {
            int a1 = io[0] + io[12];
            int b1 = io[4] + io[8];
            int c1 = io[4] - io[8];
            int d1 = io[0] - io[12];

            io[0] = (short)((a1 + b1 + 7) >> 4);
            io[8] = (short)((a1 - b1 + 7) >> 4);

            io[4] = (short)(((c1 * 2217 + d1 * 5352 + 12000) >> 16) + (d1 != 0 ? 1 : 0));
            io[12] = (short)((d1 * 2217 - c1 * 5352 + 51000) >> 16);
        }

        // ---------------------------------------------------------------------
        // Public entry points
        // ---------------------------------------------------------------------

        /// <summary>
        /// Forward 4x4 integer DCT used for residual transform blocks.
        /// Bit-exact port of libvpx vp8_short_fdct4x4_c. Pure scalar — used
        /// by the Legacy pipeline and as the bit-exactness reference for the
        /// SIMD path in <see cref="FdctEncoderSimd"/>.
        /// </summary>
        /// <param name="input">Pointer to 4 rows of 16-bit residuals; rows are
        /// separated by <paramref name="pitch"/> bytes (i.e. pitch/2 shorts).</param>
        /// <param name="output">Pointer to 16-short output buffer (row-major 4x4).</param>
        /// <param name="pitch">Input row pitch in bytes (typical: 8 for a contiguous 4-wide row).</param>
        public static void vp8_short_fdct4x4_c(short* input, short* output, int pitch)
        {
            vp8_fdct4x4_row_pass(input, output, pitch);
            vp8_fdct4x4_column_pass_inplace(output);
        }

        /// <summary>
        /// Convenience: two consecutive 4x4 forward DCTs sharing input pitch.
        /// Bit-exact port of libvpx vp8_short_fdct8x4_c. Output layout:
        /// LEFT 4x4 row-major in <c>output[0..15]</c>, RIGHT 4x4 in <c>output[16..31]</c>.
        /// </summary>
        public static void vp8_short_fdct8x4_c(short* input, short* output, int pitch)
        {
            vp8_short_fdct8x4_split_c(input, output, output + 16, pitch);
        }

        /// <summary>
        /// 8x4 forward DCT writing the LEFT 4x4 (columns 0..3) into
        /// <paramref name="outLeft"/> and the RIGHT 4x4 (columns 4..7) into
        /// <paramref name="outRight"/>. Used by the encoder to skip a stack
        /// gather + split-copy when the two halves live in separate coef
        /// arrays. Bit-exact with two side-by-side <see cref="vp8_short_fdct4x4_c"/> calls.
        /// </summary>
        public static void vp8_short_fdct8x4_split_c(short* input, short* outLeft, short* outRight, int pitch)
        {
            vp8_short_fdct4x4_c(input, outLeft, pitch);
            vp8_short_fdct4x4_c(input + 4, outRight, pitch);
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
