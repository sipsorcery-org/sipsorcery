//-----------------------------------------------------------------------------
// Filename: I420Converter.cs
//
// Description: Converts the pixel formats SIPSorcery sources deliver into the
// AV1 encoder's I420 input buffer.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Av1rt;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.AV1;

/// <summary>
/// Converts the pixel formats SIPSorcery sources deliver into the encoder's I420 input buffer. RGB
/// input is converted with the given <see cref="RgbToYuvWeights"/>; I420 and NV12 are copied.
/// </summary>
/// <remarks>
/// The picture is converted in bands of <see cref="BandRows"/> rows, which run in parallel on the
/// encoder's worker threads (<see cref="Av1Encoder.ParallelFor"/>; they are idle between frames).
/// RGB rows and NV12 chroma use AVX2 where available; the result is the same either way.
/// </remarks>
internal static class I420Converter
{
    /// <summary>Rows per band: even, so that a band holds whole chroma rows.</summary>
    internal const int BandRows = 8;

    /// <summary>Converts a picture in a single thread.</summary>
    public static void Convert(ReadOnlySpan<byte> src, int stride, VideoPixelFormatsEnum format, RgbToYuvWeights weights, Av1InputFrame dst)
    {
        Validate(format);
        Layout l = new(format, stride, dst.Width, dst.Height);
        for (int r0 = 0; r0 < dst.Height; r0 += BandRows)
        {
            ConvertBand(src, l, format, weights, dst, r0);
        }
    }

    /// <summary>Converts a picture in bands spread over the threads of <paramref name="workers"/> (null: this thread).</summary>
    public static void Convert(ReadOnlyMemory<byte> src, int stride, VideoPixelFormatsEnum format, RgbToYuvWeights weights,
                               Av1InputFrame dst, Av1Encoder workers)
    {
        Validate(format);
        Layout l = new(format, stride, dst.Width, dst.Height);
        int bands = (dst.Height + BandRows - 1) / BandRows;
        if (workers == null)
        {
            for (int b = 0; b < bands; b++)
            {
                ConvertBand(src.Span, l, format, weights, dst, b * BandRows);
            }
            return;
        }
        workers.ParallelFor(bands, b => ConvertBand(src.Span, l, format, weights, dst, b * BandRows));
    }

    /// <summary>
    /// Converts a picture in native memory (<see cref="RawImage.Sample"/>) in bands spread over the
    /// threads of <paramref name="workers"/> (null: this thread): each band is copied into a small
    /// managed buffer and converted while it is still in cache.
    /// </summary>
    public static void Convert(IntPtr src, int stride, VideoPixelFormatsEnum format, RgbToYuvWeights weights,
                               Av1InputFrame dst, Av1Encoder workers)
    {
        Validate(format);
        Layout l = new(format, stride, dst.Width, dst.Height);
        int bands = (dst.Height + BandRows - 1) / BandRows;
        if (workers == null)
        {
            for (int b = 0; b < bands; b++)
            {
                ConvertNativeBand(src, l, format, weights, dst, b * BandRows);
            }
            return;
        }
        workers.ParallelFor(bands, b => ConvertNativeBand(src, l, format, weights, dst, b * BandRows));
    }

    /// <summary>The minimum source length for a frame of this format, size and (luma or packed) stride.</summary>
    public static int SourceSize(VideoPixelFormatsEnum format, int stride, int width, int height)
    {
        int ch = (height + 1) >> 1;
        return format switch
        {
            VideoPixelFormatsEnum.I420 => stride * height + 2 * ((stride + 1) >> 1) * ch,
            VideoPixelFormatsEnum.NV12 => stride * height + stride * ch,
            _ => stride * height,
        };
    }

    /// <summary>The smallest stride that holds a row of this format and width.</summary>
    /// <remarks>NV12's chroma rows (interleaved U and V, stride shared with luma) need an even stride.</remarks>
    public static int MinStride(VideoPixelFormatsEnum format, int width) => format switch
    {
        VideoPixelFormatsEnum.NV12 => (width + 1) & ~1,
        _ => PackedStride(format, width),
    };

