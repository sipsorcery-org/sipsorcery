//-----------------------------------------------------------------------------
// Filename: vpx_image.cs
//
// Description: Port of:
//  - vpx_image.h
//  - vpx_image.c
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

/*!\file
 * \brief Describes the vpx image descriptor and associated operations
 *
 */

using System;
using System.Runtime.InteropServices;

namespace Vpx.Net
{
    /*!\brief List of supported image formats */
    public enum vpx_img_fmt_t
    {
        VPX_IMG_FMT_NONE,
        VPX_IMG_FMT_YV12 =
            vpx_image_t.VPX_IMG_FMT_PLANAR | vpx_image_t.VPX_IMG_FMT_UV_FLIP | 1, /**< planar YVU */
        VPX_IMG_FMT_I420 = vpx_image_t.VPX_IMG_FMT_PLANAR | 2,
        VPX_IMG_FMT_I422 = vpx_image_t.VPX_IMG_FMT_PLANAR | 5,
        VPX_IMG_FMT_I444 = vpx_image_t.VPX_IMG_FMT_PLANAR | 6,
        VPX_IMG_FMT_I440 = vpx_image_t.VPX_IMG_FMT_PLANAR | 7,
        VPX_IMG_FMT_NV12 = vpx_image_t.VPX_IMG_FMT_PLANAR | 9,
        VPX_IMG_FMT_I42016 = VPX_IMG_FMT_I420 | vpx_image_t.VPX_IMG_FMT_HIGHBITDEPTH,
        VPX_IMG_FMT_I42216 = VPX_IMG_FMT_I422 | vpx_image_t.VPX_IMG_FMT_HIGHBITDEPTH,
        VPX_IMG_FMT_I44416 = VPX_IMG_FMT_I444 | vpx_image_t.VPX_IMG_FMT_HIGHBITDEPTH,
        VPX_IMG_FMT_I44016 = VPX_IMG_FMT_I440 | vpx_image_t.VPX_IMG_FMT_HIGHBITDEPTH
    }

    /*!\brief List of supported color spaces */
    public enum vpx_color_space_t
    {
        VPX_CS_UNKNOWN = 0,   /**< Unknown */
        VPX_CS_BT_601 = 1,    /**< BT.601 */
        VPX_CS_BT_709 = 2,    /**< BT.709 */
        VPX_CS_SMPTE_170 = 3, /**< SMPTE.170 */
        VPX_CS_SMPTE_240 = 4, /**< SMPTE.240 */
        VPX_CS_BT_2020 = 5,   /**< BT.2020 */
        VPX_CS_RESERVED = 6,  /**< Reserved */
        VPX_CS_SRGB = 7       /**< sRGB */
    }

    /*!\brief List of supported color range */
    public enum vpx_color_range_t
    {
        VPX_CR_STUDIO_RANGE = 0, /**< Y [16..235], UV [16..240] */
        VPX_CR_FULL_RANGE = 1    /**< YUV/RGB [0..255] */
    }

    /**\brief Representation of a rectangle on a surface */
    //typedef struct vpx_image_rect
    //{
    //    unsigned int x;   /**< leftmost column */
    //    unsigned int y;   /**< topmost row */
    //    unsigned int w;   /**< width */
    //    unsigned int h;   /**< height */
    //}
    //vpx_image_rect_t; /**< alias for struct vpx_image_rect */

    public struct vpx_image_rect_t
    {
        public uint x;   /**< leftmost column */
        public uint y;   /**< topmost row */
        public uint w;   /**< width */
        public uint h;   /**< height */
    }

    /**\brief Image Descriptor */
    //    typedef struct vpx_image
    //    {
    //        vpx_img_fmt_t fmt;       /**< Image Format */
    //        vpx_color_space_t cs;    /**< Color Space */
    //        vpx_color_range_t range; /**< Color Range */

