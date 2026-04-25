//-----------------------------------------------------------------------------
// Filename: mb_encoder.cs
//
// Description: Per-macroblock encode pipeline for an intra-only DC_PRED
// VP8 keyframe. This is the orchestrator that ties together everything
// the previous PRs in the foundation series shipped:
//
//   - DC_PRED prediction (port of vp8/common/reconintra.c, just the
//     16x16 and 8x8 DC paths — written here for clarity rather than
//     pulling in the decoder's intraped.cs which wraps a fuller mode
//     dispatcher).
//   - Forward DCT + Walsh                 (PR 1, dct.cs)
//   - Quantize (regular_quantize_b)       (PR 1, quantize.cs)
//   - Coefficient tokenizer               (PR 3, tokenize.cs)
//   - Reconstruction (dequantize + idct)  (existing dequantize.cs +
//     idctllm.cs from the decoder side)
//
// What's implemented here is one function: EncodeMacroblockDcPred. It
// consumes a 16x16 source luma plane + two 8x8 source chroma planes
// for one macroblock, plus the 1-row-above-and-1-column-left context
// bytes, and produces:
//   - A 25-element TOKENEXTRA list[] (1 Y2 + 16 Y + 4 U + 4 V).
//   - A reconstructed copy of the macroblock (the bytes the decoder
//     would produce given the same bitstream).
//
// The function does NOT write any bits — that's PR 7's job. It also
// does NOT perform mode picking; the choice of DC_PRED is fixed.
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
    /// <summary>
    /// Single-macroblock encode result: token streams (one list per of the
    /// 25 transformed blocks the decoder reconstructs) plus the
    /// reconstructed macroblock pixels.
    ///
    /// Block ordering matches libvpx's MACROBLOCK.block[] layout:
    ///   - YBlocks[0..15]: 16 Y blocks (raster order, 4 per row).
    ///   - UBlocks[0..3] : 4 U blocks (raster order).
    ///   - VBlocks[0..3] : 4 V blocks (raster order).
    ///   - Y2Block       : the second-order Walsh-coded DC block.
    /// </summary>
    public sealed class MbEncodeResult
    {
        public List<TOKENEXTRA>[] YBlocks  = new List<TOKENEXTRA>[16];
        public List<TOKENEXTRA>[] UBlocks  = new List<TOKENEXTRA>[4];
        public List<TOKENEXTRA>[] VBlocks  = new List<TOKENEXTRA>[4];
        public List<TOKENEXTRA>  Y2Block  = new List<TOKENEXTRA>();

        /// <summary>Reconstructed 16x16 luma pixels, raster order.</summary>
        public byte[] ReconY = new byte[16 * 16];

        /// <summary>Reconstructed 8x8 U pixels, raster order.</summary>
        public byte[] ReconU = new byte[8 * 8];

        /// <summary>Reconstructed 8x8 V pixels, raster order.</summary>
        public byte[] ReconV = new byte[8 * 8];
    }

    public static class mb_encoder
    {
        /// <summary>
        /// Encode one 16x16 macroblock as keyframe-only, DC_PRED-only intra.
        ///
        /// Inputs:
        ///   srcY:   16*16 luma source bytes, raster order (row major).
        ///   srcU:   8*8 chroma U, raster order.
        ///   srcV:   8*8 chroma V, raster order.
        ///   aboveY: 16 luma bytes from the row immediately above this MB,
        ///           or null if this is the top row of the frame.
        ///   leftY:  16 luma bytes from the column immediately left of this
        ///           MB, or null if this is the left column.
        ///   aboveU/leftU/aboveV/leftV: the 8-byte chroma equivalents.
        ///   fq:     quantizer tables built by quantizer_init.BuildForQIndex.
        /// </summary>
        public static MbEncodeResult EncodeMacroblockDcPred(
            byte[] srcY, byte[] srcU, byte[] srcV,
            byte[] aboveY, byte[] leftY,
            byte[] aboveU, byte[] leftU,
            byte[] aboveV, byte[] leftV,
            FrameQuantizer fq)
        {
            if (srcY == null || srcY.Length != 256) throw new ArgumentException("srcY must be 16*16");
            if (srcU == null || srcU.Length != 64)  throw new ArgumentException("srcU must be 8*8");
            if (srcV == null || srcV.Length != 64)  throw new ArgumentException("srcV must be 8*8");
            if (fq == null) throw new ArgumentNullException(nameof(fq));

            var result = new MbEncodeResult();

            // ---- Step 1: DC predictions ----
            byte yPred = DcPred16x16(aboveY, leftY);
            byte uPred = DcPred8x8(aboveU, leftU);
            byte vPred = DcPred8x8(aboveV, leftV);

            // ---- Step 2: residual = source - prediction ----
            short[] residY = SubtractFlat(srcY, yPred);
            short[] residU = SubtractFlat(srcU, uPred);
            short[] residV = SubtractFlat(srcV, vPred);

            // ---- Step 3: forward DCT for each 4x4 block ----
            // Y: 16 blocks of 4x4 carved out of the 16x16 plane.
            short[][] yCoef = new short[16][];
            for (int by = 0; by < 4; by++)
            for (int bx = 0; bx < 4; bx++)
            {
                int blkIdx = by * 4 + bx;
                yCoef[blkIdx] = Fdct4x4FromPlane(residY, srcStride: 16,
                    blockOffsetX: bx * 4, blockOffsetY: by * 4);
            }
            // U / V: 4 blocks of 4x4 each.
            short[][] uCoef = new short[4][];
            short[][] vCoef = new short[4][];
            for (int by = 0; by < 2; by++)
            for (int bx = 0; bx < 2; bx++)
            {
                int blkIdx = by * 2 + bx;
                uCoef[blkIdx] = Fdct4x4FromPlane(residU, srcStride: 8,
                    blockOffsetX: bx * 4, blockOffsetY: by * 4);
                vCoef[blkIdx] = Fdct4x4FromPlane(residV, srcStride: 8,
                    blockOffsetX: bx * 4, blockOffsetY: by * 4);
            }

            // ---- Step 4: build Y2 block from the 16 Y DCs and Walsh-transform ----
            // For 16x16 intra modes (DC_PRED, V_PRED, H_PRED, TM_PRED) the
            // 16 Y DC coefficients are themselves transformed by a 4x4 Walsh
            // and then quantized as a "Y2" second-order block. The 16 Y AC
            // blocks then drop their DC (zero out coef[0]).
            short[] y2In = new short[16];
            for (int i = 0; i < 16; i++) y2In[i] = yCoef[i][0];
            short[] y2Coef = Walsh4x4(y2In);

            // Zero out the DC of each Y AC block.
            for (int i = 0; i < 16; i++) yCoef[i][0] = 0;

            // ---- Step 5: quantize ----
            short[][] yQ = new short[16][], yDQ = new short[16][];
            int[] yEob = new int[16];
            for (int i = 0; i < 16; i++)
            {
                yQ[i] = new short[16]; yDQ[i] = new short[16];
                yEob[i] = QuantizeBlock(yCoef[i], fq.Y1, yQ[i], yDQ[i]);
            }

            short[] y2Q = new short[16], y2DQ = new short[16];
            int y2Eob = QuantizeBlock(y2Coef, fq.Y2, y2Q, y2DQ);

            short[][] uQ = new short[4][], uDQ = new short[4][];
            short[][] vQ = new short[4][], vDQ = new short[4][];
            int[] uEob = new int[4], vEob = new int[4];
            for (int i = 0; i < 4; i++)
            {
                uQ[i] = new short[16]; uDQ[i] = new short[16];
                uEob[i] = QuantizeBlock(uCoef[i], fq.UV, uQ[i], uDQ[i]);
                vQ[i] = new short[16]; vDQ[i] = new short[16];
                vEob[i] = QuantizeBlock(vCoef[i], fq.UV, vQ[i], vDQ[i]);
            }

            // ---- Step 6: tokenize, with per-block above/left entropy contexts ----
            //
            // VP8 tracks an "above" context byte per column-position and a
            // "left" context byte per row-position. Each transformed block
            // reads its initial context = (aboveCtx | leftCtx) (bool OR; both
            // represented as 0 or 1) and the bit it writes (zero block vs
            // non-zero) updates BOTH the above and the left slots used for
            // its position. The slot indices come from vp8_block2above and
            // vp8_block2left in entropy.cs.
            //
            // Within an MB the relevant slots are 0..3 for the 4 Y columns
            // (above) / 4 Y rows (left), 4..5 for U, 6..7 for V, 8 for Y2.
            //
            // Bug history: an earlier draft of this file passed
            // initialContext=0 to every block. That worked for all-zero
            // residual (every block tokenizes to a single EOB and the
            // context stays 0), but broke once any block carried a
            // non-zero coefficient — the encoder wrote with prob row 0 and
            // the decoder, computing context 1 from its own state machine,
            // read with prob row 1. Output: garbled chroma in the test.
            //
            // For now the above/left arrays are local to the MB; multi-MB
            // context propagation is a follow-up.
            byte[] aboveCtx = new byte[9];
            byte[] leftCtx = new byte[9];

            // Block type per libvpx:
            //   type 0 = Y AC (firstCoeffIndex = 1; DC went to Y2)
            //   type 1 = Y2
            //   type 2 = UV
            //   type 3 = Y with DC (no Y2, not used for DC_PRED 16x16)

            // -- Y2 block (block index 24) --
            int slot = entropy.vp8_block2above[24];   // = 8
            int slotL = entropy.vp8_block2left[24];   // = 8
            result.Y2Block = new List<TOKENEXTRA>();
            bool y2NonZero = tokenize.vp8_tokenize_block(y2Q,
                firstCoeffIndex: 0, eob: y2Eob,
                blockType: 1,
                initialContext: aboveCtx[slot] | leftCtx[slotL],
                coefProbs: default_coef_probs_c.default_coef_probs,
                output: result.Y2Block);
            aboveCtx[slot] = leftCtx[slotL] = (byte)(y2NonZero ? 1 : 0);

            // -- 16 Y AC blocks (indices 0..15) --
            for (int i = 0; i < 16; i++)
            {
                int s = entropy.vp8_block2above[i];
                int sL = entropy.vp8_block2left[i];
                result.YBlocks[i] = new List<TOKENEXTRA>();
                bool nz = tokenize.vp8_tokenize_block(yQ[i],
                    firstCoeffIndex: 1, eob: yEob[i],
                    blockType: 0,
                    initialContext: CombineCtx(aboveCtx[s], leftCtx[sL]),
                    coefProbs: default_coef_probs_c.default_coef_probs,
                    output: result.YBlocks[i]);
                aboveCtx[s] = leftCtx[sL] = (byte)(nz ? 1 : 0);
            }

            // -- 4 U blocks (indices 16..19) --
            for (int i = 0; i < 4; i++)
            {
                int blkIdx = 16 + i;
                int s = entropy.vp8_block2above[blkIdx];
                int sL = entropy.vp8_block2left[blkIdx];
                result.UBlocks[i] = new List<TOKENEXTRA>();
                bool nz = tokenize.vp8_tokenize_block(uQ[i],
                    firstCoeffIndex: 0, eob: uEob[i],
                    blockType: 2,
                    initialContext: CombineCtx(aboveCtx[s], leftCtx[sL]),
                    coefProbs: default_coef_probs_c.default_coef_probs,
                    output: result.UBlocks[i]);
                aboveCtx[s] = leftCtx[sL] = (byte)(nz ? 1 : 0);
            }

            // -- 4 V blocks (indices 20..23) --
            for (int i = 0; i < 4; i++)
            {
                int blkIdx = 20 + i;
                int s = entropy.vp8_block2above[blkIdx];
                int sL = entropy.vp8_block2left[blkIdx];
                result.VBlocks[i] = new List<TOKENEXTRA>();
                bool nz = tokenize.vp8_tokenize_block(vQ[i],
                    firstCoeffIndex: 0, eob: vEob[i],
                    blockType: 2,
                    initialContext: CombineCtx(aboveCtx[s], leftCtx[sL]),
                    coefProbs: default_coef_probs_c.default_coef_probs,
                    output: result.VBlocks[i]);
                aboveCtx[s] = leftCtx[sL] = (byte)(nz ? 1 : 0);
            }

            // ---- Step 7: reconstruct ----
            // Inverse Walsh on the Y2 block to recover the (quantized) DC of
            // each Y block. libvpx's inverse Walsh writes the per-block DC
            // into mb_dqcoeff[i*16].
            short[] y2InvOut = InverseWalsh4x4(y2DQ);
            for (int i = 0; i < 16; i++)
            {
                yDQ[i][0] = y2InvOut[i];
            }

            // For each Y block: idct(dq) + prediction -> reconstructed.
            for (int by = 0; by < 4; by++)
            for (int bx = 0; bx < 4; bx++)
            {
                int blkIdx = by * 4 + bx;
                IdctAndAddToPlane(yDQ[blkIdx], yPred, result.ReconY,
                    dstStride: 16, blockOffsetX: bx * 4, blockOffsetY: by * 4);
            }
            for (int by = 0; by < 2; by++)
            for (int bx = 0; bx < 2; bx++)
            {
                int blkIdx = by * 2 + bx;
                IdctAndAddToPlane(uDQ[blkIdx], uPred, result.ReconU,
                    dstStride: 8, blockOffsetX: bx * 4, blockOffsetY: by * 4);
                IdctAndAddToPlane(vDQ[blkIdx], vPred, result.ReconV,
                    dstStride: 8, blockOffsetX: bx * 4, blockOffsetY: by * 4);
            }

            return result;
        }

        // ---------- helpers ----------

        /// <summary>
        /// 16x16 DC_PRED. RFC 6386 §10.2: when both above and left are
        /// available, the prediction is the mean of the 32 boundary
        /// samples; when only one is available, just that 16; when neither,
        /// 128. The averages are rounded to nearest with the libvpx-style
        /// "+= half" rounding.
        /// </summary>
        private static byte DcPred16x16(byte[] above, byte[] left)
        {
            int avgAbove = 0, avgLeft = 0;
            int count = 0;
            if (above != null) { for (int i = 0; i < 16; i++) avgAbove += above[i]; count += 16; }
            if (left  != null) { for (int i = 0; i < 16; i++) avgLeft  += left[i];  count += 16; }
            if (count == 0) return 128;
            int sum = avgAbove + avgLeft;
            return (byte)((sum + count / 2) / count);
        }

        /// <summary>8x8 DC_PRED — same rules as the 16x16 case.</summary>
        private static byte DcPred8x8(byte[] above, byte[] left)
        {
            int avgAbove = 0, avgLeft = 0;
            int count = 0;
            if (above != null) { for (int i = 0; i < 8; i++) avgAbove += above[i]; count += 8; }
            if (left  != null) { for (int i = 0; i < 8; i++) avgLeft  += left[i];  count += 8; }
            if (count == 0) return 128;
            int sum = avgAbove + avgLeft;
            return (byte)((sum + count / 2) / count);
        }

        /// <summary>Subtracts a flat prediction value from each source byte.</summary>
        private static short[] SubtractFlat(byte[] src, byte pred)
        {
            var r = new short[src.Length];
            for (int i = 0; i < src.Length; i++) r[i] = (short)(src[i] - pred);
            return r;
        }

        /// <summary>
        /// Extract a 4x4 sub-block from <paramref name="plane"/> (raster order
        /// at <paramref name="srcStride"/>) into a contiguous 16-short
        /// buffer, then forward-DCT it.
        /// </summary>
        private static unsafe short[] Fdct4x4FromPlane(short[] plane, int srcStride, int blockOffsetX, int blockOffsetY)
        {
            short[] block = new short[16];
            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                block[r * 4 + c] = plane[(blockOffsetY + r) * srcStride + (blockOffsetX + c)];

            short[] coef = new short[16];
            fixed (short* inP = block)
            fixed (short* outP = coef)
            {
                dct.vp8_short_fdct4x4_c(inP, outP, pitch: 8);
            }
            return coef;
        }

        /// <summary>4x4 Walsh-Hadamard on a 16-element input.</summary>
        private static unsafe short[] Walsh4x4(short[] input)
        {
            short[] outBuf = new short[16];
            fixed (short* inP = input)
            fixed (short* outP = outBuf)
            {
                dct.vp8_short_walsh4x4_c(inP, outP, pitch: 8);
            }
            return outBuf;
        }

        /// <summary>
        /// Quantize a 16-coefficient block using the supplied tables, with
        /// zbin_extra = 0. Returns the EOB.
        /// </summary>
        private static unsafe int QuantizeBlock(short[] coef, QuantizerTables t, short[] qcoeff, short[] dqcoeff)
        {
            fixed (short* coefP = coef)
            fixed (short* zbinP = t.zbin)
            fixed (short* zboostP = t.zrun_zbin_boost)
            fixed (short* roundP = t.round)
            fixed (short* quantP = t.quant)
            fixed (short* qshiftP = t.quant_shift)
            fixed (short* dequantP = t.dequant)
            fixed (short* qP = qcoeff)
            fixed (short* dqP = dqcoeff)
            {
                return quantize.vp8_regular_quantize_b_arrays(
                    coefP, zbinP, zboostP, roundP, quantP, qshiftP, dequantP,
                    qP, dqP, zbin_extra: 0);
            }
        }

        /// <summary>
        /// Bit-exact port of libvpx's vp8_short_inv_walsh4x4_c for 16 inputs
        /// (the second-order inverse Walsh used in 16x16 intra modes).
        /// libvpx writes into an MB-wide dqcoeff buffer; we return a flat
        /// 16-element array of "DC values per Y block" instead, since the
        /// caller decides where to install them.
        /// </summary>
        private static short[] InverseWalsh4x4(short[] input)
        {
            // Adapted from idctllm.cs vp8_short_inv_walsh4x4_c — one buffer
            // pass that puts the recovered DC of each Y block into the
            // returned 16-element array (one per Y block).
            int[] tmp = new int[16];
            int a1, b1, c1, d1, a2, b2, c2, d2;

            // Stage 1 — rows.
            for (int i = 0; i < 4; i++)
            {
                a1 = input[i * 4 + 0] + input[i * 4 + 3];
                b1 = input[i * 4 + 1] + input[i * 4 + 2];
                c1 = input[i * 4 + 1] - input[i * 4 + 2];
                d1 = input[i * 4 + 0] - input[i * 4 + 3];

                tmp[i * 4 + 0] = a1 + b1;
                tmp[i * 4 + 1] = c1 + d1;
                tmp[i * 4 + 2] = a1 - b1;
                tmp[i * 4 + 3] = d1 - c1;
            }

            // Stage 2 — columns.
            short[] outBuf = new short[16];
            for (int i = 0; i < 4; i++)
            {
                a1 = tmp[0 * 4 + i] + tmp[3 * 4 + i];
                b1 = tmp[1 * 4 + i] + tmp[2 * 4 + i];
                c1 = tmp[1 * 4 + i] - tmp[2 * 4 + i];
                d1 = tmp[0 * 4 + i] - tmp[3 * 4 + i];

                a2 = a1 + b1;
                b2 = c1 + d1;
                c2 = a1 - b1;
                d2 = d1 - c1;

                outBuf[0 * 4 + i] = (short)((a2 + 3) >> 3);
                outBuf[1 * 4 + i] = (short)((b2 + 3) >> 3);
                outBuf[2 * 4 + i] = (short)((c2 + 3) >> 3);
                outBuf[3 * 4 + i] = (short)((d2 + 3) >> 3);
            }
            return outBuf;
        }

        /// <summary>
        /// Inverse-DCT a 16-element block, add a flat prediction, write the
        /// resulting bytes to a 4x4 sub-block of <paramref name="dstPlane"/>.
        /// Reuses the existing decoder-side idctllm.vp8_short_idct4x4llm_c.
        /// </summary>
        private static unsafe void IdctAndAddToPlane(short[] dqcoeff, byte pred,
            byte[] dstPlane, int dstStride, int blockOffsetX, int blockOffsetY)
        {
            // The decoder's idct adds to a prediction buffer and clamps to
            // [0, 255]. Build a 4x4 prediction buffer (all = pred), call
            // idct, then copy back to the destination plane.
            byte[] predBuf = new byte[16];
            for (int i = 0; i < 16; i++) predBuf[i] = pred;
            byte[] dstBuf = new byte[16];

            fixed (short* coefP = dqcoeff)
            fixed (byte* predP = predBuf)
            fixed (byte* dstP = dstBuf)
            {
                idctllm.vp8_short_idct4x4llm_c(coefP, predP, 4, dstP, 4);
            }

            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                dstPlane[(blockOffsetY + r) * dstStride + (blockOffsetX + c)] = dstBuf[r * 4 + c];
        }

        /// <summary>
        /// VP8_COMBINEENTROPYCONTEXTS in libvpx: returns (above != 0) +
        /// (left != 0), an int in {0, 1, 2} that selects which row of
        /// the 3-context probability table to use for the next block.
        /// </summary>
        private static int CombineCtx(byte above, byte left)
        {
            return (above != 0 ? 1 : 0) + (left != 0 ? 1 : 0);
        }
    }
}