    /// <summary>The natural stride of a tightly packed frame.</summary>
    public static int PackedStride(VideoPixelFormatsEnum format, int width) => format switch
    {
        VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba => width * 4,
        VideoPixelFormatsEnum.Bgr or VideoPixelFormatsEnum.Rgb => width * 3,
        VideoPixelFormatsEnum.NV12 => (width + 1) & ~1,
        _ => width,
    };

    /// <summary>True for the packed RGB formats, which are converted with <see cref="RgbToYuvWeights"/>.</summary>
    public static bool IsRgb(VideoPixelFormatsEnum format) =>
        format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba or VideoPixelFormatsEnum.Bgr or VideoPixelFormatsEnum.Rgb;

    private static void Validate(VideoPixelFormatsEnum format)
    {
        switch (format)
        {
            case VideoPixelFormatsEnum.I420:
            case VideoPixelFormatsEnum.NV12:
            case VideoPixelFormatsEnum.Bgra:
            case VideoPixelFormatsEnum.Rgba:
            case VideoPixelFormatsEnum.Bgr:
            case VideoPixelFormatsEnum.Rgb:
                return;
            default:
                throw new NotSupportedException($"Pixel format {format} is not supported by the AV1 encoder.");
        }
    }

    // Where the source planes are: plane 0 (luma or packed pixels) rows of Stride0 bytes, and for
    // I420/NV12 the chroma plane(s) after it.
    private readonly struct Layout
    {
        public readonly int Stride0, Stride1, Offset1, Offset2, Width, Height;

        public Layout(VideoPixelFormatsEnum format, int stride, int width, int height)
        {
            Width = width;
            Height = height;
            Stride0 = stride;
            int ch = (height + 1) >> 1;
            switch (format)
            {
                case VideoPixelFormatsEnum.I420:
                    Stride1 = (stride + 1) >> 1;
                    Offset1 = stride * height;
                    Offset2 = Offset1 + Stride1 * ch;
                    break;
                case VideoPixelFormatsEnum.NV12:
                    Stride1 = stride;
                    Offset1 = Offset2 = stride * height;
                    break;
                default:
                    Stride1 = Offset1 = Offset2 = 0;
                    break;
            }
        }
    }

    // Rows [r0, r0 + BandRows) of the picture (r0 even) from the whole source picture.
    private static void ConvertBand(ReadOnlySpan<byte> src, in Layout l, VideoPixelFormatsEnum format, in RgbToYuvWeights weights,
                                    Av1InputFrame dst, int r0)
    {
        int rows = Math.Min(BandRows, l.Height - r0), c0 = r0 >> 1, crows = (rows + 1) >> 1;
        ReadOnlySpan<byte> p0 = src.Slice(r0 * l.Stride0);
        ReadOnlySpan<byte> p1 = format is VideoPixelFormatsEnum.I420 or VideoPixelFormatsEnum.NV12 ? src.Slice(l.Offset1 + c0 * l.Stride1) : default;
        ReadOnlySpan<byte> p2 = format == VideoPixelFormatsEnum.I420 ? src.Slice(l.Offset2 + c0 * l.Stride1) : default;
        ConvertRows(format, weights, l.Width, rows, crows, p0, l.Stride0, p1, p2, l.Stride1, dst, r0);
    }

    [ThreadStatic] private static byte[]? t_band;

