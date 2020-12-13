//-----------------------------------------------------------------------------
// Filename: findnearmv.cs
//
// Description: Port of:
//  - findnearmv.h
//  - findnearmv.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
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

using System.Runtime.CompilerServices;

namespace Vpx.Net
{
    public static class findnearmv
    {
        public const int LEFT_TOP_MARGIN = (16 << 3);
        public const int RIGHT_BOTTOM_MARGIN = (16 << 3);

        public static readonly byte[,] vp8_mbsplit_offset = new byte[4,16]{
          { 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
          { 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
          { 0, 2, 8, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
          { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void mv_bias(int refmb_ref_frame_sign_bias, int refframe,
                           ref int_mv mvp, in int[] ref_frame_sign_bias)
        {
            if (refmb_ref_frame_sign_bias != ref_frame_sign_bias[refframe])
            {
                mvp.as_mv.row *= -1;
                mvp.as_mv.col *= -1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static B_PREDICTION_MODE left_block_mode(in ArrPtr<MODE_INFO> cur_mb, int b)
        {
            if ((b & 3) == 0)
            {
                /* On L edge, get from MB to left of us */
                var copy_cur_mb = cur_mb;
                --copy_cur_mb;
                switch (copy_cur_mb.get().mbmi.mode)
                {
                    //case (int)MB_PREDICTION_MODE.B_PRED: return (cur_mb.bmi + b + 3)->as_mode;
                    case (int)MB_PREDICTION_MODE.B_PRED: return copy_cur_mb.get().bmi[b + 3].as_mode;
                    case (int)MB_PREDICTION_MODE.DC_PRED: return B_PREDICTION_MODE.B_DC_PRED;
                    case (int)MB_PREDICTION_MODE.V_PRED: return B_PREDICTION_MODE.B_VE_PRED;
                    case (int)MB_PREDICTION_MODE.H_PRED: return B_PREDICTION_MODE.B_HE_PRED;
                    case (int)MB_PREDICTION_MODE.TM_PRED: return B_PREDICTION_MODE.B_TM_PRED;
                    default: return B_PREDICTION_MODE.B_DC_PRED;
                }
            }

            //return (cur_mb.bmi + b - 1)->as_mode;
            return cur_mb.get().bmi[b - 1].as_mode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static B_PREDICTION_MODE above_block_mode(in ArrPtr<MODE_INFO> cur_mb, int b, int mi_stride)
        {
            if ((b >> 2) == 0)
            {
                /* On top edge, get from MB above us */
                var copy_cur_mb = cur_mb;
                copy_cur_mb -= mi_stride;

                switch (copy_cur_mb.get().mbmi.mode)
                {
                    //case (int)MB_PREDICTION_MODE.B_PRED: return (cur_mb->bmi + b + 12)->as_mode;
                    case (int)MB_PREDICTION_MODE.B_PRED: return copy_cur_mb.get().bmi[b + 12].as_mode;
                    case (int)MB_PREDICTION_MODE.DC_PRED: return B_PREDICTION_MODE.B_DC_PRED;
                    case (int)MB_PREDICTION_MODE.V_PRED: return B_PREDICTION_MODE.B_VE_PRED;
                    case (int)MB_PREDICTION_MODE.H_PRED: return B_PREDICTION_MODE.B_HE_PRED;
                    case (int)MB_PREDICTION_MODE.TM_PRED: return B_PREDICTION_MODE.B_TM_PRED;
                    default: return B_PREDICTION_MODE.B_DC_PRED;
                }
            }

            //return (cur_mb.bmi + b - 4)->as_mode;
            return cur_mb.get().bmi[b - 4].as_mode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void vp8_clamp_mv2(ref int_mv mv, in MACROBLOCKD xd)
        {
            if (mv.as_mv.col < (xd.mb_to_left_edge - LEFT_TOP_MARGIN))
            {
                mv.as_mv.col = (short)(xd.mb_to_left_edge - LEFT_TOP_MARGIN);
            }
            else if (mv.as_mv.col > xd.mb_to_right_edge + RIGHT_BOTTOM_MARGIN)
            {
                mv.as_mv.col = (short)(xd.mb_to_right_edge + RIGHT_BOTTOM_MARGIN);
            }

            if (mv.as_mv.row < (xd.mb_to_top_edge - LEFT_TOP_MARGIN))
            {
                mv.as_mv.row = (short)(xd.mb_to_top_edge - LEFT_TOP_MARGIN);
            }
            else if (mv.as_mv.row > xd.mb_to_bottom_edge + RIGHT_BOTTOM_MARGIN)
            {
                mv.as_mv.row = (short)(xd.mb_to_bottom_edge + RIGHT_BOTTOM_MARGIN);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint vp8_check_mv_bounds(int_mv mv, int mb_to_left_edge,
                                               int mb_to_right_edge,
                                               int mb_to_top_edge,
                                               int mb_to_bottom_edge)
        {
            uint need_to_clamp;
            need_to_clamp = (mv.as_mv.col < mb_to_left_edge) ? 1U : 0;
            need_to_clamp |= (mv.as_mv.col > mb_to_right_edge) ? 1U : 0;
            need_to_clamp |= (mv.as_mv.row < mb_to_top_edge) ? 1U : 0;
            need_to_clamp |= (mv.as_mv.row > mb_to_bottom_edge) ? 1U : 0;
            return need_to_clamp;
        }
    }
}
