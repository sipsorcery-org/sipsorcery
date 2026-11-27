//-----------------------------------------------------------------------------
// Filename: StreamTests.cs
//
// Description: E5b acceptance: P streams the ported encoder writes, frame by
// frame, for a table of fixed QP configurations must match the C-only
// reference build's byte for byte (refharness `h264ref stream`,
// reference/stream.tsv), and every reconstruction a decoder can see (the
// deblocked reference pictures, and all of them with the filter off) must
// equal what the ported decoder decodes from those bytes.
//
// EncoderDriver (EncoderDriver.cs) drives the ported functions the way
// upstream's frame loop does, for one spatial layer of camera content with
// the analysis features (background detection, adaptive quantisation, scene
// change detection) off.
//
// Set H264_DUMP_DIR to have a failing row write its stream there, to compare
// with `h264ref stream-dump <name> <out>`.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5b).
// 26 Nov 2026  Claude          StreamTable and StreamCheck shared with RcTests, the timestamp
//                              schedules and the mixed content (E6).
// 27 Nov 2026  Claude          The camera, cuts and partcut contents, shared with PreprocessTests,
//                              and the reconstruction check at the coded size (E7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

/// <summary>One configuration of reference/stream.tsv or rc.tsv, with its frame rows.</summary>
public sealed record StreamReference(string Name, string Content, int FrameCount, string Settings)
{
    public int InitReturnCode { get; set; } = int.MinValue;
    public string Sha1 { get; set; }
    public int Size { get; set; }

    /// <summary>Each frame as the harness prints it: index, EncodeFrame, type, temporal ids, layers, SHA1, size.</summary>
    public List<string> Frames { get; } = new();

    public override string ToString() => Name;
}

public static class StreamReferences
{
    public const string TableFile = "stream.tsv";

    private static readonly Lazy<IReadOnlyList<StreamReference>> _rows = new(() => StreamTable.Load(TableFile));

    public static IReadOnlyList<StreamReference> Rows => _rows.Value;

    public static IEnumerable<object[]> Names => Rows.Select(r => new object[] { r.Name });
}

/// <summary>The harness's frame by frame stream tables (h264ref stream, h264ref rc).</summary>
internal static class StreamTable
{
    public static IReadOnlyList<StreamReference> Load(string tableFile)
    {
        string path = Path.Combine(AppContext.BaseDirectory, tableFile);
        var rows = new List<StreamReference>();
        var byName = new Dictionary<string, StreamReference>();
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
                    var r = new StreamReference(f[1], f[2], int.Parse(f[3], CultureInfo.InvariantCulture), f[4]);
                    rows.Add(r);
                    byName.Add(r.Name, r);
                    break;
                case "frame":
                    byName[f[1]].Frames.Add(string.Join('\t', f.Skip(2)));
                    break;
                case "stream":
                    StreamReference c = byName[f[1]];
                    c.InitReturnCode = int.Parse(f[2], CultureInfo.InvariantCulture);
                    c.Sha1 = f[3];
                    c.Size = int.Parse(f[4], CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new InvalidDataException($"{tableFile}: unknown row {f[0]}.");
            }
        }
        return rows;
    }
}

public class StreamTests
{
    private readonly ITestOutputHelper _output;

    public StreamTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TableIsComplete()
    {
        Assert.NotEmpty(StreamReferences.Rows);
        Assert.All(StreamReferences.Rows, r =>
        {
            Assert.Equal(0, r.InitReturnCode);
            Assert.Equal(r.FrameCount, r.Frames.Count);
        });
        Assert.True(StreamReferences.Rows.Sum(r => r.Frames.Count(f => f.Split('\t')[2] == "P")) >= 400);
    }

    [Theory]
    [MemberData(nameof(StreamReferences.Names), MemberType = typeof(StreamReferences))]
    public void StreamMatchesReference(string name) =>
        StreamCheck.Run(StreamReferences.Rows.Single(r => r.Name == name), _output);
}

/// <summary>
/// One configuration's stream through EncoderDriver, frame by frame against the reference
/// rows, then the reconstructions against the ported decoder's pictures. Set H264_DUMP_DIR
/// to have a failing row write its stream there.
/// </summary>
internal static class StreamCheck
{
    private const string DumpEnvironmentVariable = "H264_DUMP_DIR";

