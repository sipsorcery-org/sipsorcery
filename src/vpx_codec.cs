//-----------------------------------------------------------------------------
// Filename: vpx_codec.cs
//
// Description: Port of:
//  - vpx_codec.h
//  - vpx_codec.c
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

/*!\defgroup codec Common Algorithm Interface
 * This abstraction allows applications to easily support multiple video
 * formats with minimal code duplication. This section describes the interface
 * common to all codecs (both encoders and decoders).
 * @{
 */

/*!\file
 * \brief Describes the codec algorithm interface to applications.
 *
 * This file describes the interface between an application and a
 * video codec algorithm.
 *
 * An application instantiates a specific codec instance by using
 * vpx_codec_dec_init() or vpx_codec_enc_init() and a pointer to the
 * algorithm's interface structure:
 *     <pre>
 *     my_app.c:
 *       extern vpx_codec_iface_t my_codec;
 *       {
 *           vpx_codec_ctx_t algo;
 *           int threads = 4;
 *           vpx_codec_dec_cfg_t cfg = { threads, 0, 0 };
 *           res = vpx_codec_dec_init(&algo, &my_codec, &cfg, 0);
 *       }
 *     </pre>
 *
 * Once initialized, the instance is manged using other functions from
 * the vpx_codec_* family.
 */

using System;
using System.Collections.Generic;

/*! \brief Codec capabilities bitfield
 *
 *  Each codec advertises the capabilities it supports as part of its
 *  ::vpx_codec_iface_t interface structure. Capabilities are extra interfaces
 *  or functionality, and are not required to be supported.
 *
 *  The available flags are specified by VPX_CODEC_CAP_* defines.
 */
//typedef long vpx_codec_caps_t;
using vpx_codec_caps_t = System.UInt64;

/*! \brief Initialization-time Feature Enabling
 *
 *  Certain codec features must be known at initialization time, to allow for
 *  proper memory allocation.
 *
 *  The available flags are specified by VPX_CODEC_USE_* defines.
 */
//typedef long vpx_codec_flags_t;
using vpx_codec_flags_t = System.Int64;

/*!\brief Codec interface structure.
 *
 * Contains function pointers and other data private to the codec
 * implementation. This structure is opaque to the application.
 */
//typedef const struct vpx_codec_iface vpx_codec_iface_t;
//using vpx_codec_iface_t = Vpx.Net.vpx_codec_iface;

/*!\brief Codec private data structure.
 *
 * Contains data private to the codec implementation. This structure is opaque
 * to the application.
 */
//typedef struct vpx_codec_priv vpx_codec_priv_t;
//using vpx_codec_priv_t = Vpx.Net.vpx_codec_priv;

/*!\brief Iterator
 *
 * Opaque storage used for iterating over lists.
 */
//typedef const void* vpx_codec_iter_t;
using vpx_codec_iter_t = System.IntPtr;

namespace Vpx.Net
{
    public enum vpx_codec_err_t
    {
        /*!\brief Operation completed without error */
        VPX_CODEC_OK,

        /*!\brief Unspecified error */
        VPX_CODEC_ERROR,

        /*!\brief Memory operation failed */
        VPX_CODEC_MEM_ERROR,

        /*!\brief ABI version mismatch */
        VPX_CODEC_ABI_MISMATCH,

        /*!\brief Algorithm does not have required capability */
        VPX_CODEC_INCAPABLE,

        /*!\brief The given bitstream is not supported.
         *
         * The bitstream was unable to be parsed at the highest level. The decoder
         * is unable to proceed. This error \ref SHOULD be treated as fatal to the
         * stream. */
        VPX_CODEC_UNSUP_BITSTREAM,

        /*!\brief Encoded bitstream uses an unsupported feature
         *
         * The decoder does not implement a feature required by the encoder. This
         * return code should only be used for features that prevent future
         * pictures from being properly decoded. This error \ref MAY be treated as
         * fatal to the stream or \ref MAY be treated as fatal to the current GOP.
         */
        VPX_CODEC_UNSUP_FEATURE,

        /*!\brief The coded data for this stream is corrupt or incomplete
         *
         * There was a problem decoding the current frame.  This return code
         * should only be used for failures that prevent future pictures from
         * being properly decoded. This error \ref MAY be treated as fatal to the
         * stream or \ref MAY be treated as fatal to the current GOP. If decoding
         * is continued for the current GOP, artifacts may be present.
         */
        VPX_CODEC_CORRUPT_FRAME,

