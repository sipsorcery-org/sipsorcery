//-----------------------------------------------------------------------------
// Filename: RgbToYuvWeightsUnitTest.cs
//
// Description: Unit tests for the fixed-point RGB to Y'CbCr weights, against
// libyuv's and the floating-point definitions of each matrix and range.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Av1rt;
using Xunit;
using static SIPSorcery.AV1.UnitTest.I420ConverterUnitTest;

namespace SIPSorcery.AV1.UnitTest;

[Trait("Category", "unit")]
public class RgbToYuvWeightsUnitTest
{
    // KR and KB of each supported matrix (ITU-T H.273 table 4), and both ranges.
    public static readonly TheoryData<Av1MatrixCoefficients, double, double, bool> Matrices = new()
    {
        { Av1MatrixCoefficients.Bt709, 0.2126, 0.0722, false }, { Av1MatrixCoefficients.Bt709, 0.2126, 0.0722, true },
        { Av1MatrixCoefficients.Fcc, 0.30, 0.11, false }, { Av1MatrixCoefficients.Fcc, 0.30, 0.11, true },
        { Av1MatrixCoefficients.Bt470BG, 0.299, 0.114, false }, { Av1MatrixCoefficients.Bt470BG, 0.299, 0.114, true },
        { Av1MatrixCoefficients.Bt601, 0.299, 0.114, false }, { Av1MatrixCoefficients.Bt601, 0.299, 0.114, true },
        { Av1MatrixCoefficients.Smpte240, 0.212, 0.087, false }, { Av1MatrixCoefficients.Smpte240, 0.212, 0.087, true },
        { Av1MatrixCoefficients.Bt2020Ncl, 0.2627, 0.0593, false }, { Av1MatrixCoefficients.Bt2020Ncl, 0.2627, 0.0593, true },
    };

    [Fact]
    public void LimitedRangeWeightsAreLibyuvsForBt601AndTheAdaptersForBt709()
    {
        RgbToYuvWeights bt601 = Weights(Av1MatrixCoefficients.Bt601, false), bt709 = Weights(Av1MatrixCoefficients.Bt709, false);
        Assert.Equal(new RgbToYuvWeights(66, 129, 25, 0x1080, -38, 112, -18, 0x8080), bt601);
        Assert.Equal(-74, bt601.UG);
        Assert.Equal(-94, bt601.VG);
        Assert.Equal(new RgbToYuvWeights(47, 157, 16, 0x1080, -26, 112, -10, 0x8080), bt709);
        Assert.Equal(RgbToYuvWeights.Bt709Limited, bt709);
        Assert.Equal(-86, bt709.UG);
        Assert.Equal(-102, bt709.VG);
    }

    // Within 1.5 levels of the definition (8-bit weights are off by up to a level, the shift by half
    // a level): Y' = KR R + (1 - KR - KB) G + KB B, scaled to 219 (or 255) above 16 (or 0), and Cb
    // and Cr = (B or R - Y') / (2 (1 - KB or KR)), scaled to 224 (or 255) around 128. Exact for
    // black, white and grey.
    [Theory]
    [MemberData(nameof(Matrices))]
    public void MatchesTheDefinition(Av1MatrixCoefficients matrix, double kr, double kb, bool fullRange)
    {
        RgbToYuvWeights w = Weights(matrix, fullRange);
        double ys = fullRange ? 255 : 219, cs = fullRange ? 255 : 224, y0 = fullRange ? 0 : 16;
        for (int r = 0; r < 256; r += 15)
        {
            for (int g = 0; g < 256; g += 15)
            {
                for (int b = 0; b < 256; b += 15)
                {
                    double luma = (kr * r + (1 - kr - kb) * g + kb * b) / 255;
                    double y = y0 + ys * luma;
                    double u = Math.Min(128 + cs * (b / 255.0 - luma) / (2 * (1 - kb)), 255);
                    double v = Math.Min(128 + cs * (r / 255.0 - luma) / (2 * (1 - kr)), 255);
                    Assert.InRange(Y(w, r, g, b) - y, -1.5, 1.5);
                    Assert.InRange(U(w, r, g, b) - u, -1.5, 1.5);
                    Assert.InRange(V(w, r, g, b) - v, -1.5, 1.5);
                }
            }
        }
        for (int grey = 0; grey < 256; grey++)
        {
            Assert.Equal(128, U(w, grey, grey, grey));
            Assert.Equal(128, V(w, grey, grey, grey));
        }
        Assert.Equal(fullRange ? 0 : 16, Y(w, 0, 0, 0));
        Assert.Equal(fullRange ? 255 : 235, Y(w, 255, 255, 255));
    }

    [Theory]
    [InlineData(Av1MatrixCoefficients.Unspecified)]
    [InlineData(Av1MatrixCoefficients.YCgCo)]
    [InlineData(Av1MatrixCoefficients.Bt2020Cl)]
    [InlineData(Av1MatrixCoefficients.ICtCp)]
    public void MatricesThatAreNotRgbWeightingsHaveNoWeights(Av1MatrixCoefficients matrix)
    {
        Assert.False(RgbToYuvWeights.TryGet(matrix, false, out _));
    }
}
