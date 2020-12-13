//-----------------------------------------------------------------------------
// Filename: extend.cs
//
// Description: Port of:
//  - extend.h
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

namespace Vpx.Net
{
    public unsafe static class extend
    {
        /* note the extension is only for the last row, for intra prediction purpose */
        public static void vp8_extend_mb_row(YV12_BUFFER_CONFIG ybf, byte* YPtr,
                               byte* UPtr,byte* VPtr)
        {
            int i;

            YPtr += ybf.y_stride * 14;
            UPtr += ybf.uv_stride * 6;
            VPtr += ybf.uv_stride * 6;

            for (i = 0; i < 4; ++i)
            {
                YPtr[i] = YPtr[-1];
                UPtr[i] = UPtr[-1];
                VPtr[i] = VPtr[-1];
            }

            YPtr += ybf.y_stride;
            UPtr += ybf.uv_stride;
            VPtr += ybf.uv_stride;

            for (i = 0; i < 4; ++i)
            {
                YPtr[i] = YPtr[-1];
                UPtr[i] = UPtr[-1];
                VPtr[i] = VPtr[-1];
            }
        }
    }
}
