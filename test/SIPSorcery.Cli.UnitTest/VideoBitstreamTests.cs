//-----------------------------------------------------------------------------
// Filename: VideoBitstreamTests.cs
//
// Description: Unit tests for the VP9 and AV1 header parsing the Matroska sink
// uses to describe a track: key frame detection, frame dimensions and the AV1
// CodecPrivate (av1C) record.
//
// The headers are built here bit by bit with the same BitWriter the tests use
// for every case, so a test states the spec's field order explicitly rather
// than asserting against an opaque captured blob. A wrong answer then points
// at the field that was misread.
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
using System.Linq;
using SIPSorcery.Cli.Commands.Route;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

/// <summary>Writes MSB-first bit fields, the mirror of the BitReader under test.</summary>
internal sealed class BitWriter
{
    private readonly List<byte> _bytes = new();
    private int _bitCount;

    public BitWriter F(int n, uint value)
    {
        for (int i = n - 1; i >= 0; i--)
        {
            int bit = (int)((value >> i) & 1);
            if ((_bitCount & 7) == 0)
            {
                _bytes.Add(0);
            }
            if (bit != 0)
            {
                _bytes[^1] |= (byte)(1 << (7 - (_bitCount & 7)));
            }
            _bitCount++;
        }
        return this;
    }

    public BitWriter Flag(bool value) => F(1, value ? 1u : 0u);

    /// <summary>Writes an unsigned exp-Golomb value: (bits-1) zeros, then value+1 in `bits` bits.</summary>
    public BitWriter Ue(uint value)
    {
        uint n = value + 1;
        int bits = 0;
        for (uint v = n; v > 0; v >>= 1)
        {
            bits++;
        }
        F(bits - 1, 0);
        return F(bits, n);
    }

    /// <summary>Pads to the next byte boundary with zeroes.</summary>
    public BitWriter Align() => (_bitCount & 7) == 0 ? this : F(8 - (_bitCount & 7), 0);

    public byte[] ToArray() => _bytes.ToArray();
}

[Trait("Category", "unit")]
public class Vp9BitstreamTests
{
    /// <summary>
    /// Builds a VP9 uncompressed header for profile 0, the profile WebRTC uses. Field order is
    /// VP9 spec 6.2: frame_marker, profile bits, show_existing_frame, frame_type, show_frame,
    /// error_resilient_mode, then for a key frame the sync code, color_config and frame_size.
    /// </summary>
    private static byte[] Vp9Header(bool keyFrame, int width = 640, int height = 360,
        bool showExisting = false, uint syncCode = 0x498342)
    {
        var w = new BitWriter()
            .F(2, 2)                        // frame_marker
            .F(1, 0).F(1, 0);               // profile_low_bit, profile_high_bit -> profile 0

        w.Flag(showExisting);
        if (showExisting)
        {
            return w.F(3, 0).ToArray();     // frame_to_show_map_idx
        }

        w.F(1, keyFrame ? 0u : 1u)          // frame_type: 0 = KEY_FRAME
         .Flag(true)                        // show_frame
         .Flag(false);                      // error_resilient_mode

        if (!keyFrame)
        {
            return w.ToArray();
        }

        w.F(24, syncCode)
         .F(3, 0)                           // color_space (not CS_RGB)
         .Flag(false)                       // color_range
         .F(16, (uint)(width - 1))
         .F(16, (uint)(height - 1));

        return w.ToArray();
    }

    [Fact]
    public void Reads_a_key_frames_dimensions()
    {
        Assert.True(Vp9.TryReadFrameInfo(Vp9Header(keyFrame: true, width: 1280, height: 720), out var info));

        Assert.True(info.IsKeyFrame);
        Assert.Equal(1280, info.Width);
        Assert.Equal(720, info.Height);
    }

    [Fact]
    public void An_inter_frame_is_not_a_key_frame()
    {
        Assert.True(Vp9.TryReadFrameInfo(Vp9Header(keyFrame: false), out var info));

        Assert.False(info.IsKeyFrame);
    }

