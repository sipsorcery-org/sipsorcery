//-----------------------------------------------------------------------------
// Filename: get_intra_predictor_simd_unittest.cs
//
// Description: Checks the SIMD intra predictors (P5.6): the encoder's
// (encoder/get_intra_predictor_simd.cs and the 16x16 V and H in
// common/intra_pred_common_simd.cs), which predict into a packed buffer, and
// the decoder's (decoder/get_intra_predictor_simd.cs), which predict in place.
// On the C build's fixture rows of their C twins, they must give the same
// hashes. On neighbours of every kind (random, all 0, all 255, only 0 and 255,
// and the steps that take the plane predictors' gradients to their extremes
// both ways) at random strides, they must leave the prediction, its guard
// bytes and the picture as the C# C predictors leave them. And
// WelsInitIntraPredFuncs and InitPredFunc must choose them by the CPU flags,
// leaving the other slots C. Nothing runs where the machine has no SIMD
// kernels (WelsCpuHasSimd128).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SIPSorcery.H264.Decoder;
using SIPSorcery.H264.Encoder;
using Xunit;
using static SIPSorcery.H264.WelsCommon;
using Dec = SIPSorcery.H264.Decoder.WelsDec;
using Enc = SIPSorcery.H264.Encoder.WelsEnc;
using Com = SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class get_intra_predictor_simd_unittest
{
    private static bool Supported => sad_common_simd_unittest.Supported;

    private const int kiTrials = 3000;

    /// <summary>A C predictor, its SIMD twin, its block size and its table slot.</summary>
    private readonly record struct Twin(string Name, nint C, nint S, int N, bool Luma, int Mode);

    // The encoder's, with the C build's fixture group of each: V and H are common's.
    private static readonly Twin[] EncTwins =
    [
        new(nameof(Com.WelsI16x16LumaPredV_c), (nint)(delegate*<byte*, byte*, int, void>)&Com.WelsI16x16LumaPredV_c,
            (nint)(delegate*<byte*, byte*, int, void>)&Com.WelsI16x16LumaPredV_simd, 16, true, I16_PRED_V),
        new(nameof(Com.WelsI16x16LumaPredH_c), (nint)(delegate*<byte*, byte*, int, void>)&Com.WelsI16x16LumaPredH_c,
            (nint)(delegate*<byte*, byte*, int, void>)&Com.WelsI16x16LumaPredH_simd, 16, true, I16_PRED_H),
        new(nameof(Enc.WelsI16x16LumaPredDc_c), (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredDc_c,
            (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredDc_simd, 16, true, I16_PRED_DC),
        new(nameof(Enc.WelsI16x16LumaPredPlane_c), (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredPlane_c,
            (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredPlane_simd, 16, true, I16_PRED_P),
        new(nameof(Enc.WelsI16x16LumaPredDcLeft_c), (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredDcLeft_c,
            (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredDcLeft_simd, 16, true, I16_PRED_DC_L),
        new(nameof(Enc.WelsI16x16LumaPredDcTop_c), (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredDcTop_c,
            (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredDcTop_simd, 16, true, I16_PRED_DC_T),
        new(nameof(Enc.WelsI16x16LumaPredDcNA_c), (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredDcNA_c,
            (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsI16x16LumaPredDcNA_simd, 16, true, I16_PRED_DC_128),
        new(nameof(Enc.WelsIChromaPredPlane_c), (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsIChromaPredPlane_c,
            (nint)(delegate*<byte*, byte*, int, void>)&Enc.WelsIChromaPredPlane_simd, 8, false, C_PRED_P),
    ];

    private static readonly Twin[] DecTwins =
    [
        new(nameof(Dec.WelsI16x16LumaPredV_c), (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredV_c,
            (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredV_simd, 16, true, I16_PRED_V),
        new(nameof(Dec.WelsI16x16LumaPredH_c), (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredH_c,
            (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredH_simd, 16, true, I16_PRED_H),
        new(nameof(Dec.WelsI16x16LumaPredDc_c), (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredDc_c,
            (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredDc_simd, 16, true, I16_PRED_DC),
        new(nameof(Dec.WelsI16x16LumaPredPlane_c), (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredPlane_c,
            (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredPlane_simd, 16, true, I16_PRED_P),
        new(nameof(Dec.WelsI16x16LumaPredDcLeft_c), (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredDcLeft_c,
            (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredDcLeft_simd, 16, true, I16_PRED_DC_L),
        new(nameof(Dec.WelsI16x16LumaPredDcTop_c), (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredDcTop_c,
            (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredDcTop_simd, 16, true, I16_PRED_DC_T),
        new(nameof(Dec.WelsI16x16LumaPredDcNA_c), (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredDcNA_c,
            (nint)(delegate*<byte*, int, void>)&Dec.WelsI16x16LumaPredDcNA_simd, 16, true, I16_PRED_DC_128),
        new(nameof(Dec.WelsIChromaPredPlane_c), (nint)(delegate*<byte*, int, void>)&Dec.WelsIChromaPredPlane_c,
            (nint)(delegate*<byte*, int, void>)&Dec.WelsIChromaPredPlane_simd, 8, false, C_PRED_P),
    ];

    private static Twin Find(Twin[] twins, string name) => twins.Single(t => t.Name == name);

    public static IEnumerable<object[]> EncRows => KernelFixtures.Group("enc_get_intra_predictor")
        .Concat(KernelFixtures.Group("intra_pred")).Where(r => EncTwins.Any(t => t.Name == (string)r[0]));

    public static IEnumerable<object[]> DecRows => KernelFixtures.Group("get_intra_predictor")
        .Where(r => DecTwins.Any(t => t.Name == (string)r[0]));

    public static IEnumerable<object[]> EncNames() => EncTwins.Select(t => new object[] { t.Name });

    public static IEnumerable<object[]> DecNames() => DecTwins.Select(t => new object[] { t.Name });

    [Fact]
    public void Rows_CoverEveryTwin()
    {
        Assert.Equal(EncTwins.Length, EncRows.Select(r => (string)r[0]).Distinct().Count());
        Assert.Equal(DecTwins.Length, DecRows.Select(r => (string)r[0]).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(EncRows))]
    public void Encoder_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!Supported)
        {
            return;
        }
        Twin t = Find(EncTwins, kernel);
        var f = (delegate*<byte*, byte*, int, void>)t.S;
        bool common = kernel is nameof(Com.WelsI16x16LumaPredV_c) or nameof(Com.WelsI16x16LumaPredH_c);
        var row = new KernelFixture(common ? "intra_pred" : "enc_get_intra_predictor", kernel, kase, sha1);
        string actual = common ? intra_pred_common_unittest.Hash(f, row)
                               : EncoderFixtures.PredHash(f, t.N, row.Param("stride"), row.Param("seed"));
        Assert.Equal(sha1, actual);
    }

    [Theory]
    [MemberData(nameof(DecRows))]
    public void Decoder_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!Supported)
        {
            return;
        }
        Twin t = Find(DecTwins, kernel);
        var row = new KernelFixture("get_intra_predictor", kernel, kase, sha1);
        Assert.Equal(sha1, get_intra_predictor_unittest.Hash((delegate*<byte*, int, void>)t.S, t.N, row));
    }

    // A random plane of n + 2 rows, the block at row 1, column 4; the packed prediction with 16
    // guard bytes either side, the same noise for both.
    [Theory]
    [MemberData(nameof(EncNames))]
    public void Encoder_EqualsC(string kernel)
    {
        if (!Supported)
        {
            return;
        }
        Twin t = Find(EncTwins, kernel);
        int n = t.N;
        var r = new FixtureRng(SimdFill.Seed(kernel));
        for (int trial = 0; trial < kiTrials; trial++)
        {
            int stride = n + 4 + (int)(r.Next() % 41);
            using var plane = new NativeBuffer(stride * (n + 2));
            Neighbours(ref r, trial, plane.Ptr, stride, n);
            byte[] planeBefore = plane.Span.ToArray();
            using var predC = new NativeBuffer(n * n + 32);
            using var predS = new NativeBuffer(n * n + 32);
            r.Fill(predC.Span);
            predC.Span.CopyTo(predS.Span);

            ((delegate*<byte*, byte*, int, void>)t.C)(predC.Ptr + 16, plane.Ptr + stride + 4, stride);
            ((delegate*<byte*, byte*, int, void>)t.S)(predS.Ptr + 16, plane.Ptr + stride + 4, stride);

            Assert.True(plane.Span.SequenceEqual(planeBefore), $"Trial {trial}: a predictor wrote to the picture.");
            Assert.True(predC.Span.SequenceEqual(predS.Span),
                $"Trial {trial} (stride {stride}, neighbours of kind {trial % kiKinds}): the predictions differ.");
        }
    }

    // A random plane of n + 2 rows, the block at row 1, column 4, predicted in place.
    [Theory]
    [MemberData(nameof(DecNames))]
    public void Decoder_EqualsC(string kernel)
    {
        if (!Supported)
        {
            return;
        }
        Twin t = Find(DecTwins, kernel);
        int n = t.N;
        var r = new FixtureRng(SimdFill.Seed("dec" + kernel));
        for (int trial = 0; trial < kiTrials; trial++)
        {
            int stride = n + 4 + (int)(r.Next() % 41);
            using var planeC = new NativeBuffer(stride * (n + 2));
            using var planeS = new NativeBuffer(stride * (n + 2));
            Neighbours(ref r, trial, planeC.Ptr, stride, n);
            planeC.Span.CopyTo(planeS.Span);

            ((delegate*<byte*, int, void>)t.C)(planeC.Ptr + stride + 4, stride);
            ((delegate*<byte*, int, void>)t.S)(planeS.Ptr + stride + 4, stride);

            Assert.True(planeC.Span.SequenceEqual(planeS.Span),
                $"Trial {trial} (stride {stride}, neighbours of kind {trial % kiKinds}): the planes differ.");
        }
    }

    private const int kiKinds = 8;

    /// <summary>
    /// A random plane, then the n x n block at row 1, column 4's neighbours (the row above from
    /// the corner, and the column to the left) by kind: random, all 0, all 255, only 0 and 255,
    /// then steps from 0 to 255 or back at the middle of the row and of the column, all four
    /// ways. The steps give the plane predictors' largest and smallest gradients.
    /// </summary>
    private static void Neighbours(ref FixtureRng r, int trial, byte* plane, int stride, int n)
    {
        r.Fill(new Span<byte>(plane, stride * (n + 2)));
        byte* top = plane + 3;                // top[0] the corner, top[1 + x] above column x
        byte* left = plane + stride + 3;      // left[y * stride] beside row y
        int kind = trial % kiKinds;
        for (int x = 0; x <= n; x++)
        {
            top[x] = Neighbour(ref r, kind, top[x], x - 1 >= n / 2, true);
        }
        for (int y = 0; y < n; y++)
        {
            left[y * stride] = Neighbour(ref r, kind, left[y * stride], y >= n / 2, false);
        }
    }

    private static byte Neighbour(ref FixtureRng r, int kind, byte random, bool far, bool top) => kind switch
    {
        0 => random,
        1 => 0,
        2 => 255,
        3 => (byte)((r.Next() & 1) * 255),
        // Up along the row and down the column, each way.
        4 => (byte)(far ? 255 : 0),
        5 => (byte)(far ? 0 : 255),
        6 => (byte)(far == top ? 255 : 0),
        _ => (byte)(far == top ? 0 : 255),
    };

    [Fact]
    public void WelsInitIntraPredFuncs_ChoosesByTheFlags()
    {
        const uint kuiOff = uint.MaxValue & ~(WELS_CPU_SSE2 | WELS_CPU_NEON);
        var c = (SWelsFuncPtrList*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsFuncPtrList));
        var p = (SWelsFuncPtrList*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsFuncPtrList));
        try
        {
            Enc.WelsInitIntraPredFuncs(c, 0);
            foreach (uint flags in new[] { 0u, uint.MaxValue, kuiOff })
            {
                bool simd = Supported && flags == uint.MaxValue;
                Enc.WelsInitIntraPredFuncs(p, flags);
                CheckSlots(EncTwins, simd, (nint*)p->pfGetLumaI16x16Pred, (nint*)c->pfGetLumaI16x16Pred, I16_PRED_DC_A, true);
                CheckSlots(EncTwins, simd, (nint*)p->pfGetChromaPred, (nint*)c->pfGetChromaPred, C_PRED_A, false);
                CheckSlots(EncTwins, simd, (nint*)p->pfGetLumaI4x4Pred, (nint*)c->pfGetLumaI4x4Pred, I4_PRED_A, null);
            }
        }
        finally
        {
            NativeMemory.Free(c);
            NativeMemory.Free(p);
        }
    }

    [Fact]
    public void InitPredFunc_ChoosesTheIntraPredictorsByTheFlags()
    {
        const uint kuiOff = uint.MaxValue & ~(WELS_CPU_SSE2 | WELS_CPU_NEON);
        var c = (SWelsDecoderContext*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsDecoderContext));
        var p = (SWelsDecoderContext*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsDecoderContext));
        try
        {
            Dec.InitPredFunc(c, 0);
            foreach (uint flags in new[] { 0u, uint.MaxValue, kuiOff })
            {
                bool simd = Supported && flags == uint.MaxValue;
                Dec.InitPredFunc(p, flags);
                CheckSlots(DecTwins, simd, (nint*)p->pGetI16x16LumaPredFunc, (nint*)c->pGetI16x16LumaPredFunc, I16_PRED_DC_A, true);
                CheckSlots(DecTwins, simd, (nint*)p->pGetIChromaPredFunc, (nint*)c->pGetIChromaPredFunc, C_PRED_A, false);
                CheckSlots(DecTwins, simd, (nint*)p->pGetI4x4LumaPredFunc, (nint*)c->pGetI4x4LumaPredFunc, I4_PRED_A, null);
                CheckSlots(DecTwins, simd, (nint*)p->pGetI8x8LumaPredFunc, (nint*)c->pGetI8x8LumaPredFunc, I4_PRED_A, null);
            }
        }
        finally
        {
            NativeMemory.Free(c);
            NativeMemory.Free(p);
        }
    }

    /// <summary>
    /// Each of a table's slots: the twin's C or SIMD predictor where one has the slot (luma, true,
    /// for the 16x16 table, false for chroma, null for one with no twins), and otherwise the
    /// predictor the C table has.
    /// </summary>
    private static void CheckSlots(Twin[] twins, bool simd, nint* actual, nint* cTable, int slots, bool? luma)
    {
        for (int i = 0; i < slots; i++)
        {
            Twin? t = luma is bool l ? twins.Where(x => x.Luma == l && x.Mode == i).Cast<Twin?>().SingleOrDefault() : null;
            if (t is Twin twin)
            {
                Assert.True(cTable[i] == twin.C, $"Slot {i}: the C table doesn't hold {twin.Name}.");
                SimdTwin.Slot(simd, actual[i], twin.C, twin.S);
            }
            else
            {
                Assert.True(actual[i] == cTable[i], $"Slot {i} of a {(luma is null ? "4x4 or 8x8" : luma.Value ? "16x16" : "chroma")} table changed.");
            }
        }
    }
}