    private static void ConvertNativeBand(IntPtr src, in Layout l, VideoPixelFormatsEnum format, in RgbToYuvWeights weights,
                                          Av1InputFrame dst, int r0)
    {
        int rows = Math.Min(BandRows, l.Height - r0), c0 = r0 >> 1, crows = (rows + 1) >> 1;
        bool planar = format is VideoPixelFormatsEnum.I420 or VideoPixelFormatsEnum.NV12;
        // The band's rows as the source lays them out; the last row only up to its pixels, so as
        // not to read past the end of the picture.
        int n0 = (rows - 1) * l.Stride0 + RowBytes(format, l.Width);
        int nc = planar ? (crows - 1) * l.Stride1 + (format == VideoPixelFormatsEnum.NV12 ? 2 * ((l.Width + 1) >> 1) : (l.Width + 1) >> 1) : 0;
        int n2 = format == VideoPixelFormatsEnum.I420 ? nc : 0;
        int need = n0 + nc + n2;
        byte[]? band = t_band;
        if (band == null || band.Length < need)
        {
            t_band = band = new byte[Math.Max(need, 1 << 16)];
        }
        Marshal.Copy(IntPtr.Add(src, r0 * l.Stride0), band, 0, n0);
        if (nc > 0)
        {
            Marshal.Copy(IntPtr.Add(src, l.Offset1 + c0 * l.Stride1), band, n0, nc);
        }
        if (n2 > 0)
        {
            Marshal.Copy(IntPtr.Add(src, l.Offset2 + c0 * l.Stride1), band, n0 + nc, n2);
        }
        ReadOnlySpan<byte> b = band;
        ConvertRows(format, weights, l.Width, rows, crows, b.Slice(0, n0), l.Stride0,
                    nc > 0 ? b.Slice(n0, nc) : default, n2 > 0 ? b.Slice(n0 + nc, n2) : default, l.Stride1, dst, r0);
    }

    private static int RowBytes(VideoPixelFormatsEnum format, int width) => format switch
    {
        VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba => width * 4,
        VideoPixelFormatsEnum.Bgr or VideoPixelFormatsEnum.Rgb => width * 3,
        _ => width,
    };

    // Converts `rows` picture rows starting at picture row r0 (even); p0 / p1 / p2 start at the
    // band's first luma / chroma row.
    private static void ConvertRows(VideoPixelFormatsEnum format, in RgbToYuvWeights weights, int width, int rows, int crows,
                                    ReadOnlySpan<byte> p0, int s0, ReadOnlySpan<byte> p1, ReadOnlySpan<byte> p2, int s1,
                                    Av1InputFrame dst, int r0)
    {
        int cw = (width + 1) >> 1;
        Span<byte> y = dst.Y.Span.Slice(r0 * dst.StrideY);
        Span<byte> u = dst.U.Span.Slice((r0 >> 1) * dst.StrideUV);
        Span<byte> v = dst.V.Span.Slice((r0 >> 1) * dst.StrideUV);
        int ys = dst.StrideY, cs = dst.StrideUV;
        switch (format)
        {
            case VideoPixelFormatsEnum.I420:
                for (int r = 0; r < rows; r++)
                {
                    p0.Slice(r * s0, width).CopyTo(y.Slice(r * ys, width));
                }
                for (int r = 0; r < crows; r++)
                {
                    p1.Slice(r * s1, cw).CopyTo(u.Slice(r * cs, cw));
                    p2.Slice(r * s1, cw).CopyTo(v.Slice(r * cs, cw));
                }
                return;
            case VideoPixelFormatsEnum.NV12:
                for (int r = 0; r < rows; r++)
                {
                    p0.Slice(r * s0, width).CopyTo(y.Slice(r * ys, width));
                }
                for (int r = 0; r < crows; r++)
                {
                    SplitUV(p1.Slice(r * s1, 2 * cw), u.Slice(r * cs, cw), v.Slice(r * cs, cw), cw);
                }
                return;
            case VideoPixelFormatsEnum.Bgra:
            case VideoPixelFormatsEnum.Rgba:
            case VideoPixelFormatsEnum.Bgr:
            case VideoPixelFormatsEnum.Rgb:
            {
                int bpp = format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba ? 4 : 3;
                bool bgr = format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Bgr;
                bool bt709 = weights == RgbToYuvWeights.Bt709Limited;
                for (int r = 0; r < rows; r += 2)
                {
                    bool pair = r + 1 < rows;
                    ReadOnlySpan<byte> row0 = p0.Slice(r * s0, width * bpp);
                    ReadOnlySpan<byte> row1 = pair ? p0.Slice((r + 1) * s0, width * bpp) : row0;
                    Span<byte> y0 = y.Slice(r * ys, width);
                    Span<byte> y1 = pair ? y.Slice((r + 1) * ys, width) : default;
                    Span<byte> ur = u.Slice((r >> 1) * cs, cw), vr = v.Slice((r >> 1) * cs, cw);
                    int c = 0;
                    if (bpp == 4 && Avx2.IsSupported)
                    {
                        c = PackedRowPairAvx2(row0, row1, y0, y1, ur, vr, width & ~15, bgr, weights);
                    }
                    if (bt709)
                    {
                        PackedRowPair(row0, row1, y0, y1, ur, vr, c, width, bpp, bgr ? 2 : 0, 1, bgr ? 0 : 2, default(Bt709LimitedWeights));
                    }
                    else
                    {
                        PackedRowPair(row0, row1, y0, y1, ur, vr, c, width, bpp, bgr ? 2 : 0, 1, bgr ? 0 : 2, new VariableWeights(weights));
                    }
                }
                return;
            }
        }
    }

