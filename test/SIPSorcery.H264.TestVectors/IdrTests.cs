//-----------------------------------------------------------------------------
// Filename: IdrTests.cs
//
// Description: E4b acceptance: the first frame (an IDR) the ported encoder
// writes for a table of fixed QP configurations must match the C-only
// reference build's byte for byte (refharness `h264ref idr`,
// reference/idr.tsv), and its reconstruction (the encoder's deblocked
// reference picture) must equal what the ported decoder decodes from those
// bytes.
//
// The encoder API and its init and frame loop come with E8, so EncoderDriver
// (EncoderDriver.cs) drives the ported functions the way upstream's do, for
// one spatial layer; configurations outside that scope throw
// NotSupportedException, and the reference table has none.
//
// Set H264_DUMP_DIR to have a failing row write its bytes there, to compare
// with `h264ref idr-dump <name> <out>`.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4b).
// 26 Nov 2026  Claude          IdrEncoder moved to EncoderDriver.cs and generalised to P streams (E5b).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

/// <summary>One row of reference/idr.tsv.</summary>
public sealed record IdrReference(string Name, string Content, string Settings)
{
    public int InitReturnCode { get; set; } = int.MinValue;
    public int EncodeReturnCode { get; set; }
    public string FrameType { get; set; }
    public string Layers { get; set; }
    public string Sha1 { get; set; }
    public int Size { get; set; }

    public override string ToString() => Name;
}

public static class IdrReferences
{
    public const string TableFile = "idr.tsv";

    private static readonly Lazy<IReadOnlyList<IdrReference>> _rows = new(Load);

    public static IReadOnlyList<IdrReference> Rows => _rows.Value;

    public static IEnumerable<object[]> Names => Rows.Select(r => new object[] { r.Name });

    private static IReadOnlyList<IdrReference> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, TableFile);
        var rows = new List<IdrReference>();
        var byName = new Dictionary<string, IdrReference>();
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] f = line.Split('\t');
            switch (f[0])
            {
                case "config":
                    var r = new IdrReference(f[1], f[2], f[3]);
                    rows.Add(r);
                    byName.Add(r.Name, r);
                    break;
                case "idr":
                    IdrReference c = byName[f[1]];
                    c.InitReturnCode = int.Parse(f[2], CultureInfo.InvariantCulture);
                    c.EncodeReturnCode = int.Parse(f[3], CultureInfo.InvariantCulture);
                    c.FrameType = f[4];
                    c.Layers = f[5];
                    c.Sha1 = f[6];
                    c.Size = int.Parse(f[7], CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new InvalidDataException($"{TableFile}: unknown row {f[0]}.");
            }
        }
        return rows;
    }
}

public class IdrTests
{
    private const string DumpEnvironmentVariable = "H264_DUMP_DIR";

    private readonly ITestOutputHelper _output;

    public IdrTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TableIsComplete()
    {
        Assert.NotEmpty(IdrReferences.Rows);
        Assert.All(IdrReferences.Rows, r => Assert.NotEqual(int.MinValue, r.InitReturnCode));
        // Most rows code a frame; the two noise rows at a low QP overflow the bitstream buffer.
        Assert.True(IdrReferences.Rows.Count(r => r.FrameType == "IDR") >= 40);
    }

