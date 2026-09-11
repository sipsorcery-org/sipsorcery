//-----------------------------------------------------------------------------
// Filename: dct_value_tokens.cs
//
// Description: Coefficient value -> (Token, Extra) lookup table for the VP8
// encoder. Port of libvpx's dct_value_tokens table — but rather than embed
// the 8KB of static data verbatim, this file ports the fill_value_tokens
// algorithm that libvpx originally used to GENERATE the table, and runs it
// at static-init time. The output is bit-exactly the same as the
// dct_value_tokens.h static data shipped in libvpx.
//
// Indexed via Lookup(coefficientValue) for any coefficient in the closed
// range [-2048, 2047] inclusive (DCT_MAX_VALUE = 2048). This range covers
// every legal VP8 quantized coefficient.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Created.
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
    /// (Token, Extra) pair returned by the dct_value_tokens lookup. Token is
    /// the VP8 coefficient token (one of 0..11), Extra packs the magnitude
    /// "extra" bits and the sign bit (sign in the LSB).
    /// </summary>
    public struct TOKENVALUE
    {
        public short Token;
        public short Extra;
    }

    public static class dct_value_tokens
    {
        public const int DCT_MAX_VALUE = 2048;

        // Backing array sized 2 * DCT_MAX_VALUE; centred so that
        // backing[v + DCT_MAX_VALUE] gives the entry for coefficient v.
        private static readonly TOKENVALUE[] s_table = BuildTable();

        /// <summary>
        /// Returns the (Token, Extra) lookup for coefficient value
        /// <paramref name="v"/>. v must be in [-DCT_MAX_VALUE, DCT_MAX_VALUE - 1].
        /// </summary>
        public static TOKENVALUE Lookup(int v)
        {
            return s_table[v + DCT_MAX_VALUE];
        }

        /// <summary>
        /// Returns the underlying array; index with (v + DCT_MAX_VALUE) for
        /// hot-path access. Mirrors libvpx's vp8_dct_value_tokens_ptr (which
        /// is dct_value_tokens + DCT_MAX_VALUE).
        /// </summary>
        public static TOKENVALUE[] Table => s_table;

        // Bit-exact port of fill_value_tokens() from libvpx/vp8/encoder/tokenize.c.
        // Generates the lookup that maps each coefficient v in
        // [-DCT_MAX_VALUE, DCT_MAX_VALUE - 1] to its (Token, Extra) pair.
        private static TOKENVALUE[] BuildTable()
        {
            var t = new TOKENVALUE[2 * DCT_MAX_VALUE];

            // Walk through every coefficient value, deciding which token
            // category it falls into and packing the magnitude/sign into
            // Extra. The loop visits negative values (sign=1) first, then
            // zero/positive (sign=0).
            int i = -DCT_MAX_VALUE;
            int sign = 1;
            do
            {
                if (i == 0) sign = 0;

                int a = sign != 0 ? -i : i;   // a = abs(coefficient)
                int eb = sign;                // sign goes in the LSB of Extra

                short token;
                if (a > 4)
                {
                    // Find the highest token category whose base_val <= a.
                    int j = 4;
                    while (++j < 11 && entropy.vp8_extra_bits[j].base_val <= a) { }
                    j--;
                    token = (short)j;
                    eb |= (a - entropy.vp8_extra_bits[j].base_val) << 1;
                }
                else
                {
                    // Tokens ZERO..FOUR map directly to their abs value.
                    token = (short)a;
                }

                int idx = i + DCT_MAX_VALUE;
                t[idx].Token = token;
                t[idx].Extra = (short)eb;

            } while (++i < DCT_MAX_VALUE);

            return t;
        }
    }
}
