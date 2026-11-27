//-----------------------------------------------------------------------------
// Filename: enc_decode_mb_aux_simd_unittest.cs
//
// Description: Checks the encoder's SIMD dequantisers and inverse transforms
// (encoder/decode_mb_aux_simd.cs, P5.5) against the C ones. On the C build's
// fixture rows, they must give the same hashes. On coefficients of every kind
// (any int16_t, extremes, typical, sparse) with the tables' MF and any others,
// and predictions of every kind at random strides, they must leave every
// buffer as the C# C kernels leave it: guard entries, the prediction and the
// reconstruction plane round the block included. And
// WelsInitReconstructionFuncs must choose them by the CPU flags. Nothing runs
// where the machine has no SIMD kernels (WelsCpuHasSimd128).
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

public unsafe class enc_decode_mb_aux_simd_unittest
{
    private static bool Supported => sad_common_simd_unittest.Supported;

    private const string GroupName = "enc_decode_mb_aux";
    private const int kiTrials = 3000;

    private static readonly string[] Kernels =
    [
        nameof(WelsDequant4x4_c), nameof(WelsDequantFour4x4_c), nameof(WelsDequantIHadamard4x4_c),
        nameof(WelsIDctT4Rec_c), nameof(WelsIDctFourT4Rec_c), nameof(WelsIDctRecI16x16Dc_c),
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
            nameof(WelsDequant4x4_c) => enc_decode_mb_aux_unittest.Dequant(&WelsDequant4x4_simd, 16, row),
            nameof(WelsDequantFour4x4_c) => enc_decode_mb_aux_unittest.Dequant(&WelsDequantFour4x4_simd, 64, row),
            nameof(WelsDequantIHadamard4x4_c) => enc_decode_mb_aux_unittest.DequantIHadamard4x4(&WelsDequantIHadamard4x4_simd, row),
            nameof(WelsIDctT4Rec_c) => enc_decode_mb_aux_unittest.Rec(&WelsIDctT4Rec_simd, 4, 16, row),
            nameof(WelsIDctFourT4Rec_c) => enc_decode_mb_aux_unittest.Rec(&WelsIDctFourT4Rec_simd, 8, 64, row),
            nameof(WelsIDctRecI16x16Dc_c) => enc_decode_mb_aux_unittest.Rec(&WelsIDctRecI16x16Dc_simd, 16, 16, row),
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
                nameof(WelsDequant4x4_c) => Dequant(ref r, trial, &WelsDequant4x4_c, &WelsDequant4x4_simd, 16),
                nameof(WelsDequantFour4x4_c) => Dequant(ref r, trial, &WelsDequantFour4x4_c, &WelsDequantFour4x4_simd, 64),
                nameof(WelsDequantIHadamard4x4_c) => DequantIHadamard4x4(ref r, trial),
                nameof(WelsIDctT4Rec_c) => Rec(ref r, trial, &WelsIDctT4Rec_c, &WelsIDctT4Rec_simd, 4, 16),
                nameof(WelsIDctFourT4Rec_c) => Rec(ref r, trial, &WelsIDctFourT4Rec_c, &WelsIDctFourT4Rec_simd, 8, 64),
                nameof(WelsIDctRecI16x16Dc_c) => Rec(ref r, trial, &WelsIDctRecI16x16Dc_c, &WelsIDctRecI16x16Dc_simd, 16, 16),
                _ => throw new ArgumentException($"No SIMD kernel for {kernel}."),
            };
            if (diff != null)
            {
                Assert.Fail($"{kernel} trial {trial}: {diff}");
            }
        }
    }

    // The table's MF row for a qp, or (one trial in four) any 8 values.
    private static string? Dequant(ref FixtureRng r, int trial, delegate*<short*, ushort*, void> c, delegate*<short*, ushort*, void> s, int n)
    {
        ushort* kpMF = stackalloc ushort[8];
        int qp = (int)(r.Next() % 52);
        for (int i = 0; i < 8; i++)
        {
            kpMF[i] = trial % 4 == 3 ? (ushort)r.Next() : g_kuiDequantCoeff[qp][i];
        }
        using var rs = new SimdTwin(ref r, trial, n);

        c(rs.C, kpMF);
        s(rs.S, kpMF);

        return rs.Same ? null : $"{(trial % 4 == 3 ? "random MF" : $"qp {qp}")}, {SimdTwin.KindName(trial)} coefficients: the coefficients differ.";
    }

    // The g_kuiDequantCoeff[qp][0] >> 2 the encoder passes, or any.
    private static string? DequantIHadamard4x4(ref FixtureRng r, int trial)
    {
        ushort kuiMF = trial % 4 == 3 ? (ushort)r.Next() : (ushort)(g_kuiDequantCoeff[(int)(r.Next() % 52)][0] >> 2);
        using var rs = new SimdTwin(ref r, trial, 16);

        WelsDequantIHadamard4x4_c(rs.C, kuiMF);
        WelsDequantIHadamard4x4_simd(rs.S, kuiMF);

        return rs.Same ? null : $"MF {kuiMF}, {SimdTwin.KindName(trial)} coefficients: the coefficients differ.";
    }

    // A random plane of n + 2 rows, the block at row 1, column 2, as the fixture rows; the
    // prediction at the origin of its own plane, at random strides. Predictions of every kind:
    // random, all 0, all 255, only 0 and 255.
    private static string? Rec(ref FixtureRng r, int trial, delegate*<byte*, int, byte*, int, short*, void> c,
                               delegate*<byte*, int, byte*, int, short*, void> s, int n, int coeffs)
    {
        int stride = n + 2 + (int)(r.Next() % 41), predStride = n + (int)(r.Next() % 41);
        using var recC = new NativeBuffer(stride * (n + 2));
        using var recS = new NativeBuffer(stride * (n + 2));
        using var pred = new NativeBuffer(predStride * n);
        r.Fill(recC.Span);
        recC.Span.CopyTo(recS.Span);
        int predKind = (int)(r.Next() % 4);
        for (int i = 0; i < pred.Length; i++)
        {
            pred.Ptr[i] = predKind switch { 0 => r.NextByte(), 1 => 0, 2 => 255, _ => (byte)((r.Next() & 1) * 255) };
        }
        byte[] predBefore = pred.Span.ToArray();
        using var dct = new SimdTwin(ref r, trial, coeffs);

        c(recC.Ptr + stride + 2, stride, pred.Ptr, predStride, dct.C);
        s(recS.Ptr + stride + 2, stride, pred.Ptr, predStride, dct.S);

        if (!pred.Span.SequenceEqual(predBefore) || !dct.Same)
        {
            return "a kernel wrote to its prediction or coefficients.";
        }
        return recC.Span.SequenceEqual(recS.Span) ? null
            : $"strides {stride}, {predStride}, prediction kind {predKind}, {SimdTwin.KindName(trial)} coefficients: the planes differ.";
    }

    [Fact]
    public void WelsInitReconstructionFuncs_ChoosesByTheFlags()
    {
        const uint kuiOff = uint.MaxValue & ~(WELS_CPU_SSE2 | WELS_CPU_NEON);
        foreach (uint flags in new[] { 0u, uint.MaxValue, kuiOff })
        {
            bool simd = Supported && flags == uint.MaxValue;
            var p = (SWelsFuncPtrList*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsFuncPtrList));
            try
            {
                WelsInitReconstructionFuncs(p, flags);
                SimdTwin.Slot(simd, (nint)p->pfDequantization4x4,
                    (nint)(delegate*<short*, ushort*, void>)&WelsDequant4x4_c, (nint)(delegate*<short*, ushort*, void>)&WelsDequant4x4_simd);
                SimdTwin.Slot(simd, (nint)p->pfDequantizationFour4x4,
                    (nint)(delegate*<short*, ushort*, void>)&WelsDequantFour4x4_c, (nint)(delegate*<short*, ushort*, void>)&WelsDequantFour4x4_simd);
                SimdTwin.Slot(simd, (nint)p->pfDequantizationIHadamard4x4,
                    (nint)(delegate*<short*, ushort, void>)&WelsDequantIHadamard4x4_c, (nint)(delegate*<short*, ushort, void>)&WelsDequantIHadamard4x4_simd);
                SimdTwin.Slot(simd, (nint)p->pfIDctT4,
                    (nint)(delegate*<byte*, int, byte*, int, short*, void>)&WelsIDctT4Rec_c,
                    (nint)(delegate*<byte*, int, byte*, int, short*, void>)&WelsIDctT4Rec_simd);
                SimdTwin.Slot(simd, (nint)p->pfIDctFourT4,
                    (nint)(delegate*<byte*, int, byte*, int, short*, void>)&WelsIDctFourT4Rec_c,
                    (nint)(delegate*<byte*, int, byte*, int, short*, void>)&WelsIDctFourT4Rec_simd);
                SimdTwin.Slot(simd, (nint)p->pfIDctI16x16Dc,
                    (nint)(delegate*<byte*, int, byte*, int, short*, void>)&WelsIDctRecI16x16Dc_c,
                    (nint)(delegate*<byte*, int, byte*, int, short*, void>)&WelsIDctRecI16x16Dc_simd);
            }
            finally
            {
                NativeMemory.Free(p);
            }
        }
    }
}
