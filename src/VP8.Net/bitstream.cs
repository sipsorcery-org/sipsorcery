//-----------------------------------------------------------------------------
// Filename: bitstream.cs
//
// Description: Keyframe header writer for the VP8 encoder. Port of the
// keyframe path of libvpx vp8/encoder/bitstream.c (vp8_pack_bitstream),
// limited to the slice that produces the uncompressed frame tag, the
// uncompressed start code + width/height, and the compressed first-partition
// fields up through refresh_entropy_probs. The macroblock-data tail of
// vp8_pack_bitstream is intentionally NOT included here; that follows in a
// later PR alongside the tokenizer.
//
// The output of this writer alone is not a complete VP8 frame, but the
// uncompressed chunk and the first-partition prefix it produces are
// bit-exactly the same as libvpx would emit for the same inputs, and the
// existing decoder's frame-tag parser accepts them (verified by the
// accompanying unit tests).
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Ported from libvpx vp8/encoder/bitstream.c.
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
    /// Configuration for the keyframe header writer. Defaults match the
    /// "all features off" baseline used by libvpx for a simple intra-only
    /// keyframe encode: no segmentation, no loop-filter mode/ref deltas,
    /// single token partition, no quantizer deltas.
    /// </summary>
    public sealed class KeyframeHeaderConfig
    {
        /// <summary>Frame width in pixels (14 bits, 1..16383).</summary>
        public int Width;

        /// <summary>Frame height in pixels (14 bits, 1..16383).</summary>
        public int Height;

        /// <summary>Horizontal upscaling code (2 bits, 0..3). 0 = no upscale.</summary>
        public int HorizScale;

        /// <summary>Vertical upscaling code (2 bits, 0..3). 0 = no upscale.</summary>
        public int VertScale;

        /// <summary>Profile / version (3 bits, 0..3). 0 = "bicubic", baseline profile.</summary>
        public int Version;

        /// <summary>true if the frame is to be displayed (sets show_frame bit to 1).</summary>
        public bool ShowFrame = true;

        /// <summary>Color space (1 bit). 0 = YUV (only value defined by the spec).</summary>
        public int ColorSpace;

        /// <summary>Pixel value clamping type (1 bit). 0 = clamp required.</summary>
        public int ClampType;

        /// <summary>Loop filter type (1 bit). 0 = normal, 1 = simple.</summary>
        public int FilterType;

        /// <summary>Loop filter level (6 bits, 0..63). 0 disables the filter.</summary>
        public int FilterLevel;

        /// <summary>Loop filter sharpness (3 bits, 0..7).</summary>
        public int SharpnessLevel;

        /// <summary>log2 of the number of token partitions (2 bits, 0..2). 0 = single partition.</summary>
        public int Log2NumberOfTokenPartitions;

        /// <summary>Frame baseline quantizer index (7 bits, 0..127).</summary>
        public int BaseQindex;

        /// <summary>Y1 DC quantizer delta (signed 5-bit value: 4 bits of magnitude + sign).</summary>
        public int Y1DcDeltaQ;

        /// <summary>Y2 DC quantizer delta.</summary>
        public int Y2DcDeltaQ;

        /// <summary>Y2 AC quantizer delta.</summary>
        public int Y2AcDeltaQ;

        /// <summary>UV DC quantizer delta.</summary>
        public int UvDcDeltaQ;

        /// <summary>UV AC quantizer delta.</summary>
        public int UvAcDeltaQ;
    }

    public static unsafe class bitstream
    {
        /// <summary>
        /// libvpx's "fair coin" probability used for raw bits and literals
        /// passed through the boolean coder. Equal to vp8_prob_half in
        /// libvpx/vp8/common/treecoder.h.
        /// </summary>
        public const int VP8_PROB_HALF = 128;

        /// <summary>
        /// 3-byte VP8 keyframe start code (0x9D 0x01 0x2A) defined in RFC 6386 §9.1.
        /// </summary>
        public static readonly byte[] KEYFRAME_START_CODE = { 0x9D, 0x01, 0x2A };

        /// <summary>Length of the uncompressed frame tag (3 bytes).</summary>
        public const int FRAME_TAG_BYTES = 3;

        /// <summary>Length of the keyframe-only uncompressed extra (start code + size, 7 bytes).</summary>
        public const int KEYFRAME_EXTRA_BYTES = 7;

        /// <summary>
        /// Writes a single boolean with vp8_prob_half. Mirrors libvpx's
        /// vp8_write_bit macro (treewriter.h).
        /// </summary>
        public static void vp8_write_bit(ref BOOL_CODER bc, int value)
        {
            boolhuff.vp8_encode_bool(ref bc, value, VP8_PROB_HALF);
        }

        /// <summary>
        /// Writes the 5-bit signed delta-q field (1 flag bit, then 4 bits of
        /// magnitude + 1 sign bit when non-zero). Bit-exact port of the static
        /// put_delta_q helper in libvpx/vp8/encoder/bitstream.c.
        /// </summary>
        public static void put_delta_q(ref BOOL_CODER bc, int delta_q)
        {
            if (delta_q != 0)
            {
                vp8_write_bit(ref bc, 1);
                boolhuff.vp8_encode_value(ref bc, Math.Abs(delta_q), 4);
                vp8_write_bit(ref bc, delta_q < 0 ? 1 : 0);
            }
            else
            {
                vp8_write_bit(ref bc, 0);
            }
        }

        /// <summary>
        /// Packs a complete keyframe header (uncompressed chunk + compressed
        /// first-partition prefix up through refresh_entropy_probs) into the
        /// supplied buffer.
        ///
        /// The macroblock data that normally follows the entropy-prob update
        /// is NOT written by this method; that's the next port. The caller
        /// can append further calls into the same boolean coder before
        /// invoking <see cref="FinishKeyframeFirstPartition"/> to flush.
        /// </summary>
        /// <param name="dest">Destination buffer (must be at least 16 bytes for the prefix alone).</param>
        /// <param name="destLen">Total length of <paramref name="dest"/>.</param>
        /// <param name="cfg">Header configuration.</param>
        /// <param name="bc">Boolean coder, returned in started state with the
        /// compressed-header prefix already written. The caller must then call
        /// <see cref="FinishKeyframeFirstPartition"/> when done with partition 0.</param>
        /// <returns>Byte offset within <paramref name="dest"/> at which the
        /// boolean coder began writing (i.e. the start of the compressed
        /// first partition). For a keyframe this is always
        /// FRAME_TAG_BYTES + KEYFRAME_EXTRA_BYTES = 10.</returns>
        public static int StartKeyframeHeader(byte* dest, int destLen, KeyframeHeaderConfig cfg, ref BOOL_CODER bc)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (destLen < FRAME_TAG_BYTES + KEYFRAME_EXTRA_BYTES)
            {
                throw new ArgumentException("Destination buffer too small for keyframe header prefix.", nameof(destLen));
            }
            ValidateConfig(cfg);

            // Bytes 0-2: frame tag — left blank for now and patched in
            // FinishKeyframeFirstPartition once the partition size is known.
            dest[0] = 0; dest[1] = 0; dest[2] = 0;

            // Bytes 3-5: keyframe start code 0x9D 0x01 0x2A.
            dest[3] = KEYFRAME_START_CODE[0];
            dest[4] = KEYFRAME_START_CODE[1];
            dest[5] = KEYFRAME_START_CODE[2];

            // Bytes 6-9: width/height with 2-bit scale prefix, little-endian
            // (RFC 6386 §9.1).
            int wPacked = (cfg.HorizScale << 14) | (cfg.Width & 0x3FFF);
            int hPacked = (cfg.VertScale << 14) | (cfg.Height & 0x3FFF);
            dest[6] = (byte)(wPacked & 0xff);
            dest[7] = (byte)((wPacked >> 8) & 0xff);
            dest[8] = (byte)(hPacked & 0xff);
            dest[9] = (byte)((hPacked >> 8) & 0xff);

            // Start the boolean coder at byte 10. This is partition 0.
            int firstPartitionStart = FRAME_TAG_BYTES + KEYFRAME_EXTRA_BYTES;
            bc = new BOOL_CODER();
            boolhuff.vp8_start_encode(ref bc, dest + firstPartitionStart, dest + destLen);

            // Compressed first-partition prefix (keyframe path, all features
            // off baseline).
            vp8_write_bit(ref bc, cfg.ColorSpace);
            vp8_write_bit(ref bc, cfg.ClampType);

            // segmentation_enabled = 0  (no per-MB segmentation features)
            vp8_write_bit(ref bc, 0);

            // Loop filter params.
            vp8_write_bit(ref bc, cfg.FilterType);
            boolhuff.vp8_encode_value(ref bc, cfg.FilterLevel, 6);
            boolhuff.vp8_encode_value(ref bc, cfg.SharpnessLevel, 3);

            // mode_ref_lf_delta_enabled = 0
            vp8_write_bit(ref bc, 0);

            // log2_nbr_of_dct_partitions
            boolhuff.vp8_encode_value(ref bc, cfg.Log2NumberOfTokenPartitions, 2);

            // Frame baseline quantizer index.
            boolhuff.vp8_encode_value(ref bc, cfg.BaseQindex, 7);

            // Five quantizer-delta fields (Y1DC, Y2DC, Y2AC, UVDC, UVAC).
            put_delta_q(ref bc, cfg.Y1DcDeltaQ);
            put_delta_q(ref bc, cfg.Y2DcDeltaQ);
            put_delta_q(ref bc, cfg.Y2AcDeltaQ);
            put_delta_q(ref bc, cfg.UvDcDeltaQ);
            put_delta_q(ref bc, cfg.UvAcDeltaQ);

            // For a keyframe libvpx always sets refresh_entropy_probs = 1
            // (the probability tables are reset to the default at every
            // keyframe; signalling 1 here means "use the just-decoded
            // updates for subsequent inter-frames", which is the typical
            // keyframe choice and matches what the decoder expects).
            vp8_write_bit(ref bc, 1);

            return firstPartitionStart;
        }

        /// <summary>
        /// Closes the boolean coder for partition 0 and patches the 3-byte
        /// frame tag with the now-known first_partition_length_in_bytes,
        /// show_frame, version, and key/inter flag bits. Mirrors the post-
        /// macroblock tail of libvpx vp8_pack_bitstream that does the same
        /// patch. Call this once partition 0 is fully written.
        /// </summary>
        /// <param name="dest">Same buffer that was passed to <see cref="StartKeyframeHeader"/>.</param>
        /// <param name="cfg">Same config that was passed to <see cref="StartKeyframeHeader"/>.</param>
        /// <param name="bc">The boolean coder returned by <see cref="StartKeyframeHeader"/>.</param>
        /// <returns>Total bytes written into <paramref name="dest"/>:
        /// FRAME_TAG_BYTES + KEYFRAME_EXTRA_BYTES + bytes consumed by the
        /// boolean coder.</returns>
        public static int FinishKeyframeFirstPartition(byte* dest, KeyframeHeaderConfig cfg, ref BOOL_CODER bc)
        {
            // Flush trailing bits of the boolean coder.
            boolhuff.vp8_stop_encode(ref bc);

            int firstPartitionLength = (int)bc.pos;

            // Pack the frame tag exactly as libvpx does:
            //
            //   v = (length << 5) | (show_frame << 4) | (version << 1) | key_frame_flag
            //
            // and write it little-endian into bytes 0..2. Note key_frame_flag
            // is 0 for keyframes and 1 for inter — opposite of intuition.
            int keyFrameFlag = 0; // keyframe
            int showFrame = cfg.ShowFrame ? 1 : 0;
            int v = (firstPartitionLength << 5)
                  | (showFrame << 4)
                  | ((cfg.Version & 0x7) << 1)
                  | keyFrameFlag;

            dest[0] = (byte)(v & 0xff);
            dest[1] = (byte)((v >> 8) & 0xff);
            dest[2] = (byte)((v >> 16) & 0xff);

            return FRAME_TAG_BYTES + KEYFRAME_EXTRA_BYTES + firstPartitionLength;
        }

        private static void ValidateConfig(KeyframeHeaderConfig cfg)
        {
            if (cfg.Width <= 0 || cfg.Width > 0x3FFF) throw new ArgumentOutOfRangeException(nameof(cfg.Width), cfg.Width, "Width must be 1..16383.");
            if (cfg.Height <= 0 || cfg.Height > 0x3FFF) throw new ArgumentOutOfRangeException(nameof(cfg.Height), cfg.Height, "Height must be 1..16383.");
            if ((uint)cfg.HorizScale > 3) throw new ArgumentOutOfRangeException(nameof(cfg.HorizScale), cfg.HorizScale, "HorizScale must be 0..3.");
            if ((uint)cfg.VertScale > 3) throw new ArgumentOutOfRangeException(nameof(cfg.VertScale), cfg.VertScale, "VertScale must be 0..3.");
            if ((uint)cfg.Version > 7) throw new ArgumentOutOfRangeException(nameof(cfg.Version), cfg.Version, "Version must be 0..7.");
            if ((uint)cfg.BaseQindex > 0x7F) throw new ArgumentOutOfRangeException(nameof(cfg.BaseQindex), cfg.BaseQindex, "BaseQindex must be 0..127.");
            if ((uint)cfg.FilterLevel > 0x3F) throw new ArgumentOutOfRangeException(nameof(cfg.FilterLevel), cfg.FilterLevel, "FilterLevel must be 0..63.");
            if ((uint)cfg.SharpnessLevel > 0x07) throw new ArgumentOutOfRangeException(nameof(cfg.SharpnessLevel), cfg.SharpnessLevel, "SharpnessLevel must be 0..7.");
            if ((uint)cfg.Log2NumberOfTokenPartitions > 2) throw new ArgumentOutOfRangeException(nameof(cfg.Log2NumberOfTokenPartitions), cfg.Log2NumberOfTokenPartitions, "Log2NumberOfTokenPartitions must be 0..2.");
            int dqRange = 0x1F;
            if (Math.Abs(cfg.Y1DcDeltaQ) > 0xF) throw new ArgumentOutOfRangeException(nameof(cfg.Y1DcDeltaQ), cfg.Y1DcDeltaQ, "Y1DcDeltaQ must be -15..15.");
            if (Math.Abs(cfg.Y2DcDeltaQ) > 0xF) throw new ArgumentOutOfRangeException(nameof(cfg.Y2DcDeltaQ), cfg.Y2DcDeltaQ, "Y2DcDeltaQ must be -15..15.");
            if (Math.Abs(cfg.Y2AcDeltaQ) > 0xF) throw new ArgumentOutOfRangeException(nameof(cfg.Y2AcDeltaQ), cfg.Y2AcDeltaQ, "Y2AcDeltaQ must be -15..15.");
            if (Math.Abs(cfg.UvDcDeltaQ) > 0xF) throw new ArgumentOutOfRangeException(nameof(cfg.UvDcDeltaQ), cfg.UvDcDeltaQ, "UvDcDeltaQ must be -15..15.");
            if (Math.Abs(cfg.UvAcDeltaQ) > 0xF) throw new ArgumentOutOfRangeException(nameof(cfg.UvAcDeltaQ), cfg.UvAcDeltaQ, "UvAcDeltaQ must be -15..15.");
            _ = dqRange;
        }
    }
}
