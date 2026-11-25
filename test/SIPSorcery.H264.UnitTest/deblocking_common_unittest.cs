//-----------------------------------------------------------------------------
// Filename: deblocking_common_unittest.cs
//
// Description: Checks the port of codec/common/src/deblocking_common.cpp
// against the C build. Mirrors KernelsDeblock in
// refharness/src/kernels_deblock_sad.cpp: every luma, chroma and interleaved
// chroma edge kernel over a sweep of alpha/beta, noise levels and tc0 sets,
// and WelsNonZeroCount_c.
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

public unsafe class deblocking_common_unittest
{
    // Same layout as kernels_deblock_sad.cpp: the edge at (row 4, column 4) of a
    // 24-row plane of stride 28.
    private const int DbStride = 28, DbRows = 24, DbOrigin = 4 * DbStride + 4;

    private static readonly sbyte[][] Tc = [[-1, 0, 2, 5], [1, 4, 9, 25]];

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("deblock");

    /// <summary>Base plus noise in [-spread, spread], clamped, as FillSmooth in the harness.</summary>
    private static void FillSmooth(ref FixtureRng r, Span<byte> p, int @base, int spread)
    {
        for (int i = 0; i < p.Length; i++)
        {
            int v = @base + (int)(r.Next() % (uint)(2 * spread + 1)) - spread;
            p[i] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Deblock_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("deblock", kernel, kase, sha1);
        if (kernel == nameof(WelsNonZeroCount_c))
        {
            NonZeroCount(row);
            return;
        }

        int alpha = row.Param("alpha"), beta = row.Param("beta");
        bool twoPlanes = kernel.StartsWith("DeblockChroma", StringComparison.Ordinal) && !kernel.EndsWith("2_c", StringComparison.Ordinal);
        bool lt4 = kernel.Contains("Lt4", StringComparison.Ordinal);

        var r = new FixtureRng((uint)row.Param("seed"));
        using var a = new NativeBuffer(DbStride * DbRows);
        using var b = new NativeBuffer(DbStride * DbRows);
        FillSmooth(ref r, a.Span, row.Param("base"), row.Param("spread"));
        if (twoPlanes)
        {
            FillSmooth(ref r, b.Span, row.Param("base"), row.Param("spread"));
        }
        sbyte* tc = stackalloc sbyte[4];
        if (lt4)
        {
            Tc[row.Param("tc")].CopyTo(new Span<sbyte>(tc, 4));
        }
        byte* pa = a.Ptr + DbOrigin;
        byte* pb = b.Ptr + DbOrigin;

        switch (kernel)
        {
            case nameof(DeblockLumaLt4V_c): DeblockLumaLt4V_c(pa, DbStride, alpha, beta, tc); break;
            case nameof(DeblockLumaLt4H_c): DeblockLumaLt4H_c(pa, DbStride, alpha, beta, tc); break;
            case nameof(DeblockLumaEq4V_c): DeblockLumaEq4V_c(pa, DbStride, alpha, beta); break;
            case nameof(DeblockLumaEq4H_c): DeblockLumaEq4H_c(pa, DbStride, alpha, beta); break;
            case nameof(DeblockChromaLt4V_c): DeblockChromaLt4V_c(pa, pb, DbStride, alpha, beta, tc); break;
            case nameof(DeblockChromaLt4H_c): DeblockChromaLt4H_c(pa, pb, DbStride, alpha, beta, tc); break;
            case nameof(DeblockChromaEq4V_c): DeblockChromaEq4V_c(pa, pb, DbStride, alpha, beta); break;
            case nameof(DeblockChromaEq4H_c): DeblockChromaEq4H_c(pa, pb, DbStride, alpha, beta); break;
            case nameof(DeblockChromaLt4V2_c): DeblockChromaLt4V2_c(pa, DbStride, alpha, beta, tc); break;
            case nameof(DeblockChromaLt4H2_c): DeblockChromaLt4H2_c(pa, DbStride, alpha, beta, tc); break;
            case nameof(DeblockChromaEq4V2_c): DeblockChromaEq4V2_c(pa, DbStride, alpha, beta); break;
            case nameof(DeblockChromaEq4H2_c): DeblockChromaEq4H2_c(pa, DbStride, alpha, beta); break;
            default: throw new ArgumentException($"No C# kernel {kernel}.");
        }

        using var h = new FixtureHasher();
        h.Add(a.Span);
        if (twoPlanes)
        {
            h.Add(b.Span);
        }
        Assert.Equal(sha1, h.Hex());
    }

    // 24 counts at offset 4 of a 32-byte buffer, about half of them zero.
    private static void NonZeroCount(KernelFixture row)
    {
        var r = new FixtureRng((uint)row.Param("seed"));
        sbyte* nzc = stackalloc sbyte[32];
        for (int i = 0; i < 32; i++)
        {
            byte v = r.NextByte();
            nzc[i] = (v & 1) != 0 ? (sbyte)v : (sbyte)0;
        }

        WelsNonZeroCount_c(nzc + 4);

        Assert.Equal(row.Sha1, FixtureHasher.Of(new ReadOnlySpan<byte>(nzc, 32)));
    }
}
