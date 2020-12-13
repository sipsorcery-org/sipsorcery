//-----------------------------------------------------------------------------
// Filename: vp8_dx_iface.cs
//
// Description: Port of: 
//  - vp8_dx_iface.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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

//#define VP8_CAP_POSTPROC (CONFIG_POSTPROC ? VPX_CODEC_CAP_POSTPROC : 0)
//#define VP8_CAP_ERROR_CONCEALMENT \
//(CONFIG_ERROR_CONCEALMENT ? VPX_CODEC_CAP_ERROR_CONCEALMENT : 0)

//typedef vpx_codec_stream_info_t vp8_stream_info_t;
using vp8_stream_info_t = Vpx.Net.vpx_codec_stream_info_t;
using vpx_codec_iter_t = System.IntPtr;

//#define NELEMENTS(x) ((int)(sizeof(x) / sizeof((x)[0])))

namespace Vpx.Net
{
    /* Structures for handling memory allocations */
    //typedef enum { VP8_SEG_ALG_PRIV = 256, VP8_SEG_MAX } mem_seg_id_t;

    public enum mem_seg_id_t
    {
        VP8_SEG_ALG_PRIV = 256,
        VP8_SEG_MAX
    }

    //    struct vpx_codec_alg_priv
    //    {
    //        vpx_codec_priv_t base;
    //  vpx_codec_dec_cfg_t cfg;
    //        vp8_stream_info_t si;
    //        int decoder_init;
    //#if CONFIG_MULTITHREAD
    //  // Restart threads on next frame if set to 1.
    //  // This is set when error happens in multithreaded decoding and all threads
    //  // are shut down.
    //  int restart_threads;
    //#endif
    //        int postproc_cfg_set;
    //        vp8_postproc_cfg_t postproc_cfg;
    //        vpx_decrypt_cb decrypt_cb;
    //        void* decrypt_state;
    //        vpx_image_t img;
    //        int img_setup;
    //        struct frame_buffers yv12_frame_buffers;
    //  void* user_priv;
    //        FRAGMENT_DATA fragments;
    //    };

    public class vpx_codec_alg_priv_t
    {
        public vpx_codec_priv_t @base = new vpx_codec_priv_t();
        public vpx_codec_dec_cfg_t cfg;
        public vp8_stream_info_t si;
        public int decoder_init;
        //int postproc_cfg_set;
        //public vp8_postproc_cfg_t postproc_cfg;
        //public vpx_decrypt_cb decrypt_cb;
        //public IntPtr decrypt_state;
        public vpx_image_t img = new vpx_image_t();
        public int img_setup;
        public frame_buffers yv12_frame_buffers = new frame_buffers();
        public IntPtr user_priv;
        public FRAGMENT_DATA fragments = new FRAGMENT_DATA();
    }

    public static class vp8_dx
    {
        public const string VERSION_STRING_PREFIX = "WebM Project VP8 Decoder";
        public const int VP8_CAP_POSTPROC = vpx_decoder.VPX_CODEC_CAP_POSTPROC;
        public const int VP8_CAP_ERROR_CONCEALMENT = vpx_decoder.VPX_CODEC_CAP_ERROR_CONCEALMENT;

        private static vpx_codec_iface_t _singleton;

        private static int vp8_init_ctx(vpx_codec_ctx_t ctx)
        {
            //vpx_codec_alg_priv_t priv = (vpx_codec_alg_priv_t)vpx_calloc(1, sizeof(*priv));
            //if (!priv) return 1;
            vpx_codec_alg_priv_t priv = new vpx_codec_alg_priv_t();

            //ctx.priv = new vpx_codec_priv_t();
            ctx.priv = new vpx_codec_alg_priv_t();
            //ctx.priv.init_flags = ctx.init_flags;

            //priv.si.sz = sizeof(priv->si);
            //priv.decrypt_cb = null;
            //priv.decrypt_state = IntPtr.Zero;

            //if (ctx.dec_cfg != null)            {
            /* Update the reference to the config structure to an internal copy. */
            //priv.cfg = *ctx->config.dec;
            ctx.dec_cfg = priv.cfg; //&priv->cfg;
            //}

            return 0;
        }