    private static void SplitUV(ReadOnlySpan<byte> uv, Span<byte> u, Span<byte> v, int cw)
    {
        int c = 0;
        if (Avx2.IsSupported)
        {
            // Per lane: the 8 U bytes, then the 8 V bytes; then the lanes' U halves together.
            Vector256<byte> split = Vector256.Create((byte)0, 2, 4, 6, 8, 10, 12, 14, 1, 3, 5, 7, 9, 11, 13, 15,
                                                     0, 2, 4, 6, 8, 10, 12, 14, 1, 3, 5, 7, 9, 11, 13, 15);
            for (; c + 16 <= cw; c += 16)
            {
                Vector256<byte> x = Avx2.Shuffle(Vector256.Create(uv.Slice(2 * c, 32)), split);
                x = Avx2.Permute4x64(x.AsUInt64(), 0xD8).AsByte();
                x.GetLower().CopyTo(u.Slice(c, 16));
                x.GetUpper().CopyTo(v.Slice(c, 16));
            }
        }
        for (; c < cw; c++)
        {
            u[c] = uv[2 * c];
            v[c] = uv[2 * c + 1];
        }
    }

    // Columns [c, width) of a row pair (c even), with chroma from the rounded 2x2 average. row1 is
    // row0 and y1 empty for the last row of a picture of odd height; the last column pairs with
    // itself for an odd width.
    private static void PackedRowPair<TWeights>(ReadOnlySpan<byte> row0, ReadOnlySpan<byte> row1, Span<byte> y0, Span<byte> y1,
                                                Span<byte> u, Span<byte> v, int c, int width, int bpp, int ri, int gi, int bi,
                                                TWeights w)
        where TWeights : struct, IWeights
    {
        int yr = w.YR, yg = w.YG, yb = w.YB, yRound = w.YRound, ur = w.UR, k = w.C, vb = w.VB, uvRound = w.UVRound;
        for (; c < width; c += 2)
        {
            int c1 = c + 1 < width ? c + 1 : c;
            int o00 = c * bpp, o01 = c1 * bpp;
            int r00 = row0[o00 + ri], g00 = row0[o00 + gi], b00 = row0[o00 + bi];
            int r01 = row0[o01 + ri], g01 = row0[o01 + gi], b01 = row0[o01 + bi];
            int r10 = row1[o00 + ri], g10 = row1[o00 + gi], b10 = row1[o00 + bi];
            int r11 = row1[o01 + ri], g11 = row1[o01 + gi], b11 = row1[o01 + bi];
            y0[c] = (byte)((yr * r00 + yg * g00 + yb * b00 + yRound) >> 8);
            if (c + 1 < width)
            {
                y0[c + 1] = (byte)((yr * r01 + yg * g01 + yb * b01 + yRound) >> 8);
            }
            if (!y1.IsEmpty)
            {
                y1[c] = (byte)((yr * r10 + yg * g10 + yb * b10 + yRound) >> 8);
                if (c + 1 < width)
                {
                    y1[c + 1] = (byte)((yr * r11 + yg * g11 + yb * b11 + yRound) >> 8);
                }
            }
            // libyuv AVGB: (a + b + 1) >> 1, rows first then columns.
            int ar = (((r00 + r10 + 1) >> 1) + ((r01 + r11 + 1) >> 1) + 1) >> 1;
            int ag = (((g00 + g10 + 1) >> 1) + ((g01 + g11 + 1) >> 1) + 1) >> 1;
            int ab = (((b00 + b10 + 1) >> 1) + ((b01 + b11 + 1) >> 1) + 1) >> 1;
            int dr = ar - ag, db = ab - ag;
            u[c >> 1] = (byte)((ur * dr + k * db + uvRound) >> 8);
            v[c >> 1] = (byte)((k * dr + vb * db + uvRound) >> 8);
        }
    }

