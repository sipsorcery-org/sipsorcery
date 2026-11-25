//-----------------------------------------------------------------------------
// Filename: HeaderDump.cs
//
// Description: The C# side of the D1 header-dump oracle. A line-for-line
// mirror of thirdparty/openh264-dotnet-port/refharness/src/headers.cpp: it
// drives the ported NAL and access-unit parsers the way WelsDecodeBs does
// (without reconstruction) and writes the same text dump, which the tests
// compare with the native build's (reference/headers.tsv). Keep the two in
// step: same fields, same order, same names.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SIPSorcery.H264.Decoder;
using static SIPSorcery.H264.DECODING_STATE;
using static SIPSorcery.H264.Decoder.WelsDec;
using static SIPSorcery.H264.EWelsNalUnitType;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.TestVectors;

internal static unsafe class HeaderDump
{
    /// <summary>BaseDecoderTest::ReadFrame's split of a stream into DecodeFrame2 inputs (SplitChunks in headers.cpp).</summary>
    public static List<byte[]> SplitChunks(byte[] stream)
    {
        var chunks = new List<byte[]>();
        int pos = 0, n = stream.Length;
        var buf = new List<byte>();
        for (; ; )
        {
            buf.Clear();
            int zeroCount = 0;
            while (pos < n)
            {
                byte b = stream[pos++];
                buf.Add(b);
                if (buf.Count <= 4)
                {
                    continue;
                }
                if (zeroCount < 3)
                {
                    zeroCount = b != 0 ? 0 : zeroCount + 1;
                }
                else if (b == 1)
                {
                    pos -= 4;
                    buf.RemoveRange(buf.Count - 4, 4);
                    break;
                }
                else if (b == 0)
                {
                    zeroCount = 3;
                }
                else
                {
                    zeroCount = 0;
                }
            }
            if (buf.Count == 0)
            {
                break;
            }
            chunks.Add(buf.ToArray());
        }
        return chunks;
    }

    /// <summary>fixture.h's Rng (xorshift32 with a mixed seed).</summary>
    private struct Rng
    {
        private uint _s;

        public Rng(uint seed)
        {
            _s = seed ^ 0xA5A5A5A5u;
            if (_s == 0)
            {
                _s = 1;
            }
        }