    public static void Run(StreamReference expected, ITestOutputHelper output)
    {
        string name = expected.Name;
        var ts = new TimestampSchedule();
        string settings = ts.Split(expected.Settings);

        using var enc = new EncoderDriver();
        Assert.Equal(expected.InitReturnCode, enc.Initialize(settings));

        // The source size, and the reconstruction's: the coded picture's, smaller than the
        // source when the preprocessing downsamples it (E7).
        int w = enc.PicWidth, h = enc.PicHeight;
        int rw = (enc.CodedWidth + 15) & ~15, rh = (enc.CodedHeight + 15) & ~15;
        var chunks = new List<byte[]>();
        var recons = new List<byte[][]>();     // per coded frame, null where the decoder sees another picture
        var stream = new List<byte>();
        for (int f = 0; f < expected.FrameCount; f++)
        {
            EncodedFrame got = enc.EncodeFrame(StreamContent.Fill(w, h, expected.Content, f), ts.At(f));
            string row = string.Join('\t', f.ToString(CultureInfo.InvariantCulture),
                                     got.ReturnCode.ToString(CultureInfo.InvariantCulture), got.FrameType, got.TemporalIds,
                                     got.Layers, got.Sha1, got.Bytes.Length.ToString(CultureInfo.InvariantCulture));
            output.WriteLine(row);
            stream.AddRange(got.Bytes);
            if (row != expected.Frames[f])
            {
                Dump(name, stream);
            }
            Assert.Equal(expected.Frames[f], row);

            if (got.Bytes.Length != 0)
            {
                chunks.Add(got.Bytes);
                recons.Add(got.Comparable ? new[] { enc.Reconstruction(0, rw, rh), enc.Reconstruction(1, rw / 2, rh / 2),
                                                     enc.Reconstruction(2, rw / 2, rh / 2) } : null);
            }
        }
        byte[] all = stream.ToArray();
        Assert.Equal(expected.Sha1, all.Length == 0 ? "-" : Convert.ToHexString(SHA1.HashData(all)).ToLowerInvariant());
        Assert.Equal(expected.Size, all.Length);

        // The reconstructions: the decoder's pictures from these bytes are the encoder's
        // reference pictures, cropped as the SPS says.
        var frames = new List<(int W, int H, byte[][] Planes)>();
        DecodeResult d = DecoderHash.DecodeChunks(chunks, (y, u, v) =>
            frames.Add((y.Width, y.Height, new[] { Copy(y), Copy(u), Copy(v) })));
        Assert.Equal(0, d.ErrorChunks);
        Assert.Equal(chunks.Count, frames.Count);
        int compared = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            if (recons[i] == null)
            {
                continue;
            }
            (int dw, int dh, byte[][] planes) = frames[i];
            for (int p = 0; p < 3; p++)
            {
                int pw = p == 0 ? dw : dw / 2, ph = p == 0 ? dh : dh / 2, stride = p == 0 ? rw : rw / 2;
                for (int r = 0; r < ph; r++)
                {
                    ReadOnlySpan<byte> mine = recons[i][p].AsSpan(r * stride, pw);
                    ReadOnlySpan<byte> theirs = planes[p].AsSpan(r * pw, pw);
                    int same = mine.CommonPrefixLength(theirs);
                    Assert.True(same == pw, $"frame {i} plane {p}: the reconstruction differs from the decoder's at ({same}, {r}).");
                }
            }
            compared++;
        }
        output.WriteLine($"{compared} of {frames.Count} reconstructions compared with the decoder's");

