//-----------------------------------------------------------------------------
// Filename: VideoBitstreams.cs
//
// Description: The bits of VP9 and AV1 bitstream parsing the Matroska sink
// needs to describe a track it is about to write: which frames are key frames
// (a decoder can only start on one) and the frame dimensions, plus, for AV1,
// the AV1CodecConfigurationRecord that goes in CodecPrivate.
//
// This is deliberately the SMALLEST useful subset of each spec. Nothing here
// decodes anything - it reads the uncompressed/sequence header at the front of
// a frame and stops as soon as it has what the container needs. The headers
// are read in full up to that point rather than guessed at from fixed offsets,
// because the fields that matter sit behind conditional ones (AV1's colour
// config in particular is only reachable after the operating points and a long
// run of feature flags), and a muxer that writes a plausible-looking but wrong
// profile or chroma format produces a file that plays on the machine it was
// made on and fails elsewhere.
//
// References: VP9 Bitstream & Decoding Process Specification v0.6 section 6.2
// (uncompressed_header), and the AV1 Bitstream & Decoding Process
// Specification section 5.5 (sequence_header_obu) and 5.9 (uncompressed_header),
// with the AV1CodecConfigurationRecord from the AV1 Codec ISO Media File Format
// Binding v1.2.0 section 2.3.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.IO;
using SIPSorcery.Net;

namespace SIPSorcery.Cli.Commands.Route;

/// <summary>
/// A most-significant-bit-first reader over a byte span, the f(n) of both specs. Reading past the
/// end sets <see cref="Overran"/> and returns zeroes rather than throwing, so a truncated or
/// unexpected header falls out as "could not parse" at the one place the caller checks.
/// </summary>
public sealed class BitReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _bitPosition;

    public BitReader(ReadOnlyMemory<byte> data) => _data = data;

    /// <summary>True once a read ran past the end of the data; every value read after that is suspect.</summary>
    public bool Overran { get; private set; }

    /// <summary>Reads an unsigned n bit big-endian field, f(n) in both specs. n must be 0..32.</summary>
    public uint F(int n)
    {
        uint value = 0;
        var span = _data.Span;

        for (int i = 0; i < n; i++)
        {
            int byteIndex = _bitPosition >> 3;
            if (byteIndex >= span.Length)
            {
                Overran = true;
                return value << (n - i);
            }

            int bit = (span[byteIndex] >> (7 - (_bitPosition & 7))) & 1;
            value = (value << 1) | (uint)bit;
            _bitPosition++;
        }

        return value;
    }

    /// <summary>Reads a single flag, f(1).</summary>
    public bool Flag() => F(1) != 0;

    /// <summary>Skips n bits.</summary>
    public void Skip(int n)
    {
        _bitPosition += n;
        if (_bitPosition > _data.Length * 8)
        {
            Overran = true;
        }
    }

    /// <summary>
    /// Reads an unsigned exp-Golomb value, ue(v) in H.264/H.265. The leading-zero run is capped at
    /// 32 so a run of zero bytes (which is what a misaligned read looks like) ends as an overrun
    /// rather than spinning.
    /// </summary>
    public uint Ue()
    {
        int leadingZeros = 0;
        while (leadingZeros < 32 && !Overran && F(1) == 0)
        {
            leadingZeros++;
        }

        if (leadingZeros == 0)
        {
            return 0;
        }

        if (leadingZeros >= 32)
        {
            Overran = true;
            return 0;
        }

        return (uint)((1 << leadingZeros) - 1) + F(leadingZeros);
    }
}

/// <summary>The VP9 uncompressed header fields the Matroska sink needs.</summary>
public static class Vp9
{
    private const uint FRAME_MARKER = 2;
    private const uint FRAME_SYNC_CODE = 0x498342;
    private const int CS_RGB = 7;

    /// <summary>What a key frame's uncompressed header tells the container.</summary>
    public readonly record struct FrameInfo(bool IsKeyFrame, int Width, int Height);