    //        /* Image storage dimensions */
    //        unsigned int w;         /**< Stored image width */
    //        unsigned int h;         /**< Stored image height */
    //        unsigned int bit_depth; /**< Stored image bit-depth */

    //        /* Image display dimensions */
    //        unsigned int d_w; /**< Displayed image width */
    //        unsigned int d_h; /**< Displayed image height */

    //        /* Image intended rendering dimensions */
    //        unsigned int r_w; /**< Intended rendering image width */
    //        unsigned int r_h; /**< Intended rendering image height */

    //        /* Chroma subsampling info */
    //        unsigned int x_chroma_shift; /**< subsampling order, X */
    //        unsigned int y_chroma_shift; /**< subsampling order, Y */

    //        /* Image data pointers. */
    //#define VPX_PLANE_PACKED 0  /**< To be used for all packed formats */
    //#define VPX_PLANE_Y 0       /**< Y (Luminance) plane */
    //#define VPX_PLANE_U 1       /**< U (Chroma) plane */
    //#define VPX_PLANE_V 2       /**< V (Chroma) plane */
    //#define VPX_PLANE_ALPHA 3   /**< A (Transparency) plane */
    //        unsigned char* planes[4]; /**< pointer to the top left pixel for each plane */
    //        int stride[4];            /**< stride between rows for each plane */

    //        int bps; /**< bits per sample (for packed formats) */

    //        /*!\brief The following member may be set by the application to associate
    //         * data with this image.
    //         */
    //        void* user_priv;

    //        /* The following members should be treated as private. */
    //        unsigned char* img_data; /**< private */
    //        int img_data_owner;      /**< private */
    //        int self_allocd;         /**< private */

    //        void* fb_priv; /**< Frame buffer data associated with the image. */
    //    }
    //    vpx_image_t;   /**< alias for struct vpx_image */

    public unsafe class vpx_image_t : IDisposable
    {
        public const int VPX_IMAGE_ABI_VERSION = 5;

        public const int VPX_IMG_FMT_PLANAR = 0x100;       /**< Image is a planar format. */
        public const int VPX_IMG_FMT_UV_FLIP = 0x200;      /**< V plane precedes U in memory. */
        public const int VPX_IMG_FMT_HAS_ALPHA = 0x400;    /**< Image has an alpha channel. */
        public const int VPX_IMG_FMT_HIGHBITDEPTH = 0x800; /**< Image uses 16bit framebuffer. */

        public const int VPX_PLANE_PACKED = 0;  /**< To be used for all packed formats */
        public const int VPX_PLANE_Y = 0;       /**< Y (Luminance) plane */
        public const int VPX_PLANE_U = 1;       /**< U (Chroma) plane */
        public const int VPX_PLANE_V = 2;       /**< V (Chroma) plane */
        public const int VPX_PLANE_ALPHA = 3;   /**< A (Transparency) plane */

        public vpx_img_fmt_t fmt;       /**< Image Format */
        public vpx_color_space_t cs;    /**< Color Space */
        public vpx_color_range_t range; /**< Color Range */

        /* Image storage dimensions */
        public uint w;         /**< Stored image width */
        public uint h;         /**< Stored image height */
        public uint bit_depth; /**< Stored image bit-depth */

        /* Image display dimensions */
        public uint d_w; /**< Displayed image width */
        public uint d_h; /**< Displayed image height */

        /* Image intended rendering dimensions */
        public uint r_w; /**< Intended rendering image width */
        public uint r_h; /**< Intended rendering image height */

        /* Chroma subsampling info */
        public uint x_chroma_shift; /**< subsampling order, X */
        public uint y_chroma_shift; /**< subsampling order, Y */

        /* Image data pointers. */
        public byte*[] planes = new byte*[4];
        public int[] stride = new int[4];            /**< stride between rows for each plane */

        public int bps; /**< bits per sample (for packed formats) */

        /*!\brief The following member may be set by the application to associate
         * data with this image.
         */
        public void* user_priv;