        Assert.Equal(0u, enc.Release());
    }

    private static void Dump(string name, List<byte> stream)
    {
        string dir = Environment.GetEnvironmentVariable(DumpEnvironmentVariable);
        if (!string.IsNullOrEmpty(dir))
        {
            // Compare with h264ref stream-dump (or rc-dump) <name> <out>.
            File.WriteAllBytes(Path.Combine(dir, $"{name}.cs.264"), stream.ToArray());
        }
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
/// The harness's TimestampSchedule (refharness/src/encoder.cpp): the ts.* settings, which
/// the harness keeps out of the encoder's parameters, and the timestamp of frame f. By
/// default f * 1000 / 30 ms.
/// </summary>
internal sealed class TimestampSchedule
{
    private long _num = 1000, _den = 30, _gapMs, _backMs;
    private int _jitter, _gapFrame = -1, _backFrame = -1, _zeroFrame = -1;

    /// <summary>Applies the ts.* items of a settings string and returns the others.</summary>
    public string Split(string settings)
    {
        var rest = new List<string>();
        foreach (string item in settings.Split(';'))
        {
            if (!item.StartsWith("ts.", StringComparison.Ordinal))
            {
                rest.Add(item);
                continue;
            }
            int eq = item.IndexOf('=');
            string k = item[..eq], v = item[(eq + 1)..];
            switch (k)
            {
                case "ts.num": _num = long.Parse(v, CultureInfo.InvariantCulture); break;
                case "ts.den": _den = long.Parse(v, CultureInfo.InvariantCulture); break;
                case "ts.jitter": _jitter = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "ts.gap": FrameAndMs(v, out _gapFrame, out _gapMs); break;
                case "ts.back": FrameAndMs(v, out _backFrame, out _backMs); break;
                case "ts.zero": _zeroFrame = int.Parse(v, CultureInfo.InvariantCulture); break;
                default: throw new InvalidDataException($"Unknown setting {k}.");
            }
        }
        return string.Join(';', rest);
    }

    private static void FrameAndMs(string v, out int frame, out long ms)
    {
        int colon = v.IndexOf(':');
        frame = int.Parse(colon < 0 ? v : v[..colon], CultureInfo.InvariantCulture);
        ms = colon < 0 ? 0 : long.Parse(v[(colon + 1)..], CultureInfo.InvariantCulture);
    }

    public long At(int f)
    {
        long t = f * _num / _den;
        if (_jitter > 0)
        {
            t += (long)(IdrContent.Hash32((uint)f ^ 0x5bd1e995u) % (uint)(2 * _jitter + 1)) - _jitter;
        }
        if (_gapFrame >= 0 && f >= _gapFrame)
        {
            t += _gapMs;
        }
        if (_backFrame >= 0 && f >= _backFrame)
        {
            t -= _backMs;
        }
        if (f == _zeroFrame)
        {
            t = 0;
        }
        return t < 0 ? 0 : t;
    }
}

/// <summary>
/// The synthetic moving content of the reference harness (refharness/src/encoder.cpp
/// StreamSample and FillFrame): frame f as an I420 picture, Y w x h, then U and V
/// (w/2) x (h/2), each packed.
/// </summary>
internal static class StreamContent
{
    // A triangle wave of period 256 over 0..254.
    private static int Tri(int u)
    {
        u &= 255;
        return u < 128 ? u * 2 : (255 - u) * 2;
    }

    private static int Clip255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    public static byte Sample(string kind, int plane, int r, int c, int f)
    {
        int sh = plane != 0 ? 1 : 0;
        switch (kind)
        {
            case "cuts":
                // (E7) a cut every 6 frames between the scene and the inverted, shifted scene.
                if (((f / 6) & 1) != 0)
                {
                    return (byte)(255 - IdrContent.Scene(plane, r + 37 + (f >> sh), c + 91));
                }
                return IdrContent.Scene(plane, r + (f >> sh), c + ((2 * f) >> sh));
            case "partcut":
                // (E7) the top 96 luma rows inverted and shifted for frames 5 to 8.
                if (f >= 5 && f < 9 && r < (96 >> sh))
                {
                    return (byte)(255 - IdrContent.Scene(plane, r + 37, c + 91));
                }
                return IdrContent.Scene(plane, r, c);
            case "camera":
            {
                // (E7) block over a background with new noise of up to 2 either way each frame.
                int bs = 24 >> sh, x0 = (5 * f) >> sh, y0 = (3 * f) >> sh;
                if (r >= y0 && r < y0 + bs && c >= x0 && c < x0 + bs)
                {
                    return IdrContent.Noise(plane + 3, r - y0, c - x0);
                }
                int n = (int)(IdrContent.Hash32((((uint)plane << 28) | ((uint)r << 13) | (uint)c) ^ unchecked((uint)(f + 1) * 0x85ebca6bu)) % 5) - 2;
                return (byte)Clip255(IdrContent.Scene(plane, r, c) + n);
            }
            case "mixed":
            {
                // In cycles of 30 frames: pan (10), random (4), static (8), block (8).
                int k = f % 30;
                if (k < 10)
                {
                    return Sample("pan", plane, r, c, f);
                }
                if (k < 14)
                {
                    return Sample("random", plane, r, c, f);
                }
                if (k < 22)
                {
                    return IdrContent.Scene(plane, r, c);
                }
                return Sample("block", plane, r, c, 2 * (k - 22));
            }
            case "static":
                return IdrContent.Scene(plane, r, c);
            case "pan":
                return IdrContent.Scene(plane, r + ((2 * f) >> sh), c + ((3 * f) >> sh));
            case "block":
            {
                int bs = 24 >> sh, x0 = (5 * f) >> sh, y0 = (3 * f) >> sh;
                if (r >= y0 && r < y0 + bs && c >= x0 && c < x0 + bs)
                {
                    return IdrContent.Noise(plane + 3, r - y0, c - x0);
                }
                return IdrContent.Scene(plane, r, c);
            }
            case "noise":
                return IdrContent.Noise(plane, r + (f >> sh), c + ((2 * f) >> sh));
            case "random":
                return (byte)(IdrContent.Hash32((((uint)plane << 28) | ((uint)r << 13) | (uint)c) ^ unchecked((uint)(f + 1) * 0x9e3779b9u)) >> 24);
            case "fade":
                return (byte)Clip255(IdrContent.Scene(plane, r, c) * (20 - f) / 20 + 6 * f);
            case "cut":
                return f < 4 ? IdrContent.Scene(plane, r, c) : (byte)(255 - IdrContent.Scene(plane, r + 37, c + 91));
            case "subpel":
            {
                int s = plane != 0 ? 8 : 4;
                int a = Tri(s * c + 3 * f), b = Tri(s * r + f);
                return (byte)(plane != 0 ? 64 + ((a + b) >> 2) : (a + b) >> 1);
            }
            default:
                throw new InvalidDataException($"Unknown content {kind}.");
        }
    }

    public static byte[] Fill(int w, int h, string kind, int f)
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
                    buf[offset[plane] + r * pw[plane] + c] = Sample(kind, plane, r, c, f);
                }
            }
        }
        return buf;
    }
}
