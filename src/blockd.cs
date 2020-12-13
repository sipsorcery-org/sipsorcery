//-----------------------------------------------------------------------------
// Filename: blockd.cs
//
// Description: Port of:
//  - blockd.h
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

using vp8_prob = System.Byte;
using vp8_reader = Vpx.Net.BOOL_DECODER;

namespace Vpx.Net
{
    public unsafe delegate void vp8_subpix_fn_t(byte* src_ptr, int src_pixels_per_line,
                                int xoffset, int yoffset, byte* dst_ptr, int dst_pitch);

    public static class blockd
    {
        public const int VP8_BINTRAMODES = (int)B_PREDICTION_MODE.B_HU_PRED + 1; /* 10 */
        public const int VP8_SUBMVREFS = 1 + B_PREDICTION_MODE.NEW4X4 - B_PREDICTION_MODE.LEFT4X4;

        /*#define DCPRED 1*/
        public const int DCPREDSIMTHRESH = 0;
        public const int DCPREDCNTTHRESH = 3;

        public const int MB_FEATURE_TREE_PROBS = 3;
        public const int MAX_MB_SEGMENTS = 4;

        public const int MAX_REF_LF_DELTAS = 4;
        public const int MAX_MODE_LF_DELTAS = 4;

        /* Segment Feature Masks */
        public const int SEGMENT_DELTADATA = 0;
        public const int SEGMENT_ABSDATA = 1;

        /* Segment Feature Masks */
        public const int SEGMENT_ALTQ = 0x01;
        public const int SEGMENT_ALT_LF = 0x02;

        public const int VP8_YMODES = (int)MB_PREDICTION_MODE.B_PRED + 1;
        public const int VP8_UV_MODES = (int)MB_PREDICTION_MODE.TM_PRED + 1;
    }

    public enum MV_REFERENCE_FRAME
    {
        INTRA_FRAME = 0,
        LAST_FRAME = 1,
        GOLDEN_FRAME = 2,
        ALTREF_FRAME = 3,
        MAX_REF_FRAMES = 4
    }

    public enum FRAME_TYPE
    {
        KEY_FRAME = 0,
        INTER_FRAME = 1
    }

    public enum MB_PREDICTION_MODE
    {
        DC_PRED, /* average of above and left pixels */
        V_PRED,  /* vertical prediction */
        H_PRED,  /* horizontal prediction */
        TM_PRED, /* Truemotion prediction */
        B_PRED,  /* block based prediction, each block has its own prediction mode */

        NEARESTMV,
        NEARMV,
        ZEROMV,
        NEWMV,
        SPLITMV,

        MB_MODE_COUNT
    }

    public enum B_PREDICTION_MODE
    {
        B_DC_PRED, /* average of above and left pixels */
        B_TM_PRED,

        B_VE_PRED, /* vertical prediction */
        B_HE_PRED, /* horizontal prediction */

        B_LD_PRED,
        B_RD_PRED,

        B_VR_PRED,
        B_VL_PRED,
        B_HD_PRED,
        B_HU_PRED,

        LEFT4X4,
        ABOVE4X4,
        ZERO4X4,
        NEW4X4,

        B_MODE_COUNT
    }

    //union b_mode_info
    //{
    //    B_PREDICTION_MODE as_mode;
    //    int_mv mv;
    //};

    [StructLayout(LayoutKind.Explicit)]
    public struct b_mode_info
    {
        [FieldOffset(0)] public B_PREDICTION_MODE as_mode;
        [FieldOffset(0)] public int_mv mv;
    }

    public struct MB_MODE_INFO
    {
        public byte mode, uv_mode;
        public byte ref_frame;
        public byte is_4x4;
        public int_mv mv;

        public byte partitioning;
        /* does this mb has coefficients at all, 1=no coefficients, 0=need decode
           tokens */
        public byte mb_skip_coeff;
        public byte need_to_clamp_mvs;
        /* Which set of segmentation parameters should be used for this MB */
        public byte segment_id;
    }

    public class MODE_INFO
    {
        public MB_MODE_INFO mbmi;
        public b_mode_info[] bmi = new b_mode_info[16]; 
    }

    public unsafe struct ENTROPY_CONTEXT_PLANES
    {
        public fixed sbyte y1[4];
        public fixed sbyte u[2];
        public fixed sbyte v[2];
        public byte y2;

        //char[] y1 = new char[4];
        //char[] u = new char[2];
        //char[] v = new char[2];
        //char y2;

        public void Clear(bool resetY2 = true)
        {
            var zero = stackalloc byte[4];

            fixed(sbyte* py1 = y1, pu = u, pv = v)
            {
                Buffer.MemoryCopy(zero, py1, 4, 4);
                Buffer.MemoryCopy(zero, pu, 2, 2);
                Buffer.MemoryCopy(zero, pv, 2, 2);
            }
            //Array.Clear(y1, 0, y1.Length);
            //Array.Clear(u, 0, u.Length);
            //Array.Clear(v, 0, v.Length);

            if (resetY2)
            {
                y2 = default;
            }
        }
    }