        internal static vpx_codec_err_t vp8_init(vpx_codec_ctx_t ctx,
                                vpx_codec_priv_enc_mr_cfg_t data)
        {
            vpx_codec_err_t res = vpx_codec_err_t.VPX_CODEC_OK;
            //(void)data;

            //vp8_rtcd();
            //vpx_dsp_rtcd();
            //vpx_scale_rtcd();

            /* This function only allocates space for the vpx_codec_alg_priv_t
             * structure. More memory may be required at the time the stream
             * information becomes known.
             */
            if (ctx.priv == null)
            {
                if (vp8_init_ctx(ctx) != 0) return vpx_codec_err_t.VPX_CODEC_MEM_ERROR;

                /* initialize number of fragments to zero */
                ctx.priv.fragments.count = 0;
                /* is input fragments enabled? */
                ctx.priv.fragments.enabled = (int)(ctx.priv.@base.init_flags & vpx_decoder.VPX_CODEC_USE_INPUT_FRAGMENTS);

                /*post processing level initialized to do nothing */
            }

            return res;
        }

        internal static vpx_codec_err_t vp8_destroy(vpx_codec_alg_priv_t ctx)
        {
            onyxd.vp8_remove_decoder_instances(ctx.yv12_frame_buffers);

            //vpx_free(ctx);

            return vpx_codec_err_t.VPX_CODEC_OK;
        }

        static vpx_codec_ctrl_fn_map_t[] vp8_ctf_maps = new vpx_codec_ctrl_fn_map_t[]{
            //{ VP8_SET_REFERENCE, vp8_set_reference },
            //{ VP8_COPY_REFERENCE, vp8_get_reference },
            //{ VP8_SET_POSTPROC, vp8_set_postproc },
            //{ VP8D_GET_LAST_REF_UPDATES, vp8_get_last_ref_updates },
            //{ VP8D_GET_FRAME_CORRUPTED, vp8_get_frame_corrupted },
            //{ VP8D_GET_LAST_REF_USED, vp8_get_last_ref_frame },
            //{ VPXD_GET_LAST_QUANTIZER, vp8_get_quantizer },
            //{ VPXD_SET_DECRYPTOR, vp8_set_decryptor },
            //{ -1, NULL },
        };

        public static vpx_codec_err_t update_error_state(vpx_codec_alg_priv_t ctx, vpx_internal_error_info error)
        {
            vpx_codec_err_t res = error.error_code;

            if (res != vpx_codec_err_t.VPX_CODEC_OK)
            {
                ctx.@base.err_detail = error.has_detail > 0 ? error.detail : null;
            }

            return res;
        }

        public unsafe static vpx_codec_iface_t vpx_codec_vp8_dx()
        {
            if (_singleton == null)
            {
                _singleton = new vpx_codec_iface_t
                {
                    name = $"{VERSION_STRING_PREFIX}{vpx_version.VERSION_STRING}",
                    abi_version = vpx_codec_internal.VPX_CODEC_INTERNAL_ABI_VERSION,
                    caps = vpx_codec.VPX_CODEC_CAP_DECODER | VP8_CAP_POSTPROC | VP8_CAP_ERROR_CONCEALMENT |
                            vpx_decoder.VPX_CODEC_CAP_INPUT_FRAGMENTS,
                    init = vp8_init,
                    destroy = vp8_destroy,
                    ctrl_maps = vp8_ctf_maps,
                    dec = new vpx_codec_dec_iface_t
                    {
                        peek_si = vp8_peek_si,
                        //get_si = vp8_get_si,
                        decode = vp8_decode,
                        get_frame = vp8_get_frame,
                        set_fb_fn = null
                    },
                    enc = new vpx_codec_enc_iface_t
                    {
                        cfg_map_count = 0,
                        //cfg_maps = null,
                        //get_cx_data = null,
                        //cfg_set = null,
                        get_glob_hdrs = null,
                        get_preview = null,
                        //mr_get_mem_loc = null
                    }
                };
            }

            return _singleton;
        }

        public unsafe static vpx_codec_err_t vp8_peek_si(byte* data, uint data_sz,
                                   ref vpx_codec_stream_info_t si)
        {
            return vp8_peek_si_internal(data, data_sz, ref si, null, IntPtr.Zero);
        }

        //public unsafe static vpx_codec_err_t vp8_get_si(vpx_codec_alg_priv_t ctx,
        //                          ref vpx_codec_stream_info_t si)
        //{
        //    uint sz;

        //    if (si.sz >= sizeof(vp8_stream_info_t))
        //    {
        //        sz = sizeof(vp8_stream_info_t);
        //    }
        //    else
        //    {
        //        sz = sizeof(vpx_codec_stream_info_t);
        //    }

        //    memcpy(si, &ctx->si, sz);
        //    si->sz = sz;

        //    return VPX_CODEC_OK;
        //}

