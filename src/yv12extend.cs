//-----------------------------------------------------------------------------
// Filename: yv12extend.cs
//
// Description: Port of:
//  - vpx_scale_rtcd.h
//  - yv12extend.c
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

namespace Vpx.Net
{
    public static class yv12extend
    {
        // void vp8_yv12_copy_frame_c(const struct yv12_buffer_config *src_ybc, struct yv12_buffer_config *dst_ybc);
        // #define vp8_yv12_copy_frame vp8_yv12_copy_frame_c

        // Copies the source image into the destination image and updates the
        // destination's UMV borders.
        // Note: The frames are assumed to be identical in size.

        public unsafe static void vp8_yv12_copy_frame(YV12_BUFFER_CONFIG src_ybc,
                                   YV12_BUFFER_CONFIG dst_ybc)
        {
            int row;
            byte* src = src_ybc.y_buffer;
            byte* dst = dst_ybc.y_buffer;

            for (row = 0; row < src_ybc.y_height; ++row)
            {
                //memcpy(dst, src, src_ybc.y_width);
                Buffer.MemoryCopy(src, dst, dst_ybc.y_width, src_ybc.y_width);
                src += src_ybc.y_stride;
                dst += dst_ybc.y_stride;
            }

            src = src_ybc.u_buffer;
            dst = dst_ybc.u_buffer;

            for (row = 0; row < src_ybc.uv_height; ++row)
            {
                //memcpy(dst, src, src_ybc.uv_width);
                Buffer.MemoryCopy(src, dst, dst_ybc.uv_width, src_ybc.uv_width);
                src += src_ybc.uv_stride;
                dst += dst_ybc.uv_stride;
            }

            src = src_ybc.v_buffer;
            dst = dst_ybc.v_buffer;

            for (row = 0; row < src_ybc.uv_height; ++row)
            {
                //memcpy(dst, src, src_ybc.uv_width);
                Buffer.MemoryCopy(src, dst, dst_ybc.uv_width, src_ybc.uv_width);
                src += src_ybc.uv_stride;
                dst += dst_ybc.uv_stride;
            }

            vp8_yv12_extend_frame_borders_c(dst_ybc);
        }

        public unsafe static void vp8_yv12_extend_frame_borders_c(YV12_BUFFER_CONFIG ybf)
        {
            int uv_border = ybf.border / 2;

            //assert(ybf->border % 2 == 0);
            //assert(ybf->y_height - ybf->y_crop_height < 16);
            //assert(ybf->y_width - ybf->y_crop_width < 16);
            //assert(ybf->y_height - ybf->y_crop_height >= 0);
            //assert(ybf->y_width - ybf->y_crop_width >= 0);

            extend_plane(ybf.y_buffer, ybf.y_stride, ybf.y_crop_width,
                         ybf.y_crop_height, ybf.border, ybf.border,
                         ybf.border + ybf.y_height - ybf.y_crop_height,
                         ybf.border + ybf.y_width - ybf.y_crop_width);

            extend_plane(ybf.u_buffer, ybf.uv_stride, ybf.uv_crop_width,
                         ybf.uv_crop_height, uv_border, uv_border,
                         uv_border + ybf.uv_height - ybf.uv_crop_height,
                         uv_border + ybf.uv_width - ybf.uv_crop_width);

            extend_plane(ybf.v_buffer, ybf.uv_stride, ybf.uv_crop_width,
                         ybf.uv_crop_height, uv_border, uv_border,
                         uv_border + ybf.uv_height - ybf.uv_crop_height,
                         uv_border + ybf.uv_width - ybf.uv_crop_width);
        }

        public unsafe static void extend_plane(byte* src, int src_stride, int width,
                         int height, int extend_top, int extend_left,
                         int extend_bottom, int extend_right)
        {
            int i;
            int linesize = extend_left + extend_right + width;

            /* copy the left and right most columns out */
            byte* src_ptr1 = src;
            byte* src_ptr2 = src + width - 1;
            byte* dst_ptr1 = src - extend_left;
            byte* dst_ptr2 = src + width;

            for (i = 0; i < height; ++i)
            {
                //memset(dst_ptr1, src_ptr1[0], extend_left);
                //memset(dst_ptr2, src_ptr2[0], extend_right);
                for (int j = 0; j < extend_left; j++)
                {
                    *(dst_ptr1 + j) = src_ptr1[0];
                }

                for (int k = 0; k < extend_right; k++)
                {
                    *(dst_ptr2 +k) = src_ptr2[0];
                }

                src_ptr1 += src_stride;
                src_ptr2 += src_stride;
                dst_ptr1 += src_stride;
                dst_ptr2 += src_stride;
            }

            /* Now copy the top and bottom lines into each line of the respective
             * borders
             */
            src_ptr1 = src - extend_left;
            src_ptr2 = src + src_stride * (height - 1) - extend_left;
            dst_ptr1 = src + src_stride * -extend_top - extend_left;
            dst_ptr2 = src + src_stride * height - extend_left;

            for (i = 0; i < extend_top; ++i)
            {
                //memcpy(dst_ptr1, src_ptr1, linesize);
                Buffer.MemoryCopy(src_ptr1, dst_ptr1, linesize, linesize);
                dst_ptr1 += src_stride;
            }

            for (i = 0; i < extend_bottom; ++i)
            {
                //memcpy(dst_ptr2, src_ptr2, linesize);
                Buffer.MemoryCopy(src_ptr2, dst_ptr2, linesize, linesize);
                dst_ptr2 += src_stride;
            }
        }
    }
}
