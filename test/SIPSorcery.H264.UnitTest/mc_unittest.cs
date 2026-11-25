//-----------------------------------------------------------------------------
// Filename: mc_unittest.cs
//
// Description: Checks the port of the C paths of codec/common/src/mc.cpp
// against the C build. Mirrors KernelsMc in refharness/src/kernels_mc.cpp:
// every luma quarter-pel and chroma eighth-pel position at each block size,
// the half-pel filters at widths and heights up to 17, and sample averaging.
// The kernels are reached through InitMcFunc, as the codec reaches them.
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

public unsafe class mc_unittest
{
    // Same layout as kernels_mc.cpp: a 40x40 random source with the block at
    // (8, 8), and a destination of stride 24 and h + 2 rows with the block at
    // row 1, column 2.
    private const int SrcStride = 40, SrcRows = 40, SrcOrigin = 8 * SrcStride + 8;
    private const int DstStride = 24;

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("mc");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Mc_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("mc", kernel, kase, sha1);
        int w = row.Param("w"), h = row.Param("h");
        SMcFunc mc;
        InitMcFunc(&mc, 0);

        var r = new FixtureRng((uint)row.Param("seed"));
        using var src = new NativeBuffer(SrcStride * SrcRows);
        using var dst = new NativeBuffer(DstStride * (h + 2));
        r.Fill(src.Span);
        r.Fill(dst.Span);
        byte* pSrc = src.Ptr + SrcOrigin;
        byte* pDst = dst.Ptr + DstStride + 2;

        switch (kernel)
        {
            case "McLuma_c":
                mc.pMcLumaFunc(pSrc, SrcStride, pDst, DstStride, (short)row.Param("mvx"), (short)row.Param("mvy"), w, h);
                break;
            case "McChroma_c":
                mc.pMcChromaFunc(pSrc, SrcStride, pDst, DstStride, (short)row.Param("mvx"), (short)row.Param("mvy"), w, h);
                break;
            case "McHorVer20_c":
                mc.pfLumaHalfpelHor(pSrc, SrcStride, pDst, DstStride, w, h);
                break;
            case "McHorVer02_c":
                mc.pfLumaHalfpelVer(pSrc, SrcStride, pDst, DstStride, w, h);
                break;
            case "McHorVer22_c":
                mc.pfLumaHalfpelCen(pSrc, SrcStride, pDst, DstStride, w, h);
                break;
            case "PixelAvg_c":
                using (var srcB = new NativeBuffer(16 * 17))
                {
                    r.Fill(srcB.Span);
                    mc.pfSampleAveraging(pDst, DstStride, pSrc, SrcStride, srcB.Ptr, 16, w, h);
                }
                break;
            default:
                throw new ArgumentException($"No C# case for {kernel} ({kase}).");
        }

        Assert.Equal(sha1, FixtureHasher.Of(dst.Span));
    }

    [Fact]
    public void InitMcFunc_FillsEverySlot()
    {
        SMcFunc mc = default;
        InitMcFunc(&mc, 0);
        Assert.True(mc.pfLumaHalfpelHor != null);
        Assert.True(mc.pfLumaHalfpelVer != null);
        Assert.True(mc.pfLumaHalfpelCen != null);
        Assert.True(mc.pMcChromaFunc != null);
        Assert.True(mc.pMcLumaFunc != null);
        Assert.True(mc.pfSampleAveraging != null);
        Assert.Equal(6 * sizeof(nint), sizeof(SMcFunc));
    }
}