        /* The following members should be treated as private. */
        internal byte* img_data; /**< private */
        internal int img_data_owner;      /**< private */
        internal int self_allocd;         /**< private */

        //public void* fb_priv; /**< Frame buffer data associated with the image. */

        public void Dispose()
        {
            if (img_data != null && img_data_owner > 0)
            {
                //vpx_free(img.img_data);
                Marshal.FreeHGlobal((IntPtr)img_data);
            }

            //if (img.self_allocd) free(img);
            //if (img.self_allocd > 0)
            //{
            //    img.Dispose();
            //}
        }

        /*!\brief Open a descriptor, allocating storage for the underlying image
         *
         * Returns a descriptor for storing an image of the given format. The
         * storage for the descriptor is allocated on the heap.
         *
         * \param[in]    img       Pointer to storage for descriptor. If this parameter
         *                         is NULL, the storage for the descriptor will be
         *                         allocated on the heap.
         * \param[in]    fmt       Format for the image
         * \param[in]    d_w       Width of the image
         * \param[in]    d_h       Height of the image
         * \param[in]    align     Alignment, in bytes, of the image buffer and
         *                         each row in the image(stride).
         *
         * \return Returns a pointer to the initialized image descriptor. If the img
         *         parameter is non-null, the value of the img parameter will be
         *         returned.
         */
        //vpx_image_t* vpx_img_alloc(vpx_image_t* img, vpx_img_fmt_t fmt,
        //                           unsigned int d_w, unsigned int d_h,
        //                           unsigned int align);

        public static vpx_image_t vpx_img_alloc(vpx_image_t img, vpx_img_fmt_t fmt,
                                   uint d_w, uint d_h, uint align)
        {
            return img_alloc_helper(img, fmt, d_w, d_h, align, align, null);
        }

        /*!\brief Open a descriptor, using existing storage for the underlying image
         *
         * Returns a descriptor for storing an image of the given format. The
         * storage for descriptor has been allocated elsewhere, and a descriptor is
         * desired to "wrap" that storage.
         *
         * \param[in]    img           Pointer to storage for descriptor. If this
         *                             parameter is NULL, the storage for the descriptor
         *                             will be allocated on the heap.
         * \param[in]    fmt           Format for the image
         * \param[in]    d_w           Width of the image
         * \param[in]    d_h           Height of the image
         * \param[in]    stride_align  Alignment, in bytes, of each row in the image.
         * \param[in]    img_data      Storage to use for the image
         *
         * \return Returns a pointer to the initialized image descriptor. If the img
         *         parameter is non-null, the value of the img parameter will be
         *         returned.
         */
        //vpx_image_t* vpx_img_wrap(vpx_image_t* img, vpx_img_fmt_t fmt, unsigned int d_w,
        //                          unsigned int d_h, unsigned int stride_align,
        //                          unsigned char* img_data);

        public vpx_image_t vpx_img_wrap(vpx_image_t img, vpx_img_fmt_t fmt, uint d_w,
                                  uint d_h, uint stride_align, byte* img_data)

        {
            /* By setting buf_align = 1, we don't change buffer alignment in this
            * function. */
            return img_alloc_helper(img, fmt, d_w, d_h, 1, stride_align, img_data);
        }

        /*!\brief Set the rectangle identifying the displayed portion of the image
         *
         * Updates the displayed rectangle (aka viewport) on the image surface to
         * match the specified coordinates and size.
         *
         * \param[in]    img       Image descriptor
         * \param[in]    x         leftmost column
         * \param[in]    y         topmost row
         * \param[in]    w         width
         * \param[in]    h         height
         *
         * \return 0 if the requested rectangle is valid, nonzero otherwise.
         */
        //int vpx_img_set_rect(vpx_image_t* img, unsigned int x, unsigned int y,
        //                     unsigned int w, unsigned int h);

