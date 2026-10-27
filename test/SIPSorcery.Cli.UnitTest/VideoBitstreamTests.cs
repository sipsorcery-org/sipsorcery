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
