//-----------------------------------------------------------------------------
// Filename: loopfilter.cs
//
// Description: Port of:
//  - loopfilter.h
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

using System.Runtime.InteropServices;

namespace Vpx.Net
{
    public enum LOOPFILTERTYPE 
    { 
        NORMAL_LOOPFILTER = 0, 
        SIMPLE_LOOPFILTER = 1 
    }

    /// <summary>
    /// Need to align this structure so when it is declared and
    /// passed it can be loaded into vector registers.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack=loopfilter.SIMD_WIDTH)]
    public class loop_filter_info_n
    {
        public byte[,] mblim = new byte[loopfilter.MAX_LOOP_FILTER + 1, loopfilter.SIMD_WIDTH];
        public byte[,] blim = new byte[loopfilter.MAX_LOOP_FILTER + 1, loopfilter.SIMD_WIDTH];
        public byte[,] lim = new byte[loopfilter.MAX_LOOP_FILTER + 1, loopfilter.SIMD_WIDTH];
        public byte[,] hev_thr = new byte[4, loopfilter.SIMD_WIDTH];
        public byte[,,] lvl = new byte[4, 4, 4];
        public byte[,] hev_thr_lut = new byte[2, loopfilter.MAX_LOOP_FILTER + 1];
        public byte[] mode_lf_lut = new byte[10];
    }

    public unsafe class loop_filter_info
    {
        public byte* mblim;
        public byte* blim;
        public byte* lim;
        public byte* hev_thr;
    }

    public class loopfilter
    {
        public const int MAX_LOOP_FILTER = 63;
        public const int SIMD_WIDTH = 16;
    }
}
