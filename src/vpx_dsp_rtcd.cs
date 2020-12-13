//-----------------------------------------------------------------------------
// Filename: vpx_dsp_rtcd.cs
//
// Description: Port of:
//  - vpx_dsp_rtcd.h
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

using ptrdiff_t = System.Int64;

namespace Vpx.Net
{
    /// <remarks>
    /// Port AC: The 4x4, 8x8 and 16x16 functions are mostly defined in the C source using the
    /// "intra_pred_allsizes" macro in intraped.c. Searching on the function names will only find the 
    /// seemingly undefined header.
    /// </remarks>
    public unsafe static class vpx_dsp_rtcd
    {
        #region 16x16.

        //void vpx_v_predictor_16x16_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_v_predictor_16x16 vpx_v_predictor_16x16_c

        public static void vpx_v_predictor_16x16(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_v_predictor_16x16_c(dst, stride, above, left);

        //void vpx_h_predictor_16x16_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_h_predictor_16x16 vpx_h_predictor_16x16_c

        public static void vpx_h_predictor_16x16(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_h_predictor_16x16_c(dst, stride, above, left);

        //void vpx_tm_predictor_16x16_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_tm_predictor_16x16 vpx_tm_predictor_16x16_c

        public static void vpx_tm_predictor_16x16(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_tm_predictor_16x16_c(dst, stride, above, left);

        //void vpx_dc_128_predictor_16x16_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_dc_128_predictor_16x16 vpx_dc_128_predictor_16x16_c

        public static void vpx_dc_128_predictor_16x16(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_128_predictor_16x16_c(dst, stride, above, left);

        //void vpx_dc_top_predictor_16x16_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_dc_top_predictor_16x16 vpx_dc_top_predictor_16x16_c

        public static void vpx_dc_top_predictor_16x16(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_top_predictor_16x16_c(dst, stride, above, left);

        // void vpx_dc_left_predictor_16x16_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        // #define vpx_dc_left_predictor_16x16 vpx_dc_left_predictor_16x16_c

        public static void vpx_dc_left_predictor_16x16(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_left_predictor_16x16_c(dst, stride, above, left);

        //void vpx_dc_predictor_16x16_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_dc_predictor_16x16 vpx_dc_predictor_16x16_c

        public static void vpx_dc_predictor_16x16(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_predictor_16x16_c(dst, stride, above, left);

        #endregion

        #region 8x8.

        //void vpx_v_predictor_8x8_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_v_predictor_8x8 vpx_v_predictor_8x8_c

        public static void vpx_v_predictor_8x8(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_v_predictor_8x8_c(dst, stride, above, left);

        //void vpx_h_predictor_8x8_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_h_predictor_8x8 vpx_h_predictor_8x8_c

        public static void vpx_h_predictor_8x8(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_h_predictor_8x8_c(dst, stride, above, left);

        //void vpx_tm_predictor_8x8_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_tm_predictor_8x8 vpx_tm_predictor_8x8_c

        public static void vpx_tm_predictor_8x8(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_tm_predictor_8x8_c(dst, stride, above, left);

        //void vpx_dc_128_predictor_8x8_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_dc_128_predictor_8x8 vpx_dc_128_predictor_8x8_c

        public static void vpx_dc_128_predictor_8x8(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_128_predictor_8x8_c(dst, stride, above, left);

        //void vpx_dc_top_predictor_8x8_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_dc_top_predictor_8x8 vpx_dc_top_predictor_8x8_c

        public static void vpx_dc_top_predictor_8x8(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_top_predictor_8x8_c(dst, stride, above, left);

        // void vpx_dc_left_predictor_8x8_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        // #define vpx_dc_left_predictor_8x8 vpx_dc_left_predictor_8x8_c

        public static void vpx_dc_left_predictor_8x8(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_left_predictor_8x8_c(dst, stride, above, left);

        //void vpx_dc_predictor_8x8_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_dc_predictor_8x8 vpx_dc_predictor_8x8_c

        public static void vpx_dc_predictor_8x8(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_predictor_8x8_c(dst, stride, above, left);

        #endregion

        //void vpx_d207_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_d207_predictor_4x4 vpx_d207_predictor_4x4_c

        public static void vpx_d207_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_d207_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_d207_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_d207_predictor_4x4_c(dst, stride, above, left);

        // void vpx_dc_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        // #define vpx_dc_predictor_4x4 vpx_dc_predictor_4x4_c

        public static void vpx_dc_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_dc_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_dc_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_dc_predictor_4x4_c(dst, stride, above, left);

        //void vpx_tm_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_tm_predictor_4x4 vpx_tm_predictor_4x4_c

        public static void vpx_tm_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_tm_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_tm_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
         => intraped.vpx_tm_predictor_4x4_c(dst, stride, above, left);

        //void vpx_ve_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_ve_predictor_4x4 vpx_ve_predictor_4x4_c

        public static void vpx_ve_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_ve_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_ve_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_ve_predictor_4x4_c(dst, stride, above, left);

        //void vpx_he_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_he_predictor_4x4 vpx_he_predictor_4x4_c

        public static void vpx_he_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_he_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_he_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_he_predictor_4x4_c(dst, stride, above, left);

        //void vpx_d45e_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_d45e_predictor_4x4 vpx_d45e_predictor_4x4_c

        public static void vpx_d45e_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_d45e_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_d45e_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_d45e_predictor_4x4_c(dst, stride, above, left);

        //void vpx_d135_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_d135_predictor_4x4 vpx_d135_predictor_4x4_c

        public static void vpx_d135_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_d135_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_d135_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_d135_predictor_4x4_c(dst, stride, above, left);

        //void vpx_d117_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_d117_predictor_4x4 vpx_d117_predictor_4x4_c

        public static void vpx_d117_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_d117_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_d117_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_d117_predictor_4x4_c(dst, stride, above, left);

        //void vpx_d63e_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_d63e_predictor_4x4 vpx_d63e_predictor_4x4_c

        public static void vpx_d63e_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_d63e_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_d63e_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_d63e_predictor_4x4_c(dst, stride, above, left);

        //void vpx_d153_predictor_4x4_c(uint8_t* dst, ptrdiff_t stride, const uint8_t* above, const uint8_t* left);
        //#define vpx_d153_predictor_4x4 vpx_d153_predictor_4x4_c

        public static void vpx_d153_predictor_4x4(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => vpx_d153_predictor_4x4_c(dst, stride, above, left);

        public static void vpx_d153_predictor_4x4_c(byte* dst, ptrdiff_t stride, byte* above, byte* left)
            => intraped.vpx_d153_predictor_4x4_c(dst, stride, above, left);
    }
}