        public unsafe static vpx_codec_err_t vp8_peek_si_internal(byte* data,
                                                uint data_sz,
                                                ref vpx_codec_stream_info_t si,
                                                vpx_decrypt_cb decrypt_cb,
                                                IntPtr decrypt_state)
        {
            vpx_codec_err_t res = vpx_codec_err_t.VPX_CODEC_OK;

            //assert(data != NULL);
            if (data == null)
            {
                return vpx_codec_err_t.VPX_CODEC_INVALID_PARAM;
            }

            if (data + data_sz <= data)
            {
                res = vpx_codec_err_t.VPX_CODEC_INVALID_PARAM;
            }
            else
            {
                /* Parse uncompresssed part of key frame header.
                 * 3 bytes:- including version, frame type and an offset
                 * 3 bytes:- sync code (0x9d, 0x01, 0x2a)
                 * 4 bytes:- including image width and height in the lowest 14 bits
                 *           of each 2-byte value.
                 */
                byte[] clear_buffer = new byte[10];
                byte* clear = data;
                //if (decrypt_cb)
                //{
                //    int n = VPXMIN(sizeof(clear_buffer), data_sz);
                //    decrypt_cb(decrypt_state, data, clear_buffer, n);
                //    clear = clear_buffer;
                //}
                si.is_kf = 0;

                if (data_sz >= 10 && (clear[0] & 0x01) == 0)
                { /* I-Frame */
                    si.is_kf = 1;

                    /* vet via sync code */
                    if (clear[3] != 0x9d || clear[4] != 0x01 || clear[5] != 0x2a)
                    {
                        return vpx_codec_err_t.VPX_CODEC_UNSUP_BITSTREAM;
                    }

                    si.w = (uint)(clear[6] | (clear[7] << 8)) & 0x3fff;
                    si.h = (uint)(clear[8] | (clear[9] << 8)) & 0x3fff;

                    /*printf("w=%d, h=%d\n", si->w, si->h);*/
                    if (si.h == 0 || si.w == 0) res = vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME;
                }
                else
                {
                    res = vpx_codec_err_t.VPX_CODEC_UNSUP_BITSTREAM;
                }
            }

            return res;
        }

        /// <summary>
        /// Update the input fragment data.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="data"></param>
        /// <param name="data_sz"></param>
        /// <param name="res"></param>
        /// <returns>
        /// <=0: Error.
        /// 1: OK.
        /// </returns>
        public unsafe static int update_fragments(vpx_codec_alg_priv_t ctx, byte* data,
                            uint data_sz, out vpx_codec_err_t res)
        {
            res = vpx_codec_err_t.VPX_CODEC_OK;

            if (ctx.fragments.count == 0)
            {
                /* New frame, reset fragment pointers and sizes */
                //memset((void*)ctx.fragments.ptrs, 0, sizeof(ctx.fragments.ptrs));
                //memset(ctx.fragments.sizes, 0, sizeof(ctx.fragments.sizes));
                Array.Clear(ctx.fragments.ptrs, 0, ctx.fragments.ptrs.Length);
                Array.Clear(ctx.fragments.sizes, 0, ctx.fragments.sizes.Length);
            }
            if (ctx.fragments.enabled > 0 && !(data == null && data_sz == 0))
            {
                /* Store a pointer to this fragment and return. We haven't
                 * received the complete frame yet, so we will wait with decoding.
                 */
                ctx.fragments.ptrs[ctx.fragments.count] = data;
                ctx.fragments.sizes[ctx.fragments.count] = data_sz;
                ctx.fragments.count++;
                if (ctx.fragments.count > (1 << (int)TOKEN_PARTITION.EIGHT_PARTITION) + 1)
                {
                    ctx.fragments.count = 0;
                    res = vpx_codec_err_t.VPX_CODEC_INVALID_PARAM;
                    return -1;
                }
                return 0;
            }

            if (ctx.fragments.enabled == 0 && (data == null && data_sz == 0))
            {
                return 0;
            }

            if (ctx.fragments.enabled == 0)
            {
                ctx.fragments.ptrs[0] = data;
                ctx.fragments.sizes[0] = data_sz;
                ctx.fragments.count = 1;
            }

            return 1;
        }

