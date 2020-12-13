//-----------------------------------------------------------------------------
// Filename: vpx_frame_buffer.cs
//
// Description: Port of:
//  - vpx_frame_buffer.h
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

namespace Vpx.Net
{
    /// <summary>
    /// get frame buffer callback prototype
    /// 
    /// This callback is invoked by the decoder to retrieve data for the frame
    /// buffer in order for the decode call to complete.The callback must
    /// allocate at least min_size in bytes and assign it to fb->data.The callback
    /// must zero out all the data allocated.Then the callback must set fb->size
    /// to the allocated size.The application does not need to align the allocated
    /// data. The callback is triggered when the decoder needs a frame buffer to
    /// decode a compressed image into. This function may be called more than once
    /// for every call to vpx_codec_decode. The application may set fb->priv to
    /// some data which will be passed back in the vpx_image_t and the release
    /// function call. |fb| is guaranteed to not be NULL.
    /// </summary>
    /// <param name="priv">Callback's private data</param>
    /// <param name="min_size">Size in bytes needed by the buffer</param>
    /// <param name="fb">Pointer to vpx_codec_frame_buffer_t</param>
    /// <returns>On success the callback must return 0. Any failure the callback 
    /// must return a value less than 0.</returns>
    /// <remarks>
    /// Original definition:
    /// typedef int (*vpx_get_frame_buffer_cb_fn_t)(void *priv, size_t min_size,
    /// vpx_codec_frame_buffer_t *fb);
    /// </remarks>
    public unsafe delegate int vpx_get_frame_buffer_cb_fn_t(void* priv, ulong min_size, vpx_codec_frame_buffer_t fb);

    /// <summary>
    /// release frame buffer callback prototype
    /// 
    /// This callback is invoked by the decoder when the frame buffer is not
    /// referenced by any other buffers. |fb| is guaranteed to not be NULL.
    /// </summary>
    /// <param name="priv">Callback's private data</param>
    /// <param name="fb">Pointer to vpx_codec_frame_buffer_t</param>
    /// <returns>On success the callback must return 0. Any failure the callback must return
    /// a value less than 0.</returns>
    /// <remarks>
    /// Original definition:
    /// typedef int (* vpx_release_frame_buffer_cb_fn_t) (void* priv, vpx_codec_frame_buffer_t* fb);
    /// </remarks>
    public unsafe delegate int vpx_release_frame_buffer_cb_fn_t(void* priv, vpx_codec_frame_buffer_t fb);

    /// <summary>
    /// External frame buffer. 
    /// This structure holds allocated frame buffers used by the decoder.
    /// </summary>
    public unsafe struct vpx_codec_frame_buffer_t
    {
        public byte * data; /**< Pointer to the data buffer */
        public ulong size;  /**< Size of data in bytes */
        public void* priv;  /**< Frame's private data */
    }
}
