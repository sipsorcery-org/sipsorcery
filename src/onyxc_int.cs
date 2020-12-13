//-----------------------------------------------------------------------------
// Filename: onyxc_int.cs
//
// Description: Port of:
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

namespace Vpx.Net
{
    public enum CLAMP_TYPE
    {
        RECON_CLAMP_REQUIRED = 0,
        RECON_CLAMP_NOTREQUIRED = 1
    }

    public class FRAME_CONTEXT
    {
        public byte[] bmode_prob = new byte[blockd.VP8_BINTRAMODES - 1];
        public byte[] ymode_prob = new byte[blockd.VP8_YMODES - 1]; /* interframe intra mode probs */
        public byte[] uv_mode_prob = new byte[blockd.VP8_UV_MODES - 1];
        public byte[] sub_mv_ref_prob = new byte[blockd.VP8_SUBMVREFS - 1];
        public byte[,,,] coef_probs = new byte[entropy.BLOCK_TYPES, entropy.COEF_BANDS, entropy.PREV_COEF_CONTEXTS,entropy.ENTROPY_NODES];
        public MV_CONTEXT[] mvc = new MV_CONTEXT[2];

        public FRAME_CONTEXT CopyOf()
        {
            var copy = new FRAME_CONTEXT();
            Array.Copy(bmode_prob, copy.bmode_prob, bmode_prob.Length);
            Array.Copy(ymode_prob, copy.ymode_prob, ymode_prob.Length);
            Array.Copy(uv_mode_prob, copy.uv_mode_prob, uv_mode_prob.Length);
            Array.Copy(sub_mv_ref_prob, copy.sub_mv_ref_prob, sub_mv_ref_prob.Length);
            Array.Copy(coef_probs, copy.coef_probs, coef_probs.Length);
            Array.Copy(mvc, copy.mvc, mvc.Length);
            return copy;
        }
    }

    public enum TOKEN_PARTITION
    {
        ONE_PARTITION = 0,
        TWO_PARTITION = 1,
        FOUR_PARTITION = 2,
        EIGHT_PARTITION = 3
    }

    public class VP8_COMMON
    {
        public const int MINQ = 0;
        public const int MAXQ = 127;
        public const int QINDEX_RANGE = MAXQ + 1;
        public const int NUM_YV12_BUFFERS = 4;

        public vpx_internal_error_info error;

        public short[,] Y1dequant = new short[QINDEX_RANGE, 2];
        public short[,] Y2dequant = new short[QINDEX_RANGE, 2];
        public short[,] UVdequant = new short[QINDEX_RANGE, 2];

        public int Width;
        public int Height;
        public int horiz_scale;
        public int vert_scale;

        public CLAMP_TYPE clamp_type;

        public YV12_BUFFER_CONFIG frame_to_show;

        public YV12_BUFFER_CONFIG[] yv12_fb = new YV12_BUFFER_CONFIG[NUM_YV12_BUFFERS];
        public int[] fb_idx_ref_cnt = new int[NUM_YV12_BUFFERS];
        public int new_fb_idx, lst_fb_idx, gld_fb_idx, alt_fb_idx;

        public YV12_BUFFER_CONFIG temp_scale_frame;

        public FRAME_TYPE last_frame_type; /* Save last frame's frame type for motion search. */
        public FRAME_TYPE frame_type;

        public int show_frame;

        public int frame_flags;
        public int MBs;
        public int mb_rows;
        public int mb_cols;
        public int mode_info_stride;

        /* profile settings */
        public int mb_no_coeff_skip;
        public int no_lpf;
        public int use_bilinear_mc_filter;
        public int full_pixel;

        public int base_qindex;

        public int y1dc_delta_q;
        public int y2dc_delta_q;
        public int y2ac_delta_q;
        public int uvdc_delta_q;
        public int uvac_delta_q;

        /* We allocate a MODE_INFO struct for each macroblock, together with
           an extra row on top and column on the left to simplify prediction. */

        public MODE_INFO[] mip; /* Base of allocated array */

        //public MODE_INFO* mi;    /* Corresponds to upper left visible macroblock */
        // Port AC: Replaced the MODE_INFO pointer with a custom object.
        public ArrPtr<MODE_INFO> mi;

        /* MODE_INFO for the last decoded frame to show */
        public ArrPtr<MODE_INFO> show_frame_mi;
        public LOOPFILTERTYPE filter_type;

        public loop_filter_info_n lf_info = new loop_filter_info_n();

        public int filter_level;
        public int last_sharpness_level;
        public int sharpness_level;

        public int refresh_last_frame;    /* Two state 0 = NO, 1 = YES */
        public int refresh_golden_frame;  /* Two state 0 = NO, 1 = YES */
        public int refresh_alt_ref_frame; /* Two state 0 = NO, 1 = YES */

        public int copy_buffer_to_gf;  /* 0 none, 1 Last to GF, 2 ARF to GF */
        public int copy_buffer_to_arf; /* 0 none, 1 Last to ARF, 2 GF to ARF */

        public int refresh_entropy_probs; /* Two state 0 = NO, 1 = YES */

        public int[] ref_frame_sign_bias = new int[(int)MV_REFERENCE_FRAME.MAX_REF_FRAMES]; /* Two state 0, 1 */

        /* Y,U,V,Y2 */
        //ENTROPY_CONTEXT_PLANES* above_context; /* row of context for each plane */
        public ENTROPY_CONTEXT_PLANES[] above_context; /* row of context for each plane */
        //public ENTROPY_CONTEXT_PLANES left_context = new ENTROPY_CONTEXT_PLANES();   /* (up to) 4 contexts "" */
        public ENTROPY_CONTEXT_PLANES left_context = new ENTROPY_CONTEXT_PLANES();

        public FRAME_CONTEXT lfc = new FRAME_CONTEXT(); /* last frame entropy */
        public FRAME_CONTEXT fc = new FRAME_CONTEXT();  /* this frame entropy */

        public uint current_video_frame;

        public int version;

        public TOKEN_PARTITION multi_token_partition;

        public int cpu_caps;
    }
}

