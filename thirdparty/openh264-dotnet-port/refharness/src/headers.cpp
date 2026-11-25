// headers.cpp: the D1 header-dump oracle (h264ref headers / headerdump).
//
// Drives the decoder's NAL and access-unit parsing directly, without
// reconstruction, and writes a text dump of everything the parsers produce:
// each NAL's header and the parser state after it, every VCL slice header
// (with its reordering, weight table, marking and SVC extension), every
// parameter set slot that changed, and each access unit the AU logic builds.
// The C# test (test/SIPSorcery.H264.TestVectors/HeaderDump.cs) runs the same
// driver against the port and must produce the same bytes, so keep the two in
// step: same fields, same order, same names.
//
// The driver is WelsDecodeBs (decoder.cpp) with two changes: the picture
// output path (CheckAndFinishLastPic) is left out, and ConstructAccessUnit is
// replaced by a stand-in that runs WelsDecodeInitAccessUnitStart, dumps the AU,
// clears bParamSetsLostFlag as a successful decode would, and calls
// WelsDecodeAccessUnitEnd. Input is split as BaseDecoderTest::ReadFrame does,
// and each chunk gets DecodeFrame2's per-call resets.

#include <algorithm>
#include <climits>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "au_parser.h"
#include "decoder.h"
#include "decoder_context.h"
#include "decoder_core.h"
#include "error_code.h"
#include "memmgr_nal_unit.h"
#include "memory_align.h"
#include "sha1.h"

#include "fixture.h"
#include "headers.h"

using namespace WelsDec;