        public static int vpx_img_set_rect(vpx_image_t img, uint x, uint y, uint w, uint h)
        {
            byte* data;

            if (x + w <= img.w && y + h <= img.h)
            {
                img.d_w = w;
                img.d_h = h;

                /* Calculate plane pointers */
                if (((int)img.fmt & (int)vpx_image_t.VPX_IMG_FMT_PLANAR) == 0)
                {
                    img.planes[vpx_image_t.VPX_PLANE_PACKED] =
                        img.img_data + x * img.bps / 8 + y * img.stride[vpx_image_t.VPX_PLANE_PACKED];
                }
                else
                {
                    int bytes_per_sample = (((int)img.fmt & (int)vpx_image_t.VPX_IMG_FMT_HIGHBITDEPTH) > 0) ? 2 : 1;
                    data = img.img_data;

                    if (((int)img.fmt & (int)vpx_image_t.VPX_IMG_FMT_HAS_ALPHA) > 0)
                    {
                        img.planes[vpx_image_t.VPX_PLANE_ALPHA] =
                            data + x * bytes_per_sample + y * img.stride[vpx_image_t.VPX_PLANE_ALPHA];
                        data += img.h * img.stride[vpx_image_t.VPX_PLANE_ALPHA];
                    }

                    img.planes[vpx_image_t.VPX_PLANE_Y] =
                        data + x * bytes_per_sample + y * img.stride[vpx_image_t.VPX_PLANE_Y];
                    data += img.h * img.stride[vpx_image_t.VPX_PLANE_Y];

                    if (img.fmt == vpx_img_fmt_t.VPX_IMG_FMT_NV12)
                    {
                        img.planes[vpx_image_t.VPX_PLANE_U] =
                            data + (x >> (int)img.x_chroma_shift) +
                            (y >> (int)img.y_chroma_shift) * img.stride[vpx_image_t.VPX_PLANE_U];
                        img.planes[vpx_image_t.VPX_PLANE_V] = img.planes[vpx_image_t.VPX_PLANE_U] + 1;
                    }
                    else if (((int)img.fmt & vpx_image_t.VPX_IMG_FMT_UV_FLIP) == 0)
                    {
                        img.planes[vpx_image_t.VPX_PLANE_U] =
                            data + (x >> (int)img.x_chroma_shift) * bytes_per_sample +
                            (y >> (int)img.y_chroma_shift) * img.stride[vpx_image_t.VPX_PLANE_U];
                        data += (img.h >> (int)img.y_chroma_shift) * img.stride[vpx_image_t.VPX_PLANE_U];
                        img.planes[vpx_image_t.VPX_PLANE_V] =
                            data + (x >> (int)img.x_chroma_shift) * bytes_per_sample +
                            (y >> (int)img.y_chroma_shift) * img.stride[vpx_image_t.VPX_PLANE_V];
                    }
                    else
                    {
                        img.planes[vpx_image_t.VPX_PLANE_V] =
                            data + (x >> (int)img.x_chroma_shift) * bytes_per_sample +
                            (y >> (int)img.y_chroma_shift) * img.stride[vpx_image_t.VPX_PLANE_V];
                        data += (img.h >> (int)img.y_chroma_shift) * img.stride[vpx_image_t.VPX_PLANE_V];
                        img.planes[vpx_image_t.VPX_PLANE_U] =
                            data + (x >> (int)img.x_chroma_shift) * bytes_per_sample +
                            (y >> (int)img.y_chroma_shift) * img.stride[vpx_image_t.VPX_PLANE_U];
                    }
                }
                return 0;
            }
            return -1;
        }

        /*!\brief Flip the image vertically (top for bottom)
         *
         * Adjusts the image descriptor's pointers and strides to make the image
         * be referenced upside-down.
         *
         * \param[in]    img       Image descriptor
         */
        //void vpx_img_flip(vpx_image_t* img);

