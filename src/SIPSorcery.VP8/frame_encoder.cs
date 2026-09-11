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
//                ★ mb_no_skip_coeff = 1
//                ★ prob_skip_false (8-bit literal)
//                ★ for each MB: skip-flag bit + keyframe Y mode + UV mode tree paths
//   [bc1]      Token partition (ONE_PARTITION):
//                for each MB: Y2, 16 Y, 4 U, 4 V token streams.
//
// All MBs use DC_PRED for both Y and UV. The encoder runs the per-MB
// pipeline in raster order, threading the rightmost-column / bottommost-
// row reconstruction bytes from each finished MB into the next MB's
// "left" / "above" neighbour buffers so that DC prediction matches the
// decoder's reconstruction context exactly.
//
// Cross-MB entropy-context propagation: each MB's 9-slot above/left
// entropy contexts (4 Y + 2 U + 2 V + 1 Y2) are maintained at frame
// scope rather than per-MB. The above contexts are stored as one row
// of 9-slot entries indexed by MB column position; left contexts are
// kept as a single 9-slot array reset to zero at the start of each MB
// row. Each call into mb_encoder.EncodeMacroblockDcPred passes both
// arrays in by reference so that each block's initial probability-row
// context matches the row the decoder will read with — this was the
// root cause of the macroblock-aligned colour artefacts observed on
// real webcam content with the previous per-MB-only contexts.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Created.
// 26 Apr 2026  Claude          Thread above/left entropy contexts at
//                              frame scope (previously they were local
//                              to each MB and reset at MB boundaries,
//                              which made the encoder use the wrong
//                              probability rows for the decoder's
//                              context state on non-uniform content).
// 26 Apr 2026  Claude          Enable per-MB skip flag (mb_no_skip_coeff
//                              = 1 + prob_skip_false in header). For MBs
//                              whose 25 transformed blocks are all
//                              EOB-only, write a 1-bit skip flag and
//                              suppress the token streams entirely in
//                              partition 1, mirroring libvpx's per-MB
//                              skip optimisation. Cuts per-frame
//                              tokenizer / pack_tokens work for any MB
//                              without residual content.
// 26 Apr 2026  Claude          Allocation hygiene pass: pool the per-MB
//                              MbEncodeResult and scratch buffers in a
//                              ThreadStatic FrameEncoderBuffers struct
//                              that lives for the lifetime of the
//                              encoding thread and is resized lazily on
//                              dimension changes. Per-frame allocations
//                              drop from ~18 MB to a few KB. Also fold
//                              the ExtractPlane / ExtractRow /
//                              ExtractColumn helpers from "allocate &
//                              return" to "fill in place" form.
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
    /// <summary>
    /// Per-thread reusable scratch + pool storage for frame_encoder.
    /// Allocated lazily on first call and resized only when the frame
    /// dimensions change (which is rare in a streaming workload — the
    /// camera/source resolution is usually fixed).
    ///
    /// All buffers held here are pure storage with no per-frame state;
    /// the encoder fills them anew on every call.
    /// </summary>
    internal sealed class FrameEncoderBuffers
    {
        public int Width;
        public int Height;
        public int MbCols;
        public int MbRows;

        // Pool of MbEncodeResult instances, one per MB position. Each is
        // Reset()'d before reuse. In the inter path MbResults holds the
        // ZEROMV-inter candidate and MbResultsIntra holds the DC_PRED intra
        // candidate; IsIntra[idx] selects the winning mode per MB (and
        // doubles as the intra/inter neighbour grid used for the inter
        // mode-context computation).
        public MbEncodeResult[] MbResults;
        public MbEncodeResult[] MbResultsIntra;
        public bool[] MbSkip;
        public bool[] IsIntra;

        // ZEROMV inter-prediction MB buffers (same-position samples pulled
        // from LAST_FRAME). 16x16 Y + 8x8 U + 8x8 V.
        public byte[] PredY = new byte[256];
        public byte[] PredU = new byte[64];
        public byte[] PredV = new byte[64];

        // Per-candidate entropy-context scratch (9 slots each: 4 Y + 2 U +
        // 2 V + 1 Y2). Both candidates are encoded from the same starting
        // context, so each gets its own copy and only the winner's mutated
        // context is committed back to FrameAboveCtx/LeftCtx.
        public byte[] CtxAboveInter = new byte[9];
        public byte[] CtxLeftInter = new byte[9];
        public byte[] CtxAboveIntra = new byte[9];
        public byte[] CtxLeftIntra = new byte[9];

        // Per-MB scratch shared by all MBs (single instance — only one MB
        // is being encoded at a time on this thread).
        public MbEncoderScratch Scratch = new MbEncoderScratch();

        // Per-MB context state.
        public byte[] FrameAboveCtx;     // mbCols * 9
        public byte[] LeftCtx = new byte[9];
        public byte[] AboveCtx = new byte[9];

        // Reconstruction context buffers.
        public byte[] AboveYRow;         // width
        public byte[] AboveURow;         // width / 2
        public byte[] AboveVRow;         // width / 2
        public byte[] LeftY = new byte[16];
        public byte[] LeftU = new byte[8];
        public byte[] LeftV = new byte[8];
        public byte[] AboveY = new byte[16];
        public byte[] AboveU = new byte[8];
        public byte[] AboveV = new byte[8];

        // Per-MB source slices.
        public byte[] MbY = new byte[256];
        public byte[] MbU = new byte[64];
        public byte[] MbV = new byte[64];

        // Output buffer.
        public byte[] OutBuf;

        // Reference-frame storage. After every successful encode,
        // EncodeKeyframe / future EncodeInterFrame copies the
        // per-MB reconstructed pixels here, building a frame-sized
        // copy of what the decoder will see as the most recent
        // decoded frame. Inter frames (PR 3+) will use this as the
        // prediction reference.
        //
        // LastFrameValid starts false and is set true after the first
        // successful encode. Inter encoding requires it to be true;
        // when false (first frame of stream, or after a forced reset)
        // the frame must be a keyframe regardless of interval.
        public byte[] LastFrameY;
        public byte[] LastFrameU;
        public byte[] LastFrameV;
        public bool LastFrameValid;

        public void EnsureForFrame(int width, int height)
        {
            int chromaW = width / 2;
            int mbCols = width / 16;
            int mbRows = height / 16;
            int total = mbCols * mbRows;

            if (MbResults == null || MbResults.Length < total)
            {
                int oldLen = MbResults?.Length ?? 0;
                var newResults = new MbEncodeResult[total];
                if (oldLen > 0) System.Array.Copy(MbResults, newResults, oldLen);
                for (int i = oldLen; i < total; i++) newResults[i] = new MbEncodeResult();
                MbResults = newResults;
            }
            if (MbResultsIntra == null || MbResultsIntra.Length < total)
            {
                int oldLen = MbResultsIntra?.Length ?? 0;
                var newResults = new MbEncodeResult[total];
                if (oldLen > 0) System.Array.Copy(MbResultsIntra, newResults, oldLen);
                for (int i = oldLen; i < total; i++) newResults[i] = new MbEncodeResult();
                MbResultsIntra = newResults;
            }
            if (MbSkip == null || MbSkip.Length < total) MbSkip = new bool[total];
            if (IsIntra == null || IsIntra.Length < total) IsIntra = new bool[total];

            if (FrameAboveCtx == null || FrameAboveCtx.Length < mbCols * 9)
                FrameAboveCtx = new byte[mbCols * 9];

            if (AboveYRow == null || AboveYRow.Length < width)
                AboveYRow = new byte[width];
            if (AboveURow == null || AboveURow.Length < chromaW)
            {
                AboveURow = new byte[chromaW];
                AboveVRow = new byte[chromaW];
            }

            int bufLen = System.Math.Max(4096, width * height * 2 + 1024);
            if (OutBuf == null || OutBuf.Length < bufLen)
                OutBuf = new byte[bufLen];

            int ySize = width * height;
            int cSize = chromaW * (height / 2);
            if (LastFrameY == null || LastFrameY.Length < ySize)
            {
                LastFrameY = new byte[ySize];
            }
            if (LastFrameU == null || LastFrameU.Length < cSize)
            {
                LastFrameU = new byte[cSize];
                LastFrameV = new byte[cSize];
            }
            // Dimension change invalidates any previously-stored
            // reference frame.
            if (Width != width || Height != height)
            {
                LastFrameValid = false;
            }

            Width = width;
            Height = height;
            MbCols = mbCols;
            MbRows = mbRows;
        }
    }

    public static unsafe class frame_encoder
    {
        // Per-thread reusable buffers. Most encoders are called from the
        // same thread for the lifetime of a stream, so this amortises the
        // per-frame allocations to (effectively) zero after the first
        // call.
        [System.ThreadStatic]
        private static FrameEncoderBuffers _buffers;

        /// <summary>
        /// Diagnostic: the number of macroblocks coded as intra (DC_PRED) in
        /// the most recent inter frame. Exposed for tests/tuning of the
        /// intra-fallback mode decision; not part of the bitstream.
        /// </summary>
        public static int LastInterFrameIntraMbCount;
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
        internal static byte[] EncodeKeyframeWithBuffers(byte[] srcY, byte[] srcU, byte[] srcV,
            int width, int height, int qIndex, FrameEncoderBuffers buffers)
        {
            if (buffers == null) throw new System.ArgumentNullException(nameof(buffers));
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

            // Use the caller-supplied per-codec scratch buffers.
            var buf = buffers;
            buf.EnsureForFrame(width, height);
            var scratch = buf.Scratch;

            var cfg = new KeyframeHeaderConfig
            {
                Width = width, Height = height,
                BaseQindex = qIndex,
            };

            byte[] outBuf = buf.OutBuf;

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

            // ----- Phase 1: encode all MBs (no bit writing yet) -----
            //
            // We need the per-MB skip flags before we can write
            // prob_skip_false in the header. So all MBs are encoded
            // first (predict + DCT + Walsh + quantize + tokenize +
            // reconstruct) into MbEncodeResults, with cross-MB entropy
            // context propagation happening in this pass. Bit-writing
            // happens in phase 2 below.

            MbEncodeResult[] mbResults = buf.MbResults;
            bool[] mbSkip = buf.MbSkip;
            int skipTrueCount = 0;
            int skipFalseCount = 0;

            // Reconstruction context buffers (per-row "above" cache;
            // per-MB "left" reused across MBs in a row).
            byte[] aboveYRow = buf.AboveYRow;
            byte[] aboveURow = buf.AboveURow;
            byte[] aboveVRow = buf.AboveVRow;

            // Frame-scope entropy contexts. The above row stores one
            // 9-slot context per MB column position (4 Y + 2 U + 2 V + 1
            // Y2 = 9 slots), reused across MB rows so that an MB at
            // (row r, col c) reads its above context from the bottom of
            // the MB at (r-1, c). The left context is a single 9-slot
            // array that persists across MBs in a row and is reset at
            // each row boundary (no wrap-around). C-array layout (flat,
            // row-major-ish): aboveCtx[mbCol * 9 + slot].
            byte[] frameAboveCtx = buf.FrameAboveCtx;
            Array.Clear(frameAboveCtx, 0, mbCols * 9);

            byte[] leftCtx = buf.LeftCtx;
            byte[] aboveCtx = buf.AboveCtx;
            byte[] mbY = buf.MbY;
            byte[] mbU = buf.MbU;
            byte[] mbV = buf.MbV;
            byte[] aboveY = buf.AboveY;
            byte[] aboveU = buf.AboveU;
            byte[] aboveV = buf.AboveV;
            byte[] leftY = buf.LeftY;
            byte[] leftU = buf.LeftU;
            byte[] leftV = buf.LeftV;

            for (int mbRow = 0; mbRow < mbRows; mbRow++)
            {
                bool haveLeftNeighbour = false;
                Array.Clear(leftCtx, 0, 9);

                for (int mbCol = 0; mbCol < mbCols; mbCol++)
                {
                    // Slice the 16x16 + 8x8 + 8x8 source for this MB.
                    ExtractPlaneInto(srcY, width,  mbCol * 16, mbRow * 16, 16, 16, mbY);
                    ExtractPlaneInto(srcU, chromaW, mbCol * 8,  mbRow * 8,  8, 8,  mbU);
                    ExtractPlaneInto(srcV, chromaW, mbCol * 8,  mbRow * 8,  8, 8,  mbV);

                    // Slice the relevant 16-byte (resp 8-byte) above row.
                    bool haveAbove = mbRow > 0;
                    if (haveAbove)
                    {
                        ExtractRowInto(aboveYRow, mbCol * 16, 16, aboveY);
                        ExtractRowInto(aboveURow, mbCol * 8,  8,  aboveU);
                        ExtractRowInto(aboveVRow, mbCol * 8,  8,  aboveV);
                    }

                    // Pull this MB column's above context out of the
                    // frame-scope buffer; mb_encoder mutates it in place
                    // as it processes blocks, then we copy it back.
                    int aboveBase = mbCol * 9;
                    for (int s = 0; s < 9; s++) aboveCtx[s] = frameAboveCtx[aboveBase + s];

                    int idx = mbRow * mbCols + mbCol;
                    var pooled = mbResults[idx];

                    var r = mb_encoder.EncodeMacroblockDcPred(
                        mbY, mbU, mbV,
                        haveAbove        ? aboveY : null,
                        haveLeftNeighbour ? leftY : null,
                        haveAbove        ? aboveU : null,
                        haveLeftNeighbour ? leftU : null,
                        haveAbove        ? aboveV : null,
                        haveLeftNeighbour ? leftV : null,
                        fq,
                        aboveCtx, leftCtx,
                        pooled, scratch);
                    // r is the same instance as `pooled`; explicit assign
                    // not strictly needed but kept for symmetry.
                    mbResults[idx] = r;

                    // An MB is skippable iff every one of its 25
                    // transformed blocks (Y2 + 16 Y + 4 U + 4 V) is
                    // EOB-only. EOB-only blocks contribute nothing to
                    // the residual the decoder reconstructs, so the
                    // decoder can be told to skip the entire token
                    // partition for this MB and reset its entropy
                    // contexts. Crucially, the encoder's mb_encoder also
                    // sets every per-block context slot to 0 in this
                    // case (see context update lines in mb_encoder.cs),
                    // so the encoder and decoder context state stay in
                    // sync without any extra reset on this side.
                    bool skip = IsAllEob(r);
                    mbSkip[idx] = skip;
                    if (skip) skipTrueCount++; else skipFalseCount++;

                    // The mutated aboveCtx is the new "above" for the MB
                    // immediately below this one (same column, next row).
                    for (int s = 0; s < 9; s++) frameAboveCtx[aboveBase + s] = aboveCtx[s];

                    // Update neighbour context for the next MB in this row
                    // (rightmost column of the MB's reconstruction) and for
                    // the next row (bottommost row).
                    ExtractColumnInto(r.ReconY, srcStride: 16, columnIndex: 15, rows: 16, dst: leftY);
                    ExtractColumnInto(r.ReconU, srcStride: 8,  columnIndex: 7,  rows: 8,  dst: leftU);
                    ExtractColumnInto(r.ReconV, srcStride: 8,  columnIndex: 7,  rows: 8,  dst: leftV);
                    haveLeftNeighbour = true;

                    CopyRowOut(r.ReconY, srcStride: 16, srcRow: 15,
                               dst: aboveYRow, dstOffset: mbCol * 16, count: 16);
                    CopyRowOut(r.ReconU, srcStride: 8,  srcRow: 7,
                               dst: aboveURow, dstOffset: mbCol * 8, count: 8);
                    CopyRowOut(r.ReconV, srcStride: 8,  srcRow: 7,
                               dst: aboveVRow, dstOffset: mbCol * 8, count: 8);
                }
            }

            // ----- Save the reconstructed frame as the next inter
            //       prediction reference -----
            //
            // For each MB we already have its 16x16 Y + 8x8 U + 8x8 V
            // reconstruction in mbResults[idx].ReconY/U/V (the bytes
            // the decoder will produce given the same bitstream).
            // Stitch them into LastFrameY/U/V so the next inter frame
            // (added in PR 3+) can use this as its prediction source.
            //
            // PR 1 just plumbs the storage; nothing currently consumes
            // the saved reference. The work is unconditional regardless
            // because (a) the per-MB cost is O(384 bytes) so trivial,
            // and (b) this lets PR 5's wire-up happen as a flip rather
            // than a behaviour change to EncodeKeyframe.
            for (int mbRow = 0; mbRow < mbRows; mbRow++)
            {
                for (int mbCol = 0; mbCol < mbCols; mbCol++)
                {
                    var r = mbResults[mbRow * mbCols + mbCol];

                    int yBase = (mbRow * 16) * width + (mbCol * 16);
                    for (int row = 0; row < 16; row++)
                    {
                        Buffer.BlockCopy(r.ReconY, row * 16,
                            buf.LastFrameY, yBase + row * width, 16);
                    }

                    int cBase = (mbRow * 8) * chromaW + (mbCol * 8);
                    for (int row = 0; row < 8; row++)
                    {
                        Buffer.BlockCopy(r.ReconU, row * 8,
                            buf.LastFrameU, cBase + row * chromaW, 8);
                        Buffer.BlockCopy(r.ReconV, row * 8,
                            buf.LastFrameV, cBase + row * chromaW, 8);
                    }
                }
            }
            buf.LastFrameValid = true;

            // ----- Phase 2a: mb_no_skip_coeff + prob_skip_false -----
            //
            // Now that we know how many MBs are skippable we can pick
            // prob_skip_false. libvpx's formula: prob_skip_false =
            // skipFalseCount * 256 / total. Clamped to [1, 255] so the
            // boolean coder can still encode whichever bit value occurs.
            // Falls back to 0xfb (libvpx's hardcoded initial default)
            // when no MBs are skippable, which is efficient because then
            // every per-MB skip-flag bit is "0" and a high prob_skip_false
            // makes that essentially free.
            bitstream.vp8_write_bit(ref bc0, 1);   // mb_no_skip_coeff = 1

            byte probSkipFalse;
            if (skipTrueCount == 0)
            {
                probSkipFalse = 0xfb;
            }
            else
            {
                int total = skipFalseCount + skipTrueCount;
                int p = skipFalseCount * 256 / total;
                if (p < 1) p = 1;
                if (p > 255) p = 255;
                probSkipFalse = (byte)p;
            }
            for (int b = 7; b >= 0; b--)
                bitstream.vp8_write_bit(ref bc0, (probSkipFalse >> b) & 1);

            // ----- Phase 2b: per-MB skip flag + Y/UV mode trees -----
            int totalMbsP2 = mbRows * mbCols;
            for (int i = 0; i < totalMbsP2; i++)
            {
                // Skip flag (boolean coder, prob_skip_false).
                boolhuff.vp8_encode_bool(ref bc0, mbSkip[i] ? 1 : 0, probSkipFalse);

                // Y mode = DC_PRED -> tree path 1, 0, 0 with
                // vp8_kf_ymode_prob[0..2].
                var yProbs = vp8_entropymodedata.vp8_kf_ymode_prob;
                boolhuff.vp8_encode_bool(ref bc0, 1, yProbs[0]);
                boolhuff.vp8_encode_bool(ref bc0, 0, yProbs[1]);
                boolhuff.vp8_encode_bool(ref bc0, 0, yProbs[2]);

                // UV mode = DC_PRED -> bit 0 with vp8_kf_uv_mode_prob[0].
                var uvProbs = vp8_entropymodedata.vp8_kf_uv_mode_prob;
                boolhuff.vp8_encode_bool(ref bc0, 0, uvProbs[0]);
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

                // Pack tokens for each MB in raster order: Y2 first,
                // then 16 Y, then 4 U, then 4 V — same order
                // tokenize_mb emits. SKIPPABLE MBs contribute zero
                // tokens to partition 1, mirroring the decoder which on
                // skip_flag = 1 calls vp8_reset_mb_tokens_context and
                // never reads coefficient bits for the MB.
                int totalMbs = mbRows * mbCols;
                for (int i = 0; i < totalMbs; i++)
                {
                    if (mbSkip[i]) continue;

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

        /// <summary>
        /// Encode an I420 source frame as a VP8 inter (P) frame using
        /// ZEROMV + LAST_FRAME for every macroblock. The reference frame
        /// must already be cached on the per-thread <see cref="FrameEncoderBuffers"/>
        /// via a previous call to <see cref="EncodeKeyframe"/> (or another
        /// <see cref="EncodeInterFrame"/>).
        ///
        /// PR 5 of the P-frame foundation series — the orchestration layer
        /// that ties the header writer (PR 2), the inter MB encoder (PR 3),
        /// and the per-MB inter mode bit writer (PR 4) into a fully
        /// decodable inter frame.
        /// </summary>
        /// <param name="srcY">width * height luma source bytes.</param>
        /// <param name="srcU">(width/2) * (height/2) chroma U.</param>
        /// <param name="srcV">(width/2) * (height/2) chroma V.</param>
        /// <param name="width">Frame width (multiple of 16).</param>
        /// <param name="height">Frame height (multiple of 16).</param>
        /// <param name="qIndex">Base quantizer (0..127).</param>
        /// <param name="buffers">Per-instance scratch/reference buffers.</param>
        /// <param name="intraFallback">When true, every MB is encoded both as a
        /// ZEROMV inter candidate and a DC_PRED intra candidate and the cheaper
        /// (rate-distortion measured against the source) is kept. This stops
        /// error accumulation on content ZEROMV cannot represent, at roughly 2x
        /// the encode cost per inter frame. When false, every MB is ZEROMV
        /// inter (the original P-frame behaviour): cheapest, but regions whose
        /// per-frame change quantises to zero drift until the next keyframe.</param>
        /// <returns>The encoded VP8 inter frame bytes.</returns>
        internal static byte[] EncodeInterFrameWithBuffers(byte[] srcY, byte[] srcU, byte[] srcV,
            int width, int height, int qIndex, FrameEncoderBuffers buffers, bool intraFallback = true)
        {
            if (buffers == null) throw new System.ArgumentNullException(nameof(buffers));
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

            FrameQuantizer fq = quantizer_init.BuildForQIndex(qIndex);

            var buf = buffers;
            buf.EnsureForFrame(width, height);
            var scratch = buf.Scratch;

            if (!buf.LastFrameValid)
            {
                throw new InvalidOperationException(
                    "EncodeInterFrame requires a valid LAST_FRAME reference. Call EncodeKeyframe first.");
            }

            var cfg = new InterFrameHeaderConfig
            {
                BaseQindex = qIndex,
            };

            byte[] outBuf = buf.OutBuf;

            // ----- Open compressed first partition (header through refresh_last_frame) -----
            BOOL_CODER bc0 = new BOOL_CODER();
            int partitionStart;
            fixed (byte* p = outBuf)
            {
                partitionStart = bitstream.StartInterFrameHeader(p, outBuf.Length, cfg, ref bc0);
            }

            // ----- Coef-prob updates: 1056 zero flag bits (no updates) -----
            for (int t = 0; t < entropy.BLOCK_TYPES; t++)
                for (int b = 0; b < entropy.COEF_BANDS; b++)
                    for (int c = 0; c < entropy.PREV_COEF_CONTEXTS; c++)
                        for (int n = 0; n < entropy.ENTROPY_NODES; n++)
                            boolhuff.vp8_encode_bool(ref bc0, 0,
                                coefupdateprobs.vp8_coef_update_probs[t, b, c, n]);

            // ----- Phase 1: encode all MBs with a per-MB intra/inter RD decision -----
            //
            // Same two-phase split as the keyframe path: encode every MB
            // first so we know skip flags before writing prob_skip_false
            // and the per-MB skip bits.
            //
            // For each MB we evaluate two candidates and keep the cheaper:
            //   - ZEROMV inter: same-position 16x16 Y + 8x8 U + 8x8 V samples
            //     from buf.LastFrameY/U/V (the previous reconstruction).
            //   - DC_PRED intra: predicted from the CURRENT frame's causal
            //     reconstructed neighbours (above row + left column), exactly
            //     as the keyframe path does.
            //
            // The intra candidate is what stops the high-motion error
            // accumulation: when ZEROMV cannot represent fast-changing
            // content at the chosen quantizer, the MB is intra-refreshed
            // instead of letting stale prediction ghost through and compound
            // across the inter frames between keyframes. When ZEROMV already
            // predicts the MB perfectly (zero residual -> skippable) we keep
            // inter unconditionally, which preserves the tiny all-skip frames
            // for static content.
            //
            // The decoder reconstructs whichever mode we signal, so either
            // choice is bit-exact; the decision only trades quality vs size.

            MbEncodeResult[] mbResults = buf.MbResults;            // inter candidate pool.
            MbEncodeResult[] mbResultsIntra = buf.MbResultsIntra;  // intra candidate pool.
            bool[] mbSkip = buf.MbSkip;
            bool[] isIntra = buf.IsIntra;
            int skipTrueCount = 0;
            int skipFalseCount = 0;
            int intraCount = 0;

            byte[] frameAboveCtx = buf.FrameAboveCtx;
            Array.Clear(frameAboveCtx, 0, mbCols * 9);

            byte[] leftCtx = buf.LeftCtx;
            byte[] mbY = buf.MbY;
            byte[] mbU = buf.MbU;
            byte[] mbV = buf.MbV;

            // ZEROMV prediction buffers (16x16 Y + 8x8 U + 8x8 V).
            byte[] predY = buf.PredY;
            byte[] predU = buf.PredU;
            byte[] predV = buf.PredV;

            // Per-candidate entropy-context copies (both candidates start
            // from the same above/left context; only the winner's mutated
            // context is committed).
            byte[] ctxAboveInter = buf.CtxAboveInter;
            byte[] ctxLeftInter = buf.CtxLeftInter;
            byte[] ctxAboveIntra = buf.CtxAboveIntra;
            byte[] ctxLeftIntra = buf.CtxLeftIntra;

            // Current-frame reconstructed-neighbour buffers for intra
            // prediction (same role as in the keyframe path).
            byte[] aboveYRow = buf.AboveYRow;
            byte[] aboveURow = buf.AboveURow;
            byte[] aboveVRow = buf.AboveVRow;
            byte[] leftY = buf.LeftY;
            byte[] leftU = buf.LeftU;
            byte[] leftV = buf.LeftV;
            byte[] aboveY = buf.AboveY;
            byte[] aboveU = buf.AboveU;
            byte[] aboveV = buf.AboveV;

            for (int mbRow = 0; mbRow < mbRows; mbRow++)
            {
                bool haveLeftNeighbour = false;
                Array.Clear(leftCtx, 0, 9);

                for (int mbCol = 0; mbCol < mbCols; mbCol++)
                {
                    // Source MB.
                    ExtractPlaneInto(srcY, width,   mbCol * 16, mbRow * 16, 16, 16, mbY);
                    ExtractPlaneInto(srcU, chromaW, mbCol * 8,  mbRow * 8,   8,  8, mbU);
                    ExtractPlaneInto(srcV, chromaW, mbCol * 8,  mbRow * 8,   8,  8, mbV);

                    // Same-position prediction from LAST_FRAME.
                    ExtractPlaneInto(buf.LastFrameY, width,   mbCol * 16, mbRow * 16, 16, 16, predY);
                    ExtractPlaneInto(buf.LastFrameU, chromaW, mbCol * 8,  mbRow * 8,   8,  8, predU);
                    ExtractPlaneInto(buf.LastFrameV, chromaW, mbCol * 8,  mbRow * 8,   8,  8, predV);

                    bool haveAbove = mbRow > 0;
                    if (haveAbove)
                    {
                        ExtractRowInto(aboveYRow, mbCol * 16, 16, aboveY);
                        ExtractRowInto(aboveURow, mbCol * 8,  8,  aboveU);
                        ExtractRowInto(aboveVRow, mbCol * 8,  8,  aboveV);
                    }

                    int aboveBase = mbCol * 9;
                    int idx = mbRow * mbCols + mbCol;

                    // --- Inter (ZEROMV) candidate, from a copy of the start context. ---
                    for (int s = 0; s < 9; s++) ctxAboveInter[s] = frameAboveCtx[aboveBase + s];
                    for (int s = 0; s < 9; s++) ctxLeftInter[s] = leftCtx[s];

                    var interR = mb_encoder.EncodeMacroblockZeroMvLast(
                        mbY, mbU, mbV,
                        predY, predU, predV,
                        fq,
                        ctxAboveInter, ctxLeftInter,
                        mbResults[idx], scratch);

                    MbEncodeResult winner;
                    byte[] winAbove, winLeft;
                    bool chooseIntra = false;

                    if (intraFallback)
                    {
                        // --- Intra (DC_PRED) candidate, from a copy of the start context. ---
                        //
                        // Both candidates are always evaluated. We deliberately do
                        // NOT shortcut on "inter residual quantised to zero": that
                        // condition means the coded residual is zero, NOT that the
                        // prediction matches the source. For fine/high-contrast
                        // moving detail the residual can quantise to zero while the
                        // source has actually changed -- the MB would then be
                        // skipped and the stale prediction would ghost through and
                        // accumulate. The RD cost below is measured against the
                        // SOURCE (distortion = SSE vs the source MB), so such an MB
                        // shows a large inter cost and is refreshed via intra.
                        for (int s = 0; s < 9; s++) ctxAboveIntra[s] = frameAboveCtx[aboveBase + s];
                        for (int s = 0; s < 9; s++) ctxLeftIntra[s] = leftCtx[s];

                        var intraR = mb_encoder.EncodeMacroblockDcPred(
                            mbY, mbU, mbV,
                            haveAbove         ? aboveY : null,
                            haveLeftNeighbour ? leftY : null,
                            haveAbove         ? aboveU : null,
                            haveLeftNeighbour ? leftU : null,
                            haveAbove         ? aboveV : null,
                            haveLeftNeighbour ? leftV : null,
                            fq,
                            ctxAboveIntra, ctxLeftIntra,
                            mbResultsIntra[idx], scratch);

                        long interCost = ModeRdCost(mbY, mbU, mbV, interR, isIntra: false);
                        long intraCost = ModeRdCost(mbY, mbU, mbV, intraR, isIntra: true);

                        if (intraCost < interCost)
                        {
                            winner = intraR; winAbove = ctxAboveIntra; winLeft = ctxLeftIntra;
                            chooseIntra = true;
                        }
                        else
                        {
                            winner = interR; winAbove = ctxAboveInter; winLeft = ctxLeftInter;
                        }
                    }
                    else
                    {
                        // Intra fallback disabled: every MB is the ZEROMV inter
                        // candidate (original P-frame behaviour, ~half the encode
                        // cost). The reconstructed-neighbour maintenance below is
                        // still performed so the buffers stay coherent.
                        winner = interR; winAbove = ctxAboveInter; winLeft = ctxLeftInter;
                    }

                    isIntra[idx] = chooseIntra;
                    if (chooseIntra) intraCount++;

                    bool skip = IsAllEob(winner);
                    mbSkip[idx] = skip;
                    if (skip) skipTrueCount++; else skipFalseCount++;

                    // Commit the winner's mutated entropy context.
                    for (int s = 0; s < 9; s++) frameAboveCtx[aboveBase + s] = winAbove[s];
                    for (int s = 0; s < 9; s++) leftCtx[s] = winLeft[s];

                    // Update the current-frame reconstructed-neighbour buffers
                    // from the winner so a later intra MB predicts from the
                    // pixels the decoder will have reconstructed.
                    ExtractColumnInto(winner.ReconY, srcStride: 16, columnIndex: 15, rows: 16, dst: leftY);
                    ExtractColumnInto(winner.ReconU, srcStride: 8,  columnIndex: 7,  rows: 8,  dst: leftU);
                    ExtractColumnInto(winner.ReconV, srcStride: 8,  columnIndex: 7,  rows: 8,  dst: leftV);
                    haveLeftNeighbour = true;

                    CopyRowOut(winner.ReconY, srcStride: 16, srcRow: 15, dst: aboveYRow, dstOffset: mbCol * 16, count: 16);
                    CopyRowOut(winner.ReconU, srcStride: 8,  srcRow: 7,  dst: aboveURow, dstOffset: mbCol * 8,  count: 8);
                    CopyRowOut(winner.ReconV, srcStride: 8,  srcRow: 7,  dst: aboveVRow, dstOffset: mbCol * 8,  count: 8);
                }
            }

            // ----- Save reconstructed frame as next inter prediction ref -----
            //
            // Same per-MB stitch as the keyframe path, reading each MB's
            // winning-candidate reconstruction. Future inter frames in the
            // stream use these bytes as their LAST_FRAME prediction source.
            for (int mbRow = 0; mbRow < mbRows; mbRow++)
            {
                for (int mbCol = 0; mbCol < mbCols; mbCol++)
                {
                    int idx = mbRow * mbCols + mbCol;
                    var r = isIntra[idx] ? mbResultsIntra[idx] : mbResults[idx];

                    int yBase = (mbRow * 16) * width + (mbCol * 16);
                    for (int row = 0; row < 16; row++)
                    {
                        Buffer.BlockCopy(r.ReconY, row * 16,
                            buf.LastFrameY, yBase + row * width, 16);
                    }

                    int cBase = (mbRow * 8) * chromaW + (mbCol * 8);
                    for (int row = 0; row < 8; row++)
                    {
                        Buffer.BlockCopy(r.ReconU, row * 8,
                            buf.LastFrameU, cBase + row * chromaW, 8);
                        Buffer.BlockCopy(r.ReconV, row * 8,
                            buf.LastFrameV, cBase + row * chromaW, 8);
                    }
                }
            }
            buf.LastFrameValid = true;

            // ----- Phase 2a: mb_no_skip_coeff + prob_skip_false -----
            bitstream.vp8_write_bit(ref bc0, 1);   // mb_no_skip_coeff = 1

            byte probSkipFalse;
            if (skipTrueCount == 0)
            {
                probSkipFalse = 0xfb;
            }
            else
            {
                int total = skipFalseCount + skipTrueCount;
                int p = skipFalseCount * 256 / total;
                if (p < 1) p = 1;
                if (p > 255) p = 255;
                probSkipFalse = (byte)p;
            }
            for (int b = 7; b >= 0; b--)
                bitstream.vp8_write_bit(ref bc0, (probSkipFalse >> b) & 1);

            // ----- Phase 2b: prob_intra / prob_last / prob_gf -----
            //
            // prob_intra = P(is_inter bit == 0) = P(MB is intra), scaled to
            // [1, 255] from the actual intra/inter split chosen above so both
            // intra and inter MBs code at a sensible cost. With no intra MBs
            // this collapses to 1 (writing is_inter=1 costs ~0 bits), matching
            // the previous all-inter behaviour.
            //   prob_last = 1   -> writing ref_is_LAST=0 costs ~0 bits.
            //   prob_gf   = 128 -> never used (ref is always LAST).
            int totalMbCount = mbRows * mbCols;
            int probIntraInt = intraCount * 256 / totalMbCount;
            if (probIntraInt < 1) probIntraInt = 1;
            if (probIntraInt > 255) probIntraInt = 255;
            byte probIntra = (byte)probIntraInt;
            const byte probLast = 1;
            const byte probGf = 128;
            for (int b = 7; b >= 0; b--) bitstream.vp8_write_bit(ref bc0, (probIntra >> b) & 1);
            for (int b = 7; b >= 0; b--) bitstream.vp8_write_bit(ref bc0, (probLast >> b) & 1);
            for (int b = 7; b >= 0; b--) bitstream.vp8_write_bit(ref bc0, (probGf >> b) & 1);

            // ymode_prob update flag = 0 (use defaults).
            bitstream.vp8_write_bit(ref bc0, 0);
            // uv_mode_prob update flag = 0.
            bitstream.vp8_write_bit(ref bc0, 0);

            // MV context updates: for each of the two MV components
            // (row, col) and each of MVPcount (=19) probabilities, write
            // an "update?" flag of 0 with the default vp8_mv_update_probs.
            // We never update MV probs because we never code MV
            // residuals (all MBs are ZEROMV).
            for (int comp = 0; comp < 2; comp++)
            {
                byte[] up = entropymv.vp8_mv_update_probs[comp].prob;
                int mvpCount = (int)MV_ENUM.MVPcount;
                for (int j = 0; j < mvpCount; j++)
                {
                    boolhuff.vp8_encode_bool(ref bc0, 0, up[j]);
                }
            }

            // ----- Phase 2c: per-MB skip flag + mode bits -----
            //
            // Each MB is either an intra (DC_PRED) MB or a ZEROMV inter MB.
            //
            // For inter MBs the mode tree probability row is the decoder's
            // cnt[CNT_INTRA] (decodemv.read_mb_modes_mv): walking the above,
            // left and above-left neighbours, each INTER neighbour (every
            // inter MB here has mv == 0) adds 2 (above/left) or 1 (above-left)
            // to cnt[CNT_INTRA]; intra neighbours and off-frame borders add
            // nothing. So cnt = 2*[above inter] + 2*[left inter] +
            // 1*[above-left inter], in the range [0, 5]. We pre-build the six
            // possible rows and index by the per-MB count.
            //
            // For intra MBs we write is_inter=0 then the DC_PRED Y/UV mode
            // bits (matching the decoder's intra branch).
            byte[][] modeProbRows = new byte[6][];
            for (int row = 0; row < 6; row++)
            {
                modeProbRows[row] = new byte[4];
                for (int j = 0; j < 4; j++)
                    modeProbRows[row][j] = (byte)modecont.vp8_mode_contexts[row, j];
            }

            for (int mbRow = 0; mbRow < mbRows; mbRow++)
            {
                for (int mbCol = 0; mbCol < mbCols; mbCol++)
                {
                    int i = mbRow * mbCols + mbCol;

                    // Skip flag (boolean coder, prob_skip_false).
                    boolhuff.vp8_encode_bool(ref bc0, mbSkip[i] ? 1 : 0, probSkipFalse);

                    if (isIntra[i])
                    {
                        // is_inter = 0, then DC_PRED luma + chroma mode bits.
                        bitstream.WriteInterMbAsIntra(ref bc0, probIntra);
                        bitstream.WriteIntraMbYMode16x16(ref bc0, MB_PREDICTION_MODE.DC_PRED);
                        bitstream.WriteIntraMbUVMode(ref bc0, MB_PREDICTION_MODE.DC_PRED);
                    }
                    else
                    {
                        // Neighbour-derived inter mode context (intra/border
                        // neighbours contribute nothing).
                        bool aboveInter = mbRow > 0       && !isIntra[(mbRow - 1) * mbCols + mbCol];
                        bool leftInter = mbCol > 0       && !isIntra[mbRow * mbCols + (mbCol - 1)];
                        bool aboveLeftInter = mbRow > 0 && mbCol > 0 && !isIntra[(mbRow - 1) * mbCols + (mbCol - 1)];
                        int cnt = (aboveInter ? 2 : 0) + (leftInter ? 2 : 0) + (aboveLeftInter ? 1 : 0);

                        // is_inter=1, ref_is_LAST=0, ZEROMV tree path.
                        bitstream.WriteInterMbRefAndMode(
                            ref bc0,
                            probIntra, probLast, probGf,
                            MV_REFERENCE_FRAME.LAST_FRAME,
                            modeProbRows[cnt],
                            MB_PREDICTION_MODE.ZEROMV);
                    }
                }
            }

            // ----- Close partition 0 -----
            int totalThroughP0;
            fixed (byte* p = outBuf)
            {
                totalThroughP0 = bitstream.FinishInterFrameFirstPartition(p, cfg, ref bc0);
            }

            // ----- Open partition 1 (token partition) -----
            BOOL_CODER bc1 = new BOOL_CODER();
            fixed (byte* p = outBuf)
            {
                boolhuff.vp8_start_encode(ref bc1, p + totalThroughP0, p + outBuf.Length);

                int totalMbs = mbRows * mbCols;
                for (int i = 0; i < totalMbs; i++)
                {
                    if (mbSkip[i]) continue;

                    var r = isIntra[i] ? mbResultsIntra[i] : mbResults[i];
                    bitstream.vp8_pack_tokens(ref bc1, r.Y2Block);
                    for (int b = 0; b < 16; b++) bitstream.vp8_pack_tokens(ref bc1, r.YBlocks[b]);
                    for (int b = 0; b < 4;  b++) bitstream.vp8_pack_tokens(ref bc1, r.UBlocks[b]);
                    for (int b = 0; b < 4;  b++) bitstream.vp8_pack_tokens(ref bc1, r.VBlocks[b]);
                }

                boolhuff.vp8_stop_encode(ref bc1);
            }

            LastInterFrameIntraMbCount = intraCount;

            int totalBytes = totalThroughP0 + (int)bc1.pos;
            byte[] result = new byte[totalBytes];
            Array.Copy(outBuf, result, totalBytes);
            return result;
        }

        /// <summary>
        /// Convenience wrapper for callers that don't want to manage their
        /// own <see cref="FrameEncoderBuffers"/>. Uses a per-thread
        /// instance allocated lazily on first call.
        ///
        /// IMPORTANT: this wrapper is unsuitable for codecs that emit
        /// inter frames between keyframes -- if the keyframe call lands
        /// on one thread and the next inter call lands on another (e.g.
        /// when invoked from a Timer callback), the second thread's
        /// buffers will not have a valid LAST_FRAME reference and the
        /// inter call will throw. Use the <see cref="VP8Codec"/> entry
        /// point in that case; it owns a single per-codec
        /// <see cref="FrameEncoderBuffers"/> guarded by a lock.
        /// </summary>
        public static byte[] EncodeKeyframe(byte[] srcY, byte[] srcU, byte[] srcV,
            int width, int height, int qIndex)
        {
            return EncodeKeyframeWithBuffers(srcY, srcU, srcV, width, height, qIndex,
                _buffers ??= new FrameEncoderBuffers());
        }

        /// <summary>
        /// Convenience wrapper for callers that don't want to manage their
        /// own <see cref="FrameEncoderBuffers"/>. Uses a per-thread
        /// instance allocated lazily on first call.
        ///
        /// IMPORTANT: see the warning on <see cref="EncodeKeyframe"/>
        /// regarding cross-thread invocation. <see cref="VP8Codec"/> is
        /// the production-correct entry point.
        /// </summary>
        public static byte[] EncodeInterFrame(byte[] srcY, byte[] srcU, byte[] srcV,
            int width, int height, int qIndex, bool intraFallback = true)
        {
            return EncodeInterFrameWithBuffers(srcY, srcU, srcV, width, height, qIndex,
                _buffers ??= new FrameEncoderBuffers(), intraFallback);
        }

        // ----- helpers -----

        /// <summary>
        /// True iff every transformed block in the MB is EOB-only — i.e.
        /// the MB has no residual content and can be marked as skipped.
        /// libvpx's per-MB skip detection uses the same condition (sum
        /// of EOBs across the 25 blocks == 0; we check the token list
        /// length equivalently).
        /// </summary>
        private static bool IsAllEob(MbEncodeResult r)
        {
            if (r.Y2Block.Count != 1) return false;
            for (int i = 0; i < 16; i++) if (r.YBlocks[i].Count != 1) return false;
            for (int i = 0; i < 4;  i++) if (r.UBlocks[i].Count != 1) return false;
            for (int i = 0; i < 4;  i++) if (r.VBlocks[i].Count != 1) return false;
            return true;
        }

        // RD mode-decision tuning for the inter-frame intra fallback.
        //
        // cost = SSE(source vs reconstruction) + RD_LAMBDA * tokenRate, with
        // a small additive bias toward inter (INTRA_MODE_PENALTY) so that
        // marginal MBs stay inter -- cheaper to signal and keeps the
        // inter-prediction chain intact. Distortion dominates the decision,
        // which is exactly what halts high-motion error accumulation: an MB
        // that ZEROMV reconstructs far from the source (large SSE) loses
        // decisively to its DC_PRED intra refresh, so stale content cannot
        // ghost through and compound across inter frames. The inter-skippable
        // shortcut in the encode loop means this is only consulted for MBs
        // that actually code a residual.
        private const long INTER_RD_LAMBDA = 24;
        private const long INTRA_MODE_PENALTY = 2000;

        private static long ModeRdCost(byte[] srcY, byte[] srcU, byte[] srcV, MbEncodeResult r, bool isIntra)
        {
            long sse = Sse(srcY, r.ReconY, 256) + Sse(srcU, r.ReconU, 64) + Sse(srcV, r.ReconV, 64);
            long rate = TokenCount(r);
            long cost = sse + INTER_RD_LAMBDA * rate;
            if (isIntra) cost += INTRA_MODE_PENALTY;
            return cost;
        }

        private static long Sse(byte[] src, byte[] recon, int n)
        {
            long sum = 0;
            for (int i = 0; i < n; i++) { int d = src[i] - recon[i]; sum += (long)d * d; }
            return sum;
        }

        private static int TokenCount(MbEncodeResult r)
        {
            int c = r.Y2Block.Count;
            for (int b = 0; b < 16; b++) c += r.YBlocks[b].Count;
            for (int b = 0; b < 4;  b++) c += r.UBlocks[b].Count;
            for (int b = 0; b < 4;  b++) c += r.VBlocks[b].Count;
            return c;
        }

        private static void ExtractPlaneInto(byte[] src, int srcStride, int x, int y, int w, int h, byte[] dst)
        {
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    dst[r * w + c] = src[(y + r) * srcStride + (x + c)];
        }

        private static void ExtractRowInto(byte[] src, int offset, int count, byte[] dst)
        {
            for (int i = 0; i < count; i++) dst[i] = src[offset + i];
        }

        private static void ExtractColumnInto(byte[] src, int srcStride, int columnIndex, int rows, byte[] dst)
        {
            for (int r = 0; r < rows; r++) dst[r] = src[r * srcStride + columnIndex];
        }

        private static void CopyRowOut(byte[] src, int srcStride, int srcRow,
            byte[] dst, int dstOffset, int count)
        {
            for (int i = 0; i < count; i++) dst[dstOffset + i] = src[srcRow * srcStride + i];
        }
    }
}
