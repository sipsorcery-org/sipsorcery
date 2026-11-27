//-----------------------------------------------------------------------------
// Filename: aq_unittest.cs
//
// Description: Checks the port of the adaptive quantisation
// (SampleVariance16x16_c and CAdaptiveQuantization, codec/processing/src/
// adaptivequantization) against the C build. Mirrors KernelsAq in
// refharness/src/kernels_processing.cpp: the variance kernel on four kinds of
// content and three strides, then Set, Process and Get in the quality and
// bitrate modes, on the VAA statistics of the pictures and on the kernel's
// own variances, and the invalid inputs.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;
using static SIPSorcery.H264.UnitTest.PreprocessFixtures;

namespace SIPSorcery.H264.UnitTest;

public unsafe class aq_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("aq");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Aq_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("aq", kernel, kase, sha1);
        using var hash = new FixtureHasher();
        if (kase == "invalid")
        {
            var aq = new CAdaptiveQuantization(0);
            hash.AddI32((int)aq.Set(0, null));
            hash.AddI32((int)aq.Get(0, null));
        }
        else if (kernel == nameof(WelsVP.SampleVariance16x16_c))
        {
            Variance(row, hash);
        }
        else
        {
            Process(row, hash);
        }
        Assert.Equal(sha1, hash.Hex());
    }

    private static void Variance(KernelFixture row, FixtureHasher hash)
    {
        int stride = row.Param("stride");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var pics = new vaacalc_unittest.Pictures(ref r, 16, 16, stride, row.Param("kind"));
        SMotionTextureUnit u;
        u.uiMotionIndex = (ushort)r.Next();
        u.uiTextureIndex = (ushort)r.Next();
        WelsVP.SampleVariance16x16_c(pics.Ref, stride, pics.Cur, stride, &u);
        hash.AddI32(u.uiMotionIndex);
        hash.AddI32(u.uiTextureIndex);
    }

    private static void Process(KernelFixture row, FixtureHasher hash)
    {
        int w = row.Param("w"), h = row.Param("h"), stride = row.Param("stride");
        // kAqSizes: the chroma stride is half the luma's.
        var s = new FixtureYuvSize(w, h, stride, stride >> 1);
        int vaa = row.Param("vaa");
        var r = new FixtureRng((uint)row.Param("seed"));
        MakeYuvPair(ref r, s, row.Param("mix"), out FixtureYuv cur, out FixtureYuv refPic);
        using (cur)
        using (refPic)
        {
            using var o = new vaacalc_unittest.Outputs(ref r, w, h);
            RunVaa(cur, refPic, s, o);
            SVAACalcResult res = o.Result();
            res.pCurY = vaa != 0 ? cur.Pl[0].P : null;
            res.pRefY = vaa != 0 ? refPic.Pl[0].P : null;
            int mbs = (w >> 4) * (h >> 4);
            using var mt = new NativeBuffer((mbs + 2) * sizeof(SMotionTextureUnit));
            r.Fill(mt.Span);
            using var dqp = new NativeBuffer(mbs + 2);
            r.Fill(dqp.Span);
            SAdaptiveQuantizationParam param = default;
            param.iAdaptiveQuantMode = row.Param("mode");
            param.pCalcResult = &res;
            param.pMotionTextureUnit = (SMotionTextureUnit*)mt.Ptr;
            param.pMotionTextureIndexToDeltaQp = (sbyte*)dqp.Ptr;
            param.iAverMotionTextureIndexToDeltaQp = (int)r.Next();
            SAdaptiveQuantizationParam output = default;
            output.iAverMotionTextureIndexToDeltaQp = (int)r.Next();

            var aq = new CAdaptiveQuantization(0);
            SPixMap mc = cur.Map(), mr = refPic.Map();
            hash.AddI32((int)aq.Set(0, &param));
            hash.AddI32((int)aq.Process(0, &mc, &mr));
            hash.AddI32((int)aq.Get(0, &output));
            hash.AddI32(output.iAverMotionTextureIndexToDeltaQp);
            hash.AddI32(param.iAverMotionTextureIndexToDeltaQp);
            hash.Add(mt.Span);
            hash.Add(dqp.Span);
        }
    }
}
