//-----------------------------------------------------------------------------
// Filename: MatroskaTests.cs
//
// Description: Unit tests for the route Matroska sink pieces: the live
// MatroskaWriter's EBML output and the H264 Annex B to ISO/AVC conversions.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text;
using SIPSorcery.Cli.Commands.Route;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class MatroskaTests
{
    private static readonly byte[] Sps = { 0x67, 0x42, 0xC0, 0x1E, 0xDA, 0x02, 0x80 };
    private static readonly byte[] Pps = { 0x68, 0xCE, 0x3C, 0x80 };
    private static readonly byte[] Idr = { 0x65, 0x88, 0x84, 0x00 };
    private static readonly byte[] Aud = { 0x09, 0xF0 };

    private static byte[] AnnexB(params byte[][] nals)
    {
        var output = new List<byte>();
        foreach (var nal in nals)
        {
            output.AddRange(new byte[] { 0, 0, 0, 1 });
            output.AddRange(nal);
        }
        return output.ToArray();
    }

    [Fact]
    public void Header_starts_with_the_EBML_magic_and_matroska_doctype()
    {
        var output = new MemoryStream();
        var writer = new MatroskaWriter(output);

        writer.WriteHeader(new[] { new MatroskaTrack(2, false, "A_OPUS", MatroskaSinkNode.OpusHead(2), SamplingFrequency: 48000, Channels: 2) });

        byte[] bytes = output.ToArray();
        Assert.Equal(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, bytes[..4]);
        Assert.Contains("matroska", Encoding.ASCII.GetString(bytes));
        Assert.Contains("A_OPUS", Encoding.ASCII.GetString(bytes));
        Assert.Equal(bytes.Length, writer.BytesWritten);
    }

    [Fact]
    public void Frame_is_written_as_a_simple_block_in_a_cluster()
    {
        var output = new MemoryStream();
        var writer = new MatroskaWriter(output);
        writer.WriteHeader(new[] { new MatroskaTrack(1, true, "V_VP8", Width: 640, Height: 480) });
        long headerLength = output.Length;

        writer.WriteFrame(1, 0, true, new byte[] { 0xAA, 0xBB });

        byte[] block = output.ToArray()[(int)headerLength..];
        // Cluster ID, unknown size, Timestamp element (0xE7, size 1, value 0).
        Assert.Equal(new byte[] { 0x1F, 0x43, 0xB6, 0x75 }, block[..4]);
        Assert.Equal(new byte[] { 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, block[4..12]);
        Assert.Equal(new byte[] { 0xE7, 0x81, 0x00 }, block[12..15]);
        // SimpleBlock: ID 0xA3, size 6, track 1, relative timestamp 0, key frame flag, payload.
        Assert.Equal(new byte[] { 0xA3, 0x86, 0x81, 0x00, 0x00, 0x80, 0xAA, 0xBB }, block[15..]);
    }

    [Fact]
    public void Blocks_share_a_cluster_until_it_needs_to_roll()
    {
        var output = new MemoryStream();
        var writer = new MatroskaWriter(output);
        writer.WriteHeader(new[] { new MatroskaTrack(2, false, "A_OPUS", SamplingFrequency: 48000, Channels: 2) });
        long headerLength = output.Length;

        writer.WriteFrame(2, 0, true, new byte[] { 1 });
        writer.WriteFrame(2, 20, true, new byte[] { 2 });
        writer.WriteFrame(2, 6000, true, new byte[] { 3 });   // more than 5s on: a new cluster

        byte[] rest = output.ToArray()[(int)headerLength..];
        Assert.Equal(2, CountClusters(rest));
    }

    [Fact]
    public void Writing_a_frame_before_the_header_throws()
    {
        var writer = new MatroskaWriter(new MemoryStream());

        Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(1, 0, true, new byte[] { 1 }));
    }

    [Fact]
    public void Splits_annex_b_into_nal_units()
    {
        byte[] au = AnnexB(Sps, Pps, Idr);

        var nals = MatroskaSinkNode.H264.SplitAnnexB(au);

        Assert.Equal(3, nals.Count);
        Assert.Equal(Sps, au.AsSpan(nals[0].Offset, nals[0].Length).ToArray());
        Assert.Equal(Pps, au.AsSpan(nals[1].Offset, nals[1].Length).ToArray());
        Assert.Equal(Idr, au.AsSpan(nals[2].Offset, nals[2].Length).ToArray());
    }

    [Fact]
    public void Converts_to_length_prefixed_and_detects_the_key_frame()
    {
        byte[] au = AnnexB(Aud, Sps, Pps, Idr);

        byte[] converted = MatroskaSinkNode.H264.ToLengthPrefixed(au, out bool keyFrame);

        Assert.True(keyFrame);
        var expected = new List<byte>();
        foreach (var nal in new[] { Sps, Pps, Idr })   // the access unit delimiter is dropped
        {
            expected.AddRange(new byte[] { 0, 0, 0, (byte)nal.Length });
            expected.AddRange(nal);
        }
        Assert.Equal(expected.ToArray(), converted);
    }

    [Fact]
    public void Non_idr_access_unit_is_not_a_key_frame()
    {
        MatroskaSinkNode.H264.ToLengthPrefixed(AnnexB(new byte[] { 0x41, 0x9A, 0x00 }), out bool keyFrame);

        Assert.False(keyFrame);
    }

    [Fact]
    public void Builds_avcC_from_the_sps_and_pps()
    {
        byte[]? avcC = MatroskaSinkNode.H264.TryBuildAvcC(AnnexB(Sps, Pps, Idr));

        Assert.NotNull(avcC);
        Assert.Equal(new byte[] { 1, 0x42, 0xC0, 0x1E, 0xFF, 0xE1, 0, (byte)Sps.Length }, avcC![..8]);
        Assert.Equal(Sps, avcC[8..(8 + Sps.Length)]);
        int ppsAt = 8 + Sps.Length;
        Assert.Equal(new byte[] { 1, 0, (byte)Pps.Length }, avcC[ppsAt..(ppsAt + 3)]);
        Assert.Equal(Pps, avcC[(ppsAt + 3)..]);
    }

    [Fact]
    public void No_avcC_without_parameter_sets()
    {
        Assert.Null(MatroskaSinkNode.H264.TryBuildAvcC(AnnexB(Idr)));
    }

    [Fact]
    public void Opus_head_is_a_valid_identification_header()
    {
        byte[] head = MatroskaSinkNode.OpusHead(2);

        Assert.Equal(19, head.Length);
        Assert.Equal("OpusHead", Encoding.ASCII.GetString(head, 0, 8));
        Assert.Equal(1, head[8]);
        Assert.Equal(2, head[9]);
        Assert.Equal(48000, BitConverter.ToInt32(head, 12));
    }

    private static int CountClusters(byte[] bytes)
    {
        int count = 0;
        for (int i = 0; i + 3 < bytes.Length; i++)
        {
            if (bytes[i] == 0x1F && bytes[i + 1] == 0x43 && bytes[i + 2] == 0xB6 && bytes[i + 3] == 0x75)
            {
                count++;
            }
        }
        return count;
    }
}
