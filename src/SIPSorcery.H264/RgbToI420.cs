//-----------------------------------------------------------------------------
// Filename: RgbToI420.cs
//
// Description: Puts the H.264 encoder's input into I420: packed RGB is
// converted with BT.601 limited range, the colour a stream that signals nothing
// is taken to be, NV12 is split into its planes and I420 is passed through. Not
// part of the OpenH264 port itself.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E8).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.H264;

/// <summary>
/// The pixel formats <see cref="H264Codec"/> encodes from and their layouts. I420 and NV12
/// samples have the chroma plane(s) straight after the luma rows: I420's U and V planes have a
/// stride of half the luma stride, rounded up, and NV12's interleaved plane shares the luma stride.
/// </summary>
internal static unsafe class RgbToI420
{
    // libyuv's RGBToY, RGBToU and RGBToV, BT.601 limited range with 8 fractional bits:
    // Y = (66 R + 129 G + 25 B + 0x1080) >> 8, U = (112 B - 74 G - 38 R + 0x8080) >> 8 and
    // V = (112 R - 94 G - 18 B + 0x8080) >> 8, so white is 235 and grey has chroma 128.
    private const int YR = 66, YG = 129, YB = 25, Y_ROUND = 0x1080;
    private const int UR = -38, UG = -74, UB = 112;
    private const int VR = 112, VG = -94, VB = -18, UV_ROUND = 0x8080;

    /// <summary>True for the formats the encoder takes.</summary>
    public static bool IsSupported(VideoPixelFormatsEnum format) =>
        format is VideoPixelFormatsEnum.I420 or VideoPixelFormatsEnum.NV12 or VideoPixelFormatsEnum.Bgra
            or VideoPixelFormatsEnum.Rgba or VideoPixelFormatsEnum.Bgr or VideoPixelFormatsEnum.Rgb;

    /// <summary>The natural stride of a tightly packed frame.</summary>
    public static int PackedStride(VideoPixelFormatsEnum format, int width) => format switch
    {
        VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba => width * 4,
        VideoPixelFormatsEnum.Bgr or VideoPixelFormatsEnum.Rgb => width * 3,
        VideoPixelFormatsEnum.NV12 => (width + 1) & ~1,
        _ => width,
    };

    /// <summary>The smallest stride that holds a row of this format and width.</summary>
    /// <remarks>NV12's chroma rows (interleaved U and V, stride shared with luma) need an even stride.</remarks>
    public static int MinStride(VideoPixelFormatsEnum format, int width) => PackedStride(format, width);

    /// <summary>The minimum sample length for a frame of this format, size and (luma or packed) stride.</summary>
    public static long SourceSize(VideoPixelFormatsEnum format, int stride, int width, int height)
    {
        long ch = (height + 1) >> 1;
        return format switch
        {
            VideoPixelFormatsEnum.I420 => (long)stride * height + 2 * ((stride + 1) >> 1) * ch,
            VideoPixelFormatsEnum.NV12 => (long)stride * height + stride * ch,
            _ => (long)stride * height,
        };
    }

    /// <summary>
    /// Splits an NV12 frame's interleaved chroma into the U and V planes of an I420 frame. The luma
    /// plane is not copied: the encoder reads it from the source.
    /// </summary>
    public static void SplitNv12(byte* src, int stride, int width, int height, byte* u, byte* v, int uvStride)
    {
        int cw = (width + 1) >> 1, ch = (height + 1) >> 1;
        byte* uv = src + (long)stride * height;
        for (int r = 0; r < ch; r++)
        {
            byte* s = uv + (long)r * stride;
            byte* du = u + (long)r * uvStride;
            byte* dv = v + (long)r * uvStride;
            for (int c = 0; c < cw; c++)
            {
                du[c] = s[2 * c];
                dv[c] = s[2 * c + 1];
            }
        }
    }

    /// <summary>
    /// Converts a packed RGB frame (BGRA, RGBA, BGR or RGB) to I420 planes, with chroma from the
    /// rounded average of each 2x2 block. For an odd width or height the last column or row
    /// pairs with itself.
    /// </summary>
    public static void ConvertRgb(byte* src, int stride, VideoPixelFormatsEnum format, int width, int height,
                                  byte* y, int yStride, byte* u, byte* v, int uvStride)
    {
        int bpp = format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba ? 4 : 3;
        bool bgr = format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Bgr;
        int ri = bgr ? 2 : 0, gi = 1, bi = bgr ? 0 : 2;

        for (int r = 0; r < height; r += 2)
        {
            bool pair = r + 1 < height;
            byte* row0 = src + (long)r * stride;
            byte* row1 = pair ? row0 + stride : row0;
            byte* y0 = y + (long)r * yStride;
            byte* y1 = pair ? y0 + yStride : null;
            byte* du = u + (long)(r >> 1) * uvStride;
            byte* dv = v + (long)(r >> 1) * uvStride;

            for (int c = 0; c < width; c += 2)
            {
                int c1 = c + 1 < width ? c + 1 : c;
                byte* p00 = row0 + c * bpp, p01 = row0 + c1 * bpp;
                byte* p10 = row1 + c * bpp, p11 = row1 + c1 * bpp;
                int r00 = p00[ri], g00 = p00[gi], b00 = p00[bi];
                int r01 = p01[ri], g01 = p01[gi], b01 = p01[bi];
                int r10 = p10[ri], g10 = p10[gi], b10 = p10[bi];
                int r11 = p11[ri], g11 = p11[gi], b11 = p11[bi];

                y0[c] = Luma(r00, g00, b00);
                if (c + 1 < width)
                {
                    y0[c + 1] = Luma(r01, g01, b01);
                }
                if (pair)
                {
                    y1[c] = Luma(r10, g10, b10);
                    if (c + 1 < width)
                    {
                        y1[c + 1] = Luma(r11, g11, b11);
                    }
                }

                // libyuv's AVGB, (a + b + 1) >> 1, down the rows and then across.
                int ar = (((r00 + r10 + 1) >> 1) + ((r01 + r11 + 1) >> 1) + 1) >> 1;
                int ag = (((g00 + g10 + 1) >> 1) + ((g01 + g11 + 1) >> 1) + 1) >> 1;
                int ab = (((b00 + b10 + 1) >> 1) + ((b01 + b11 + 1) >> 1) + 1) >> 1;
                du[c >> 1] = (byte)((UR * ar + UG * ag + UB * ab + UV_ROUND) >> 8);
                dv[c >> 1] = (byte)((VR * ar + VG * ag + VB * ab + UV_ROUND) >> 8);
            }
        }
    }

    private static byte Luma(int r, int g, int b) => (byte)((YR * r + YG * g + YB * b + Y_ROUND) >> 8);
}
