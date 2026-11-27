//-----------------------------------------------------------------------------
// Filename: encode_mb_aux_simd_unittest.cs
//
// Description: Checks the SIMD forward transforms and quantisers
// (encoder/encode_mb_aux_simd.cs, P5.5) against the C ones. On the C build's
// fixture rows, they must give the same hashes. On random, extreme and sparse
// coefficients and differences, with the quantisation tables' FF and MF and
// any others from 0 to 32767, they must leave every buffer as the C# C kernels
// leave it, guard entries either side included. And WelsInitEncodingFuncs must
// choose them by the CPU flags. Nothing runs where the machine has no SIMD
// kernels (WelsCpuHasSimd128).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SIPSorcery.H264.Encoder;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class encode_mb_aux_simd_unittest
{
    private static bool Supported => sad_common_simd_unittest.Supported;

    private const string GroupName = "encode_mb_aux";
    private const int kiTrials = 3000;

    private static readonly string[] Kernels =
    [
        nameof(WelsQuant4x4_c), nameof(WelsQuant4x4Dc_c), nameof(WelsQuantFour4x4_c), nameof(WelsQuantFour4x4Max_c),
        nameof(WelsHadamardT4Dc_c), nameof(WelsDctT4_c), nameof(WelsDctFourT4_c),
    ];

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName).Where(r => Kernels.Contains((string)r[0]));

    public static IEnumerable<object[]> KernelNames() => Kernels.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Rows))]
    public void Simd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!Supported)
        {
            return;
        }
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(WelsQuant4x4_c) => encode_mb_aux_unittest.Quant(&WelsQuant4x4_simd, 16, row),
            nameof(WelsQuantFour4x4_c) => encode_mb_aux_unittest.Quant(&WelsQuantFour4x4_simd, 64, row),
            nameof(WelsQuantFour4x4Max_c) => encode_mb_aux_unittest.QuantMax(&WelsQuantFour4x4Max_simd, row),
            nameof(WelsQuant4x4Dc_c) => encode_mb_aux_unittest.QuantDc(&WelsQuant4x4Dc_simd, row),
            nameof(WelsHadamardT4Dc_c) => encode_mb_aux_unittest.HadamardT4Dc(&WelsHadamardT4Dc_simd, row),
            nameof(WelsDctT4_c) => encode_mb_aux_unittest.Dct(&WelsDctT4_simd, 4, row),
            nameof(WelsDctFourT4_c) => encode_mb_aux_unittest.Dct(&WelsDctFourT4_simd, 8, row),
            _ => throw new ArgumentException($"No SIMD kernel for {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    [Theory]
    [MemberData(nameof(KernelNames))]
    public void Simd_EqualsC(string kernel)
    {
        if (!Supported)
        {
            return;
        }
        var r = new FixtureRng(SimdFill.Seed(kernel));
        for (int trial = 0; trial < kiTrials; trial++)
        {
            string? diff = kernel switch
            {
                nameof(WelsQuant4x4_c) => Quant(ref r, trial, &WelsQuant4x4_c, &WelsQuant4x4_simd, 16),
                nameof(WelsQuantFour4x4_c) => Quant(ref r, trial, &WelsQuantFour4x4_c, &WelsQuantFour4x4_simd, 64),
                nameof(WelsQuantFour4x4Max_c) => QuantMax(ref r, trial),
                nameof(WelsQuant4x4Dc_c) => QuantDc(ref r, trial),
                nameof(WelsHadamardT4Dc_c) => HadamardT4Dc(ref r, trial),
                nameof(WelsDctT4_c) => Dct(ref r, trial, &WelsDctT4_c, &WelsDctT4_simd, 4),
                nameof(WelsDctFourT4_c) => Dct(ref r, trial, &WelsDctFourT4_c, &WelsDctFourT4_simd, 8),
                _ => throw new ArgumentException($"No SIMD kernel for {kernel}."),
            };
            if (diff != null)
            {
                Assert.Fail($"{kernel} trial {trial}: {diff}");
            }
        }
    }

    /// <summary>
    /// A row of the tables, or (one trial in four) any FF and MF from 0 to 32767, into
    /// <paramref name="pFF"/> and <paramref name="pMF"/>.
    /// </summary>
    private static string QuantParams(ref FixtureRng r, int trial, short* pFF, short* pMF)
    {
        if (trial % 4 == 3)
        {
            for (int i = 0; i < 8; i++)
            {
                pFF[i] = (short)(r.Next() & 0x7FFF);
                pMF[i] = (short)(r.Next() & 0x7FFF);
            }
            return "random FF and MF";
        }
        int qp = (int)(r.Next() % 52);
        bool intra = (r.Next() & 1) != 0;
        short* ff = intra ? (short*)(g_iQuantIntraFF + qp) : (short*)(g_kiQuantInterFF + qp);
        new ReadOnlySpan<short>(ff, 8).CopyTo(new Span<short>(pFF, 8));
        new ReadOnlySpan<short>((short*)(g_kiQuantMF + qp), 8).CopyTo(new Span<short>(pMF, 8));
        return $"qp {qp}{(intra ? " intra" : "")}";
    }

    private static string? Quant(ref FixtureRng r, int trial, delegate*<short*, short*, short*, void> c,
                                 delegate*<short*, short*, short*, void> s, int n)
    {
        short* pFF = stackalloc short[8], pMF = stackalloc short[8];
        string what = QuantParams(ref r, trial, pFF, pMF);
        using var dct = new SimdTwin(ref r, trial, n);

        c(dct.C, pFF, pMF);
        s(dct.S, pFF, pMF);

        return dct.Same ? null : $"{what}, {SimdTwin.KindName(trial)} coefficients: the coefficients differ.";
    }

    private static string? QuantMax(ref FixtureRng r, int trial)
    {
        short* pFF = stackalloc short[8], pMF = stackalloc short[8];
        string what = QuantParams(ref r, trial, pFF, pMF);
        using var dct = new SimdTwin(ref r, trial, 64);
        using var max = new SimdTwin(ref r, 0, 4);

        WelsQuantFour4x4Max_c(dct.C, pFF, pMF, max.C);
        WelsQuantFour4x4Max_simd(dct.S, pFF, pMF, max.S);

        return dct.Same && max.Same ? null : $"{what}, {SimdTwin.KindName(trial)} coefficients: the {(dct.Same ? "maxima" : "coefficients")} differ.";
    }

    private static string? QuantDc(ref FixtureRng r, int trial)
    {
        // The (FF << 1, MF >> 1) the encoder passes, or any from 0 to 32767.
        short* pFF = stackalloc short[8], pMF = stackalloc short[8];
        string what = QuantParams(ref r, trial, pFF, pMF);
        short iFF = trial % 4 == 3 ? pFF[0] : (short)(pFF[0] << 1);
        short iMF = trial % 4 == 3 ? pMF[0] : (short)(pMF[0] >> 1);
        using var dct = new SimdTwin(ref r, trial, 16);

        WelsQuant4x4Dc_c(dct.C, iFF, iMF);
        WelsQuant4x4Dc_simd(dct.S, iFF, iMF);

        return dct.Same ? null : $"{what} (FF {iFF}, MF {iMF}), {SimdTwin.KindName(trial)} coefficients: the coefficients differ.";
    }

    private static string? HadamardT4Dc(ref FixtureRng r, int trial)
    {
        using var dct = new SimdTwin(ref r, trial, 256);
        using var dc = new SimdTwin(ref r, 0, 16);

        WelsHadamardT4Dc_c(dc.C, dct.C);
        WelsHadamardT4Dc_simd(dc.S, dct.S);

        return dct.Same && dc.Same ? null : $"{SimdTwin.KindName(trial)} coefficients: the {(dct.Same ? "DC outputs" : "inputs")} differ.";
    }

    // Differences of every kind (SimdFill.Pair) at random strides, n x n samples from the origin.
    private static string? Dct(ref FixtureRng r, int trial, delegate*<short*, byte*, int, byte*, int, void> c,
                               delegate*<short*, byte*, int, byte*, int, void> s, int n)
    {
        int s1 = n + (int)(r.Next() % 41), s2 = n + (int)(r.Next() % 41);
        using var p1 = new NativeBuffer(s1 * n);
        using var p2 = new NativeBuffer(s2 * n);
        SimdFill.Pair(ref r, trial, p1.Span, p2.Span);
        byte[] b1 = p1.Span.ToArray(), b2 = p2.Span.ToArray();
        using var dct = new SimdTwin(ref r, 0, n * n);

        c(dct.C, p1.Ptr, s1, p2.Ptr, s2);
        s(dct.S, p1.Ptr, s1, p2.Ptr, s2);

        if (!p1.Span.SequenceEqual(b1) || !p2.Span.SequenceEqual(b2))
        {
            return $"strides {s1}, {s2}: a kernel wrote to its samples.";
        }
        return dct.Same ? null : $"strides {s1}, {s2}, fill {trial % 6}: the coefficients differ.";
    }

    [Fact]
    public void WelsInitEncodingFuncs_ChoosesByTheFlags()
    {
        const uint kuiOff = uint.MaxValue & ~(WELS_CPU_SSE2 | WELS_CPU_NEON);
        foreach (uint flags in new[] { 0u, uint.MaxValue, kuiOff })
        {
            bool simd = Supported && flags == uint.MaxValue;
            var p = (SWelsFuncPtrList*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsFuncPtrList));
            try
            {
                WelsInitEncodingFuncs(p, flags);
                SimdTwin.Slot(simd, (nint)p->pfTransformHadamard4x4Dc,
                    (nint)(delegate*<short*, short*, void>)&WelsHadamardT4Dc_c, (nint)(delegate*<short*, short*, void>)&WelsHadamardT4Dc_simd);
                SimdTwin.Slot(simd, (nint)p->pfDctT4,
                    (nint)(delegate*<short*, byte*, int, byte*, int, void>)&WelsDctT4_c, (nint)(delegate*<short*, byte*, int, byte*, int, void>)&WelsDctT4_simd);
                SimdTwin.Slot(simd, (nint)p->pfDctFourT4,
                    (nint)(delegate*<short*, byte*, int, byte*, int, void>)&WelsDctFourT4_c, (nint)(delegate*<short*, byte*, int, byte*, int, void>)&WelsDctFourT4_simd);
                SimdTwin.Slot(simd, (nint)p->pfQuantization4x4,
                    (nint)(delegate*<short*, short*, short*, void>)&WelsQuant4x4_c, (nint)(delegate*<short*, short*, short*, void>)&WelsQuant4x4_simd);
                SimdTwin.Slot(simd, (nint)p->pfQuantizationDc4x4,
                    (nint)(delegate*<short*, short, short, void>)&WelsQuant4x4Dc_c, (nint)(delegate*<short*, short, short, void>)&WelsQuant4x4Dc_simd);
                SimdTwin.Slot(simd, (nint)p->pfQuantizationFour4x4,
                    (nint)(delegate*<short*, short*, short*, void>)&WelsQuantFour4x4_c, (nint)(delegate*<short*, short*, short*, void>)&WelsQuantFour4x4_simd);
                SimdTwin.Slot(simd, (nint)p->pfQuantizationFour4x4Max,
                    (nint)(delegate*<short*, short*, short*, short*, void>)&WelsQuantFour4x4Max_c,
                    (nint)(delegate*<short*, short*, short*, short*, void>)&WelsQuantFour4x4Max_simd);

                // The ones that stay C.
                Assert.True(p->pfQuantizationHadamard2x2 == (delegate*<short*, short, short, short*, short*, int>)&WelsHadamardQuant2x2_c);
                Assert.True(p->pfQuantizationHadamard2x2Skip == (delegate*<short*, short, short, int>)&WelsHadamardQuant2x2Skip_c);
                Assert.True(p->pfScan4x4 == (delegate*<short*, short*, void>)&WelsScan4x4DcAc_c);
                Assert.True(p->pfCalculateSingleCtr4x4 == (delegate*<short*, int>)&WelsCalculateSingleCtr4x4_c);
                Assert.True(p->pfGetNoneZeroCount == (delegate*<short*, int>)&WelsGetNoneZeroCount_c);
            }
            finally
            {
                NativeMemory.Free(p);
            }
        }
    }
}

