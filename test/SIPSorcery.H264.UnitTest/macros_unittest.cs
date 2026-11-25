//-----------------------------------------------------------------------------
// Filename: macros_unittest.cs
//
// Description: Checks the port of codec/common/inc/macros.h against the C
// build. Each case mirrors KernelsMacros in refharness/src/kernels_common.cpp:
// same inputs in the same order, same bytes hashed.
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

public unsafe class macros_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("macros");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Macro_MatchesCBuild(string kernel, string kase, string sha1)
    {
        using var h = new FixtureHasher();
        var r = new FixtureRng(1);
        switch (kernel)
        {
            case "WelsClip1":
                for (int x = -2048; x < 2048; ++x)
                {
                    h.Add([WelsClip1(x)]);
                }
                break;

            case "CeilLog2":
                for (int i = -4; i <= 70000; ++i)
                {
                    h.AddI32(CeilLog2(i));
                }
                break;

            case "WELS_LOG2":
                for (uint v = 0; v <= 70000; ++v)
                {
                    h.AddI32(WELS_LOG2(v));
                }
                for (int k = 0; k < 32; ++k)
                {
                    h.AddI32(WELS_LOG2(1u << k));
                    h.AddI32(WELS_LOG2((1u << k) - 1));
                    h.AddI32(WELS_LOG2(~0u >> k));
                }
                break;

            case "WelsMedian":
                for (int i = 0; i < 4096; ++i)
                {
                    int x = (short)(r.Next() >> 16);
                    int y = (short)(r.Next() >> 16);
                    int z = (short)(r.Next() >> 16);
                    h.AddI32(WelsMedian(x, y, z));
                }
                break;

            case "WELS_DIV_ROUND":
                for (int i = 0; i < 4096; ++i)
                {
                    int x = (int)(r.Next() >> 11) - (1 << 20);
                    int y = (int)(r.Next() & 1023);
                    h.AddI32(WELS_DIV_ROUND(x, y));
                }
                break;

            case "WELS_DIV_ROUND64":
                for (int i = 0; i < 4096; ++i)
                {
                    long x = ((long)r.Next() << 16) - (1L << 47);
                    long y = r.Next() & 0xFFFFF;
                    h.AddI64(WELS_DIV_ROUND64(x, y));
                }
                break;

            case "WELS_ROUND":
                for (int i = 0; i < 4096; ++i)
                {
                    double x = (int)r.Next() / 4096.0;
                    h.AddI32(WELS_ROUND(x));
                }
                break;

            case "WelsSetMemMultiplebytes_c":
                byte* buf = stackalloc byte[4 * 48];
                foreach (int len in new[] { 1, 2, 4 })
                {
                    for (int count = 0; count <= 40; ++count)
                    {
                        var span = new Span<byte>(buf, 4 * 48);
                        r.Fill(span);
                        uint value = (count % 5 == 0) ? 0 : r.Next();
                        WelsSetMemMultiplebytes_c(buf + 4, value, count, len);
                        h.Add(span);
                    }
                }
                break;

            case "WELS_NON_ZERO_COUNT_AVERAGE":
                for (int a = -1; a <= 16; ++a)
                {
                    for (int b = -1; b <= 16; ++b)
                    {
                        sbyte nC = 0;
                        WELS_NON_ZERO_COUNT_AVERAGE(ref nC, (sbyte)a, (sbyte)b);
                        h.Add([(byte)nC]);
                    }
                }
                break;

            default:
                throw new ArgumentException($"No C# case for macro {kernel} ({kase}).");
        }
        Assert.Equal(sha1, h.Hex());
    }

    [Fact]
    public void SelectionMacros_ReturnAnOperand()
    {
        Assert.Equal(7, WELS_MAX(3, 7));
        Assert.Equal((short)-3, WELS_MIN((short)-3, (short)7));
        Assert.Equal(51, CLIP3_QP_0_51(60));
        Assert.Equal(0, CLIP3_QP_0_51(-1));
        Assert.Equal(2.5, WELS_CLIP3(2.5, 0.0, 3.0));
        Assert.Equal(5, WelsClip3(9, 0, 5));
        // Min when both are non-negative, otherwise max (so a negative "unset" value loses).
        Assert.Equal(4, WELS_MIN_POSITIVE(9, 4));
        Assert.Equal(4, WELS_MIN_POSITIVE(-1, 4));
        Assert.Equal(-3, WELS_MIN_POSITIVE(-3, -7));
        // int16_t is promoted to int before negation, as in C.
        Assert.Equal(32768, WELS_ABS(short.MinValue));
        Assert.Equal(32, WELS_ALIGN(17, 16));
        Assert.True(WELS_POWER2_IF(64));
        Assert.False(WELS_POWER2_IF(0));
        Assert.Equal(0x0101u, (uint)BUTTERFLY1x2(1));
        Assert.Equal(0x00020002u, BUTTERFLY2x4(2));
        Assert.Equal(0x0000000300000003ul, BUTTERFLY4x8(3));
        Assert.Equal(-5, NEG_NUM(5));
        Assert.Equal(-1, WELS_SIGN(-9));
    }
}