        /*!\brief An application-supplied parameter is not valid.
         *
         */
        VPX_CODEC_INVALID_PARAM,

        /*!\brief An iterator reached the end of list.
         *
         */
        VPX_CODEC_LIST_END
    }

    /*!\brief Bit depth for codec
     * *
     * This enumeration determines the bit depth of the codec.
     */
    public enum vpx_bit_depth_t
    {
        VPX_BITS_8 = 8,   /**<  8 bits */
        VPX_BITS_10 = 10, /**< 10 bits */
        VPX_BITS_12 = 12, /**< 12 bits */
    }

    /*!\brief Codec context structure
     *
     * All codecs \ref MUST support this context structure fully. In general,
     * this data should be considered private to the codec algorithm, and
     * not be manipulated or examined by the calling application. Applications
     * may reference the 'name' member to get a printable description of the
     * algorithm.
     */
    //    typedef struct vpx_codec_ctx
    //    {
    //        const char* name;             /**< Printable interface name */
    //        vpx_codec_iface_t* iface;     /**< Interface pointers */
    //        vpx_codec_err_t err;          /**< Last returned error */
    //        const char* err_detail;       /**< Detailed info, if available */
    //        vpx_codec_flags_t init_flags; /**< Flags passed at init time */
    //        union {
    //    /**< Decoder Configuration Pointer */
    //    const struct vpx_codec_dec_cfg *dec;
    //    /**< Encoder Configuration Pointer */
    //    const struct vpx_codec_enc_cfg *enc;
    //    const void* raw;
    //    }
    //    config;               /**< Configuration pointer aliasing union */
    //  vpx_codec_priv_t* priv; /**< Algorithm private storage */
    //}
    //vpx_codec_ctx_t;

    /// <summary>
    /// Codec context structure.
    /// 
    /// All codecs MUST support this context structure fully. In general,
    /// this data should be considered private to the codec algorithm, and
    /// not be manipulated or examined by the calling application.Applications
    /// may reference the 'name' member to get a printable description of the
    /// algorithm.
    /// </summary>
    public class vpx_codec_ctx_t
    {
        public string name;                     /**< Printable interface name */
        public vpx_codec_iface_t iface;         /**< Interface pointers */
        public vpx_codec_err_t err;             /**< Last returned error */
        public string err_detail;               /**< Detailed info, if available */
        public vpx_codec_flags_t init_flags;    /**< Flags passed at init time */
        //union {
        //    /**< Decoder Configuration Pointer */
        //    const struct vpx_codec_dec_cfg *dec;
        //    /**< Encoder Configuration Pointer */
        //    const struct vpx_codec_enc_cfg *enc;
        //    const void* raw;
        // }
        //config;               /**< Configuration pointer aliasing union */
        public vpx_codec_dec_cfg_t dec_cfg;
        public vpx_codec_enc_cfg_t enc_cfg;
        //public vpx_codec_priv_t priv; /**< Algorithm private storage */
        public vpx_codec_alg_priv_t priv;
    }

    public class vpx_codec
    {
        public const int VPX_CODEC_ABI_VERSION = 4 + vpx_image_t.VPX_IMAGE_ABI_VERSION;

        public const int VPX_CODEC_CAP_DECODER = 0x1; /**< Is a decoder */
        public const int VPX_CODEC_CAP_ENCODER = 0x2; /**< Is an encoder */

        /*! Can support images at greater than 8 bitdepth.
         */
        public const int VPX_CODEC_CAP_HIGHBITDEPTH = 0x4;

        /**< extract major from packed version */
        public static int VPX_VERSION_MAJOR(int v) => ((v) >> 16) & 0xff;

        /**< extract minor from packed version */
        public static int VPX_VERSION_MINOR(int v) => ((v) >> 8) & 0xff;

        /**< extract patch from packed version */
        public static int VPX_VERSION_PATCH(int v) => ((v) >> 0) & 0xff;

        /*
         * Library Version Number Interface
         *
         * For example, see the following sample return values:
         *     vpx_codec_version()           (1<<16 | 2<<8 | 3)
         *     vpx_codec_version_str()       "v1.2.3-rc1-16-gec6a1ba"
         *     vpx_codec_version_extra_str() "rc1-16-gec6a1ba"
         */

        /*!\brief Return the version information (as an integer)
         *
         * Returns a packed encoding of the library version number. This will only
         * include
         * the major.minor.patch component of the version number. Note that this encoded
         * value should be accessed through the macros provided, as the encoding may
         * change
         * in the future.
         *
         */
        public static int vpx_codec_version() => vpx_version.VERSION_PACKED;

