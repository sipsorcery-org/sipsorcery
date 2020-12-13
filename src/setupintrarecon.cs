//-----------------------------------------------------------------------------
// Filename: setupintrarecon.cs
//
// Description: Port of:
//  - setupintrarecon.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 06 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public unsafe static class setupintrarecon
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void setup_intra_recon_left(byte* y_buffer,
                                          byte* u_buffer,
                                          byte* v_buffer, int y_stride,
                                          int uv_stride)
        {
            int i;

            for (i = 0; i < 16; ++i) y_buffer[y_stride * i] = 129;

            for (i = 0; i < 8; ++i) u_buffer[uv_stride * i] = 129;

            for (i = 0; i < 8; ++i) v_buffer[uv_stride * i] = 129;
        }

        public static void vp8_setup_intra_recon_top_line(YV12_BUFFER_CONFIG ybf)
        {
            //memset(ybf->y_buffer - 1 - ybf->y_stride, 127, ybf->y_width + 5);
            Mem.memset<byte>(ybf.y_buffer - 1 - ybf.y_stride, 127, ybf.y_width + 5);
            //memset(ybf->u_buffer - 1 - ybf->uv_stride, 127, ybf->uv_width + 5);
            Mem.memset<byte>(ybf.u_buffer - 1 - ybf.uv_stride, 127, ybf.uv_width + 5);
            //memset(ybf->v_buffer - 1 - ybf->uv_stride, 127, ybf->uv_width + 5);
            Mem.memset<byte>(ybf.v_buffer - 1 - ybf.uv_stride, 127, ybf.uv_width + 5);
        }
    }
}