    [Fact]
    public void A_re_shown_reference_is_not_a_key_frame()
    {
        // show_existing_frame re-displays a reference and carries no header, so it must never be
        // flagged as a key frame: a decoder starting there has nothing to decode.
        Assert.True(Vp9.TryReadFrameInfo(Vp9Header(keyFrame: true, showExisting: true), out var info));

        Assert.False(info.IsKeyFrame);
    }

    [Fact]
    public void Rejects_data_that_is_not_a_vp9_frame()
    {
        // Wrong frame_marker: the first two bits of a VP9 frame are always 0b10.
        Assert.False(Vp9.TryReadFrameInfo(new byte[] { 0x00, 0x00, 0x00, 0x00 }, out _));
    }

    [Fact]
    public void Rejects_a_key_frame_with_a_bad_sync_code()
    {
        Assert.False(Vp9.TryReadFrameInfo(Vp9Header(keyFrame: true, syncCode: 0x123456), out _));
    }

    [Fact]
    public void Rejects_a_truncated_header()
    {
        byte[] full = Vp9Header(keyFrame: true);

        Assert.False(Vp9.TryReadFrameInfo(full.AsSpan(0, 3).ToArray(), out _));
    }

    [Fact]
    public void Rejects_an_empty_payload()
    {
        Assert.False(Vp9.TryReadFrameInfo(Array.Empty<byte>(), out _));
    }
}

[Trait("Category", "unit")]
public class Av1BitstreamTests
{
    private const int OBU_SEQUENCE_HEADER = 1;
    private const int OBU_FRAME = 6;

    /// <summary>Wraps a payload in an OBU with a size field, which is how our depacketiser emits them.</summary>
    private static byte[] Obu(int type, byte[] payload)
    {
        var obu = new List<byte> { (byte)((type << 3) | 0x02) };     // type + obu_has_size_field
        int size = payload.Length;
        do
        {
            byte b = (byte)(size & 0x7F);
            size >>= 7;
            obu.Add(size > 0 ? (byte)(b | 0x80) : b);
        }
        while (size > 0);
        obu.AddRange(payload);
        return obu.ToArray();
    }

    /// <summary>
    /// A sequence header payload for the shape Chrome sends: profile 0, one operating point, no
    /// timing info, 8 bit 4:2:0. Field order is AV1 spec 5.5.1 followed by 5.5.2 color_config.
    /// </summary>
    private static byte[] SequenceHeaderPayload(int width = 640, int height = 360, int seqProfile = 0,
        int levelIdx = 5, bool highBitdepth = false)
    {
        var w = new BitWriter()
            .F(3, (uint)seqProfile)
            .Flag(false)                    // still_picture
            .Flag(false)                    // reduced_still_picture_header
            .Flag(false)                    // timing_info_present_flag
            .Flag(false)                    // initial_display_delay_present_flag
            .F(5, 0);                       // operating_points_cnt_minus_1 -> one operating point

        w.F(12, 0)                          // operating_point_idc[0]
         .F(5, (uint)levelIdx);             // seq_level_idx[0]; <= 7 so no seq_tier

        w.F(4, 15).F(4, 15)                 // frame_width_bits_minus_1, frame_height_bits_minus_1 -> 16 bits each
         .F(16, (uint)(width - 1))
         .F(16, (uint)(height - 1));

        w.Flag(false)                       // frame_id_numbers_present_flag
         .Flag(false)                       // use_128x128_superblock
         .Flag(false)                       // enable_filter_intra
         .Flag(false)                       // enable_intra_edge_filter
         .Flag(false)                       // enable_interintra_compound
         .Flag(false)                       // enable_masked_compound
         .Flag(false)                       // enable_warped_motion
         .Flag(false)                       // enable_dual_filter
         .Flag(false)                       // enable_order_hint -> no jnt_comp / ref_frame_mvs / order_hint_bits
         .Flag(true)                        // seq_choose_screen_content_tools -> force = 2
         .Flag(true)                        // seq_choose_integer_mv
         .Flag(false)                       // enable_superres
         .Flag(false)                       // enable_cdef
         .Flag(false);                      // enable_restoration

        // color_config(): 8 bit, not monochrome, no colour description -> profile 0 gives 4:2:0.
        w.Flag(highBitdepth)                // high_bitdepth
         .Flag(false)                       // mono_chrome
         .Flag(false)                       // color_description_present_flag
         .Flag(false)                       // color_range
         .F(2, 0)                           // chroma_sample_position
         .Flag(false);                      // separate_uv_delta_q

        return w.ToArray();
    }

