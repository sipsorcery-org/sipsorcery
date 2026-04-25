//-----------------------------------------------------------------------------
// Filename: quantize.cs
//
// Description: Forward quantization for the VP8 encoder. Port of:
//  - libvpx/vp8/encoder/vp8_quantize.c (vp8_regular_quantize_b_c only)
//
// This is the bit-exact reference quantizer used by libvpx's encoder.
// It writes both the quantized coefficients (qcoeff, used for entropy
// coding) and the dequantized coefficients (dqcoeff, used for the
// reconstruction loop and rate-distortion). Pairs with the decoder-side
// dequantize.cs.
//
// This foundation port exposes a self-contained array-based API
// (vp8_regular_quantize_b_arrays) for direct unit-testing without first
// having to set up the full encoder-side BLOCK/MACROBLOCK structures.
// A wrapper over the encoder BLOCK struct will be added once that struct
// is ported.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Ported from libvpx vp8/encoder/vp8_quantize.c.
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
    public static unsafe class quantize
    {
        /// <summary>
        /// Bit-exact port of libvpx vp8_regular_quantize_b_c.
        ///
        /// Quantizes a 16-coefficient transform block in zigzag order, writing
        /// both qcoeff (entropy-coded values) and dqcoeff (reconstruction
        /// values). Returns the end-of-block index (eob), which is one more
        /// than the last non-zero coefficient position (0 means all-zero block).
        /// </summary>
        /// <param name="coeff">16 input transform coefficients (in raster order).</param>
        /// <param name="zbin">16 zbin thresholds.</param>
        /// <param name="zrun_zbin_boost">16 zero-run zbin boost values.</param>
        /// <param name="round">16 round values.</param>
        /// <param name="quant">16 quant values.</param>
        /// <param name="quant_shift">16 quant_shift values.</param>
        /// <param name="dequant">16 dequant values.</param>
        /// <param name="qcoeff">16 output quantized coefficients (raster order).</param>
        /// <param name="dqcoeff">16 output dequantized coefficients (raster order).</param>
        /// <param name="zbin_extra">Zbin "over-quant" value (0 in keyframe-only path).</param>
        /// <returns>End-of-block index (0..16); 0 means the block is all zero.</returns>
        public static int vp8_regular_quantize_b_arrays(
            short* coeff,
            short* zbin,
            short* zrun_zbin_boost,
            short* round,
            short* quant,
            short* quant_shift,
            short* dequant,
            short* qcoeff,
            short* dqcoeff,
            short zbin_extra)
        {
            int eob = -1;
            int zbin_boost_idx = 0;

            // Zero out outputs (libvpx uses memset(_, 0, 32) — 32 bytes == 16 shorts).
            for (int j = 0; j < 16; j++) { qcoeff[j] = 0; dqcoeff[j] = 0; }

            for (int i = 0; i < 16; ++i)
            {
                int rc = entropy.vp8_default_zig_zag1d[i];
                int z = coeff[rc];

                int zbin_thr = zbin[rc] + zrun_zbin_boost[zbin_boost_idx] + zbin_extra;
                zbin_boost_idx++;

                int sz = (z >> 31);          // sign of z (arithmetic shift: 0 or -1)
                int x = (z ^ sz) - sz;       // x = abs(z)

                if (x >= zbin_thr)
                {
                    x += round[rc];
                    int y = ((((x * quant[rc]) >> 16) + x) * quant_shift[rc]) >> 16;  // quantize
                    x = (y ^ sz) - sz;                                                 // re-sign
                    qcoeff[rc] = (short)x;
                    dqcoeff[rc] = (short)(x * dequant[rc]);

                    if (y != 0)
                    {
                        eob = i;                  // last non-zero coefficient
                        zbin_boost_idx = 0;       // reset zero-run length
                    }
                }
            }

            return eob + 1;
        }
    }
}
