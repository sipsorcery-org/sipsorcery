//-----------------------------------------------------------------------------
// Filename: denoise_unittest.cs
//
// Description: Checks the port of the denoiser (codec/processing/src/denoise:
// BilateralLumaFilter8_c, WaverageChromaFilter8_c, Gauss3x3Filter and
// CDenoiser) against the C build. Mirrors KernelsDenoise in
// refharness/src/kernels_processing.cpp: each filter on four kinds of content
// and three strides, CDenoiser on the three planes of pictures large and too
// small for the filters, and each plane missing in turn.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;
using static SIPSorcery.H264.UnitTest.PreprocessFixtures;

namespace SIPSorcery.H264.UnitTest;

public unsafe class denoise_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("denoise");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Denoise_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("denoise", kernel, kase, sha1);
        using var hash = new FixtureHasher();
        if (kase == "invalid")
        {
            Invalid(hash);
        }
        else if (kernel == "CDenoiser")
        {
            Denoiser(row, hash);
        }
        else
        {
            Filter(row, hash);
        }
        Assert.Equal(sha1, hash.Hex());
    }

    private static void Filter(KernelFixture row, FixtureHasher hash)
    {
        int stride = row.Param("stride");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var q = new FixturePlane(ref r, stride, 12, stride);
        Shape(ref r, q, row.Param("kind"));
        byte* p = q.P + 4 * stride + 3;
        switch (row.Kernel)
        {
            case nameof(WelsVP.BilateralLumaFilter8_c):
                WelsVP.BilateralLumaFilter8_c(p, stride);
                break;
            case nameof(WelsVP.WaverageChromaFilter8_c):
                WelsVP.WaverageChromaFilter8_c(p, stride);
                break;
            case nameof(WelsVP.Gauss3x3Filter):
                WelsVP.Gauss3x3Filter(p, stride);
                break;
            default:
                throw new ArgumentException($"No C# kernel {row.Kernel}.");
        }
        q.HashInto(hash);
    }

    private static void Denoiser(KernelFixture row, FixtureHasher hash)
    {
        var s = new FixtureYuvSize(row.Param("w"), row.Param("h"), row.Param("stride"), row.Param("uvstride"));
        int kind = row.Param("kind");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var y = new FixtureYuv(ref r, s);
        foreach (FixturePlane q in y.Pl)
        {
            Shape(ref r, q, kind);
        }
        var dn = new CDenoiser(0);
        SPixMap src = y.Map(), dst = default;
        hash.AddI32((int)dn.Process(0, &src, &dst));
        y.HashInto(hash);
    }

    private static void Invalid(FixtureHasher hash)
    {
        var r = new FixtureRng(1200);
        using var y = new FixtureYuv(ref r, new FixtureYuvSize(16, 16, 16, 8));
        var dn = new CDenoiser(0);
        for (int c = 0; c < 3; c++)
        {
            SPixMap src = y.Map(), dst = default;
            src.pPixel[c] = null;
            hash.AddI32((int)dn.Process(0, &src, &dst));
        }
        y.HashInto(hash);
    }
}
