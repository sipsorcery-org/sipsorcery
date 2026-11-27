//-----------------------------------------------------------------------------
// Filename: mc_simd_unittest.cs
//
// Description: Checks the SIMD motion compensation kernels (common/mc_simd.cs,
// P5.3) against the C ones: on the C build's fixture rows, which must give the
// same hashes, and on random and extreme blocks at random strides, which must
// give the same destination, the samples round the block included, as the C#
// C kernels. Each slot of the table InitMcFunc fills is tried at every width and
// height up to the largest its callers use, and the luma and chroma slots at
// every fractional position. Nothing runs where the machine has no SIMD kernels
// (WelsCpuHasSimd128).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.3).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class mc_simd_unittest
{
    private static bool Supported => sad_common_simd_unittest.Supported;

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("mc");

    public static IEnumerable<object[]> Slots() =>
    [
        ["LumaHalfpelHor"], ["LumaHalfpelVer"], ["LumaHalfpelCen"], ["SampleAveraging"], ["McLuma"], ["McChroma"],
    ];

    [Theory]
    [MemberData(nameof(Rows))]
    public void Simd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!Supported)
        {
            return;
        }
        Assert.Equal(sha1, mc_unittest.Run(new KernelFixture("mc", kernel, kase, sha1), uint.MaxValue));
    }

    [Theory]
    [MemberData(nameof(Slots))]
    public void Simd_EqualsC(string slot)
    {
        if (!Supported)
        {
            return;
        }
        SMcFunc c, s;
        InitMcFunc(&c, 0);
        InitMcFunc(&s, uint.MaxValue);
        var r = new FixtureRng(SimdFill.Seed(slot));
        int trial = 0;

        // McHorVer22_c holds 17 columns, and the encoder asks for 17 at most. The luma
        // quarter-pel kernels average in 16x16 buffers. Chroma blocks are 2, 4 and 8 wide, and
        // the copy has a 16-wide case.
        (int maxW, int maxH, int positions, int trials) = slot switch
        {
            "SampleAveraging" => (33, 17, 1, 6),
            "McLuma" => (16, 16, 16, 2),
            "McChroma" => (16, 9, 64, 2),
            _ => (17, 17, 1, 6),
        };
        for (int w = 1; w <= maxW; w++)
        {
            if (slot == "McChroma" && w != 2 && w != 4 && w != 8 && w != 16)
            {
                continue;
            }
            for (int h = 1; h <= maxH; h++)
            {
                for (int pos = 0; pos < positions; pos++)
                {
                    for (int t = 0; t < trials; t++, trial++)
                    {
                        Case(ref r, trial, slot, &c, &s, w, h, pos);
                    }
                }
            }
        }
    }

    private static void Case(ref FixtureRng r, int trial, string slot, SMcFunc* c, SMcFunc* s, int w, int h, int pos)
    {
        // Three samples and more all round the block, for the filter taps.
        int srcStride = w + 6 + (int)(r.Next() % 24);
        int dstStride = w + (int)(r.Next() % 24);
        int bStride = w + (int)(r.Next() % 24);
        using var src = new NativeBuffer(srcStride * (h + 6));
        using var srcB = new NativeBuffer(bStride * h);
        // The block at row 1, column 1, with the samples round it to show stray writes.
        using var dstC = new NativeBuffer(dstStride * (h + 2) + 2);
        using var dstS = new NativeBuffer(dstC.Length);
        SimdFill.Pair(ref r, trial, src.Span, srcB.Span);
        r.Fill(dstC.Span);
        dstC.Span.CopyTo(dstS.Span);

        byte* pSrc = src.Ptr + 3 * srcStride + 3;
        byte* pC = dstC.Ptr + dstStride + 1;
        byte* pS = dstS.Ptr + dstStride + 1;
        // Only the low bits of a motion vector choose the position.
        short mvx, mvy;
        if (slot == "McLuma")
        {
            mvx = (short)((pos >> 2) + 4 * ((int)(r.Next() % 9) - 4));
            mvy = (short)((pos & 3) + 4 * ((int)(r.Next() % 9) - 4));
        }
        else
        {
            mvx = (short)((pos >> 3) + 8 * ((int)(r.Next() % 9) - 4));
            mvy = (short)((pos & 7) + 8 * ((int)(r.Next() % 9) - 4));
        }

        switch (slot)
        {
            case "LumaHalfpelHor":
                c->pfLumaHalfpelHor(pSrc, srcStride, pC, dstStride, w, h);
                s->pfLumaHalfpelHor(pSrc, srcStride, pS, dstStride, w, h);
                break;
            case "LumaHalfpelVer":
                c->pfLumaHalfpelVer(pSrc, srcStride, pC, dstStride, w, h);
                s->pfLumaHalfpelVer(pSrc, srcStride, pS, dstStride, w, h);
                break;
            case "LumaHalfpelCen":
                c->pfLumaHalfpelCen(pSrc, srcStride, pC, dstStride, w, h);
                s->pfLumaHalfpelCen(pSrc, srcStride, pS, dstStride, w, h);
                break;
            case "SampleAveraging":
                c->pfSampleAveraging(pC, dstStride, pSrc, srcStride, srcB.Ptr, bStride, w, h);
                s->pfSampleAveraging(pS, dstStride, pSrc, srcStride, srcB.Ptr, bStride, w, h);
                break;
            case "McLuma":
                c->pMcLumaFunc(pSrc, srcStride, pC, dstStride, mvx, mvy, w, h);
                s->pMcLumaFunc(pSrc, srcStride, pS, dstStride, mvx, mvy, w, h);
                break;
            case "McChroma":
                c->pMcChromaFunc(pSrc, srcStride, pC, dstStride, mvx, mvy, w, h);
                s->pMcChromaFunc(pSrc, srcStride, pS, dstStride, mvx, mvy, w, h);
                break;
            default:
                throw new ArgumentException($"No slot {slot}.");
        }

        if (!dstC.Span.SequenceEqual(dstS.Span))
        {
            Assert.Fail($"{slot} {w}x{h}, mv ({mvx}, {mvy}), trial {trial}: the SIMD kernel's destination differs from the C kernel's.");
        }
    }

    [Fact]
    public void InitMcFunc_ChoosesByTheFlags()
    {
        SMcFunc c, s, off;
        InitMcFunc(&c, 0);
        InitMcFunc(&s, uint.MaxValue);
        InitMcFunc(&off, uint.MaxValue & ~(WELS_CPU_SSE2 | WELS_CPU_NEON));
        nint* pc = (nint*)&c, ps = (nint*)&s, poff = (nint*)&off;
        for (int i = 0; i < sizeof(SMcFunc) / sizeof(nint); i++)
        {
            Assert.True(ps[i] != 0);
            Assert.Equal(Supported, pc[i] != ps[i]);
            Assert.Equal(pc[i], poff[i]);
        }
    }
}
