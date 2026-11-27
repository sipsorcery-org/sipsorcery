//-----------------------------------------------------------------------------
// Filename: vaacalc_simd_unittest.cs
//
// Description: Checks the SIMD VAA statistics kernels
// (processing/vaacalcfuncs_simd.cs, P5.6) against the C ones. On the C build's
// vaacalc fixture rows, directly and through CVAACalculation, they must give
// the same hashes. On pictures of every kind (random, 255 against 0, only 0 and
// 255, checkerboards, near-equal) at sizes that are and aren't whole
// macroblocks and random strides, they must leave every output as the C# C
// kernels leave it, the guard macroblocks after the last included. And
// InitVaaFuncs must choose them by the CPU flags. Nothing runs where the
// machine has no SIMD kernels (WelsCpuHasSimd128).
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
using Xunit;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class vaacalc_simd_unittest
{
    private static bool Supported => sad_common_simd_unittest.Supported;

    private const int kiTrials = 3000;

    private static readonly string[] Kernels =
    [
        nameof(WelsVP.VAACalcSad_c), nameof(WelsVP.VAACalcSadVar_c), nameof(WelsVP.VAACalcSadSsd_c),
        nameof(WelsVP.VAACalcSadBgd_c), nameof(WelsVP.VAACalcSadSsdBgd_c),
    ];

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("vaacalc");

    public static IEnumerable<object[]> KernelNames() => Kernels.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Rows))]
    public void Simd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!Supported)
        {
            return;
        }
        var row = new KernelFixture("vaacalc", kernel, kase, sha1);
        using var hash = new FixtureHasher();
        if (kase == "invalid")
        {
            vaacalc_unittest.Invalid(hash, -1);
        }
        else if (kernel == "CVAACalculation")
        {
            vaacalc_unittest.Process(row, hash, -1);
        }
        else
        {
            vaacalc_unittest.Kernel(row, hash, simd: true);
        }
        Assert.Equal(sha1, hash.Hex());
    }

    // Widths and heights of 1 to 80 and 1 to 64 samples, so 0 to 5 and 0 to 4 macroblocks with
    // or without a part one left over; strides up to 32 more than the width.
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
            int w = 1 + (int)(r.Next() % 80), h = 1 + (int)(r.Next() % 64);
            int stride = w + (int)(r.Next() % 33);
            using var cur = new NativeBuffer(stride * h);
            using var @ref = new NativeBuffer(stride * h);
            SimdFill.Pair(ref r, trial, cur.Span, @ref.Span);
            byte[] curBefore = cur.Span.ToArray(), refBefore = @ref.Span.ToArray();

            // Both sets of outputs filled with the same noise.
            var r2 = r;
            using var oc = new vaacalc_unittest.Outputs(ref r, w, h);
            using var os = new vaacalc_unittest.Outputs(ref r2, w, h);
            int frameSadC = oc.FrameSad, frameSadS = os.FrameSad;

            vaacalc_unittest.Run(kernel, false, cur.Ptr, @ref.Ptr, w, h, stride, &frameSadC, oc);
            vaacalc_unittest.Run(kernel, true, cur.Ptr, @ref.Ptr, w, h, stride, &frameSadS, os);
            oc.FrameSad = frameSadC;
            os.FrameSad = frameSadS;

            using var hc = new FixtureHasher();
            using var hs = new FixtureHasher();
            oc.HashInto(hc);
            os.HashInto(hs);
            string where = $"trial {trial} ({w}x{h}, stride {stride}, pictures of kind {trial % 6})";
            Assert.True(cur.Span.SequenceEqual(curBefore) && @ref.Span.SequenceEqual(refBefore), $"{where}: a kernel wrote to a picture.");
            Assert.True(frameSadC == frameSadS, $"{where}: frame SAD {frameSadS}, C {frameSadC}.");
            Assert.True(hc.Hex() == hs.Hex(), $"{where}: the outputs differ.");
        }
    }

    [Fact]
    public void InitVaaFuncs_ChoosesByTheFlags()
    {
        const uint kuiOff = uint.MaxValue & ~(WELS_CPU_SSE2 | WELS_CPU_NEON);
        foreach (uint flags in new[] { 0u, uint.MaxValue, kuiOff })
        {
            bool simd = Supported && flags == uint.MaxValue;
            SVaaFuncs f = default;
            CVAACalculation.InitVaaFuncs(ref f, (int)flags);
            SimdTwin.Slot(simd, (nint)f.pfVAACalcSad,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, void>)&WelsVP.VAACalcSad_c,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, void>)&WelsVP.VAACalcSad_simd);
            SimdTwin.Slot(simd, (nint)f.pfVAACalcSadVar,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, void>)&WelsVP.VAACalcSadVar_c,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, void>)&WelsVP.VAACalcSadVar_simd);
            SimdTwin.Slot(simd, (nint)f.pfVAACalcSadSsd,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, int*, void>)&WelsVP.VAACalcSadSsd_c,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, int*, void>)&WelsVP.VAACalcSadSsd_simd);
            SimdTwin.Slot(simd, (nint)f.pfVAACalcSadBgd,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, int*, byte*, void>)&WelsVP.VAACalcSadBgd_c,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, int*, byte*, void>)&WelsVP.VAACalcSadBgd_simd);
            SimdTwin.Slot(simd, (nint)f.pfVAACalcSadSsdBgd,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, int*, int*, byte*, void>)&WelsVP.VAACalcSadSsdBgd_c,
                (nint)(delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, int*, int*, byte*, void>)&WelsVP.VAACalcSadSsdBgd_simd);
        }
    }
}
