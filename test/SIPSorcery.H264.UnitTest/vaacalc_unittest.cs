//-----------------------------------------------------------------------------
// Filename: vaacalc_unittest.cs
//
// Description: Checks the port of codec/processing/src/vaacalc (the five VAA
// statistics kernels and CVAACalculation) against the C build. Mirrors
// KernelsVaaCalc in refharness/src/kernels_processing.cpp: each kernel on a
// table of picture sizes, strides and contents with the outputs pre-filled
// with noise, then CVAACalculation's Set and Process for each flag
// combination and the invalid inputs.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5b).
// 26 Nov 2026  Claude          The pictures, outputs and pixel map shared with complexity_unittest (E6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;

namespace SIPSorcery.H264.UnitTest;

public unsafe class vaacalc_unittest
{
    // The C harness's kVaaMargin: room before the first row and after the last.
    private const int Margin = 512;

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("vaacalc");

    [Theory]
    [MemberData(nameof(Rows))]
    public void VaaCalc_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("vaacalc", kernel, kase, sha1);
        using var hash = new FixtureHasher();
        if (kase == "invalid")
        {
            Invalid(hash);
        }
        else if (kernel == "CVAACalculation")
        {
            Process(row, hash);
        }
        else
        {
            Kernel(row, hash);
        }
        Assert.Equal(sha1, hash.Hex());
    }

    private static void Kernel(KernelFixture row, FixtureHasher hash)
    {
        int w = row.Param("w"), h = row.Param("h"), stride = row.Param("stride");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var pics = new Pictures(ref r, w, h, stride, row.Param("kind"));
        using var o = new Outputs(ref r, w, h);
        int* sad = o.Sad8x8, sum = o.Sum16x16, sq = o.SqSum16x16, ssd = o.Ssd16x16, sd = o.Sd8x8;
        byte* mad = o.Mad8x8;
        int frameSad = o.FrameSad;
        switch (row.Kernel)
        {
            case nameof(WelsVP.VAACalcSad_c):
                WelsVP.VAACalcSad_c(pics.Cur, pics.Ref, w, h, stride, &frameSad, sad);
                break;
            case nameof(WelsVP.VAACalcSadVar_c):
                WelsVP.VAACalcSadVar_c(pics.Cur, pics.Ref, w, h, stride, &frameSad, sad, sum, sq);
                break;
            case nameof(WelsVP.VAACalcSadSsd_c):
                WelsVP.VAACalcSadSsd_c(pics.Cur, pics.Ref, w, h, stride, &frameSad, sad, sum, sq, ssd);
                break;
            case nameof(WelsVP.VAACalcSadBgd_c):
                WelsVP.VAACalcSadBgd_c(pics.Cur, pics.Ref, w, h, stride, &frameSad, sad, sd, mad);
                break;
            case nameof(WelsVP.VAACalcSadSsdBgd_c):
                WelsVP.VAACalcSadSsdBgd_c(pics.Cur, pics.Ref, w, h, stride, &frameSad, sad, sum, sq, ssd, sd, mad);
                break;
            default:
                throw new ArgumentException($"No C# kernel {row.Kernel}.");
        }
        o.FrameSad = frameSad;
        o.HashInto(hash);
    }

    private static void Process(KernelFixture row, FixtureHasher hash)
    {
        int w = row.Param("w"), h = row.Param("h"), stride = row.Param("stride");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var pics = new Pictures(ref r, w, h, stride, 1);
        using var o = new Outputs(ref r, w, h);
        SVAACalcResult res = o.Result();
        SVAACalcParam param = default;
        param.iCalcVar = row.Param("var");
        param.iCalcBgd = row.Param("bgd");
        param.iCalcSsd = row.Param("ssd");
        param.pCalcResult = &res;
        var calc = new CVAACalculation(0);
        SPixMap cur = PixMap(pics.Cur, w, h, stride), refMap = PixMap(pics.Ref, w, h, stride);
        hash.AddI32((int)calc.Set((int)EMethods.METHOD_VAA_STATISTICS, &param));
        hash.AddI32((int)calc.Process((int)EMethods.METHOD_VAA_STATISTICS, &cur, &refMap));
        o.FrameSad = res.iFrameSad;
        hash.AddI32(res.pCurY == pics.Cur ? 1 : 0);
        hash.AddI32(res.pRefY == pics.Ref ? 1 : 0);
        o.HashInto(hash);
    }

    private static void Invalid(FixtureHasher hash)
    {
        const int w = 16, h = 16, stride = 16;
        var r = new FixtureRng(200);
        using var pics = new Pictures(ref r, w, h, stride, 0);
        using var o = new Outputs(ref r, w, h);
        SVAACalcResult res = o.Result();
        SVAACalcParam param = default;
        var calc = new CVAACalculation(0);
        hash.AddI32((int)calc.Set((int)EMethods.METHOD_VAA_STATISTICS, null));
        hash.AddI32((int)calc.Set((int)EMethods.METHOD_VAA_STATISTICS, &param));
        param.pCalcResult = &res;
        hash.AddI32((int)calc.Set((int)EMethods.METHOD_VAA_STATISTICS, &param));
        SPixMap cur = PixMap(pics.Cur, w, h, stride), refMap = PixMap(null, w, h, stride);
        hash.AddI32((int)calc.Process((int)EMethods.METHOD_VAA_STATISTICS, &cur, &refMap));
        hash.AddI32((int)calc.Process((int)EMethods.METHOD_VAA_STATISTICS, &refMap, &cur));
        o.FrameSad = res.iFrameSad;
        hash.AddI32(res.pCurY == null ? 1 : 0);
        hash.AddI32(res.pRefY == null ? 1 : 0);
        o.HashInto(hash);
    }

    internal static SPixMap PixMap(byte* p, int w, int h, int stride)
    {
        SPixMap m = default;
        m.pPixel[0] = p;
        m.iSizeInBits = 8;
        m.iStride[0] = stride;
        m.sRect.iRectWidth = w;
        m.sRect.iRectHeight = h;
        m.eFormat = EVideoFormat.VIDEO_FORMAT_I420;
        return m;
    }

    // MakePictures: kind 0 unrelated, 1 the reference within 4 of the current
    // picture, 2 identical, 3 only 0 and 255.
    internal sealed class Pictures : IDisposable
    {
        private readonly NativeBuffer _cur, _ref;

        public Pictures(ref FixtureRng r, int w, int h, int stride, int kind)
        {
            int n = stride * h + 2 * Margin;
            _cur = new NativeBuffer(n);
            _ref = new NativeBuffer(n);
            r.Fill(_cur.Span);
            r.Fill(_ref.Span);
            byte* c = _cur.Ptr, f = _ref.Ptr;
            for (int i = 0; i < n; i++)
            {
                switch (kind)
                {
                    case 1:
                        int v = c[i] + (int)(r.Next() % 9) - 4;
                        f[i] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                        break;
                    case 2:
                        f[i] = c[i];
                        break;
                    case 3:
                        c[i] = (byte)((c[i] & 1) != 0 ? 255 : 0);
                        f[i] = (byte)((f[i] & 1) != 0 ? 255 : 0);
                        break;
                }
            }
        }

        public byte* Cur => _cur.Ptr + Margin;
        public byte* Ref => _ref.Ptr + Margin;

        public void Dispose()
        {
            _cur.Dispose();
            _ref.Dispose();
        }
    }

    // VaaOutputs: per macroblock of the rounded-up size plus two guards, filled
    // with noise in the C harness's order.
    internal sealed class Outputs : IDisposable
    {
        private readonly NativeBuffer _sad, _sum, _sq, _ssd, _sd, _mad;
        private readonly int _mbs;

        public int FrameSad;

        public Outputs(ref FixtureRng r, int w, int h)
        {
            _mbs = ((w + 15) >> 4) * ((h + 15) >> 4) + 2;
            _sad = new NativeBuffer(_mbs * 16);
            _sum = new NativeBuffer(_mbs * 4);
            _sq = new NativeBuffer(_mbs * 4);
            _ssd = new NativeBuffer(_mbs * 4);
            _sd = new NativeBuffer(_mbs * 16);
            _mad = new NativeBuffer(_mbs * 4);
            FrameSad = (int)r.Next();
            r.Fill(_sad.Span);
            r.Fill(_sum.Span);
            r.Fill(_sq.Span);
            r.Fill(_ssd.Span);
            r.Fill(_sd.Span);
            r.Fill(_mad.Span);
        }

        public int* Sad8x8 => (int*)_sad.Ptr;
        public int* Sum16x16 => (int*)_sum.Ptr;
        public int* SqSum16x16 => (int*)_sq.Ptr;
        public int* Ssd16x16 => (int*)_ssd.Ptr;
        public int* Sd8x8 => (int*)_sd.Ptr;
        public byte* Mad8x8 => _mad.Ptr;

        public SVAACalcResult Result() => new SVAACalcResult
        {
            pSad8x8 = Sad8x8,
            pSsd16x16 = Ssd16x16,
            pSum16x16 = Sum16x16,
            pSumOfSquare16x16 = SqSum16x16,
            pSumOfDiff8x8 = Sd8x8,
            pMad8x8 = Mad8x8,
            iFrameSad = FrameSad,
        };

        public void HashInto(FixtureHasher h)
        {
            h.AddI32(FrameSad);
            AddInts(h, Sad8x8, _mbs * 4);
            AddInts(h, Sum16x16, _mbs);
            AddInts(h, SqSum16x16, _mbs);
            AddInts(h, Ssd16x16, _mbs);
            AddInts(h, Sd8x8, _mbs * 4);
            h.Add(_mad.Span);
        }

        private static void AddInts(FixtureHasher h, int* p, int n)
        {
            for (int i = 0; i < n; i++)
            {
                h.AddI32(p[i]);
            }
        }

        public void Dispose()
        {
            _sad.Dispose();
            _sum.Dispose();
            _sq.Dispose();
            _ssd.Dispose();
            _sd.Dispose();
            _mad.Dispose();
        }
    }
}
