//-----------------------------------------------------------------------------
// Filename: I420ConverterUnitTest.cs
//
// Description: Unit tests for the conversion of SIPSorcery's pixel formats
// into the AV1 encoder's I420 input buffer.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Av1rt;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.AV1.UnitTest;

/// <summary>
/// The band-parallel, AVX2 conversion must give exactly what the plain per-pixel formulas give
/// (libyuv's C rows), for every format, entry point, awkward size and set of weights.
/// </summary>
[Trait("Category", "unit")]
public class I420ConverterUnitTest
{
    public static readonly TheoryData<VideoPixelFormatsEnum> Formats = new()
    {
        VideoPixelFormatsEnum.Bgra, VideoPixelFormatsEnum.Rgba, VideoPixelFormatsEnum.Bgr,
        VideoPixelFormatsEnum.Rgb, VideoPixelFormatsEnum.NV12, VideoPixelFormatsEnum.I420,
    };

    // (width, height, extra stride bytes): odd sizes, sizes around the 16-pixel vector step, a
    // band count below and above the parallel threshold.
    private static readonly (int W, int H, int Pad)[] Sizes =
    {
        (16, 16, 0), (17, 19, 3), (33, 9, 0), (47, 31, 5), (64, 48, 0), (97, 35, 7), (130, 66, 0),
    };

    // Limited and full range, and luma weights far from BT.709's.
    private static readonly RgbToYuvWeights[] WeightSets =
    {
        Weights(Av1MatrixCoefficients.Bt709, false), Weights(Av1MatrixCoefficients.Bt601, true),
        Weights(Av1MatrixCoefficients.Bt2020Ncl, false),
    };

    internal static RgbToYuvWeights Weights(Av1MatrixCoefficients matrix, bool fullRange)
    {
        Assert.True(RgbToYuvWeights.TryGet(matrix, fullRange, out var weights));
        return weights;
    }

    internal static byte Y(in RgbToYuvWeights w, int r, int g, int b) => checked((byte)((w.YR * r + w.YG * g + w.YB * b + w.YRound) >> 8));
    internal static byte U(in RgbToYuvWeights w, int r, int g, int b) => checked((byte)((w.UR * r + w.UG * g + w.C * b + w.UVRound) >> 8));
    internal static byte V(in RgbToYuvWeights w, int r, int g, int b) => checked((byte)((w.C * r + w.VG * g + w.VB * b + w.UVRound) >> 8));

