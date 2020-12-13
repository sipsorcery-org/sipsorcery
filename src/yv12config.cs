//-----------------------------------------------------------------------------
// Filename: yv12config.cs
//
// Description: Port of:
//  - yv12config.h
//  - yv12config.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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
    /// <remarks>
    /// Copying this strucutre is precarious due to the native memory pointer
    /// buffer_alloc. It can only be freed (using vpx_mem.vpx_free) by one owner. A second
    /// attempt to free the same unmanaged memory will at best result in undefined behaviour
    /// and at worst a memory access violation exception.
    /// </remarks>
    public unsafe struct YV12_BUFFER_CONFIG
    {
        public int y_width;
        public int y_height;
        public int y_crop_width;
        public int y_crop_height;
        public int y_stride;

        public int uv_width;
        public int uv_height;
        public int uv_crop_width;
        public int uv_crop_height;
        public int uv_stride;

        public int alpha_width;
        public int alpha_height;
        public int alpha_stride;

        public byte* y_buffer;
        public byte* u_buffer;
        public byte* v_buffer;
        public byte* alpha_buffer;

        public byte* buffer_alloc;
        public ulong buffer_alloc_sz;
        public int border;
        public ulong frame_size;
        public int subsampling_x;
        public int subsampling_y;
        public uint bit_depth;
        public vpx_color_space_t color_space;
        public vpx_color_range_t color_range;
        public int render_width;
        public int render_height;

        public int corrupted;
        public int flags;

        public override bool Equals(object obj)
        {
            if (obj is YV12_BUFFER_CONFIG)
            {
                return this.Equals((YV12_BUFFER_CONFIG)obj);
            }
            return false;
        }

        public bool Equals(YV12_BUFFER_CONFIG y)
        {
            return
                y_width == y.y_width &&
                y_height == y.y_height &&
                y_crop_width == y.y_crop_width &&
                y_crop_height == y.y_crop_height &&
                y_stride == y.y_stride &&
                uv_width == y.uv_width &&
                uv_height == y.uv_height &&
                uv_crop_width == y.uv_crop_width &&
                uv_crop_height == y.uv_crop_height &&
                uv_stride == y.uv_stride &&
                alpha_width == y.alpha_width &&
                alpha_height == y.alpha_height &&
                alpha_stride == y.alpha_stride &&
                y_buffer == y.y_buffer &&
                u_buffer == y.u_buffer &&
                v_buffer == y.v_buffer &&
                alpha_buffer == y.alpha_buffer &&
                buffer_alloc == y.buffer_alloc &&
                buffer_alloc_sz == y.buffer_alloc_sz &&
                border == y.border &&
                frame_size == y.frame_size &&
                subsampling_x == y.subsampling_x &&
                subsampling_y == y.subsampling_y &&
                bit_depth == y.bit_depth &&
                color_space == y.color_space &&
                color_range == y.color_range &&
                render_width == y.render_width &&
                render_height == y.render_height &&
                corrupted == y.corrupted &&
                flags == y.flags;
        }

        public static bool operator ==(YV12_BUFFER_CONFIG lhs, YV12_BUFFER_CONFIG rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(YV12_BUFFER_CONFIG lhs, YV12_BUFFER_CONFIG rhs)
        {
            return !(lhs.Equals(rhs));
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public static class yv12config
    {
        public const int VP8BORDERINPIXELS = 32;
        public const int VP9INNERBORDERINPIXELS = 96;
        public const int VP9_INTERP_EXTEND = 4;
        public const int VP9_ENC_BORDER_IN_PIXELS = 160;
        public const int VP9_DEC_BORDER_IN_PIXELS = 32;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ybf"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="border">The border must be a multiple of 32.</param>
        /// <returns></returns>
        public static int vp8_yv12_alloc_frame_buffer(ref YV12_BUFFER_CONFIG ybf, int width, int height,
                                int border)
        {
            vp8_yv12_de_alloc_frame_buffer(ref ybf);
            return vp8_yv12_realloc_frame_buffer(ref ybf, width, height, border);
        }

        private unsafe static int vp8_yv12_realloc_frame_buffer(ref YV12_BUFFER_CONFIG ybf, int width,
                                  int height, int border)
        {
            int aligned_width = (width + 15) & ~15;
            int aligned_height = (height + 15) & ~15;
            int y_stride = ((aligned_width + 2 * border) + 31) & ~31;
            int yplane_size = (aligned_height + 2 * border) * y_stride;
            int uv_width = aligned_width >> 1;
            int uv_height = aligned_height >> 1;
            /** There is currently a bunch of code which assumes
             *  uv_stride == y_stride/2, so enforce this here. */
            int uv_stride = y_stride >> 1;
            int uvplane_size = (uv_height + border) * uv_stride;
            ulong frame_size = (ulong)yplane_size + 2 * (ulong)uvplane_size;

            if (ybf.buffer_alloc == null || (IntPtr)ybf.buffer_alloc == IntPtr.Zero)
            {
                ybf.buffer_alloc = (byte*)vpx_mem.vpx_memalign(32, frame_size);
                //ybf.buffer_alloc = (byte*)vpx_malloc((int)frame_size);
                ybf.buffer_alloc_sz = frame_size;
            }

            // Port AC: This check seems intended to catch cases where a YV12 buffer needs to be re-used
            // and the memory buffer was not released first. It would seem safer to simply release the 
            // existing memory if it was present and then allocate the new memory required. In order
            // to keep compatibility this logic is currently being left as is and the caveat is the 
            // vp8_yv12_de_alloc_frame_buffer will always need to be called before re-using a buffer
            // with a different size.
            if (ybf.buffer_alloc == null ||
                (IntPtr)ybf.buffer_alloc == IntPtr.Zero || 
                ybf.buffer_alloc_sz < frame_size) return -1;

            /* Only support allocating buffers that have a border that's a multiple
             * of 32. The border restriction is required to get 16-byte alignment of
             * the start of the chroma rows without introducing an arbitrary gap
             * between planes, which would break the semantics of things like
             * vpx_img_set_rect(). */
            if ((border & 0x1f) > 0) return -3;

            ybf.y_crop_width = width;
            ybf.y_crop_height = height;
            ybf.y_width = aligned_width;
            ybf.y_height = aligned_height;
            ybf.y_stride = y_stride;

            ybf.uv_crop_width = (width + 1) / 2;
            ybf.uv_crop_height = (height + 1) / 2;
            ybf.uv_width = uv_width;
            ybf.uv_height = uv_height;
            ybf.uv_stride = uv_stride;

            ybf.alpha_width = 0;
            ybf.alpha_height = 0;
            ybf.alpha_stride = 0;

            ybf.border = border;
            ybf.frame_size = frame_size;

            ybf.y_buffer = ybf.buffer_alloc + (border * y_stride) + border;
            ybf.u_buffer =
                ybf.buffer_alloc + yplane_size + (border / 2 * uv_stride) + border / 2;
            ybf.v_buffer = ybf.buffer_alloc + yplane_size + uvplane_size +
                            (border / 2 * uv_stride) + border / 2;
            ybf.alpha_buffer = null;

            ybf.corrupted = 0; /* assume not currupted by errors */
            return 0;
        }

        public unsafe static int vp8_yv12_de_alloc_frame_buffer(ref YV12_BUFFER_CONFIG ybf)
        {
            //if (ybf)
            //{
            // If libvpx is using frame buffer callbacks then buffer_alloc_sz must
            // not be set.
            if (ybf.buffer_alloc != null && ybf.buffer_alloc_sz > 0)
            {
                vpx_mem.vpx_free(ybf.buffer_alloc);
                ybf.buffer_alloc = null;
                ybf.buffer_alloc_sz = 0;
            }

            /* buffer_alloc isn't accessed by most functions.  Rather y_buffer,
              u_buffer and v_buffer point to buffer_alloc and are used.  Clear out
              all of this so that a freed pointer isn't inadvertently used */
            // memset(ybf, 0, sizeof(YV12_BUFFER_CONFIG));
            //}
            //else
            //{
            //    return -1;
            //}

            return 0;
        }
    }
}
