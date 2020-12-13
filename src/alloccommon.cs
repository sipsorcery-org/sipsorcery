//-----------------------------------------------------------------------------
// Filename: alloccommon.cs
//
// Description: Port of:
//  - alloccommon.h
//  - alloccommon.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    public static class alloccommon
    {
        public static void vp8_setup_version(VP8_COMMON cm)
        {
            switch (cm.version)
            {
                case 0:
                    cm.no_lpf = 0;
                    cm.filter_type = LOOPFILTERTYPE.NORMAL_LOOPFILTER;
                    cm.use_bilinear_mc_filter = 0;
                    cm.full_pixel = 0;
                    break;
                case 1:
                    cm.no_lpf = 0;
                    cm.filter_type = LOOPFILTERTYPE.SIMPLE_LOOPFILTER;
                    cm.use_bilinear_mc_filter = 1;
                    cm.full_pixel = 0;
                    break;
                case 2:
                    cm.no_lpf = 1;
                    cm.filter_type = LOOPFILTERTYPE.NORMAL_LOOPFILTER;
                    cm.use_bilinear_mc_filter = 1;
                    cm.full_pixel = 0;
                    break;
                case 3:
                    cm.no_lpf = 1;
                    cm.filter_type = LOOPFILTERTYPE.SIMPLE_LOOPFILTER;
                    cm.use_bilinear_mc_filter = 1;
                    cm.full_pixel = 1;
                    break;
                default:
                    /*4,5,6,7 are reserved for future use*/
                    cm.no_lpf = 0;
                    cm.filter_type = LOOPFILTERTYPE.NORMAL_LOOPFILTER;
                    cm.use_bilinear_mc_filter = 0;
                    cm.full_pixel = 0;
                    break;
            }
        }

        public unsafe static int vp8_alloc_frame_buffers(VP8_COMMON oci, int width, int height)
        {
            int i;

            vp8_de_alloc_frame_buffers(oci);

            /* our internal buffers are always multiples of 16 */
            if ((width & 0xf) != 0) width += 16 - (width & 0xf);

            if ((height & 0xf) != 0) height += 16 - (height & 0xf);

            for (i = 0; i < VP8_COMMON.NUM_YV12_BUFFERS; ++i)
            {
                oci.fb_idx_ref_cnt[i] = 0;
                oci.yv12_fb[i].flags = 0;
                if (yv12config.vp8_yv12_alloc_frame_buffer(ref oci.yv12_fb[i], width, height,
                                                yv12config.VP8BORDERINPIXELS) < 0)
                {
                    goto allocation_fail;
                }
            }

            oci.new_fb_idx = 0;
            oci.lst_fb_idx = 1;
            oci.gld_fb_idx = 2;
            oci.alt_fb_idx = 3;

            oci.fb_idx_ref_cnt[0] = 1;
            oci.fb_idx_ref_cnt[1] = 1;
            oci.fb_idx_ref_cnt[2] = 1;
            oci.fb_idx_ref_cnt[3] = 1;

            if (yv12config.vp8_yv12_alloc_frame_buffer(ref oci.temp_scale_frame, width, 16,
                                            yv12config.VP8BORDERINPIXELS) < 0)
            {
                goto allocation_fail;
            }

            oci.mb_rows = height >> 4;
            oci.mb_cols = width >> 4;
            oci.MBs = oci.mb_rows * oci.mb_cols;
            oci.mode_info_stride = oci.mb_cols + 1;
            //oci.mip = vpx_calloc((oci.mb_cols + 1) * (oci.mb_rows + 1), sizeof(MODE_INFO));
            oci.mip = new MODE_INFO[(oci.mb_cols + 1) * (oci.mb_rows + 1)];
            // Port AC: NASTY, highly inefficient. Need to find a better way but running into problems
            // with C# only supporting fixed arrays of primitive types. Would need to re-design the 
            // MODE_INFO.b_mode_info etc. C data structure which would then deviate significantly from
            // C code and make future maintenance difficult.
            for (int ii=0; ii< oci.mip.Length; ii++)
            {
                oci.mip[ii] = new MODE_INFO();
            }

            //if (!oci->mip) goto allocation_fail;

            //oci.mi = oci.mip + oci.mode_info_stride + 1;
            //oci.mi = oci.mip[oci.mode_info_stride + 1];
            oci.mi = new ArrPtr<MODE_INFO>(oci.mip, oci.mode_info_stride + 1);

            /* Allocation of previous mode info will be done in vp8_decode_frame()
             * as it is a decoder only data */

            //oci->above_context =
            //vpx_calloc(sizeof(ENTROPY_CONTEXT_PLANES) * oci->mb_cols, 1);

            oci.above_context = new ENTROPY_CONTEXT_PLANES[oci.mb_cols];

            //if (!oci.above_context) goto allocation_fail;

            return 0;

        allocation_fail:
            vp8_de_alloc_frame_buffers(oci);
            return 1;
        }

        public static void vp8_de_alloc_frame_buffers(VP8_COMMON oci)
        {
            int i;
            for (i = 0; i < VP8_COMMON.NUM_YV12_BUFFERS; ++i)
            {
                yv12config.vp8_yv12_de_alloc_frame_buffer(ref oci.yv12_fb[i]);
            }

            yv12config.vp8_yv12_de_alloc_frame_buffer(ref oci.temp_scale_frame);

            //vpx_mem.vpx_free(oci.above_context);
            //vpx_mem.vpx_free(oci.mip);

            oci.above_context = null;
            oci.mip = null;
        }

        public static void vp8_create_common(VP8_COMMON oci)
        {
            systemdependent.vp8_machine_specific_config(oci);

            entropymode.vp8_init_mbmode_probs(oci);
            entropymode.vp8_default_bmode_probs(oci.fc.bmode_prob);

            oci.mb_no_coeff_skip = 1;
            oci.no_lpf = 0;
            oci.filter_type = LOOPFILTERTYPE.NORMAL_LOOPFILTER;
            oci.use_bilinear_mc_filter = 0;
            oci.full_pixel = 0;
            oci.multi_token_partition = TOKEN_PARTITION.ONE_PARTITION;
            oci.clamp_type = CLAMP_TYPE.RECON_CLAMP_REQUIRED;

            /* Initialize reference frame sign bias structure to defaults */
            //memset(oci.ref_frame_sign_bias, 0, sizeof(oci->ref_frame_sign_bias));

            /* Default disable buffer to buffer copying */
            oci.copy_buffer_to_gf = 0;
            oci.copy_buffer_to_arf = 0;
        }

        public static void vp8_remove_common(VP8_COMMON oci) 
        { 
            vp8_de_alloc_frame_buffers(oci); 
        }
    }
}
