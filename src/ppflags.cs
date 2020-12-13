//-----------------------------------------------------------------------------
// Filename: ppflags.cs
//
// Description: Port of:
//  - ppflags.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public struct vp8_ppflags_t
    {
        public int post_proc_flag;
        public int deblocking_level;
        public int noise_level;
        public int display_ref_frame_flag;
        public int display_mb_modes_flag;
        public int display_b_modes_flag;
        public int display_mv_flag;
    }
}