    [Theory]
    [MemberData(nameof(IdrReferences.Names), MemberType = typeof(IdrReferences))]
    public unsafe void IdrMatchesReference(string name)
    {
        IdrReference expected = IdrReferences.Rows.Single(r => r.Name == name);

        using var enc = new EncoderDriver();
        Assert.Equal(expected.InitReturnCode, enc.Initialize(expected.Settings));
        if (expected.InitReturnCode != 0)
        {
            return;
        }

        EncodedFrame got = enc.EncodeFrame(IdrContent.Fill(enc.PicWidth, enc.PicHeight, expected.Content), 0);
        _output.WriteLine($"{name}: EncodeFrame {got.ReturnCode} (WelsEncoderEncodeExt {got.InternalReturnCode}), " +
                          $"{got.FrameType}, {got.Layers}, {got.Bytes.Length} bytes");
        if (got.Sha1 != expected.Sha1 && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DumpEnvironmentVariable)))
        {
            // Compare with h264ref idr-dump <name> <out>.
            File.WriteAllBytes(Path.Combine(Environment.GetEnvironmentVariable(DumpEnvironmentVariable), $"{name}.cs.264"),
                               got.Bytes);
        }
        Assert.Equal(expected.EncodeReturnCode, got.ReturnCode);
        Assert.Equal(expected.FrameType, got.FrameType);
        Assert.Equal(expected.Layers, got.Layers);
        Assert.Equal(expected.Sha1, got.Sha1);
        Assert.Equal(expected.Size, got.Bytes.Length);

        if (got.ReturnCode == 0)
        {
            // The reconstruction: the decoder's picture from these bytes is the encoder's
            // deblocked reference picture, cropped as the SPS says.
            var frames = new List<(int W, int H, byte[][] Planes)>();
            DecodeResult d = DecoderHash.DecodeChunks(new[] { got.Bytes }, (y, u, v) =>
                frames.Add((y.Width, y.Height, new[] { Copy(y), Copy(u), Copy(v) })));
            Assert.Equal(0, d.ErrorChunks);
            Assert.Single(frames);
            (int w, int h, byte[][] planes) = frames[0];
            _output.WriteLine($"decoded {w}x{h}");
            for (int i = 0; i < 3; i++)
            {
                int pw = i == 0 ? w : w / 2, ph = i == 0 ? h : h / 2;
                byte[] recon = enc.Reconstruction(i, pw, ph);
                Assert.Equal(recon.Length, planes[i].Length);
                int same = recon.AsSpan().CommonPrefixLength(planes[i]);
                Assert.True(same == recon.Length,
                            $"plane {i}: the reconstruction differs from the decoder's at ({same % pw}, {same / pw}).");
            }
        }

        Assert.Equal(0u, enc.Release());
    }

    private static unsafe byte[] Copy(DecoderHash.Plane p)
    {
        var a = new byte[p.Width * p.Height];
        for (int r = 0; r < p.Height; r++)
        {
            new ReadOnlySpan<byte>(p.Data + r * p.Stride, p.Width).CopyTo(a.AsSpan(r * p.Width));
        }
        return a;
    }
}

/// <summary>
/// The synthetic source pictures of the reference harness (refharness/src/encoder.cpp
/// FillContent): an I420 picture, Y w x h, then U and V (w/2) x (h/2), each packed.
/// </summary>
internal static class IdrContent
{
    // The "lowbias32" finaliser, so a sample does not depend on the order the planes are filled in.
    public static uint Hash32(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    public static byte Noise(int plane, int r, int c) => (byte)(Hash32(((uint)plane << 28) | ((uint)r << 13) | (uint)c) >> 24);

    // 24x24 luma regions (12x12 chroma), not macroblock aligned, of six kinds.
    public static byte Scene(int plane, int r, int c)
    {
        int s = plane != 0 ? 12 : 24;
        switch (((c / s) * 7 + (r / s) * 3) % 6)
        {
            case 0: return (byte)((60 + 37 * (c / s + r / s) + plane * 50) & 0xff);
            case 1: return (byte)((c * 3 + plane * 40) & 0xff);
            case 2: return (byte)((r * 5 + plane * 90) & 0xff);
            case 3: return Noise(plane, r, c);
            case 4: return (((r >> 2) + (c >> 2)) & 1) != 0 ? (byte)200 : (byte)40;
            default: return (byte)(((r + c) & 7) * 30 + plane * 7);
        }
    }

    public static byte[] Fill(int w, int h, string kind)
    {
        int cw = w >> 1, ch = h >> 1;
        var buf = new byte[w * h + 2 * cw * ch];
        int[] offset = { 0, w * h, w * h + cw * ch };
        int[] pw = { w, cw, cw }, ph = { h, ch, ch };
        for (int plane = 0; plane < 3; plane++)
        {
            for (int r = 0; r < ph[plane]; r++)
            {
                for (int c = 0; c < pw[plane]; c++)
                {
                    buf[offset[plane] + r * pw[plane] + c] = kind switch
                    {
                        "flat" => plane == 0 ? (byte)100 : plane == 1 ? (byte)110 : (byte)150,
                        "gradient" => (byte)((plane == 0 ? c * 4 + r * 4 : plane == 1 ? c * 8 : r * 8) & 0xff),
                        "noise" => Noise(plane, r, c),
                        "scene" => Scene(plane, r, c),
                        _ => throw new InvalidDataException($"Unknown content {kind}."),
                    };
                }
            }
        }
        return buf;
    }
}