namespace {

struct Dump {
  std::string s;
  void Tag (const char* t) { s += t; }
  void Kv (const char* k, long long v) {
    s += ' ';
    s += k;
    s += '=';
    s += std::to_string (v);
  }
  void Hex (const char* k, const uint8_t* p, int n) {
    static const char kH[] = "0123456789abcdef";
    s += ' ';
    s += k;
    s += '=';
    for (int i = 0; i < n; ++i) {
      s += kH[p[i] >> 4];
      s += kH[p[i] & 15];
    }
  }
  void Bits (const char* k, const bool* p, int n) {
    s += ' ';
    s += k;
    s += '=';
    for (int i = 0; i < n; ++i) s += p[i] ? '1' : '0';
  }
  template <typename T> void Seq (const char* k, const T* p, int n) {
    s += ' ';
    s += k;
    s += '=';
    for (int i = 0; i < n; ++i) {
      if (i) s += ',';
      s += std::to_string ((long long)p[i]);
    }
  }
  void End() { s += '\n'; }
};

void QuietLog (void*, const int32_t, const char*, va_list) {}

long long SpsIdx (PWelsDecoderContext c, const SSps* p) {
  if (p == NULL) return -1;
  const uint8_t* b = (const uint8_t*)p;
  const uint8_t* s0 = (const uint8_t*)&c->sSpsPpsCtx.sSpsBuffer[0];
  if (b >= s0 && b < s0 + sizeof (c->sSpsPpsCtx.sSpsBuffer)) return (b - s0) / (long long)sizeof (SSps);
  const uint8_t* u0 = (const uint8_t*)&c->sSpsPpsCtx.sSubsetSpsBuffer[0];
  if (b >= u0 && b < u0 + sizeof (c->sSpsPpsCtx.sSubsetSpsBuffer)) return 100 + (b - u0) / (long long)sizeof (SSubsetSps);
  return -2;
}

long long SubsetIdx (PWelsDecoderContext c, const SSubsetSps* p) {
  if (p == NULL) return -1;
  const uint8_t* b = (const uint8_t*)p;
  const uint8_t* u0 = (const uint8_t*)&c->sSpsPpsCtx.sSubsetSpsBuffer[0];
  if (b >= u0 && b < u0 + sizeof (c->sSpsPpsCtx.sSubsetSpsBuffer)) return (b - u0) / (long long)sizeof (SSubsetSps);
  return -2;
}

long long PpsIdx (PWelsDecoderContext c, const SPps* p) {
  if (p == NULL) return -1;
  const uint8_t* b = (const uint8_t*)p;
  const uint8_t* p0 = (const uint8_t*)&c->sSpsPpsCtx.sPpsBuffer[0];
  if (b >= p0 && b < p0 + sizeof (c->sSpsPpsCtx.sPpsBuffer)) return (b - p0) / (long long)sizeof (SPps);
  return -2;
}

long long LevelIdx (const SLevelLimits* p) {
  return p == NULL ? -1 : (long long) (p - WelsCommon::g_ksLevelLimits);
}

void DumpSps (Dump& d, const char* tag, int i, int avail, const SSps* p) {
  d.Tag ("P ");
  d.Tag (tag);
  d.Kv ("i", i);
  d.Kv ("a", avail);
  d.Kv ("id", p->iSpsId);
  d.Kv ("mbw", p->iMbWidth);
  d.Kv ("mbh", p->iMbHeight);
  d.Kv ("tot", p->uiTotalMbCount);
  d.Kv ("l2fn", p->uiLog2MaxFrameNum);
  d.Kv ("poct", p->uiPocType);
  d.Kv ("l2poc", p->iLog2MaxPocLsb);
  d.Kv ("onr", p->iOffsetForNonRefPic);
  d.Kv ("otb", p->iOffsetForTopToBottomField);
  d.Kv ("ncyc", p->iNumRefFramesInPocCycle);
  d.Seq ("ofr", p->iOffsetForRefFrame, p->iNumRefFramesInPocCycle < 0 ? 0 : (p->iNumRefFramesInPocCycle > 256 ? 256 : p->iNumRefFramesInPocCycle));
  d.Kv ("nref", p->iNumRefFrames);
  d.Kv ("crl", p->sFrameCrop.iLeftOffset);
  d.Kv ("crt", p->sFrameCrop.iTopOffset);
  d.Kv ("crr", p->sFrameCrop.iRightOffset);
  d.Kv ("crb", p->sFrameCrop.iBottomOffset);
  d.Kv ("prof", p->uiProfileIdc);
  d.Kv ("lvl", p->uiLevelIdc);
  d.Kv ("cfi", p->uiChromaFormatIdc);
  d.Kv ("cat", p->uiChromaArrayType);
  d.Kv ("bdl", p->uiBitDepthLuma);
  d.Kv ("bdc", p->uiBitDepthChroma);
  d.Kv ("dpoaz", p->bDeltaPicOrderAlwaysZeroFlag);
  d.Kv ("gaps", p->bGapsInFrameNumValueAllowedFlag);
  d.Kv ("fmo", p->bFrameMbsOnlyFlag);
  d.Kv ("mbaff", p->bMbaffFlag);
  d.Kv ("d8", p->bDirect8x8InferenceFlag);
  d.Kv ("crop", p->bFrameCroppingFlag);
  d.Kv ("vuip", p->bVuiParamPresentFlag);
  d.Kv ("cs0", p->bConstraintSet0Flag);
  d.Kv ("cs1", p->bConstraintSet1Flag);
  d.Kv ("cs2", p->bConstraintSet2Flag);
  d.Kv ("cs3", p->bConstraintSet3Flag);
  d.Kv ("sep", p->bSeparateColorPlaneFlag);
  d.Kv ("qpb", p->bQpPrimeYZeroTransfBypassFlag);
  d.Kv ("ssm", p->bSeqScalingMatrixPresentFlag);
  d.Bits ("slp", p->bSeqScalingListPresentFlag, 12);
  d.Hex ("sl4", &p->iScalingList4x4[0][0], 6 * 16);
  d.Hex ("sl8", &p->iScalingList8x8[0][0], 6 * 64);
  const SVui& v = p->sVui;
  d.Kv ("arp", v.bAspectRatioInfoPresentFlag);
  d.Kv ("ari", v.uiAspectRatioIdc);
  d.Kv ("sarw", v.uiSarWidth);
  d.Kv ("sarh", v.uiSarHeight);
  d.Kv ("ovp", v.bOverscanInfoPresentFlag);
  d.Kv ("ova", v.bOverscanAppropriateFlag);
  d.Kv ("vst", v.bVideoSignalTypePresentFlag);
  d.Kv ("vf", v.uiVideoFormat);
  d.Kv ("vfr", v.bVideoFullRangeFlag);
  d.Kv ("cdp", v.bColourDescripPresentFlag);
  d.Kv ("cp", v.uiColourPrimaries);
  d.Kv ("tc", v.uiTransferCharacteristics);
  d.Kv ("mc", v.uiMatrixCoeffs);
  d.Kv ("clp", v.bChromaLocInfoPresentFlag);
  d.Kv ("clt", v.uiChromaSampleLocTypeTopField);
  d.Kv ("clb", v.uiChromaSampleLocTypeBottomField);
  d.Kv ("tip", v.bTimingInfoPresentFlag);
  d.Kv ("nuit", v.uiNumUnitsInTick);
  d.Kv ("ts", v.uiTimeScale);
  d.Kv ("ffr", v.bFixedFrameRateFlag);
  d.Kv ("nhrd", v.bNalHrdParamPresentFlag);
  d.Kv ("vhrd", v.bVclHrdParamPresentFlag);
  d.Kv ("psp", v.bPicStructPresentFlag);
  d.Kv ("bsr", v.bBitstreamRestrictionFlag);
  d.Kv ("mvpb", v.bMotionVectorsOverPicBoundariesFlag);
  d.Kv ("mbpp", v.uiMaxBytesPerPicDenom);
  d.Kv ("mbpm", v.uiMaxBitsPerMbDenom);
  d.Kv ("mvh", v.uiLog2MaxMvLengthHorizontal);
  d.Kv ("mvv", v.uiLog2MaxMvLengthVertical);
  d.Kv ("mnrf", v.uiMaxNumReorderFrames);
  d.Kv ("mdfb", v.uiMaxDecFrameBuffering);
  d.Kv ("lim", LevelIdx (p->pSLevelLimits));
  d.End();
}

void DumpSubsetExt (Dump& d, int i, const SSubsetSps* p) {
  const SSpsSvcExt& e = p->sSpsSvcExt;
  d.Tag ("P subx");
  d.Kv ("i", i);
  d.Kv ("sl", e.sSeqScaledRefLayer.iLeftOffset);
  d.Kv ("st", e.sSeqScaledRefLayer.iTopOffset);
  d.Kv ("sr", e.sSeqScaledRefLayer.iRightOffset);
  d.Kv ("sb", e.sSeqScaledRefLayer.iBottomOffset);
  d.Kv ("ess", e.uiExtendedSpatialScalability);
  d.Kv ("phx", e.uiChromaPhaseXPlus1Flag);
  d.Kv ("phy", e.uiChromaPhaseYPlus1);
  d.Kv ("rphx", e.uiSeqRefLayerChromaPhaseXPlus1Flag);
  d.Kv ("rphy", e.uiSeqRefLayerChromaPhaseYPlus1);
  d.Kv ("ildf", e.bInterLayerDeblockingFilterCtrlPresentFlag);
  d.Kv ("tclp", e.bSeqTCoeffLevelPredFlag);
  d.Kv ("atclp", e.bAdaptiveTCoeffLevelPredFlag);
  d.Kv ("shr", e.bSliceHeaderRestrictionFlag);
  d.Kv ("svui", p->bSvcVuiParamPresentFlag);
  d.Kv ("e2", p->bAdditionalExtension2Flag);
  d.Kv ("e2d", p->bAdditionalExtension2DataFlag);
  d.End();
}

void DumpPps (Dump& d, int i, int avail, const SPps* p) {
  d.Tag ("P pps");
  d.Kv ("i", i);
  d.Kv ("a", avail);
  d.Kv ("id", p->iPpsId);
  d.Kv ("sps", p->iSpsId);
  d.Kv ("nsg", p->uiNumSliceGroups);
  d.Kv ("sgmt", p->uiSliceGroupMapType);
  d.Seq ("rl", p->uiRunLength, MAX_SLICEGROUP_IDS);
  d.Seq ("tl", p->uiTopLeft, MAX_SLICEGROUP_IDS);
  d.Seq ("br", p->uiBottomRight, MAX_SLICEGROUP_IDS);
  d.Kv ("sgcr", p->uiSliceGroupChangeRate);
  d.Kv ("psmu", p->uiPicSizeInMapUnits);
  d.Seq ("sgid", p->uiSliceGroupId, MAX_SLICEGROUP_IDS);
  d.Kv ("nr0", p->uiNumRefIdxL0Active);
  d.Kv ("nr1", p->uiNumRefIdxL1Active);
  d.Kv ("qp", p->iPicInitQp);
  d.Kv ("qs", p->iPicInitQs);
  d.Kv ("cq0", p->iChromaQpIndexOffset[0]);
  d.Kv ("cq1", p->iChromaQpIndexOffset[1]);
  d.Kv ("cabac", p->bEntropyCodingModeFlag);
  d.Kv ("pop", p->bPicOrderPresentFlag);
  d.Kv ("sgcd", p->bSliceGroupChangeDirectionFlag);
  d.Kv ("dfc", p->bDeblockingFilterControlPresentFlag);
  d.Kv ("cip", p->bConstainedIntraPredFlag);
  d.Kv ("rpc", p->bRedundantPicCntPresentFlag);
  d.Kv ("wp", p->bWeightedPredFlag);
  d.Kv ("wbi", p->uiWeightedBipredIdc);
  d.Kv ("t8", p->bTransform8x8ModeFlag);
  d.Kv ("psm", p->bPicScalingMatrixPresentFlag);
  d.Bits ("plp", p->bPicScalingListPresentFlag, 12);
  d.Hex ("sl4", &p->iScalingList4x4[0][0], 6 * 16);
  d.Hex ("sl8", &p->iScalingList8x8[0][0], 6 * 64);
  d.Kv ("cq2", p->iSecondChromaQPIndexOffset);
  d.End();
}

void DumpHeaderExt (Dump& d, const char* tag, const SNalUnitHeaderExt* h) {
  d.Tag (tag);
  d.Kv ("type", h->sNalUnitHeader.eNalUnitType);
  d.Kv ("nri", h->sNalUnitHeader.uiNalRefIdc);
  d.Kv ("idr", h->bIdrFlag);
  d.Kv ("prio", h->uiPriorityId);
  d.Kv ("nilp", h->iNoInterLayerPredFlag);
  d.Kv ("did", h->uiDependencyId);
  d.Kv ("qid", h->uiQualityId);
  d.Kv ("tid", h->uiTemporalId);
  d.Kv ("urb", h->bUseRefBasePicFlag);
  d.Kv ("disc", h->bDiscardableFlag);
  d.Kv ("out", h->bOutputFlag);
  d.Kv ("r3", h->uiReservedThree2Bits);
  d.Kv ("dq", h->uiLayerDqId);
  d.End();
}

void DumpBaseMarking (Dump& d, const SRefBasePicMarking* m) {
  d.Tag ("Q");
  d.Kv ("adapt", m->bAdaptiveRefBasePicMarkingModeFlag);
  if (m->bAdaptiveRefBasePicMarkingModeFlag) {
    d.s += " m=";
    for (int i = 0; i < MAX_MMCO_COUNT; ++i) {
      const auto& e = m->mmco_base[i];
      if (i) d.s += ',';
      d.s += std::to_string ((long long)e.uiMmcoType) + '/' + std::to_string ((long long)e.iShortFrameNum) + '/' +
             std::to_string ((long long)e.uiDiffOfPicNums) + '/' + std::to_string ((long long)e.uiLongTermPicNum);
      if (e.uiMmcoType == MMCO_END) break;
    }
  }
  d.End();
}

void DumpVcl (Dump& d, PWelsDecoderContext c, PNalUnit n) {
  DumpHeaderExt (d, "H", &n->sNalHeaderExt);
  const auto& v = n->sNalData.sVclNal;
  const SSliceHeaderExt& x = v.sSliceHeaderExt;
  const SSliceHeader& s = x.sSliceHeader;
  d.Tag ("S");
  d.Kv ("first", s.iFirstMbInSlice);
  d.Kv ("st", s.eSliceType);
  d.Kv ("spsi", SpsIdx (c, s.pSps));
  d.Kv ("ppsi", PpsIdx (c, s.pPps));
  d.Kv ("spsid", s.iSpsId);
  d.Kv ("ppsid", s.iPpsId);
  d.Kv ("idr", s.bIdrFlag);
  d.Kv ("fn", s.iFrameNum);
  d.Kv ("poc", s.iPicOrderCntLsb);
  d.Kv ("dpb", s.iDeltaPicOrderCntBottom);
  d.Kv ("dp0", s.iDeltaPicOrderCnt[0]);
  d.Kv ("dp1", s.iDeltaPicOrderCnt[1]);
  d.Kv ("rpc", s.iRedundantPicCnt);
  d.Kv ("dsmv", s.iDirectSpatialMvPredFlag);
  d.Kv ("rc0", s.uiRefCount[0]);
  d.Kv ("rc1", s.uiRefCount[1]);
  d.Kv ("ovr", s.bNumRefIdxActiveOverrideFlag);
  d.Kv ("qpd", s.iSliceQpDelta);
  d.Kv ("qp", s.iSliceQp);
  d.Kv ("qsd", s.iSliceQsDelta);
  d.Kv ("dbk", s.uiDisableDeblockingFilterIdc);
  d.Kv ("alpha", s.iSliceAlphaC0Offset);
  d.Kv ("beta", s.iSliceBetaOffset);
  d.Kv ("sgcc", s.iSliceGroupChangeCycle);
  d.Kv ("cabac", s.iCabacInitIdc);
  d.Kv ("mbw", s.iMbWidth);
  d.Kv ("mbh", s.iMbHeight);
  d.Kv ("idrid", s.uiIdrPicId);
  d.Kv ("fld", s.bFieldPicFlag);
  d.Kv ("bot", s.bBottomFiledFlag);
  d.Kv ("spsw", s.bSpForSwitchFlag);
  d.End();

  const SBitStringAux& b = v.sSliceBitsRead;
  d.Tag ("B");
  d.Kv ("raw", (long long) (b.pStartBuf - c->sRawData.pHead));
  d.Kv ("len", (long long) (b.pEndBuf - b.pStartBuf));
  d.Kv ("cur", (long long) (b.pCurBuf - b.pStartBuf));
  d.Kv ("bits", b.iBits);
  d.Kv ("left", b.iLeftBits);
  d.Kv ("cb", b.uiCurBits);
  d.Kv ("idx", (long long)b.iIndex);
  d.Kv ("ext", v.bSliceHeaderExtFlag);
  d.End();

  const SRefPicListReorderSyn& r = s.pRefPicListReordering;
  for (int l = 0; l < LIST_A; ++l) {
    d.Tag ("L");
    d.Kv ("l", l);
    d.Kv ("f", r.bRefPicListReorderingFlag[l]);
    if (r.bRefPicListReorderingFlag[l]) {
      d.s += " e=";
      for (int i = 0; i < MAX_REF_PIC_COUNT + 1; ++i) {
        const auto& e = r.sReorderingSyn[l][i];
        if (i) d.s += ',';
        d.s += std::to_string ((long long)e.uiReorderingOfPicNumsIdc) + '/' +
               std::to_string ((long long)e.uiAbsDiffPicNumMinus1) + '/' + std::to_string ((long long)e.uiLongTermPicNum);
        if (e.uiReorderingOfPicNumsIdc == 3) break;
      }
    }
    d.End();
  }

  if (s.pPps != NULL && (s.pPps->bWeightedPredFlag || s.pPps->uiWeightedBipredIdc == 1)) {
    const SPredWeightTabSyn& w = s.sPredWeightTable;
    d.Tag ("W");
    d.Kv ("ld", w.uiLumaLog2WeightDenom);
    d.Kv ("cd", w.uiChromaLog2WeightDenom);
    for (int l = 0; l < LIST_A; ++l) {
      d.s += l == 0 ? " w0=" : " w1=";
      for (int i = 0; i < MAX_REF_PIC_COUNT; ++i) {
        if (i) d.s += ',';
        d.s += std::to_string ((long long)w.sPredList[l].iLumaWeight[i]) + '/' +
               std::to_string ((long long)w.sPredList[l].iLumaOffset[i]) + '/' +
               std::to_string ((long long)w.sPredList[l].iChromaWeight[i][0]) + '/' +
               std::to_string ((long long)w.sPredList[l].iChromaOffset[i][0]) + '/' +
               std::to_string ((long long)w.sPredList[l].iChromaWeight[i][1]) + '/' +
               std::to_string ((long long)w.sPredList[l].iChromaOffset[i][1]);
      }
    }
    d.End();
  }

  const SRefPicMarking& m = s.sRefMarking;
  d.Tag ("M");
  d.Kv ("noout", m.bNoOutputOfPriorPicsFlag);
  d.Kv ("lt", m.bLongTermRefFlag);
  d.Kv ("adapt", m.bAdaptiveRefPicMarkingModeFlag);
  if (m.bAdaptiveRefPicMarkingModeFlag) {
    d.s += " m=";
    for (int i = 0; i < MAX_MMCO_COUNT; ++i) {
      const auto& e = m.sMmcoRef[i];
      if (i) d.s += ',';
      d.s += std::to_string ((long long)e.uiMmcoType) + '/' + std::to_string ((long long)e.iShortFrameNum) + '/' +
             std::to_string ((long long)e.iDiffOfPicNum) + '/' + std::to_string ((long long)e.uiLongTermPicNum) + '/' +
             std::to_string ((long long)e.iLongTermFrameIdx) + '/' + std::to_string ((long long)e.iMaxLongTermFrameIdx);
      if (e.uiMmcoType == MMCO_END) break;
    }
  }
  d.End();

  d.Tag ("X");
  d.Kv ("sub", SubsetIdx (c, x.pSubsetSps));
  d.Kv ("ildf", x.uiDisableInterLayerDeblockingFilterIdc);
  d.Kv ("ila", x.iInterLayerSliceAlphaC0Offset);
  d.Kv ("ilb", x.iInterLayerSliceBetaOffset);
  d.Kv ("sw", x.iScaledRefLayerPicWidthInSampleLuma);
  d.Kv ("sh", x.iScaledRefLayerPicHeightInSampleLuma);
  d.Kv ("bpwt", x.bBasePredWeightTableFlag);
  d.Kv ("srb", x.bStoreRefBasePicFlag);
  d.Kv ("cir", x.bConstrainedIntraResamplingFlag);
  d.Kv ("skip", x.bSliceSkipFlag);
  d.Kv ("abm", x.bAdaptiveBaseModeFlag);
  d.Kv ("dbm", x.bDefaultBaseModeFlag);
  d.Kv ("amp", x.bAdaptiveMotionPredFlag);
  d.Kv ("dmp", x.bDefaultMotionPredFlag);
  d.Kv ("arp", x.bAdaptiveResidualPredFlag);
  d.Kv ("drp", x.bDefaultResidualPredFlag);
  d.Kv ("tclp", x.bTCoeffLevelPredFlag);
  d.Kv ("phx", x.uiRefLayerChromaPhaseXPlus1Flag);
  d.Kv ("phy", x.uiRefLayerChromaPhaseYPlus1);
  d.Kv ("rdq", x.uiRefLayerDqId);
  d.Kv ("s0", x.uiScanIdxStart);
  d.Kv ("s1", x.uiScanIdxEnd);
  d.End();
  DumpBaseMarking (d, &x.sRefBasePicMarking);
}

void DumpPrefix (Dump& d, PWelsDecoderContext c) {
  PNalUnit n = &c->sSpsPpsCtx.sPrefixNal;
  DumpHeaderExt (d, "H", &n->sNalHeaderExt);
  const SPrefixNalUnit& p = n->sNalData.sPrefixNal;
  d.Tag ("F");
  d.Kv ("srb", p.bStoreRefBasePicFlag);
  d.Kv ("aext", p.bPrefixNalUnitAdditionalExtFlag);
  d.Kv ("ext", p.bPrefixNalUnitExtFlag);
  d.Kv ("ok", p.bPrefixNalCorrectFlag);
  d.End();
  DumpBaseMarking (d, &p.sRefPicBaseMarking);
}

// Snapshot of the parameter set slots, to dump only the ones that changed.
struct Snap {
  std::vector<uint8_t> sps, sub, pps;
  std::vector<uint8_t> spsA, subA, ppsA;
};

void TakeSnap (PWelsDecoderContext c, Snap& s) {
  SWelsDecoderSpsPpsCTX& x = c->sSpsPpsCtx;
  s.sps.assign ((uint8_t*)x.sSpsBuffer, (uint8_t*)x.sSpsBuffer + sizeof (x.sSpsBuffer));
  s.sub.assign ((uint8_t*)x.sSubsetSpsBuffer, (uint8_t*)x.sSubsetSpsBuffer + sizeof (x.sSubsetSpsBuffer));
  s.pps.assign ((uint8_t*)x.sPpsBuffer, (uint8_t*)x.sPpsBuffer + sizeof (x.sPpsBuffer));
  s.spsA.assign (x.bSpsAvailFlags, x.bSpsAvailFlags + MAX_SPS_COUNT);
  s.subA.assign (x.bSubspsAvailFlags, x.bSubspsAvailFlags + MAX_SPS_COUNT);
  s.ppsA.assign (x.bPpsAvailFlags, x.bPpsAvailFlags + MAX_PPS_COUNT);
}

void DiffSnap (Dump& d, PWelsDecoderContext c, Snap& s) {
  SWelsDecoderSpsPpsCTX& x = c->sSpsPpsCtx;
  for (int i = 0; i <= MAX_SPS_COUNT; ++i) {
    bool a = i < MAX_SPS_COUNT ? x.bSpsAvailFlags[i] : false;
    if (memcmp (&x.sSpsBuffer[i], &s.sps[i * sizeof (SSps)], sizeof (SSps)) != 0 ||
        (i < MAX_SPS_COUNT && (uint8_t)a != s.spsA[i]))
      DumpSps (d, "sps", i, i < MAX_SPS_COUNT ? a : -1, &x.sSpsBuffer[i]);
  }
  for (int i = 0; i <= MAX_SPS_COUNT; ++i) {
    bool a = i < MAX_SPS_COUNT ? x.bSubspsAvailFlags[i] : false;
    if (memcmp (&x.sSubsetSpsBuffer[i], &s.sub[i * sizeof (SSubsetSps)], sizeof (SSubsetSps)) != 0 ||
        (i < MAX_SPS_COUNT && (uint8_t)a != s.subA[i])) {
      DumpSps (d, "sub", i, i < MAX_SPS_COUNT ? a : -1, &x.sSubsetSpsBuffer[i].sSps);
      DumpSubsetExt (d, i, &x.sSubsetSpsBuffer[i]);
    }
  }
  for (int i = 0; i <= MAX_PPS_COUNT; ++i) {
    bool a = i < MAX_PPS_COUNT ? x.bPpsAvailFlags[i] : false;
    if (memcmp (&x.sPpsBuffer[i], &s.pps[i * sizeof (SPps)], sizeof (SPps)) != 0 ||
        (i < MAX_PPS_COUNT && (uint8_t)a != s.ppsA[i]))
      DumpPps (d, i, i < MAX_PPS_COUNT ? a : -1, &x.sPpsBuffer[i]);
  }
  TakeSnap (c, s);
}

struct Driver {
  PWelsDecoderContext ctx = NULL;
  SDecoderStatistics stats;
  SWelsLastDecPicInfo last;
  Snap snap;
  Dump d;
  int nals = 0, aus = 0;

