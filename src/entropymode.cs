//-----------------------------------------------------------------------------
// Filename: entropymode.cs
//
// Description: Port of:
//  - entropymode.c
//  - entropymode.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 28 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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
using vp8_tree_index = System.SByte;

namespace Vpx.Net
{
    public static class entropymode
    {
        private const int SUBMVREF_COUNT = 5;

        private static readonly vp8_prob[,] vp8_sub_mv_ref_prob2 =
        {
              { 147, 136, 18 },
              { 106, 145, 1 },
              { 179, 121, 1 },
              { 223, 1, 34 },
              { 208, 1, 1 }
        };

        private static readonly vp8_prob[] sub_mv_ref_prob = { 180, 162, 25 };

        /* Again, these trees use the same probability indices as their
           explicitly-programmed predecessors. */

        public static readonly vp8_tree_index[] vp8_ymode_tree = {
            -1 * (int)MB_PREDICTION_MODE.DC_PRED, 2, 4, 6, -1 * (int)MB_PREDICTION_MODE.V_PRED,
            -1 * (int)MB_PREDICTION_MODE.H_PRED, -1 * (int)MB_PREDICTION_MODE.TM_PRED, -1 * (int)MB_PREDICTION_MODE.B_PRED
        };

        public static readonly vp8_tree_index[] vp8_kf_ymode_tree = {
            -1 * (int)MB_PREDICTION_MODE.B_PRED, 2, 4, 6,
            -1 * (int)MB_PREDICTION_MODE.DC_PRED, -1 * (int)MB_PREDICTION_MODE.V_PRED,
            -1 * (int)MB_PREDICTION_MODE.H_PRED, -1 * (int)MB_PREDICTION_MODE.TM_PRED };

        public static readonly vp8_tree_index[] vp8_uv_mode_tree = {
            -1 * (int)MB_PREDICTION_MODE.DC_PRED, 2,
            -1 * (int)MB_PREDICTION_MODE.V_PRED, 4,
            -1 * (int)MB_PREDICTION_MODE.H_PRED, -1 * (int)MB_PREDICTION_MODE.TM_PRED };

        public static readonly vp8_tree_index[] vp8_mbsplit_tree = { -3, 2, -2, 4, -0, -1 };

        public static readonly vp8_tree_index[] vp8_mv_ref_tree = {
            -1 * (int)MB_PREDICTION_MODE.ZEROMV, 2, -1 * (int)MB_PREDICTION_MODE.NEARESTMV, 4,
            -1 * (int)MB_PREDICTION_MODE.NEARMV, 6, -1 * (int)MB_PREDICTION_MODE.NEWMV,
            -1 * (int)MB_PREDICTION_MODE.SPLITMV };

        public static readonly vp8_tree_index[] vp8_sub_mv_ref_tree = {
            -1 * (int)B_PREDICTION_MODE.LEFT4X4, 2, -1 * (int)B_PREDICTION_MODE.ABOVE4X4, 4,
            -1 * (int)B_PREDICTION_MODE.ZERO4X4,  -1 * (int)B_PREDICTION_MODE.NEW4X4 };

        public static readonly vp8_tree_index[] vp8_small_mvtree = { 2,  8,  4,  6,  -0, -1, -2,
                                              -3, 10, 12, -4, -5, -6, -7 };

        public static readonly vp8_tree_index[] vp8_bmode_tree = /* INTRAMODECONTEXTNODE value */
            {
              -1 * (int)B_PREDICTION_MODE.B_DC_PRED, 2,          /* 0 = DC_NODE */
              -1 * (int)B_PREDICTION_MODE.B_TM_PRED, 4,          /* 1 = TM_NODE */
              -1 * (int)B_PREDICTION_MODE.B_VE_PRED, 6,          /* 2 = VE_NODE */
              8,          12,         /* 3 = COM_NODE */
              -1 * (int)B_PREDICTION_MODE.B_HE_PRED, 10,         /* 4 = HE_NODE */
              -1 * (int)B_PREDICTION_MODE.B_RD_PRED, -1 * (int)B_PREDICTION_MODE.B_VR_PRED, /* 5 = RD_NODE */
              -1 * (int)B_PREDICTION_MODE.B_LD_PRED, 14,         /* 6 = LD_NODE */
              -1 * (int)B_PREDICTION_MODE.B_VL_PRED, 16,         /* 7 = VL_NODE */
              -1 * (int)B_PREDICTION_MODE.B_HD_PRED,-1 * (int)B_PREDICTION_MODE.B_HU_PRED  /* 8 = HD_NODE */
            };

        public static void vp8_init_mbmode_probs(VP8_COMMON x)
        {
            //memcpy(x.fc.ymode_prob, vp8_entropymodeldata.vp8_ymode_prob, sizeof(vp8_ymode_prob));
            //memcpy(x.fc.uv_mode_prob, vp8_entropymodeldata.vp8_uv_mode_prob, sizeof(vp8_uv_mode_prob));
            //memcpy(x.fc.sub_mv_ref_prob, sub_mv_ref_prob, sizeof(sub_mv_ref_prob));

            Array.Copy(vp8_entropymodedata.vp8_ymode_prob, x.fc.ymode_prob, vp8_entropymodedata.vp8_ymode_prob.Length);
            Array.Copy(vp8_entropymodedata.vp8_uv_mode_prob, x.fc.uv_mode_prob, vp8_entropymodedata.vp8_uv_mode_prob.Length);
            Array.Copy(sub_mv_ref_prob, x.fc.sub_mv_ref_prob, x.fc.sub_mv_ref_prob.Length);
        }

        public static void vp8_default_bmode_probs(vp8_prob[] dest)
        {
            //memcpy(dest, vp8_bmode_prob, sizeof(vp8_bmode_prob));
            Array.Copy(vp8_entropymodedata.vp8_bmode_prob, dest, vp8_entropymodedata.vp8_bmode_prob.Length);
        }
    }
}