        public unsafe static vpx_codec_err_t vp8_decode(vpx_codec_alg_priv_t ctx,
                                  byte* data, uint data_sz,
                                  IntPtr user_priv, long deadline)
        {
            try
            {
                vpx_codec_err_t res = vpx_codec_err_t.VPX_CODEC_OK;
                uint resolution_change = 0;
                uint w, h;

                if (ctx.fragments.enabled == 0 && data == null && data_sz == 0)
                {
                    return 0;
                }

                /* Update the input fragment data */
                if (update_fragments(ctx, data, data_sz, out res) <= 0) return res;

                /* Determine the stream parameters. Note that we rely on peek_si to
                 * validate that we have a buffer that does not wrap around the top
                 * of the heap.
                 */
                w = ctx.si.w;
                h = ctx.si.h;

                //res = vp8_peek_si_internal(ctx.fragments.ptrs[0], ctx.fragments.sizes[0],
                //                           ctx.si, ctx.decrypt_cb, ctx.decrypt_state);

                res = vp8_peek_si_internal(ctx.fragments.ptrs[0], ctx.fragments.sizes[0],
                                           ref ctx.si, null, IntPtr.Zero);

                if (res == vpx_codec_err_t.VPX_CODEC_UNSUP_BITSTREAM && ctx.si.is_kf == 0)
                {
                    /* the peek function returns an error for non keyframes, however for
                     * this case, it is not an error */
                    res = vpx_codec_err_t.VPX_CODEC_OK;
                }

                if (ctx.decoder_init == 0 && ctx.si.is_kf == 0)
                {
                    res = vpx_codec_err_t.VPX_CODEC_UNSUP_BITSTREAM;
                }

                if ((ctx.si.h != h) || (ctx.si.w != w)) resolution_change = 1;

                /* Initialize the decoder instance on the first frame*/
                if (res == vpx_codec_err_t.VPX_CODEC_OK && ctx.decoder_init == 0)
                {
                    VP8D_CONFIG oxcf = new VP8D_CONFIG();

                    oxcf.Width = (int)ctx.si.w;
                    oxcf.Height = (int)ctx.si.h;
                    oxcf.Version = 9;
                    oxcf.postprocess = 0;
                    oxcf.max_threads = (int)ctx.cfg.threads;
                    oxcf.error_concealment = (int)(ctx.@base.init_flags & vpx_decoder.VPX_CODEC_USE_ERROR_CONCEALMENT);

                    res = onyxd.vp8_create_decoder_instances(ctx.yv12_frame_buffers, oxcf);
                    if (res == vpx_codec_err_t.VPX_CODEC_OK) ctx.decoder_init = 1;
                }

                if (res == vpx_codec_err_t.VPX_CODEC_OK)
                {
                    VP8D_COMP pbi = ctx.yv12_frame_buffers.pbi[0];
                    VP8_COMMON pc = pbi.common;
                    if (resolution_change > 0)
                    {
                        MACROBLOCKD xd = pbi.mb;

                        pc.Width = (int)ctx.si.w;
                        pc.Height = (int)ctx.si.h;
                        {
                            int prev_mb_rows = pc.mb_rows;

                            // Port AC: TODO identify alternative error mechanism.
                            //if (setjmp(pbi.common.error.jmp))
                            //{
                            //    pbi.common.error.setjmp = 0;
                            //    /* on failure clear the cached resolution to ensure a full
                            //     * reallocation is attempted on resync. */
                            //    ctx.si.w = 0;
                            //    ctx.si.h = 0;
                            //    vpx_clear_system_state();
                            //    /* same return value as used in vp8dx_receive_compressed_data */
                            //    return -1;
                            //}

                            //pbi.common.error.setjmp = 1;

                            if (pc.Width <= 0)
                            {
                                pc.Width = (int)w;
                                vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                                   "Invalid frame width");
                            }

                            if (pc.Height <= 0)
                            {
                                pc.Height = (int)h;
                                vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                                   "Invalid frame height");
                            }

                            if (alloccommon.vp8_alloc_frame_buffers(pc, pc.Width, pc.Height) != 0)
                            {
                                vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_MEM_ERROR,
                                                   "Failed to allocate frame buffers");
                            }

                            xd.pre = pc.yv12_fb[pc.lst_fb_idx];
                            xd.dst = pc.yv12_fb[pc.new_fb_idx];

                            mbpitch.vp8_build_block_doffsets(pbi.mb);

                            // Port AC: Assume this cast was a noop to remove compiler warning.
                            //(void)prev_mb_rows;
                        }

                        // Port AC: TODO identify alternative error mechanism.
                        //pbi.common.error.setjmp = 0;

                        /* required to get past the first get_free_fb() call */
                        pbi.common.fb_idx_ref_cnt[0] = 0;
                    }

