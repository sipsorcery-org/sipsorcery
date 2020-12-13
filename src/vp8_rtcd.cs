//-----------------------------------------------------------------------------
// Filename: vp8_rtcd.cs
//
// Description: Port of:
//  - vp8_rtcd.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// Halloween 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public unsafe static class vp8_rtcd
    {
        //void vp8_sixtap_predict4x4_c(unsigned char* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, unsigned char* dst_ptr, int dst_pitch);
        //#define vp8_sixtap_predict4x4 vp8_sixtap_predict4x4_c

        public static void vp8_sixtap_predict4x4(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
            => vp8_sixtap_predict4x4_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);

        public static void vp8_sixtap_predict4x4_c(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
        {
            filter.vp8_sixtap_predict4x4_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);
        }

        //void vp8_sixtap_predict8x4_c(unsigned char* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, unsigned char* dst_ptr, int dst_pitch);
        //#define vp8_sixtap_predict8x4 vp8_sixtap_predict8x4_c

        public static void vp8_sixtap_predict8x4(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
            => vp8_sixtap_predict8x4_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);

        public static void vp8_sixtap_predict8x4_c(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
        {
            filter.vp8_sixtap_predict8x4_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);
        }

        //void vp8_sixtap_predict8x8_c(unsigned char* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, unsigned char* dst_ptr, int dst_pitch);
        //#define vp8_sixtap_predict8x8 vp8_sixtap_predict8x8_c

        public static void vp8_sixtap_predict8x8(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
            => vp8_sixtap_predict8x8_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);

        public static void vp8_sixtap_predict8x8_c(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
        {
            filter.vp8_sixtap_predict8x8_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);
        }

        //void vp8_sixtap_predict16x16_c(unsigned char* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, unsigned char* dst_ptr, int dst_pitch);
        //#define vp8_sixtap_predict16x16 vp8_sixtap_predict16x16_c

        public static void vp8_sixtap_predict16x16(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
            => vp8_sixtap_predict16x16_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);

        public static void vp8_sixtap_predict16x16_c(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
        {
            filter.vp8_sixtap_predict16x16_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);
        }

        //void vp8_bilinear_predict16x16_c(unsigned char* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, unsigned char* dst_ptr, int dst_pitch);
        //#define vp8_bilinear_predict16x16 vp8_bilinear_predict16x16_c

        public static void vp8_bilinear_predict16x16(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
            => vp8_bilinear_predict16x16_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);

        public static void vp8_bilinear_predict16x16_c(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
        {
            filter.vp8_bilinear_predict16x16_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);
        }

        //void vp8_bilinear_predict4x4_c(unsigned char* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, unsigned char* dst_ptr, int dst_pitch);
        //#define vp8_bilinear_predict4x4 vp8_bilinear_predict4x4_c

        public static void vp8_bilinear_predict4x4(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
            => vp8_bilinear_predict4x4_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);

        public static void vp8_bilinear_predict4x4_c(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
        {
            filter.vp8_bilinear_predict4x4_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);
        }

        //void vp8_bilinear_predict8x4_c(unsigned char* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, unsigned char* dst_ptr, int dst_pitch);
        //#define vp8_bilinear_predict8x4 vp8_bilinear_predict8x4_c

        public static void vp8_bilinear_predict8x4(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
           => vp8_bilinear_predict8x4_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);

        public static void vp8_bilinear_predict8x4_c(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
        {
            filter.vp8_bilinear_predict8x4_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);
        }

        //void vp8_bilinear_predict8x8_c(unsigned char* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, unsigned char* dst_ptr, int dst_pitch);
        //#define vp8_bilinear_predict8x8 vp8_bilinear_predict8x8_c

        public static void vp8_bilinear_predict8x8(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
            => vp8_bilinear_predict8x8_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);

        public static void vp8_bilinear_predict8x8_c(byte* src_ptr, int src_pixels_per_line, int xoffset, int yoffset, byte* dst_ptr, int dst_pitch)
        {
            filter.vp8_bilinear_predict8x8_c(src_ptr, src_pixels_per_line, xoffset, yoffset, dst_ptr, dst_pitch);
        }

        //void vp8_loop_filter_mbv_c(unsigned char* y_ptr, unsigned char* u_ptr, unsigned char* v_ptr, int y_stride, int uv_stride, struct loop_filter_info *lfi);
        //#define vp8_loop_filter_mbv vp8_loop_filter_mbv_c

        public static void vp8_loop_filter_mbv(byte* y_ptr, byte* u_ptr, byte* v_ptr, int y_stride, int uv_stride, loop_filter_info lfi)
            => vp8_loop_filter_mbv_c(y_ptr, u_ptr, v_ptr, y_stride, uv_stride, lfi);

        public static void vp8_loop_filter_mbv_c(byte* y_ptr, byte* u_ptr, byte* v_ptr, int y_stride, int uv_stride, loop_filter_info lfi)
        {
            loopfilter_filters.vp8_loop_filter_mbv_c(y_ptr, u_ptr, v_ptr, y_stride, uv_stride, lfi);
        }

        //void vp8_loop_filter_bv_c(unsigned char* y_ptr, unsigned char* u_ptr, unsigned char* v_ptr, int y_stride, int uv_stride, struct loop_filter_info *lfi);
        //#define vp8_loop_filter_bv vp8_loop_filter_bv_c

        public static void vp8_loop_filter_bv(byte* y_ptr, byte* u_ptr, byte* v_ptr, int y_stride, int uv_stride, loop_filter_info lfi)
            => vp8_loop_filter_bv_c(y_ptr, u_ptr, v_ptr, y_stride, uv_stride, lfi);

        public static void vp8_loop_filter_bv_c(byte* y_ptr, byte* u_ptr, byte* v_ptr, int y_stride, int uv_stride, loop_filter_info lfi)
        {
            loopfilter_filters.vp8_loop_filter_bv_c(y_ptr, u_ptr, v_ptr, y_stride, uv_stride, lfi);
        }

        //void vp8_loop_filter_mbh_c(unsigned char* y_ptr, unsigned char* u_ptr, unsigned char* v_ptr, int y_stride, int uv_stride, struct loop_filter_info *lfi);
        //#define vp8_loop_filter_mbh vp8_loop_filter_mbh_c

        public static void vp8_loop_filter_mbh(byte* y_ptr, byte* u_ptr, byte* v_ptr, int y_stride, int uv_stride, loop_filter_info lfi)
            => vp8_loop_filter_mbh_c(y_ptr, u_ptr, v_ptr, y_stride, uv_stride, lfi);

        public static void vp8_loop_filter_mbh_c(byte* y_ptr, byte* u_ptr, byte* v_ptr, int y_stride, int uv_stride, loop_filter_info lfi)
        {
            loopfilter_filters.vp8_loop_filter_mbh_c(y_ptr, u_ptr, v_ptr, y_stride, uv_stride, lfi);
        }

        //void vp8_loop_filter_bh_c(unsigned char* y_ptr, unsigned char* u_ptr, unsigned char* v_ptr, int y_stride, int uv_stride, struct loop_filter_info *lfi);
        //#define vp8_loop_filter_bh vp8_loop_filter_bh_c

        public static void vp8_loop_filter_bh(byte* y_ptr, byte* u_ptr, byte* v_ptr, int y_stride, int uv_stride, loop_filter_info lfi)
            => vp8_loop_filter_bh_c(y_ptr, u_ptr, v_ptr, y_stride, uv_stride, lfi);

        public static void vp8_loop_filter_bh_c(byte* y_ptr, byte* u_ptr, byte* v_ptr, int y_stride, int uv_stride, loop_filter_info lfi)
        {
            loopfilter_filters.vp8_loop_filter_bh_c(y_ptr, u_ptr, v_ptr, y_stride, uv_stride, lfi);
        }

        //void vp8_loop_filter_simple_vertical_edge_c(unsigned char* y_ptr, int y_stride, const unsigned char* blimit);
        //#define vp8_loop_filter_simple_mbv vp8_loop_filter_simple_vertical_edge_c

        public static void vp8_loop_filter_simple_mbv(byte* y_ptr, int y_stride, in byte* blimit)
            => vp8_loop_filter_simple_vertical_edge_c(y_ptr, y_stride, blimit);

        public static void vp8_loop_filter_simple_vertical_edge_c(byte* y_ptr, int y_stride, in byte* blimit)
        {
            loopfilter_filters.vp8_loop_filter_simple_vertical_edge_c(y_ptr, y_stride, blimit);
        }

        //void vp8_loop_filter_bvs_c(unsigned char* y_ptr, int y_stride, const unsigned char* blimit);
        //#define vp8_loop_filter_simple_bv vp8_loop_filter_bvs_c

        public static void vp8_loop_filter_simple_bv(byte* y_ptr, int y_stride, in byte* blimit)
            => vp8_loop_filter_bvs_c(y_ptr, y_stride, blimit);

        public static void vp8_loop_filter_bvs_c(byte* y_ptr, int y_stride, in byte* blimit)
        {
            loopfilter_filters.vp8_loop_filter_bvs_c(y_ptr, y_stride, blimit);
        }

        //void vp8_loop_filter_simple_horizontal_edge_c(unsigned char* y_ptr, int y_stride, const unsigned char* blimit);
        //#define vp8_loop_filter_simple_mbh vp8_loop_filter_simple_horizontal_edge_c

        public static void vp8_loop_filter_simple_mbh(byte* y_ptr, int y_stride, in byte* blimit)
            => vp8_loop_filter_simple_horizontal_edge_c(y_ptr, y_stride, blimit);

        public static void vp8_loop_filter_simple_horizontal_edge_c(byte* y_ptr, int y_stride, in byte* blimit)
        {
            loopfilter_filters.vp8_loop_filter_simple_horizontal_edge_c(y_ptr, y_stride, blimit);
        }

        //void vp8_loop_filter_bhs_c(unsigned char* y_ptr, int y_stride, const unsigned char* blimit);
        //#define vp8_loop_filter_simple_bh vp8_loop_filter_bhs_c

        public static void vp8_loop_filter_simple_bh(byte* y_ptr, int y_stride, in byte* blimit)
            => vp8_loop_filter_bhs_c(y_ptr, y_stride, blimit);

        public static void vp8_loop_filter_bhs_c(byte* y_ptr, int y_stride, in byte* blimit)
        {
            loopfilter_filters.vp8_loop_filter_bhs_c(y_ptr, y_stride, blimit);
        }

        //void vp8_copy_mem16x16_c(unsigned char* src, int src_stride, unsigned char* dst, int dst_stride);
        //#define vp8_copy_mem16x16 vp8_copy_mem16x16_c

        public static void vp8_copy_mem16x16(byte* src, int src_stride, byte* dst, int dst_stride)
            => reconinter.vp8_copy_mem16x16_c(src, src_stride, dst, dst_stride);

        //void vp8_copy_mem8x4_c(unsigned char* src, int src_stride, unsigned char* dst, int dst_stride);
        //#define vp8_copy_mem8x4 vp8_copy_mem8x4_c

        public static void vp8_copy_mem8x4(byte* src, int src_stride, byte* dst, int dst_stride)
            => reconinter.vp8_copy_mem8x4_c(src, src_stride, dst, dst_stride);

        //void vp8_copy_mem8x8_c(unsigned char* src, int src_stride, unsigned char* dst, int dst_stride);
        //#define vp8_copy_mem8x8 vp8_copy_mem8x8_c

        public static void vp8_copy_mem8x8(byte* src, int src_stride, byte* dst, int dst_stride)
            => reconinter.vp8_copy_mem8x8_c(src, src_stride, dst, dst_stride);

        //void vp8_dequant_idct_add_c(short* input, short* dq, unsigned char* dest, int stride);
        //#define vp8_dequant_idct_add vp8_dequant_idct_add_c

        public static void vp8_dequant_idct_add(short* input, short* dq, byte* dest, int stride)
            => vp8_dequant_idct_add_c(input, dq, dest, stride);

        public static void vp8_dequant_idct_add_c(short* input, short* dq, byte* dest, int stride)
            => dequantize.vp8_dequant_idct_add_c(input, dq, dest, stride);

        //void vp8_dc_only_idct_add_c(short input_dc, unsigned char* pred_ptr, int pred_stride, unsigned char* dst_ptr, int dst_stride);
        //#define vp8_dc_only_idct_add vp8_dc_only_idct_add_c

        public static void vp8_dc_only_idct_add(short input_dc, byte* pred_ptr, int pred_stride, byte* dst_ptr, int dst_stride)
            => vp8_dc_only_idct_add_c(input_dc, pred_ptr, pred_stride, dst_ptr, dst_stride);

        public static void vp8_dc_only_idct_add_c(short input_dc, byte* pred_ptr, int pred_stride, byte* dst_ptr, int dst_stride)
            => idctllm.vp8_dc_only_idct_add_c(input_dc, pred_ptr, pred_stride, dst_ptr, dst_stride);

        //void vp8_dequantize_b_c(struct blockd*, short* DQC);
        //#define vp8_dequantize_b vp8_dequantize_b_c

        public static void vp8_dequantize_b(BLOCKD d, short* DQC)
            =>  vp8_dequantize_b_c(d, DQC);

        public static void vp8_dequantize_b_c(BLOCKD d, short* DQC)
            => dequantize.vp8_dequantize_b_c(d, DQC);

        //void vp8_short_inv_walsh4x4_c(short* input, short* mb_dqcoeff);
        //#define vp8_short_inv_walsh4x4 vp8_short_inv_walsh4x4_c

        public static void vp8_short_inv_walsh4x4(short* input, short* mb_dqcoeff)
            => vp8_short_inv_walsh4x4_c(input, mb_dqcoeff);

        public static void vp8_short_inv_walsh4x4_c(short* input, short* mb_dqcoeff)
            => idctllm.vp8_short_inv_walsh4x4_c(input, mb_dqcoeff);

        //void vp8_short_inv_walsh4x4_1_c(short* input, short* mb_dqcoeff);
        //#define vp8_short_inv_walsh4x4_1 vp8_short_inv_walsh4x4_1_c

        public static void vp8_short_inv_walsh4x4_1(short* input, short* mb_dqcoeff)
            => idctllm.vp8_short_inv_walsh4x4_1_c(input, mb_dqcoeff);

        //void vp8_dequant_idct_add_y_block_c(short* q, short* dq, unsigned char* dst, int stride, char* eobs);
        //#define vp8_dequant_idct_add_y_block vp8_dequant_idct_add_y_block_c

        public static void vp8_dequant_idct_add_y_block(short* q, short* dq, byte* dst, int stride, sbyte* eobs)
            => vp8_dequant_idct_add_y_block_c(q, dq, dst, stride, eobs);

        public static void vp8_dequant_idct_add_y_block_c(short* q, short* dq, byte* dst, int stride, sbyte* eobs)
            => idct_blk.vp8_dequant_idct_add_y_block_c(q, dq, dst, stride, eobs);

        //void vp8_dequant_idct_add_uv_block_c(short* q, short* dq, unsigned char* dst_u, unsigned char* dst_v, int stride, char* eobs);
        //#define vp8_dequant_idct_add_uv_block vp8_dequant_idct_add_uv_block_c

        public static void vp8_dequant_idct_add_uv_block(short* q, short* dq, byte* dst_u, byte* dst_v, int stride, sbyte* eobs)
            => vp8_dequant_idct_add_uv_block_c(q, dq, dst_u, dst_v, stride, eobs);

        public static void vp8_dequant_idct_add_uv_block_c(short* q, short* dq, byte* dst_u, byte* dst_v, int stride, sbyte* eobs)
            => idct_blk.vp8_dequant_idct_add_uv_block_c(q, dq, dst_u, dst_v, stride, eobs);
    }
}
