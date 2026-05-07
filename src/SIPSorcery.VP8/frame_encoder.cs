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
// keyframe path (and the analogous inter path): uncompressed frame tag,
// start code / dimensions, compressed first partition (bc0), then one or
// more token partitions.
//
// Token partitions (log2_nbr_of_dct_partitions in 0..3 -> 1 / 2 / 4 / 8):
//   When log2 is 0, token data is a single bool-coded stream immediately
//   after partition 0 (legacy single-partition layout).
//   When log2 > 0, (N-1)*3 bytes of little-endian 24-bit sizes follow
//   partition 0, then N partition bodies are concatenated (MB row r is
//   assigned to partition r & (N-1)). The frame tag's first partition
//   length excludes those token partitions.
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
//                log2_nbr_of_dct_partitions (0..3)
//                base_qindex
//                5x put_delta_q
//                refresh_entropy_probs = 1
//                ★ coef-prob updates  -- 1056 zero bits ("no update")
//                ★ mb_no_skip_coeff = 1
//                ★ prob_skip_false (8-bit literal)
//                ★ for each MB: skip-flag bit + keyframe Y mode + UV mode tree paths
//   [token data]  One partition (log2 = 0) or size table + N partitions (log2 > 0):
//                for each non-skipped MB: Y2, 16 Y, 4 U, 4 V token streams
//                (raster order within each partition's row set).
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
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
        // Reset()'d before reuse.
        public MbEncodeResult[] MbResults;
        public bool[] MbSkip;

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

        // Inter MB prediction (256/64/64) — avoids resizing Above* neighbours.
        public byte[] PredY = new byte[256];
        public byte[] PredU = new byte[64];
        public byte[] PredV = new byte[64];

        // Per-MB source slices.
        public byte[] MbY = new byte[256];
        public byte[] MbU = new byte[64];
        public byte[] MbV = new byte[64];

        // Output buffer.
        public byte[] OutBuf;

        // Per-token-partition output buffers (only allocated when
        // log2NumTokenPartitions > 0; null otherwise so the single-
        // partition path stays allocation-free). Lazily sized in
        // <see cref="EnsureForFrame(int, int, int)"/>.
        public byte[][] PartitionBufs;

        // Reused length array for multi-partition token pack (N <= 8).
        public int[] PartitionLengthsScratch;

        // Reference-frame storage.
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

        public void EnsureForFrame(int width, int height) =>
            EnsureForFrame(width, height, log2NumTokenPartitions: 0);

        public void EnsureForFrame(int width, int height, int log2NumTokenPartitions)
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
            if (MbSkip == null || MbSkip.Length < total) MbSkip = new bool[total];

            if (FrameAboveCtx == null || FrameAboveCtx.Length < mbCols * 9)
                FrameAboveCtx = new byte[mbCols * 9];

            if (AboveYRow == null || AboveYRow.Length < width)
                AboveYRow = new byte[width];
            if (AboveURow == null || AboveURow.Length < chromaW)
            {
                AboveURow = new byte[chromaW];
                AboveVRow = new byte[chromaW];
            }

            // Partition-1 token data grows with content but stays far below
            // this generous cap; bool-coder byte writes skip per-byte buffer
            // checks in Release (see boolhuff.vp8_encode_bool), relying on
            // this slack so the hot path stays allocation- and branch-light.
            int bufLen = System.Math.Max(4096, width * height * 2 + 1024);
            if (OutBuf == null || OutBuf.Length < bufLen)
                OutBuf = new byte[bufLen];

            // Per-partition output buffers for log2N > 0. Each partition
            // gets a worst-case-sized buffer to keep the bool-coder room
            // check free of partition-imbalance edge cases. The total
            // memory cost is bounded (N <= 8) and amortised across the
            // lifetime of the encoder.
            if (log2NumTokenPartitions > 0)
            {
                int n = 1 << log2NumTokenPartitions;
                int perPart = System.Math.Max(4096, width * height + 1024);
                if (PartitionBufs == null || PartitionBufs.Length < n)
                    PartitionBufs = new byte[n][];
                for (int i = 0; i < n; i++)
                    if (PartitionBufs[i] == null || PartitionBufs[i].Length < perPart)
                        PartitionBufs[i] = new byte[perPart];
            }

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
        /// <summary>Shared validation for <c>log2_nbr_of_dct_partitions</c> (0..3).</summary>
        internal static void ValidateLog2TokenPartitions(int log2NumTokenPartitions)
        {
            if ((uint)log2NumTokenPartitions > 3)
                throw new ArgumentOutOfRangeException(nameof(log2NumTokenPartitions),
                    log2NumTokenPartitions, "Must be 0..3 (1, 2, 4, or 8 partitions).");
        }

        // Per-thread reusable buffers. Most encoders are called from the
        // same thread for the lifetime of a stream, so this amortises the
        // per-frame allocations to (effectively) zero after the first
        // call.
        [System.ThreadStatic]
        private static FrameEncoderBuffers _buffers;
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
            int width, int height, int qIndex, FrameEncoderBuffers buffers,
            IEncoderMemoryOps memoryOps = null, int log2NumTokenPartitions = 0)
            => CopyPooled(EncodeKeyframeWithBuffersPooled(srcY, srcU, srcV, width, height, qIndex, buffers, memoryOps, log2NumTokenPartitions));

        /// <summary>Pooled variant of <see cref="EncodeKeyframeWithBuffers"/>; returns a slice over <see cref="FrameEncoderBuffers.OutBuf"/> (avoids allocating/copying the encoded frame blob; other small per-frame allocations may still occur).</summary>
        internal static ArraySegment<byte> EncodeKeyframeWithBuffersPooled(byte[] srcY, byte[] srcU, byte[] srcV,
            int width, int height, int qIndex, FrameEncoderBuffers buffers,
            IEncoderMemoryOps memoryOps = null, int log2NumTokenPartitions = 0)
        {
            if (buffers == null) throw new ArgumentNullException(nameof(buffers));
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

            memoryOps ??= LegacyEncoderMemoryOps.Instance;
            var py = new PlaneView(srcY, 0, width);
            var pu = new PlaneView(srcU, 0, width / 2);
            var pv = new PlaneView(srcV, 0, width / 2);
            return EncodeKeyframeWithBuffersPlanes(py, pu, pv, width, height, qIndex, buffers, memoryOps, log2NumTokenPartitions);
        }

        /// <summary>Allocates a fresh byte[] from a pooled segment. Used by the public byte[]-returning entry points.</summary>
        private static byte[] CopyPooled(ArraySegment<byte> seg)
        {
            byte[] result = new byte[seg.Count];
            Buffer.BlockCopy(seg.Array, seg.Offset, result, 0, seg.Count);
            return result;
        }

        /// <summary>Keyframe encode with Y/U/V stored contiguously in a single I420 buffer.</summary>
        internal static byte[] EncodeKeyframeWithBuffersContiguousI420(byte[] i420, int width, int height,
            int qIndex, FrameEncoderBuffers buffers, IEncoderMemoryOps memoryOps, int log2NumTokenPartitions = 0)
            => CopyPooled(EncodeKeyframeWithBuffersContiguousI420Pooled(i420, width, height, qIndex, buffers, memoryOps, log2NumTokenPartitions));

        /// <summary>Pooled variant of <see cref="EncodeKeyframeWithBuffersContiguousI420"/>; returns a slice over <see cref="FrameEncoderBuffers.OutBuf"/> (avoids allocating/copying the encoded frame blob; other small per-frame allocations may still occur).</summary>
        internal static ArraySegment<byte> EncodeKeyframeWithBuffersContiguousI420Pooled(byte[] i420, int width, int height,
            int qIndex, FrameEncoderBuffers buffers, IEncoderMemoryOps memoryOps, int log2NumTokenPartitions = 0)
        {
            if (buffers == null) throw new ArgumentNullException(nameof(buffers));
            if (memoryOps == null) throw new ArgumentNullException(nameof(memoryOps));
            if (width <= 0 || width % 16 != 0)
                throw new ArgumentException("width must be a positive multiple of 16", nameof(width));
            if (height <= 0 || height % 16 != 0)
                throw new ArgumentException("height must be a positive multiple of 16", nameof(height));
            int chromaW = width / 2, chromaH = height / 2;
            int ySize = width * height, cSize = chromaW * chromaH;
            if (i420 == null || i420.Length < ySize + 2 * cSize)
                throw new ArgumentException("i420 size mismatch", nameof(i420));
            var py = new PlaneView(i420, 0, width);
            var pu = new PlaneView(i420, ySize, chromaW);
            var pv = new PlaneView(i420, ySize + cSize, chromaW);
            return EncodeKeyframeWithBuffersPlanes(py, pu, pv, width, height, qIndex, buffers, memoryOps, log2NumTokenPartitions);
        }

        private readonly struct PlaneView
        {
            public readonly byte[] Buffer;
            public readonly int BaseOffset;
            public readonly int Stride;
            public PlaneView(byte[] buffer, int baseOffset, int stride)
            {
                Buffer = buffer;
                BaseOffset = baseOffset;
                Stride = stride;
            }
        }

        private static ArraySegment<byte> EncodeKeyframeWithBuffersPlanes(PlaneView srcY, PlaneView srcU, PlaneView srcV,
            int width, int height, int qIndex, FrameEncoderBuffers buffers, IEncoderMemoryOps memOps,
            int log2NumTokenPartitions = 0)
        {
            if (buffers == null) throw new System.ArgumentNullException(nameof(buffers));
            if (memOps == null) throw new ArgumentNullException(nameof(memOps));
            if (width <= 0 || width % 16 != 0)
                throw new ArgumentException("width must be a positive multiple of 16", nameof(width));
            if (height <= 0 || height % 16 != 0)
                throw new ArgumentException("height must be a positive multiple of 16", nameof(height));
            ValidateLog2TokenPartitions(log2NumTokenPartitions);
            if (srcY.Buffer == null || srcU.Buffer == null || srcV.Buffer == null)
                throw new ArgumentException("plane buffer is null");

            int chromaW = width / 2, chromaH = height / 2;

            int mbCols = width / 16;
            int mbRows = height / 16;

            // Build the quantizer for this Q index.
            FrameQuantizer fq = quantizer_init.BuildForQIndex(qIndex);

            // Use the caller-supplied per-codec scratch buffers.
            var buf = buffers;
            buf.EnsureForFrame(width, height, log2NumTokenPartitions);
            var scratch = buf.Scratch;

            var cfg = new KeyframeHeaderConfig
            {
                Width = width, Height = height,
                BaseQindex = qIndex,
                Log2NumberOfTokenPartitions = log2NumTokenPartitions,
            };

            byte[] outBuf = buf.OutBuf;

            // Pin outBuf for the entire encode. The BOOL_CODER stores a
            // raw byte* into the buffer, so after the original fixed-scope
            // pointer is released the GC could otherwise relocate the
            // array between bc0 / bc1 invocations and silently corrupt
            // the bitstream. (This was latent in the single-partition
            // path, but became reliably triggerable once the multi-
            // partition path added per-frame allocations large enough to
            // induce gen-0 compaction during encoding.)
            GCHandle outBufPin = GCHandle.Alloc(outBuf, GCHandleType.Pinned);
            try
            {

            // ----- Open the compressed first partition (header through refresh_entropy_probs) -----
            BOOL_CODER bc0 = new BOOL_CODER();
            int partitionStart;
            fixed (byte* p = outBuf)
            {
                using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.FirstPartitionHeader))
                {
                    partitionStart = bitstream.StartKeyframeHeader(p, outBuf.Length, cfg, ref bc0);

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
                }
            }

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
                    memOps.CopyPlaneRect(srcY.Buffer, srcY.BaseOffset, srcY.Stride, mbCol * 16, mbRow * 16, 16, 16, mbY);
                    memOps.CopyPlaneRect(srcU.Buffer, srcU.BaseOffset, srcU.Stride, mbCol * 8, mbRow * 8, 8, 8, mbU);
                    memOps.CopyPlaneRect(srcV.Buffer, srcV.BaseOffset, srcV.Stride, mbCol * 8, mbRow * 8, 8, 8, mbV);

                    // Slice the relevant 16-byte (resp 8-byte) above row.
                    bool haveAbove = mbRow > 0;
                    if (haveAbove)
                    {
                        memOps.CopyRowAt(aboveYRow, mbCol * 16, 16, aboveY);
                        memOps.CopyRowAt(aboveURow, mbCol * 8, 8, aboveU);
                        memOps.CopyRowAt(aboveVRow, mbCol * 8, 8, aboveV);
                    }

                    // Pull this MB column's above context out of the
                    // frame-scope buffer; mb_encoder mutates it in place
                    // as it processes blocks, then we copy it back.
                    int aboveBase = mbCol * 9;
                    using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.Phase1MbScalarCtx))
                    {
                        Buffer.BlockCopy(frameAboveCtx, aboveBase, aboveCtx, 0, 9);
                    }

                    int idx = mbRow * mbCols + mbCol;
                    var pooled = mbResults[idx];

                    var fuse = new LastFrameFuseTarget(buf.LastFrameY, buf.LastFrameU, buf.LastFrameV,
                        width, chromaW, mbCol, mbRow);

                    var r = mb_encoder.EncodeMacroblockDcPredImpl(
                        mbY, mbU, mbV,
                        haveAbove        ? aboveY : null,
                        haveLeftNeighbour ? leftY : null,
                        haveAbove        ? aboveU : null,
                        haveLeftNeighbour ? leftU : null,
                        haveAbove        ? aboveV : null,
                        haveLeftNeighbour ? leftV : null,
                        fq,
                        aboveCtx, leftCtx,
                        pooled, scratch,
                        memOps,
                        fuse);
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
                    using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.Phase1MbScalarCtx))
                    {
                        bool skip = IsAllEob(r);
                        mbSkip[idx] = skip;
                        if (skip) skipTrueCount++; else skipFalseCount++;

                        // The mutated aboveCtx is the new "above" for the MB
                        // immediately below this one (same column, next row).
                        Buffer.BlockCopy(aboveCtx, 0, frameAboveCtx, aboveBase, 9);
                    }

                    // Update neighbour context for the next MB in this row
                    // (rightmost column of the MB's reconstruction) and for
                    // the next row (bottommost row).
                    memOps.CopyColumn(buf.LastFrameY, width, mbCol * 16 + 15, mbRow * 16, 16, leftY);
                    memOps.CopyColumn(buf.LastFrameU, chromaW, mbCol * 8 + 7, mbRow * 8, 8, leftU);
                    memOps.CopyColumn(buf.LastFrameV, chromaW, mbCol * 8 + 7, mbRow * 8, 8, leftV);
                    haveLeftNeighbour = true;

                    memOps.CopyRowFrom2d(buf.LastFrameY, width, mbRow * 16 + 15, mbCol * 16,
                               aboveYRow, dstOffset: mbCol * 16, count: 16);
                    memOps.CopyRowFrom2d(buf.LastFrameU, chromaW, mbRow * 8 + 7, mbCol * 8,
                               aboveURow, dstOffset: mbCol * 8, count: 8);
                    memOps.CopyRowFrom2d(buf.LastFrameV, chromaW, mbRow * 8 + 7, mbCol * 8,
                               aboveVRow, dstOffset: mbCol * 8, count: 8);
                }
            }

            // ----- Last-frame reference was written during MB encode (fused idct) -----
            buf.LastFrameValid = true;

            // ----- Phase 2: partition-0 bits after phase 1 (before token partition) -----
            using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.Phase2FirstPartitionBits))
            {
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

            // ----- Phase 4: token partition(s) -----
            int totalBytes;
            if (log2NumTokenPartitions == 0)
            {
                BOOL_CODER bc1 = new BOOL_CODER();
                fixed (byte* p = outBuf)
                {
                    boolhuff.vp8_start_encode(ref bc1, p + totalThroughP0, p + outBuf.Length);

                    using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.PackTokens))
                    {
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
                    }

                    boolhuff.vp8_stop_encode(ref bc1);
                }

                totalBytes = totalThroughP0 + (int)bc1.pos;
            }
            else
            {
                totalBytes = PackMultiPartitionTokens(buf, outBuf, totalThroughP0,
                    mbResults, mbSkip, mbRows, mbCols, log2NumTokenPartitions);
            }
            return new ArraySegment<byte>(outBuf, 0, totalBytes);
            }
            finally { outBufPin.Free(); }
        }

        /// <summary>
        /// Pack tokens into <c>1 &lt;&lt; log2NumTokenPartitions</c> token partitions in
        /// parallel, then write the partition size table (after partition 0)
        /// and concatenated partitions into <paramref name="outBuf"/>.
        /// Partitions are interleaved by row (row r belongs to partition
        /// r &amp; (N-1)) so each partition can be encoded independently.
        /// </summary>
        private static int PackMultiPartitionTokens(FrameEncoderBuffers buf, byte[] outBuf, int totalThroughP0,
            MbEncodeResult[] mbResults, bool[] mbSkip, int mbRows, int mbCols, int log2NumTokenPartitions)
        {
            int n = 1 << log2NumTokenPartitions;
            if (buf.PartitionLengthsScratch == null || buf.PartitionLengthsScratch.Length < n)
                buf.PartitionLengthsScratch = new int[n];
            int[] partitionLengths = buf.PartitionLengthsScratch;

            using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.PackTokens))
            {
                Parallel.For(0, n, p =>
                {
                    partitionLengths[p] = PackSinglePartition(p, n, mbRows, mbCols,
                        mbResults, mbSkip, buf.PartitionBufs[p]);
                });
            }

            // ----- Stitch: size table + concatenated partitions -----
            int writePos = totalThroughP0;
            // (N-1) 24-bit little-endian sizes for partitions 1..N-1.
            for (int p = 0; p < n - 1; p++)
            {
                int sz = partitionLengths[p];
                outBuf[writePos++] = (byte)(sz & 0xFF);
                outBuf[writePos++] = (byte)((sz >> 8) & 0xFF);
                outBuf[writePos++] = (byte)((sz >> 16) & 0xFF);
            }
            // Partition bytes in order: 0, 1, ..., N-1.
            for (int p = 0; p < n; p++)
            {
                int len = partitionLengths[p];
                if (len > 0)
                {
                    Buffer.BlockCopy(buf.PartitionBufs[p], 0, outBuf, writePos, len);
                    writePos += len;
                }
            }
            return writePos;
        }

        /// <summary>
        /// Pack one token partition: walks every MB whose
        /// <c>(row &amp; (N-1)) == partitionIndex</c> and emits its
        /// Y2 + 16 Y + 4 U + 4 V token streams through an independent
        /// <see cref="BOOL_CODER"/>. Returns the byte length of the written
        /// partition body.
        /// </summary>
        private static int PackSinglePartition(int partitionIndex, int n, int mbRows, int mbCols,
            MbEncodeResult[] mbResults, bool[] mbSkip, byte[] partBuf)
        {
            BOOL_CODER bc = new BOOL_CODER();
            fixed (byte* pb = partBuf)
            {
                boolhuff.vp8_start_encode(ref bc, pb, pb + partBuf.Length);

                for (int row = partitionIndex; row < mbRows; row += n)
                {
                    int rowBase = row * mbCols;
                    for (int col = 0; col < mbCols; col++)
                    {
                        int i = rowBase + col;
                        if (mbSkip[i]) continue;

                        var r = mbResults[i];
                        bitstream.vp8_pack_tokens(ref bc, r.Y2Block);
                        for (int b = 0; b < 16; b++) bitstream.vp8_pack_tokens(ref bc, r.YBlocks[b]);
                        for (int b = 0; b < 4;  b++) bitstream.vp8_pack_tokens(ref bc, r.UBlocks[b]);
                        for (int b = 0; b < 4;  b++) bitstream.vp8_pack_tokens(ref bc, r.VBlocks[b]);
                    }
                }

                boolhuff.vp8_stop_encode(ref bc);
            }
            return (int)bc.pos;
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
        /// <returns>The encoded VP8 inter frame bytes.</returns>
        internal static byte[] EncodeInterFrameWithBuffers(byte[] srcY, byte[] srcU, byte[] srcV,
            int width, int height, int qIndex, FrameEncoderBuffers buffers, IEncoderMemoryOps memoryOps = null,
            int log2NumTokenPartitions = 0)
            => CopyPooled(EncodeInterFrameWithBuffersPooled(srcY, srcU, srcV, width, height, qIndex, buffers, memoryOps, log2NumTokenPartitions));

        /// <summary>Pooled variant of <see cref="EncodeInterFrameWithBuffers"/>; returns a slice over <see cref="FrameEncoderBuffers.OutBuf"/> (avoids allocating/copying the encoded frame blob; other small per-frame allocations may still occur).</summary>
        internal static ArraySegment<byte> EncodeInterFrameWithBuffersPooled(byte[] srcY, byte[] srcU, byte[] srcV,
            int width, int height, int qIndex, FrameEncoderBuffers buffers, IEncoderMemoryOps memoryOps = null,
            int log2NumTokenPartitions = 0)
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

            memoryOps ??= LegacyEncoderMemoryOps.Instance;
            var py = new PlaneView(srcY, 0, width);
            var pu = new PlaneView(srcU, 0, width / 2);
            var pv = new PlaneView(srcV, 0, width / 2);
            return EncodeInterFrameWithBuffersPlanes(py, pu, pv, width, height, qIndex, buffers, memoryOps, log2NumTokenPartitions);
        }

        internal static byte[] EncodeInterFrameWithBuffersContiguousI420(byte[] i420, int width, int height,
            int qIndex, FrameEncoderBuffers buffers, IEncoderMemoryOps memoryOps, int log2NumTokenPartitions = 0)
            => CopyPooled(EncodeInterFrameWithBuffersContiguousI420Pooled(i420, width, height, qIndex, buffers, memoryOps, log2NumTokenPartitions));

        /// <summary>Pooled variant of <see cref="EncodeInterFrameWithBuffersContiguousI420"/>; returns a slice over <see cref="FrameEncoderBuffers.OutBuf"/> (avoids allocating/copying the encoded frame blob; other small per-frame allocations may still occur).</summary>
        internal static ArraySegment<byte> EncodeInterFrameWithBuffersContiguousI420Pooled(byte[] i420, int width, int height,
            int qIndex, FrameEncoderBuffers buffers, IEncoderMemoryOps memoryOps, int log2NumTokenPartitions = 0)
        {
            if (buffers == null) throw new ArgumentNullException(nameof(buffers));
            if (memoryOps == null) throw new ArgumentNullException(nameof(memoryOps));
            if (width <= 0 || width % 16 != 0)
                throw new ArgumentException("width must be a positive multiple of 16", nameof(width));
            if (height <= 0 || height % 16 != 0)
                throw new ArgumentException("height must be a positive multiple of 16", nameof(height));
            int chromaW = width / 2, chromaH = height / 2;
            int ySize = width * height, cSize = chromaW * chromaH;
            if (i420 == null || i420.Length < ySize + 2 * cSize)
                throw new ArgumentException("i420 size mismatch", nameof(i420));
            var py = new PlaneView(i420, 0, width);
            var pu = new PlaneView(i420, ySize, chromaW);
            var pv = new PlaneView(i420, ySize + cSize, chromaW);
            return EncodeInterFrameWithBuffersPlanes(py, pu, pv, width, height, qIndex, buffers, memoryOps, log2NumTokenPartitions);
        }

        private static ArraySegment<byte> EncodeInterFrameWithBuffersPlanes(PlaneView srcY, PlaneView srcU, PlaneView srcV,
            int width, int height, int qIndex, FrameEncoderBuffers buffers, IEncoderMemoryOps memOps,
            int log2NumTokenPartitions = 0)
        {
            if (memOps == null) throw new ArgumentNullException(nameof(memOps));
            if (width <= 0 || width % 16 != 0)
                throw new ArgumentException("width must be a positive multiple of 16", nameof(width));
            if (height <= 0 || height % 16 != 0)
                throw new ArgumentException("height must be a positive multiple of 16", nameof(height));
            ValidateLog2TokenPartitions(log2NumTokenPartitions);
            if (srcY.Buffer == null || srcU.Buffer == null || srcV.Buffer == null)
                throw new ArgumentException("plane buffer is null");

            int mbCols = width / 16;
            int mbRows = height / 16;
            int chromaW = width / 2;

            FrameQuantizer fq = quantizer_init.BuildForQIndex(qIndex);

            var buf = buffers;
            buf.EnsureForFrame(width, height, log2NumTokenPartitions);
            var scratch = buf.Scratch;

            if (!buf.LastFrameValid)
            {
                throw new InvalidOperationException(
                    "EncodeInterFrame requires a valid LAST_FRAME reference. Call EncodeKeyframe first.");
            }

            var cfg = new InterFrameHeaderConfig
            {
                BaseQindex = qIndex,
                Log2NumberOfTokenPartitions = log2NumTokenPartitions,
            };

            byte[] outBuf = buf.OutBuf;

            // Pin outBuf for the entire encode (see EncodeKeyframeWithBuffersPlanes
            // for the rationale).
            GCHandle outBufPin = GCHandle.Alloc(outBuf, GCHandleType.Pinned);
            try
            {

            // ----- Open compressed first partition (header through refresh_last_frame) -----
            BOOL_CODER bc0 = new BOOL_CODER();
            int partitionStart;
            fixed (byte* p = outBuf)
            {
                using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.FirstPartitionHeader))
                {
                    partitionStart = bitstream.StartInterFrameHeader(p, outBuf.Length, cfg, ref bc0);

                    // ----- Coef-prob updates: 1056 zero flag bits (no updates) -----
                    for (int t = 0; t < entropy.BLOCK_TYPES; t++)
                        for (int b = 0; b < entropy.COEF_BANDS; b++)
                            for (int c = 0; c < entropy.PREV_COEF_CONTEXTS; c++)
                                for (int n = 0; n < entropy.ENTROPY_NODES; n++)
                                    boolhuff.vp8_encode_bool(ref bc0, 0,
                                        coefupdateprobs.vp8_coef_update_probs[t, b, c, n]);
                }
            }

            // ----- Phase 1: encode all MBs into MbEncodeResults -----
            //
            // Same two-phase split as the keyframe path: encode every MB
            // first so we know skip flags before writing prob_skip_false
            // and the per-MB skip bits. For the ZEROMV-everywhere model
            // the prediction for each MB is the same-position 16x16 Y +
            // 8x8 U + 8x8 V samples from buf.LastFrameY/U/V.

            MbEncodeResult[] mbResults = buf.MbResults;
            bool[] mbSkip = buf.MbSkip;
            int skipTrueCount = 0;
            int skipFalseCount = 0;

            byte[] frameAboveCtx = buf.FrameAboveCtx;
            Array.Clear(frameAboveCtx, 0, mbCols * 9);

            byte[] leftCtx = buf.LeftCtx;
            byte[] aboveCtx = buf.AboveCtx;
            byte[] mbY = buf.MbY;
            byte[] mbU = buf.MbU;
            byte[] mbV = buf.MbV;

            byte[] predY = buf.PredY;
            byte[] predU = buf.PredU;
            byte[] predV = buf.PredV;

            for (int mbRow = 0; mbRow < mbRows; mbRow++)
            {
                Array.Clear(leftCtx, 0, 9);

                for (int mbCol = 0; mbCol < mbCols; mbCol++)
                {
                    memOps.CopyPlaneRect(srcY.Buffer, srcY.BaseOffset, srcY.Stride, mbCol * 16, mbRow * 16, 16, 16, mbY);
                    memOps.CopyPlaneRect(srcU.Buffer, srcU.BaseOffset, srcU.Stride, mbCol * 8, mbRow * 8, 8, 8, mbU);
                    memOps.CopyPlaneRect(srcV.Buffer, srcV.BaseOffset, srcV.Stride, mbCol * 8, mbRow * 8, 8, 8, mbV);

                    memOps.CopyPlaneRect(buf.LastFrameY, 0, width, mbCol * 16, mbRow * 16, 16, 16, predY);
                    memOps.CopyPlaneRect(buf.LastFrameU, 0, chromaW, mbCol * 8, mbRow * 8, 8, 8, predU);
                    memOps.CopyPlaneRect(buf.LastFrameV, 0, chromaW, mbCol * 8, mbRow * 8, 8, 8, predV);

                    // Pull this MB column's above context.
                    int aboveBase = mbCol * 9;
                    using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.Phase1MbScalarCtx))
                    {
                        Buffer.BlockCopy(frameAboveCtx, aboveBase, aboveCtx, 0, 9);
                    }

                    int idx = mbRow * mbCols + mbCol;
                    var pooled = mbResults[idx];

                    var fuseInter = new LastFrameFuseTarget(buf.LastFrameY, buf.LastFrameU, buf.LastFrameV,
                        width, chromaW, mbCol, mbRow);

                    var r = mb_encoder.EncodeMacroblockZeroMvLastImpl(
                        mbY, mbU, mbV,
                        predY, predU, predV,
                        fq,
                        aboveCtx, leftCtx,
                        pooled, scratch,
                        memOps,
                        fuseInter);
                    mbResults[idx] = r;

                    using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.Phase1MbScalarCtx))
                    {
                        bool skip = IsAllEob(r);
                        mbSkip[idx] = skip;
                        if (skip) skipTrueCount++; else skipFalseCount++;

                        Buffer.BlockCopy(aboveCtx, 0, frameAboveCtx, aboveBase, 9);
                    }
                }
            }

            buf.LastFrameValid = true;

            // ----- Phase 2: partition-0 tail before token partition -----
            using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.Phase2FirstPartitionBits))
            {
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

                // ----- Phase 2b: inter-only prob_intra / prob_last / prob_gf -----
                //
                // Choices for the ZEROMV-everywhere model:
                //   prob_intra = 1   -> writing is_inter=1 costs ~0 bits.
                //   prob_last  = 1   -> writing ref_is_LAST=0 costs ~0 bits.
                //   prob_gf    = 128 -> never used (ref is always LAST).
                const byte probIntra = 1;
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

                // ----- Phase 2c: per-MB skip flag + ref_frame + inter mode -----
                //
                // The inter mode tree probabilities depend on a per-MB
                // neighbour-MV-counting context cnt[CNT_INTRA]. The decoder
                // (decodemv.read_mb_modes_mv) accumulates this context by
                // walking the above, left and aboveleft neighbours: each
                // non-intra neighbour with a zero MV adds either 2
                // (above/left) or 1 (aboveleft) to cnt[CNT_INTRA].
                //
                // For an all-ZEROMV LAST_FRAME stream the cnt[CNT_INTRA]
                // value is determined entirely by the MB's position, since
                // every inter MB has ref_frame = LAST and mv.as_int = 0:
                //   (0,0)        -> 0 (no inter neighbours)
                //   (0,c) c>0    -> 2 (only left contributes)
                //   (r,0) r>0    -> 2 (only above contributes)
                //   (r,c) r>0,c>0 -> 5 (above+left+aboveleft all contribute: 2+2+1)
                //
                // We pre-build the three context rows we'll need and pick
                // the right one per MB, then walk the inter mode tree with
                // the decoder-matching row.
                byte[] modeProbsRow0 = new byte[4];
                byte[] modeProbsRow2 = new byte[4];
                byte[] modeProbsRow5 = new byte[4];
                for (int j = 0; j < 4; j++)
                {
                    modeProbsRow0[j] = (byte)modecont.vp8_mode_contexts[0, j];
                    modeProbsRow2[j] = (byte)modecont.vp8_mode_contexts[2, j];
                    modeProbsRow5[j] = (byte)modecont.vp8_mode_contexts[5, j];
                }

                for (int mbRow = 0; mbRow < mbRows; mbRow++)
                {
                    for (int mbCol = 0; mbCol < mbCols; mbCol++)
                    {
                        int i = mbRow * mbCols + mbCol;

                        // Skip flag (boolean coder, prob_skip_false).
                        boolhuff.vp8_encode_bool(ref bc0, mbSkip[i] ? 1 : 0, probSkipFalse);

                        // Pick the context-correct mode prob row.
                        byte[] modeProbs;
                        if (mbRow == 0 && mbCol == 0)       modeProbs = modeProbsRow0;
                        else if (mbRow == 0 || mbCol == 0)  modeProbs = modeProbsRow2;
                        else                                modeProbs = modeProbsRow5;

                        // is_inter=1, ref_is_LAST=0, ZEROMV tree path.
                        bitstream.WriteInterMbRefAndMode(
                            ref bc0,
                            probIntra, probLast, probGf,
                            MV_REFERENCE_FRAME.LAST_FRAME,
                            modeProbs,
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

            // ----- Phase 4: token partition(s) -----
            int totalBytes;
            if (log2NumTokenPartitions == 0)
            {
                BOOL_CODER bc1 = new BOOL_CODER();
                fixed (byte* p = outBuf)
                {
                    boolhuff.vp8_start_encode(ref bc1, p + totalThroughP0, p + outBuf.Length);

                    int totalMbs = mbRows * mbCols;
                    using (new EncodeProfiler.Scope(Vp8EncodeProfilePhase.PackTokens))
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

                totalBytes = totalThroughP0 + (int)bc1.pos;
            }
            else
            {
                totalBytes = PackMultiPartitionTokens(buf, outBuf, totalThroughP0,
                    mbResults, mbSkip, mbRows, mbCols, log2NumTokenPartitions);
            }
            return new ArraySegment<byte>(outBuf, 0, totalBytes);
            }
            finally { outBufPin.Free(); }
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
            int width, int height, int qIndex)
        {
            return EncodeInterFrameWithBuffers(srcY, srcU, srcV, width, height, qIndex,
                _buffers ??= new FrameEncoderBuffers());
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

    }
}
