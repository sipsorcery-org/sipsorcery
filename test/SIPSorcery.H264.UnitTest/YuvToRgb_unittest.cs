//-----------------------------------------------------------------------------
// Filename: YuvToRgb_unittest.cs
//
// Description: Unit tests for YuvToRgb.cs, H264Codec's output conversion: the
// fixed point matrices against the BT.601 and BT.709 equations, the choice of
// matrix from an SPS's VUI, and the plane addressing.
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

using System;
using SIPSorcery.H264.Decoder;
using Xunit;

namespace SIPSorcery.H264.UnitTest;

public unsafe class YuvToRgb_unittest
{
    public static TheoryData<string, double, double, bool> Matrices => new()
    {
        { nameof(YuvToRgbMatrix.Bt601Limited), 0.299, 0.114, false },
        { nameof(YuvToRgbMatrix.Bt709Limited), 0.2126, 0.0722, false },
        { nameof(YuvToRgbMatrix.Bt601Full), 0.299, 0.114, true },
        { nameof(YuvToRgbMatrix.Bt709Full), 0.2126, 0.0722, true },
    };

    private static YuvToRgbMatrix Get(string name) => name switch
    {
        nameof(YuvToRgbMatrix.Bt601Limited) => YuvToRgbMatrix.Bt601Limited,
        nameof(YuvToRgbMatrix.Bt709Limited) => YuvToRgbMatrix.Bt709Limited,
        nameof(YuvToRgbMatrix.Bt601Full) => YuvToRgbMatrix.Bt601Full,
        _ => YuvToRgbMatrix.Bt709Full,
    };

    /// <summary>Converts one pixel through ToRgb, as a 1x1 picture.</summary>
    private static (int R, int G, int B) Convert(YuvToRgbMatrix m, byte y, byte u, byte v)
    {
        byte* rgb = stackalloc byte[3];
        var pic = new DecodedPicture { Y = &y, U = &u, V = &v, YStride = 1, UVStride = 1, Width = 1, Height = 1, Matrix = m };
        YuvToRgb.ToRgb(pic, rgb, 3, bgr: false);
        return (rgb[0], rgb[1], rgb[2]);
    }

    /// <summary>The equations of BT.601 / BT.709 (E.2 / Rec. 709 section 3), in floating point.</summary>
    private static (int R, int G, int B) Reference(double kr, double kb, bool full, int y, int u, int v)
    {
        double kg = 1 - kr - kb;
        double yy = full ? y : (y - 16) * 255.0 / 219;
        double cb = (u - 128) * (full ? 1 : 255.0 / 224);
        double cr = (v - 128) * (full ? 1 : 255.0 / 224);
        double r = yy + 2 * (1 - kr) * cr;
        double g = yy - 2 * kb * (1 - kb) / kg * cb - 2 * kr * (1 - kr) / kg * cr;
        double b = yy + 2 * (1 - kb) * cb;
        static int Clamp(double x) => (int)Math.Round(Math.Clamp(x, 0, 255));
        return (Clamp(r), Clamp(g), Clamp(b));
    }

    [Theory]
    [MemberData(nameof(Matrices))]
    public void Matrix_IsWithinOneOfTheEquations(string name, double kr, double kb, bool full)
    {
        YuvToRgbMatrix m = Get(name);
        int worst = 0;
        for (int y = 0; y < 256; y += 3)
        {
            for (int u = 0; u < 256; u += 5)
            {
                for (int v = 0; v < 256; v += 5)
                {
                    var got = Convert(m, (byte)y, (byte)u, (byte)v);
                    var want = Reference(kr, kb, full, y, u, v);
                    worst = Math.Max(worst, Math.Max(Math.Abs(got.R - want.R), Math.Max(Math.Abs(got.G - want.G), Math.Abs(got.B - want.B))));
                }
            }
        }
        Assert.True(worst <= 1, $"{name}: off by up to {worst}.");
    }

    [Fact]
    public void Matrix_MapsBlackAndWhiteExactly()
    {
        Assert.Equal((0, 0, 0), Convert(YuvToRgbMatrix.Bt601Limited, 16, 128, 128));
        Assert.Equal((255, 255, 255), Convert(YuvToRgbMatrix.Bt601Limited, 235, 128, 128));
        Assert.Equal((0, 0, 0), Convert(YuvToRgbMatrix.Bt709Limited, 16, 128, 128));
        Assert.Equal((255, 255, 255), Convert(YuvToRgbMatrix.Bt709Limited, 235, 128, 128));
        Assert.Equal((0, 0, 0), Convert(YuvToRgbMatrix.Bt601Full, 0, 128, 128));
        Assert.Equal((255, 255, 255), Convert(YuvToRgbMatrix.Bt709Full, 255, 128, 128));
        // Limited range footroom and headroom clip.
        Assert.Equal((0, 0, 0), Convert(YuvToRgbMatrix.Bt601Limited, 0, 128, 128));
        Assert.Equal((255, 255, 255), Convert(YuvToRgbMatrix.Bt601Limited, 255, 128, 128));
    }

