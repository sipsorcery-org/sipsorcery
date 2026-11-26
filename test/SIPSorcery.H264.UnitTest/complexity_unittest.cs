//-----------------------------------------------------------------------------
// Filename: complexity_unittest.cs
//
// Description: Checks the port of codec/processing/src/complexityanalysis
// (CComplexityAnalysis, camera half) against the C build. Mirrors
// KernelsComplexity in refharness/src/kernels_processing.cpp: Set, Process and
// Get for each mode, with and without the background exclusion, over GOM sizes
// that do and do not split macroblock rows, on VAACalcSadVar_c statistics and
// on raw noise, then the invalid inputs.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;

namespace SIPSorcery.H264.UnitTest;

public unsafe class complexity_unittest
{
    // kRefMbTypes.
    private static readonly uint[] RefMbTypes = [0, 1, 2, 4, 8, 0x10, 0x100, 0x7, 0xfffffff8u, 0xffffffffu];

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("complexity");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Complexity_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("complexity", kernel, kase, sha1);
        using var hash = new FixtureHasher();
        if (kase == "invalid")
        {
            Invalid(hash);
        }
        else
        {
            Analyse(row, hash);
        }
        Assert.Equal(sha1, hash.Hex());
    }

    private static void Analyse(KernelFixture row, FixtureHasher hash)
    {
        int w = row.Param("w"), h = row.Param("h"), stride = row.Param("stride");
        int gom = row.Param("gom"), data = row.Param("data"), seed = row.Param("seed");
        int mbs = (w >> 4) * (h >> 4);
        int goms = (mbs + gom - 1) / gom;

        var r = new FixtureRng((uint)seed);
        using var pics = new vaacalc_unittest.Pictures(ref r, w, h, stride, seed & 3);
        using var o = new vaacalc_unittest.Outputs(ref r, w, h);
        if (data == 0)
        {
            int frameSad = o.FrameSad;
            WelsVP.VAACalcSadVar_c(pics.Cur, pics.Ref, w, h, stride, &frameSad, o.Sad8x8, o.Sum16x16, o.SqSum16x16);
            o.FrameSad = frameSad;
        }
        SVAACalcResult res = o.Result();
        using var b = new Buffers(ref r, mbs, goms);
        SComplexityAnalysisParam param = default;
        param.iComplexityAnalysisMode = row.Param("mode");
        param.iCalcBgd = row.Param("bgd");
        param.iMbNumInGom = gom;
        param.iFrameComplexity = Noise64(ref r);
        b.Attach(ref param);
        param.pCalcResult = &res;
        SComplexityAnalysisParam output = default;
        output.iFrameComplexity = Noise64(ref r);

        var ca = new CComplexityAnalysis(0);
        SPixMap cur = vaacalc_unittest.PixMap(pics.Cur, w, h, stride), refMap = vaacalc_unittest.PixMap(pics.Ref, w, h, stride);
        hash.AddI32((int)ca.Set((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &param));
        hash.AddI32((int)ca.Process((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &cur, &refMap));
        hash.AddI32((int)ca.Get((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &output));
        hash.AddI64(output.iFrameComplexity);
        hash.AddI64(param.iFrameComplexity);
        b.HashInto(hash);
    }

    private static void Invalid(FixtureHasher hash)
    {
        const int w = 48, h = 32, stride = 64;
        var r = new FixtureRng(400);
        using var pics = new vaacalc_unittest.Pictures(ref r, w, h, stride, 1);
        using var o = new vaacalc_unittest.Outputs(ref r, w, h);
        SVAACalcResult res = o.Result();
        using var b = new Buffers(ref r, 6, 3);
        SComplexityAnalysisParam param = default;
        param.iMbNumInGom = 2;
        param.iFrameComplexity = Noise64(ref r);
        b.Attach(ref param);
        param.pCalcResult = &res;
        SComplexityAnalysisParam output = default;
        var ca = new CComplexityAnalysis(0);
        SPixMap cur = vaacalc_unittest.PixMap(pics.Cur, w, h, stride), refMap = vaacalc_unittest.PixMap(pics.Ref, w, h, stride);
        hash.AddI32((int)ca.Set((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, null));
        hash.AddI32((int)ca.Get((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, null));
        foreach (int mode in new[] { 1, -3, 7 })
        {
            param.iComplexityAnalysisMode = mode;
            output.iFrameComplexity = Noise64(ref r);
            hash.AddI32((int)ca.Set((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &param));
            hash.AddI32((int)ca.Process((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &cur, &refMap));
            hash.AddI32((int)ca.Get((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &output));
            hash.AddI64(output.iFrameComplexity);
        }
        b.HashInto(hash);
    }

    private static long Noise64(ref FixtureRng r)
    {
        uint hi = r.Next();
        return (long)(((ulong)hi << 32) | r.Next());
    }

    // ComplexityBuffers: the per-macroblock inputs and the per-GOM outputs, plus two guards each,
    // filled in the C harness's order.
    private sealed class Buffers : IDisposable
    {
        private readonly NativeBuffer _bgd, _refMbType, _gomComplexity, _gomForeground;
        private readonly int _goms;

        public Buffers(ref FixtureRng r, int mbs, int goms)
        {
            _goms = goms + 2;
            _bgd = new NativeBuffer(mbs + 2);
            _refMbType = new NativeBuffer((mbs + 2) * 4);
            _gomComplexity = new NativeBuffer(_goms * 4);
            _gomForeground = new NativeBuffer(_goms * 4);
            for (int i = 0; i < mbs + 2; i++)
            {
                uint x = r.Next();
                _bgd.Ptr[i] = (x & 1) != 0 ? (byte)0 : (byte)(x >> 8);
            }
            for (int i = 0; i < mbs + 2; i++)
            {
                ((uint*)_refMbType.Ptr)[i] = RefMbTypes[r.Next() % 10];
            }
            r.Fill(_gomComplexity.Span);
            for (int i = 0; i < _goms; i++)
            {
                ((int*)_gomForeground.Ptr)[i] = (int)(r.Next() % 1000);
            }
        }

        public void Attach(ref SComplexityAnalysisParam param)
        {
            param.pGomComplexity = (int*)_gomComplexity.Ptr;
            param.pGomForegroundBlockNum = (int*)_gomForeground.Ptr;
            param.pBackgroundMbFlag = (sbyte*)_bgd.Ptr;
            param.uiRefMbType = (uint*)_refMbType.Ptr;
        }

        public void HashInto(FixtureHasher h)
        {
            for (int i = 0; i < _goms; i++)
            {
                h.AddI32(((int*)_gomComplexity.Ptr)[i]);
            }
            for (int i = 0; i < _goms; i++)
            {
                h.AddI32(((int*)_gomForeground.Ptr)[i]);
            }
        }

        public void Dispose()
        {
            _bgd.Dispose();
            _refMbType.Dispose();
            _gomComplexity.Dispose();
            _gomForeground.Dispose();
        }
    }
}