        /*!\brief Return the version information (as a string)
         *
         * Returns a printable string containing the full library version number. This
         * may
         * contain additional text following the three digit version number, as to
         * indicate
         * release candidates, prerelease versions, etc.
         *
         */
        public static string vpx_codec_version_str() => vpx_version.VERSION_STRING_NOSP;

        /*!\brief Return the version information (as a string)
         *
         * Returns a printable "extra string". This is the component of the string
         * returned
         * by vpx_codec_version_str() following the three digit version number.
         *
         */
        public static string vpx_codec_version_extra_str => vpx_version.VERSION_EXTRA;

        /*!\brief Return the build configuration
         *
         * Returns a printable string containing an encoded version of the build
         * configuration. This may be useful to vpx support.
         *
         */
        public static string vpx_codec_build_config() => vpx_config.vpx_codec_build_config();

        /*!\brief Return the version major number */
        public static int vpx_codec_version_major() => ((vpx_codec_version() >> 16) & 0xff);

        /*!\brief Return the version minor number */
        public static int vpx_codec_version_minor() => ((vpx_codec_version() >> 8) & 0xff);

        /*!\brief Return the version patch number */
        public static int vpx_codec_version_patch() => ((vpx_codec_version() >> 0) & 0xff);

        /*!\brief Return the name for a given interface
         *
         * Returns a human readable string for name of the given codec interface.
         *
         * \param[in]    iface     Interface pointer
         *
         */
        //const char* vpx_codec_iface_name(vpx_codec_iface_t * iface);

        public static string vpx_codec_iface_name(vpx_codec_iface_t iface)
        {
            return iface != null ? iface.name : "<invalid interface>";
        }

        /*!\brief Convert error number to printable string
         *
         * Returns a human readable string for the last error returned by the
         * algorithm. The returned error will be one line and will not contain
         * any newline characters.
         *
         *
         * \param[in]    err     Error number.
         *
         */
        // const char *vpx_codec_err_to_string(vpx_codec_err_t err);

        public static string vpx_codec_err_to_string(vpx_codec_err_t err)
        {
            switch (err)
            {
                case vpx_codec_err_t.VPX_CODEC_ERROR: return "Unspecified internal error";
                case vpx_codec_err_t.VPX_CODEC_MEM_ERROR: return "Memory allocation error";
                case vpx_codec_err_t.VPX_CODEC_ABI_MISMATCH: return "ABI version mismatch";
                case vpx_codec_err_t.VPX_CODEC_INCAPABLE:
                    return "Codec does not implement requested capability";
                case vpx_codec_err_t.VPX_CODEC_UNSUP_BITSTREAM:
                    return "Bitstream not supported by this decoder";
                case vpx_codec_err_t.VPX_CODEC_UNSUP_FEATURE:
                    return "Bitstream required feature not supported by this decoder";
                case vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME: return "Corrupt frame detected";
                case vpx_codec_err_t.VPX_CODEC_INVALID_PARAM: return "Invalid parameter";
                case vpx_codec_err_t.VPX_CODEC_LIST_END: return "End of iterated list";
            }

            return "Unrecognized error code";
        }

        /*!\brief Retrieve error synopsis for codec context
         *
         * Returns a human readable string for the last error returned by the
         * algorithm. The returned error will be one line and will not contain
         * any newline characters.
         *
         *
         * \param[in]    ctx     Pointer to this instance's context.
         *
         */
        // const char *vpx_codec_error(vpx_codec_ctx_t *ctx);

        public static string vpx_codec_error(vpx_codec_ctx_t ctx)
        {
            return ctx != null ? vpx_codec_err_to_string(ctx.err)
               : vpx_codec_err_to_string(vpx_codec_err_t.VPX_CODEC_INVALID_PARAM);
        }

        /*!\brief Retrieve detailed error information for codec context
         *
         * Returns a human readable string providing detailed information about
         * the last error.
         *
         * \param[in]    ctx     Pointer to this instance's context.
         *
         * \retval NULL
         *     No detailed information is available.
         */
        // const char *vpx_codec_error_detail(vpx_codec_ctx_t *ctx);

        public static string vpx_codec_error_detail(vpx_codec_ctx_t ctx)
        {
            if (ctx != null)
            {
                //return ctx.priv != null ? ctx.priv.err_detail : ctx.err_detail;
                return ctx.err_detail;
            }

            return null;
        }

