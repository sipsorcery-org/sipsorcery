//-----------------------------------------------------------------------------
// Filename: dequantize.cs
//
// Description: Port of:
//  - dequantize.c
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
    public unsafe static class dequantize
    {
        public static void vp8_dequantize_b_c(BLOCKD d, short* DQC)
        {
            int i;
            //short* DQ = d->dqcoeff;
            //short* Q = d->qcoeff;
            var DQ = d.dqcoeff;
            var Q = d.qcoeff;

            for (i = 0; i < 16; ++i)
            {
                //DQ.src()[i] = (short)(Q.src()[i] * DQC[i]);
                DQ.set(i, (short)(Q.get(i) * DQC[i]));
            }
        }

        public static void vp8_dequant_idct_add_c(short* input, short* dq, byte* dest,
                                    int stride)
        {
            int i;

            for (i = 0; i < 16; ++i)
            {
                input[i] = (short)(dq[i] * input[i]);
            }

            idctllm.vp8_short_idct4x4llm_c(input, dest, stride, dest, stride);

            //memset(input, 0, 32);
            Mem.memset<short>(input, 0, 16);
        }
    }
}
