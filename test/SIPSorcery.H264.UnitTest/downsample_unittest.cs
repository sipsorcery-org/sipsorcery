//-----------------------------------------------------------------------------
// Filename: downsample_unittest.cs
//
// Description: Checks the port of the downsampling (codec/processing/src/
// downsample: the dyadic, quarter, one-third and general ratio kernels and
// CDownsampling) against the C build. Mirrors KernelsDownsample in
// refharness/src/kernels_processing.cpp: each kernel on whole and odd sizes,
// then CDownsampling::Process down each of its paths, within the sample
// buffers (repeated halving, then the general ratio) and past them.
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

namespace SIPSorcery.H264.UnitTest;

public unsafe class downsample_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("downsample");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Downsample_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("downsample", kernel, kase, sha1);
        using var hash = new FixtureHasher();
        if (kernel == "CDownsampling")
        {
            Process(row, hash);
        }
        else
        {
            Kernel(row, hash);
        }
        Assert.Equal(sha1, hash.Hex());
    }

    private static void Kernel(KernelFixture row, FixtureHasher hash)
    {
        int dw = row.Param("dw"), dh = row.Param("dh"), sw = row.Param("sw"), sh = row.Param("sh");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var src = new FixturePlane(ref r, sw, sh + 2, sw + 16);
        using var dst = new FixturePlane(ref r, dw, dh + 2, dw + 8);
        switch (row.Kernel)
        {
            case nameof(WelsVP.DyadicBilinearDownsampler_c):
                WelsVP.DyadicBilinearDownsampler_c(dst.P, dst.Stride, src.P, src.Stride, sw, sh);
                break;
            case nameof(WelsVP.DyadicBilinearQuarterDownsampler_c):
                WelsVP.DyadicBilinearQuarterDownsampler_c(dst.P, dst.Stride, src.P, src.Stride, sw, sh);
                break;
            case nameof(WelsVP.DyadicBilinearOneThirdDownsampler_c):
                WelsVP.DyadicBilinearOneThirdDownsampler_c(dst.P, dst.Stride, src.P, src.Stride, sw, dh);
                break;
            case nameof(WelsVP.GeneralBilinearFastDownsampler_c):
                WelsVP.GeneralBilinearFastDownsampler_c(dst.P, dst.Stride, dw, dh, src.P, src.Stride, sw, sh);
                break;
            case nameof(WelsVP.GeneralBilinearAccurateDownsampler_c):
                WelsVP.GeneralBilinearAccurateDownsampler_c(dst.P, dst.Stride, dw, dh, src.P, src.Stride, sw, sh);
                break;
            default:
                throw new ArgumentException($"No C# kernel {row.Kernel}.");
        }
        dst.HashInto(hash);
    }

    private static void Process(KernelFixture row, FixtureHasher hash)
    {
        var r = new FixtureRng((uint)row.Param("seed"));
        using var src = new FixtureYuv(ref r, new FixtureYuvSize(row.Param("sw"), row.Param("sh"), row.Param("ss"), row.Param("ssuv")));
        using var dst = new FixtureYuv(ref r, new FixtureYuvSize(row.Param("dw"), row.Param("dh"), row.Param("ds"), row.Param("dsuv")));
        var ds = new CDownsampling(0);
        try
        {
            SPixMap ms = src.Map(), md = dst.Map();
            hash.AddI32((int)ds.Process(0, &ms, &md));
        }
        finally
        {
            ds.Destroy();
        }
        dst.HashInto(hash);
    }
}
