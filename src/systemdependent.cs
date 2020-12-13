//-----------------------------------------------------------------------------
// Filename: systemdependent.cs
//
// Description: Port of:
//  - systemdependent.h
//  - systemdependent.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 28 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public static class systemdependent
    {
        public static void vp8_machine_specific_config(VP8_COMMON ctx)
        {
            ctx.cpu_caps = 0;
        }
    }
}