        /* REQUIRED FUNCTIONS
         *
         * The following functions are required to be implemented for all codecs.
         * They represent the base case functionality expected of all codecs.
         */

        /*!\brief Destroy a codec instance
         *
         * Destroys a codec context, freeing any associated memory buffers.
         *
         * \param[in] ctx   Pointer to this instance's context
         *
         * \retval #VPX_CODEC_OK
         *     The codec algorithm initialized.
         * \retval #VPX_CODEC_MEM_ERROR
         *     Memory allocation failed.
         */
        // vpx_codec_err_t vpx_codec_destroy(vpx_codec_ctx_t *ctx);

        public static vpx_codec_err_t vpx_codec_destroy(vpx_codec_ctx_t ctx)
        {
            vpx_codec_err_t res;

            if (ctx == null)
            {
                res = vpx_codec_err_t.VPX_CODEC_INVALID_PARAM;
            }
            else if (ctx.iface == null || ctx.priv == null)
            {
                res = vpx_codec_err_t.VPX_CODEC_ERROR;
            }
            else
            {
                //ctx.iface.destroy((vpx_codec_alg_priv_t)ctx.priv);

                ctx.iface = null;
                ctx.name = null;
                ctx.priv = null;
                res = vpx_codec_err_t.VPX_CODEC_OK;
            }

            // return SAVE_STATUS(ctx, res);
            return ctx != null ? (ctx.err = res) : res;
        }

        /*!\brief Get the capabilities of an algorithm.
         *
         * Retrieves the capabilities bitfield from the algorithm's interface.
         *
         * \param[in] iface   Pointer to the algorithm interface
         *
         */
        // vpx_codec_caps_t vpx_codec_get_caps(vpx_codec_iface_t *iface);

        public static vpx_codec_caps_t vpx_codec_get_caps(vpx_codec_iface_t iface)
        {
            return (iface != null) ? iface.caps : 0;
        }

        /*!\brief Control algorithm
         *
         * This function is used to exchange algorithm specific data with the codec
         * instance. This can be used to implement features specific to a particular
         * algorithm.
         *
         * This wrapper function dispatches the request to the helper function
         * associated with the given ctrl_id. It tries to call this function
         * transparently, but will return #VPX_CODEC_ERROR if the request could not
         * be dispatched.
         *
         * Note that this function should not be used directly. Call the
         * #vpx_codec_control wrapper macro instead.
         *
         * \param[in]     ctx              Pointer to this instance's context
         * \param[in]     ctrl_id          Algorithm specific control identifier
         *
         * \retval #VPX_CODEC_OK
         *     The control request was processed.
         * \retval #VPX_CODEC_ERROR
         *     The control request was not processed.
         * \retval #VPX_CODEC_INVALID_PARAM
         *     The data was not valid.
         */
        //vpx_codec_err_t vpx_codec_control_(vpx_codec_ctx_t* ctx, int ctrl_id, ...)
        //public static vpx_codec_err_t vpx_codec_control_(vpx_codec_ctx_t ctx, int ctrl_id, List<Object> valist)
        //{
        //    vpx_codec_err_t res;

        //    if (ctx == null || ctrl_id == 0)
        //    {
        //        res = vpx_codec_err_t.VPX_CODEC_INVALID_PARAM;
        //    }
        //    else if (ctx.iface == null || ctx.priv == null || ctx.iface.ctrl_maps == null)
        //    {
        //        res = vpx_codec_err_t.VPX_CODEC_ERROR;
        //    }
        //    else
        //    {
        //        vpx_codec_ctrl_fn_map_t entry;

        //        res = vpx_codec_err_t.VPX_CODEC_INCAPABLE;

        //        for (entry = ctx.iface.ctrl_maps; entry.fn; entry++)
        //        {
        //            if (entry.ctrl_id == 0 || entry.ctrl_id == ctrl_id)
        //            {
        //                va_list ap;

        //                va_start(ap, ctrl_id);
        //                res = entry.fn((vpx_codec_alg_priv_t)ctx->priv, ap);
        //                va_end(ap);
        //                break;
        //            }
        //        }
        //    }

        //    //return SAVE_STATUS(ctx, res);
        //    return ctx != null ? (ctx.err = res) : res;
        //}

        /*!\brief vpx_codec_control wrapper macro
         *
         * This macro allows for type safe conversions across the variadic parameter
         * to vpx_codec_control_().
         *
         * \internal
         * It works by dispatching the call to the control function through a wrapper
         * function named with the id parameter.
         */
        //#define vpx_codec_control(ctx, id, data) \
        //   vpx_codec_control_##id(ctx, id, data) /**<\hideinitializer*/

