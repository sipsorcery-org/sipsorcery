//-----------------------------------------------------------------------------
// Filename: treereader.cs
//
// Description: Port of:
//  - treereader.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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

using vp8_prob = System.Byte;
using vp8_reader = Vpx.Net.BOOL_DECODER;
using vp8_tree_index = System.SByte;
using vp8_tree = System.SByte;

namespace Vpx.Net
{
    /// <summary>
    /// Intent of tree data structure is to make decoding trivial.
    /// 
    /// Trees map alphabets into huffman-like codes suitable for an arithmetic
    /// bit coder.Timothy S Murphy  11 October 2004
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc6386#section-8.1 for an explanation.
    /// </remarks>
    public static class treereader
    {
        public unsafe delegate int vp8_read_delegate(ref BOOL_DECODER br, int probability);
        public static vp8_read_delegate vp8_read = dboolhuff.vp8dx_decode_bool;

        /* Intent of tree data structure is to make decoding trivial. */

        public unsafe static int vp8_treed_read(
           ref vp8_reader r, /* !!! must return a 0 or 1 !!! */
           vp8_tree[] t, 
           in vp8_prob* p)
        {
            vp8_tree_index i = 0;

            while ((i = t[i + vp8_read(ref r, p[i >> 1])]) > 0)
            {
            }

            return -i;
        }

        public unsafe static int vp8_treed_read(
          ref vp8_reader r, /* !!! must return a 0 or 1 !!! */
          vp8_tree[] t,
          in vp8_prob[] p)
        {
            vp8_tree_index i = 0;

            while ((i = t[i + vp8_read(ref r, p[i >> 1])]) > 0)
            {
            }

            return -i;
        }

        public unsafe static int vp8_read_bit(ref vp8_reader r)
            => vp8_read(ref r, (vp8_prob)128);

        public static int vp8_read_literal(ref vp8_reader r, int bits)
            => dboolhuff.vp8_decode_value(ref r, bits);
    }
}
