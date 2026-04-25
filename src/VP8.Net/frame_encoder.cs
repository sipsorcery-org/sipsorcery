//-----------------------------------------------------------------------------
// Filename: frame_encoder.cs
//
// Description: VP8 keyframe frame-level encoder — the orchestrator that
// pulls together the uncompressed header writer (PR 2), per-MB encode
// pipeline (PR 6), and token bitstream writer (PR 4) into a single
// EncodeKeyframe entry point that produces a fully decodable VP8 frame
// from an I420 YUV source.
//
// The bitstream layout follows libvpx's vp8_pack_bitstream for the
// keyframe + ONE_PARTITION (single token partition) path:
//
//   [3 bytes]  Uncompressed frame tag         <- patched at the end with
//                                                first_partition_length.
//   [3 bytes]  Keyframe start code 0x9D 0x01 0x2A
//   [4 bytes]  Width / height with scale prefixes
//   [bc0]      Compressed first partition:
//                color_space, clamp_type
//                segmentation_enabled = 0
//                filter_type, filter_level, sharpness_level
//                mode_ref_lf_delta_enabled = 0
//                log2_nbr_of_dct_partitions = 0
//                base_qindex
//                5x put_delta_q
//                refresh_entropy_probs = 1
//                ★ coef-prob updates  -- 1056 zero bits ("no update")
//                ★ mb_no_skip_coeff = 0
//                ★ for each MB: keyframe Y mode + UV mode tree paths
//   [bc1]      Token partition (ONE_PARTITION):
//                for each MB: Y2, 16 Y, 4 U, 4 V token streams.
//
// All MBs use DC_PRED for both Y and UV. The encoder runs the per-MB
// pipeline in raster order, threading the rightmost-column / bottommost-
// row reconstruction bytes from each finished MB into the next MB's
// "left" / "above" neighbour buffers so that DC prediction matches the
// decoder's reconstruction context exactly.
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

using System;
using System.Collections.Generic;

namespace Vpx.Net
{
    public static unsafe class frame_encoder
    {
        // The keyframe Y mode tree (vp8_kf_ymode_tree, in entropymode.cs)
        // contains 4 internal nodes; the 4 corresponding probabilities live
        // in vp8_kf_ymode_prob = { 145, 156, 163, 128 }.
        // The keyframe UV mode tree has 3 internal nodes; probs are
        // vp8_kf_uv_mode_prob = { 142, 114, 183 }.

