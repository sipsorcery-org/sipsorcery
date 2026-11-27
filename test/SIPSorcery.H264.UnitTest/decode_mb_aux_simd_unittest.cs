//-----------------------------------------------------------------------------
// Filename: decode_mb_aux_simd_unittest.cs
//
// Description: Checks the decoder's SIMD inverse transforms
// (decoder/decode_mb_aux_simd.cs, P5.5) against the C ones. On the C build's
// fixture rows, through InitPredFunc's table, they must give the same hashes.
// On coefficients of every kind (any int16_t, extremes, typical, sparse), with
// blocks all 0, 0 at DC only or not, and non-zero counts set or not, at random
// strides, they must leave the plane and the coefficients as the C# C kernels
// leave them, round the blocks included. And InitPredFunc must choose them by
// the CPU flags. Nothing runs where the machine has no SIMD kernels
// (WelsCpuHasSimd128).
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
using SIPSorcery.H264.Decoder;
using Xunit;
using static SIPSorcery.H264.Decoder.WelsDec;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class decode_mb_aux_simd_unittest
{
    private static bool Supported => sad_common_simd_unittest.Supported;

    private const int kiTrials = 3000;

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("decode_mb_aux")
        .Where(r => (string)r[0] is nameof(IdctResAddPred_c) or nameof(IdctFourResAddPred_));

    [Theory]
    [MemberData(nameof(Rows))]
    public void Simd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!Supported)
        {
            return;
        }
        var row = new KernelFixture("decode_mb_aux", kernel, kase, sha1);
        string actual = kernel == nameof(IdctResAddPred_c)
            ? decode_mb_aux_unittest.Idct(&IdctResAddPred_simd, 4, row)
            : decode_mb_aux_unittest.IdctFour(row, uint.MaxValue);
        Assert.Equal(sha1, actual);
    }

    // A random plane of 6 rows, the block at row 1, column 2, at a random stride.
    [Fact]
    public void IdctResAddPred_EqualsC()
    {
        if (!Supported)
        {
            return;
        }
        var r = new FixtureRng(SimdFill.Seed(nameof(IdctResAddPred_c)));
        for (int trial = 0; trial < kiTrials; trial++)
        {
            int stride = 6 + (int)(r.Next() % 41);
            using var planeC = new NativeBuffer(stride * 6);
            using var planeS = new NativeBuffer(stride * 6);
            Fill(ref r, planeC.Span);
            planeC.Span.CopyTo(planeS.Span);
            using var rs = new SimdTwin(ref r, trial, 16);

            IdctResAddPred_c(planeC.Ptr + stride + 2, stride, rs.C);
            IdctResAddPred_simd(planeS.Ptr + stride + 2, stride, rs.S);

            if (!planeC.Span.SequenceEqual(planeS.Span) || !rs.Same)
            {
                Assert.Fail($"Trial {trial} (stride {stride}, {SimdTwin.KindName(trial)} coefficients): the {(rs.Same ? "planes" : "coefficients")} differ.");
            }
        }
    }

    // An 8x8 block at row 1, column 2 of a 10-row plane. Each block all 0, 0 at DC only, or
    // as drawn, and each of pNzc[0, 1, 4, 5] 0 or not: every combination of skipped and
    // transformed blocks.
    [Fact]
    public void IdctFourResAddPred_EqualsC()
    {
        if (!Supported)
        {
            return;
        }
        var r = new FixtureRng(SimdFill.Seed(nameof(IdctFourResAddPred_)));
        sbyte* pNzc = stackalloc sbyte[6];
        int transformed = 0, skipped = 0;
        for (int trial = 0; trial < kiTrials; trial++)
        {
            int stride = 10 + (int)(r.Next() % 41);
            using var planeC = new NativeBuffer(stride * 10);
            using var planeS = new NativeBuffer(stride * 10);
            Fill(ref r, planeC.Span);
            planeC.Span.CopyTo(planeS.Span);
            using var rs = new SimdTwin(ref r, trial, 64);
            for (int b = 0; b < 4; b++)
            {
                int mode = (int)(r.Next() % 3);
                for (int i = 0; i < 16; i++)
                {
                    if (mode == 0 || (mode == 1 && i == 0))
                    {
                        rs.C[b * 16 + i] = rs.S[b * 16 + i] = 0;
                    }
                }
            }
            for (int i = 0; i < 6; i++)
            {
                pNzc[i] = (r.Next() % 3) switch { 0 => 0, 1 => 1, _ => (sbyte)r.Next() };
            }
            foreach (int b in new[] { 0, 1, 2, 3 })
            {
                int nzc = pNzc[b < 2 ? b : b + 2];
                if (nzc != 0 || rs.C[b * 16] != 0)
                {
                    transformed++;
                }
                else
                {
                    skipped++;
                }
            }

            IdctFourResAddPred_(planeC.Ptr + stride + 2, stride, rs.C, pNzc);
            IdctFourResAddPred_simd(planeS.Ptr + stride + 2, stride, rs.S, pNzc);

            if (!planeC.Span.SequenceEqual(planeS.Span) || !rs.Same)
            {
                Assert.Fail($"Trial {trial} (stride {stride}, nzc {pNzc[0]} {pNzc[1]} {pNzc[4]} {pNzc[5]}, {SimdTwin.KindName(trial)} coefficients): " +
                            $"the {(rs.Same ? "planes" : "coefficients")} differ.");
            }
        }
        Assert.True(transformed > kiTrials && skipped > kiTrials / 4, $"{transformed} blocks transformed, {skipped} skipped.");
    }

    /// <summary>A plane of every kind: random, all 0, all 255, only 0 and 255.</summary>
    private static void Fill(ref FixtureRng r, Span<byte> plane)
    {
        int kind = (int)(r.Next() % 4);
        for (int i = 0; i < plane.Length; i++)
        {
            plane[i] = kind switch { 0 => r.NextByte(), 1 => 0, 2 => 255, _ => (byte)((r.Next() & 1) * 255) };
        }
    }

    [Fact]
    public void InitPredFunc_ChoosesByTheFlags()
    {
        const uint kuiOff = uint.MaxValue & ~(WELS_CPU_SSE2 | WELS_CPU_NEON);
        foreach (uint flags in new[] { 0u, uint.MaxValue, kuiOff })
        {
            bool simd = Supported && flags == uint.MaxValue;
            var pCtx = (SWelsDecoderContext*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsDecoderContext));
            try
            {
                InitPredFunc(pCtx, flags);
                SimdTwin.Slot(simd, (nint)pCtx->pIdctResAddPredFunc,
                    (nint)(delegate*<byte*, int, short*, void>)&IdctResAddPred_c, (nint)(delegate*<byte*, int, short*, void>)&IdctResAddPred_simd);
                SimdTwin.Slot(simd, (nint)pCtx->pIdctFourResAddPredFunc,
                    (nint)(delegate*<byte*, int, short*, sbyte*, void>)&IdctFourResAddPred_,
                    (nint)(delegate*<byte*, int, short*, sbyte*, void>)&IdctFourResAddPred_simd);
                Assert.True(pCtx->pIdctResAddPredFunc8x8 == (delegate*<byte*, int, short*, void>)&IdctResAddPred8x8_c);
            }
            finally
            {
                NativeMemory.Free(pCtx);
            }
        }
    }
}