    /// <remarks>
    /// Port AC: The original pointers in this struct were used to point to
    /// elements in the same named block of memory in MACROBLOCKD. For the managed
    /// port, pointers to a array elements cannot be used. Instead the pointers 
    /// have been changed to indexes.
    /// </remarks>
    public unsafe struct BLOCKD
    {
        //public short* qcoeff;
        public ArrPtr<short> qcoeff;    // References the MACROBLOCKD.qcoeff array.
        //public short* dqcoeff;
        public ArrPtr<short> dqcoeff;   // References the MACROBLOCKD.dqcoeff array.
        //public byte* predictor;
        public ArrPtr<byte> predictor;  // References the MACROBLOCKD.predictor array.
        //public short* dequant;
        public ArrPtr<short> dequant;
        public int offset;
        //public char* eob;
        public ArrPtr<sbyte> eob;       // References the MACROBLOCKD.eobs array.
        public b_mode_info bmi;
    }

    /* Macroblock level features */
    public enum MB_LVL_FEATURES
    {
        MB_LVL_ALT_Q = 0,  /* Use alternate Quantizer .... */
        MB_LVL_ALT_LF = 1, /* Use alternate loop filter value... */
        MB_LVL_MAX = 2     /* Number of MB level fe atures supported */
    }

    //[StructLayout(LayoutKind.Sequential, Pack = loopfilter.SIMD_WIDTH)]
    public unsafe class MACROBLOCKD
    {
        public const int MB_FEATURE_TREE_PROBS = 3;
        public const int MAX_MB_SEGMENTS = 4;
        public const int MAX_REF_LF_DELTAS = 4;
        public const int MAX_MODE_LF_DELTAS = 4;

        /* Segment Feature Masks */
        public const int SEGMENT_DELTADATA = 0;
        public const int SEGMENT_ABSDATA = 1;

        public byte[] predictor = new byte[384];
        public short[] qcoeff = new short[400];
        public short[] dqcoeff = new short[400];
        public sbyte[] eobs = new sbyte[25];

        public short[] dequant_y1 = new short[16];
        public short[] dequant_y1_dc = new short[16];
        public short[] dequant_y2 = new short[16];
        public short[] dequant_uv = new short[16];

        /* 16 Y blocks, 4 U, 4 V, 1 DC 2nd order block, each with 16 entries. */
        public BLOCKD[] block = new BLOCKD[25];
        public uint fullpixel_mask;

        public YV12_BUFFER_CONFIG pre; /* Filtered copy of previous frame reconstruction */
        public YV12_BUFFER_CONFIG dst;

        public ArrPtr<MODE_INFO> mode_info_context;
        public int mode_info_stride;

        public FRAME_TYPE frame_type;

        public int up_available;
        public int left_available;

        public byte*[] recon_above = new byte*[3];
        public byte*[] recon_left = new byte*[3];
        public int[] recon_left_stride = new int[2];

        /* Y,U,V,Y2 */
        // Port AC: above_context gets used to point to elements in the VP8_COMMON.above_context array.
        public ArrPtr<ENTROPY_CONTEXT_PLANES> above_context;
        public ENTROPY_CONTEXT_PLANES left_context;

        /* 0 indicates segmentation at MB level is not enabled. Otherwise the
         * individual bits indicate which features are active. */
        public byte segmentation_enabled;

        /* 0 (do not update) 1 (update) the macroblock segmentation map. */
        public byte update_mb_segmentation_map;

        /* 0 (do not update) 1 (update) the macroblock segmentation feature data. */
        public byte update_mb_segmentation_data;

        /* 0 (do not update) 1 (update) the macroblock segmentation feature data. */
        public byte mb_segement_abs_delta;

        /* Per frame flags that define which MB level features (such as quantizer or
         * loop filter level) */
        /* are enabled and when enabled the probabilities used to decode the per MB
         * flags in MB_MODE_INFO */
        /* Probability Tree used to code Segment number */
        public vp8_prob[] mb_segment_tree_probs = new vp8_prob[MB_FEATURE_TREE_PROBS];
        /* Segment parameters */
        public sbyte[,] segment_feature_data = new sbyte[(int)MB_LVL_FEATURES.MB_LVL_MAX, MAX_MB_SEGMENTS];

        /* mode_based Loop filter adjustment */
        public byte mode_ref_lf_delta_enabled;
        public byte mode_ref_lf_delta_update;

        /* Delta values have the range +/- MAX_LOOP_FILTER */
        public sbyte[] last_ref_lf_deltas = new sbyte[MAX_REF_LF_DELTAS];    /* 0 = Intra, Last, GF, ARF */
        public sbyte[] ref_lf_deltas = new sbyte[MAX_REF_LF_DELTAS]; /* 0 = Intra, Last, GF, ARF */
        /* 0 = BPRED, ZERO_MV, MV, SPLIT */
        public sbyte[] last_mode_lf_deltas = new sbyte[MAX_MODE_LF_DELTAS];
        public sbyte[] mode_lf_deltas = new sbyte[MAX_MODE_LF_DELTAS]; /* 0 = BPRED, ZERO_MV, MV, SPLIT */

        /* Distance of MB away from frame edges */
        public int mb_to_left_edge;
        public int mb_to_right_edge;
        public int mb_to_top_edge;
        public int mb_to_bottom_edge;

        public vp8_subpix_fn_t subpixel_predict;
        public vp8_subpix_fn_t subpixel_predict8x4;
        public vp8_subpix_fn_t subpixel_predict8x8;
        public vp8_subpix_fn_t subpixel_predict16x16;

        // Port AC: Unknown why current_bc was declared as void*. Everywhere it's accessed in libvpx is 
        // to use as a boolean decoder.
        //public void* current_bc;
        public vp8_reader current_bc;

        public int corrupted;

        public vpx_internal_error_info error_info;
    }
}