    /// <summary>A frame OBU payload whose uncompressed_header says KEY_FRAME or an inter frame.</summary>
    private static byte[] FramePayload(bool keyFrame) =>
        new BitWriter()
            .Flag(false)                                // show_existing_frame
            .F(2, keyFrame ? 0u : 1u)                   // frame_type: 0 = KEY_FRAME
            .Flag(true)                                 // show_frame
            .F(8, 0)                                    // padding, never read
            .ToArray();

    private static byte[] TemporalUnit(params byte[][] obus) => obus.SelectMany(o => o).ToArray();

    [Fact]
    public void Parses_the_sequence_header_chrome_sends()
    {
        var tu = TemporalUnit(
            Obu(OBU_SEQUENCE_HEADER, SequenceHeaderPayload(1280, 720, levelIdx: 5)),
            Obu(OBU_FRAME, FramePayload(keyFrame: true)));

        var sh = Av1.TryParseSequenceHeader(tu);

        Assert.NotNull(sh);
        Assert.Equal(0, sh!.SeqProfile);
        Assert.Equal(5, sh.SeqLevelIdx0);
        Assert.Equal(0, sh.SeqTier0);
        Assert.False(sh.HighBitdepth);
        Assert.False(sh.Monochrome);
        // Profile 0 is 4:2:0, which is both subsampling flags set. Getting this right means the
        // walk through the operating points and the feature flags landed on color_config exactly.
        Assert.True(sh.SubsamplingX);
        Assert.True(sh.SubsamplingY);
        Assert.Equal(1280, sh.MaxFrameWidth);
        Assert.Equal(720, sh.MaxFrameHeight);
    }

    [Fact]
    public void Has_no_sequence_header_in_an_inter_only_temporal_unit()
    {
        // Every non-key temporal unit looks like this, and the sink has to keep waiting rather than
        // writing a track it cannot describe.
        Assert.Null(Av1.TryParseSequenceHeader(TemporalUnit(Obu(OBU_FRAME, FramePayload(keyFrame: false)))));
    }

    [Fact]
    public void Builds_an_av1C_whose_packed_bytes_match_the_sequence_header()
    {
        var sh = Av1.TryParseSequenceHeader(TemporalUnit(Obu(OBU_SEQUENCE_HEADER, SequenceHeaderPayload(levelIdx: 5))));
        Assert.NotNull(sh);

        byte[] av1C = Av1.BuildAv1C(sh!);

        Assert.Equal(0x81, av1C[0]);                            // marker 1 + version 1
        Assert.Equal(0 << 5 | 5, av1C[1]);                      // seq_profile 0, seq_level_idx_0 5
        // tier 0, high_bitdepth 0, twelve_bit 0, monochrome 0, subsampling_x 1, subsampling_y 1, csp 0
        Assert.Equal(0b0000_1100, av1C[2]);
        Assert.Equal(0x00, av1C[3]);                            // no initial presentation delay

        // configOBUs carries the sequence header OBU verbatim, which is what the decoder reads.
        Assert.True(av1C.Length > 4);
        Assert.Equal(sh!.ObuBytes, av1C.Skip(4).ToArray());
    }

    [Fact]
    public void A_ten_bit_sequence_header_sets_high_bitdepth_in_av1C()
    {
        var sh = Av1.TryParseSequenceHeader(
            TemporalUnit(Obu(OBU_SEQUENCE_HEADER, SequenceHeaderPayload(highBitdepth: true))));

        Assert.NotNull(sh);
        Assert.True(sh!.HighBitdepth);
        Assert.False(sh.TwelveBit);
        Assert.Equal(0b0100_1100, Av1.BuildAv1C(sh)[2]);
    }