  bool Init() {
    ctx = (PWelsDecoderContext)calloc (1, sizeof (SWelsDecoderContext));
    if (ctx == NULL) return false;
    ctx->pMemAlign = new CMemoryAlign (16);
    memset (&stats, 0, sizeof stats);
    memset (&last, 0, sizeof last);
    WelsDecoderLastDecPicInfoDefaults (last);
    ctx->pLastDecPicInfo = &last;
    ctx->pDecoderStatistics = &stats;
    ctx->sLogCtx.pfLog = QuietLog;
    WelsDecoderSpsPpsDefaults (ctx->sSpsPpsCtx);
    ctx->pParam = (SDecodingParam*)ctx->pMemAlign->WelsMallocz (sizeof (SDecodingParam), "SDecodingParam");
    if (ctx->pParam == NULL) return false;
    ctx->pParam->eEcActiveIdc = ERROR_CON_SLICE_COPY;
    ctx->pParam->bParseOnly = false;
    ctx->pParam->uiTargetDqLayer = UCHAR_MAX;
    ctx->eVideoType = VIDEO_BITSTREAM_DEFAULT;
    ctx->iFeedbackNalRefIdc = -1;
    if (WelsInitStaticMemory (ctx) != ERR_NONE) return false;
    // WelsOpenDecoder's flags (LONG_TERM_REF).
    ctx->bParamSetsLostFlag = true;
    ctx->bNewSeqBegin = true;
    ctx->bPrintFrameErrorTraceFlag = true;
    ctx->iSeqNum = 0;
    TakeSnap (ctx, snap);
    return true;
  }