    // The weights PackedRowPair reads. It is specialised for each implementation, so with
    // Bt709LimitedWeights, the default, the weights are constants folded into the multiplies.
    private interface IWeights
    {
        int YR { get; }
        int YG { get; }
        int YB { get; }
        int YRound { get; }
        int UR { get; }
        int C { get; }
        int VB { get; }
        int UVRound { get; }
    }

    // RgbToYuvWeights.Bt709Limited.
    private readonly struct Bt709LimitedWeights : IWeights
    {
        public int YR => 47;
        public int YG => 157;
        public int YB => 16;
        public int YRound => 0x1080;
        public int UR => -26;
        public int C => 112;
        public int VB => -10;
        public int UVRound => 0x8080;
    }

    private readonly struct VariableWeights(RgbToYuvWeights w) : IWeights
    {
        public int YR => w.YR;
        public int YG => w.YG;
        public int YB => w.YB;
        public int YRound => w.YRound;
        public int UR => w.UR;
        public int C => w.C;
        public int VB => w.VB;
        public int UVRound => w.UVRound;
    }

    // The first n16 (a multiple of 16) columns of a 32-bit row pair, as PackedRowPair computes
    // them. Each pixel's bytes 0 and 2 (B and R, or R and B) become one 16-bit pair and bytes 1
    // and 3 (G and A) another, so one pmaddwd per pair gives the weighted sums. Returns n16.
    private static int PackedRowPairAvx2(ReadOnlySpan<byte> row0, ReadOnlySpan<byte> row1, Span<byte> y0, Span<byte> y1,
                                         Span<byte> u, Span<byte> v, int n16, bool bgr, in RgbToYuvWeights w)
    {
        Vector256<short> kY = bgr ? Pairs(w.YB, w.YR) : Pairs(w.YR, w.YB);
        Vector256<short> kU = bgr ? Pairs(w.C, w.UR) : Pairs(w.UR, w.C);
        Vector256<short> kV = bgr ? Pairs(w.VB, w.C) : Pairs(w.C, w.VB);
        Vector256<short> kYg = Pairs(w.YG, 0), kUg = Pairs(w.UG, 0), kVg = Pairs(w.VG, 0);
        Vector256<int> lo = Vector256.Create(0x00FF00FF);
        Vector256<int> roundY = Vector256.Create(w.YRound), roundC = Vector256.Create(w.UVRound);
        Vector256<int> order = Vector256.Create(0, 4, 1, 5, 0, 0, 0, 0);
        bool two = !y1.IsEmpty;

        for (int c = 0; c < n16; c += 16)
        {
            Vector256<byte> a0 = Vector256.Create(row0.Slice(4 * c, 32)), b0 = Vector256.Create(row0.Slice(4 * c + 32, 32));
            Vector256<byte> a1 = Vector256.Create(row1.Slice(4 * c, 32)), b1 = Vector256.Create(row1.Slice(4 * c + 32, 32));

            Luma(a0, b0, kY, kYg, lo, roundY, order).CopyTo(y0.Slice(c, 16));
            if (two)
            {
                Luma(a1, b1, kY, kYg, lo, roundY, order).CopyTo(y1.Slice(c, 16));
            }

            // Rows first, then the even and odd pixels: pixels 0, 2, 8, 10 | 4, 6, 12, 14 (and the
            // odd ones), i.e. chroma 0, 1, 4, 5 | 2, 3, 6, 7, put in order by the qword permute.
            Vector256<byte> ra = Avx2.Average(a0, a1), rb = Avx2.Average(b0, b1);
            Vector256<byte> even = Avx.Shuffle(ra.AsSingle(), rb.AsSingle(), 0x88).AsByte();
            Vector256<byte> odd = Avx.Shuffle(ra.AsSingle(), rb.AsSingle(), 0xDD).AsByte();
            Vector256<int> px = Avx2.Permute4x64(Avx2.Average(even, odd).AsUInt64(), 0xD8).AsInt32();
            Vector256<short> br = (px & lo).AsInt16(), ga = (Avx2.ShiftRightLogical(px, 8) & lo).AsInt16();
            Vector256<int> cu = Avx2.ShiftRightArithmetic(Avx2.MultiplyAddAdjacent(br, kU) + Avx2.MultiplyAddAdjacent(ga, kUg) + roundC, 8);
            Vector256<int> cv = Avx2.ShiftRightArithmetic(Avx2.MultiplyAddAdjacent(br, kV) + Avx2.MultiplyAddAdjacent(ga, kVg) + roundC, 8);
            // dwords: u0-3 v0-3 u0-3 v0-3 | u4-7 v4-7 ...  ->  u0-7 v0-7
            Vector256<byte> uv = Avx2.PackUnsignedSaturate(Avx2.PackSignedSaturate(cu, cv), Avx2.PackSignedSaturate(cu, cv));
            Vector128<ulong> q = Avx2.PermuteVar8x32(uv.AsInt32(), order).GetLower().AsUInt64();
            BinaryPrimitives.WriteUInt64LittleEndian(u.Slice(c >> 1, 8), q.GetElement(0));
            BinaryPrimitives.WriteUInt64LittleEndian(v.Slice(c >> 1, 8), q.GetElement(1));
        }
        return n16;
    }

