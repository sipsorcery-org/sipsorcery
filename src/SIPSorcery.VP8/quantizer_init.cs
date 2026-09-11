//-----------------------------------------------------------------------------
// Filename: quantizer_init.cs
//
// Description: Quantizer-table builder for the VP8 encoder. Port of the
// inner loop of libvpx vp8cx_init_quantizer (vp8/encoder/vp8_quantize.c)
// + the supporting invert_quant helper. Given a base Q index in [0..127]
// (and optional dc/ac quantizer deltas), produces the six 16-element
// per-block-type tables (zbin, round, quant, quant_shift, dequant,
// zrun_zbin_boost) the encoder needs for Y1, Y2, and UV transforms.
//
// libvpx caches the build for all 128 Q indices at once; this port
// builds a single Q's tables on demand. The numerical output is
// bit-exactly the same as libvpx's improved-quant path (the default).
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

using System;

namespace Vpx.Net
{
    /// <summary>
    /// One block-type's quantizer arrays (16 elements each, raster order).
    /// Mirrors the per-Q slice of libvpx's Y1quant/Y1zbin/etc. table.
    /// </summary>
    public sealed class QuantizerTables
    {
        public short[] zbin = new short[16];
        public short[] round = new short[16];
        public short[] quant = new short[16];
        public short[] quant_shift = new short[16];
        public short[] dequant = new short[16];
        public short[] zrun_zbin_boost = new short[16];
    }

    /// <summary>
    /// All three block-type quantizer-table sets for a given Q index.
    /// </summary>
    public sealed class FrameQuantizer
    {
        public QuantizerTables Y1;
        public QuantizerTables Y2;
        public QuantizerTables UV;
    }

    public static class quantizer_init
    {
        // -- Constants from libvpx/vp8/encoder/vp8_quantize.c ---------------

        // Per-zig-zag-position zbin boost: when no non-zero coefficient has
        // been seen yet within a zero-run, the threshold for the next
        // coefficient is bumped by zbin_boost[run_length] * dequant[1] >> 7.
        // Identical for Y1/Y2/UV.
        private static readonly int[] s_zbinBoost = {
            0, 0, 8, 10, 12, 14, 16, 20, 24, 28, 32, 36, 40, 44, 44, 44,
        };

        // qrounding_factors / qzbin_factors (libvpx). 129 entries; the
        // encoder only ever uses indices 0..127. The Y2 variants are
        // identical in libvpx so they're shared here.
        private static readonly int[] s_qroundingFactors = MakeFlat129(48);

        private static readonly int[] s_qzbinFactors = MakeQzbinFactors();

        private static int[] MakeFlat129(int v)
        {
            var a = new int[129];
            for (int i = 0; i < 129; i++) a[i] = v;
            return a;
        }

        private static int[] MakeQzbinFactors()
        {
            // 84 for QIndex < 48, 80 for QIndex >= 48 (extends to 128).
            var a = new int[129];
            for (int i = 0; i < 48; i++) a[i] = 84;
            for (int i = 48; i < 129; i++) a[i] = 80;
            return a;
        }

        // -- invert_quant -------------------------------------------------

        /// <summary>
        /// Bit-exact port of libvpx invert_quant in its "improved_quant"
        /// branch (the default). For dequant value <paramref name="d"/>,
        /// produces a (quant, shift) pair such that
        ///     y = (((x * quant) >> 16) + x) * shift) >> 16
        /// computes the same x/d division libvpx's regular_quantize_b uses.
        /// </summary>
        public static void InvertQuant(short d, out short quant, out short shift)
        {
            int t = d;
            int l;
            for (l = 0; t > 1; l++) t >>= 1;
            int m = 1 + (1 << (16 + l)) / d;
            quant = (short)(m - (1 << 16));
            shift = (short)(1 << (16 - l));
        }

        // -- Per-block-type build ----------------------------------------

        /// <summary>
        /// Build a single block-type's quantizer arrays for the given Q
        /// index, using the supplied DC and AC dequant lookups.
        /// </summary>
        private static QuantizerTables BuildOne(int Q, int dcDelta, int acDelta,
            Func<int, int, int> dcLookup, Func<int, int, int> acLookup)
        {
            var t = new QuantizerTables();

            // Position 0 is the DC coefficient.
            int qval = dcLookup(Q, dcDelta);
            InvertQuant((short)qval, out t.quant[0], out t.quant_shift[0]);
            t.zbin[0] = (short)(((s_qzbinFactors[Q] * qval) + 64) >> 7);
            t.round[0] = (short)((s_qroundingFactors[Q] * qval) >> 7);
            t.dequant[0] = (short)qval;
            t.zrun_zbin_boost[0] = (short)((qval * s_zbinBoost[0]) >> 7);

            // Position 1 is the first AC coefficient. Positions 2..15
            // share the same quant/shift/zbin/round/dequant as position 1
            // but get their own zrun_zbin_boost.
            qval = acLookup(Q, acDelta);
            InvertQuant((short)qval, out t.quant[1], out t.quant_shift[1]);
            t.zbin[1] = (short)(((s_qzbinFactors[Q] * qval) + 64) >> 7);
            t.round[1] = (short)((s_qroundingFactors[Q] * qval) >> 7);
            t.dequant[1] = (short)qval;
            t.zrun_zbin_boost[1] = (short)((qval * s_zbinBoost[1]) >> 7);

            for (int i = 2; i < 16; i++)
            {
                t.quant[i] = t.quant[1];
                t.quant_shift[i] = t.quant_shift[1];
                t.zbin[i] = t.zbin[1];
                t.round[i] = t.round[1];
                t.dequant[i] = t.dequant[1];
                t.zrun_zbin_boost[i] = (short)((t.dequant[1] * s_zbinBoost[i]) >> 7);
            }
            return t;
        }

        /// <summary>
        /// Build the full Y1/Y2/UV quantizer-table set for the given Q
        /// index and quantizer deltas. Use deltas of 0 for the simple
        /// keyframe-only encoder; the deltas are written into the
        /// compressed frame header by bitstream.StartKeyframeHeader.
        /// </summary>
        public static FrameQuantizer BuildForQIndex(
            int qIndex,
            int y1dcDelta = 0,
            int y2dcDelta = 0, int y2acDelta = 0,
            int uvdcDelta = 0, int uvacDelta = 0)
        {
            if (qIndex < 0 || qIndex > 127)
                throw new ArgumentOutOfRangeException(nameof(qIndex), qIndex, "Q index must be 0..127.");

            return new FrameQuantizer
            {
                Y1 = BuildOne(qIndex, y1dcDelta, 0,
                    quant_common.vp8_dc_quant,
                    (q, _) => quant_common.vp8_ac_yquant(q)),
                Y2 = BuildOne(qIndex, y2dcDelta, y2acDelta,
                    quant_common.vp8_dc2quant,
                    quant_common.vp8_ac2quant),
                UV = BuildOne(qIndex, uvdcDelta, uvacDelta,
                    quant_common.vp8_dc_uv_quant,
                    quant_common.vp8_ac_uv_quant),
            };
        }
    }
}