                    // Port AC: TODO identify alternative error mechanism.
                    //if (setjmp(pbi.common.error.jmp))
                    //{
                    //    vpx_clear_system_state();
                    //    /* We do not know if the missing frame(s) was supposed to update
                    //     * any of the reference buffers, but we act conservative and
                    //     * mark only the last buffer as corrupted.
                    //     */
                    //    pc.yv12_fb[pc.lst_fb_idx].corrupted = 1;

                    //    if (pc.fb_idx_ref_cnt[pc.new_fb_idx] > 0)
                    //    {
                    //        pc.fb_idx_ref_cnt[pc.new_fb_idx]--;
                    //    }
                    //    pc.error.setjmp = 0;

                    //    res = update_error_state(ctx, pbi.common.error);
                    //    return res;
                    //}

                    //pbi.common.error.setjmp = 1;

                    /* update the pbi fragment data */
                    pbi.fragments = ctx.fragments;

                    ctx.user_priv = user_priv;
                    if (onyxd.vp8dx_receive_compressed_data(pbi, deadline) != 0)
                    {
                        res = update_error_state(ctx, pbi.common.error);
                    }

                    /* get ready for the next series of fragments */
                    ctx.fragments.count = 0;
                }

                return res;
            }
            catch(VpxException vpxExcp)
            {
                return vpxExcp.ErrorCode;
            }
        }

        static vpx_image_t vp8_get_frame(vpx_codec_alg_priv_t ctx, vpx_codec_iter_t iter)
        {
            vpx_image_t img = null;

            /* iter acts as a flip flop, so an image is only returned on the first
             * call to get_frame.
             */
            if (iter == IntPtr.Zero && ctx.yv12_frame_buffers.pbi[0] != null)
            {
                YV12_BUFFER_CONFIG sd = new YV12_BUFFER_CONFIG();
                long time_stamp = 0, time_end_stamp = 0;
                vp8_ppflags_t flags = new vp8_ppflags_t();
                // Port AC: ppflags struct will be initialised to zero by default.
                //vp8_zero(flags);

                //if (ctx.@base.init_flags & VPX_CODEC_USE_POSTPROC) {
                //    flags.post_proc_flag = ctx->postproc_cfg.post_proc_flag;
                //    flags.deblocking_level = ctx->postproc_cfg.deblocking_level;
                //    flags.noise_level = ctx->postproc_cfg.noise_level;
                //}

                if (0 == onyxd.vp8dx_get_raw_frame(ctx.yv12_frame_buffers.pbi[0], ref sd,
                                             out time_stamp, out time_end_stamp, ref flags))
                {
                    yuvconfig2image(ctx.img, sd, ctx.user_priv);

                    img = ctx.img;
                    //*iter = img;
                }
            }

            return img;
        }

        public static unsafe void yuvconfig2image(vpx_image_t img, in YV12_BUFFER_CONFIG yv12, IntPtr user_priv)
        {
            /** vpx_img_wrap() doesn't allow specifying independent strides for
             * the Y, U, and V planes, nor other alignment adjustments that
             * might be representable by a YV12_BUFFER_CONFIG, so we just
             * initialize all the fields.*/
            img.fmt = vpx_img_fmt_t.VPX_IMG_FMT_I420;
            img.w = (uint)yv12.y_stride;
            img.h = (uint)((yv12.y_height + 2 * yv12config.VP8BORDERINPIXELS + 15) & ~15);
            img.d_w = img.r_w = (uint)yv12.y_width;
            img.d_h = img.r_h = (uint)yv12.y_height;
            img.x_chroma_shift = 1;
            img.y_chroma_shift = 1;
            img.planes[vpx_image_t.VPX_PLANE_Y] = yv12.y_buffer;
            img.planes[vpx_image_t.VPX_PLANE_U] = yv12.u_buffer;
            img.planes[vpx_image_t.VPX_PLANE_V] = yv12.v_buffer;
            img.planes[vpx_image_t.VPX_PLANE_ALPHA] = null;
            img.stride[vpx_image_t.VPX_PLANE_Y] = yv12.y_stride;
            img.stride[vpx_image_t.VPX_PLANE_U] = yv12.uv_stride;
            img.stride[vpx_image_t.VPX_PLANE_V] = yv12.uv_stride;
            img.stride[vpx_image_t.VPX_PLANE_ALPHA] = yv12.y_stride;
            img.bit_depth = 8;
            img.bps = 12;
            img.user_priv = user_priv.ToPointer();
            img.img_data = yv12.buffer_alloc;
            img.img_data_owner = 0;
            img.self_allocd = 0;
        }
    }
}
