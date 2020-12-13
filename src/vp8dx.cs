//-----------------------------------------------------------------------------
// Filename: vp8dx.cs
//
// Description: Port of:
//  - vp8dx.h
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

namespace Vpx.Net
{
    /// <summary>
    /// Control functions.
    /// The set of macros define the control functions of VP8 interface.
    /// </summary>
    public enum vp8_com_control_id
    {
        /*!\brief pass in an external frame into decoder to be used as reference frame
         */
        VP8_SET_REFERENCE = 1,
        VP8_COPY_REFERENCE = 2, /**< get a copy of reference frame from the decoder */
        VP8_SET_POSTPROC = 3,   /**< set the decoder's post processing settings  */

        /* TODO(jkoleszar): The encoder incorrectly reuses some of these values (5+)
         * for its control ids. These should be migrated to something like the
         * VP8_DECODER_CTRL_ID_START range next time we're ready to break the ABI.
         */
        VP9_GET_REFERENCE = 128, /**< get a pointer to a reference frame */
        VP8_COMMON_CTRL_ID_MAX,
        VP8_DECODER_CTRL_ID_START = 256
    };

    /// <summary>
    /// VP8 decoder control functions.
    /// This set of macros define the control functions available for the VP8
    /// decoder interface.
    /// </summary>
    public enum vp8_dec_control_id
    {
        /** control function to get info on which reference frames were updated
         *  by the last decode
         */
        VP8D_GET_LAST_REF_UPDATES = vp8_com_control_id.VP8_DECODER_CTRL_ID_START,

        /** check if the indicated frame is corrupted */
        VP8D_GET_FRAME_CORRUPTED,

        /** control function to get info on which reference frames were used
         *  by the last decode
         */
        VP8D_GET_LAST_REF_USED,

        /** decryption function to decrypt encoded buffer data immediately
         * before decoding. Takes a vpx_decrypt_init, which contains
         * a callback function and opaque context pointer.
         */
        VPXD_SET_DECRYPTOR,
        VP8D_SET_DECRYPTOR = VPXD_SET_DECRYPTOR,

        /** control function to get the dimensions that the current frame is decoded
         * at. This may be different to the intended display size for the frame as
         * specified in the wrapper or frame header (see VP9D_GET_DISPLAY_SIZE). */
        VP9D_GET_FRAME_SIZE,

        /** control function to get the current frame's intended display dimensions
         * (as specified in the wrapper or frame header). This may be different to
         * the decoded dimensions of this frame (see VP9D_GET_FRAME_SIZE). */
        VP9D_GET_DISPLAY_SIZE,

        /** control function to get the bit depth of the stream. */
        VP9D_GET_BIT_DEPTH,

        /** control function to set the byte alignment of the planes in the reference
         * buffers. Valid values are power of 2, from 32 to 1024. A value of 0 sets
         * legacy alignment. I.e. Y plane is aligned to 32 bytes, U plane directly
         * follows Y plane, and V plane directly follows U plane. Default value is 0.
         */
        VP9_SET_BYTE_ALIGNMENT,

        /** control function to invert the decoding order to from right to left. The
         * function is used in a test to confirm the decoding independence of tile
         * columns. The function may be used in application where this order
         * of decoding is desired.
         *
         * TODO(yaowu): Rework the unit test that uses this control, and in a future
         *              release, this test-only control shall be removed.
         */
        VP9_INVERT_TILE_DECODE_ORDER,

        /** control function to set the skip loop filter flag. Valid values are
         * integers. The decoder will skip the loop filter when its value is set to
         * nonzero. If the loop filter is skipped the decoder may accumulate decode
         * artifacts. The default value is 0.
         */
        VP9_SET_SKIP_LOOP_FILTER,

        /** control function to decode SVC stream up to the x spatial layers,
         * where x is passed in through the control, and is 0 for base layer.
         */
        VP9_DECODE_SVC_SPATIAL_LAYER,

        /*!\brief Codec control function to get last decoded frame quantizer.
         *
         * Return value uses internal quantizer scale defined by the codec.
         *
         * Supported in codecs: VP8, VP9
         */
        VPXD_GET_LAST_QUANTIZER,

        /*!\brief Codec control function to set row level multi-threading.
         *
         * 0 : off, 1 : on
         *
         * Supported in codecs: VP9
         */
        VP9D_SET_ROW_MT,

        /*!\brief Codec control function to set loopfilter optimization.
         *
         * 0 : off, Loop filter is done after all tiles have been decoded
         * 1 : on, Loop filter is done immediately after decode without
         *     waiting for all threads to sync.
         *
         * Supported in codecs: VP9
         */
        VP9D_SET_LOOP_FILTER_OPT,

        VP8_DECODER_CTRL_ID_MAX
    }

    /// <summary>
    /// Decrypt n bytes of data from input -> output, using the decrypt_state
    /// assed in VPXD_SET_DECRYPTOR.
    /// </summary>
    /// <remarks>
    /// Original definition:
    /// typedef void(*vpx_decrypt_cb)(void* decrypt_state, const unsigned char *input,
    ///                          unsigned char *output, int count);
    /// </remarks>
    public unsafe delegate void vpx_decrypt_cb(void* decrypt_state, byte* input, byte*output, int count);
}