        /// <summary>
        /// Encode an I420 source frame (planar Y, U, V) as a VP8 keyframe
        /// using DC_PRED for every macroblock and all features (segmentation,
        /// loopfilter mode/ref deltas, multi-token-partition, etc.) off.
        /// </summary>
        /// <param name="srcY">width * height luma bytes, raster order.</param>
        /// <param name="srcU">(width/2) * (height/2) chroma U.</param>
        /// <param name="srcV">(width/2) * (height/2) chroma V.</param>
        /// <param name="width">Frame width in pixels (multiple of 16).</param>
        /// <param name="height">Frame height in pixels (multiple of 16).</param>
        /// <param name="qIndex">VP8 base quantizer (0 = lossless-ish, 127 = very lossy).</param>
        /// <returns>The encoded VP8 frame bytes.</returns>
        public static byte[] EncodeKeyframe(byte[] srcY, byte[] srcU, byte[] srcV,
            int width, int height, int qIndex)
        {
            if (width <= 0 || width % 16 != 0)
                throw new ArgumentException("width must be a positive multiple of 16", nameof(width));
            if (height <= 0 || height % 16 != 0)
                throw new ArgumentException("height must be a positive multiple of 16", nameof(height));
            if (srcY == null || srcY.Length != width * height)
                throw new ArgumentException("srcY size mismatch");
            int chromaW = width / 2, chromaH = height / 2;
            if (srcU == null || srcU.Length != chromaW * chromaH)
                throw new ArgumentException("srcU size mismatch");
            if (srcV == null || srcV.Length != chromaW * chromaH)
                throw new ArgumentException("srcV size mismatch");

            int mbCols = width / 16;
            int mbRows = height / 16;

            // Build the quantizer for this Q index.
            FrameQuantizer fq = quantizer_init.BuildForQIndex(qIndex);

            var cfg = new KeyframeHeaderConfig
            {
                Width = width, Height = height,
                BaseQindex = qIndex,
            };

            // Allocate a generous output buffer. For the sizes we deal with
            // (small synthetic frames) 64KB is plenty; production callers
            // would size based on the source.
            int bufLen = Math.Max(4096, width * height * 2 + 1024);
            byte[] outBuf = new byte[bufLen];

            // ----- Open the compressed first partition (header through refresh_entropy_probs) -----
            BOOL_CODER bc0 = new BOOL_CODER();
            int partitionStart;
            fixed (byte* p = outBuf)
            {
                partitionStart = bitstream.StartKeyframeHeader(p, outBuf.Length, cfg, ref bc0);
            }

            // ----- Coef-prob updates: 1056 zero bits (no updates) -----
            // 4 block types x 8 bands x 3 contexts x 11 nodes = 1056. For
            // each (type, band, ctx, node) the encoder writes a 1-bit flag
            // saying "is this prob being updated?" and, if 1, an 8-bit
            // replacement. Foundation: write all-zero flags so the decoder
            // keeps default_coef_probs.
            for (int t = 0; t < entropy.BLOCK_TYPES; t++)
                for (int b = 0; b < entropy.COEF_BANDS; b++)
                    for (int c = 0; c < entropy.PREV_COEF_CONTEXTS; c++)
                        for (int n = 0; n < entropy.ENTROPY_NODES; n++)
                            // Probability is the entry in vp8_coef_update_probs;
                            // we always write 0 so the actual prob doesn't
                            // change the boolean coder state much, but we
                            // must use the right prob for the decoder to
                            // recognise the bit.
                            boolhuff.vp8_encode_bool(ref bc0, 0,
                                coefupdateprobs.vp8_coef_update_probs[t, b, c, n]);

            // ----- mb_no_skip_coeff = 0 -----
            // No per-MB skip optimization. The decoder will not look for
            // a per-MB skip flag; every MB's coefficient blocks are decoded.
            bitstream.vp8_write_bit(ref bc0, 0);

            // ----- Per-MB modes loop -----
            // Encode every MB's Y mode and UV mode through their keyframe
            // trees. We use DC_PRED (= 0) everywhere. Cache the per-MB
            // encode results so we can replay the token streams into bc1
            // afterwards.
            var mbResults = new MbEncodeResult[mbRows * mbCols];

            // Reconstruction context buffers — one row of bottom-of-MB
            // samples (the "above" context for the next row) and per-MB
            // right-edge buffers (the "left" context for the next MB in
            // the row).
            byte[] aboveYRow = new byte[width];
            byte[] aboveURow = new byte[chromaW];
            byte[] aboveVRow = new byte[chromaW];
            // First row has no "above"; null indicates "use 128 default".

            for (int mbRow = 0; mbRow < mbRows; mbRow++)
            {
                byte[] leftY = null, leftU = null, leftV = null;

                for (int mbCol = 0; mbCol < mbCols; mbCol++)
                {
                    // Slice the 16x16 + 8x8 + 8x8 source for this MB.
                    byte[] mbY = ExtractPlane(srcY, width,  mbCol * 16, mbRow * 16, 16, 16);
                    byte[] mbU = ExtractPlane(srcU, chromaW, mbCol * 8,  mbRow * 8,  8, 8);
                    byte[] mbV = ExtractPlane(srcV, chromaW, mbCol * 8,  mbRow * 8,  8, 8);

                    // Slice the relevant 16-byte (resp 8-byte) above row.
                    byte[] aboveY = (mbRow == 0) ? null : ExtractRow(aboveYRow, mbCol * 16, 16);
                    byte[] aboveU = (mbRow == 0) ? null : ExtractRow(aboveURow, mbCol * 8, 8);
                    byte[] aboveV = (mbRow == 0) ? null : ExtractRow(aboveVRow, mbCol * 8, 8);

                    var r = mb_encoder.EncodeMacroblockDcPred(
                        mbY, mbU, mbV,
                        aboveY, leftY, aboveU, leftU, aboveV, leftV,
                        fq);
                    mbResults[mbRow * mbCols + mbCol] = r;

                    // Encode the Y mode (DC_PRED) -> bits 1, 0, 0 with
                    // probs vp8_kf_ymode_prob[0..2].
                    var yProbs = vp8_entropymodedata.vp8_kf_ymode_prob;
                    boolhuff.vp8_encode_bool(ref bc0, 1, yProbs[0]);
                    boolhuff.vp8_encode_bool(ref bc0, 0, yProbs[1]);
                    boolhuff.vp8_encode_bool(ref bc0, 0, yProbs[2]);

                    // Encode the UV mode (DC_PRED) -> bit 0 with prob[0].
                    var uvProbs = vp8_entropymodedata.vp8_kf_uv_mode_prob;
                    boolhuff.vp8_encode_bool(ref bc0, 0, uvProbs[0]);

                    // Update neighbour context for the next MB in this row
                    // (rightmost column of the MB's reconstruction) and for
                    // the next row (bottommost row).
                    leftY = ExtractColumn(r.ReconY, srcStride: 16, columnIndex: 15, rows: 16);
                    leftU = ExtractColumn(r.ReconU, srcStride: 8,  columnIndex: 7,  rows: 8);
                    leftV = ExtractColumn(r.ReconV, srcStride: 8,  columnIndex: 7,  rows: 8);

                    CopyRowOut(r.ReconY, srcStride: 16, srcRow: 15,
                               dst: aboveYRow, dstOffset: mbCol * 16, count: 16);
                    CopyRowOut(r.ReconU, srcStride: 8,  srcRow: 7,
                               dst: aboveURow, dstOffset: mbCol * 8, count: 8);
                    CopyRowOut(r.ReconV, srcStride: 8,  srcRow: 7,
                               dst: aboveVRow, dstOffset: mbCol * 8, count: 8);
                }
            }

            // ----- Close partition 0 (flush + patch frame tag) -----
            int partition0Length;
            int totalThroughP0;
            fixed (byte* p = outBuf)
            {
                totalThroughP0 = bitstream.FinishKeyframeFirstPartition(p, cfg, ref bc0);
                partition0Length = totalThroughP0 - partitionStart;
                _ = partition0Length;
            }

            // ----- Open partition 1 (token partition) -----
            BOOL_CODER bc1 = new BOOL_CODER();
            fixed (byte* p = outBuf)
            {
                boolhuff.vp8_start_encode(ref bc1, p + totalThroughP0, p + outBuf.Length);

                // Pack tokens for each MB in raster order: Y2 first, then
                // 16 Y, then 4 U, then 4 V — same order tokenize_mb emits.
                for (int i = 0; i < mbResults.Length; i++)
                {
                    var r = mbResults[i];
                    bitstream.vp8_pack_tokens(ref bc1, r.Y2Block);
                    for (int b = 0; b < 16; b++) bitstream.vp8_pack_tokens(ref bc1, r.YBlocks[b]);
                    for (int b = 0; b < 4;  b++) bitstream.vp8_pack_tokens(ref bc1, r.UBlocks[b]);
                    for (int b = 0; b < 4;  b++) bitstream.vp8_pack_tokens(ref bc1, r.VBlocks[b]);
                }

                boolhuff.vp8_stop_encode(ref bc1);
            }

            int totalBytes = totalThroughP0 + (int)bc1.pos;
            byte[] result = new byte[totalBytes];
            Array.Copy(outBuf, result, totalBytes);
            return result;
        }

        // ----- helpers -----

        private static byte[] ExtractPlane(byte[] src, int srcStride, int x, int y, int w, int h)
        {
            var b = new byte[w * h];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    b[r * w + c] = src[(y + r) * srcStride + (x + c)];
            return b;
        }

        private static byte[] ExtractRow(byte[] src, int offset, int count)
        {
            var b = new byte[count];
            for (int i = 0; i < count; i++) b[i] = src[offset + i];
            return b;
        }

        private static byte[] ExtractColumn(byte[] src, int srcStride, int columnIndex, int rows)
        {
            var b = new byte[rows];
            for (int r = 0; r < rows; r++) b[r] = src[r * srcStride + columnIndex];
            return b;
        }

        private static void CopyRowOut(byte[] src, int srcStride, int srcRow,
            byte[] dst, int dstOffset, int count)
        {
            for (int i = 0; i < count; i++) dst[dstOffset + i] = src[srcRow * srcStride + i];
        }
    }
}