        public uint Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return _s;
        }
    }

    /// <summary>
    /// A damaged copy of the chunks, as DamageChunks in headers.cpp: of every 16
    /// chunks one is dropped, one is cut short and three get one to three bit flips
    /// past the start code.
    /// </summary>
    public static List<byte[]> DamageChunks(List<byte[]> chunks, uint streamSize, int variant)
    {
        var r = new Rng(unchecked(streamSize * 2654435761u + (uint)variant));
        var output = new List<byte[]>();
        foreach (byte[] c in chunks)
        {
            uint x = r.Next() % 16;
            if (x == 0)
            {
                continue;
            }
            byte[] d = (byte[])c.Clone();
            if (x == 1)
            {
                if (d.Length > 5)
                {
                    Array.Resize(ref d, 5 + (int)(r.Next() % (uint)(d.Length - 5)));
                }
            }
            else if (x <= 4)
            {
                uint flips = 1 + r.Next() % 3;
                for (uint f = 0; f < flips; ++f)
                {
                    if (d.Length <= 4)
                    {
                        break;
                    }
                    uint pos = 4 + r.Next() % (uint)(d.Length - 4);
                    d[pos] ^= (byte)(1u << (int)(r.Next() % 8));
                }
            }
            output.Add(d);
        }
        return output;
    }

    /// <summary>Runs the header-parsing driver over a whole stream, as HeaderDump in headers.cpp.</summary>
    public static bool Run(byte[] stream, out string dump, out int nals, out int aus) =>
        RunChunks(SplitChunks(stream), out dump, out nals, out aus);

    /// <summary>Runs the header-parsing driver over the chunks, as HeaderDumpChunks in headers.cpp.</summary>
    public static bool RunChunks(List<byte[]> chunks, out string dump, out int nals, out int aus)
    {
        var drv = new Driver();
        try
        {
            if (!drv.Init())
            {
                dump = null;
                nals = aus = 0;
                return false;
            }
            int i = 0;
            foreach (byte[] c in chunks)
            {
                drv.Chunk(c, i++);
            }
            drv.Flush();
            dump = drv.d.ToString();
            nals = drv.nals;
            aus = drv.aus;
            return true;
        }
        finally
        {
            drv.Close();
        }
    }

    public static string Sha1Hex(string s)
    {
        byte[] h = SHA1.HashData(Encoding.ASCII.GetBytes(s));
        return Convert.ToHexString(h).ToLowerInvariant();
    }

    private sealed class Dump
    {
        private readonly StringBuilder s = new StringBuilder(1 << 16);

        public void Tag(string t) => s.Append(t);

        public void Raw(string t) => s.Append(t);

        public void Raw(char c) => s.Append(c);

        public void Num(long v) => s.Append(v.ToString(CultureInfo.InvariantCulture));

        public void Kv(string k, long v)
        {
            s.Append(' ').Append(k).Append('=');
            Num(v);
        }

        public void Kv(string k, bool v) => Kv(k, v ? 1L : 0L);

        public void Hex(string k, byte* p, int n)
        {
            const string kH = "0123456789abcdef";
            s.Append(' ').Append(k).Append('=');
            for (int i = 0; i < n; ++i)
            {
                s.Append(kH[p[i] >> 4]).Append(kH[p[i] & 15]);
            }
        }

        public void Bits(string k, bool* p, int n)
        {
            s.Append(' ').Append(k).Append('=');
            for (int i = 0; i < n; ++i)
            {
                s.Append(p[i] ? '1' : '0');
            }
        }

        public void Seq(string k, sbyte* p, int n)
        {
            s.Append(' ').Append(k).Append('=');
            for (int i = 0; i < n; ++i)
            {
                if (i != 0)
                {
                    s.Append(',');
                }
                Num(p[i]);
            }
        }

        public void Seq(string k, uint* p, int n)
        {
            s.Append(' ').Append(k).Append('=');
            for (int i = 0; i < n; ++i)
            {
                if (i != 0)
                {
                    s.Append(',');
                }
                Num(p[i]);
            }
        }

        public void End() => s.Append('\n');

        public override string ToString() => s.ToString();
    }

    private static long SpsIdx(SWelsDecoderContext* c, SSps* p)
    {
        if (p == null)
        {
            return -1;
        }
        byte* b = (byte*)p;
        byte* s0 = (byte*)&c->sSpsPpsCtx.sSpsBuffer;
        if (b >= s0 && b < s0 + sizeof(InlineArray33<SSps>))
        {
            return (b - s0) / sizeof(SSps);
        }
        byte* u0 = (byte*)&c->sSpsPpsCtx.sSubsetSpsBuffer;
        if (b >= u0 && b < u0 + sizeof(InlineArray33<SSubsetSps>))
        {
            return 100 + (b - u0) / sizeof(SSubsetSps);
        }
        return -2;
    }

    private static long SubsetIdx(SWelsDecoderContext* c, SSubsetSps* p)
    {
        if (p == null)
        {
            return -1;
        }
        byte* b = (byte*)p;
        byte* u0 = (byte*)&c->sSpsPpsCtx.sSubsetSpsBuffer;
        if (b >= u0 && b < u0 + sizeof(InlineArray33<SSubsetSps>))
        {
            return (b - u0) / sizeof(SSubsetSps);
        }
        return -2;
    }

    private static long PpsIdx(SWelsDecoderContext* c, SPps* p)
    {
        if (p == null)
        {
            return -1;
        }
        byte* b = (byte*)p;
        byte* p0 = (byte*)&c->sSpsPpsCtx.sPpsBuffer;
        if (b >= p0 && b < p0 + sizeof(InlineArray257<SPps>))
        {
            return (b - p0) / sizeof(SPps);
        }
        return -2;
    }

    private static long LevelIdx(SLevelLimits* p) => p == null ? -1 : p - g_ksLevelLimits;

    private static void DumpSps(Dump d, string tag, int i, int avail, SSps* p)
    {
        d.Tag("P ");
        d.Tag(tag);
        d.Kv("i", i);
        d.Kv("a", avail);
        d.Kv("id", p->iSpsId);
        d.Kv("mbw", p->iMbWidth);
        d.Kv("mbh", p->iMbHeight);
        d.Kv("tot", p->uiTotalMbCount);
        d.Kv("l2fn", p->uiLog2MaxFrameNum);
        d.Kv("poct", p->uiPocType);
        d.Kv("l2poc", p->iLog2MaxPocLsb);
        d.Kv("onr", p->iOffsetForNonRefPic);
        d.Kv("otb", p->iOffsetForTopToBottomField);
        d.Kv("ncyc", p->iNumRefFramesInPocCycle);
        d.Seq("ofr", p->iOffsetForRefFrame, p->iNumRefFramesInPocCycle < 0 ? 0 : (p->iNumRefFramesInPocCycle > 256 ? 256 : p->iNumRefFramesInPocCycle));
        d.Kv("nref", p->iNumRefFrames);
        d.Kv("crl", p->sFrameCrop.iLeftOffset);
        d.Kv("crt", p->sFrameCrop.iTopOffset);
        d.Kv("crr", p->sFrameCrop.iRightOffset);
        d.Kv("crb", p->sFrameCrop.iBottomOffset);
        d.Kv("prof", (long)p->uiProfileIdc);
        d.Kv("lvl", p->uiLevelIdc);
        d.Kv("cfi", p->uiChromaFormatIdc);
        d.Kv("cat", p->uiChromaArrayType);
        d.Kv("bdl", p->uiBitDepthLuma);
        d.Kv("bdc", p->uiBitDepthChroma);
        d.Kv("dpoaz", p->bDeltaPicOrderAlwaysZeroFlag);
        d.Kv("gaps", p->bGapsInFrameNumValueAllowedFlag);
        d.Kv("fmo", p->bFrameMbsOnlyFlag);
        d.Kv("mbaff", p->bMbaffFlag);
        d.Kv("d8", p->bDirect8x8InferenceFlag);
        d.Kv("crop", p->bFrameCroppingFlag);
        d.Kv("vuip", p->bVuiParamPresentFlag);
        d.Kv("cs0", p->bConstraintSet0Flag);
        d.Kv("cs1", p->bConstraintSet1Flag);
        d.Kv("cs2", p->bConstraintSet2Flag);
        d.Kv("cs3", p->bConstraintSet3Flag);
        d.Kv("sep", p->bSeparateColorPlaneFlag);
        d.Kv("qpb", p->bQpPrimeYZeroTransfBypassFlag);
        d.Kv("ssm", p->bSeqScalingMatrixPresentFlag);
        d.Bits("slp", p->bSeqScalingListPresentFlag, 12);
        d.Hex("sl4", (byte*)&p->iScalingList4x4, 6 * 16);
        d.Hex("sl8", (byte*)&p->iScalingList8x8, 6 * 64);
        SVui* v = &p->sVui;
        d.Kv("arp", v->bAspectRatioInfoPresentFlag);
        d.Kv("ari", v->uiAspectRatioIdc);
        d.Kv("sarw", v->uiSarWidth);
        d.Kv("sarh", v->uiSarHeight);
        d.Kv("ovp", v->bOverscanInfoPresentFlag);
        d.Kv("ova", v->bOverscanAppropriateFlag);
        d.Kv("vst", v->bVideoSignalTypePresentFlag);
        d.Kv("vf", v->uiVideoFormat);
        d.Kv("vfr", v->bVideoFullRangeFlag);
        d.Kv("cdp", v->bColourDescripPresentFlag);
        d.Kv("cp", v->uiColourPrimaries);
        d.Kv("tc", v->uiTransferCharacteristics);
        d.Kv("mc", v->uiMatrixCoeffs);
        d.Kv("clp", v->bChromaLocInfoPresentFlag);
        d.Kv("clt", v->uiChromaSampleLocTypeTopField);
        d.Kv("clb", v->uiChromaSampleLocTypeBottomField);
        d.Kv("tip", v->bTimingInfoPresentFlag);
        d.Kv("nuit", v->uiNumUnitsInTick);
        d.Kv("ts", v->uiTimeScale);
        d.Kv("ffr", v->bFixedFrameRateFlag);
        d.Kv("nhrd", v->bNalHrdParamPresentFlag);
        d.Kv("vhrd", v->bVclHrdParamPresentFlag);
        d.Kv("psp", v->bPicStructPresentFlag);
        d.Kv("bsr", v->bBitstreamRestrictionFlag);
        d.Kv("mvpb", v->bMotionVectorsOverPicBoundariesFlag);
        d.Kv("mbpp", v->uiMaxBytesPerPicDenom);
        d.Kv("mbpm", v->uiMaxBitsPerMbDenom);
        d.Kv("mvh", v->uiLog2MaxMvLengthHorizontal);
        d.Kv("mvv", v->uiLog2MaxMvLengthVertical);
        d.Kv("mnrf", v->uiMaxNumReorderFrames);
        d.Kv("mdfb", v->uiMaxDecFrameBuffering);
        d.Kv("lim", LevelIdx(p->pSLevelLimits));
        d.End();
    }

    private static void DumpSubsetExt(Dump d, int i, SSubsetSps* p)
    {
        SSpsSvcExt* e = &p->sSpsSvcExt;
        d.Tag("P subx");
        d.Kv("i", i);
        d.Kv("sl", e->sSeqScaledRefLayer.iLeftOffset);
        d.Kv("st", e->sSeqScaledRefLayer.iTopOffset);
        d.Kv("sr", e->sSeqScaledRefLayer.iRightOffset);
        d.Kv("sb", e->sSeqScaledRefLayer.iBottomOffset);
        d.Kv("ess", e->uiExtendedSpatialScalability);
        d.Kv("phx", e->uiChromaPhaseXPlus1Flag);
        d.Kv("phy", e->uiChromaPhaseYPlus1);
        d.Kv("rphx", e->uiSeqRefLayerChromaPhaseXPlus1Flag);
        d.Kv("rphy", e->uiSeqRefLayerChromaPhaseYPlus1);
        d.Kv("ildf", e->bInterLayerDeblockingFilterCtrlPresentFlag);
        d.Kv("tclp", e->bSeqTCoeffLevelPredFlag);
        d.Kv("atclp", e->bAdaptiveTCoeffLevelPredFlag);
        d.Kv("shr", e->bSliceHeaderRestrictionFlag);
        d.Kv("svui", p->bSvcVuiParamPresentFlag);
        d.Kv("e2", p->bAdditionalExtension2Flag);
        d.Kv("e2d", p->bAdditionalExtension2DataFlag);
        d.End();
    }

    private static void DumpPps(Dump d, int i, int avail, SPps* p)
    {
        d.Tag("P pps");
        d.Kv("i", i);
        d.Kv("a", avail);
        d.Kv("id", p->iPpsId);
        d.Kv("sps", p->iSpsId);
        d.Kv("nsg", p->uiNumSliceGroups);
        d.Kv("sgmt", p->uiSliceGroupMapType);
        d.Seq("rl", p->uiRunLength, MAX_SLICEGROUP_IDS);
        d.Seq("tl", p->uiTopLeft, MAX_SLICEGROUP_IDS);
        d.Seq("br", p->uiBottomRight, MAX_SLICEGROUP_IDS);
        d.Kv("sgcr", p->uiSliceGroupChangeRate);
        d.Kv("psmu", p->uiPicSizeInMapUnits);
        d.Seq("sgid", p->uiSliceGroupId, MAX_SLICEGROUP_IDS);
        d.Kv("nr0", p->uiNumRefIdxL0Active);
        d.Kv("nr1", p->uiNumRefIdxL1Active);
        d.Kv("qp", p->iPicInitQp);
        d.Kv("qs", p->iPicInitQs);
        d.Kv("cq0", p->iChromaQpIndexOffset[0]);
        d.Kv("cq1", p->iChromaQpIndexOffset[1]);
        d.Kv("cabac", p->bEntropyCodingModeFlag);
        d.Kv("pop", p->bPicOrderPresentFlag);
        d.Kv("sgcd", p->bSliceGroupChangeDirectionFlag);
        d.Kv("dfc", p->bDeblockingFilterControlPresentFlag);
        d.Kv("cip", p->bConstainedIntraPredFlag);
        d.Kv("rpc", p->bRedundantPicCntPresentFlag);
        d.Kv("wp", p->bWeightedPredFlag);
        d.Kv("wbi", p->uiWeightedBipredIdc);
        d.Kv("t8", p->bTransform8x8ModeFlag);
        d.Kv("psm", p->bPicScalingMatrixPresentFlag);
        d.Bits("plp", p->bPicScalingListPresentFlag, 12);
        d.Hex("sl4", (byte*)&p->iScalingList4x4, 6 * 16);
        d.Hex("sl8", (byte*)&p->iScalingList8x8, 6 * 64);
        d.Kv("cq2", p->iSecondChromaQPIndexOffset);
        d.End();
    }

    private static void DumpHeaderExt(Dump d, string tag, SNalUnitHeaderExt* h)
    {
        d.Tag(tag);
        d.Kv("type", (long)h->sNalUnitHeader.eNalUnitType);
        d.Kv("nri", h->sNalUnitHeader.uiNalRefIdc);
        d.Kv("idr", h->bIdrFlag);
        d.Kv("prio", h->uiPriorityId);
        d.Kv("nilp", h->iNoInterLayerPredFlag);
        d.Kv("did", h->uiDependencyId);
        d.Kv("qid", h->uiQualityId);
        d.Kv("tid", h->uiTemporalId);
        d.Kv("urb", h->bUseRefBasePicFlag);
        d.Kv("disc", h->bDiscardableFlag);
        d.Kv("out", h->bOutputFlag);
        d.Kv("r3", h->uiReservedThree2Bits);
        d.Kv("dq", h->uiLayerDqId);
        d.End();
    }

    private static void DumpBaseMarking(Dump d, SRefBasePicMarking* m)
    {
        d.Tag("Q");
        d.Kv("adapt", m->bAdaptiveRefBasePicMarkingModeFlag);
        if (m->bAdaptiveRefBasePicMarkingModeFlag)
        {
            d.Raw(" m=");
            for (int i = 0; i < MAX_MMCO_COUNT; ++i)
            {
                SMmcoBaseEntry* e = &m->mmco_base[i];
                if (i != 0)
                {
                    d.Raw(',');
                }
                d.Num(e->uiMmcoType); d.Raw('/');
                d.Num(e->iShortFrameNum); d.Raw('/');
                d.Num(e->uiDiffOfPicNums); d.Raw('/');
                d.Num(e->uiLongTermPicNum);
                if (e->uiMmcoType == (uint)EMmcoCode.MMCO_END)
                {
                    break;
                }
            }
        }
        d.End();
    }

    private static void DumpVcl(Dump d, SWelsDecoderContext* c, SNalUnit* n)
    {
        DumpHeaderExt(d, "H", &n->sNalHeaderExt);
        SVclNal* v = &n->sNalData.sVclNal;
        SSliceHeaderExt* x = &v->sSliceHeaderExt;
        SSliceHeader* s = &x->sSliceHeader;
        d.Tag("S");
        d.Kv("first", s->iFirstMbInSlice);
        d.Kv("st", (long)s->eSliceType);
        d.Kv("spsi", SpsIdx(c, s->pSps));
        d.Kv("ppsi", PpsIdx(c, s->pPps));
        d.Kv("spsid", s->iSpsId);
        d.Kv("ppsid", s->iPpsId);
        d.Kv("idr", s->bIdrFlag);
        d.Kv("fn", s->iFrameNum);
        d.Kv("poc", s->iPicOrderCntLsb);
        d.Kv("dpb", s->iDeltaPicOrderCntBottom);
        d.Kv("dp0", s->iDeltaPicOrderCnt[0]);
        d.Kv("dp1", s->iDeltaPicOrderCnt[1]);
        d.Kv("rpc", s->iRedundantPicCnt);
        d.Kv("dsmv", s->iDirectSpatialMvPredFlag);
        d.Kv("rc0", s->uiRefCount[0]);
        d.Kv("rc1", s->uiRefCount[1]);
        d.Kv("ovr", s->bNumRefIdxActiveOverrideFlag);
        d.Kv("qpd", s->iSliceQpDelta);
        d.Kv("qp", s->iSliceQp);
        d.Kv("qsd", s->iSliceQsDelta);
        d.Kv("dbk", s->uiDisableDeblockingFilterIdc);
        d.Kv("alpha", s->iSliceAlphaC0Offset);
        d.Kv("beta", s->iSliceBetaOffset);
        d.Kv("sgcc", s->iSliceGroupChangeCycle);
        d.Kv("cabac", s->iCabacInitIdc);
        d.Kv("mbw", s->iMbWidth);
        d.Kv("mbh", s->iMbHeight);
        d.Kv("idrid", s->uiIdrPicId);
        d.Kv("fld", s->bFieldPicFlag);
        d.Kv("bot", s->bBottomFiledFlag);
        d.Kv("spsw", s->bSpForSwitchFlag);
        d.End();

        SBitStringAux* b = &v->sSliceBitsRead;
        d.Tag("B");
        d.Kv("raw", b->pStartBuf - c->sRawData.pHead);
        d.Kv("len", b->pEndBuf - b->pStartBuf);
        d.Kv("cur", b->pCurBuf - b->pStartBuf);
        d.Kv("bits", b->iBits);
        d.Kv("left", b->iLeftBits);
        d.Kv("cb", b->uiCurBits);
        d.Kv("idx", (long)b->iIndex);
        d.Kv("ext", v->bSliceHeaderExtFlag);
        d.End();

        SRefPicListReorderSyn* r = &s->pRefPicListReordering;
        for (int l = 0; l < (int)EListIndex.LIST_A; ++l)
        {
            d.Tag("L");
            d.Kv("l", l);
            d.Kv("f", r->bRefPicListReorderingFlag[l]);
            if (r->bRefPicListReorderingFlag[l])
            {
                d.Raw(" e=");
                for (int i = 0; i < MAX_REF_PIC_COUNT + 1; ++i)
                {
                    SReorderingSynEntry* e = &r->sReorderingSyn[l][i];
                    if (i != 0)
                    {
                        d.Raw(',');
                    }
                    d.Num(e->uiReorderingOfPicNumsIdc); d.Raw('/');
                    d.Num(e->uiAbsDiffPicNumMinus1); d.Raw('/');
                    d.Num(e->uiLongTermPicNum);
                    if (e->uiReorderingOfPicNumsIdc == 3)
                    {
                        break;
                    }
                }
            }
            d.End();
        }

        if (s->pPps != null && (s->pPps->bWeightedPredFlag || s->pPps->uiWeightedBipredIdc == 1))
        {
            SPredWeightTabSyn* w = &s->sPredWeightTable;
            d.Tag("W");
            d.Kv("ld", w->uiLumaLog2WeightDenom);
            d.Kv("cd", w->uiChromaLog2WeightDenom);
            for (int l = 0; l < (int)EListIndex.LIST_A; ++l)
            {
                d.Raw(l == 0 ? " w0=" : " w1=");
                SPredListEntry* pl = &w->sPredList[l];
                for (int i = 0; i < MAX_REF_PIC_COUNT; ++i)
                {
                    if (i != 0)
                    {
                        d.Raw(',');
                    }
                    d.Num(pl->iLumaWeight[i]); d.Raw('/');
                    d.Num(pl->iLumaOffset[i]); d.Raw('/');
                    d.Num(pl->iChromaWeight[i][0]); d.Raw('/');
                    d.Num(pl->iChromaOffset[i][0]); d.Raw('/');
                    d.Num(pl->iChromaWeight[i][1]); d.Raw('/');
                    d.Num(pl->iChromaOffset[i][1]);
                }
            }
            d.End();
        }

        SRefPicMarking* m = &s->sRefMarking;
        d.Tag("M");
        d.Kv("noout", m->bNoOutputOfPriorPicsFlag);
        d.Kv("lt", m->bLongTermRefFlag);
        d.Kv("adapt", m->bAdaptiveRefPicMarkingModeFlag);
        if (m->bAdaptiveRefPicMarkingModeFlag)
        {
            d.Raw(" m=");
            for (int i = 0; i < MAX_MMCO_COUNT; ++i)
            {
                SMmcoRefEntry* e = &m->sMmcoRef[i];
                if (i != 0)
                {
                    d.Raw(',');
                }
                d.Num(e->uiMmcoType); d.Raw('/');
                d.Num(e->iShortFrameNum); d.Raw('/');
                d.Num(e->iDiffOfPicNum); d.Raw('/');
                d.Num(e->uiLongTermPicNum); d.Raw('/');
                d.Num(e->iLongTermFrameIdx); d.Raw('/');
                d.Num(e->iMaxLongTermFrameIdx);
                if (e->uiMmcoType == (uint)EMmcoCode.MMCO_END)
                {
                    break;
                }
            }
        }
        d.End();

        d.Tag("X");
        d.Kv("sub", SubsetIdx(c, x->pSubsetSps));
        d.Kv("ildf", x->uiDisableInterLayerDeblockingFilterIdc);
        d.Kv("ila", x->iInterLayerSliceAlphaC0Offset);
        d.Kv("ilb", x->iInterLayerSliceBetaOffset);
        d.Kv("sw", x->iScaledRefLayerPicWidthInSampleLuma);
        d.Kv("sh", x->iScaledRefLayerPicHeightInSampleLuma);
        d.Kv("bpwt", x->bBasePredWeightTableFlag);
        d.Kv("srb", x->bStoreRefBasePicFlag);
        d.Kv("cir", x->bConstrainedIntraResamplingFlag);
        d.Kv("skip", x->bSliceSkipFlag);
        d.Kv("abm", x->bAdaptiveBaseModeFlag);
        d.Kv("dbm", x->bDefaultBaseModeFlag);
        d.Kv("amp", x->bAdaptiveMotionPredFlag);
        d.Kv("dmp", x->bDefaultMotionPredFlag);
        d.Kv("arp", x->bAdaptiveResidualPredFlag);
        d.Kv("drp", x->bDefaultResidualPredFlag);
        d.Kv("tclp", x->bTCoeffLevelPredFlag);
        d.Kv("phx", x->uiRefLayerChromaPhaseXPlus1Flag);
        d.Kv("phy", x->uiRefLayerChromaPhaseYPlus1);
        d.Kv("rdq", x->uiRefLayerDqId);
        d.Kv("s0", x->uiScanIdxStart);
        d.Kv("s1", x->uiScanIdxEnd);
        d.End();
        DumpBaseMarking(d, &x->sRefBasePicMarking);
    }

    private static void DumpPrefix(Dump d, SWelsDecoderContext* c)
    {
        SNalUnit* n = &c->sSpsPpsCtx.sPrefixNal;
        DumpHeaderExt(d, "H", &n->sNalHeaderExt);
        SPrefixNalUnit* p = &n->sNalData.sPrefixNal;
        d.Tag("F");
        d.Kv("srb", p->bStoreRefBasePicFlag);
        d.Kv("aext", p->bPrefixNalUnitAdditionalExtFlag);
        d.Kv("ext", p->bPrefixNalUnitExtFlag);
        d.Kv("ok", p->bPrefixNalCorrectFlag);
        d.End();
        DumpBaseMarking(d, &p->sRefPicBaseMarking);
    }

    /// <summary>
    /// Snapshot of the parameter set slots, to dump only the ones that changed.
    /// headers.cpp re-copies the whole snapshot after every diff; copying just
    /// the slots that changed leaves the same snapshot.
    /// </summary>
    private sealed class Snap
    {
        public readonly byte[] sps = new byte[sizeof(InlineArray33<SSps>)];
        public readonly byte[] sub = new byte[sizeof(InlineArray33<SSubsetSps>)];
        public readonly byte[] pps = new byte[sizeof(InlineArray257<SPps>)];
        public readonly bool[] spsA = new bool[MAX_SPS_COUNT];
        public readonly bool[] subA = new bool[MAX_SPS_COUNT];
        public readonly bool[] ppsA = new bool[MAX_PPS_COUNT];
    }

    private static void TakeSnap(SWelsDecoderContext* c, Snap s)
    {
        SWelsDecoderSpsPpsCTX* x = &c->sSpsPpsCtx;
        new ReadOnlySpan<byte>(&x->sSpsBuffer, s.sps.Length).CopyTo(s.sps);
        new ReadOnlySpan<byte>(&x->sSubsetSpsBuffer, s.sub.Length).CopyTo(s.sub);
        new ReadOnlySpan<byte>(&x->sPpsBuffer, s.pps.Length).CopyTo(s.pps);
        for (int i = 0; i < MAX_SPS_COUNT; ++i)
        {
            s.spsA[i] = x->bSpsAvailFlags[i];
            s.subA[i] = x->bSubspsAvailFlags[i];
        }
        for (int i = 0; i < MAX_PPS_COUNT; ++i)
        {
            s.ppsA[i] = x->bPpsAvailFlags[i];
        }
    }

    private static bool SlotChanged(void* cur, byte[] snap, int index, int size)
    {
        var now = new ReadOnlySpan<byte>(cur, size);
        var was = new Span<byte>(snap, index * size, size);
        if (now.SequenceEqual(was))
        {
            return false;
        }
        now.CopyTo(was);
        return true;
    }

    private static void DiffSnap(Dump d, SWelsDecoderContext* c, Snap s)
    {
        SWelsDecoderSpsPpsCTX* x = &c->sSpsPpsCtx;
        for (int i = 0; i <= MAX_SPS_COUNT; ++i)
        {
            bool a = i < MAX_SPS_COUNT && x->bSpsAvailFlags[i];
            bool changed = SlotChanged(&x->sSpsBuffer[i], s.sps, i, sizeof(SSps));
            if (i < MAX_SPS_COUNT && a != s.spsA[i])
            {
                s.spsA[i] = a;
                changed = true;
            }
            if (changed)
            {
                DumpSps(d, "sps", i, i < MAX_SPS_COUNT ? (a ? 1 : 0) : -1, &x->sSpsBuffer[i]);
            }
        }
        for (int i = 0; i <= MAX_SPS_COUNT; ++i)
        {
            bool a = i < MAX_SPS_COUNT && x->bSubspsAvailFlags[i];
            bool changed = SlotChanged(&x->sSubsetSpsBuffer[i], s.sub, i, sizeof(SSubsetSps));
            if (i < MAX_SPS_COUNT && a != s.subA[i])
            {
                s.subA[i] = a;
                changed = true;
            }
            if (changed)
            {
                DumpSps(d, "sub", i, i < MAX_SPS_COUNT ? (a ? 1 : 0) : -1, &x->sSubsetSpsBuffer[i].sSps);
                DumpSubsetExt(d, i, &x->sSubsetSpsBuffer[i]);
            }
        }
        for (int i = 0; i <= MAX_PPS_COUNT; ++i)
        {
            bool a = i < MAX_PPS_COUNT && x->bPpsAvailFlags[i];
            bool changed = SlotChanged(&x->sPpsBuffer[i], s.pps, i, sizeof(SPps));
            if (i < MAX_PPS_COUNT && a != s.ppsA[i])
            {
                s.ppsA[i] = a;
                changed = true;
            }
            if (changed)
            {
                DumpPps(d, i, i < MAX_PPS_COUNT ? (a ? 1 : 0) : -1, &x->sPpsBuffer[i]);
            }
        }
    }

    private sealed class Driver
    {
        public SWelsDecoderContext* ctx;
        public SDecoderStatistics* stats;
        public SWelsLastDecPicInfo* last;
        public readonly Snap snap = new Snap();
        public readonly Dump d = new Dump();
        public int nals, aus;

        public bool Init()
        {
            ctx = (SWelsDecoderContext*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsDecoderContext));
            ctx->pMemAlign = WELS_NEW_CMemoryAlign(16);
            stats = (SDecoderStatistics*)NativeMemory.AllocZeroed((nuint)sizeof(SDecoderStatistics));
            last = (SWelsLastDecPicInfo*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsLastDecPicInfo));
            WelsDecoderLastDecPicInfoDefaults(last);
            ctx->pLastDecPicInfo = last;
            ctx->pDecoderStatistics = stats;
            // sLogCtx.pfLog stays null: WelsLog is then a no-op, as QuietLog makes it natively.
            WelsDecoderSpsPpsDefaults(&ctx->sSpsPpsCtx);
            ctx->pParam = (SDecodingParam*)ctx->pMemAlign->WelsMallocz((uint)sizeof(SDecodingParam), "SDecodingParam");
            if (ctx->pParam == null)
            {
                return false;
            }
            ctx->pParam->eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_SLICE_COPY;
            ctx->pParam->bParseOnly = false;
            ctx->pParam->uiTargetDqLayer = byte.MaxValue;
            ctx->eVideoType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_DEFAULT;
            ctx->iFeedbackNalRefIdc = -1;
            if (WelsInitStaticMemory(ctx) != ERR_NONE)
            {
                return false;
            }
            // WelsOpenDecoder's flags (LONG_TERM_REF).
            ctx->bParamSetsLostFlag = true;
            ctx->bNewSeqBegin = true;
            ctx->bPrintFrameErrorTraceFlag = true;
            ctx->iSeqNum = 0;
            TakeSnap(ctx, snap);
            return true;
        }

        public void Close()
        {
            if (ctx != null)
            {
                CMemoryAlign* pMa = ctx->pMemAlign;
                if (ctx->pParam != null)
                {
                    WelsFreeStaticMemory(ctx);
                }
                if (pMa != null)
                {
                    WELS_DELETE_CMemoryAlign(ref pMa);
                }
                NativeMemory.Free(ctx);
                ctx = null;
            }
            if (stats != null)
            {
                NativeMemory.Free(stats);
                stats = null;
            }
            if (last != null)
            {
                NativeMemory.Free(last);
                last = null;
            }
        }

        private void Ctx(string tag)
        {
            SAccessUnit* au = ctx->pAccessUnitList;
            SWelsDecoderSpsPpsCTX* x = &ctx->sSpsPpsCtx;
            d.Tag(tag);
            d.Kv("err", ctx->iErrorCode);
            d.Kv("aurdy", ctx->bAuReadyFlag);
            d.Kv("avail", au->uiAvailUnitsNum);
            d.Kv("actual", au->uiActualUnitsNum);
            d.Kv("start", au->uiStartPos);
            d.Kv("end", au->uiEndPos);
            d.Kv("done", au->bCompletedAuFlag);
            d.Kv("count", au->uiCountUnitsNum);
            d.Kv("newseq", ctx->bNewSeqBegin);
            d.Kv("nextseq", ctx->bNextNewSeqBegin);
            d.Kv("lost", ctx->bParamSetsLostFlag);
            d.Kv("newsps", ctx->bHasNewSps);
            d.Kv("spsah", x->bSpsExistAheadFlag);
            d.Kv("subah", x->bSubspsExistAheadFlag);
            d.Kv("ppsah", x->bPpsExistAheadFlag);
            d.Kv("avc", x->bAvcBasedFlag);
            d.Kv("ow", x->iOverwriteFlags);
            d.Kv("seqid", x->iSeqId);
            d.Kv("ign", x->iSpsErrorIgnored);
            d.Kv("igs", x->iSubSpsErrorIgnored);
            d.Kv("igp", x->iPpsErrorIgnored);
            d.Kv("ppli", x->iPPSLastInvalidId);
            d.Kv("ppin", x->iPPSInvalidNum);
            d.Kv("spli", x->iSPSLastInvalidId);
            d.Kv("spin", x->iSPSInvalidNum);
            d.Kv("ssli", x->iSubSPSLastInvalidId);
            d.Kv("ssin", x->iSubSPSInvalidNum);
            d.Kv("pocm", last->iPrevPicOrderCntMsb);
            d.Kv("pocl", last->iPrevPicOrderCntLsb);
            d.Kv("idrid", ctx->uiCurIdrPicId);
            d.Kv("tdq", ctx->uiTargetDqId);
            d.Kv("seq", ctx->iSeqNum);
            d.Kv("sne", stats->iSpsNoExistNalNum);
            d.Kv("ssne", stats->iSubSpsNoExistNalNum);
            d.Kv("pne", stats->iPpsNoExistNalNum);
            d.Kv("sre", stats->iSpsReportErrorNum);
            d.Kv("ssre", stats->iSubSpsReportErrorNum);
            d.Kv("pre", stats->iPpsReportErrorNum);
            d.Kv("idrl", stats->uiIDRLostNum);
            d.End();
        }

        // The ConstructAccessUnit stand-in.
        private void AccessUnit()
        {
            SBufferInfo bi = default;
            int iErr = WelsDecodeInitAccessUnitStart(ctx, &bi);
            SAccessUnit* au = ctx->pAccessUnitList;
            d.Tag("A");
            d.Kv("n", aus++);
            d.Kv("ret", iErr);
            d.Kv("onel", ctx->bOnlyOneLayerInCurAuFlag);
            d.Kv("fbv", ctx->iFeedbackVclNalInAu);
            d.Kv("fbt", ctx->iFeedbackTidInAu);
            d.Kv("fbn", ctx->iFeedbackNalRefIdc);
            d.Kv("csps", SpsIdx(ctx, ctx->pSps));
            d.Kv("cpps", PpsIdx(ctx, ctx->pPps));
            d.Raw(" act=");
            for (int i = 0; i < MAX_LAYER_NUM; ++i)
            {
                if (i != 0)
                {
                    d.Raw(',');
                }
                d.Num(SpsIdx(ctx, ctx->sSpsPpsCtx.pActiveLayerSps[i].p));
            }
            d.End();
            Ctx("a");
            if (iErr != ERR_NONE)
            {
                return;
            }
            for (uint i = au->uiStartPos; i <= au->uiEndPos; ++i)
            {
                SNalUnit* n = au->pNalUnitsList[i];
                SSliceHeader* s = &n->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader;
                d.Tag("U");
                d.Kv("i", i);
                d.Kv("type", (long)n->sNalHeaderExt.sNalUnitHeader.eNalUnitType);
                d.Kv("did", n->sNalHeaderExt.uiDependencyId);
                d.Kv("first", s->iFirstMbInSlice);
                d.Kv("fn", s->iFrameNum);
                d.Kv("poc", s->iPicOrderCntLsb);
                d.End();
            }
            ctx->bParamSetsLostFlag = false;
            WelsDecodeAccessUnitEnd(ctx);
            Ctx("e");
        }

        // One NAL, from ParseNalHeader to the error handling, as WelsDecodeBs.
        // Returns true when WelsDecodeBs would return here.
        private bool Nal(byte* pDstNal, int iDstIdx, byte* pSrcNal, int iSrcIdx, ref int iRet, bool isLast)
        {
            int iConsumedBytes = 0;
            pDstNal[iDstIdx] = pDstNal[iDstIdx + 1] = pDstNal[iDstIdx + 2] = pDstNal[iDstIdx + 3] = 0;
            if (isLast)
            {
                ctx->sRawData.pCurPos = pDstNal + iDstIdx + 4;
            }
            byte* pNalPayload = ParseNalHeader(ctx, &ctx->sCurNalHead, pDstNal, iDstIdx, pSrcNal - 3, iSrcIdx + 3, &iConsumedBytes);
            EWelsNalUnitType t = ctx->sCurNalHead.eNalUnitType;
            SNalUnit* parsed = null;
            if (pNalPayload != null && (t == NAL_UNIT_CODED_SLICE || t == NAL_UNIT_CODED_SLICE_IDR || t == NAL_UNIT_CODED_SLICE_EXT))
            {
                parsed = ctx->pAccessUnitList->pNalUnitsList[ctx->pAccessUnitList->uiAvailUnitsNum - 1];
            }
            if (pNalPayload != null && IS_PARAM_SETS_NALS(t))
            {
                iRet = ParseNonVclNal(ctx, pNalPayload, iDstIdx - iConsumedBytes, pSrcNal - 3, iSrcIdx + 3);
            }
            d.Tag("N");
            d.Kv("n", nals++);
            d.Kv("type", (long)t);
            d.Kv("nri", ctx->sCurNalHead.uiNalRefIdc);
            d.Kv("fzb", ctx->sCurNalHead.uiForbiddenZeroBit);
            d.Kv("len", iDstIdx);
            d.Kv("src", iSrcIdx + 3);
            d.Kv("used", iConsumedBytes);
            d.Kv("pay", pNalPayload != null ? pNalPayload - pDstNal : -1);
            d.Kv("ret", iRet);
            d.End();
            Ctx("c");
            if (parsed != null)
            {
                DumpVcl(d, ctx, parsed);
            }
            if (pNalPayload != null && t == NAL_UNIT_PREFIX)
            {
                DumpPrefix(d, ctx);
            }
            DiffSnap(d, ctx, snap);
            if (pNalPayload != null && ctx->bAuReadyFlag && ctx->pAccessUnitList->uiAvailUnitsNum != 0)
            {
                AccessUnit();
            }
            DecodeFinishUpdate(ctx);
            DiffSnap(d, ctx, snap);

            if ((((int)dsOutOfMemory | (int)dsNoParamSets) & ctx->iErrorCode) != 0)
            {
                ctx->bParamSetsLostFlag = true;
                if (isLast || ((int)dsOutOfMemory & ctx->iErrorCode) != 0)
                {
                    return true;
                }
            }
            if (iRet != 0)
            {
                iRet = 0;
                if (((int)dsNoParamSets & ctx->iErrorCode) != 0)
                {
                    ctx->bParamSetsLostFlag = true;
                }
                return true;
            }
            if (!isLast && ctx->iErrorCode != ERR_NONE && (ctx->iErrorCode & (int)dsDataErrorConcealed) == 0)
            {
                return true;
            }
            return false;
        }

        private static bool RawDataWrapIsClean(SWelsDecoderContext* pCtx, int iNewBytes)
        {
            SAccessUnit* pAu = pCtx->pAccessUnitList;
            if (pAu == null || pAu->pNalUnitsList == null || pAu->uiAvailUnitsNum == 0)
            {
                return true;
            }
            byte* pHead = pCtx->sRawData.pHead;
            for (uint i = 0; i < pAu->uiAvailUnitsNum; ++i)
            {
                SNalUnit* pNal = pAu->pNalUnitsList[i];
                if (pNal == null)
                {
                    continue;
                }
                byte* pStart = pNal->sNalData.sVclNal.sSliceBitsRead.pStartBuf;
                if (pStart != null && pStart >= pHead && pStart < pHead + iNewBytes)
                {
                    return false;
                }
            }
            return true;
        }

        // WelsDecodeBs, the !bEndOfStreamFlag branch.
        private void DecodeBs(byte* kpBsBuf, int kiBsLen)
        {
            SDataBuffer* pRawData = &ctx->sRawData;
            int iSrcIdx = 0, iSrcConsumed = 0, iDstIdx = 0, iSrcLength, iRet = 0, iOffset = 0;
            byte* pSrcNal;
            byte* pDstNal;

            if (null == DetectStartCodePrefix(kpBsBuf, &iOffset, kiBsLen))
            {
                ctx->iErrorCode |= (int)dsBitstreamError;
                return;
            }
            pSrcNal = kpBsBuf + iOffset;
            iSrcLength = kiBsLen - iOffset;
            if ((kiBsLen + 4) > (pRawData->pEnd - pRawData->pCurPos))
            {
                if (!RawDataWrapIsClean(ctx, kiBsLen + 4))
                {
                    ctx->iErrorCode |= (int)dsOutOfMemory;
                    return;
                }
                pRawData->pCurPos = pRawData->pHead;
            }
            pDstNal = pRawData->pCurPos;
            bool bNalStartBytes = false;
            while (iSrcConsumed < iSrcLength)
            {
                if ((2 + iSrcConsumed < iSrcLength) && (0 == LD16(pSrcNal + iSrcIdx)) && (pSrcNal[2 + iSrcIdx] <= 0x03))
                {
                    if (bNalStartBytes && (pSrcNal[2 + iSrcIdx] != 0x00 && pSrcNal[2 + iSrcIdx] != 0x01))
                    {
                        ctx->iErrorCode |= (int)dsBitstreamError;
                        return;
                    }
                    if (pSrcNal[2 + iSrcIdx] == 0x02)
                    {
                        ctx->iErrorCode |= (int)dsBitstreamError;
                        return;
                    }
                    else if (pSrcNal[2 + iSrcIdx] == 0x00)
                    {
                        pDstNal[iDstIdx++] = pSrcNal[iSrcIdx++];
                        iSrcConsumed++;
                        bNalStartBytes = true;
                    }
                    else if (pSrcNal[2 + iSrcIdx] == 0x03)
                    {
                        if ((3 + iSrcConsumed < iSrcLength) && pSrcNal[3 + iSrcIdx] > 0x03)
                        {
                            /* Just skip */
                        }
                        else
                        {
                            ST16(pDstNal + iDstIdx, 0);
                            iDstIdx += 2;
                        }
                        iSrcIdx += 3;
                        iSrcConsumed += 3;
                    }
                    else
                    { // 0x01
                        bNalStartBytes = false;
                        if (Nal(pDstNal, iDstIdx, pSrcNal, iSrcIdx, ref iRet, false))
                        {
                            return;
                        }
                        pDstNal += (iDstIdx + 4);
                        if ((iSrcLength - iSrcConsumed + 4) > (pRawData->pEnd - pDstNal))
                        {
                            if (!RawDataWrapIsClean(ctx, iSrcLength - iSrcConsumed + 4))
                            {
                                ctx->iErrorCode |= (int)dsOutOfMemory;
                                return;
                            }
                            pDstNal = pRawData->pCurPos = pRawData->pHead;
                        }
                        else
                        {
                            pRawData->pCurPos = pDstNal;
                        }
                        pSrcNal += iSrcIdx + 3;
                        iSrcConsumed += 3;
                        iSrcIdx = 0;
                        iDstIdx = 0;
                    }
                    continue;
                }
                pDstNal[iDstIdx++] = pSrcNal[iSrcIdx++];
                iSrcConsumed++;
            }
            Nal(pDstNal, iDstIdx, pSrcNal, iSrcIdx, ref iRet, true);
        }

        private void ResetPerCall(bool endOfStream)
        {
            ctx->bEndOfStreamFlag = endOfStream;
            ctx->iErrorCode = (int)dsErrorFree;
            ctx->iFeedbackVclNalInAu = (int)FEEDBACK_VCL_NAL_IN_AU.FEEDBACK_UNKNOWN_NAL;
            ctx->bReferenceLostAtT0Flag = false;
            ctx->iFrameNum = -1;
            ctx->iFeedbackTidInAu = -1;
            ctx->iFeedbackNalRefIdc = -1;
            ctx->uiTimeStamp = 0;
        }

        // DecodeFrame2's per-call resets, then WelsDecodeBs.
        public void Chunk(byte[] chunk, int idx)
        {
            d.Tag("C");
            d.Kv("i", idx);
            d.Kv("len", chunk.Length);
            int iChk = CheckBsBuffer(ctx, chunk.Length);
            d.Kv("chk", iChk);
            d.End();
            if (iChk != ERR_NONE)
            {
                return;
            }
            ResetPerCall(false);
            fixed (byte* p = chunk)
            {
                DecodeBs(p, chunk.Length);
            }
            Ctx("r");
        }

        // DecodeFrame2 (NULL, 0): the bEndOfStreamFlag branch of WelsDecodeBs.
        public void Flush()
        {
            d.Tag("C eos");
            d.End();
            ResetPerCall(true);
            if (ctx->pAccessUnitList->uiAvailUnitsNum != 0)
            {
                ctx->pAccessUnitList->uiEndPos = ctx->pAccessUnitList->uiAvailUnitsNum - 1;
                AccessUnit();
                DecodeFinishUpdate(ctx);
                DiffSnap(d, ctx, snap);
                if ((((int)dsOutOfMemory | (int)dsNoParamSets) & ctx->iErrorCode) != 0)
                {
                    ctx->bParamSetsLostFlag = true;
                }
            }
            Ctx("r");
        }
    }
}
