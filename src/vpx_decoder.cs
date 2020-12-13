//-----------------------------------------------------------------------------
// Filename: vpx_decoder.cs
//
// Description: Describes the decoder algorithm interface to applications.
//
// This file describes the interface between an application and a
// video decoder algorithm.
//
// This abstraction allows applications using this decoder to easily support
// multiple video formats with minimal code duplication. This section describes
// the interface common to all decoders.
//
// Provides the high level interface to wrap decoder algorithms.
//
// Port of: 
//  - vpx_decoder.c
//  - vpx_decoder.h
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

using vpx_codec_flags_t = System.Int64;
using vpx_codec_iter_t = System.IntPtr;

namespace Vpx.Net
{
    /// <summary>
    /// Stream properties.
    /// This structure is used to query or set properties of the decoded
    /// stream. Algorithms may extend this structure with data specific
    /// to their bitstream by setting the sz member appropriately.
    /// </summary>
    public struct vpx_codec_stream_info_t
    {
        //public uint sz;    /**< Size of this structure */
        public uint w;     /**< Width (or 0 for unknown/default) */
        public uint h;     /**< Height (or 0 for unknown/default) */
        public uint is_kf; /**< Current frame is a keyframe */
    }

    /// <summary>
    ///  Initialization Configurations.
    ///  This structure is used to pass init time configuration options to the
    ///  decoder.
    /// </summary>
    public struct vpx_codec_dec_cfg_t
    {
        public uint threads; /**< Maximum number of threads to use, default 1 */
        public uint w;       /**< Width */
        public uint h;       /**< Height */
    }

    public static class vpx_decoder
    {
        /// <summary>
        /// Current ABI version number.
        /// If this file is altered in any way that changes the ABI, this value
        /// must be bumped.  Examples include, but are not limited to, changing
        /// types, removing or reassigning enums, adding/removing/rearranging
        /// fields to structures.
        /// </summary>
        public const int VPX_DECODER_ABI_VERSION = 3 + vpx_codec.VPX_CODEC_ABI_VERSION;

        /* Decoder capabilities bitfield
        *
        *  Each decoder advertises the capabilities it supports as part of its
        *  ::vpx_codec_iface_t interface structure. Capabilities are extra interfaces
        *  or functionality, and are not required to be supported by a decoder.
        *
        *  The available flags are specified by VPX_CODEC_CAP_* defines.
        */

        /// <summary>
        /// Will issue put_slice callbacks.
        /// </summary>
        public const int VPX_CODEC_CAP_PUT_SLICE = 0x10000;

        /// <summary>
        /// Will issue put_frame callbacks.
        /// </summary>
        public const int VPX_CODEC_CAP_PUT_FRAME = 0x20000;

        /// <summary>
        /// Can postprocess decoded frame.
        /// </summary>
        public const int VPX_CODEC_CAP_POSTPROC = 0x40000;

        /// <summary>
        /// Can conceal errors due to packet los.
        /// </summary>
        public const int VPX_CODEC_CAP_ERROR_CONCEALMENT = 0x80000;

        /// <summary>
        /// Can receive encoded frames one fragment at a time.
        /// </summary>
        public const int VPX_CODEC_CAP_INPUT_FRAGMENTS = 0x100000;

        /// <summary>
        /// Can support frame-based multi-threading.
        /// </summary>
        public const int VPX_CODEC_CAP_FRAME_THREADING = 0x200000;

        /// <summary>
        /// Can support external frame buffers.
        /// </summary>
        public const int VPX_CODEC_CAP_EXTERNAL_FRAME_BUFFER = 0x400000;

        /*
        * Initialization-time Feature Enabling.
        * Certain codec features must be known at initialization time, to allow for
        * proper memory allocation.
        * 
        * The available flags are specified by VPX_CODEC_USE_* defines.
        */

        /// <summary>
        /// Postprocess decoded frame.
        /// </summary>
        public const int VPX_CODEC_USE_POSTPROC = 0x10000;

        /// <summary>
        /// Conceal errors in decoded frames.
        /// </summary>
        public const int VPX_CODEC_USE_ERROR_CONCEALMENT = 0x20000;

        /// <summary>
        /// The input frame should be passed to the decoder one fragment at a time.
        /// </summary>
        public const int VPX_CODEC_USE_INPUT_FRAGMENTS = 0x40000;

        /// <summary>
        /// Enable frame-based multi-threading.
        /// </summary>
        public const int VPX_CODEC_USE_FRAME_THREADING = 0x80000;

        public static vpx_codec_alg_priv_t get_alg_priv(vpx_codec_ctx_t ctx)
        {
            return ctx.priv;
        }

