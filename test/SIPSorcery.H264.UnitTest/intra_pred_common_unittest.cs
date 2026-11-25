//-----------------------------------------------------------------------------
// Filename: intra_pred_common_unittest.cs
//
// Description: Checks the port of codec/common/src/intra_pred_common.cpp
// against the C build. Mirrors KernelsIntraPred in
// refharness/src/kernels_mc.cpp.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class intra_pred_common_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("intra_pred");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Predictor_MatchesCBuild(string kernel, string kase, string sha1)
    {
        delegate*<byte*, byte*, int, void> f = kernel switch
        {
            nameof(WelsI16x16LumaPredV_c) => &WelsI16x16LumaPredV_c,
            nameof(WelsI16x16LumaPredH_c) => &WelsI16x16LumaPredH_c,
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        var row = new KernelFixture("intra_pred", kernel, kase, sha1);
        int stride = row.Param("stride");

        // Reference: 18 rows, the block at row 1, column 1. Prediction: 16x16 at
        // stride 16 with 16 guard bytes either side.
        var r = new FixtureRng((uint)row.Param("seed"));
        using var refPlane = new NativeBuffer(stride * 18);
        using var pred = new NativeBuffer(256 + 32);
        r.Fill(refPlane.Span);
        r.Fill(pred.Span);

        f(pred.Ptr + 16, refPlane.Ptr + stride + 1, stride);

        Assert.Equal(sha1, FixtureHasher.Of(pred.Span));
    }
}
