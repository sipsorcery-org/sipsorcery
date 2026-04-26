//-----------------------------------------------------------------------------
// Filename: tokenize.cs
//
// Description: Coefficient tokenizer for the VP8 encoder. Port of the
// per-block portion of libvpx vp8/encoder/tokenize.c — specifically the
// "scan a block of zigzag-ordered quantized coefficients and emit the
// corresponding TOKENEXTRA stream" loop, packaged as a self-contained
// vp8_tokenize_block that can be tested in isolation without first having
// to port the encoder-side BLOCK / MACROBLOCK / MACROBLOCKD structures.
//
// The macroblock-level orchestration (vp8_tokenize_mb, the EOB stuffing
// helpers, and the coef_counts cost table updates used for RD) is NOT
// included here; that follows in PR 5 alongside encodeframe.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Ported from libvpx vp8/encoder/tokenize.c.
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

using System.Collections.Generic;

using vp8_prob = System.Byte;

namespace Vpx.Net
{
    /// <summary>
    /// One token emitted by the tokenizer. Mirrors libvpx's TOKENEXTRA but
    /// holds the 11-element probability row directly (as a byte[]) instead
    /// of the C-style pointer-into-a-4D-table.
    /// </summary>
    public struct TOKENEXTRA
    {
        /// <summary>11-element probability row indexed by tree node (0..10).</summary>
        public vp8_prob[] context_tree;

        /// <summary>Magnitude/sign extra bits for category tokens; 0 for ZERO..FOUR.</summary>
        public short Extra;

        /// <summary>Token value 0..11 (ZERO..DCT_EOB_TOKEN).</summary>
        public byte Token;

        /// <summary>1 if the EOB tree-node bit is implicit (skip), 0 otherwise.</summary>
        public byte skip_eob_node;
    }

    public static unsafe class tokenize
    {
        /// <summary>
        /// Tokenize a single 4x4 block of zigzag-ordered quantized
        /// coefficients into a stream of TOKENEXTRA records.
        ///
        /// Output stream contract: one TOKENEXTRA per non-zero coefficient
        /// from <paramref name="firstCoeffIndex"/> up to (but not including)
        /// <paramref name="eob"/>, then optionally one DCT_EOB_TOKEN if the
        /// block isn't fully populated.
        /// </summary>
        /// <param name="qcoeff">16 quantized coefficients in raster order.</param>
        /// <param name="firstCoeffIndex">First zigzag index to process. 1
        /// for block type 0 (Y AC, the DC having gone through Y2); 0 for
        /// every other block type. Mirrors the "c = type ? 0 : 1" line in
        /// libvpx tokenize1st_order_b.</param>
        /// <param name="eob">End-of-block index in zigzag order. Must be in
        /// [firstCoeffIndex, 16]. 0 means "block is fully zero from the
        /// first coefficient onward" — only an EOB token will be emitted.</param>
        /// <param name="blockType">VP8 block type (0..3) used to index
        /// coefProbs and choose the band table behaviour.</param>
        /// <param name="initialContext">Previous-token-class context for
        /// the first coefficient — usually computed from the left/above
        /// macroblocks' contexts (VP8_COMBINEENTROPYCONTEXTS in libvpx).</param>
        /// <param name="coefProbs">[type, band, ctx, node] probability
        /// table; typically default_coef_probs_c.default_coef_probs at the
        /// start of a keyframe.</param>
        /// <param name="output">List to append TOKENEXTRAs to.</param>
        /// <returns>true if the block has any non-zero coefficient (the
        /// caller updates entropy contexts on this); false if the block
        /// was entirely zero from <paramref name="firstCoeffIndex"/>.</returns>
        public static bool vp8_tokenize_block(
            short[] qcoeff,
            int firstCoeffIndex,
            int eob,
            int blockType,
            int initialContext,
            vp8_prob[,,,] coefProbs,
            List<TOKENEXTRA> output)
        {
            int c = firstCoeffIndex;
            int pt = initialContext;

            // Special case: block is entirely zero from c onward — just
            // emit a single EOB token and tell the caller "no nonzero".
            if (c >= eob)
            {
                int band = entropy.vp8_coef_bands[c];
                output.Add(new TOKENEXTRA
                {
                    Token = (byte)entropy.DCT_EOB_TOKEN,
                    Extra = 0,
                    context_tree = SliceProbRow(coefProbs, blockType, band, pt),
                    skip_eob_node = 0,
                });
                return false;
            }

            // First non-zero coefficient. Note: the band for the very first
            // coefficient is taken from c directly (NOT zigzagged — already
            // an entropy band index for that position) — and the rc is
            // c itself, since libvpx uses qcoeff_ptr[c] here without
            // zigzagging the first coefficient lookup. (See tokenize1st_order_b.)
            {
                int v = qcoeff[c];
                var tv = dct_value_tokens.Lookup(v);
                int firstBand = entropy.vp8_coef_bands[c];

                output.Add(new TOKENEXTRA
                {
                    Token = (byte)tv.Token,
                    Extra = tv.Extra,
                    context_tree = SliceProbRow(coefProbs, blockType, firstBand, pt),
                    skip_eob_node = 0,
                });

                pt = entropy.vp8_prev_token_class[tv.Token];
                c++;
            }

            // Subsequent coefficients are read via the zigzag scan order.
            for (; c < eob; ++c)
            {
                int rc = entropy.vp8_default_zig_zag1d[c];
                int band = entropy.vp8_coef_bands[c];
                int v = qcoeff[rc];
                var tv = dct_value_tokens.Lookup(v);

                output.Add(new TOKENEXTRA
                {
                    Token = (byte)tv.Token,
                    Extra = tv.Extra,
                    context_tree = SliceProbRow(coefProbs, blockType, band, pt),
                    // skip_eob_node is set when the previous token's class
                    // was 0 (i.e. ZERO_TOKEN): the EOB node is implicitly
                    // not present and the encoder skips writing it.
                    skip_eob_node = (byte)(pt == 0 ? 1 : 0),
                });

                pt = entropy.vp8_prev_token_class[tv.Token];
            }

            // Trailing EOB — only emitted if eob < 16 (i.e. there's room
            // for a terminator). When eob == 16 the block was fully
            // populated and no EOB is needed.
            if (c < 16)
            {
                int band = entropy.vp8_coef_bands[c];
                output.Add(new TOKENEXTRA
                {
                    Token = (byte)entropy.DCT_EOB_TOKEN,
                    Extra = 0,
                    context_tree = SliceProbRow(coefProbs, blockType, band, pt),
                    skip_eob_node = 0,
                });
            }

            return true;
        }