        /// <summary>
        /// Initialize a decoder instance.
        /// 
        /// Initializes a decoder context using the given interface. Applications
        /// should call the vpx_codec_dec_init convenience macro instead of this
        /// function directly, to ensure that the ABI version number parameter
        /// is properly initialized.
        ///
        /// If the library was configured with --disable-multithread, this call
        /// is not thread safe and should be guarded with a lock if being used
        /// in a multithreaded context.
        /// </summary>
        /// <param name="ctx">Pointer to this instance's context.</param>
        /// <param name="iface">Pointer to the algorithm interface to use.</param>
        /// <param name="cfg">Configuration to use, if known. May be NULL.</param>
        /// <param name="flags">Bitfield of VPX_CODEC_USE_* flags.</param>
        /// <param name="ver">ABI version number. Must be set to VPX_DECODER_ABI_VERSION.</param>
        /// <returns>
        /// VPX_CODEC_OK The decoder algorithm initialized.
        /// VPX_CODEC_MEM_ERROR Memory allocation failed.
        /// </returns>
        public static vpx_codec_err_t vpx_codec_dec_init_ver(vpx_codec_ctx_t ctx,
                                       vpx_codec_iface_t iface,
                                       vpx_codec_dec_cfg_t cfg,
                                       vpx_codec_flags_t flags, 
                                       int ver = VPX_DECODER_ABI_VERSION) 
        {
            vpx_codec_err_t res;

            if (ver != VPX_DECODER_ABI_VERSION)
                res = vpx_codec_err_t.VPX_CODEC_ABI_MISMATCH;
            else if (ctx == null || iface == null)
                res = vpx_codec_err_t.VPX_CODEC_INVALID_PARAM;
            else if (iface.abi_version != vpx_codec_internal.VPX_CODEC_INTERNAL_ABI_VERSION)
                res = vpx_codec_err_t.VPX_CODEC_ABI_MISMATCH;
            else if (((flags & VPX_CODEC_USE_POSTPROC) > 0) &&
                     ((iface.caps & VPX_CODEC_CAP_POSTPROC) == 0))
                res = vpx_codec_err_t.VPX_CODEC_INCAPABLE;
            else if (((flags & VPX_CODEC_USE_ERROR_CONCEALMENT) > 0) &&
                     ((iface.caps & VPX_CODEC_CAP_ERROR_CONCEALMENT) == 0))
                res = vpx_codec_err_t.VPX_CODEC_INCAPABLE;
            else if (((flags & VPX_CODEC_USE_INPUT_FRAGMENTS) > 0) &&
                     ((iface.caps & VPX_CODEC_CAP_INPUT_FRAGMENTS) == 0))
                res = vpx_codec_err_t.VPX_CODEC_INCAPABLE;
            else if ((iface.caps & vpx_codec.VPX_CODEC_CAP_DECODER) == 0)
                res = vpx_codec_err_t.VPX_CODEC_INCAPABLE;
            else
            {
                //memset(ctx, 0, sizeof(*ctx));
                ctx.iface = iface;
                ctx.name = iface.name;
                ctx.priv = null;
                ctx.init_flags = flags;
                //ctx.config.dec = cfg;
                ctx.dec_cfg = cfg;

                res = ctx.iface.init(ctx, null);
                if (res != vpx_codec_err_t.VPX_CODEC_OK)
                {
                    //ctx.err_detail = ctx.priv != null ? ctx.priv.err_detail : null;
                    vpx_codec.vpx_codec_destroy(ctx);
                }
            }

            //return SAVE_STATUS(ctx, res);
            return ctx != null ? (ctx.err = res) : res;
        }

        /// <summary>
        /// Convenience macro for vpx_codec_dec_init_ver()
        ///
        /// Ensures the ABI version parameter is properly set.
        /// </summary>
        public static vpx_codec_err_t vpx_codec_dec_init(vpx_codec_ctx_t ctx,
                                       vpx_codec_iface_t iface,
                                       vpx_codec_dec_cfg_t cfg,
                                       vpx_codec_flags_t flags)
        {
            return vpx_codec_dec_init_ver(ctx, iface, cfg, flags);
        }

        //vpx_codec_err_t vpx_codec_peek_stream_info(vpx_codec_iface_t* iface,
        //                                           const uint8_t* data,
        //                                           unsigned int data_sz,
        //                                           vpx_codec_stream_info_t* si)
        //{
        //    vpx_codec_err_t res;

        //    if (!iface || !data || !data_sz || !si ||
        //        si->sz < sizeof(vpx_codec_stream_info_t))
        //        res = VPX_CODEC_INVALID_PARAM;
        //    else
        //    {
        //        /* Set default/unknown values */
        //        si->w = 0;
        //        si->h = 0;

        //        res = iface->dec.peek_si(data, data_sz, si);
        //    }

        //    return res;
        //}

        //vpx_codec_err_t vpx_codec_get_stream_info(vpx_codec_ctx_t* ctx,
        //                                          vpx_codec_stream_info_t* si)
        //{
        //    vpx_codec_err_t res;

