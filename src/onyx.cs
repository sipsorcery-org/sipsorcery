//-----------------------------------------------------------------------------
// Filename: onyx.cs
//
// Description: 

// Port of: 
//  - onyx.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public class onyx
    {
        public const int MINQ = 0;
        public const int MAXQ = 127;
        public const int QINDEX_RANGE = MAXQ + 1;

        public const int NUM_YV12_BUFFERS = 4;

        public const int MAX_PARTITIONS = 9;
    }
}