        // 4 block types x 8 coef bands x 3 prev-token contexts = 96 unique
        // probability rows in the default_coef_probs table. The encoder
        // hot path calls SliceProbRow up to ~75 times per MB (= ~90,000
        // calls per 640x480 frame). Each fresh allocation is ~32 bytes
        // (16 byte header + 11 byte payload aligned to 8); allocating
        // 90K of those per frame is ~3 MB/sec of GC pressure on a
        // 30 fps stream, which is exactly the kind of per-frame
        // allocation the encoder needs to avoid.
        //
        // Caching strategy: lazily build a flat vp8_prob[][] keyed by
        // (type * 24 + band * 3 + ctx) the first time we see a given
        // coefProbs reference, and reuse on every subsequent call. The
        // common case (and only case in the current encoder) is the
        // singleton default_coef_probs.default_coef_probs table.
        private static vp8_prob[,,,] s_cachedTable;
        private static vp8_prob[][]  s_cachedRows;
        private static readonly object s_cacheLock = new object();

        private static vp8_prob[] SliceProbRow(vp8_prob[,,,] coefProbs, int type, int band, int ctx)
        {
            // Fast path: same table reference as the previous cache fill.
            if (object.ReferenceEquals(coefProbs, s_cachedTable))
            {
                return s_cachedRows[type * 24 + band * 3 + ctx];
            }

            // Cache miss: build the cache for this table, then return.
            return BuildCacheAndSlice(coefProbs, type, band, ctx);
        }

        private static vp8_prob[] BuildCacheAndSlice(vp8_prob[,,,] coefProbs, int type, int band, int ctx)
        {
            lock (s_cacheLock)
            {
                if (!object.ReferenceEquals(coefProbs, s_cachedTable))
                {
                    int blockTypes = coefProbs.GetLength(0);
                    int coefBands  = coefProbs.GetLength(1);
                    int prevCtx    = coefProbs.GetLength(2);
                    int nodes      = coefProbs.GetLength(3);
                    var rows = new vp8_prob[blockTypes * coefBands * prevCtx][];
                    for (int t = 0; t < blockTypes; t++)
                        for (int b = 0; b < coefBands; b++)
                            for (int c = 0; c < prevCtx; c++)
                            {
                                var row = new vp8_prob[nodes];
                                for (int i = 0; i < nodes; i++)
                                    row[i] = coefProbs[t, b, c, i];
                                rows[t * (coefBands * prevCtx) + b * prevCtx + c] = row;
                            }
                    // Publish atomically so concurrent readers see a fully
                    // initialised cache.
                    System.Threading.Interlocked.Exchange(ref s_cachedRows,  rows);
                    System.Threading.Interlocked.Exchange(ref s_cachedTable, coefProbs);
                }
                return s_cachedRows[type * (coefProbs.GetLength(1) * coefProbs.GetLength(2))
                                  + band * coefProbs.GetLength(2)
                                  + ctx];
            }
        }
    }
}
