//-----------------------------------------------------------------------------
// Filename: onyxd_if.cs
//
// Description: Port of:
//  - onyxd_if.c
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
    public static class onyxd
    {
        private static volatile bool _isDecoderInitialised = false;

        private static void initialize_dec()
        {
            if (!_isDecoderInitialised)
            {
                _isDecoderInitialised = true;
                // Port AC: vpx_dsp_rtcd is an empty function when built with bare bones config options.
                //vpx_dsp_rtcd();
                reconintra.vp8_init_intra_predictors();
            }
        }

        public static void remove_decompressor(VP8D_COMP pbi)
        {
            //vp8_remove_common(pbi.common);
            //vpx_mem.vpx_free(pbi);
        }

        public static vpx_codec_err_t vp8_remove_decoder_instances(frame_buffers fb)
        {
            VP8D_COMP pbi = fb.pbi[0];

            if (pbi != null) return vpx_codec_err_t.VPX_CODEC_ERROR;

            /* decoder instance for single thread mode */
            remove_decompressor(pbi);
            return vpx_codec_err_t.VPX_CODEC_OK;
        }


        // Port AC: Changed return type from int to vpx_codec_err_t.
        public static vpx_codec_err_t vp8_create_decoder_instances(frame_buffers fb, VP8D_CONFIG oxcf)
        {
            /* decoder instance for single thread mode */
            fb.pbi[0] = create_decompressor(oxcf);
            if (fb.pbi[0] == null) return vpx_codec_err_t.VPX_CODEC_ERROR;

           return vpx_codec_err_t.VPX_CODEC_OK;
        }

        public static VP8D_COMP create_decompressor(VP8D_CONFIG oxcf)
        {
            //VP8D_COMP pbi = vpx_memalign(32, sizeof(VP8D_COMP));

            //if (!pbi) return NULL;

            //memset(pbi, 0, sizeof(VP8D_COMP));

            VP8D_COMP pbi = new VP8D_COMP();

            // Port AC: jmp_buf seems to be being used as kind of like an exception handler.
            // There's no direct translation for C# and memory allocation errors will generate
            // runtime exceptions. Should be safe to remove.
            //if (setjmp(pbi.common.error.jmp))
            //{
            //    pbi.common.error.setjmp = 0;
            //    remove_decompressor(pbi);
            //    return 0;
            //}

            //pbi.common.error.setjmp = 1;

            alloccommon.vp8_create_common(pbi.common);

            pbi.common.current_video_frame = 0;
            pbi.ready_for_new_data = 1;

            /* vp8cx_init_de_quantizer() is first called here. Add check in
             * frame_init_dequantizer() to avoid
             *  unnecessary calling of vp8cx_init_de_quantizer() for every frame.
             */
            decodeframe.vp8cx_init_de_quantizer(pbi);

            vp8_loopfilter.vp8_loop_filter_init(pbi.common);

            //pbi.common.error.setjmp = 0;

            // Port AC: Assume void cast is a noop to remove compiler warning.
            //(void)oxcf;
            pbi.ec_enabled = 0;

            /* Error concealment is activated after a key frame has been
             * decoded without errors when error concealment is enabled.
             */
            pbi.ec_active = 0;

            pbi.decoded_key_frame = 0;

            /* Independent partitions is activated when a frame updates the
             * token probability table to have equal probabilities over the
             * PREV_COEF context.
             */
            pbi.independent_partitions = 0;

            mbpitch.vp8_setup_block_dptrs(pbi.mb);

            if (!_isDecoderInitialised)
            {
                //once(initialize_dec);
                initialize_dec();
            }

            return pbi;
        }

        public static int check_fragments_for_errors(VP8D_COMP pbi)
        {
            if (pbi.ec_active == 0 && pbi.fragments.count <= 1 &&
                pbi.fragments.sizes[0] == 0)
            {
                VP8_COMMON cm = pbi.common;

                /* If error concealment is disabled we won't signal missing frames
                 * to the decoder.
                 */
                if (cm.fb_idx_ref_cnt[cm.lst_fb_idx] > 1)
                {
                    /* The last reference shares buffer with another reference
                     * buffer. Move it to its own buffer before setting it as
                     * corrupt, otherwise we will make multiple buffers corrupt.
                     */
                    int prev_idx = cm.lst_fb_idx;
                    cm.fb_idx_ref_cnt[prev_idx]--;
                    cm.lst_fb_idx = get_free_fb(cm);
                    yv12extend.vp8_yv12_copy_frame(cm.yv12_fb[prev_idx], cm.yv12_fb[cm.lst_fb_idx]);
                }
                /* This is used to signal that we are missing frames.
                 * We do not know if the missing frame(s) was supposed to update
                 * any of the reference buffers, but we act conservative and
                 * mark only the last buffer as corrupted.
                 */
                cm.yv12_fb[cm.lst_fb_idx].corrupted = 1;

                /* Signal that we have no frame to show. */
                cm.show_frame = 0;

                /* Nothing more to do. */
                return 0;
            }

            return 1;
        }

        public static int vp8dx_receive_compressed_data(VP8D_COMP pbi, Int64 time_stamp)
        {
            VP8_COMMON cm = pbi.common;
            int retcode = -1;

            pbi.common.error.error_code = vpx_codec_err_t.VPX_CODEC_OK;

            retcode = check_fragments_for_errors(pbi);
            if (retcode <= 0) return retcode;

            cm.new_fb_idx = get_free_fb(cm);

            /* setup reference frames for vp8_decode_frame */
            pbi.dec_fb_ref[(int)MV_REFERENCE_FRAME.INTRA_FRAME] = cm.yv12_fb[cm.new_fb_idx];
            pbi.dec_fb_ref[(int)MV_REFERENCE_FRAME.LAST_FRAME] = cm.yv12_fb[cm.lst_fb_idx];
            pbi.dec_fb_ref[(int)MV_REFERENCE_FRAME.GOLDEN_FRAME] = cm.yv12_fb[cm.gld_fb_idx];
            pbi.dec_fb_ref[(int)MV_REFERENCE_FRAME.ALTREF_FRAME] = cm.yv12_fb[cm.alt_fb_idx];

            retcode = decodeframe.vp8_decode_frame(pbi);

            if (retcode < 0)
            {
                if (cm.fb_idx_ref_cnt[cm.new_fb_idx] > 0)
                {
                    cm.fb_idx_ref_cnt[cm.new_fb_idx]--;
                }

                pbi.common.error.error_code = vpx_codec_err_t.VPX_CODEC_ERROR;
                // Propagate the error info.
                if (pbi.mb.error_info.error_code != vpx_codec_err_t.VPX_CODEC_ERROR)
                {
                    pbi.common.error.error_code = pbi.mb.error_info.error_code;
                    //memcpy(pbi.common.error.detail, pbi.mb.error_info.detail,
                    //       sizeof(pbi.mb.error_info.detail));
                    pbi.common.error.detail = pbi.mb.error_info.detail;
                }
                goto decode_exit;
            }

            if (swap_frame_buffers(cm) != 0)
            {
                pbi.common.error.error_code = vpx_codec_err_t.VPX_CODEC_ERROR;
                goto decode_exit;
            }

            //vpx_clear_system_state();

            if (cm.show_frame > 0)
            {
                cm.current_video_frame++;
                cm.show_frame_mi = cm.mi;
            }

            pbi.ready_for_new_data = 0;
            pbi.last_time_stamp = time_stamp;

        decode_exit:
            //vpx_clear_system_state();
            return retcode;
        }

        private static int get_free_fb(VP8_COMMON cm)
        {
            int i;
            for (i = 0; i < VP8_COMMON.NUM_YV12_BUFFERS; ++i)
            {
                if (cm.fb_idx_ref_cnt[i] == 0) break;
            }

            //assert(i < NUM_YV12_BUFFERS);
            cm.fb_idx_ref_cnt[i] = 1;
            return i;
        }

        /* If any buffer copy / swapping is signalled it should be done here. */
        public static int swap_frame_buffers(VP8_COMMON cm)
        {
            int err = 0;

            /* The alternate reference frame or golden frame can be updated
             *  using the new, last, or golden/alt ref frame.  If it
             *  is updated using the newly decoded frame it is a refresh.
             *  An update using the last or golden/alt ref frame is a copy.
             */
            if (cm.copy_buffer_to_arf > 0)
            {
                int new_fb = 0;

                if (cm.copy_buffer_to_arf == 1)
                {
                    new_fb = cm.lst_fb_idx;
                }
                else if (cm.copy_buffer_to_arf == 2)
                {
                    new_fb = cm.gld_fb_idx;
                }
                else
                {
                    err = -1;
                }

                ref_cnt_fb(cm.fb_idx_ref_cnt, ref cm.alt_fb_idx, new_fb);
            }

            if (cm.copy_buffer_to_gf > 0)
            {
                int new_fb = 0;

                if (cm.copy_buffer_to_gf == 1)
                {
                    new_fb = cm.lst_fb_idx;
                }
                else if (cm.copy_buffer_to_gf == 2)
                {
                    new_fb = cm.alt_fb_idx;
                }
                else
                {
                    err = -1;
                }

                ref_cnt_fb(cm.fb_idx_ref_cnt, ref cm.gld_fb_idx, new_fb);
            }

            if (cm.refresh_golden_frame > 0)
            {
                ref_cnt_fb(cm.fb_idx_ref_cnt, ref cm.gld_fb_idx, cm.new_fb_idx);
            }

            if (cm.refresh_alt_ref_frame > 0)
            {
                ref_cnt_fb(cm.fb_idx_ref_cnt, ref cm.alt_fb_idx, cm.new_fb_idx);
            }

            if (cm.refresh_last_frame > 0)
            {
                ref_cnt_fb(cm.fb_idx_ref_cnt, ref cm.lst_fb_idx, cm.new_fb_idx);

                cm.frame_to_show = cm.yv12_fb[cm.lst_fb_idx];
            }
            else
            {
                cm.frame_to_show = cm.yv12_fb[cm.new_fb_idx];
            }

            cm.fb_idx_ref_cnt[cm.new_fb_idx]--;

            return err;
        }

        private unsafe static void ref_cnt_fb(int[] buf, ref int idx, int new_idx)
        {
            if (buf[idx] > 0) buf[idx]--;

            idx = new_idx;

            buf[new_idx]++;
        }

        public static int vp8dx_get_raw_frame(VP8D_COMP pbi, ref YV12_BUFFER_CONFIG sd,
                        out long time_stamp, out long time_end_stamp,
                        ref vp8_ppflags_t flags)
        {
            int ret = -1;
            time_stamp = 0;
            time_end_stamp = 0;

            if (pbi.ready_for_new_data == 1) return ret;

            /* ie no raw frame to show!!! */
            if (pbi.common.show_frame == 0) return ret;

            pbi.ready_for_new_data = 1;
            time_stamp = pbi.last_time_stamp;
            time_end_stamp = 0;

            //(void)flags;

            if (pbi.common.frame_to_show != null)
            {
                sd = pbi.common.frame_to_show;
                sd.y_width = pbi.common.Width;
                sd.y_height = pbi.common.Height;
                sd.uv_height = pbi.common.Height / 2;
                ret = 0;
            }
            else
            {
                ret = -1;
            }

            // Port AC: Noop in libvpx.
            //vpx_clear_system_state();
            return ret;
        }
    }
}
