//-----------------------------------------------------------------------------
// Filename: dboolhuff.cs
//
// Description: Port of:
//  - dboolhuff.h
//  - dboolhuff.c
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

using System.Runtime.CompilerServices;

using VP8_BD_VALUE = System.UInt64;

namespace Vpx.Net
{
    public unsafe class BOOL_DECODER
    {
        public byte* user_buffer_end;
        public byte* user_buffer;
        public VP8_BD_VALUE value;
        public int count;
        public uint range;
        //vpx_decrypt_cb decrypt_cb;
        //void* decrypt_state;
    }

    /// <summary>
    /// Boolean entropy decoder.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc6386#section-7 for a description of how this
    /// decoder works.
    /// </remarks>
    public unsafe static class dboolhuff
    {
        public const int CHAR_BIT = 8;  // Taken from limits.h.
        public const int VP8_BD_VALUE_SIZE = ((int)sizeof(VP8_BD_VALUE) * CHAR_BIT);

        /// <summary>
        /// This is meant to be a large, positive constant that can still be efficiently
        /// loaded as an immediate (on platforms like ARM, for example).
        /// Even relatively modest values like 100 would work fine.
        /// </summary>
        public const int VP8_LOTS_OF_BITS = 0x40000000;

        public unsafe static int vp8dx_start_decode(ref BOOL_DECODER br, in byte[] source, uint source_sz)
        {
            fixed (byte* pSrc = source)
            {
                return vp8dx_start_decode(ref br, pSrc, source_sz, null, null);
            }
        }

        public static int vp8dx_start_decode(ref BOOL_DECODER br, in byte* source,
                       uint source_sz, vpx_decrypt_cb decrypt_cb,
                       void* decrypt_state)
        {
            if (source_sz == 0 && source == null) return 1;

            // To simplify calling code this fuction can be called with |source| == null
            // and |source_sz| == 0. This and vp8dx_bool_decoder_fill() are essentially
            // no-ops in this case.
            // Work around a ubsan warning with a ternary to avoid adding 0 to null.
            br.user_buffer_end = source != null ? source + source_sz : source;
            br.user_buffer = source;
            br.value = 0;
            br.count = -8;
            br.range = 255;
            //br.decrypt_cb = decrypt_cb;
            //br.decrypt_state = decrypt_state;

            /* Populate the buffer */
            vp8dx_bool_decoder_fill(ref br);

            return 0;
        }

        public unsafe static int vp8dx_decode_bool(ref BOOL_DECODER br, int probability)
        {
            int bit = 0;
            VP8_BD_VALUE value;
            uint split;
            VP8_BD_VALUE bigsplit;
            int count;
            uint range;

            split = (uint)(1 + (((br.range - 1) * probability) >> 8));

            if (br.count < 0) vp8dx_bool_decoder_fill(ref br);

            value = br.value;
            count = br.count;

            bigsplit = (VP8_BD_VALUE)split << (VP8_BD_VALUE_SIZE - 8);

            range = split;

            if (value >= bigsplit)
            {
                range = br.range - split;
                value = value - bigsplit;
                bit = 1;
            }

            {
                byte shift = entropy.vp8_norm[(byte)range];
                range <<= shift;
                value <<= shift;
                count -= shift;
            }
            br.value = value;
            br.count = count;
            br.range = range;

            return bit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int vp8_decode_value(ref BOOL_DECODER br, int bits)
        {
            int z = 0;
            int bit;

            for (bit = bits - 1; bit >= 0; bit--)
            {
                z |= (vp8dx_decode_bool(ref br, 0x80) << bit);
            }

            return z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int vp8dx_bool_error(ref BOOL_DECODER br)
        {
            /* Check if we have reached the end of the buffer.
             *
             * Variable 'count' stores the number of bits in the 'value' buffer, minus
             * 8. The top byte is part of the algorithm, and the remainder is buffered
             * to be shifted into it. So if count == 8, the top 16 bits of 'value' are
             * occupied, 8 for the algorithm and 8 in the buffer.
             *
             * When reading a byte from the user's buffer, count is filled with 8 and
             * one byte is filled into the value buffer. When we reach the end of the
             * data, count is additionally filled with VP8_LOTS_OF_BITS. So when
             * count == VP8_LOTS_OF_BITS - 1, the user's data has been exhausted.
             */
            if ((br.count > VP8_BD_VALUE_SIZE) && (br.count < VP8_LOTS_OF_BITS))
            {
                /* We have tried to decode bits after the end of
                 * stream was encountered.
                 */
                return 1;
            }

            /* No error. */
            return 0;
        }

        public unsafe static void vp8dx_bool_decoder_fill(ref BOOL_DECODER br)
        {
            byte* bufptr = br.user_buffer;
            VP8_BD_VALUE value = br.value;
            int count = br.count;
            int shift = VP8_BD_VALUE_SIZE - CHAR_BIT - (count + CHAR_BIT);
            ulong bytes_left = (ulong)(br.user_buffer_end - bufptr);
            ulong bits_left = bytes_left * CHAR_BIT;
            int x = shift + CHAR_BIT - (int)bits_left;
            int loop_end = 0;
            //byte[] decrypted = new byte[sizeof(VP8_BD_VALUE) + 1];

            //if (br->decrypt_cb)
            //{
            //    size_t n = VPXMIN(sizeof(decrypted), bytes_left);
            //    br->decrypt_cb(br->decrypt_state, bufptr, decrypted, (int)n);
            //    bufptr = decrypted;
            //}

            if (x >= 0)
            {
                count += VP8_LOTS_OF_BITS;
                loop_end = x;
            }

            if (x < 0 || bits_left > 0)
            {
                while (shift >= loop_end)
                {
                    count += CHAR_BIT;
                    value |= (VP8_BD_VALUE)(*bufptr) << shift;
                    ++bufptr;
                    ++br.user_buffer;
                    shift -= CHAR_BIT;
                }
            }

            br.value = value;
            br.count = count;
        }
    }
}