    private static Vector128<byte> Luma(Vector256<byte> a, Vector256<byte> b, Vector256<short> kY, Vector256<short> kYg,
                                        Vector256<int> lo, Vector256<int> round, Vector256<int> order)
    {
        Vector256<int> ya = Y8(a.AsInt32(), kY, kYg, lo, round), yb = Y8(b.AsInt32(), kY, kYg, lo, round);
        // dwords: a0-3 b0-3 a0-3 b0-3 | a4-7 b4-7 ...  ->  a0-7 b0-7
        Vector256<short> s = Avx2.PackSignedSaturate(ya, yb);
        return Avx2.PermuteVar8x32(Avx2.PackUnsignedSaturate(s, s).AsInt32(), order).GetLower().AsByte();
    }

    private static Vector256<int> Y8(Vector256<int> px, Vector256<short> kY, Vector256<short> kYg, Vector256<int> lo, Vector256<int> round)
    {
        Vector256<short> br = (px & lo).AsInt16(), ga = (Avx2.ShiftRightLogical(px, 8) & lo).AsInt16();
        return Avx2.ShiftRightLogical(Avx2.MultiplyAddAdjacent(br, kY) + Avx2.MultiplyAddAdjacent(ga, kYg) + round, 8);
    }

    // (lo, hi) in every dword, by one broadcast.
    private static Vector256<short> Pairs(short lo, short hi) => Vector256.Create((ushort)lo | (hi << 16)).AsInt16();
}