    [Fact]
    public void Finds_a_key_frame()
    {
        var tu = TemporalUnit(
            Obu(OBU_SEQUENCE_HEADER, SequenceHeaderPayload()),
            Obu(OBU_FRAME, FramePayload(keyFrame: true)));

        Assert.True(Av1.IsKeyFrame(tu, reducedStillPictureHeader: false));
    }

    [Fact]
    public void An_inter_frame_is_not_a_key_frame()
    {
        Assert.False(Av1.IsKeyFrame(TemporalUnit(Obu(OBU_FRAME, FramePayload(keyFrame: false))),
            reducedStillPictureHeader: false));
    }

    [Fact]
    public void A_re_shown_reference_is_not_a_key_frame()
    {
        byte[] showExisting = new BitWriter().Flag(true).F(3, 0).F(8, 0).ToArray();

        Assert.False(Av1.IsKeyFrame(TemporalUnit(Obu(OBU_FRAME, showExisting)),
            reducedStillPictureHeader: false));
    }

    [Fact]
    public void A_temporal_unit_with_no_frame_obu_is_not_a_key_frame()
    {
        Assert.False(Av1.IsKeyFrame(TemporalUnit(Obu(OBU_SEQUENCE_HEADER, SequenceHeaderPayload())),
            reducedStillPictureHeader: false));
    }

    [Fact]
    public void Survives_a_truncated_sequence_header()
    {
        byte[] payload = SequenceHeaderPayload();
        byte[] tu = Obu(OBU_SEQUENCE_HEADER, payload.AsSpan(0, 3).ToArray());

        // Reading past the end must come back as "cannot describe this track" rather than throwing
        // or, worse, inventing dimensions from whatever the overrun returned.
        Assert.Null(Av1.TryParseSequenceHeader(tu));
    }

    [Fact]
    public void Survives_arbitrary_bytes()
    {
        var random = new Random(1234);
        for (int i = 0; i < 200; i++)
        {
            byte[] junk = new byte[random.Next(1, 64)];
            random.NextBytes(junk);

            // Neither call may throw, whatever the bytes are: they come off the network.
            var exception = Record.Exception(() =>
            {
                try
                {
                    Av1.TryParseSequenceHeader(junk);
                    Av1.IsKeyFrame(junk, false);
                }
                catch (FormatException)
                {
                    // AV1Packetiser.ParseObus rejects a malformed OBU this way; the sink treats it
                    // as "no track yet", which is the same outcome as a null parse.
                }
                Vp9.TryReadFrameInfo(junk, out _);
            });

            Assert.Null(exception);
        }
    }
}

[Trait("Category", "unit")]
public class BitReaderTests
{
    [Fact]
    public void Reads_fields_most_significant_bit_first()
    {
        var reader = new BitReader(new byte[] { 0b1011_0010, 0b0100_0000 });

        Assert.Equal(1u, reader.F(1));
        Assert.Equal(0b011u, reader.F(3));
        Assert.Equal(0b0010_01u, reader.F(6));
        Assert.False(reader.Overran);
    }

    [Fact]
    public void Reads_a_full_32_bit_field()
    {
        var reader = new BitReader(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        Assert.Equal(0xDEADBEEFu, reader.F(32));
        Assert.False(reader.Overran);
    }

    [Fact]
    public void Flags_an_overrun_instead_of_throwing()
    {
        var reader = new BitReader(new byte[] { 0xFF });

        reader.F(8);
        Assert.False(reader.Overran);

        reader.F(1);
        Assert.True(reader.Overran);
    }

    [Fact]
    public void Skipping_past_the_end_is_an_overrun()
    {
        var reader = new BitReader(new byte[] { 0xFF, 0xFF });
        reader.Skip(17);

        Assert.True(reader.Overran);
    }
}

[Trait("Category", "unit")]
public class H265BitstreamTests
{
    private const int NAL_TRAIL_R = 1;
    private const int NAL_IDR_W_RADL = 19;
    private const int NAL_CRA = 21;
    private const int NAL_VPS = 32;
    private const int NAL_SPS = 33;
    private const int NAL_PPS = 34;
    private const int NAL_AUD = 35;

