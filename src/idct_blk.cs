//-----------------------------------------------------------------------------
// Filename: idct_blk.cs
//
// Description: Port of:
//  - idct_blk.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 09 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public static unsafe class idct_blk
    {
        public static void vp8_dequant_idct_add_y_block_c(short* q, short* dq, byte* dst,
                                    int stride, sbyte* eobs)
        {
            int i, j;

            for (i = 0; i < 4; ++i)
            {
                for (j = 0; j < 4; ++j)
                {
                    if (*eobs++ > 1)
                    {
                        dequantize.vp8_dequant_idct_add_c(q, dq, dst, stride);
                    }
                    else
                    {
                        idctllm.vp8_dc_only_idct_add_c((short)(q[0] * dq[0]), dst, stride, dst, stride);
                        //memset(q, 0, 2 * sizeof(q[0]));
                        Mem.memset<short>(q, 0, 2);
                    }

                    q += 16;
                    dst += 4;
                }

                dst += 4 * stride - 16;
            }
        }

        public static void vp8_dequant_idct_add_uv_block_c(short* q, short* dq, byte* dst_u,
                                    byte* dst_v, int stride, sbyte* eobs)
        {
            int i, j;

            for (i = 0; i < 2; ++i)
            {
                for (j = 0; j < 2; ++j)
                {
                    if (*eobs++ > 1)
                    {
                        dequantize.vp8_dequant_idct_add_c(q, dq, dst_u, stride);
                    }
                    else
                    {
                        idctllm.vp8_dc_only_idct_add_c((short)(q[0] * dq[0]), dst_u, stride, dst_u, stride);
                        //memset(q, 0, 2 * sizeof(q[0]));
                        Mem.memset<short>(q, 0, 2);
                    }

                    q += 16;
                    dst_u += 4;
                }

                dst_u += 4 * stride - 8;
            }

            for (i = 0; i < 2; ++i)
            {
                for (j = 0; j < 2; ++j)
                {
                    if (*eobs++ > 1)
                    {
                        dequantize.vp8_dequant_idct_add_c(q, dq, dst_v, stride);
                    }
                    else
                    {
                        idctllm.vp8_dc_only_idct_add_c((short)(q[0] * dq[0]), dst_v, stride, dst_v, stride);
                        //memset(q, 0, 2 * sizeof(q[0]));
                        Mem.memset<short>(q, 0, 2);
                    }

                    q += 16;
                    dst_v += 4;
                }

                dst_v += 4 * stride - 8;
            }
        }
    }
}
