//-----------------------------------------------------------------------------
// Filename: sample_simd_unittest.cs
//
// Description: Checks the SIMD SATD kernels (encoder/sample_simd.cs, P5.2)
// against the C ones: on the C build's fixture rows, which must give the same
// hashes, and on random and extreme blocks at random strides, which must give
// the same results as the C# C kernels. Also checks that WelsInitSampleSadFunc
// puts the SIMD kernels in the table when the flags allow them, and leaves the
// Combined3 slots NULL either way. Nothing runs where the machine has no SIMD
// kernels (WelsCpuHasSimd128).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.2).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.H264.Encoder;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class sample_simd_unittest
{
    private static readonly string[] Sizes = { "4x4", "8x4", "4x8", "8x8", "16x8", "8x16", "16x16" };

    public static IEnumerable<object[]> SatdRows =>
        KernelFixtures.Group("sample").Where(row => ((string)row[0]).StartsWith("WelsSampleSatd") &&
                                                    !((string)row[0]).Contains("Combined3"));

    public static IEnumerable<object[]> Kernels => Sizes.Select(size => new object[] { $"WelsSampleSatd{size}_c" });

    [Theory]
    [MemberData(nameof(SatdRows))]
    public void Simd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!sad_common_simd_unittest.Supported)
        {
            return;
        }
        var row = new KernelFixture("sample", kernel, kase, sha1);
        Lookup(kernel, out _, out var simd, out int w, out int h);
        Assert.Equal(sha1, sample_unittest.Satd(simd, w, h, row));
    }

    [Theory]
    [MemberData(nameof(Kernels))]
    public void Simd_EqualsC(string kernel)
    {
        if (!sad_common_simd_unittest.Supported)
        {
            return;
        }
        Lookup(kernel, out var c, out var simd, out int w, out int h);
        var r = new FixtureRng(SimdFill.Seed(kernel));
        for (int trial = 0; trial < 1500; trial++)
        {
            int s1 = w + (int)(r.Next() % 40);
            int s2 = w + (int)(r.Next() % 40);
            using var a = new NativeBuffer(s1 * h);
            using var b = new NativeBuffer(s2 * h);
            SimdFill.Pair(ref r, trial, a.Span, b.Span);
            Assert.Equal(c(a.Ptr, s1, b.Ptr, s2), simd(a.Ptr, s1, b.Ptr, s2));
        }
    }

    [Fact]
    public void InitSampleSadFunc_ChoosesByTheFlags()
    {
        SWelsFuncPtrList list = default;
        WelsInitSampleSadFunc(&list, 0);
        Assert.True(list.sSampleDealingFuncs.pfSampleSatd[BLOCK_16x16] == (delegate*<byte*, int, byte*, int, int>)&WelsSampleSatd16x16_c);
        Assert.True(list.sSampleDealingFuncs.pfSampleSad[BLOCK_8x8] == (delegate*<byte*, int, byte*, int, int>)&WelsSampleSad8x8_c);

        list = default;
        WelsInitSampleSadFunc(&list, uint.MaxValue);
        if (sad_common_simd_unittest.Supported)
        {
            Assert.True(list.sSampleDealingFuncs.pfSampleSatd[BLOCK_16x16] == (delegate*<byte*, int, byte*, int, int>)&WelsSampleSatd16x16_simd);
            Assert.True(list.sSampleDealingFuncs.pfSampleSad[BLOCK_8x8] == (delegate*<byte*, int, byte*, int, int>)&WelsSampleSad8x8_simd);
            Assert.True(list.sSampleDealingFuncs.pfSample4Sad[BLOCK_16x16] == (delegate*<byte*, int, byte*, int, int*, void>)&WelsSampleSadFour16x16_simd);
        }
        // The port follows upstream's C path, where these stay NULL, on every machine.
        Assert.True(list.sSampleDealingFuncs.pfIntra4x4Combined3Satd == null);
        Assert.True(list.sSampleDealingFuncs.pfIntra8x8Combined3Satd == null);
        Assert.True(list.sSampleDealingFuncs.pfIntra8x8Combined3Sad == null);
        Assert.True(list.sSampleDealingFuncs.pfIntra16x16Combined3Satd == null);
        Assert.True(list.sSampleDealingFuncs.pfIntra16x16Combined3Sad == null);
    }

    [Fact]
    public void FeatureMask_ZeroTurnsTheSimdKernelsOff()
    {
        // Only the function under test reads the mask here, so other tests running at the same
        // time are not affected: they never set it, and they give the same results either way.
        uint saved = WelsCpuFeatureMask;
        try
        {
            WelsCpuFeatureMask = 0;
            Assert.Equal(0u, WelsCPUFeatureDetect(null));
            Assert.False(WelsCpuHasSimd128(WelsCPUFeatureDetect(null)));
        }
        finally
        {
            WelsCpuFeatureMask = saved;
        }
        Assert.Equal(sad_common_simd_unittest.Supported, WelsCpuHasSimd128(WelsCPUFeatureDetect(null)));
    }

    // Out parameters rather than a tuple: function pointers can't be generic arguments.
    private static void Lookup(string kernel, out delegate*<byte*, int, byte*, int, int> c,
                               out delegate*<byte*, int, byte*, int, int> simd, out int w, out int h)
    {
        switch (kernel)
        {
            case nameof(WelsSampleSatd4x4_c): c = &WelsSampleSatd4x4_c; simd = &WelsSampleSatd4x4_simd; w = 4; h = 4; break;
            case nameof(WelsSampleSatd8x4_c): c = &WelsSampleSatd8x4_c; simd = &WelsSampleSatd8x4_simd; w = 8; h = 4; break;
            case nameof(WelsSampleSatd4x8_c): c = &WelsSampleSatd4x8_c; simd = &WelsSampleSatd4x8_simd; w = 4; h = 8; break;
            case nameof(WelsSampleSatd8x8_c): c = &WelsSampleSatd8x8_c; simd = &WelsSampleSatd8x8_simd; w = 8; h = 8; break;
            case nameof(WelsSampleSatd16x8_c): c = &WelsSampleSatd16x8_c; simd = &WelsSampleSatd16x8_simd; w = 16; h = 8; break;
            case nameof(WelsSampleSatd8x16_c): c = &WelsSampleSatd8x16_c; simd = &WelsSampleSatd8x16_simd; w = 8; h = 16; break;
            case nameof(WelsSampleSatd16x16_c): c = &WelsSampleSatd16x16_c; simd = &WelsSampleSatd16x16_simd; w = 16; h = 16; break;
            default: throw new ArgumentException($"No SIMD kernel for {kernel}.");
        }
    }
}