    [Fact]
    public void FromSps_ReadsTheVui()
    {
        SSps sps = default;
        Assert.Equal(YuvToRgbMatrix.Bt601Limited, YuvToRgbMatrix.FromSps(null));
        Assert.Equal(YuvToRgbMatrix.Bt601Limited, YuvToRgbMatrix.FromSps(&sps));

        // A VUI with no video signal type says nothing about colour.
        sps.bVuiParamPresentFlag = true;
        sps.sVui.bVideoFullRangeFlag = true;
        Assert.Equal(YuvToRgbMatrix.Bt601Limited, YuvToRgbMatrix.FromSps(&sps));

        sps.sVui.bVideoSignalTypePresentFlag = true;
        Assert.Equal(YuvToRgbMatrix.Bt601Full, YuvToRgbMatrix.FromSps(&sps));

        // matrix_coefficients only counts with colour_description_present_flag.
        sps.sVui.uiMatrixCoeffs = 1;
        Assert.Equal(YuvToRgbMatrix.Bt601Full, YuvToRgbMatrix.FromSps(&sps));
        sps.sVui.bColourDescripPresentFlag = true;
        Assert.Equal(YuvToRgbMatrix.Bt709Full, YuvToRgbMatrix.FromSps(&sps));
        sps.sVui.bVideoFullRangeFlag = false;
        Assert.Equal(YuvToRgbMatrix.Bt709Limited, YuvToRgbMatrix.FromSps(&sps));

        // SMPTE 170M (6) and BT.470BG (5) are BT.601; unspecified (2) is taken as BT.601.
        foreach (byte mc in new byte[] { 2, 5, 6 })
        {
            sps.sVui.uiMatrixCoeffs = mc;
            Assert.Equal(YuvToRgbMatrix.Bt601Limited, YuvToRgbMatrix.FromSps(&sps));
        }
    }

    /// <summary>
    /// A 4x2 picture in planes with padded strides: each pixel takes its chroma from the 2x2
    /// block it is in, BGR and RGB are the same pixels with R and B swapped, and CopyI420 packs
    /// the rows.
    /// </summary>
    [Fact]
    public void ToRgbAndCopyI420_AddressThePlanes()
    {
        const int w = 4, h = 2, ys = 8, cs = 5;
        byte[] yPlane = new byte[ys * h];
        byte[] uPlane = new byte[cs];
        byte[] vPlane = new byte[cs];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                yPlane[r * ys + c] = (byte)(60 + 40 * r + 10 * c);
            }
        }
        uPlane[0] = 90; uPlane[1] = 200;
        vPlane[0] = 240; vPlane[1] = 60;
        Array.Fill(yPlane, (byte)0xEE, w, ys - w);   // padding that must not be read

        byte[] rgb = new byte[w * 3 * h];
        byte[] bgr = new byte[(w * 3 + 2) * h];
        byte[] i420 = new byte[w * h + 2 * (w / 2) * (h / 2)];
        fixed (byte* py = yPlane, pu = uPlane, pv = vPlane, prgb = rgb, pbgr = bgr, pi = i420)
        {
            var pic = new DecodedPicture { Y = py, U = pu, V = pv, YStride = ys, UVStride = cs, Width = w, Height = h, Matrix = YuvToRgbMatrix.Bt601Limited };
            Assert.Equal(i420.Length, pic.I420Size);
            YuvToRgb.ToRgb(pic, prgb, w * 3, bgr: false);
            YuvToRgb.ToRgb(pic, pbgr, w * 3 + 2, bgr: true);
            YuvToRgb.CopyI420(pic, pi);
        }

        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                var want = Convert(YuvToRgbMatrix.Bt601Limited, yPlane[r * ys + c], uPlane[c / 2], vPlane[c / 2]);
                int o = r * w * 3 + c * 3;
                Assert.Equal(want, (rgb[o], rgb[o + 1], rgb[o + 2]));
                int ob = r * (w * 3 + 2) + c * 3;
                Assert.Equal(want, (bgr[ob + 2], bgr[ob + 1], bgr[ob]));
            }
        }

        Assert.Equal(new byte[] { 60, 70, 80, 90, 100, 110, 120, 130, 90, 200, 240, 60 }, i420);
    }
}