    /// <summary>
    /// Reads the front of a VP9 frame. For a key frame the dimensions come back with it; for any
    /// other frame only <see cref="FrameInfo.IsKeyFrame"/> is meaningful (width/height are 0, which
    /// is fine because the track is only ever created from a key frame). Returns false if the data
    /// is not a VP9 frame header at all.
    /// </summary>
    public static bool TryReadFrameInfo(ReadOnlyMemory<byte> frame, out FrameInfo info)
    {
        info = default;

        var reader = new BitReader(frame);

        if (reader.F(2) != FRAME_MARKER)
        {
            return false;
        }

        uint profileLowBit = reader.F(1);
        uint profileHighBit = reader.F(1);
        int profile = (int)((profileHighBit << 1) + profileLowBit);

        if (profile == 3)
        {
            reader.Skip(1);         // reserved_zero
        }

        if (reader.Flag())
        {
            // show_existing_frame: a reference is re-shown and there is no header to read.
            info = new FrameInfo(false, 0, 0);
            return !reader.Overran;
        }

        bool isKeyFrame = reader.F(1) == 0;     // frame_type: 0 is KEY_FRAME.
        reader.Skip(1);                         // show_frame
        reader.Skip(1);                         // error_resilient_mode

        if (!isKeyFrame)
        {
            info = new FrameInfo(false, 0, 0);
            return !reader.Overran;
        }

        if (reader.F(24) != FRAME_SYNC_CODE)
        {
            return false;
        }

        // ---- color_config() ----
        if (profile >= 2)
        {
            reader.Skip(1);                     // ten_or_twelve_bit
        }

        uint colorSpace = reader.F(3);
        if (colorSpace != CS_RGB)
        {
            reader.Skip(1);                     // color_range
            if (profile == 1 || profile == 3)
            {
                reader.Skip(3);                 // subsampling_x, subsampling_y, reserved_zero
            }
        }
        else if (profile == 1 || profile == 3)
        {
            reader.Skip(1);                     // reserved_zero
        }

        // ---- frame_size() ----
        int width = (int)reader.F(16) + 1;
        int height = (int)reader.F(16) + 1;

        if (reader.Overran || width <= 0 || height <= 0)
        {
            return false;
        }

        info = new FrameInfo(true, width, height);
        return true;
    }
}

/// <summary>
/// The AV1 sequence header fields the Matroska sink needs, and the
/// AV1CodecConfigurationRecord ("av1C") built from them for CodecPrivate.
/// </summary>
public static class Av1
{
    private const int OBU_TYPE_MASK = 0x78;
    private const int OBU_TYPE_SHIFT = 3;
    private const byte OBU_EXTENSION_FLAG = 0x04;
    private const byte OBU_HAS_SIZE_FIELD = 0x02;

    /// <summary>
    /// The sequence header fields that go into the av1C record and the track's dimensions, plus the
    /// sequence header OBU itself, which av1C carries verbatim in its configOBUs.
    /// </summary>
    public sealed record SequenceHeader(
        byte[] ObuBytes,
        int SeqProfile,
        int SeqLevelIdx0,
        int SeqTier0,
        bool HighBitdepth,
        bool TwelveBit,
        bool Monochrome,
        bool SubsamplingX,
        bool SubsamplingY,
        int ChromaSamplePosition,
        bool ReducedStillPictureHeader,
        int MaxFrameWidth,
        int MaxFrameHeight);

    /// <summary>
    /// Walks a temporal unit's OBUs, yielding each one's type and the offset/length of its payload
    /// within <paramref name="temporalUnit"/>. The splitting itself is delegated to
    /// <see cref="AV1Packetiser.ParseObus"/> so the leb128 size handling lives in one place; only
    /// the per-OBU header decode (to find where the payload starts) is done here.
    /// </summary>
    private static IEnumerable<(AV1Packetiser.AV1ObuType Type, byte[] Obu, int PayloadOffset, int PayloadLength)> EnumerateObus(byte[] temporalUnit)
    {
        foreach (byte[] obu in AV1Packetiser.ParseObus(temporalUnit))
        {
            if (obu.Length < 1)
            {
                continue;
            }

            var type = (AV1Packetiser.AV1ObuType)((obu[0] & OBU_TYPE_MASK) >> OBU_TYPE_SHIFT);
            int offset = 1;

            if ((obu[0] & OBU_EXTENSION_FLAG) != 0)
            {
                offset += 1;
            }

            if ((obu[0] & OBU_HAS_SIZE_FIELD) != 0)
            {
                if (!AV1Packetiser.TryReadLeb128(obu, ref offset, out int size, out _))
                {
                    continue;
                }

                int available = Math.Max(0, Math.Min(size, obu.Length - offset));
                yield return (type, obu, offset, available);
            }
            else
            {
                yield return (type, obu, offset, obu.Length - offset);
            }
        }
    }

