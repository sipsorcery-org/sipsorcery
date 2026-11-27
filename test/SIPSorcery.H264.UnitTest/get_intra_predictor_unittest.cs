//-----------------------------------------------------------------------------
// Filename: get_intra_predictor_unittest.cs
//
// Description: Checks the decoder's intra predictors (get_intra_predictor)
// against the C build ("get_intra_predictor" rows in reference/kernels.tsv,
// refharness src/kernels_decoder.cpp KernelsIntraPredDec, whose buffer
// layouts this must follow).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 27 Nov 2026  Claude          The n x n runner shared with the SIMD tests (P5.6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIPSorcery.H264.Decoder.WelsDec;

namespace SIPSorcery.H264.UnitTest;

public unsafe class get_intra_predictor_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("get_intra_predictor");

    // The n x n predictors, with n.
    private static (nint f, int n) Pred(string kernel) => kernel switch
    {
        nameof(WelsI4x4LumaPredV_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredV_c, 4),
        nameof(WelsI4x4LumaPredH_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredH_c, 4),
        nameof(WelsI4x4LumaPredDc_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredDc_c, 4),
        nameof(WelsI4x4LumaPredDcLeft_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredDcLeft_c, 4),
        nameof(WelsI4x4LumaPredDcTop_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredDcTop_c, 4),
        nameof(WelsI4x4LumaPredDcNA_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredDcNA_c, 4),
        nameof(WelsI4x4LumaPredDDL_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredDDL_c, 4),
        nameof(WelsI4x4LumaPredDDLTop_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredDDLTop_c, 4),
        nameof(WelsI4x4LumaPredDDR_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredDDR_c, 4),
        nameof(WelsI4x4LumaPredVL_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredVL_c, 4),
        nameof(WelsI4x4LumaPredVLTop_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredVLTop_c, 4),
        nameof(WelsI4x4LumaPredVR_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredVR_c, 4),
        nameof(WelsI4x4LumaPredHU_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredHU_c, 4),
        nameof(WelsI4x4LumaPredHD_c) => ((nint)(delegate*<byte*, int, void>)&WelsI4x4LumaPredHD_c, 4),
        nameof(WelsIChromaPredV_c) => ((nint)(delegate*<byte*, int, void>)&WelsIChromaPredV_c, 8),
        nameof(WelsIChromaPredH_c) => ((nint)(delegate*<byte*, int, void>)&WelsIChromaPredH_c, 8),
        nameof(WelsIChromaPredPlane_c) => ((nint)(delegate*<byte*, int, void>)&WelsIChromaPredPlane_c, 8),
        nameof(WelsIChromaPredDc_c) => ((nint)(delegate*<byte*, int, void>)&WelsIChromaPredDc_c, 8),
        nameof(WelsIChromaPredDcLeft_c) => ((nint)(delegate*<byte*, int, void>)&WelsIChromaPredDcLeft_c, 8),
        nameof(WelsIChromaPredDcTop_c) => ((nint)(delegate*<byte*, int, void>)&WelsIChromaPredDcTop_c, 8),
        nameof(WelsIChromaPredDcNA_c) => ((nint)(delegate*<byte*, int, void>)&WelsIChromaPredDcNA_c, 8),
        nameof(WelsI16x16LumaPredV_c) => ((nint)(delegate*<byte*, int, void>)&WelsI16x16LumaPredV_c, 16),
        nameof(WelsI16x16LumaPredH_c) => ((nint)(delegate*<byte*, int, void>)&WelsI16x16LumaPredH_c, 16),
        nameof(WelsI16x16LumaPredPlane_c) => ((nint)(delegate*<byte*, int, void>)&WelsI16x16LumaPredPlane_c, 16),
        nameof(WelsI16x16LumaPredDc_c) => ((nint)(delegate*<byte*, int, void>)&WelsI16x16LumaPredDc_c, 16),
        nameof(WelsI16x16LumaPredDcTop_c) => ((nint)(delegate*<byte*, int, void>)&WelsI16x16LumaPredDcTop_c, 16),
        nameof(WelsI16x16LumaPredDcLeft_c) => ((nint)(delegate*<byte*, int, void>)&WelsI16x16LumaPredDcLeft_c, 16),
        nameof(WelsI16x16LumaPredDcNA_c) => ((nint)(delegate*<byte*, int, void>)&WelsI16x16LumaPredDcNA_c, 16),
        _ => (0, 0),
    };

    private static delegate*<byte*, int, bool, bool, void> Pred8x8(string kernel) => kernel switch
    {
        nameof(WelsI8x8LumaPredV_c) => &WelsI8x8LumaPredV_c,
        nameof(WelsI8x8LumaPredH_c) => &WelsI8x8LumaPredH_c,
        nameof(WelsI8x8LumaPredDc_c) => &WelsI8x8LumaPredDc_c,
        nameof(WelsI8x8LumaPredDcLeft_c) => &WelsI8x8LumaPredDcLeft_c,
        nameof(WelsI8x8LumaPredDcTop_c) => &WelsI8x8LumaPredDcTop_c,
        nameof(WelsI8x8LumaPredDcNA_c) => &WelsI8x8LumaPredDcNA_c,
        nameof(WelsI8x8LumaPredDDL_c) => &WelsI8x8LumaPredDDL_c,
        nameof(WelsI8x8LumaPredDDLTop_c) => &WelsI8x8LumaPredDDLTop_c,
        nameof(WelsI8x8LumaPredDDR_c) => &WelsI8x8LumaPredDDR_c,
        nameof(WelsI8x8LumaPredVL_c) => &WelsI8x8LumaPredVL_c,
        nameof(WelsI8x8LumaPredVLTop_c) => &WelsI8x8LumaPredVLTop_c,
        nameof(WelsI8x8LumaPredVR_c) => &WelsI8x8LumaPredVR_c,
        nameof(WelsI8x8LumaPredHU_c) => &WelsI8x8LumaPredHU_c,
        nameof(WelsI8x8LumaPredHD_c) => &WelsI8x8LumaPredHD_c,
        _ => null,
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Predictor_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("get_intra_predictor", kernel, kase, sha1);
        var (f, n) = Pred(kernel);
        if (f != 0)
        {
            Assert.Equal(sha1, Hash((delegate*<byte*, int, void>)f, n, row));
            return;
        }

        delegate*<byte*, int, bool, bool, void> f8 = Pred8x8(kernel);
        Assert.True(f8 != null, $"No C# kernel {kernel}.");
        var r = new FixtureRng((uint)row.Param("seed"));
        int stride = row.Param("stride");
        using var plane = new NativeBuffer(stride * 10);
        r.Fill(plane.Span);

        f8(plane.Ptr + stride + 4, stride, row.Param("tl") != 0, row.Param("tr") != 0);

        Assert.Equal(sha1, FixtureHasher.Of(plane.Span));
    }

    // A random plane of n + 2 rows, the n x n block at row 1, column 4, predicted in place.
    internal static string Hash(delegate*<byte*, int, void> f, int n, KernelFixture row)
    {
        int stride = row.Param("stride");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var plane = new NativeBuffer(stride * (n + 2));
        r.Fill(plane.Span);

        f(plane.Ptr + stride + 4, stride);

        return FixtureHasher.Of(plane.Span);
    }

    [Fact]
    public void Group_HasEveryPredictor()
    {
        Assert.Equal(42, KernelFixtures.Group("get_intra_predictor").Select(r => (string)r[0]).Distinct().Count());
    }
}
