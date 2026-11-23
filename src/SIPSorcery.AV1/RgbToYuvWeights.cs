//-----------------------------------------------------------------------------
// Filename: RgbToYuvWeights.cs
//
// Description: Fixed-point weights for converting RGB to Y'CbCr with the
// matrix coefficients and range an AV1 stream signals.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Av1rt;

namespace SIPSorcery.AV1;

/// <summary>
/// Weights with 8 fractional bits that turn 8-bit RGB into Y'CbCr for one matrix and range, in the
/// form of libyuv's RGBToY / RGBToU / RGBToV (for BT.601 limited range they are libyuv's own):
/// Y = (YR * r + YG * g + YB * b + YRound) &gt;&gt; 8, U = (UR * r + UG * g + C * b + UVRound) &gt;&gt; 8
/// and V = (C * r + VG * g + VB * b + UVRound) &gt;&gt; 8, where C is half the chroma range. The luma
/// weights add up to the luma scale, so white is 235 (255 for full range), and each set of chroma
/// weights adds up to 0, so grey has neutral chroma and U = (UR * (r - g) + C * (b - g) + UVRound)
/// &gt;&gt; 8, likewise V. UVRound is 128.5 in fixed point, or a hair less for full range, whose
/// chroma would otherwise reach 256 for blue and red; so no result needs clamping.
/// </summary>
internal readonly record struct RgbToYuvWeights(short YR, short YG, short YB, int YRound, short UR, short C, short VB, int UVRound)
{
    /// <summary>BT.709 limited range, the default for RGB input.</summary>
    public static readonly RgbToYuvWeights Bt709Limited = new(47, 157, 16, 0x1080, -26, 112, -10, 0x8080);

    public short UG => (short)(-C - UR);

    public short VG => (short)(-C - VB);

    /// <summary>Gets the weights for converting RGB to the given matrix and range.</summary>
    /// <returns>False for matrices that are not a weighting of R, G and B (YCgCo, constant luminance, ICtCp and the like).</returns>
    public static bool TryGet(Av1MatrixCoefficients matrix, bool fullRange, out RgbToYuvWeights weights)
    {
        if (!TryGetLumaWeights(matrix, out double kr, out double kb))
        {
            weights = default;
            return false;
        }

        // Limited range: luma 16..235 and chroma 16..240 of 0..255; full range: both 0..255.
        double lumaScale = (fullRange ? 255 : 219) * 256 / 255.0;
        double chromaScale = (fullRange ? 255 : 224) * 256 / 255.0 / 2;
        short yr = Round(kr * lumaScale), yb = Round(kb * lumaScale);
        short yg = (short)(Round(lumaScale) - yr - yb);
        short c = Round(chromaScale);
        short ur = (short)-Round(chromaScale * kr / (1 - kb));
        short vb = (short)-Round(chromaScale * kb / (1 - kr));
        int yRound = (fullRange ? 0 : 16 << 8) + 0x80;
        weights = new RgbToYuvWeights(yr, yg, yb, yRound, ur, c, vb, fullRange ? 0x807F : 0x8080);
        return true;
    }

    // KR and KB of the non-constant-luminance matrices, from ITU-T H.273 table 4.
    private static bool TryGetLumaWeights(Av1MatrixCoefficients matrix, out double kr, out double kb)
    {
        (kr, kb) = matrix switch
        {
            Av1MatrixCoefficients.Bt709 => (0.2126, 0.0722),
            Av1MatrixCoefficients.Fcc => (0.30, 0.11),
            Av1MatrixCoefficients.Bt470BG or Av1MatrixCoefficients.Bt601 => (0.299, 0.114),
            Av1MatrixCoefficients.Smpte240 => (0.212, 0.087),
            Av1MatrixCoefficients.Bt2020Ncl => (0.2627, 0.0593),
            _ => (0.0, 0.0),
        };
        return kr != 0;
    }

    private static short Round(double x) => (short)Math.Round(x, MidpointRounding.AwayFromZero);
}