  void Close() {
    if (ctx == NULL) return;
    CMemoryAlign* pMa = ctx->pMemAlign;
    WelsFreeStaticMemory (ctx);
    delete pMa;
    free (ctx);
    ctx = NULL;
  }

  void Ctx (const char* tag) {
    PAccessUnit au = ctx->pAccessUnitList;
    SWelsDecoderSpsPpsCTX& x = ctx->sSpsPpsCtx;
    d.Tag (tag);
    d.Kv ("err", ctx->iErrorCode);
    d.Kv ("aurdy", ctx->bAuReadyFlag);
    d.Kv ("avail", au->uiAvailUnitsNum);
    d.Kv ("actual", au->uiActualUnitsNum);
    d.Kv ("start", au->uiStartPos);
    d.Kv ("end", au->uiEndPos);
    d.Kv ("done", au->bCompletedAuFlag);
    d.Kv ("count", au->uiCountUnitsNum);
    d.Kv ("newseq", ctx->bNewSeqBegin);
    d.Kv ("nextseq", ctx->bNextNewSeqBegin);
    d.Kv ("lost", ctx->bParamSetsLostFlag);
    d.Kv ("newsps", ctx->bHasNewSps);
    d.Kv ("spsah", x.bSpsExistAheadFlag);
    d.Kv ("subah", x.bSubspsExistAheadFlag);
    d.Kv ("ppsah", x.bPpsExistAheadFlag);
    d.Kv ("avc", x.bAvcBasedFlag);
    d.Kv ("ow", x.iOverwriteFlags);
    d.Kv ("seqid", x.iSeqId);
    d.Kv ("ign", x.iSpsErrorIgnored);
    d.Kv ("igs", x.iSubSpsErrorIgnored);
    d.Kv ("igp", x.iPpsErrorIgnored);
    d.Kv ("ppli", x.iPPSLastInvalidId);
    d.Kv ("ppin", x.iPPSInvalidNum);
    d.Kv ("spli", x.iSPSLastInvalidId);
    d.Kv ("spin", x.iSPSInvalidNum);
    d.Kv ("ssli", x.iSubSPSLastInvalidId);
    d.Kv ("ssin", x.iSubSPSInvalidNum);
    d.Kv ("pocm", last.iPrevPicOrderCntMsb);
    d.Kv ("pocl", last.iPrevPicOrderCntLsb);
    d.Kv ("idrid", ctx->uiCurIdrPicId);
    d.Kv ("tdq", ctx->uiTargetDqId);
    d.Kv ("seq", ctx->iSeqNum);
    d.Kv ("sne", stats.iSpsNoExistNalNum);
    d.Kv ("ssne", stats.iSubSpsNoExistNalNum);
    d.Kv ("pne", stats.iPpsNoExistNalNum);
    d.Kv ("sre", stats.iSpsReportErrorNum);
    d.Kv ("ssre", stats.iSubSpsReportErrorNum);
    d.Kv ("pre", stats.iPpsReportErrorNum);
    d.Kv ("idrl", stats.uiIDRLostNum);
    d.End();
  }