    /// <summary>
    /// Finds and parses the sequence header OBU in a temporal unit, or returns null if there is
    /// none (every non-key temporal unit) or it could not be read.
    /// </summary>
    public static SequenceHeader? TryParseSequenceHeader(byte[] temporalUnit)
    {
        foreach (var (type, obu, payloadOffset, payloadLength) in EnumerateObus(temporalUnit))
        {
            if (type != AV1Packetiser.AV1ObuType.SequenceHeader || payloadLength <= 0)
            {
                continue;
            }

            var parsed = ParseSequenceHeaderPayload(obu.AsMemory(payloadOffset, payloadLength), obu);
            if (parsed != null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static SequenceHeader? ParseSequenceHeaderPayload(ReadOnlyMemory<byte> payload, byte[] obuBytes)
    {
        var r = new BitReader(payload);

        int seqProfile = (int)r.F(3);
        r.Skip(1);                                          // still_picture
        bool reducedStillPicture = r.Flag();

        int seqLevelIdx0;
        int seqTier0 = 0;

        if (reducedStillPicture)
        {
            seqLevelIdx0 = (int)r.F(5);
        }
        else
        {
            bool timingInfoPresent = r.Flag();
            bool decoderModelInfoPresent = false;
            int bufferDelayLengthMinus1 = 0;

            if (timingInfoPresent)
            {
                // timing_info(): num_units_in_display_tick, time_scale, equal_picture_interval
                r.Skip(32);
                r.Skip(32);
                if (r.Flag())
                {
                    SkipUvlc(r);                            // num_ticks_per_picture_minus_1
                }

                decoderModelInfoPresent = r.Flag();
                if (decoderModelInfoPresent)
                {
                    // decoder_model_info()
                    bufferDelayLengthMinus1 = (int)r.F(5);
                    r.Skip(32);                             // num_units_in_decoding_tick
                    r.Skip(5 + 5);                          // buffer_removal_time_length_minus_1, frame_presentation_time_length_minus_1
                }
            }

            bool initialDisplayDelayPresent = r.Flag();
            int operatingPointsCntMinus1 = (int)r.F(5);

            seqLevelIdx0 = 0;
            for (int i = 0; i <= operatingPointsCntMinus1; i++)
            {
                r.Skip(12);                                 // operating_point_idc[i]
                int levelIdx = (int)r.F(5);
                int tier = 0;

                if (levelIdx > 7)
                {
                    tier = (int)r.F(1);
                }

                if (decoderModelInfoPresent && r.Flag())
                {
                    // operating_parameters_info(): two buffer delays plus low_delay_mode_flag
                    r.Skip(2 * (bufferDelayLengthMinus1 + 1) + 1);
                }

                if (initialDisplayDelayPresent && r.Flag())
                {
                    r.Skip(4);                              // initial_display_delay_minus_1[i]
                }

                // av1C carries operating point 0, which is the one a plain decoder uses.
                if (i == 0)
                {
                    seqLevelIdx0 = levelIdx;
                    seqTier0 = tier;
                }
            }
        }

        int frameWidthBits = (int)r.F(4) + 1;
        int frameHeightBits = (int)r.F(4) + 1;
        int maxFrameWidth = (int)r.F(frameWidthBits) + 1;
        int maxFrameHeight = (int)r.F(frameHeightBits) + 1;

        bool frameIdNumbersPresent = !reducedStillPicture && r.Flag();
        if (frameIdNumbersPresent)
        {
            r.Skip(4 + 3);                                  // delta_frame_id_length_minus_2, additional_frame_id_length_minus_1
        }

        r.Skip(3);                                          // use_128x128_superblock, enable_filter_intra, enable_intra_edge_filter

        if (!reducedStillPicture)
        {
            r.Skip(4);                                      // interintra_compound, masked_compound, warped_motion, dual_filter

            bool enableOrderHint = r.Flag();
            if (enableOrderHint)
            {
                r.Skip(2);                                  // enable_jnt_comp, enable_ref_frame_mvs
            }

            int forceScreenContentTools = r.Flag() ? 2 : (int)r.F(1);   // seq_choose_screen_content_tools
            if (forceScreenContentTools > 0 && !r.Flag())                // seq_choose_integer_mv
            {
                r.Skip(1);                                  // seq_force_integer_mv
            }

            if (enableOrderHint)
            {
                r.Skip(3);                                  // order_hint_bits_minus_1
            }
        }

        r.Skip(3);                                          // enable_superres, enable_cdef, enable_restoration

        // ---- color_config() ----
        bool highBitdepth = r.Flag();
        bool twelveBit = false;

        if (seqProfile == 2 && highBitdepth)
        {
            twelveBit = r.Flag();
        }

        int bitDepth = seqProfile == 2 && highBitdepth ? (twelveBit ? 12 : 10) : (highBitdepth ? 10 : 8);
        bool monochrome = seqProfile != 1 && r.Flag();

        int colorPrimaries = 2;         // CP_UNSPECIFIED
        int transferCharacteristics = 2;// TC_UNSPECIFIED
        int matrixCoefficients = 2;     // MC_UNSPECIFIED

        if (r.Flag())                   // color_description_present_flag
        {
            colorPrimaries = (int)r.F(8);
            transferCharacteristics = (int)r.F(8);
            matrixCoefficients = (int)r.F(8);
        }

        bool subsamplingX;
        bool subsamplingY;
        int chromaSamplePosition = 0;   // CSP_UNKNOWN

        if (monochrome)
        {
            r.Skip(1);                  // color_range
            subsamplingX = true;
            subsamplingY = true;
        }
        else if (colorPrimaries == 1 && transferCharacteristics == 13 && matrixCoefficients == 0)
        {
            // CP_BT_709 + TC_SRGB + MC_IDENTITY is full range 4:4:4 with no explicit range bit.
            subsamplingX = false;
            subsamplingY = false;
        }
        else
        {
            r.Skip(1);                  // color_range

            if (seqProfile == 0)
            {
                subsamplingX = true;
                subsamplingY = true;
            }
            else if (seqProfile == 1)
            {
                subsamplingX = false;
                subsamplingY = false;
            }
            else if (bitDepth == 12)
            {
                subsamplingX = r.Flag();
                subsamplingY = subsamplingX && r.Flag();
            }
            else
            {
                subsamplingX = true;
                subsamplingY = false;
            }

            if (subsamplingX && subsamplingY)
            {
                chromaSamplePosition = (int)r.F(2);
            }
        }

        if (r.Overran || maxFrameWidth <= 0 || maxFrameHeight <= 0)
        {
            return null;
        }

        return new SequenceHeader(obuBytes, seqProfile, seqLevelIdx0, seqTier0, highBitdepth, twelveBit,
            monochrome, subsamplingX, subsamplingY, chromaSamplePosition, reducedStillPicture,
            maxFrameWidth, maxFrameHeight);
    }

    /// <summary>Skips a uvlc() coded value (AV1 spec 4.10.3).</summary>
    private static void SkipUvlc(BitReader r)
    {
        int leadingZeros = 0;
        while (leadingZeros < 32 && !r.Flag() && !r.Overran)
        {
            leadingZeros++;
        }

        if (leadingZeros < 32)
        {
            r.Skip(leadingZeros);
        }
    }

    /// <summary>
    /// True if the temporal unit contains a frame whose frame_type is KEY_FRAME and which is shown.
    /// A decoder can only start here, so it is what the Matroska key frame flag has to mean.
    /// </summary>
    public static bool IsKeyFrame(byte[] temporalUnit, bool reducedStillPictureHeader)
    {
        foreach (var (type, obu, payloadOffset, payloadLength) in EnumerateObus(temporalUnit))
        {
            if (type != AV1Packetiser.AV1ObuType.Frame && type != AV1Packetiser.AV1ObuType.FrameHeader)
            {
                continue;
            }

            if (payloadLength <= 0)
            {
                continue;
            }

            // uncompressed_header(): with a reduced still picture header the frame is a key frame by
            // definition; otherwise show_existing_frame then frame_type (0 = KEY_FRAME).
            if (reducedStillPictureHeader)
            {
                return true;
            }

            var r = new BitReader(obu.AsMemory(payloadOffset, payloadLength));
            if (r.Flag())
            {
                continue;               // show_existing_frame: re-showing a reference, not a key frame.
            }

            if (r.F(2) == 0 && !r.Overran)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the AV1CodecConfigurationRecord for CodecPrivate: four packed bytes describing the
    /// operating point and chroma format, followed by the sequence header OBU verbatim.
    /// </summary>
    public static byte[] BuildAv1C(SequenceHeader sh)
    {
        var av1C = new MemoryStream(sh.ObuBytes.Length + 4);

        av1C.WriteByte(0x81);                                       // marker = 1, version = 1
        av1C.WriteByte((byte)((sh.SeqProfile << 5) | (sh.SeqLevelIdx0 & 0x1F)));
        av1C.WriteByte((byte)(
            (sh.SeqTier0 << 7) |
            ((sh.HighBitdepth ? 1 : 0) << 6) |
            ((sh.TwelveBit ? 1 : 0) << 5) |
            ((sh.Monochrome ? 1 : 0) << 4) |
            ((sh.SubsamplingX ? 1 : 0) << 3) |
            ((sh.SubsamplingY ? 1 : 0) << 2) |
            (sh.ChromaSamplePosition & 0x03)));
        // reserved (3) = 0, initial_presentation_delay_present (1) = 0, and with it zero the
        // remaining 4 bits: a live stream has no presentation delay to declare.
        av1C.WriteByte(0x00);

        av1C.Write(sh.ObuBytes, 0, sh.ObuBytes.Length);

        return av1C.ToArray();
    }
}

/// <summary>
/// The H265 (HEVC) Annex B to Matroska (ISO/HEVC) conversions: length-prefixed NAL units, the
/// HEVCDecoderConfigurationRecord ("hvcC") for CodecPrivate, and the SPS fields hvcC and the track
/// need. NAL splitting is shared with H264 - the Annex B start code scan is the same for both.
/// </summary>
public static class H265
{
    // nal_unit_type values (H.265 table 7-1). 16..23 are the IRAP range: a decoder can start on any
    // of them, which is exactly what the Matroska key frame flag means.
    private const int NAL_IRAP_FIRST = 16;
    private const int NAL_IRAP_LAST = 23;
    private const int NAL_VPS = 32;
    private const int NAL_SPS = 33;
    private const int NAL_PPS = 34;
    private const int NAL_AUD = 35;

    /// <summary>The nal_unit_type from a two byte H265 NAL header.</summary>
    public static int NalType(byte firstByte) => (firstByte >> 1) & 0x3F;

    /// <summary>The SPS fields hvcC and the Matroska video track need.</summary>
    public sealed record SpsInfo(
        byte[] ProfileTierLevel,     // the 12 aligned bytes hvcC copies verbatim
        int ChromaFormatIdc,
        int BitDepthLumaMinus8,
        int BitDepthChromaMinus8,
        int NumTemporalLayers,
        bool TemporalIdNested,
        int Width,
        int Height);

    /// <summary>
    /// Converts an Annex B access unit to 4 byte length-prefixed NAL units, dropping access unit
    /// delimiters. <paramref name="keyFrame"/> is true if it contains an IRAP picture.
    /// </summary>
    public static byte[] ToLengthPrefixed(byte[] au, out bool keyFrame)
    {
        keyFrame = false;
        var output = new MemoryStream(au.Length + 16);
        Span<byte> length = stackalloc byte[4];

        foreach (var (offset, len) in MatroskaSinkNode.H264.SplitAnnexB(au))
        {
            if (len < 2)
            {
                continue;
            }

            int type = NalType(au[offset]);
            if (type == NAL_AUD)
            {
                continue;
            }

            if (type >= NAL_IRAP_FIRST && type <= NAL_IRAP_LAST)
            {
                keyFrame = true;
            }

            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)len);
            output.Write(length);
            output.Write(au, offset, len);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Builds an hvcC from the first VPS, SPS and PPS in an access unit, or null until all three
    /// have been seen. They travel in-band ahead of each IRAP picture, as for H264's avcC.
    /// </summary>
    public static byte[]? TryBuildHvcC(byte[] au, out SpsInfo? spsInfo)
    {
        spsInfo = null;
        byte[]? vps = null, sps = null, pps = null;

        foreach (var (offset, len) in MatroskaSinkNode.H264.SplitAnnexB(au))
        {
            if (len < 2)
            {
                continue;
            }

            switch (NalType(au[offset]))
            {
                case NAL_VPS when vps == null: vps = au.AsSpan(offset, len).ToArray(); break;
                case NAL_SPS when sps == null: sps = au.AsSpan(offset, len).ToArray(); break;
                case NAL_PPS when pps == null: pps = au.AsSpan(offset, len).ToArray(); break;
            }
        }

        if (vps == null || sps == null || pps == null)
        {
            return null;
        }

        spsInfo = TryParseSps(sps);
        if (spsInfo == null)
        {
            return null;
        }

        var hvcC = new MemoryStream();
        hvcC.WriteByte(1);                                              // configurationVersion
        hvcC.Write(spsInfo.ProfileTierLevel, 0, 12);                    // profile_space .. general_level_idc
        hvcC.WriteByte(0xF0);                                           // 4 reserved bits + min_spatial_segmentation_idc high
        hvcC.WriteByte(0x00);                                           // min_spatial_segmentation_idc low (0: not signalled)
        hvcC.WriteByte(0xFC);                                           // 6 reserved bits + parallelismType 0 (unknown)
        hvcC.WriteByte((byte)(0xFC | (spsInfo.ChromaFormatIdc & 0x03)));
        hvcC.WriteByte((byte)(0xF8 | (spsInfo.BitDepthLumaMinus8 & 0x07)));
        hvcC.WriteByte((byte)(0xF8 | (spsInfo.BitDepthChromaMinus8 & 0x07)));
        hvcC.WriteByte(0x00);                                           // avgFrameRate high (0: not signalled)
        hvcC.WriteByte(0x00);                                           // avgFrameRate low
        // constantFrameRate 0 (unknown) + numTemporalLayers + temporalIdNested + lengthSizeMinusOne 3.
        hvcC.WriteByte((byte)(
            (0 << 6) |
            ((spsInfo.NumTemporalLayers & 0x07) << 3) |
            ((spsInfo.TemporalIdNested ? 1 : 0) << 2) |
            0x03));
        hvcC.WriteByte(3);                                              // numOfArrays: VPS, SPS, PPS

        WriteArray(hvcC, NAL_VPS, vps);
        WriteArray(hvcC, NAL_SPS, sps);
        WriteArray(hvcC, NAL_PPS, pps);

        return hvcC.ToArray();
    }

    /// <summary>Writes one hvcC NAL array: array_completeness 0, the type, then the single NAL.</summary>
    private static void WriteArray(MemoryStream hvcC, int nalType, byte[] nal)
    {
        hvcC.WriteByte((byte)(nalType & 0x3F));
        hvcC.WriteByte(0);
        hvcC.WriteByte(1);                                              // numNalus
        hvcC.WriteByte((byte)(nal.Length >> 8));
        hvcC.WriteByte((byte)nal.Length);
        hvcC.Write(nal, 0, nal.Length);
    }

    /// <summary>
    /// Parses the SPS fields hvcC and the track need. The profile_tier_level block is copied as 12
    /// whole bytes, which is exact: it starts one byte into the RBSP (sps_video_parameter_set_id,
    /// sps_max_sub_layers_minus1 and sps_temporal_id_nesting_flag fill a byte between them) and its
    /// general part is 96 bits, so it is byte aligned at both ends. Everything after it needs the
    /// bit reader, because the ue(v) fields are not aligned.
    /// </summary>
    public static SpsInfo? TryParseSps(byte[] spsNal)
    {
        // The RBSP, with emulation prevention bytes removed, starting after the 2 byte NAL header.
        byte[] rbsp = RemoveEmulationPrevention(spsNal, 2);

        if (rbsp.Length < 13)
        {
            return null;
        }

        int maxSubLayersMinus1 = (rbsp[0] >> 1) & 0x07;
        bool temporalIdNested = (rbsp[0] & 0x01) != 0;

        var profileTierLevel = new byte[12];
        Array.Copy(rbsp, 1, profileTierLevel, 0, 12);

        var r = new BitReader(rbsp);
        r.Skip(8);                                      // sps_video_parameter_set_id .. nesting flag
        r.Skip(96);                                     // the general profile_tier_level just copied

        // The sub-layer part of profile_tier_level, present only with more than one temporal
        // sub-layer. WebRTC streams are single layer, but a conforming reader has to step over it
        // or every field after this lands at the wrong offset.
        Span<bool> profilePresent = stackalloc bool[8];
        Span<bool> levelPresent = stackalloc bool[8];
        for (int i = 0; i < maxSubLayersMinus1; i++)
        {
            profilePresent[i] = r.Flag();
            levelPresent[i] = r.Flag();
        }

        if (maxSubLayersMinus1 > 0)
        {
            r.Skip(2 * (8 - maxSubLayersMinus1));       // reserved_zero_2bits
        }

        for (int i = 0; i < maxSubLayersMinus1; i++)
        {
            if (profilePresent[i])
            {
                r.Skip(88);
            }
            if (levelPresent[i])
            {
                r.Skip(8);
            }
        }

        r.Ue();                                         // sps_seq_parameter_set_id
        int chromaFormatIdc = (int)r.Ue();
        if (chromaFormatIdc == 3)
        {
            r.Skip(1);                                  // separate_colour_plane_flag
        }

        int width = (int)r.Ue();                        // pic_width_in_luma_samples
        int height = (int)r.Ue();                       // pic_height_in_luma_samples

        if (r.Flag())                                   // conformance_window_flag
        {
            int left = (int)r.Ue();
            int right = (int)r.Ue();
            int top = (int)r.Ue();
            int bottom = (int)r.Ue();

            // The window is in chroma units, so the luma crop depends on the chroma format
            // (4:2:0 halves both axes, 4:2:2 only the horizontal one).
            int subWidthC = chromaFormatIdc == 1 || chromaFormatIdc == 2 ? 2 : 1;
            int subHeightC = chromaFormatIdc == 1 ? 2 : 1;
            width -= subWidthC * (left + right);
            height -= subHeightC * (top + bottom);
        }

        int bitDepthLumaMinus8 = (int)r.Ue();
        int bitDepthChromaMinus8 = (int)r.Ue();

        if (r.Overran || width <= 0 || height <= 0 || chromaFormatIdc > 3 ||
            bitDepthLumaMinus8 > 7 || bitDepthChromaMinus8 > 7)
        {
            return null;
        }

        return new SpsInfo(profileTierLevel, chromaFormatIdc, bitDepthLumaMinus8, bitDepthChromaMinus8,
            maxSubLayersMinus1 + 1, temporalIdNested, width, height);
    }

    /// <summary>
    /// Returns the RBSP from <paramref name="startOffset"/>: the NAL payload with the emulation
    /// prevention byte stripped out of every 00 00 03 sequence. Parsing the bits without this reads
    /// a 0x03 that is not part of the syntax, and every field after it lands at the wrong offset.
    /// </summary>
    public static byte[] RemoveEmulationPrevention(byte[] nal, int startOffset)
    {
        if (startOffset >= nal.Length)
        {
            return Array.Empty<byte>();
        }

        var rbsp = new List<byte>(nal.Length - startOffset);
        int zeros = 0;

        for (int i = startOffset; i < nal.Length; i++)
        {
            byte b = nal[i];

            if (zeros >= 2 && b == 0x03)
            {
                zeros = 0;
                continue;       // the emulation prevention byte itself
            }

            rbsp.Add(b);
            zeros = b == 0 ? zeros + 1 : 0;
        }

        return rbsp.ToArray();
    }
}
