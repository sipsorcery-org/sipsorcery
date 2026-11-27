//-----------------------------------------------------------------------------
// Filename: deblocking_common_simd_unittest.cs
//
// Description: Checks the SIMD loop filter edge kernels
// (common/deblocking_common_simd.cs, P5.4) against the C ones: on the C build's
// fixture rows, which must give the same hashes, and on edges of every kind
// (smooth with a step across the edge, flat near 0 and 255, ramps, extremes and
// noise) with random alpha, beta and tc0, which must leave the planes as the C#
// C kernels leave them. And that both DeblockingInits choose them by the CPU
// flags. Nothing runs where the machine has no SIMD kernels (WelsCpuHasSimd128).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.4).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class deblocking_common_simd_unittest
{
    private static bool Supported => sad_common_simd_unittest.Supported;

    // The edge at row 8, column 8, with room for 16 samples along it either way.
    private const int Rows = 26, EdgeRow = 8, EdgeCol = 8;

    private static readonly string[] Kernels =
    [
        nameof(DeblockLumaLt4V_c), nameof(DeblockLumaLt4H_c), nameof(DeblockLumaEq4V_c), nameof(DeblockLumaEq4H_c),
        nameof(DeblockChromaLt4V_c), nameof(DeblockChromaLt4H_c), nameof(DeblockChromaEq4V_c), nameof(DeblockChromaEq4H_c),
        nameof(DeblockChromaLt4V2_c), nameof(DeblockChromaLt4H2_c), nameof(DeblockChromaEq4V2_c), nameof(DeblockChromaEq4H2_c),
    ];

    public static IEnumerable<object[]> Rows_ =>
        KernelFixtures.Group("deblock").Where(r => (string)r[0] != nameof(WelsNonZeroCount_c));

    public static IEnumerable<object[]> KernelNames() => Kernels.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Rows_))]
    public void Simd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!Supported)
        {
            return;
        }
        Assert.Equal(sha1, deblocking_common_unittest.Run(new KernelFixture("deblock", kernel, kase, sha1), true));
    }

    [Theory]
    [MemberData(nameof(KernelNames))]
    public void Simd_EqualsC(string kernel)
    {
        if (!Supported)
        {
            return;
        }
        bool horizontalEdge = kernel.Contains("V_c", StringComparison.Ordinal) || kernel.Contains("V2_c", StringComparison.Ordinal);
        bool luma = kernel.StartsWith("DeblockLuma", StringComparison.Ordinal);
        var r = new FixtureRng(SimdFill.Seed(kernel));
        sbyte* tc = stackalloc sbyte[4];

        int filtered = 0;
        const int kiTrials = 3000;
        for (int trial = 0; trial < kiTrials; trial++)
        {
            int stride = 24 + (int)(r.Next() % 17);
            using var cA = new NativeBuffer(stride * Rows);
            using var cB = new NativeBuffer(stride * Rows);
            using var sA = new NativeBuffer(stride * Rows);
            using var sB = new NativeBuffer(stride * Rows);
            Fill(ref r, trial, cA.Span, stride, horizontalEdge);
            Fill(ref r, trial / 6, cB.Span, stride, horizontalEdge);
            cA.Span.CopyTo(sA.Span);
            cB.Span.CopyTo(sB.Span);
            byte[] before = cA.Span.ToArray();

            // Alpha and beta as the tables give them, 0 to 255 and 0 to 18, and past both.
            int alpha = (r.Next() % 8) switch { 0 => 0, 1 => 1, 2 => 255, 3 => (int)(r.Next() % 256), _ => 4 + (int)(r.Next() % 60) };
            int beta = (r.Next() % 8) switch { 0 => 0, 1 => 1, 2 => (int)(r.Next() % 256), _ => (int)(r.Next() % 19) };
            for (int i = 0; i < 4; i++)
            {
                // Mostly what TC0_TBL_LOOKUP gives (-1 to 25, plus 1 for chroma), sometimes any int8_t.
                tc[i] = (r.Next() % 10) == 0 ? (sbyte)r.Next() : (sbyte)((int)(r.Next() % 28) - 1);
            }

            int origin = EdgeRow * stride + EdgeCol;
            deblocking_common_unittest.Call(kernel, false, cA.Ptr + origin, cB.Ptr + origin, stride, alpha, beta, tc);
            deblocking_common_unittest.Call(kernel, true, sA.Ptr + origin, sB.Ptr + origin, stride, alpha, beta, tc);

            if (!cA.Span.SequenceEqual(sA.Span) || !cB.Span.SequenceEqual(sB.Span))
            {
                Assert.Fail($"{kernel} trial {trial} (stride {stride}, alpha {alpha}, beta {beta}, tc {tc[0]} {tc[1]} {tc[2]} {tc[3]}, " +
                            $"{(luma ? "luma" : "chroma")}): the SIMD kernel's planes differ from the C kernel's.");
            }
            if (!cA.Span.SequenceEqual(before))
            {
                filtered++;
            }
        }
        // The content and parameters must make the C kernel filter often, or the trials check little.
        Assert.True(filtered > kiTrials / 4, $"{kernel}: only {filtered} of {kiTrials} trials changed the plane.");
    }

    /// <summary>A plane for an edge, of a different kind by <paramref name="trial"/>.</summary>
    private static void Fill(ref FixtureRng r, int trial, Span<byte> plane, int stride, bool horizontalEdge)
    {
        int kind = trial % 6;
        if (kind == 0)
        {
            r.Fill(plane);
            return;
        }
        if (kind == 1)
        {
            // Only 0 and 255.
            for (int i = 0; i < plane.Length; i++)
            {
                plane[i] = (byte)((r.Next() & 1) * 255);
            }
            return;
        }
        // Smooth, or flat near the ends of the range, or a ramp, with a step across the edge.
        int @base = kind == 3 ? (int)(r.Next() % 12) : kind == 4 ? 243 + (int)(r.Next() % 13) : (int)(r.Next() % 256);
        int spread = kind == 3 || kind == 4 ? (int)(r.Next() % 3) : (int)(r.Next() % 8);
        int step = (int)(r.Next() % 41) - 20;
        int ramp = kind == 5 ? (int)(r.Next() % 9) - 4 : 0;
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < stride; x++)
            {
                int across = horizontalEdge ? y - EdgeRow : x - EdgeCol;
                int v = @base + (int)(r.Next() % (uint)(2 * spread + 1)) - spread + (across >= 0 ? step : 0) + ramp * across;
                plane[y * stride + x] = (byte)Math.Clamp(v, 0, 255);
            }
        }
    }

    // The kernels in the order of the tables' slots (SDeblockingFunc; DeblockingFunc has the
    // first 8): LT4Ver, EQ4Ver, LT4Hor, EQ4Hor for luma, chroma, and the single plane chroma.
    private static nint[] CKernels =>
    [
        (nint)(delegate*<byte*, int, int, int, sbyte*, void>)&DeblockLumaLt4V_c,
        (nint)(delegate*<byte*, int, int, int, void>)&DeblockLumaEq4V_c,
        (nint)(delegate*<byte*, int, int, int, sbyte*, void>)&DeblockLumaLt4H_c,
        (nint)(delegate*<byte*, int, int, int, void>)&DeblockLumaEq4H_c,
        (nint)(delegate*<byte*, byte*, int, int, int, sbyte*, void>)&DeblockChromaLt4V_c,
        (nint)(delegate*<byte*, byte*, int, int, int, void>)&DeblockChromaEq4V_c,
        (nint)(delegate*<byte*, byte*, int, int, int, sbyte*, void>)&DeblockChromaLt4H_c,
        (nint)(delegate*<byte*, byte*, int, int, int, void>)&DeblockChromaEq4H_c,
        (nint)(delegate*<byte*, int, int, int, sbyte*, void>)&DeblockChromaLt4V2_c,
        (nint)(delegate*<byte*, int, int, int, void>)&DeblockChromaEq4V2_c,
        (nint)(delegate*<byte*, int, int, int, sbyte*, void>)&DeblockChromaLt4H2_c,
        (nint)(delegate*<byte*, int, int, int, void>)&DeblockChromaEq4H2_c,
    ];

    private static nint[] SimdKernels =>
    [
        (nint)(delegate*<byte*, int, int, int, sbyte*, void>)&DeblockLumaLt4V_simd,
        (nint)(delegate*<byte*, int, int, int, void>)&DeblockLumaEq4V_simd,
        (nint)(delegate*<byte*, int, int, int, sbyte*, void>)&DeblockLumaLt4H_simd,
        (nint)(delegate*<byte*, int, int, int, void>)&DeblockLumaEq4H_simd,
        (nint)(delegate*<byte*, byte*, int, int, int, sbyte*, void>)&DeblockChromaLt4V_simd,
        (nint)(delegate*<byte*, byte*, int, int, int, void>)&DeblockChromaEq4V_simd,
        (nint)(delegate*<byte*, byte*, int, int, int, sbyte*, void>)&DeblockChromaLt4H_simd,
        (nint)(delegate*<byte*, byte*, int, int, int, void>)&DeblockChromaEq4H_simd,
        (nint)(delegate*<byte*, int, int, int, sbyte*, void>)&DeblockChromaLt4V2_simd,
        (nint)(delegate*<byte*, int, int, int, void>)&DeblockChromaEq4V2_simd,
        (nint)(delegate*<byte*, int, int, int, sbyte*, void>)&DeblockChromaLt4H2_simd,
        (nint)(delegate*<byte*, int, int, int, void>)&DeblockChromaEq4H2_simd,
    ];

    [Fact]
    public void DeblockingInit_ChoosesByTheFlags()
    {
        const uint kuiOff = uint.MaxValue & ~(WELS_CPU_SSE2 | WELS_CPU_NEON);
        nint[] c = CKernels;
        nint[] simd = Supported ? SimdKernels : c;

        // The decoder's table: 12 edge filters.
        Decoder.SDeblockingFunc dc, ds, doff;
        Decoder.WelsDec.DeblockingInit(&dc, 0);
        Decoder.WelsDec.DeblockingInit(&ds, unchecked((int)uint.MaxValue));
        Decoder.WelsDec.DeblockingInit(&doff, unchecked((int)kuiOff));
        Assert.Equal(c.Length, sizeof(Decoder.SDeblockingFunc) / sizeof(nint));
        Check((nint*)&dc, c, c.Length);
        Check((nint*)&ds, simd, c.Length);
        Check((nint*)&doff, c, c.Length);

        // The encoder's: the first 8 slots are its edge filters, then DeblockingBSCalc, which
        // stays C.
        Encoder.DeblockingFunc ec = default, es = default, eoff = default;
        Encoder.WelsEnc.DeblockingInit(&ec, 0);
        Encoder.WelsEnc.DeblockingInit(&es, unchecked((int)uint.MaxValue));
        Encoder.WelsEnc.DeblockingInit(&eoff, unchecked((int)kuiOff));
        Check((nint*)&ec, c, 8);
        Check((nint*)&es, simd, 8);
        Check((nint*)&eoff, c, 8);
        Assert.Equal((nint)ec.pfDeblockingBSCalc, (nint)es.pfDeblockingBSCalc);
    }

    /// <summary>The first <paramref name="slots"/> slots of a table hold <paramref name="expected"/>.</summary>
    private static void Check(nint* table, nint[] expected, int slots)
    {
        for (int i = 0; i < slots; i++)
        {
            Assert.True(expected[i] == table[i], $"Slot {i} holds the wrong kernel.");
        }
    }
}
