//-----------------------------------------------------------------------------
// Filename: mv.cs
//
// Description: Port of:
//  - mv.h
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

using System;
using System.Runtime.InteropServices;

namespace Vpx.Net
{
    public struct MV
    {
        public short row;
        public short col;
    }

    /// <summary>
    /// Facilitates faster equality tests and copies.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct int_mv
    {
        [FieldOffset(0)] public UInt32 as_int;
        [FieldOffset(0)] public MV as_mv;
    }
}