        //    if (!ctx || !si || si->sz < sizeof(vpx_codec_stream_info_t))
        //        res = VPX_CODEC_INVALID_PARAM;
        //    else if (!ctx->iface || !ctx->priv)
        //        res = VPX_CODEC_ERROR;
        //    else
        //    {
        //        /* Set default/unknown values */
        //        si->w = 0;
        //        si->h = 0;

        //        res = ctx->iface->dec.get_si(get_alg_priv(ctx), si);
        //    }

        //    return SAVE_STATUS(ctx, res);
        //}

        public unsafe static vpx_codec_err_t vpx_codec_decode(vpx_codec_ctx_t ctx, byte* data,
                                         uint data_sz, IntPtr user_priv, long deadline)
        {
            vpx_codec_err_t res;

            /* Sanity checks */
            /* NULL data ptr allowed if data_sz is 0 too */
            if (ctx == null || data == null || data_sz == 0)
                res = vpx_codec_err_t.VPX_CODEC_INVALID_PARAM;
            else if (ctx.iface == null || ctx.priv == null)
                res = vpx_codec_err_t.VPX_CODEC_ERROR;
            else
            {
                res = ctx.iface.dec.decode(get_alg_priv(ctx), data, data_sz, user_priv,
                                             deadline);
            }

            return ctx != null ? (ctx.err = res) : res;
        }

        public static vpx_image_t vpx_codec_get_frame(vpx_codec_ctx_t ctx, vpx_codec_iter_t iter)
        {
            vpx_image_t img;

            if (ctx == null || iter  == null|| ctx.iface == null || ctx.priv == null)
            {
                img = null;
            }
            else
            {
                img = ctx.iface.dec.get_frame(get_alg_priv(ctx), iter);
            }

            return img;
        }

        //vpx_codec_err_t vpx_codec_register_put_frame_cb(vpx_codec_ctx_t* ctx,
        //                                                vpx_codec_put_frame_cb_fn_t cb,
        //                                                void* user_priv)
        //{
        //    vpx_codec_err_t res;

        //    if (!ctx || !cb)
        //        res = VPX_CODEC_INVALID_PARAM;
        //    else if (!ctx->iface || !ctx->priv)
        //        res = VPX_CODEC_ERROR;
        //    else if (!(ctx->iface->caps & VPX_CODEC_CAP_PUT_FRAME))
        //        res = VPX_CODEC_INCAPABLE;
        //    else
        //    {
        //        ctx->priv->dec.put_frame_cb.u.put_frame = cb;
        //        ctx->priv->dec.put_frame_cb.user_priv = user_priv;
        //        res = VPX_CODEC_OK;
        //    }

        //    return SAVE_STATUS(ctx, res);
        //}

        //vpx_codec_err_t vpx_codec_register_put_slice_cb(vpx_codec_ctx_t* ctx,
        //                                                vpx_codec_put_slice_cb_fn_t cb,
        //                                                void* user_priv)
        //{
        //    vpx_codec_err_t res;

        //    if (!ctx || !cb)
        //        res = VPX_CODEC_INVALID_PARAM;
        //    else if (!ctx->iface || !ctx->priv)
        //        res = VPX_CODEC_ERROR;
        //    else if (!(ctx->iface->caps & VPX_CODEC_CAP_PUT_SLICE))
        //        res = VPX_CODEC_INCAPABLE;
        //    else
        //    {
        //        ctx->priv->dec.put_slice_cb.u.put_slice = cb;
        //        ctx->priv->dec.put_slice_cb.user_priv = user_priv;
        //        res = VPX_CODEC_OK;
        //    }

        //    return SAVE_STATUS(ctx, res);
        //}

        //vpx_codec_err_t vpx_codec_set_frame_buffer_functions(
        //    vpx_codec_ctx_t* ctx, vpx_get_frame_buffer_cb_fn_t cb_get,
        //    vpx_release_frame_buffer_cb_fn_t cb_release, void* cb_priv)
        //{
        //    vpx_codec_err_t res;

        //    if (!ctx || !cb_get || !cb_release)
        //    {
        //        res = VPX_CODEC_INVALID_PARAM;
        //    }
        //    else if (!ctx->iface || !ctx->priv)
        //    {
        //        res = VPX_CODEC_ERROR;
        //    }
        //    else if (!(ctx->iface->caps & VPX_CODEC_CAP_EXTERNAL_FRAME_BUFFER))
        //    {
        //        res = VPX_CODEC_INCAPABLE;
        //    }
        //    else
        //    {
        //        res = ctx->iface->dec.set_fb_fn(get_alg_priv(ctx), cb_get, cb_release,
        //                                        cb_priv);
        //    }

        //    return SAVE_STATUS(ctx, res);
        //}

    }
}