        /*!\brief vpx_codec_control type definition macro
         *
         * This macro allows for type safe conversions across the variadic parameter
         * to vpx_codec_control_(). It defines the type of the argument for a given
         * control identifier.
         *
         * \internal
         * It defines a static function with
         * the correctly typed arguments as a wrapper to the type-unsafe internal
         * function.
         */
        //#define VPX_CTRL_USE_TYPE(id, typ)                                           \
        //    static vpx_codec_err_t vpx_codec_control_##id(vpx_codec_ctx_t *, int, typ) \
        //        VPX_UNUSED;                                                            \
        //                                                                                \
        //    static vpx_codec_err_t vpx_codec_control_##id(vpx_codec_ctx_t *ctx,        \
        //                                                int ctrl_id, typ data) {     \
        //    return vpx_codec_control_(ctx, ctrl_id, data);                           \
        //    } /**<\hideinitializer*/

        //public static vpx_codec_err_t vpx_codec_control_hhid(vpx_codec_ctx_t ctx, int ctrl_id, typ data)
        //{
        //    return vpx_codec_control_(ctx, ctrl_id, data);
        //}

        /*!\brief vpx_codec_control deprecated type definition macro
         *
         * Like #VPX_CTRL_USE_TYPE, but indicates that the specified control is
         * deprecated and should not be used. Consult the documentation for your
         * codec for more information.
         *
         * \internal
         * It defines a static function with the correctly typed arguments as a
         * wrapper to the type-unsafe internal function.
         */
        //#define VPX_CTRL_USE_TYPE_DEPRECATED(id, typ)                            \
        //VPX_DECLSPEC_DEPRECATED static vpx_codec_err_t vpx_codec_control_##id( \
        //      vpx_codec_ctx_t *, int, typ) VPX_DEPRECATED VPX_UNUSED;            \
        //                                                                         \
        //  VPX_DECLSPEC_DEPRECATED static vpx_codec_err_t vpx_codec_control_##id( \
        //      vpx_codec_ctx_t *ctx, int ctrl_id, typ data) {                     \
        //    return vpx_codec_control_(ctx, ctrl_id, data);                       \
        //  } /**<\hideinitializer*/

        /*!\brief vpx_codec_control void type definition macro
         *
         * This macro allows for type safe conversions across the variadic parameter
         * to vpx_codec_control_(). It indicates that a given control identifier takes
         * no argument.
         *
         * \internal
         * It defines a static function without a data argument as a wrapper to the
         * type-unsafe internal function.
         */
        //#define VPX_CTRL_VOID(id)                                               \
        //static vpx_codec_err_t vpx_codec_control_##id(vpx_codec_ctx_t *, int) \
        //      VPX_UNUSED;                                                       \
        //                                                                        \
        //  static vpx_codec_err_t vpx_codec_control_##id(vpx_codec_ctx_t *ctx,   \
        //                                                int ctrl_id) {          \
        //    return vpx_codec_control_(ctx, ctrl_id);                            \
        //  } /**<\hideinitializer*/

        //#endif

        //public static vpx_codec_err_t vpx_codec_control<T>(vpx_codec_ctx_t ctx, int ctrl_id)
        //{
        //    return vpx_codec_control_(ctx, ctrl_id);
        //}

        /// <remarks>
        /// Original defintion:
        /// void vpx_internal_error(struct vpx_internal_error_info *info,
        ///  vpx_codec_err_t error, const char* fmt, ...) 
        /// The use of the "va_list" parameter when calling this function was minimal.
        /// That left the "fmt" parameter being used as a descriptive error message rather than
        /// a format string.
        /// </remarks>
        public static void vpx_internal_error(ref vpx_internal_error_info info, vpx_codec_err_t error, string fmt)
        {
            //va_list ap;

            info.error_code = error;
            info.has_detail = 0;
            info.detail = fmt;

            //if (fmt)
            //{
            //    size_t sz = sizeof(info->detail);

            //    info->has_detail = 1;
            //    va_start(ap, fmt);
            //    vsnprintf(info->detail, sz - 1, fmt, ap);
            //    va_end(ap);
            //    info->detail[sz - 1] = '\0';
            //}

            //if (info->setjmp) longjmp(info->jmp, info->error_code);

            VpxException vpxExcp = new VpxException(error, fmt);
            throw vpxExcp;
        }
    }
}