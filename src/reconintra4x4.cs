//-----------------------------------------------------------------------------
// Filename: reconintra4x4.cs
//
// Description: Port of:
//  - reconintra4x4.c
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

using System;
using System.Runtime.CompilerServices;

namespace Vpx.Net
{
    public unsafe static class reconintra4x4
    {
        static intra_pred_fn[] pred = new intra_pred_fn[10];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void intra_prediction_down_copy(MACROBLOCKD xd, byte* above_right_src)
        {
            int dst_stride = xd.dst.y_stride;
            byte* above_right_dst = xd.dst.y_buffer - dst_stride + 16;

            uint* src_ptr = (uint*)above_right_src;
            uint* dst_ptr0 = (uint*)(above_right_dst + 4 * dst_stride);
            uint* dst_ptr1 = (uint*)(above_right_dst + 8 * dst_stride);
            uint* dst_ptr2 = (uint*)(above_right_dst + 12 * dst_stride);

            *dst_ptr0 = *src_ptr;
            *dst_ptr1 = *src_ptr;
            *dst_ptr2 = *src_ptr;
        }

        public static void vp8_init_intra4x4_predictors_internal()
        {
            pred[(int)B_PREDICTION_MODE.B_DC_PRED] = vpx_dsp_rtcd.vpx_dc_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_TM_PRED] = vpx_dsp_rtcd.vpx_tm_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_VE_PRED] = vpx_dsp_rtcd.vpx_ve_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_HE_PRED] = vpx_dsp_rtcd.vpx_he_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_LD_PRED] = vpx_dsp_rtcd.vpx_d45e_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_RD_PRED] = vpx_dsp_rtcd.vpx_d135_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_VR_PRED] = vpx_dsp_rtcd.vpx_d117_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_VL_PRED] = vpx_dsp_rtcd.vpx_d63e_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_HD_PRED] = vpx_dsp_rtcd.vpx_d153_predictor_4x4;
            pred[(int)B_PREDICTION_MODE.B_HU_PRED] = vpx_dsp_rtcd.vpx_d207_predictor_4x4;
        }

        public unsafe static void vp8_intra4x4_predict(byte* above, byte* yleft,
                          int left_stride, B_PREDICTION_MODE b_mode,
                          byte* dst, int dst_stride,
                          byte top_left)
        {
            byte[] Aboveb = new byte[12];
            //byte * Above = Aboveb + 4;
            byte[] Left = new byte[4];

            Left[0] = yleft[0];
            Left[1] = yleft[left_stride];
            Left[2] = yleft[2 * left_stride];
            Left[3] = yleft[3 * left_stride];
            //memcpy(Above, above, 8);
            //Above[-1] = top_left;

            fixed (byte* pLeft = Left, pAboveb = Aboveb)
            {
                byte* Above = pAboveb + 4;
                //memcpy(Above, above, 8);
                Buffer.MemoryCopy(above, Above, 8, 8);
                Above[-1] = top_left;

                //DebugProbe.DumpAboveAndLeft(Aboveb, Left);

                pred[(int)b_mode](dst, dst_stride, Above, pLeft);
            }
        }
    }
}
