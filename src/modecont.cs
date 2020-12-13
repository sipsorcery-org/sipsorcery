//-----------------------------------------------------------------------------
// Filename: modecont.cs
//
// Description: Port of:
//  - modecont.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 03 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public static class modecont
    {
        public static int[,] vp8_mode_contexts = new int[6, 4]
        {
          { /* 0 */
            7, 1, 1, 143 },
          { /* 1 */
            14, 18, 14, 107 },
          { /* 2 */
            135, 64, 57, 68 },
          { /* 3 */
            60, 56, 128, 65 },
          { /* 4 */
            159, 134, 128, 34 },
          { /* 5 */
            234, 188, 128, 28 }
        };
    }
}
