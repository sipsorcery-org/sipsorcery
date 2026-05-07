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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using vp8_prob = System.Byte;

namespace Vpx.Net
{
    /// <summary>
    /// One token emitted by the tokenizer. Mirrors libvpx's TOKENEXTRA:
    /// coefficient tree probabilities are referenced by row index into the
    /// flat cache built for the active <c>coef_probs</c> table (see
    /// <see cref="tokenize.EnsureCoefProbCache"/> / pack via
    /// <see cref="tokenize.GetCoefProbRowForPack"/>).
    /// </summary>
    public struct TOKENEXTRA
    {
        /// <summary>Token value 0..11 (ZERO..DCT_EOB_TOKEN).</summary>
        public byte Token;

        /// <summary>1 if the EOB tree-node bit is implicit (skip), 0 otherwise.</summary>
        public byte skip_eob_node;

        /// <summary>Row in the flattened coef-prob table: type * (bands*ctx) + band * ctx + prev.</summary>
        public byte CoefProbRowIndex;

        /// <summary>Magnitude/sign extra bits for category tokens; 0 for ZERO..FOUR.</summary>
        public short Extra;
    }

    /// <summary>
    /// Fixed-capacity token stream for one 4×4 block. Avoids per-MB <see cref="List{T}"/>
    /// growth on the encoder hot path; capacity covers worst-case token count for one block.
    /// </summary>
    public sealed class TokenStreamBuffer : IEnumerable<TOKENEXTRA>
    {
        public const int DefaultCapacity = 24;
        private readonly TOKENEXTRA[] _items;

        public TokenStreamBuffer(int capacity = DefaultCapacity) =>
            _items = new TOKENEXTRA[capacity];

        public int Count { get; private set; }

        public TOKENEXTRA this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _items[index];
            }
        }

        public void Clear() => Count = 0;

        public IEnumerator<TOKENEXTRA> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return _items[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(TOKENEXTRA t)
        {
            if (Count >= _items.Length)
                throw new InvalidOperationException("Token buffer capacity exceeded.");
            _items[Count++] = t;
        }

        public ReadOnlySpan<TOKENEXTRA> AsSpan() => _items.AsSpan(0, Count);
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
        /// <param name="output">Token stream buffer for this block (cleared by caller via <see cref="TokenStreamBuffer.Clear"/>).</param>
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
            TokenStreamBuffer output)
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
                    CoefProbRowIndex = CoefProbRowIndex(coefProbs, blockType, band, pt),
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
                    CoefProbRowIndex = CoefProbRowIndex(coefProbs, blockType, firstBand, pt),
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
                    CoefProbRowIndex = CoefProbRowIndex(coefProbs, blockType, band, pt),
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
                    CoefProbRowIndex = CoefProbRowIndex(coefProbs, blockType, band, pt),
                    skip_eob_node = 0,
                });
            }

            return true;
        }

        // 4 block types x 8 coef bands x 3 prev-token contexts = 96 unique
        // probability rows in the default_coef_probs table. Packing uses
        // a dense row index on each TOKENEXTRA instead of storing a byte[]
        // reference (smaller token records; same cache).
        // One flat vp8_prob[][] per coef table instance (reference-keyed) so
        // parallel encodes/tests with different coefProbs cannot clobber each other.
        private static readonly ConditionalWeakTable<vp8_prob[,,,], vp8_prob[][]> s_coefRowsByTable =
            new ConditionalWeakTable<vp8_prob[,,,], vp8_prob[][]>();

        // Cached delegate so the per-token-pack <see cref="ConditionalWeakTable.GetValue"/>
        // call doesn't allocate a fresh CreateValueCallback every invocation. Profiler
        // showed 150k+ method-group-to-delegate allocations per 640x480 keyframe before
        // this change (~8 MB / frame).
        private static readonly ConditionalWeakTable<vp8_prob[,,,], vp8_prob[][]>.CreateValueCallback s_buildCoefRows = BuildCoefRows;

        // Per-thread last table + rows: avoids a ConditionalWeakTable GetValue on repeat
        // packing with the same coefProbs reference (typical: default_coef_probs every
        // token). Must not be process-wide statics — two threads packing different tables
        // could otherwise race and return the wrong row array.
        [System.ThreadStatic] private static vp8_prob[,,,] t_lastCoefKey;
        [System.ThreadStatic] private static vp8_prob[][] t_lastCoefRows;

        /// <summary>Forces row materialisation for <paramref name="coefProbs"/> (e.g. benchmarks).</summary>
        internal static void EnsureCoefProbCache(vp8_prob[,,,] coefProbs) => _ = RowsFor(coefProbs);

        private static vp8_prob[][] RowsFor(vp8_prob[,,,] coefProbs)
        {
            if (coefProbs == null) throw new ArgumentNullException(nameof(coefProbs));
            if (ReferenceEquals(coefProbs, t_lastCoefKey))
                return t_lastCoefRows;

            var rows = s_coefRowsByTable.GetValue(coefProbs, s_buildCoefRows);
            t_lastCoefKey = coefProbs;
            t_lastCoefRows = rows;
            return rows;
        }

        private static vp8_prob[][] BuildCoefRows(vp8_prob[,,,] coef)
        {
            int blockTypes = coef.GetLength(0);
            int coefBands  = coef.GetLength(1);
            int prevCtx    = coef.GetLength(2);
            int nodes      = coef.GetLength(3);
            var rows = new vp8_prob[blockTypes * coefBands * prevCtx][];
            for (int t = 0; t < blockTypes; t++)
                for (int b = 0; b < coefBands; b++)
                    for (int c = 0; c < prevCtx; c++)
                    {
                        var row = new vp8_prob[nodes];
                        for (int i = 0; i < nodes; i++)
                            row[i] = coef[t, b, c, i];
                        rows[t * (coefBands * prevCtx) + b * prevCtx + c] = row;
                    }
            return rows;
        }

        /// <summary>Resolves a coefficient tree probability row for token packing or decode tests.</summary>
        internal static vp8_prob[] GetCoefProbRowForPack(vp8_prob[,,,] coefProbs, byte rowIndex)
        {
            var rows = RowsFor(coefProbs);
            if (rowIndex >= rows.Length)
                throw new ArgumentOutOfRangeException(nameof(rowIndex), "Coef probability row index out of range.");
            return rows[rowIndex];
        }

        private static byte CoefProbRowIndex(vp8_prob[,,,] coefProbs, int type, int band, int ctx)
        {
            _ = RowsFor(coefProbs);
            int w = coefProbs.GetLength(2);
            int idx = type * (coefProbs.GetLength(1) * w) + band * w + ctx;
            return (byte)idx;
        }
    }
}