        public static void vpx_img_flip(vpx_image_t img)
        {
            /* Note: In the calculation pointer adjustment calculation, we want the
             * rhs to be promoted to a signed type. Section 6.3.1.8 of the ISO C99
             * standard indicates that if the adjustment parameter is unsigned, the
             * stride parameter will be promoted to unsigned, causing errors when
             * the lhs is a larger type than the rhs.
             */
            img.planes[vpx_image_t.VPX_PLANE_Y] += (int)(img.d_h - 1) * img.stride[vpx_image_t.VPX_PLANE_Y];
            img.stride[vpx_image_t.VPX_PLANE_Y] = -img.stride[vpx_image_t.VPX_PLANE_Y];

            img.planes[vpx_image_t.VPX_PLANE_U] += (int)((img.d_h >> (int)img.y_chroma_shift) - 1) *
                                        img.stride[vpx_image_t.VPX_PLANE_U];
            img.stride[vpx_image_t.VPX_PLANE_U] = -img.stride[vpx_image_t.VPX_PLANE_U];

            img.planes[vpx_image_t.VPX_PLANE_V] += (int)((img.d_h >> (int)img.y_chroma_shift) - 1) *
                                        img.stride[vpx_image_t.VPX_PLANE_V];
            img.stride[vpx_image_t.VPX_PLANE_V] = -img.stride[vpx_image_t.VPX_PLANE_V];

            img.planes[vpx_image_t.VPX_PLANE_ALPHA] +=
                (int)(img.d_h - 1) * img.stride[vpx_image_t.VPX_PLANE_ALPHA];
            img.stride[vpx_image_t.VPX_PLANE_ALPHA] = -img.stride[vpx_image_t.VPX_PLANE_ALPHA];
        }

        /*!\brief Close an image descriptor
         *
         * Frees all allocated storage associated with an image descriptor.
         *
         * \param[in]    img       Image descriptor
         */
        //void vpx_img_free(vpx_image_t* img);

        public static void vpx_img_free(vpx_image_t img)
        {
            img?.Dispose();
        }

