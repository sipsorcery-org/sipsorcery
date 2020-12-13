//-----------------------------------------------------------------------------
// Filename: onyxd_int.cs
//
// Description: 

// Port of: 
//  - onyxc_int.h
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

using vp8_prob = System.Byte;
using vp8_reader = Vpx.Net.BOOL_DECODER;

namespace Vpx.Net
{
    public unsafe class FRAGMENT_DATA
     {
        public int enabled;
        public uint count;
        public byte*[] ptrs = new byte*[onyx.MAX_PARTITIONS];
        public uint[] sizes = new uint[onyx.MAX_PARTITIONS];
    }

    public class frame_buffers
    {
        public const int MAX_FB_MT_DEC = 32;

        /*
         * this struct will be populated with frame buffer management
         * info in future commits. */

        /* decoder instances */
        public VP8D_COMP[] pbi = new VP8D_COMP[MAX_FB_MT_DEC];
    }

    public class VP8D_COMP
    {
        public const int NUM_YV12_BUFFERS = 4;
        public const int MAX_PARTITIONS = 9;

        //DECLARE_ALIGNED(16, MACROBLOCKD, mb);
        public MACROBLOCKD mb = new MACROBLOCKD();

        public YV12_BUFFER_CONFIG[] dec_fb_ref = new YV12_BUFFER_CONFIG[NUM_YV12_BUFFERS];

        //DECLARE_ALIGNED(16, VP8_COMMON, common);
        public VP8_COMMON common = new VP8_COMMON();

        /* the last partition will be used for the modes/mvs */
        public vp8_reader[] mbc = new vp8_reader[MAX_PARTITIONS];

        public VP8D_CONFIG oxcf = new VP8D_CONFIG();

        public FRAGMENT_DATA fragments = new FRAGMENT_DATA();

        public Int64 last_time_stamp;
        public int ready_for_new_data;

        public vp8_prob prob_intra;
        public vp8_prob prob_last;
        public vp8_prob prob_gf;
        public vp8_prob prob_skip_false;

        public int ec_enabled;
        public int ec_active;
        public int decoded_key_frame;
        public int independent_partitions;
        public int frame_corrupt_residual;

        //vpx_decrypt_cb decrypt_cb;
        //void* decrypt_state;

        public VP8D_COMP()
        {
            for(int i=0; i<mbc.Length; i++)
            {
                mbc[i] = new vp8_reader();
            }
        }
    }
}