    /// <summary>A NAL with its two byte H265 header: nal_unit_type, layer 0, temporal id 0.</summary>
    private static byte[] Nal(int type, params byte[] payload)
    {
        var nal = new byte[2 + payload.Length];
        nal[0] = (byte)(type << 1);
        nal[1] = 0x01;
        payload.CopyTo(nal, 2);
        return nal;
    }

    /// <summary>Wraps NALs in an Annex B access unit with 4 byte start codes, as the depacketiser emits.</summary>
    private static byte[] AnnexB(params byte[][] nals)
    {
        var au = new List<byte>();
        foreach (var nal in nals)
        {
            au.AddRange(new byte[] { 0, 0, 0, 1 });
            au.AddRange(nal);
        }
        return au.ToArray();
    }

    /// <summary>
    /// An SPS for the single-layer 4:2:0 8 bit stream WebRTC sends. Field order is H.265 7.3.2.2:
    /// the first byte packs sps_video_parameter_set_id, sps_max_sub_layers_minus1 and
    /// sps_temporal_id_nesting_flag, then the 12 byte profile_tier_level, then the ue(v) fields.
    /// </summary>
    private static byte[] Sps(int width = 640, int height = 360, int chromaFormatIdc = 1,
        int levelIdc = 93, (int Left, int Right, int Top, int Bottom)? conformanceWindow = null)
    {
        var w = new BitWriter()
            .F(4, 0)                        // sps_video_parameter_set_id
            .F(3, 0)                        // sps_max_sub_layers_minus1 -> single layer
            .F(1, 1);                       // sps_temporal_id_nesting_flag

        // profile_tier_level: general_profile_space 0, tier 0, profile_idc 1 (Main), then the
        // 32 compatibility bits, the 48 constraint bits and general_level_idc. 12 bytes exactly.
        w.F(2, 0).F(1, 0).F(5, 1)
         .F(32, 0x60000000)
         .F(24, 0x900000).F(24, 0x000000)
         .F(8, (uint)levelIdc);

        w.Ue(0)                             // sps_seq_parameter_set_id
         .Ue((uint)chromaFormatIdc);
        if (chromaFormatIdc == 3)
        {
            w.Flag(false);                  // separate_colour_plane_flag
        }

        w.Ue((uint)width).Ue((uint)height);

        if (conformanceWindow is { } cw)
        {
            w.Flag(true).Ue((uint)cw.Left).Ue((uint)cw.Right).Ue((uint)cw.Top).Ue((uint)cw.Bottom);
        }
        else
        {
            w.Flag(false);                  // conformance_window_flag
        }

        w.Ue(0)                             // bit_depth_luma_minus8
         .Ue(0)                             // bit_depth_chroma_minus8
         .Align();

        return Nal(NAL_SPS, w.ToArray());
    }

    [Fact]
    public void Reads_the_sps_dimensions()
    {
        var info = H265.TryParseSps(Sps(1280, 720));

        Assert.NotNull(info);
        Assert.Equal(1280, info!.Width);
        Assert.Equal(720, info.Height);
        Assert.Equal(1, info.ChromaFormatIdc);
        Assert.Equal(0, info.BitDepthLumaMinus8);
        Assert.Equal(1, info.NumTemporalLayers);
        Assert.True(info.TemporalIdNested);
    }

    [Fact]
    public void Applies_the_conformance_window_crop()
    {
        // 4:2:0 means the window is in 2x2 chroma units, so a right offset of 2 crops 4 luma
        // columns. Reporting the uncropped size would stretch the picture in the player.
        var info = H265.TryParseSps(Sps(640, 368, conformanceWindow: (0, 2, 0, 4)));

        Assert.NotNull(info);
        Assert.Equal(640 - 2 * 2, info!.Width);
        Assert.Equal(368 - 2 * 4, info.Height);
    }

