//-----------------------------------------------------------------------------
// Filename: entropy.cs
//
// Description: Port of:
//  - entropy.h
//  - entropy.c
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
    public static class entropy
    {
        public const int MAX_ENTROPY_TOKENS = 12;
        public const int ENTROPY_NODES = 11;

        public static readonly byte[] vp8_norm = {
            0, 7, 6, 6, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        /* Coefficients are predicted via a 3-dimensional probability table. */
        /* Outside dimension.  0 = Y no DC, 1 = Y2, 2 = UV, 3 = Y with DC */
        public const int BLOCK_TYPES = 4;

        /* Middle dimension is a coarsening of the coefficient's
        position within the 4x4 DCT. */
        public const int COEF_BANDS = 8;

        /*# define DC_TOKEN_CONTEXTS        3*/ /* 00, 0!0, !0!0 */
        public const int PREV_COEF_CONTEXTS = 3;

        //const int vp8_mb_feature_data_bits[MB_LVL_MAX] = { 7, 6 };
        public static readonly int[] vp8_mb_feature_data_bits = { 7, 6 };

        /// <summary>
        /// VP8 raster-to-zigzag coefficient ordering for 4x4 blocks.
        /// Port of libvpx vp8_default_zig_zag1d (vp8/common/entropy.c).
        /// Used by both quantize and tokenize.
        /// </summary>
        public static readonly int[] vp8_default_zig_zag1d = {
            0,  1,  4,  8,
            5,  2,  3,  6,
            9, 12, 13, 10,
            7, 11, 14, 15,
        };

        public static void vp8_default_coef_probs(VP8_COMMON pc)
        {
            //memcpy(pc->fc.coef_probs, default_coef_probs, sizeof(default_coef_probs));
            Array.Copy(default_coef_probs_c.default_coef_probs, pc.fc.coef_probs, default_coef_probs_c.default_coef_probs.Length);
        }

        // ---------------------------------------------------------------
        // Encoder-side token tables (PR 3 of the VP8 encoder series).
        // Ports of constants from libvpx/vp8/common/entropy.{c,h} used by
        // the tokenizer (tokenize.cs) and the token bitstream writer
        // (pack_tokens, in a follow-up PR).
        //
        // Reference: libvpx, MIT/BSD-3-Clause licensed.
        // ---------------------------------------------------------------

        /// <summary>Token enum values matching the libvpx ordering. Used as
        /// indices into vp8_coef_encodings / vp8_extra_bits.</summary>
        public const int ZERO_TOKEN          = 0;
        public const int ONE_TOKEN           = 1;
        public const int TWO_TOKEN           = 2;
        public const int THREE_TOKEN         = 3;
        public const int FOUR_TOKEN          = 4;
        public const int DCT_VAL_CATEGORY1   = 5;
        public const int DCT_VAL_CATEGORY2   = 6;
        public const int DCT_VAL_CATEGORY3   = 7;
        public const int DCT_VAL_CATEGORY4   = 8;
        public const int DCT_VAL_CATEGORY5   = 9;
        public const int DCT_VAL_CATEGORY6   = 10;
        public const int DCT_EOB_TOKEN       = 11;

        /// <summary>
        /// Maps a 4x4 coefficient zigzag index to its 8-band entropy context.
        /// Port of libvpx vp8_coef_bands.
        /// </summary>
        public static readonly byte[] vp8_coef_bands = {
            0, 1, 2, 3, 6, 4, 5, 6,
            6, 6, 6, 6, 6, 6, 6, 7,
        };

        /// <summary>
        /// Maps a token to its "previous token class" — the entropy context
        /// the next coefficient inherits. Port of libvpx vp8_prev_token_class.
        /// </summary>
        public static readonly byte[] vp8_prev_token_class = {
            0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 0,
        };

        /// <summary>
        /// Encoding tree for VP8 coefficient tokens. Port of libvpx
        /// vp8_coef_tree (vp8/common/entropy.c). Negative values are
        /// terminal token values (negated), positive values are next
        /// internal-node offsets.
        /// </summary>
        public static readonly sbyte[] vp8_coef_tree = {
            -DCT_EOB_TOKEN,    2,                          //  0 = EOB
            -ZERO_TOKEN,       4,                          //  1 = ZERO
            -ONE_TOKEN,        6,                          //  2 = ONE
            8,                 12,                         //  3 = LOW_VAL
            -TWO_TOKEN,        10,                         //  4 = TWO
            -THREE_TOKEN,     -FOUR_TOKEN,                 //  5 = THREE
            14,                16,                         //  6 = HIGH_LOW
            -DCT_VAL_CATEGORY1,-DCT_VAL_CATEGORY2,         //  7 = CAT_ONE
            18,                20,                         //  8 = CAT_THREEFOUR
            -DCT_VAL_CATEGORY3,-DCT_VAL_CATEGORY4,         //  9 = CAT_THREE
            -DCT_VAL_CATEGORY5,-DCT_VAL_CATEGORY6,         // 10 = CAT_FIVE
        };

        /// <summary>(value, length) pair: the bit-string the encoder emits for each token.</summary>
        public struct vp8_token { public int value; public int Len; }

        /// <summary>
        /// Per-token encoding: bits to write and how many of them. Port of
        /// libvpx vp8_coef_encodings.
        /// </summary>
        public static readonly vp8_token[] vp8_coef_encodings = {
            new vp8_token { value =   2, Len = 2 }, // ZERO
            new vp8_token { value =   6, Len = 3 }, // ONE
            new vp8_token { value =  28, Len = 5 }, // TWO
            new vp8_token { value =  58, Len = 6 }, // THREE
            new vp8_token { value =  59, Len = 6 }, // FOUR
            new vp8_token { value =  60, Len = 6 }, // CAT1
            new vp8_token { value =  61, Len = 6 }, // CAT2
            new vp8_token { value = 124, Len = 7 }, // CAT3
            new vp8_token { value = 125, Len = 7 }, // CAT4
            new vp8_token { value = 126, Len = 7 }, // CAT5
            new vp8_token { value = 127, Len = 7 }, // CAT6
            new vp8_token { value =   0, Len = 1 }, // EOB
        };

        // Trees and probabilities for the "extra bits" of categorised tokens.
        // Port of cat1..cat6 / Pcat1..Pcat6 in libvpx vp8/common/entropy.c.
        public static readonly sbyte[] vp8_cat1_tree  = { 0, 0 };
        public static readonly sbyte[] vp8_cat2_tree  = { 2, 2, 0, 0 };
        public static readonly sbyte[] vp8_cat3_tree  = { 2, 2, 4, 4, 0, 0 };
        public static readonly sbyte[] vp8_cat4_tree  = { 2, 2, 4, 4, 6, 6, 0, 0 };
        public static readonly sbyte[] vp8_cat5_tree  = { 2, 2, 4, 4, 6, 6, 8, 8, 0, 0 };
        public static readonly sbyte[] vp8_cat6_tree  = { 2,2,4,4,6,6,8,8,10,10,12,12,14,14,16,16,18,18,20,20,0,0 };

        public static readonly byte[] vp8_Pcat1 = { 159 };
        public static readonly byte[] vp8_Pcat2 = { 165, 145 };
        public static readonly byte[] vp8_Pcat3 = { 173, 148, 140 };
        public static readonly byte[] vp8_Pcat4 = { 176, 155, 140, 135 };
        public static readonly byte[] vp8_Pcat5 = { 180, 157, 141, 134, 130 };
        public static readonly byte[] vp8_Pcat6 = { 254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129 };

        /// <summary>
        /// Per-token "extra bits" descriptor. tree+prob describe the
        /// length-variable extra-bit code; Len is the number of magnitude
        /// bits; base_val is the smallest absolute coefficient value the
        /// token can represent. Port of libvpx vp8_extra_bit_struct.
        /// </summary>
        public struct vp8_extra_bit_struct
        {
            public sbyte[] tree;
            public byte[] prob;
            public int Len;
            public int base_val;
        }

        /// <summary>
        /// Per-token extra-bits info, indexed by token value.
        /// Port of libvpx vp8_extra_bits[12].
        /// </summary>
        public static readonly vp8_extra_bit_struct[] vp8_extra_bits = {
            new vp8_extra_bit_struct { tree = null,         prob = null,    Len = 0,  base_val = 0  },  // ZERO
            new vp8_extra_bit_struct { tree = null,         prob = null,    Len = 0,  base_val = 1  },  // ONE
            new vp8_extra_bit_struct { tree = null,         prob = null,    Len = 0,  base_val = 2  },  // TWO
            new vp8_extra_bit_struct { tree = null,         prob = null,    Len = 0,  base_val = 3  },  // THREE
            new vp8_extra_bit_struct { tree = null,         prob = null,    Len = 0,  base_val = 4  },  // FOUR
            new vp8_extra_bit_struct { tree = vp8_cat1_tree, prob = vp8_Pcat1, Len = 1,  base_val = 5  },
            new vp8_extra_bit_struct { tree = vp8_cat2_tree, prob = vp8_Pcat2, Len = 2,  base_val = 7  },
            new vp8_extra_bit_struct { tree = vp8_cat3_tree, prob = vp8_Pcat3, Len = 3,  base_val = 11 },
            new vp8_extra_bit_struct { tree = vp8_cat4_tree, prob = vp8_Pcat4, Len = 4,  base_val = 19 },
            new vp8_extra_bit_struct { tree = vp8_cat5_tree, prob = vp8_Pcat5, Len = 5,  base_val = 35 },
            new vp8_extra_bit_struct { tree = vp8_cat6_tree, prob = vp8_Pcat6, Len = 11, base_val = 67 },
            new vp8_extra_bit_struct { tree = null,         prob = null,    Len = 0,  base_val = 0  },  // EOB
        };
    }
}