  // The ConstructAccessUnit stand-in.
  void AccessUnit() {
    SBufferInfo bi;
    memset (&bi, 0, sizeof bi);
    int32_t iErr = WelsDecodeInitAccessUnitStart (ctx, &bi);
    PAccessUnit au = ctx->pAccessUnitList;
    d.Tag ("A");
    d.Kv ("n", aus++);
    d.Kv ("ret", iErr);
    d.Kv ("onel", ctx->bOnlyOneLayerInCurAuFlag);
    d.Kv ("fbv", ctx->iFeedbackVclNalInAu);
    d.Kv ("fbt", ctx->iFeedbackTidInAu);
    d.Kv ("fbn", ctx->iFeedbackNalRefIdc);
    d.Kv ("csps", SpsIdx (ctx, ctx->pSps));
    d.Kv ("cpps", PpsIdx (ctx, ctx->pPps));
    d.s += " act=";
    for (int i = 0; i < MAX_LAYER_NUM; ++i) {
      if (i) d.s += ',';
      d.s += std::to_string (SpsIdx (ctx, ctx->sSpsPpsCtx.pActiveLayerSps[i]));
    }
    d.End();
    Ctx ("a");
    if (iErr != ERR_NONE) return;
    for (uint32_t i = au->uiStartPos; i <= au->uiEndPos; ++i) {
      PNalUnit n = au->pNalUnitsList[i];
      const SSliceHeader& s = n->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader;
      d.Tag ("U");
      d.Kv ("i", i);
      d.Kv ("type", n->sNalHeaderExt.sNalUnitHeader.eNalUnitType);
      d.Kv ("did", n->sNalHeaderExt.uiDependencyId);
      d.Kv ("first", s.iFirstMbInSlice);
      d.Kv ("fn", s.iFrameNum);
      d.Kv ("poc", s.iPicOrderCntLsb);
      d.End();
    }
    ctx->bParamSetsLostFlag = false;
    WelsDecodeAccessUnitEnd (ctx);
    Ctx ("e");
  }