    /// <summary>The picture converted by the per-pixel formulas, into packed I420.</summary>
    internal static byte[] Reference(byte[] src, int stride, VideoPixelFormatsEnum format, int w, int h, RgbToYuvWeights weights)
    {
        int cw = (w + 1) >> 1, ch = (h + 1) >> 1;
        var dst = new byte[w * h + 2 * cw * ch];
        int uo = w * h, vo = w * h + cw * ch;
        switch (format)
        {
            case VideoPixelFormatsEnum.I420:
            {
                int cs = (stride + 1) >> 1, su = stride * h, sv = su + cs * ch;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        dst[y * w + x] = src[y * stride + x];
                    }
                }
                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < cw; x++)
                    {
                        dst[uo + y * cw + x] = src[su + y * cs + x];
                        dst[vo + y * cw + x] = src[sv + y * cs + x];
                    }
                }
                return dst;
            }
            case VideoPixelFormatsEnum.NV12:
            {
                int suv = stride * h;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        dst[y * w + x] = src[y * stride + x];
                    }
                }
                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < cw; x++)
                    {
                        dst[uo + y * cw + x] = src[suv + y * stride + 2 * x];
                        dst[vo + y * cw + x] = src[suv + y * stride + 2 * x + 1];
                    }
                }
                return dst;
            }
        }
        int bpp = format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba ? 4 : 3;
        bool bgr = format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Bgr;
        int ri = bgr ? 2 : 0, bi = bgr ? 0 : 2;
        int R(int x, int y) => src[y * stride + x * bpp + ri];
        int G(int x, int y) => src[y * stride + x * bpp + 1];
        int B(int x, int y) => src[y * stride + x * bpp + bi];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                dst[y * w + x] = Y(weights, R(x, y), G(x, y), B(x, y));
            }
        }
        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                int x0 = 2 * x, x1 = Math.Min(2 * x + 1, w - 1), y0 = 2 * y, y1 = Math.Min(2 * y + 1, h - 1);
                int Avg(Func<int, int, int> c) =>
                    (((c(x0, y0) + c(x0, y1) + 1) >> 1) + ((c(x1, y0) + c(x1, y1) + 1) >> 1) + 1) >> 1;
                int ar = Avg(R), ag = Avg(G), ab = Avg(B);
                dst[uo + y * cw + x] = U(weights, ar, ag, ab);
                dst[vo + y * cw + x] = V(weights, ar, ag, ab);
            }
        }
        return dst;
    }

    private static byte[] Packed(Av1InputFrame f)
    {
        int w = f.Width, h = f.Height, cw = f.ChromaWidth, ch = f.ChromaHeight;
        var dst = new byte[w * h + 2 * cw * ch];
        for (int y = 0; y < h; y++)
        {
            f.Y.Span.Slice(y * f.StrideY, w).CopyTo(dst.AsSpan(y * w));
        }
        for (int y = 0; y < ch; y++)
        {
            f.U.Span.Slice(y * f.StrideUV, cw).CopyTo(dst.AsSpan(w * h + y * cw));
            f.V.Span.Slice(y * f.StrideUV, cw).CopyTo(dst.AsSpan(w * h + cw * ch + y * cw));
        }
        return dst;
    }

    // The first two rows in 2x2 blocks of black, the primaries, the secondaries and white, where
    // chroma reaches its extremes.
    private static void PaintSaturatedBlocks(byte[] src, int stride, VideoPixelFormatsEnum format, int w)
    {
        int bpp = format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba ? 4 : 3;
        bool bgr = format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Bgr;
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int colour = (x >> 1) & 7, o = y * stride + x * bpp;
                src[o + (bgr ? 2 : 0)] = (byte)((colour & 1) != 0 ? 255 : 0);
                src[o + 1] = (byte)((colour & 2) != 0 ? 255 : 0);
                src[o + (bgr ? 0 : 2)] = (byte)((colour & 4) != 0 ? 255 : 0);
            }
        }
    }

    private enum Entry { Span, Memory, Native }

    [Theory]
    [MemberData(nameof(Formats))]
    public void MatchesPerPixelFormulas(VideoPixelFormatsEnum format)
    {
        var rnd = new Random((int)format);
        RgbToYuvWeights[] weightSets = I420Converter.IsRgb(format) ? WeightSets : [default];
        foreach (var (w, h, pad) in Sizes)
        {
            // NV12 rounds odd widths up to an even stride (whole interleaved chroma rows).
            int stride = I420Converter.MinStride(format, w) + pad;
            var src = new byte[I420Converter.SourceSize(format, stride, w, h)];
            rnd.NextBytes(src);
            if (I420Converter.IsRgb(format))
            {
                PaintSaturatedBlocks(src, stride, format, w);
            }

            using var encoder = new Av1Encoder(new Av1EncoderOptions { Width = w, Height = h, Threads = 1 });
            // A 4-thread encoder that has encoded a frame (so its workers exist) runs the bands.
            using var workers = new Av1Encoder(new Av1EncoderOptions { Width = 320, Height = 256, Threads = 4 });
            workers.GetInputFrame();
            workers.EncodeInputFrame(new ArrayBufferWriter<byte>());
            foreach (RgbToYuvWeights weights in weightSets)
            {
                byte[] want = Reference(src, stride, format, w, h, weights);
                foreach (Entry entry in Enum.GetValues<Entry>())
                {
                    Av1InputFrame frame = encoder.GetInputFrame();
                    frame.Y.Span.Fill(0xEE);
                    frame.U.Span.Fill(0xEE);
                    frame.V.Span.Fill(0xEE);
                    switch (entry)
                    {
                        case Entry.Span:
                            I420Converter.Convert(src, stride, format, weights, frame);
                            break;
                        case Entry.Memory:
                            I420Converter.Convert(src.AsMemory(), stride, format, weights, frame, workers);
                            break;
                        default:
                            IntPtr p = Marshal.AllocHGlobal(src.Length);
                            try
                            {
                                Marshal.Copy(src, 0, p, src.Length);
                                I420Converter.Convert(p, stride, format, weights, frame, workers);
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(p);
                            }
                            break;
                    }
                    byte[] got = Packed(frame);
                    int at = got.AsSpan().CommonPrefixLength(want);
                    Assert.True(at == want.Length, $"{format} {w}x{h} pad {pad} via {entry} with {weights}: differs at byte {at}");
                }
            }
        }
    }
}
