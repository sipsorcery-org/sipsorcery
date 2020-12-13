//-----------------------------------------------------------------------------
// Filename: vp8_loopfilter.cs
//
// Description: Port of:
//  - vp8_loopfilter.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public unsafe static class vp8_loopfilter
    {
        static void lf_init_lut(loop_filter_info_n lfi)
        {
            int filt_lvl;

            for (filt_lvl = 0; filt_lvl <= loopfilter.MAX_LOOP_FILTER; ++filt_lvl)
            {
                if (filt_lvl >= 40)
                {
                    lfi.hev_thr_lut[(int)FRAME_TYPE.KEY_FRAME, filt_lvl] = 2;
                    lfi.hev_thr_lut[(int)FRAME_TYPE.INTER_FRAME, filt_lvl] = 3;
                }
                else if (filt_lvl >= 20)
                {
                    lfi.hev_thr_lut[(int)FRAME_TYPE.KEY_FRAME, filt_lvl] = 1;
                    lfi.hev_thr_lut[(int)FRAME_TYPE.INTER_FRAME, filt_lvl] = 2;
                }
                else if (filt_lvl >= 15)
                {
                    lfi.hev_thr_lut[(int)FRAME_TYPE.KEY_FRAME, filt_lvl] = 1;
                    lfi.hev_thr_lut[(int)FRAME_TYPE.INTER_FRAME, filt_lvl] = 1;
                }
                else
                {
                    lfi.hev_thr_lut[(int)FRAME_TYPE.KEY_FRAME, filt_lvl] = 0;
                    lfi.hev_thr_lut[(int)FRAME_TYPE.INTER_FRAME, filt_lvl] = 0;
                }
            }

            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.DC_PRED] = 1;
            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.V_PRED] = 1;
            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.H_PRED] = 1;
            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.TM_PRED] = 1;
            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.B_PRED] = 0;

            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.ZEROMV] = 1;
            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.NEARESTMV] = 2;
            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.NEARMV] = 2;
            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.NEWMV] = 2;
            lfi.mode_lf_lut[(int)MB_PREDICTION_MODE.SPLITMV] = 3;
        }

        private static void vp8_loop_filter_update_sharpness(loop_filter_info_n lfi,
                                      int sharpness_lvl)
        {
            int i;

            /* For each possible value for the loop filter fill out limits */
            for (i = 0; i <= loopfilter.MAX_LOOP_FILTER; ++i)
            {
                int filt_lvl = i;
                int block_inside_limit = 0;

                /* Set loop filter paramaeters that control sharpness. */
                block_inside_limit = filt_lvl >> ((sharpness_lvl > 0) ? 1 : 0);
                block_inside_limit = block_inside_limit >> ((sharpness_lvl > 4) ? 1 : 0);

                if (sharpness_lvl > 0)
                {
                    if (block_inside_limit > (9 - sharpness_lvl))
                    {
                        block_inside_limit = (9 - sharpness_lvl);
                    }
                }

                if (block_inside_limit < 1) block_inside_limit = 1;

                //memset(lfi.lim[i], block_inside_limit, loopfilter.SIMD_WIDTH);
                //memset(lfi.blim[i], (2 * filt_lvl + block_inside_limit), loopfilter.SIMD_WIDTH);
                //memset(lfi.mblim[i], (2 * (filt_lvl + 2) + block_inside_limit),
                //       loopfilter.SIMD_WIDTH);

                for (int j = 0; j < loopfilter.SIMD_WIDTH; j++)
                {
                    lfi.lim[i, j] = (byte)block_inside_limit;
                    lfi.blim[i, j] = (byte)(2 * filt_lvl + block_inside_limit);
                    lfi.mblim[i, j] = (byte)(2 * (filt_lvl + 2) + block_inside_limit);
                }
            }
        }

        public static void vp8_loop_filter_init(VP8_COMMON cm)
        {
            loop_filter_info_n lfi = cm.lf_info;
            int i;

            /* init limits for given sharpness*/
            vp8_loop_filter_update_sharpness(lfi, cm.sharpness_level);
            cm.last_sharpness_level = cm.sharpness_level;

            /* init LUT for lvl  and hev thr picking */
            lf_init_lut(lfi);

            /* init hev threshold const vectors */
            for (i = 0; i < 4; ++i)
            {
                //memset(lfi.hev_thr[i], i, loopfilter.SIMD_WIDTH);
                for (int j = 0; j < loopfilter.SIMD_WIDTH; j++)
                {
                    lfi.hev_thr[i, j] = (byte)i;
                }
            }
        }

        public static void vp8_loop_filter_frame_init(VP8_COMMON cm, MACROBLOCKD mbd, int default_filt_lvl)
        {
            int seg,  /* segment number */
            @ref,  /* index in ref_lf_deltas */
            mode; /* index in mode_lf_deltas */

            loop_filter_info_n lfi = cm.lf_info;

            /* update limits if sharpness has changed */
            if (cm.last_sharpness_level != cm.sharpness_level)
            {
                vp8_loop_filter_update_sharpness(lfi, cm.sharpness_level);
                cm.last_sharpness_level = cm.sharpness_level;
            }

            for (seg = 0; seg < blockd.MAX_MB_SEGMENTS; ++seg)
            {
                int lvl_seg = default_filt_lvl;
                int lvl_ref, lvl_mode;

                /* Note the baseline filter values for each segment */
                if (mbd.segmentation_enabled > 0)
                {
                    if (mbd.mb_segement_abs_delta == blockd.SEGMENT_ABSDATA)
                    {
                        lvl_seg = mbd.segment_feature_data[(int)MB_LVL_FEATURES.MB_LVL_ALT_LF, seg];
                    }
                    else
                    { /* Delta Value */
                        lvl_seg += mbd.segment_feature_data[(int)MB_LVL_FEATURES.MB_LVL_ALT_LF, seg];
                    }
                    lvl_seg = (lvl_seg > 0) ? ((lvl_seg > 63) ? 63 : lvl_seg) : 0;
                }

                if (mbd.mode_ref_lf_delta_enabled == 0)
                {
                    /* we could get rid of this if we assume that deltas are set to
                     * zero when not in use; encoder always uses deltas
                     */
                    //memset(lfi->lvl[seg][0], lvl_seg, 4 * 4);
                    fixed(byte* plfi = lfi.lvl)
                    {
                        byte* pseg = plfi + seg * lfi.lvl.GetLength(1);
                        for(int i=0; i<4*4; i++)
                        {
                            *pseg++ = (byte)lvl_seg;
                        }
                    }

                    continue;
                }

                /* INTRA_FRAME */
                @ref = (int)MV_REFERENCE_FRAME.INTRA_FRAME;

                /* Apply delta for reference frame */
                lvl_ref = lvl_seg + mbd.ref_lf_deltas[@ref];

                /* Apply delta for Intra modes */
                mode = 0; /* B_PRED */
                /* Only the split mode BPRED has a further special case */
                lvl_mode = lvl_ref + mbd.mode_lf_deltas[mode];
                /* clamp */
                lvl_mode = (lvl_mode > 0) ? (lvl_mode > 63 ? 63 : lvl_mode) : 0;

                lfi.lvl[seg, @ref, mode] = (byte)lvl_mode;

                mode = 1; /* all the rest of Intra modes */
                /* clamp */
                lvl_mode = (lvl_ref > 0) ? (lvl_ref > 63 ? 63 : lvl_ref) : 0;
                lfi.lvl[seg, @ref, mode] = (byte)lvl_mode;

                /* LAST, GOLDEN, ALT */
                for (@ref = 1; @ref < (int)MV_REFERENCE_FRAME.MAX_REF_FRAMES; ++@ref)
                {
                    /* Apply delta for reference frame */
                    lvl_ref = lvl_seg + mbd.ref_lf_deltas[@ref];

                    /* Apply delta for Inter modes */
                    for (mode = 1; mode < 4; ++mode)
                    {
                        lvl_mode = lvl_ref + mbd.mode_lf_deltas[mode];
                        /* clamp */
                        lvl_mode = (lvl_mode > 0) ? (lvl_mode > 63 ? 63 : lvl_mode) : 0;

                        lfi.lvl[seg, @ref, mode] = (byte)lvl_mode;
                    }
                }
            }
        }

        public unsafe static void vp8_loop_filter_row_normal(VP8_COMMON cm, ArrPtr<MODE_INFO> mode_info_context,
                            int mb_row, int post_ystride, int post_uvstride,
                            byte* y_ptr, byte* u_ptr,
                            byte* v_ptr)
        {
            int mb_col;
            int filter_level;
            loop_filter_info_n lfi_n = cm.lf_info;
            loop_filter_info lfi = new loop_filter_info();
            FRAME_TYPE frame_type = cm.frame_type;

            for (mb_col = 0; mb_col < cm.mb_cols; ++mb_col)
            {
                int skip_lf = (mode_info_context.get().mbmi.mode != (int)MB_PREDICTION_MODE.B_PRED &&
                               mode_info_context.get().mbmi.mode != (int)MB_PREDICTION_MODE.SPLITMV &&
                               mode_info_context.get().mbmi.mb_skip_coeff > 0) ? 1 : 0;

                int mode_index = lfi_n.mode_lf_lut[mode_info_context.get().mbmi.mode];
                int seg = mode_info_context.get().mbmi.segment_id;
                int ref_frame = mode_info_context.get().mbmi.ref_frame;

                filter_level = lfi_n.lvl[seg, ref_frame, mode_index];

                if (filter_level > 0)
                {
                    int hev_index = lfi_n.hev_thr_lut[(int)frame_type, filter_level];

                    //lfi.mblim = lfi_n.mblim[filter_level];
                    //lfi.blim = lfi_n.blim[filter_level];
                    //lfi.lim = lfi_n.lim[filter_level];
                    //lfi.hev_thr = lfi_n.hev_thr[hev_index];

                    fixed (byte* pMblin = lfi_n.mblim, pBlim = lfi_n.blim, pLim = lfi_n.lim, pHev_thr = lfi_n.hev_thr)
                    {
                        lfi.mblim = pMblin + lfi_n.mblim.GetLength(1) * filter_level;
                        lfi.blim = pBlim + lfi_n.blim.GetLength(1) * filter_level;
                        lfi.lim = pLim + lfi_n.lim.GetLength(1) * filter_level;
                        lfi.hev_thr = pHev_thr + lfi_n.hev_thr.GetLength(1) * hev_index;

                        if (mb_col > 0)
                        {
                            vp8_rtcd.vp8_loop_filter_mbv(y_ptr, u_ptr, v_ptr, post_ystride, post_uvstride, lfi);
                        }

                        if (skip_lf == 0)
                        {
                            vp8_rtcd.vp8_loop_filter_bv(y_ptr, u_ptr, v_ptr, post_ystride, post_uvstride, lfi);
                        }

                        /* don't apply across umv border */
                        if (mb_row > 0)
                        {
                            vp8_rtcd.vp8_loop_filter_mbh(y_ptr, u_ptr, v_ptr, post_ystride, post_uvstride, lfi);
                        }

                        if (skip_lf == 0)
                        {
                            vp8_rtcd.vp8_loop_filter_bh(y_ptr, u_ptr, v_ptr, post_ystride, post_uvstride, lfi);
                        }
                    }
                }

                y_ptr += 16;
                u_ptr += 8;
                v_ptr += 8;

                mode_info_context++; /* step to next MB */
            }
        }

        public unsafe static void vp8_loop_filter_row_simple(VP8_COMMON cm, ArrPtr<MODE_INFO> mode_info_context,
                                        int mb_row, int post_ystride,
                                        byte* y_ptr)
        {
            int mb_col;
            int filter_level;
            loop_filter_info_n lfi_n = cm.lf_info;

            for (mb_col = 0; mb_col < cm.mb_cols; ++mb_col)
            {
                int skip_lf = (mode_info_context.get().mbmi.mode != (int)MB_PREDICTION_MODE.B_PRED &&
                               mode_info_context.get().mbmi.mode != (int)MB_PREDICTION_MODE.SPLITMV &&
                               mode_info_context.get().mbmi.mb_skip_coeff > 0) ? 1 : 0;

                int mode_index = lfi_n.mode_lf_lut[mode_info_context.get().mbmi.mode];
                int seg = mode_info_context.get().mbmi.segment_id;
                int ref_frame = mode_info_context.get().mbmi.ref_frame;

                filter_level = lfi_n.lvl[seg, ref_frame, mode_index];

                if (filter_level > 0)
                {
                    if (mb_col > 0)
                    {
                        //vp8_rtcd.vp8_loop_filter_simple_mbv(y_ptr, post_ystride, lfi_n.mblim[filter_level]);
                        fixed (byte* pMBlim = lfi_n.mblim)
                        {
                            int step = filter_level * lfi_n.mblim.GetLength(1);
                            vp8_rtcd.vp8_loop_filter_simple_mbv(y_ptr, post_ystride, pMBlim + step);
                        }
                    }

                    if (skip_lf == 0)
                    {
                        //vp8_rtcd.vp8_loop_filter_simple_bv(y_ptr, post_ystride, lfi_n.blim[filter_level]);
                        fixed (byte* pBlim = lfi_n.blim)
                        {
                            int step = lfi_n.blim.GetLength(1) * filter_level;
                            vp8_rtcd.vp8_loop_filter_simple_bv(y_ptr, post_ystride, pBlim + step);
                        }
                    }

                    /* don't apply across umv border */
                    if (mb_row > 0)
                    {
                        //vp8_rtcd.vp8_loop_filter_simple_mbh(y_ptr, post_ystride, lfi_n.mblim[filter_level]);
                        fixed (byte* pMBlim = lfi_n.mblim)
                        {
                            int step = lfi_n.mblim.GetLength(1) * filter_level;
                            vp8_rtcd.vp8_loop_filter_simple_mbh(y_ptr, post_ystride, pMBlim + step);
                        }
                    }

                    if (skip_lf == 0)
                    {
                        //vp8_rtcd.vp8_loop_filter_simple_bh(y_ptr, post_ystride, lfi_n.blim[filter_level]);
                        fixed (byte* pBlim = lfi_n.blim)
                        {
                            int step = lfi_n.blim.GetLength(1) * filter_level;
                            vp8_rtcd.vp8_loop_filter_simple_bh(y_ptr, post_ystride, pBlim + step);
                        }
                    }
                }

                y_ptr += 16;

                mode_info_context++; /* step to next MB */
            }
        }
    }
}