    [Fact]
    public void A_444_conformance_window_crops_one_to_one()
    {
        var info = H265.TryParseSps(Sps(640, 360, chromaFormatIdc: 3, conformanceWindow: (0, 2, 0, 2)));

        Assert.NotNull(info);
        Assert.Equal(638, info!.Width);
        Assert.Equal(358, info.Height);
    }

    [Fact]
    public void Copies_the_profile_tier_level_verbatim_into_hvcC()
    {
        byte[] au = AnnexB(Nal(NAL_VPS, 0x0C), Sps(levelIdc: 93), Nal(NAL_PPS, 0xC1), Nal(NAL_IDR_W_RADL, 0xAF));

        byte[]? hvcC = H265.TryBuildHvcC(au, out var sps);

        Assert.NotNull(hvcC);
        Assert.NotNull(sps);
        Assert.Equal(1, hvcC![0]);                                  // configurationVersion
        Assert.Equal(sps!.ProfileTierLevel, hvcC.Skip(1).Take(12).ToArray());
        Assert.Equal(93, hvcC[12]);                                 // general_level_idc, last of the 12
        // chromaFormat 1 and both bit depths 0, each in the low bits with the reserved bits set.
        Assert.Equal(0xFC | 1, hvcC[16]);
        Assert.Equal(0xF8 | 0, hvcC[17]);
        Assert.Equal(0xF8 | 0, hvcC[18]);
        Assert.Equal(3, hvcC[22]);                                  // numOfArrays: VPS, SPS, PPS
    }

    [Fact]
    public void The_hvcC_arrays_carry_the_parameter_sets()
    {
        byte[] vps = Nal(NAL_VPS, 0x0C);
        byte[] sps = Sps();
        byte[] pps = Nal(NAL_PPS, 0xC1);

        byte[]? hvcC = H265.TryBuildHvcC(AnnexB(vps, sps, pps, Nal(NAL_IDR_W_RADL, 0xAF)), out _);

        Assert.NotNull(hvcC);
        int offset = 23;                                            // first array header
        foreach (var (type, nal) in new[] { (NAL_VPS, vps), (NAL_SPS, sps), (NAL_PPS, pps) })
        {
            Assert.Equal(type, hvcC![offset] & 0x3F);
            Assert.Equal(1, (hvcC[offset + 1] << 8) | hvcC[offset + 2]);    // numNalus
            int length = (hvcC[offset + 3] << 8) | hvcC[offset + 4];
            Assert.Equal(nal.Length, length);
            Assert.Equal(nal, hvcC.Skip(offset + 5).Take(length).ToArray());
            offset += 5 + length;
        }
        Assert.Equal(hvcC!.Length, offset);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void No_hvcC_until_every_parameter_set_has_arrived(bool vps, bool sps, bool pps)
    {
        var nals = new List<byte[]>();
        if (vps) { nals.Add(Nal(NAL_VPS, 0x0C)); }
        if (sps) { nals.Add(Sps()); }
        if (pps) { nals.Add(Nal(NAL_PPS, 0xC1)); }
        nals.Add(Nal(NAL_IDR_W_RADL, 0xAF));

        Assert.Null(H265.TryBuildHvcC(AnnexB(nals.ToArray()), out _));
    }

    [Theory]
    [InlineData(NAL_IDR_W_RADL, true)]
    [InlineData(NAL_CRA, true)]
    [InlineData(16, true)]          // BLA_W_LP, the first of the IRAP range
    [InlineData(23, true)]          // the last of the IRAP range
    [InlineData(NAL_TRAIL_R, false)]
    [InlineData(24, false)]         // just past the IRAP range
    public void Key_frames_are_the_irap_nal_types(int nalType, bool expected)
    {
        H265.ToLengthPrefixed(AnnexB(Nal(nalType, 0xAF, 0x01)), out bool keyFrame);

        Assert.Equal(expected, keyFrame);
    }

    [Fact]
    public void Length_prefixes_the_nals_and_drops_the_access_unit_delimiter()
    {
        byte[] slice = Nal(NAL_TRAIL_R, 0xAF, 0x01, 0x02);

        byte[] converted = H265.ToLengthPrefixed(AnnexB(Nal(NAL_AUD, 0x50), slice), out _);

        // Only the slice survives, carrying a 4 byte big-endian length in place of the start code.
        Assert.Equal(4 + slice.Length, converted.Length);
        Assert.Equal(slice.Length, (converted[0] << 24) | (converted[1] << 16) | (converted[2] << 8) | converted[3]);
        Assert.Equal(slice, converted.Skip(4).ToArray());
    }

    [Fact]
    public void Strips_emulation_prevention_bytes()
    {
        // 00 00 03 in the NAL is an escaped 00 00; the 0x03 is not part of the syntax and parsing
        // the bits without removing it puts every field after it at the wrong offset.
        byte[] nal = { 0x42, 0x01, 0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x03, 0x02, 0xAA };

        byte[] rbsp = H265.RemoveEmulationPrevention(nal, 2);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x00, 0x02, 0xAA }, rbsp);
    }