  // One NAL, from ParseNalHeader to the error handling, as WelsDecodeBs.
  // Returns true when WelsDecodeBs would return here.
  bool Nal (uint8_t* pDstNal, int32_t iDstIdx, uint8_t* pSrcNal, int32_t iSrcIdx, int32_t& iRet, bool last) {
    int32_t iConsumedBytes = 0;
    pDstNal[iDstIdx] = pDstNal[iDstIdx + 1] = pDstNal[iDstIdx + 2] = pDstNal[iDstIdx + 3] = 0;
    if (last) ctx->sRawData.pCurPos = pDstNal + iDstIdx + 4;
    uint8_t* pNalPayload =
      ParseNalHeader (ctx, &ctx->sCurNalHead, pDstNal, iDstIdx, pSrcNal - 3, iSrcIdx + 3, &iConsumedBytes);
    EWelsNalUnitType t = ctx->sCurNalHead.eNalUnitType;
    PNalUnit parsed = NULL;
    if (pNalPayload && (t == NAL_UNIT_CODED_SLICE || t == NAL_UNIT_CODED_SLICE_IDR || t == NAL_UNIT_CODED_SLICE_EXT))
      parsed = ctx->pAccessUnitList->pNalUnitsList[ctx->pAccessUnitList->uiAvailUnitsNum - 1];
    if (pNalPayload && IS_PARAM_SETS_NALS (t))
      iRet = ParseNonVclNal (ctx, pNalPayload, iDstIdx - iConsumedBytes, pSrcNal - 3, iSrcIdx + 3);
    d.Tag ("N");
    d.Kv ("n", nals++);
    d.Kv ("type", t);
    d.Kv ("nri", ctx->sCurNalHead.uiNalRefIdc);
    d.Kv ("fzb", ctx->sCurNalHead.uiForbiddenZeroBit);
    d.Kv ("len", iDstIdx);
    d.Kv ("src", iSrcIdx + 3);
    d.Kv ("used", iConsumedBytes);
    d.Kv ("pay", pNalPayload ? (long long) (pNalPayload - pDstNal) : -1);
    d.Kv ("ret", iRet);
    d.End();
    Ctx ("c");
    if (parsed) DumpVcl (d, ctx, parsed);
    if (pNalPayload && t == NAL_UNIT_PREFIX) DumpPrefix (d, ctx);
    DiffSnap (d, ctx, snap);
    if (pNalPayload && ctx->bAuReadyFlag && ctx->pAccessUnitList->uiAvailUnitsNum != 0) AccessUnit();
    DecodeFinishUpdate (ctx);
    DiffSnap (d, ctx, snap);

    if ((dsOutOfMemory | dsNoParamSets) & ctx->iErrorCode) {
      ctx->bParamSetsLostFlag = true;
      if (last || (dsOutOfMemory & ctx->iErrorCode)) return true;
    }
    if (iRet) {
      iRet = 0;
      if (dsNoParamSets & ctx->iErrorCode) ctx->bParamSetsLostFlag = true;
      return true;
    }
    if (!last && ctx->iErrorCode != ERR_NONE && !(ctx->iErrorCode & dsDataErrorConcealed)) return true;
    return false;
  }

