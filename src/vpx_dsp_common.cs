//-----------------------------------------------------------------------------
// Filename: vpx_dsp_common.cs
//
// Description: Port of:
//  - vpx_dsp_common.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 11 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public static class vpx_dsp_common
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte clip_pixel(int val)
        {
            return (byte)((val > 255) ? 255 : (val < 0) ? 0 : val);
        }
    }
}