    [Fact]
    public void Leaves_a_03_that_is_not_an_escape_alone()
    {
        byte[] nal = { 0x42, 0x01, 0x03, 0x00, 0x03, 0xFF };

        // Only a 0x03 preceded by two zero bytes is an escape; the leading one is real data, and
        // the one after a single 0x00 is too.
        Assert.Equal(new byte[] { 0x03, 0x00, 0x03, 0xFF }, H265.RemoveEmulationPrevention(nal, 2));
    }

    [Fact]
    public void Reads_a_sps_through_its_emulation_prevention_bytes()
    {
        // A width whose ue(v) encoding produces a run of zero bytes forces the escape into the SPS,
        // so this fails if the parser reads the raw NAL instead of the RBSP.
        byte[] sps = Sps(640, 360);
        var escaped = new List<byte>(sps.Take(2));
        int zeros = 0;
        foreach (byte b in sps.Skip(2))
        {
            if (zeros >= 2 && b <= 0x03)
            {
                escaped.Add(0x03);
                zeros = 0;
            }
            escaped.Add(b);
            zeros = b == 0 ? zeros + 1 : 0;
        }

        var info = H265.TryParseSps(escaped.ToArray());

        Assert.NotNull(info);
        Assert.Equal(640, info!.Width);
        Assert.Equal(360, info.Height);
    }

    [Fact]
    public void Rejects_a_truncated_sps()
    {
        byte[] sps = Sps();

        Assert.Null(H265.TryParseSps(sps.AsSpan(0, 8).ToArray()));
    }

    [Fact]
    public void Survives_arbitrary_bytes()
    {
        var random = new Random(99);
        for (int i = 0; i < 200; i++)
        {
            byte[] junk = new byte[random.Next(1, 96)];
            random.NextBytes(junk);

            var exception = Record.Exception(() =>
            {
                H265.TryParseSps(junk);
                H265.TryBuildHvcC(junk, out _);
                H265.ToLengthPrefixed(junk, out _);
            });

            Assert.Null(exception);
        }
    }
}

[Trait("Category", "unit")]
public class ExpGolombTests
{
    [Theory]
    [InlineData(0b1000_0000, 0u)]            // '1'
    [InlineData(0b0100_0000, 1u)]            // '010'
    [InlineData(0b0110_0000, 2u)]            // '011'
    [InlineData(0b0010_0000, 3u)]            // '00100'
    [InlineData(0b0011_1000, 6u)]            // '00111'
    public void Reads_small_exp_golomb_values(byte data, uint expected)
    {
        Assert.Equal(expected, new BitReader(new[] { data }).Ue());
    }

    [Fact]
    public void Round_trips_a_range_of_values()
    {
        foreach (uint value in new uint[] { 0, 1, 2, 7, 8, 100, 639, 640, 1279, 65535 })
        {
            var reader = new BitReader(new BitWriter().Ue(value).Align().ToArray());

            Assert.Equal(value, reader.Ue());
            Assert.False(reader.Overran);
        }
    }

    [Fact]
    public void A_long_run_of_zeroes_is_an_overrun_not_a_hang()
    {
        var reader = new BitReader(new byte[8]);

        reader.Ue();

        Assert.True(reader.Overran);
    }
}