  static bool RawDataWrapIsClean (PWelsDecoderContext pCtx, int32_t iNewBytes) {
    const PAccessUnit pAu = pCtx->pAccessUnitList;
    if (pAu == NULL || pAu->pNalUnitsList == NULL || pAu->uiAvailUnitsNum == 0) return true;
    const uint8_t* pHead = pCtx->sRawData.pHead;
    for (uint32_t i = 0; i < pAu->uiAvailUnitsNum; ++i) {
      const PNalUnit pNal = pAu->pNalUnitsList[i];
      if (pNal == NULL) continue;
      const uint8_t* pStart = pNal->sNalData.sVclNal.sSliceBitsRead.pStartBuf;
      if (pStart != NULL && pStart >= pHead && pStart < pHead + iNewBytes) return false;
    }
    return true;
  }

  // WelsDecodeBs, the !bEndOfStreamFlag branch.
  void DecodeBs (const uint8_t* kpBsBuf, const int32_t kiBsLen) {
    SDataBuffer* pRawData = &ctx->sRawData;
    int32_t iSrcIdx = 0, iSrcConsumed = 0, iDstIdx = 0, iSrcLength = 0, iRet = 0, iOffset = 0;
    uint8_t* pSrcNal = NULL;
    uint8_t* pDstNal = NULL;

    if (NULL == DetectStartCodePrefix (kpBsBuf, &iOffset, kiBsLen)) {
      ctx->iErrorCode |= dsBitstreamError;
      return;
    }
    pSrcNal = const_cast<uint8_t*> (kpBsBuf) + iOffset;
    iSrcLength = kiBsLen - iOffset;
    if ((kiBsLen + 4) > (pRawData->pEnd - pRawData->pCurPos)) {
      if (!RawDataWrapIsClean (ctx, kiBsLen + 4)) {
        ctx->iErrorCode |= dsOutOfMemory;
        return;
      }
      pRawData->pCurPos = pRawData->pHead;
    }
    pDstNal = pRawData->pCurPos;
    bool bNalStartBytes = false;
    while (iSrcConsumed < iSrcLength) {
      if ((2 + iSrcConsumed < iSrcLength) && (0 == LD16 (pSrcNal + iSrcIdx)) && (pSrcNal[2 + iSrcIdx] <= 0x03)) {
        if (bNalStartBytes && (pSrcNal[2 + iSrcIdx] != 0x00 && pSrcNal[2 + iSrcIdx] != 0x01)) {
          ctx->iErrorCode |= dsBitstreamError;
          return;
        }
        if (pSrcNal[2 + iSrcIdx] == 0x02) {
          ctx->iErrorCode |= dsBitstreamError;
          return;
        } else if (pSrcNal[2 + iSrcIdx] == 0x00) {
          pDstNal[iDstIdx++] = pSrcNal[iSrcIdx++];
          iSrcConsumed++;
          bNalStartBytes = true;
        } else if (pSrcNal[2 + iSrcIdx] == 0x03) {
          if ((3 + iSrcConsumed < iSrcLength) && pSrcNal[3 + iSrcIdx] > 0x03) {
            /* Just skip */
          } else {
            ST16 (pDstNal + iDstIdx, 0);
            iDstIdx += 2;
          }
          iSrcIdx += 3;
          iSrcConsumed += 3;
        } else { // 0x01
          bNalStartBytes = false;
          if (Nal (pDstNal, iDstIdx, pSrcNal, iSrcIdx, iRet, false)) return;
          pDstNal += (iDstIdx + 4);
          if ((iSrcLength - iSrcConsumed + 4) > (pRawData->pEnd - pDstNal)) {
            if (!RawDataWrapIsClean (ctx, iSrcLength - iSrcConsumed + 4)) {
              ctx->iErrorCode |= dsOutOfMemory;
              return;
            }
            pDstNal = pRawData->pCurPos = pRawData->pHead;
          } else {
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
    Nal (pDstNal, iDstIdx, pSrcNal, iSrcIdx, iRet, true);
  }

  // DecodeFrame2's per-call resets, then WelsDecodeBs.
  void Chunk (const uint8_t* p, int32_t n, int idx) {
    d.Tag ("C");
    d.Kv ("i", idx);
    d.Kv ("len", n);
    int32_t iChk = CheckBsBuffer (ctx, n);
    d.Kv ("chk", iChk);
    d.End();
    if (iChk != ERR_NONE) return;
    ctx->bEndOfStreamFlag = false;
    ctx->iErrorCode = dsErrorFree;
    ctx->iFeedbackVclNalInAu = FEEDBACK_UNKNOWN_NAL;
    ctx->bReferenceLostAtT0Flag = false;
    ctx->iFrameNum = -1;
    ctx->iFeedbackTidInAu = -1;
    ctx->iFeedbackNalRefIdc = -1;
    ctx->uiTimeStamp = 0;
    DecodeBs (p, n);
    Ctx ("r");
  }

  // DecodeFrame2 (NULL, 0): the bEndOfStreamFlag branch of WelsDecodeBs.
  void Flush() {
    d.Tag ("C eos");
    d.End();
    ctx->bEndOfStreamFlag = true;
    ctx->iErrorCode = dsErrorFree;
    ctx->iFeedbackVclNalInAu = FEEDBACK_UNKNOWN_NAL;
    ctx->bReferenceLostAtT0Flag = false;
    ctx->iFrameNum = -1;
    ctx->iFeedbackTidInAu = -1;
    ctx->iFeedbackNalRefIdc = -1;
    ctx->uiTimeStamp = 0;
    if (ctx->pAccessUnitList->uiAvailUnitsNum != 0) {
      ctx->pAccessUnitList->uiEndPos = ctx->pAccessUnitList->uiAvailUnitsNum - 1;
      AccessUnit();
      DecodeFinishUpdate (ctx);
      DiffSnap (d, ctx, snap);
      if ((dsOutOfMemory | dsNoParamSets) & ctx->iErrorCode) ctx->bParamSetsLostFlag = true;
    }
    Ctx ("r");
  }
};

}  // namespace

Chunks SplitChunks (const std::vector<uint8_t>& s) {
  // BaseDecoderTest::ReadFrame's split, as in main.cpp.
  Chunks chunks;
  size_t pos = 0, n = s.size();
  std::vector<uint8_t> buf;
  for (;;) {
    buf.clear();
    int zeroCount = 0;
    while (pos < n) {
      uint8_t b = s[pos++];
      buf.push_back (b);
      if (buf.size() <= 4) continue;
      if (zeroCount < 3) {
        zeroCount = b != 0 ? 0 : zeroCount + 1;
      } else if (b == 1) {
        pos -= 4;
        buf.resize (buf.size() - 4);
        break;
      } else if (b == 0) {
        zeroCount = 3;
      } else {
        zeroCount = 0;
      }
    }
    if (buf.empty()) break;
    chunks.push_back (buf);
  }
  return chunks;
}

// Deterministic damage, the kind a lossy network does to a stream: of every
// 16 chunks (NALs, roughly) one is dropped, one is cut short and three get
// one to three bit flips past the start code. Seeded from the stream size
// and the variant, with fixture.h's Rng, so the C# side damages identically.
Chunks DamageChunks (const Chunks& in, uint32_t streamSize, int variant) {
  fixture::Rng r (streamSize * 2654435761u + (uint32_t)variant);
  Chunks out;
  for (const std::vector<uint8_t>& c : in) {
    uint32_t x = r.Next() % 16;
    if (x == 0) continue;
    std::vector<uint8_t> d = c;
    if (x == 1) {
      if (d.size() > 5) d.resize (5 + r.Next() % (uint32_t) (d.size() - 5));
    } else if (x <= 4) {
      uint32_t flips = 1 + r.Next() % 3;
      for (uint32_t f = 0; f < flips; ++f) {
        if (d.size() <= 4) break;
        uint32_t pos = 4 + r.Next() % (uint32_t) (d.size() - 4);
        d[pos] ^= (uint8_t) (1u << (r.Next() % 8));
      }
    }
    out.push_back (d);
  }
  return out;
}

bool HeaderDumpChunks (const Chunks& chunks, std::string& out, int& nals, int& aus) {
  Driver drv;
  if (!drv.Init()) {
    drv.Close();
    return false;
  }
  int i = 0;
  for (const std::vector<uint8_t>& c : chunks) drv.Chunk (c.data(), (int32_t)c.size(), i++);
  drv.Flush();
  out.swap (drv.d.s);
  nals = drv.nals;
  aus = drv.aus;
  drv.Close();
  return true;
}

bool HeaderDump (const std::vector<uint8_t>& s, std::string& out, int& nals, int& aus) {
  return HeaderDumpChunks (SplitChunks (s), out, nals, aus);
}

std::string Sha1Hex (const std::string& s) {
  SHA1Context ctx;
  SHA1Reset (&ctx);
  SHA1Input (&ctx, (const unsigned char*)s.data(), (unsigned)s.size());
  unsigned char d[SHA_DIGEST_LENGTH];
  SHA1Result (&ctx, d);
  char hex[SHA_DIGEST_LENGTH * 2 + 1];
  for (int i = 0; i < SHA_DIGEST_LENGTH; ++i) snprintf (&hex[i * 2], 3, "%02x", d[i]);
  return hex;
}
