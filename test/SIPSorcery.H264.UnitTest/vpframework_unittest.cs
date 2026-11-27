//-----------------------------------------------------------------------------
// Filename: vpframework_unittest.cs
//
// Description: Checks the port of the video processing framework
// (CVpFrameWork, codec/processing/src/common/WelsFrameWork.cpp) against the C
// build. Mirrors KernelsVpFrameWork in refharness/src/kernels_processing.cpp:
// Process through WelsCreateVpInterface for each change CheckValid must catch
// (or let through), for the colour space conversion, the denoiser, the VAA
// statistics and the downsampling; then on one framework Init, Uninit,
// Flush, SpecialFeature, Get and Set for the ported methods and the camera
// preprocessing's calls in the encoder's order.
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

using System;
using System.Collections.Generic;
using Xunit;
using static SIPSorcery.H264.UnitTest.PreprocessFixtures;

namespace SIPSorcery.H264.UnitTest;

public unsafe class vpframework_unittest
{
    // kRefMbTypes.
    private static readonly uint[] RefMbTypes = [0, 1, 2, 4, 8, 0x10, 0x100, 0x7, 0xfffffff8u, 0xffffffffu];

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("vpframework");

    [Theory]
    [MemberData(nameof(Rows))]
    public void VpFrameWork_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("vpframework", kernel, kase, sha1);
        using var hash = new FixtureHasher();
        if (kernel == "Dispatch")
        {
            Dispatch(hash);
        }
        else
        {
            Process(row, hash);
        }
        Assert.Equal(sha1, hash.Hex());
    }

    // Mutate.
    private static void Mutate(int m, ref SPixMap s, ref SPixMap d)
    {
        switch (m)
        {
            case 1: s.eFormat = EVideoFormat.VIDEO_FORMAT_RGB; break;
            case 2: s.eFormat = EVideoFormat.VIDEO_FORMAT_YV12; break;
            case 3: s.eFormat = d.eFormat = EVideoFormat.VIDEO_FORMAT_YV12; break;
            case 4: s.sRect.iRectWidth = 0; break;
            case 5: s.sRect.iRectHeight = -1; break;
            case 6:
                s.sRect.iRectWidth = s.iStride[0] = 4096;
                s.sRect.iRectHeight = 2305;
                break;
            case 7: s.sRect.iRectTop = s.sRect.iRectHeight; break;
            case 8: s.sRect.iRectLeft = s.sRect.iRectWidth; break;
            case 9:
                s.sRect.iRectTop = s.sRect.iRectHeight - 1;
                s.sRect.iRectLeft = s.sRect.iRectWidth - 1;
                break;
            case 10: s.iStride[0] = s.sRect.iRectWidth - 1; break;
            case 11: d.sRect.iRectWidth = 0; break;
            case 12: d.iStride[0] = d.sRect.iRectWidth - 1; break;
            case 13: d.eFormat = EVideoFormat.VIDEO_FORMAT_YV12; break;
            case 14: s.pPixel[1] = (byte*)null; break;
            case 15: s.iStride[2] = 0; break;
            case 16: s.iStride[1] = (s.sRect.iRectWidth >> 1) - 1; break;
            case 17: d.pPixel[2] = (byte*)null; break;
            case 18: d.iStride[1] = -1; break;
            case 19: d.iStride[2] = (d.sRect.iRectWidth >> 1) - 1; break;
            case 20: s.pPixel[0] = (byte*)null; break;
            case 21: d.pPixel[0] = (byte*)null; break;
            case 22:
                s.sRect.iRectWidth = 0;
                s.sRect.iRectLeft = -1;
                break;
            case 23:
                d.sRect.iRectWidth = 0;
                d.sRect.iRectLeft = -1;
                break;
        }
    }

    private static void Process(KernelFixture row, FixtureHasher hash)
    {
        int method = row.Param("method"), m = row.Param("mutation");
        var r = new FixtureRng((uint)row.Param("seed"));
        MakeYuvPair(ref r, new FixtureYuvSize(32, 32, 64, 32), 0, out FixtureYuv src, out FixtureYuv refPic);
        using (src)
        using (refPic)
        {
            using var half = new FixtureYuv(ref r, new FixtureYuvSize(16, 16, 32, 16));
            using var o = new vaacalc_unittest.Outputs(ref r, 32, 32);
            SVAACalcResult res = o.Result();
            SVAACalcParam vp = default;
            vp.iCalcVar = vp.iCalcBgd = vp.iCalcSsd = 1;
            vp.pCalcResult = &res;
            CVpFrameWork* pVp = null;
            hash.AddI32((int)WelsVP.WelsCreateVpInterface(&pVp, WelsVP.WELSVP_INTERFACE_VERION));
            hash.AddI32((int)pVp->Set((int)EMethods.METHOD_VAA_STATISTICS, &vp));
            SPixMap s = src.Map(), d = method == (int)EMethods.METHOD_DOWNSAMPLE ? half.Map() : refPic.Map();
            Mutate(m, ref s, ref d);
            hash.AddI32((int)pVp->Process(method, &s, &d));
            src.HashInto(hash);
            refPic.HashInto(hash);
            half.HashInto(hash);
            o.FrameSad = res.iFrameSad;
            hash.AddI32(res.pCurY == src.Pl[0].P ? 1 : 0);
            hash.AddI32(res.pRefY == refPic.Pl[0].P ? 1 : 0);
            o.HashInto(hash);
            hash.AddI32((int)WelsVP.WelsDestroyVpInterface(pVp, WelsVP.WELSVP_INTERFACE_VERION));
        }
    }

    private static void Dispatch(FixtureHasher hash)
    {
        var r = new FixtureRng(1600);
        var s = new FixtureYuvSize(48, 32, 64, 32);
        MakeYuvPair(ref r, s, 1, out FixtureYuv src, out FixtureYuv refPic);
        using (src)
        using (refPic)
        {
            using var o = new vaacalc_unittest.Outputs(ref r, s.W, s.H);
            SVAACalcResult res = o.Result();
            CVpFrameWork* pVp = null;
            hash.AddI32((int)WelsVP.WelsCreateVpInterface(&pVp, 0));
            hash.AddI32((int)WelsVP.WelsCreateVpInterface(&pVp, WelsVP.WELSVP_INTERFACE_VERION));
            int[] types = [0, 1, 2, 3, 5, 6, 7, 8, 9, 0x102, 0x7f06];
            foreach (int t in types)
            {
                hash.AddI32((int)pVp->Init(t, null));
                hash.AddI32((int)pVp->Uninit(t));
                hash.AddI32((int)pVp->Flush(t));
                hash.AddI32((int)pVp->SpecialFeature(t, null, null));
                hash.AddI32((int)pVp->Get(t, null));
                hash.AddI32((int)pVp->Set(t, null));
            }
            using var scratch = new NativeBuffer(16 * 4);
            EMethods[] noState =
            [
                EMethods.METHOD_COLORSPACE_CONVERT, EMethods.METHOD_DENOISE, EMethods.METHOD_DOWNSAMPLE,
                EMethods.METHOD_VAA_STATISTICS, EMethods.METHOD_BACKGROUND_DETECTION,
            ];
            foreach (EMethods t in noState)
            {
                r.Fill(scratch.Span);
                hash.AddI32((int)pVp->Get((int)t, scratch.Ptr));
                hash.Add(scratch.Span);
            }
            foreach (EMethods t in new[] { EMethods.METHOD_COLORSPACE_CONVERT, EMethods.METHOD_DENOISE, EMethods.METHOD_DOWNSAMPLE })
            {
                hash.AddI32((int)pVp->Set((int)t, scratch.Ptr));
            }

            SPixMap ms = src.Map(), mr = refPic.Map();
            hash.AddI32((int)pVp->Process((int)EMethods.METHOD_COLORSPACE_CONVERT, &ms, &mr));
            hash.AddI32((int)pVp->Process((int)EMethods.METHOD_DENOISE, &ms, null));
            hash.AddI32((int)pVp->Process((int)EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO, &ms, &mr));
            SSceneChangeResult scr = default;
            hash.AddI32((int)pVp->Get((int)EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO, &scr));
            HashScdResult(hash, scr, null);

            SVAACalcParam vp = default;
            vp.iCalcVar = vp.iCalcBgd = vp.iCalcSsd = 1;
            vp.pCalcResult = &res;
            hash.AddI32((int)pVp->Set((int)EMethods.METHOD_VAA_STATISTICS, &vp));
            hash.AddI32((int)pVp->Process((int)EMethods.METHOD_VAA_STATISTICS, &ms, &mr));

            int mbs = MbCount(s.W, s.H);
            using var flags = new NativeBuffer(mbs + 2);
            r.Fill(flags.Span);
            SBGDInterface bi = default;
            bi.pBackgroundMbFlag = (sbyte*)flags.Ptr;
            bi.pCalcRes = &res;
            hash.AddI32((int)pVp->Set((int)EMethods.METHOD_BACKGROUND_DETECTION, &bi));
            hash.AddI32((int)pVp->Process((int)EMethods.METHOD_BACKGROUND_DETECTION, &ms, &mr));

            using var mt = new NativeBuffer((mbs + 2) * sizeof(SMotionTextureUnit));
            r.Fill(mt.Span);
            using var dqp = new NativeBuffer(mbs + 2);
            r.Fill(dqp.Span);
            SAdaptiveQuantizationParam aqp = default;
            aqp.iAdaptiveQuantMode = (int)EAQModes.AQ_BITRATE_MODE;
            aqp.pCalcResult = &res;
            aqp.pMotionTextureUnit = (SMotionTextureUnit*)mt.Ptr;
            aqp.pMotionTextureIndexToDeltaQp = (sbyte*)dqp.Ptr;
            hash.AddI32((int)pVp->Set((int)EMethods.METHOD_ADAPTIVE_QUANT, &aqp));
            hash.AddI32((int)pVp->Process((int)EMethods.METHOD_ADAPTIVE_QUANT, &ms, &mr));
            SAdaptiveQuantizationParam aqo = default;
            hash.AddI32((int)pVp->Get((int)EMethods.METHOD_ADAPTIVE_QUANT, &aqo));
            hash.AddI32(aqo.iAverMotionTextureIndexToDeltaQp);

            using var gomComplexity = new NativeBuffer(4 * 4);
            using var gomForeground = new NativeBuffer(4 * 4);
            using var refMbType = new NativeBuffer((mbs + 2) * 4);
            r.Fill(gomComplexity.Span);
            uint* types32 = (uint*)refMbType.Ptr;
            for (int i = 0; i < mbs + 2; i++)
            {
                types32[i] = RefMbTypes[r.Next() % 10];
            }
            SComplexityAnalysisParam cp = default;
            cp.iComplexityAnalysisMode = (int)EComplexityAnalysisMode.GOM_SAD;
            cp.iCalcBgd = 1;
            cp.iMbNumInGom = 3;
            cp.pGomComplexity = (int*)gomComplexity.Ptr;
            cp.pGomForegroundBlockNum = (int*)gomForeground.Ptr;
            cp.pBackgroundMbFlag = (sbyte*)flags.Ptr;
            cp.uiRefMbType = types32;
            cp.pCalcResult = &res;
            hash.AddI32((int)pVp->Set((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &cp));
            hash.AddI32((int)pVp->Process((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &ms, &mr));
            SComplexityAnalysisParam co = default;
            hash.AddI32((int)pVp->Get((int)EMethods.METHOD_COMPLEXITY_ANALYSIS, &co));
            hash.AddI64(co.iFrameComplexity);

            src.HashInto(hash);
            refPic.HashInto(hash);
            o.FrameSad = res.iFrameSad;
            o.HashInto(hash);
            hash.Add(flags.Span);
            hash.Add(mt.Span);
            hash.Add(dqp.Span);
            hash.Add(gomComplexity.Span);
            hash.Add(gomForeground.Span);
            hash.AddI32((int)WelsVP.WelsDestroyVpInterface(pVp, WelsVP.WELSVP_INTERFACE_VERION));
        }
    }
}