/// <summary>
/// Two copies of n 16-bit coefficients with 8 guard entries either side, for a C kernel and its
/// SIMD version. The coefficients are of a kind chosen by the trial; the guards are random.
/// </summary>
internal sealed unsafe class SimdTwin : IDisposable
{
    private const int Guard = 8;

    private static readonly short[] Extremes = [short.MinValue, short.MinValue + 1, -1, 0, 1, short.MaxValue - 1, short.MaxValue];

    private readonly NativeBuffer _c, _s;

    public SimdTwin(ref FixtureRng r, int trial, int n)
    {
        _c = new NativeBuffer((n + 2 * Guard) * sizeof(short));
        _s = new NativeBuffer((n + 2 * Guard) * sizeof(short));
        short* p = (short*)_c.Ptr;
        for (int i = 0; i < n + 2 * Guard; i++)
        {
            p[i] = i < Guard || i >= n + Guard ? (short)r.Next() : Coeff(ref r, trial % 6 == 5 ? (int)(r.Next() % 5) : trial % 6);
        }
        _c.Span.CopyTo(_s.Span);
    }

    /// <summary>A coefficient of a kind: any, extreme, typical, sparse or medium.</summary>
    public static short Coeff(ref FixtureRng r, int kind) => kind switch
    {
        0 => (short)r.Next(),
        1 => Extremes[r.Next() % (uint)Extremes.Length],
        2 => (short)((int)(r.Next() % 601) - 300),
        3 => r.Next() % 4 == 0 ? (short)r.Next() : (short)0,
        _ => (short)((int)(r.Next() % 8193) - 4096),
    };

    public static string KindName(int trial) =>
        (trial % 6) switch { 0 => "any", 1 => "extreme", 2 => "typical", 3 => "sparse", 4 => "medium", _ => "mixed" };

    /// <summary>The C kernel's first coefficient.</summary>
    public short* C => (short*)_c.Ptr + Guard;

    /// <summary>The SIMD kernel's first coefficient.</summary>
    public short* S => (short*)_s.Ptr + Guard;

    /// <summary>Both copies alike, guards included.</summary>
    public bool Same => _c.Span.SequenceEqual(_s.Span);

    /// <summary>A table slot holds the SIMD kernel where <paramref name="simd"/>, else the C one.</summary>
    public static void Slot(bool simd, nint actual, nint c, nint s) =>
        Assert.True(actual == (simd ? s : c), $"The slot holds {(actual == c ? "the C kernel" : actual == s ? "the SIMD kernel" : "another kernel")}.");

    public void Dispose()
    {
        _c.Dispose();
        _s.Dispose();
    }
}
