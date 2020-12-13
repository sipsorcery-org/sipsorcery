//-----------------------------------------------------------------------------
// Filename: reconintra.cs
//
// Description: Port of:
//  - reconintra.c
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

using System.Runtime.CompilerServices;

using ptrdiff_t = System.Int64;

[assembly: InternalsVisibleTo("VP8.Net.UnitTest")]

namespace Vpx.Net
{
    public unsafe delegate void intra_pred_fn(byte* dst, ptrdiff_t stride, byte* above, byte* left);

    public unsafe static class reconintra
    {
        internal enum PredictionSizes
        {
            SIZE_16,
            SIZE_8,
            NUM_SIZES,
        };

        internal static intra_pred_fn[,] pred = new intra_pred_fn[4, (int)PredictionSizes.NUM_SIZES];
        internal static intra_pred_fn[,,] dc_pred = new intra_pred_fn[2, 2, (int)PredictionSizes.NUM_SIZES];

        static volatile bool _arePredictorsInitialised;

        public static void vp8_init_intra_predictors()
        {
            if (!_arePredictorsInitialised)
            {
                _arePredictorsInitialised = true;
                vp8_init_intra_predictors_internal();
            }
        }

        private unsafe static void vp8_init_intra_predictors_internal()
        {
            //#define INIT_SIZE(sz)                                           \
            //            pred[V_PRED][SIZE_##sz] = vpx_v_predictor_##sz##x##sz;        \
            //  pred[H_PRED][SIZE_##sz] = vpx_h_predictor_##sz##x##sz;        \
            //  pred[TM_PRED][SIZE_##sz] = vpx_tm_predictor_##sz##x##sz;      \
            //                                                                \

            //            dc_pred[0][0][SIZE_##sz] = vpx_dc_128_predictor_##sz##x##sz;  \
            //  dc_pred[0][1][SIZE_##sz] = vpx_dc_top_predictor_##sz##x##sz;  \
            //  dc_pred[1][0][SIZE_##sz] = vpx_dc_left_predictor_##sz##x##sz; \
            //  dc_pred[1][1][SIZE_##sz] = vpx_dc_predictor_##sz##x##sz

            //INIT_SIZE(16);

            pred[(int)MB_PREDICTION_MODE.V_PRED, (int)PredictionSizes.SIZE_16] = vpx_dsp_rtcd.vpx_v_predictor_16x16;
            pred[(int)MB_PREDICTION_MODE.H_PRED, (int)PredictionSizes.SIZE_16] = vpx_dsp_rtcd.vpx_h_predictor_16x16;
            pred[(int)MB_PREDICTION_MODE.TM_PRED, (int)PredictionSizes.SIZE_16] = vpx_dsp_rtcd.vpx_tm_predictor_16x16;

            dc_pred[0, 0, (int)PredictionSizes.SIZE_16] = vpx_dsp_rtcd.vpx_dc_128_predictor_16x16;
            dc_pred[0, 1, (int)PredictionSizes.SIZE_16] = vpx_dsp_rtcd.vpx_dc_top_predictor_16x16;
            dc_pred[1, 0, (int)PredictionSizes.SIZE_16] = vpx_dsp_rtcd.vpx_dc_left_predictor_16x16;
            dc_pred[1, 1, (int)PredictionSizes.SIZE_16] = vpx_dsp_rtcd.vpx_dc_predictor_16x16;

            //INIT_SIZE(8);

            pred[(int)MB_PREDICTION_MODE.V_PRED, (int)PredictionSizes.SIZE_8] = vpx_dsp_rtcd.vpx_v_predictor_8x8;
            pred[(int)MB_PREDICTION_MODE.H_PRED, (int)PredictionSizes.SIZE_8] = vpx_dsp_rtcd.vpx_h_predictor_8x8;
            pred[(int)MB_PREDICTION_MODE.TM_PRED, (int)PredictionSizes.SIZE_8] = vpx_dsp_rtcd.vpx_tm_predictor_8x8;

            dc_pred[0, 0, (int)PredictionSizes.SIZE_8] = vpx_dsp_rtcd.vpx_dc_128_predictor_8x8;
            dc_pred[0, 1, (int)PredictionSizes.SIZE_8] = vpx_dsp_rtcd.vpx_dc_top_predictor_8x8;
            dc_pred[1, 0, (int)PredictionSizes.SIZE_8] = vpx_dsp_rtcd.vpx_dc_left_predictor_8x8;
            dc_pred[1, 1, (int)PredictionSizes.SIZE_8] = vpx_dsp_rtcd.vpx_dc_predictor_8x8;

            reconintra4x4.vp8_init_intra4x4_predictors_internal();
        }

        public static void vp8_build_intra_predictors_mby_s(MACROBLOCKD x, byte* yabove_row,
                                      byte* yleft, int left_stride,
                                      byte* ypred_ptr, int y_stride)
        {
            MB_PREDICTION_MODE mode = (MB_PREDICTION_MODE)x.mode_info_context.get().mbmi.mode;
            //DECLARE_ALIGNED(16, uint8_t, yleft_col[16]);
            byte* yleft_col = stackalloc byte[16];
            int i;
            intra_pred_fn fn;

            for (i = 0; i < 16; ++i)
            {
                yleft_col[i] = yleft[i * left_stride];
            }

            if (mode == MB_PREDICTION_MODE.DC_PRED)
            {
                fn = dc_pred[x.left_available, x.up_available, (int)PredictionSizes.SIZE_16];
            }
            else
            {
                fn = pred[(int)mode, (int)PredictionSizes.SIZE_16];
            }

            fn(ypred_ptr, y_stride, yabove_row, yleft_col);
        }

        public static void vp8_build_intra_predictors_mbuv_s(
            MACROBLOCKD x, byte* uabove_row, byte* vabove_row,
            byte* uleft, byte* vleft, int left_stride,
            byte* upred_ptr, byte* vpred_ptr, int pred_stride)
        {
            MB_PREDICTION_MODE uvmode = (MB_PREDICTION_MODE)x.mode_info_context.get().mbmi.uv_mode;

            byte* uleft_col = stackalloc byte[8];
            byte* vleft_col = stackalloc byte[8];

            int i;
            intra_pred_fn fn;

            for (i = 0; i < 8; ++i)
            {
                uleft_col[i] = uleft[i * left_stride];
                vleft_col[i] = vleft[i * left_stride];
            }

            if (uvmode == MB_PREDICTION_MODE.DC_PRED)
            {
                fn = dc_pred[x.left_available, x.up_available, (int)PredictionSizes.SIZE_8];
            }
            else
            {
                fn = pred[(int)uvmode, (int)PredictionSizes.SIZE_8];
            }

            fn(upred_ptr, pred_stride, uabove_row, uleft_col);
            fn(vpred_ptr, pred_stride, vabove_row, vleft_col);
        }
    }
}
