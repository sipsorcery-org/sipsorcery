//-----------------------------------------------------------------------------
// Filename: YuvToRgb.cs
//
// Description: Copies and converts the decoder's output pictures for
// H264Codec: packing the three strided I420 planes into one contiguous I420
// buffer, or converting them to 24-bit BGR or RGB with the colour matrix and
// range the stream signals. Not part of the OpenH264 port.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorcery.H264.Decoder;

namespace SIPSorcery.H264;

/// <summary>
/// The YUV to RGB conversion for one colour matrix and range, in 8 fractional bits:
/// <c>R = ((Y - YOffset) * YScale + RFromV * V' + 128) &gt;&gt; 8</c> and so on, with
/// <c>U' = U - 128</c> and <c>V' = V - 128</c>.
/// </summary>
internal readonly struct YuvToRgbMatrix
{
    public readonly int YOffset;
    public readonly int YScale;
    public readonly int RFromV;
    public readonly int GFromU;
    public readonly int GFromV;
    public readonly int BFromU;

    private YuvToRgbMatrix(int yOffset, int yScale, int rFromV, int gFromU, int gFromV, int bFromU)
    {
        YOffset = yOffset;
        YScale = yScale;
        RFromV = rFromV;
        GFromU = gFromU;
        GFromV = gFromV;
        BFromU = bFromU;
    }

    // Kr = 0.299 and Kb = 0.114 (BT.601), Kr = 0.2126 and Kb = 0.0722 (BT.709). Limited range
    // scales luma by 255/219 and chroma by 255/224; full range scales neither.
    public static readonly YuvToRgbMatrix Bt601Limited = new(16, 298, 409, 100, 208, 516);
    public static readonly YuvToRgbMatrix Bt709Limited = new(16, 298, 459, 55, 136, 541);
    public static readonly YuvToRgbMatrix Bt601Full = new(0, 256, 359, 88, 183, 454);
    public static readonly YuvToRgbMatrix Bt709Full = new(0, 256, 403, 48, 120, 475);

    /// <summary>
    /// The matrix an SPS signals in its VUI: BT.709 when matrix_coefficients is 1, otherwise
    /// BT.601, which is also what a stream that signals nothing is taken to be, as browsers
    /// take it; full range when video_full_range_flag is set, otherwise limited range.
    /// </summary>
    public static unsafe YuvToRgbMatrix FromSps(SSps* sps)
    {
        if (sps == null || !sps->bVuiParamPresentFlag || !sps->sVui.bVideoSignalTypePresentFlag)
        {
            return Bt601Limited;
        }

        bool bt709 = sps->sVui.bColourDescripPresentFlag && sps->sVui.uiMatrixCoeffs == 1;
        bool full = sps->sVui.bVideoFullRangeFlag;
        return full ? (bt709 ? Bt709Full : Bt601Full) : (bt709 ? Bt709Limited : Bt601Limited);
    }
}

/// <summary>One decoded picture as the decoder outputs it: three planes, cropped, 4:2:0.</summary>
internal unsafe struct DecodedPicture
{
    public byte* Y;
    public byte* U;
    public byte* V;
    public int YStride;
    public int UVStride;
    public int Width;
    public int Height;
    public YuvToRgbMatrix Matrix;

    public int ChromaWidth => (Width + 1) / 2;
    public int ChromaHeight => (Height + 1) / 2;

    /// <summary>The size of the picture as contiguous I420: the Y plane, then U, then V.</summary>
    public int I420Size => Width * Height + 2 * ChromaWidth * ChromaHeight;
}

internal static unsafe class YuvToRgb
{
    /// <summary>Packs the picture's planes into <paramref name="dst"/> as contiguous I420.</summary>
    public static void CopyI420(in DecodedPicture pic, byte* dst)
    {
        CopyPlane(pic.Y, pic.YStride, pic.Width, pic.Height, dst);
        dst += pic.Width * pic.Height;
        int cw = pic.ChromaWidth, ch = pic.ChromaHeight;
        CopyPlane(pic.U, pic.UVStride, cw, ch, dst);
        CopyPlane(pic.V, pic.UVStride, cw, ch, dst + cw * ch);
    }

    private static void CopyPlane(byte* src, int srcStride, int width, int height, byte* dst)
    {
        for (int row = 0; row < height; row++)
        {
            Buffer.MemoryCopy(src + (nint)row * srcStride, dst + (nint)row * width, width, width);
        }
    }

    /// <summary>
    /// Converts the picture to packed 24-bit pixels, rows <paramref name="dstStride"/> bytes
    /// apart: B, G, R in memory when <paramref name="bgr"/> is set, otherwise R, G, B.
    /// </summary>
    public static void ToRgb(in DecodedPicture pic, byte* dst, int dstStride, bool bgr)
    {
        YuvToRgbMatrix m = pic.Matrix;
        int ri = bgr ? 2 : 0;
        int bi = bgr ? 0 : 2;

        for (int row = 0; row < pic.Height; row++)
        {
            byte* y = pic.Y + (nint)row * pic.YStride;
            byte* u = pic.U + (nint)(row >> 1) * pic.UVStride;
            byte* v = pic.V + (nint)(row >> 1) * pic.UVStride;
            byte* d = dst + (nint)row * dstStride;

            for (int col = 0; col < pic.Width; col++, d += 3)
            {
                int c = (y[col] - m.YOffset) * m.YScale + 128;
                int du = u[col >> 1] - 128;
                int dv = v[col >> 1] - 128;
                d[ri] = Clamp((c + m.RFromV * dv) >> 8);
                d[1] = Clamp((c - m.GFromU * du - m.GFromV * dv) >> 8);
                d[bi] = Clamp((c + m.BFromU * du) >> 8);
            }
        }
    }

    private static byte Clamp(int x) => (uint)x <= 255 ? (byte)x : x < 0 ? (byte)0 : (byte)255;
}