        private static vpx_image_t img_alloc_helper(vpx_image_t img, vpx_img_fmt_t fmt,
                                     uint d_w, uint d_h,
                                     uint buf_align,
                                     uint stride_align,
                                     byte* img_data)
        {
            uint h, w, s, xcs, ycs, bps;
            uint stride_in_bytes;
            int align;

            /* Treat align==0 like align==1 */
            if (buf_align == 0) buf_align = 1;

            /* Validate alignment (must be power of 2) */
            if ((buf_align & (buf_align - 1)) != 0) goto fail;

            /* Treat align==0 like align==1 */
            if (stride_align == 0) stride_align = 1;

            /* Validate alignment (must be power of 2) */
            if ((stride_align & (stride_align - 1)) != 0) goto fail;

            /* Get sample size for this format */
            switch (fmt)
            {
                case vpx_img_fmt_t.VPX_IMG_FMT_I420:
                case vpx_img_fmt_t.VPX_IMG_FMT_YV12:
                case vpx_img_fmt_t.VPX_IMG_FMT_NV12: bps = 12; break;
                case vpx_img_fmt_t.VPX_IMG_FMT_I422:
                case vpx_img_fmt_t.VPX_IMG_FMT_I440: bps = 16; break;
                case vpx_img_fmt_t.VPX_IMG_FMT_I444: bps = 24; break;
                case vpx_img_fmt_t.VPX_IMG_FMT_I42016: bps = 24; break;
                case vpx_img_fmt_t.VPX_IMG_FMT_I42216:
                case vpx_img_fmt_t.VPX_IMG_FMT_I44016: bps = 32; break;
                case vpx_img_fmt_t.VPX_IMG_FMT_I44416: bps = 48; break;
                default: bps = 16; break;
            }

            /* Get chroma shift values for this format */
            // For VPX_IMG_FMT_NV12, xcs needs to be 0 such that UV data is all read at
            // one time.
            switch (fmt)
            {
                case vpx_img_fmt_t.VPX_IMG_FMT_I420:
                case vpx_img_fmt_t.VPX_IMG_FMT_YV12:
                case vpx_img_fmt_t.VPX_IMG_FMT_I422:
                case vpx_img_fmt_t.VPX_IMG_FMT_I42016:
                case vpx_img_fmt_t.VPX_IMG_FMT_I42216: xcs = 1; break;
                default: xcs = 0; break;
            }

            switch (fmt)
            {
                case vpx_img_fmt_t.VPX_IMG_FMT_I420:
                case vpx_img_fmt_t.VPX_IMG_FMT_NV12:
                case vpx_img_fmt_t.VPX_IMG_FMT_I440:
                case vpx_img_fmt_t.VPX_IMG_FMT_YV12:
                case vpx_img_fmt_t.VPX_IMG_FMT_I42016:
                case vpx_img_fmt_t.VPX_IMG_FMT_I44016: ycs = 1; break;
                default: ycs = 0; break;
            }

            /* Calculate storage sizes. If the buffer was allocated externally, the width
             * and height shouldn't be adjusted. */
            w = d_w;
            h = d_h;
            s = (((int)fmt & vpx_image_t.VPX_IMG_FMT_PLANAR) > 0) ? w : bps * w / 8;
            s = (s + stride_align - 1) & ~(stride_align - 1);
            stride_in_bytes = (((int)fmt & vpx_image_t.VPX_IMG_FMT_HIGHBITDEPTH) > 0) ? s * 2 : s;

            /* Allocate the new image */
            if (img == null)
            {
                //img = (vpx_image_t*)calloc(1, sizeof(vpx_image_t));

                //if (!img) goto fail;

                img = new vpx_image_t();
                img.self_allocd = 1;
            }
            else
            {
                //memset(img, 0, sizeof(vpx_image_t));
            }

            img.img_data = img_data;

            if (img_data == null)
            {
                ulong alloc_size;
                /* Calculate storage sizes given the chroma subsampling */
                align = (1 << (int)xcs) - 1;
                w = (uint)((d_w + align) & ~align);
                align = (1 << (int)ycs) - 1;
                h = (uint)((d_h + align) & ~align);

                s = (((int)fmt & vpx_image_t.VPX_IMG_FMT_PLANAR) > 0) ? w : bps * w / 8;
                s = (s + stride_align - 1) & ~(stride_align - 1);
                stride_in_bytes = (((int)fmt & vpx_image_t.VPX_IMG_FMT_HIGHBITDEPTH) > 0) ? s * 2 : s;
                alloc_size = (((int)fmt & vpx_image_t.VPX_IMG_FMT_PLANAR) > 0) ? (ulong)h * s * bps / 8
                                                        : (ulong)h * s;

                //if (alloc_size != (size_t)alloc_size) goto fail;

                //img.img_data = (byte*)vpx_memalign(buf_align, (size_t)alloc_size);
                img.img_data = (byte*)Marshal.AllocHGlobal((int)alloc_size);
                img.img_data_owner = 1;
            }

            if (img.img_data == null) goto fail;

            img.fmt = fmt;
            img.bit_depth = (((int)fmt & vpx_image_t.VPX_IMG_FMT_HIGHBITDEPTH) > 0) ? 16U : 8U;
            img.w = w;
            img.h = h;
            img.x_chroma_shift = xcs;
            img.y_chroma_shift = ycs;
            img.bps = (int)bps;

            /* Calculate strides */
            img.stride[vpx_image_t.VPX_PLANE_Y] = img.stride[vpx_image_t.VPX_PLANE_ALPHA] = (int)stride_in_bytes;
            img.stride[vpx_image_t.VPX_PLANE_U] = img.stride[vpx_image_t.VPX_PLANE_V] = (int)(stride_in_bytes >> (int)xcs);

            /* Default viewport to entire image */
            if (vpx_img_set_rect(img, 0, 0, d_w, d_h) == 0) return img;

            fail:
            vpx_img_free(img);
            return null;
        }
    }
}
