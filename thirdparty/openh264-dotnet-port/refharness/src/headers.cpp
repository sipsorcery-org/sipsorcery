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
//
// The D2 macroblock dump (h264ref mbs / mbdump) is the same driver with one
// addition: before WelsDecodeAccessUnitEnd the stand-in parses the access
// unit's slices the way DecodeCurrentAccessUnit does up to and including
// WelsDecodeSlice (MbAlloc, MbParse), single-threaded and without
// reconstruction, error concealment or the reference list code. The reference
// list is a fixed stand-in (RefList) and one picture serves as pDec. Each
// parsed slice gets a "D" line with the parser's end state and the SHA-1 of a
// binary record of every macroblock it touched (Record); each access unit a
// "Y" line with the SHA-1 of the picture (which only I_PCM writes during the
// parse). The header lines stay in the dump, so the parse's side effects on the
// context (statistics, error codes) are checked too. CABAC slices and B slices
// are skipped (not ported yet) on both sides.
//
// D3 extends the same mbs mode with intra reconstruction: each slice of the
// target layer is reconstructed after its parse, as DecodeCurrentAccessUnit
// does through WelsDecodeConstructSlice, by a copy of
// WelsTargetSliceConstruction's macroblock loop (ReconSlice) that calls
// WelsTargetMbConstruction for the intra macroblocks only and stops before the
// deblocking. Its result and the SHA-1 of the reconstructed samples go on the
// slice's "D" line, and the "Y" line now covers the intra reconstruction.
// Inter macroblocks are left as the picture held them (inter prediction is
// D4), so intra macroblocks next to them predict from those samples; both
// sides do the same.
//
// D4 replaces the stand-ins with the decoder's own reference handling and
// reconstructs every macroblock. The pictures come from the DPB
// (AllocPicBuffOnNewSeqBegin on a new sequence, the "Z" line; PrefetchPic per
// access unit), each slice of the top dependency layer builds its list with
// InitRefPicList after the frame_num gap check (a "gap", "rpl", "rc" and
// "refs" group on its "D" line), and the access unit ends as
// DecodeCurrentAccessUnit's does: the completeness flags
// (CheckRefPicturesComplete, "arc"), DecodeFrameConstruction's share of them,
// then for a reference picture WelsMarkAsRef and ExpandReferencingPicture (a
// "K" line with the lists after marking), and the frame_num bookkeeping. The
// "Y" line names the picture as well. Error concealment (NeedErrorCon,
// ImplementErrorCon, MarkECFrameAsRef) and deblocking are not run (D6, D5), so
// a damaged picture keeps whatever its unreconstructed macroblocks held and
// references are unfiltered; both sides do the same. Where upstream returns
// from DecodeCurrentAccessUnit early (an FmoParamUpdate or reconstruction
// error) the access unit ends without marking, which is where upstream's
// concealment would take over. Skipped slices still do nothing, an access
// unit with no slice parsed is not marked, and once PrefetchPic fails the rest
// of the stream is skipped (noPic).

#include <algorithm>
#include <climits>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "au_parser.h"
#include "decode_mb_aux.h"
#include "decode_slice.h"
#include "decoder.h"
#include "decoder_context.h"
#include "decoder_core.h"
#include "error_code.h"
#include "expand_pic.h"
#include "fmo.h"
#include "manage_dec_ref.h"
#include "mc.h"
#include "memmgr_nal_unit.h"
#include "memory_align.h"
#include "pic_queue.h"
#include "sha1.h"
#include "vlc_decoder.h"

#include "fixture.h"
#include "headers.h"

// Defined in the decoder but declared in no header.
namespace WelsDec {
PPicture AllocPicture (PWelsDecoderContext pCtx, const int32_t kiPicWidth, const int32_t kiPicHeight);
void FreePicture (PPicture pPic, CMemoryAlign* pMa);
void InitCurDqLayerData (PWelsDecoderContext pCtx, PDqLayer pCurDq);
int32_t InitRefPicList (PWelsDecoderContext pCtx, const uint8_t kuiNRi, int32_t iPoc);
}

using namespace WelsDec;

namespace {

// decoder_core.cpp's InitDqLayerInfo, which is static there. Verbatim.
inline void InitDqLayerInfo (PDqLayer pDqLayer, PLayerInfo pLayerInfo, PNalUnit pNalUnit, PPicture pPicDec) {
  PNalUnitHeaderExt pNalHdrExt    = &pNalUnit->sNalHeaderExt;
  PSliceHeaderExt pShExt          = &pNalUnit->sNalData.sVclNal.sSliceHeaderExt;
  PSliceHeader pSh                = &pShExt->sSliceHeader;
  const uint8_t kuiQualityId      = pNalHdrExt->uiQualityId;

  memcpy (&pDqLayer->sLayerInfo, pLayerInfo, sizeof (SLayerInfo)); //confirmed_safe_unsafe_usage

  pDqLayer->pDec        = pPicDec;
  pDqLayer->iMbWidth    = pSh->iMbWidth;        // MB width of this picture
  pDqLayer->iMbHeight   = pSh->iMbHeight;// MB height of this picture

  pDqLayer->iSliceIdcBackup = (pSh->iFirstMbInSlice << 7) | (pNalHdrExt->uiDependencyId << 4) | (pNalHdrExt->uiQualityId);

  /* Common syntax elements across all slices of a DQLayer */
  pDqLayer->uiPpsId                                     = pLayerInfo->pPps->iPpsId;
  pDqLayer->uiDisableInterLayerDeblockingFilterIdc      = pShExt->uiDisableInterLayerDeblockingFilterIdc;
  pDqLayer->iInterLayerSliceAlphaC0Offset               = pShExt->iInterLayerSliceAlphaC0Offset;
  pDqLayer->iInterLayerSliceBetaOffset                  = pShExt->iInterLayerSliceBetaOffset;
  pDqLayer->iSliceGroupChangeCycle                      = pSh->iSliceGroupChangeCycle;
  pDqLayer->bStoreRefBasePicFlag                        = pShExt->bStoreRefBasePicFlag;
  pDqLayer->bTCoeffLevelPredFlag                        = pShExt->bTCoeffLevelPredFlag;
  pDqLayer->bConstrainedIntraResamplingFlag             = pShExt->bConstrainedIntraResamplingFlag;
  pDqLayer->uiRefLayerDqId                              = pShExt->uiRefLayerDqId;
  pDqLayer->uiRefLayerChromaPhaseXPlus1Flag             = pShExt->uiRefLayerChromaPhaseXPlus1Flag;
  pDqLayer->uiRefLayerChromaPhaseYPlus1                 = pShExt->uiRefLayerChromaPhaseYPlus1;
  pDqLayer->bUseWeightPredictionFlag                    = false;
  pDqLayer->bUseWeightedBiPredIdc = false;
  //memcpy(&pDqLayer->sScaledRefLayer, &pShExt->sScaledRefLayer, sizeof(SPosOffset));//confirmed_safe_unsafe_usage

  if (kuiQualityId == BASE_QUALITY_ID) {
    pDqLayer->pRefPicListReordering = &pSh->pRefPicListReordering;
    pDqLayer->pRefPicMarking = &pSh->sRefMarking;

    pDqLayer->bUseWeightPredictionFlag = pSh->pPps->bWeightedPredFlag;
    pDqLayer->bUseWeightedBiPredIdc = pSh->pPps->uiWeightedBipredIdc != 0;
    if (pSh->pPps->bWeightedPredFlag || pSh->pPps->uiWeightedBipredIdc) {
      pDqLayer->pPredWeightTable = &pSh->sPredWeightTable;
    }
    pDqLayer->pRefPicBaseMarking        = &pShExt->sRefBasePicMarking;
  }

  pDqLayer->uiLayerDqId                 = pNalHdrExt->uiLayerDqId;      // dq_id of current layer
  pDqLayer->bUseRefBasePicFlag          = pNalHdrExt->bUseRefBasePicFlag;
}

// One macroblock's parse results, the D2 record: everything the CAVLC
// macroblock parsers write for it, as raw little-endian bytes.
void Record (std::string& b, PDqLayer dq, PPicture pic, int32_t xy) {
  auto put = [&b] (const void* p, size_t n) { b.append ((const char*)p, n); };
  put (&xy, 4);
  put (&pic->pMbType[xy], 4);
  put (&dq->pSliceIdc[xy], 4);
  const uint8_t f[11] = {
    (uint8_t)dq->pNoSubMbPartSizeLessThan8x8Flag[xy], (uint8_t)dq->pTransformSize8x8Flag[xy],
    (uint8_t)dq->pLumaQp[xy], (uint8_t)dq->pChromaQp[xy][0], (uint8_t)dq->pChromaQp[xy][1], (uint8_t)dq->pCbp[xy],
    (uint8_t)dq->pResidualPredFlag[xy], (uint8_t)dq->pInterPredictionDoneFlag[xy],
    (uint8_t)dq->pMbRefConcealedFlag[xy], dq->pIntraNxNAvailFlag[xy], (uint8_t)dq->pChromaPredMode[xy]
  };
  put (f, sizeof f);
  put (dq->pNzc[xy], 24);
  put (dq->pIntraPredMode[xy], 8);
  put (dq->pIntra4x4FinalMode[xy], 16);
  put (dq->pSubMbType[xy], 4 * sizeof (uint32_t));
  put (pic->pMv[LIST_0][xy], 16 * 2 * sizeof (int16_t));
  put (pic->pRefIndex[LIST_0][xy], 16);
  put (dq->pScaledTCoeff[xy], MB_COEFF_LIST_SIZE * sizeof (int16_t));
}

std::string Sha1Final (SHA1Context& c) {
  unsigned char h[SHA_DIGEST_LENGTH];
  SHA1Result (&c, h);
  char hex[SHA_DIGEST_LENGTH * 2 + 1];
  for (int i = 0; i < SHA_DIGEST_LENGTH; ++i) snprintf (&hex[i * 2], 3, "%02x", h[i]);
  return hex;
}

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

  // The D2 macroblock dump (mbs mode), see MbParse.
  bool mbs = false;
  SVlcTable vlc;
  int slices = 0, mbRecs = 0;   // slices parsed, macroblock records written
  // Set once PrefetchPic finds no free picture. Upstream's DecodeFrame2 then
  // resets the whole decoder (ResetDecoder, the API layer: D6), and without
  // that PrefetchPic's next search would run past the buffer, so the rest of
  // the stream is skipped (skip 6).
  bool noPic = false;

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
    if (mbs) {
      // CWelsDecoder's VLC tables (welsDecoderExt.cpp).
      memset (&vlc, 0, sizeof vlc);
      InitVlcTable (&vlc);
      ctx->pVlcTable = &vlc;
      // InitDecFuncs (WelsInitDecoder), without the deblocking (D5).
      WelsBlockFuncInit (&ctx->sBlockFunc, 0);
      InitPredFunc (ctx, 0);
      InitMcFunc (&ctx->sMcFunc, 0);
      InitExpandPictureFunc (&ctx->sExpandPicFunc, 0);
    }
    return true;
  }

  void Close() {
    if (ctx == NULL) return;
    CMemoryAlign* pMa = ctx->pMemAlign;
    if (mbs) WelsFreeDynamicMemory (ctx);
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
    if (mbs) {
      if (!MbAlloc()) return;  // ConstructAccessUnit returns without WelsDecodeAccessUnitEnd
      MbParse();
    }
    ctx->bParamSetsLostFlag = false;
    WelsDecodeAccessUnitEnd (ctx);
    Ctx ("e");
  }

  // InitConstructAccessUnit's AllocPicBuffOnNewSeqBegin: the DPB and the DQ
  // layer memory for a new sequence. Returns false on failure.
  bool MbAlloc() {
    if (!ctx->bNewSeqBegin) return true;
    int32_t iErr = AllocPicBuffOnNewSeqBegin (ctx);
    d.Tag ("Z");
    d.Kv ("w", ctx->pSps->iMbWidth << 4);
    d.Kv ("h", ctx->pSps->iMbHeight << 4);
    d.Kv ("ret", iErr);
    d.Kv ("cap", ctx->pPicBuff != NULL ? ctx->pPicBuff->iCapacity : -1);
    d.Kv ("pq", ctx->iPicQueueNumber);
    d.End();
    return iErr == ERR_NONE;
  }

  // A picture: "b<DPB index>" then "f<frame_num>", or "l<long term frame
  // index>" for a long term reference, then "x" if it is incomplete; "-" for
  // none.
  void Pic (PPicture p) {
    if (p == NULL) {
      d.s += '-';
      return;
    }
    int32_t b = -1;
    for (int32_t i = 0; ctx->pPicBuff != NULL && i < ctx->pPicBuff->iCapacity; ++i)
      if (ctx->pPicBuff->ppPic[i] == p) b = i;
    d.s += 'b' + std::to_string (b);
    d.s += p->bIsLongRef ? 'l' + std::to_string (p->iLongTermFrameIdx) : 'f' + std::to_string (p->iFrameNum);
    if (!p->bIsComplete) d.s += 'x';
  }

  void PicList (const char* k, PPicture* l, int32_t n) {
    if (n > MAX_DPB_COUNT) n = MAX_DPB_COUNT;
    d.s += ' ';
    d.s += k;
    d.s += '=';
    for (int32_t i = 0; i < n; ++i) {
      if (i) d.s += ',';
      Pic (l[i]);
    }
  }

  // InitRefPicList's result (-1 when it is not called) and list 0.
  void DumpRefList (int32_t iRpl) {
    d.Kv ("rpl", iRpl);
    d.Kv ("rc", ctx->sRefPic.uiRefCount[LIST_0]);
    PicList ("refs", ctx->sRefPic.pRefList[LIST_0], ctx->sRefPic.uiRefCount[LIST_0]);
  }

  // WelsMarkAsRef's result and the lists it leaves.
  void DumpMarking (int au, int32_t iRet) {
    d.Tag ("K");
    d.Kv ("au", au);
    d.Kv ("ret", iRet);
    d.Kv ("maxlt", ctx->sRefPic.iMaxLongTermFrameIdx);
    d.Kv ("mmco5", ctx->pLastDecPicInfo->bLastHasMmco5);
    PicList ("st", ctx->sRefPic.pShortRefList[LIST_0], ctx->sRefPic.uiShortRefCount[LIST_0]);
    PicList ("lt", ctx->sRefPic.pLongRefList[LIST_0], ctx->sRefPic.uiLongRefCount[LIST_0]);
    d.End();
  }

  // What DecodeCurrentAccessUnit does with the access unit (decoder_core.cpp,
  // the iThreadCount <= 1 path, with error concealment on), in order, without
  // the concealment itself and the picture output.
  void MbParse() {
    PAccessUnit pCurAu = ctx->pAccessUnitList;
    const int au = aus - 1;
    bool bAllRefComplete = true;
    // GetTargetDqId.
    const uint8_t kuiTargetLayerDqId = WELS_MIN (ctx->uiTargetDqId, ctx->pParam->uiTargetDqLayer);
    const uint8_t kuiDependencyIdMax = (kuiTargetLayerDqId & 0x7F) >> 4;
    int16_t iLastIdD = -1, iLastIdQ = -1;
    ctx->uiNalRefIdc = 0;
    if (ctx->bInitialDqLayersMem || ctx->pCurDqLayer == NULL) ctx->pCurDqLayer = ctx->pDqLayersList[0];
    InitCurDqLayerData (ctx, ctx->pCurDqLayer);
    if (ctx->pPicBuff == NULL || (ctx->pSps->iMbWidth << 4) > ctx->iImgWidthInPixel
        || (ctx->pSps->iMbHeight << 4) > ctx->iImgHeightInPixel) {
      d.Tag ("D");
      d.Kv ("au", au);
      d.Kv ("skip", 3);
      d.End();
      return;
    }
    if (!noPic) ctx->pDec = PrefetchPic (ctx->pPicBuff);
    if (ctx->iTotalNumMbRec != 0) ctx->iTotalNumMbRec = 0;
    if (ctx->pDec == NULL) {
      noPic = true;
      ctx->iErrorCode |= dsOutOfMemory;
      d.Tag ("D");
      d.Kv ("au", au);
      d.Kv ("skip", 6);
      d.End();
      return;
    }
    ctx->pDec->bNewSeqBegin = ctx->bNewSeqBegin;
    // The picture's start (iTotalNumMbRec == 0).
    for (int32_t i = 0; i < LAYER_NUM_EXCHANGEABLE; ++ i)
      memset (ctx->sMb.pSliceIdc[i], 0xff, (ctx->sMb.iMbWidth * ctx->sMb.iMbHeight * sizeof (int32_t)));
    memset (ctx->pCurDqLayer->pMbCorrectlyDecodedFlag, 0, ctx->pSps->iMbWidth * ctx->pSps->iMbHeight * sizeof (bool));
    memset (ctx->pCurDqLayer->pMbRefConcealedFlag, 0, ctx->pSps->iMbWidth * ctx->pSps->iMbHeight * sizeof (bool));
    memset (ctx->pDec->pRefPic[LIST_0], 0, sizeof (PPicture) * MAX_DPB_COUNT);
    memset (ctx->pDec->pRefPic[LIST_1], 0, sizeof (PPicture) * MAX_DPB_COUNT);
    ctx->pDec->iMbNum = ctx->pSps->iMbWidth * ctx->pSps->iMbHeight;
    ctx->pDec->iMbEcedNum = 0;
    ctx->pDec->iMbEcedPropNum = 0;
    ctx->bRPLRError = false;
    GetI4LumaIChromaAddrTable (ctx->iDecBlockOffsetArray, ctx->pDec->iLinesize[0], ctx->pDec->iLinesize[1]);

    SLayerInfo pLayerInfo;
    memset (&pLayerInfo, 0, sizeof (SLayerInfo));
    PSliceHeader pLastSh = NULL;  // the last slice header through the loop
    bool parsed = false, target = false, ended = false;
    for (uint32_t iIdx = pCurAu->uiStartPos; iIdx <= pCurAu->uiEndPos; ++iIdx) {
      PNalUnit pNalCur = pCurAu->pNalUnitsList[iIdx];
      PDqLayer dq_cur = ctx->pCurDqLayer;
      PSliceHeader pSh = &pNalCur->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader;
      PSliceHeaderExt pShExt = &pNalCur->sNalData.sVclNal.sSliceHeaderExt;
      d.Tag ("D");
      d.Kv ("au", au);
      d.Kv ("i", iIdx);
      d.Kv ("first", pSh->iFirstMbInSlice);
      d.Kv ("st", pSh->eSliceType);
      if (pNalCur->sNalHeaderExt.uiLayerDqId > kuiTargetLayerDqId) {
        d.Kv ("skip", 4);
        d.End();
        break;
      }
      const int16_t iCurrIdQ = pNalCur->sNalHeaderExt.uiQualityId;
      const int16_t iCurrIdD = pNalCur->sNalHeaderExt.uiDependencyId;
      const uint8_t uiTid = pNalCur->sNalHeaderExt.uiTemporalId;
      ctx->bRPLRError = false;
      const bool bReconstructSlice = pNalCur->sNalHeaderExt.uiLayerDqId == kuiTargetLayerDqId;
      memcpy (&pLayerInfo.sNalHeaderExt, &pNalCur->sNalHeaderExt, sizeof (SNalUnitHeaderExt));
      ctx->pDec->iFrameNum = pSh->iFrameNum;
      ctx->pDec->iFramePoc = pSh->iPicOrderCntLsb;
      ctx->pDec->bIdrFlag = pNalCur->sNalHeaderExt.bIdrFlag;
      ctx->pDec->eSliceType = pSh->eSliceType;
      memcpy (&pLayerInfo.sSliceInLayer.sSliceHeaderExt, pShExt, sizeof (SSliceHeaderExt));
      pLayerInfo.sSliceInLayer.bSliceHeaderExtFlag = pNalCur->sNalData.sVclNal.bSliceHeaderExtFlag;
      pLayerInfo.sSliceInLayer.eSliceType = pSh->eSliceType;
      pLayerInfo.sSliceInLayer.iLastMbQp = pSh->iSliceQp;
      dq_cur->pBitStringAux = &pNalCur->sNalData.sVclNal.sSliceBitsRead;
      ctx->uiNalRefIdc = pNalCur->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc;
      pLayerInfo.pPps = pSh->pPps;
      pLayerInfo.pSps = pSh->pSps;
      pLayerInfo.pSubsetSps = pShExt->pSubsetSps;
      pLastSh = pSh;
      ctx->pFmo = &ctx->sFmoList[pSh->iPpsId];
      int32_t iRet = FmoParamUpdate (ctx->pFmo, pLayerInfo.pSps, pLayerInfo.pPps, &ctx->iActiveFmoNum, ctx->pMemAlign);
      if (iRet != ERR_NONE) {
        ctx->iErrorCode |= iRet == ERR_INFO_OUT_OF_MEMORY ? dsOutOfMemory : dsBitstreamError;
        d.Kv ("fmo", iRet);
        d.End();
        ended = true;
        break;
      }
      const bool bFreshSliceAvailable = (iCurrIdD != iLastIdD || iCurrIdQ != iLastIdQ);
      WelsDqLayerDecodeStart (ctx, pNalCur, pLayerInfo.pSps, pLayerInfo.pPps);
      if ((iLastIdD < 0) || (iLastIdD == iCurrIdD)) {
        InitDqLayerInfo (dq_cur, &pLayerInfo, pNalCur, ctx->pDec);
        int skip = 0;
        if (ctx->pPps->bEntropyCodingModeFlag || pSh->pPps->bEntropyCodingModeFlag) skip = 1;
        else if (pSh->eSliceType == B_SLICE) skip = 2;
        else if ((pSh->iMbWidth << 4) > ctx->iImgWidthInPixel || (pSh->iMbHeight << 4) > ctx->iImgHeightInPixel) skip = 3;
        if (skip != 0) {
          d.Kv ("skip", skip);
        } else {
          // Subclause 8.2.5.2, gaps in frame_num.
          int gap = 0;
          if (!dq_cur->sLayerInfo.pSps->bGapsInFrameNumValueAllowedFlag) {
            const bool kbIdrFlag = dq_cur->sLayerInfo.sNalHeaderExt.bIdrFlag
                                   || (dq_cur->sLayerInfo.sNalHeaderExt.sNalUnitHeader.eNalUnitType == NAL_UNIT_CODED_SLICE_IDR);
            const int32_t iPrevFrameNum = ctx->pLastDecPicInfo->iPrevFrameNum;
            if (!kbIdrFlag && pSh->iFrameNum != iPrevFrameNum
                && pSh->iFrameNum != ((iPrevFrameNum + 1) & ((1 << dq_cur->sLayerInfo.pSps->uiLog2MaxFrameNum) - 1))) {
              gap = 1;
              bAllRefComplete = false;
              ctx->iErrorCode |= dsRefLost;
            }
          }
          int32_t iRpl = -1;
          if (iCurrIdD == kuiDependencyIdMax && iCurrIdQ == BASE_QUALITY_ID) {
            iRpl = InitRefPicList (ctx, ctx->uiNalRefIdc, pSh->iPicOrderCntLsb);
            if (iRpl) {
              ctx->bRPLRError = true;
              bAllRefComplete = false;
              // HandleReferenceLost.
              if (0 == uiTid || 1 == uiTid) ctx->bReferenceLostAtT0Flag = true;
              ctx->iErrorCode |= dsRefLost;
            }
          }
          d.Kv ("gap", gap);
          DumpRefList (iRpl);
          iRet = WelsDecodeSlice (ctx, bFreshSliceAvailable, pNalCur);
          DumpSlice (iRet);
          parsed = true;
          if (iRet != ERR_NONE) {
            bAllRefComplete = false;
            // HandleReferenceLostL0.
            if (0 == uiTid) ctx->bReferenceLostAtT0Flag = true;
            ctx->iErrorCode |= dsBitstreamError;
          }
          if (bReconstructSlice) {
            target = true;
            // WelsDecodeConstructSlice (with its HandleReferenceLostL0); a
            // reconstruction error ends DecodeCurrentAccessUnit.
            if (ReconSlice() != ERR_NONE) {
              if (0 == uiTid) ctx->bReferenceLostAtT0Flag = true;
              ctx->iErrorCode |= dsBitstreamError;
              ctx->pDec->bIsComplete = false;
              d.End();
              ended = true;
              break;
            }
          }
          if (bAllRefComplete && ctx->eSliceType != I_SLICE) {
            if (ctx->sRefPic.uiRefCount[LIST_0] > 0) {
              bAllRefComplete &= CheckRefPicturesComplete (ctx);
            } else {
              bAllRefComplete = false;
            }
          }
          d.Kv ("arc", bAllRefComplete);
        }
      } else {
        d.Kv ("skip", 5);
      }
      d.End();
      iLastIdD = iCurrIdD;
      iLastIdQ = iCurrIdQ;
    }
    if (parsed) {
      PPicture pic = ctx->pDec;
      if (!ended) {
        pic->bIsComplete = bAllRefComplete;
        if (!pic->bIsComplete) ctx->iErrorCode |= dsDataErrorConcealed;
        if (target) {
          // DecodeFrameConstruction's share: a complete, error free IDR picture.
          PDqLayer pCurDq = ctx->pCurDqLayer;
          if (ctx->iTotalNumMbRec == pCurDq->iMbWidth * pCurDq->iMbHeight
              && pCurDq->sLayerInfo.sNalHeaderExt.bIdrFlag && ctx->iErrorCode == dsErrorFree)
            pic->bIsComplete = true;
        }
      }
      SHA1Context c;
      SHA1Reset (&c);
      for (int p = 0; p < 3; ++p) {
        const int w = p ? pic->iWidthInPixel >> 1 : pic->iWidthInPixel;
        const int h = p ? pic->iHeightInPixel >> 1 : pic->iHeightInPixel;
        for (int r = 0; r < h; ++r) SHA1Input (&c, pic->pData[p] + r * pic->iLinesize[p], (unsigned)w);
      }
      d.Tag ("Y");
      d.Kv ("au", au);
      d.s += " pic=";
      Pic (pic);
      d.s += " sha=" + Sha1Final (c);
      d.End();
      if (target && !ended) {
        ctx->iTotalNumMbRec = 0;
        ctx->pLastDecPicInfo->pPreviousDecodedPictureInDpb = pic;
        ctx->bUsedAsRef = ctx->uiNalRefIdc > 0;
        if (ctx->bUsedAsRef) {
          for (int32_t listIdx = LIST_0; listIdx < LIST_A; ++listIdx) {
            uint32_t i = 0;
            while (i < MAX_DPB_COUNT && ctx->sRefPic.pRefList[listIdx][i]) {
              pic->pRefPic[listIdx][i] = ctx->sRefPic.pRefList[listIdx][i];
              ++i;
            }
          }
          const int32_t iRet = WelsMarkAsRef (ctx);
          if (iRet == ERR_INFO_DUPLICATE_FRAME_NUM) ctx->iErrorCode |= dsBitstreamError;
          ExpandReferencingPicture (pic->pData, pic->iWidthInPixel, pic->iHeightInPixel, pic->iLinesize,
                                    ctx->sExpandPicFunc.pfExpandLumaPicture, ctx->sExpandPicFunc.pfExpandChromaPicture);
          DumpMarking (au, iRet);
        }
      }
    }
    // The frame_num bookkeeping after a dq layer.
    if (!ended && pLastSh != NULL) {
      if (pCurAu->pNalUnitsList[pCurAu->uiStartPos]->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc > 0)
        last.iPrevFrameNum = pLastSh->iFrameNum;
      if (last.bLastHasMmco5) last.iPrevFrameNum = 0;
    }
    ctx->iTotalNumMbRec = 0;
    ctx->pDec = NULL;
  }

  // WelsTargetSliceConstruction (decode_slice.cpp) up to its deblocking. Adds
  // to the slice's "D" line its return value, the macroblocks reconstructed and
  // the SHA-1 of their samples (16x16 luma, 8x8 Cb, 8x8 Cr, in slice order).
  int32_t ReconSlice() {
    PDqLayer pCurDqLayer = ctx->pCurDqLayer;
    PSlice pCurSlice = &pCurDqLayer->sLayerInfo.sSliceInLayer;
    PSliceHeader pSliceHeader = &pCurSlice->sSliceHeaderExt.sSliceHeader;
    const int32_t iTotalMbTargetLayer = pSliceHeader->pSps->uiTotalMbCount;
    const int32_t iCurLayerWidth = pCurDqLayer->iMbWidth << 4;
    const int32_t iCurLayerHeight = pCurDqLayer->iMbHeight << 4;
    PFmo pFmo = ctx->pFmo;
    const int32_t iTotalNumMb = pCurSlice->iTotalMbInCurSlice;
    int32_t iCountNumMb = 0;
    int32_t iRet = ERR_NONE;
    std::string b;
    int32_t n = 0;

    if (!ctx->sSpsPpsCtx.bAvcBasedFlag && iCurLayerWidth != ctx->iCurSeqIntervalMaxPicWidth) {
      iRet = ERR_INFO_WIDTH_MISMATCH;
    } else {
      int32_t iNextMbXyIndex = pSliceHeader->iFirstMbInSlice;
      pCurDqLayer->iMbX = iNextMbXyIndex % pCurDqLayer->iMbWidth;
      pCurDqLayer->iMbY = iNextMbXyIndex / pCurDqLayer->iMbWidth;
      pCurDqLayer->iMbXyIndex = iNextMbXyIndex;
      if (0 == iNextMbXyIndex) {
        pCurDqLayer->pDec->iSpsId = ctx->pSps->iSpsId;
        pCurDqLayer->pDec->iPpsId = ctx->pPps->iPpsId;
        pCurDqLayer->pDec->uiQualityId = pCurDqLayer->sLayerInfo.sNalHeaderExt.uiQualityId;
      }
      do {
        if (iCountNumMb >= iTotalNumMb) break;
        if (WelsTargetMbConstruction (ctx)) {
          iRet = ERR_INFO_MB_RECON_FAIL;
          break;
        }
        RecordSamples (b, pCurDqLayer->pDec, pCurDqLayer->iMbX, pCurDqLayer->iMbY);
        ++n;
        ++iCountNumMb;
        if (!pCurDqLayer->pMbCorrectlyDecodedFlag[iNextMbXyIndex]) {
          pCurDqLayer->pMbCorrectlyDecodedFlag[iNextMbXyIndex] = true;
          ctx->pDec->iMbEcedPropNum += (pCurDqLayer->pMbRefConcealedFlag[iNextMbXyIndex] ? 1 : 0);
          ++ctx->iTotalNumMbRec;
        }
        if (ctx->iTotalNumMbRec > iTotalMbTargetLayer) {
          iRet = ERR_INFO_MB_NUM_EXCEED_FAIL;
          break;
        }
        if (pSliceHeader->pPps->uiNumSliceGroups > 1) {
          iNextMbXyIndex = FmoNextMb (pFmo, iNextMbXyIndex);
        } else {
          ++iNextMbXyIndex;
        }
        if (-1 == iNextMbXyIndex || iNextMbXyIndex >= iTotalMbTargetLayer) break;
        pCurDqLayer->iMbX = iNextMbXyIndex % pCurDqLayer->iMbWidth;
        pCurDqLayer->iMbY = iNextMbXyIndex / pCurDqLayer->iMbWidth;
        pCurDqLayer->iMbXyIndex = iNextMbXyIndex;
      } while (1);
      if (iRet == ERR_NONE) {
        ctx->pDec->iWidthInPixel = iCurLayerWidth;
        ctx->pDec->iHeightInPixel = iCurLayerHeight;
      }
    }
    d.Kv ("rec", iRet);
    d.Kv ("rmbs", n);
    d.s += " rsha=" + Sha1Hex (b);
    return iRet;
  }

  static void RecordSamples (std::string& b, PPicture pic, int32_t iMbX, int32_t iMbY) {
    for (int p = 0; p < 3; ++p) {
      const int n = p ? 8 : 16;
      const int32_t s = pic->iLinesize[p];
      const uint8_t* q = pic->pData[p] + (iMbY * n) * s + iMbX * n;
      for (int r = 0; r < n; ++r) b.append ((const char*) (q + r * s), (size_t)n);
    }
  }

  // The rest of a parsed slice's "D" line: the parser's end state and the
  // records of the macroblocks it reached, first to iMbXyIndex (the last one
  // parsed, or the one that failed) in slice order.
  void DumpSlice (int32_t iRet) {
    PDqLayer dq = ctx->pCurDqLayer;
    PSlice pSlice = &dq->sLayerInfo.sSliceInLayer;
    PSliceHeader pSh = &pSlice->sSliceHeaderExt.sSliceHeader;
    PBitStringAux pBs = dq->pBitStringAux;
    d.Kv ("ret", iRet);
    d.Kv ("tot", pSlice->iTotalMbInCurSlice);
    d.Kv ("end", dq->iMbXyIndex);
    d.Kv ("cur", (long long) (pBs->pCurBuf - pBs->pStartBuf));
    d.Kv ("left", pBs->iLeftBits);
    d.Kv ("cb", pBs->uiCurBits);
    d.Kv ("qp", pSlice->iLastMbQp);
    d.Kv ("run", pSlice->iMbSkipRun);
    d.Kv ("conc", ctx->bMbRefConcealed);
    d.Kv ("err", ctx->iErrorCode);
    const int32_t kiTotal = (int32_t)pSh->pSps->uiTotalMbCount;
    std::string b;
    int32_t n = 0;
    for (int32_t xy = pSh->iFirstMbInSlice; xy >= 0 && xy < kiTotal && n < kiTotal;) {
      Record (b, dq, ctx->pDec, xy);
      ++n;
      if (xy == dq->iMbXyIndex) break;
      xy = pSh->pPps->uiNumSliceGroups > 1 ? FmoNextMb (ctx->pFmo, xy) : xy + 1;
    }
    d.Kv ("mbs", n);
    d.s += " sha=" + Sha1Hex (b);
    ++slices;
    mbRecs += n;
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

bool MbDumpChunks (const Chunks& chunks, std::string& out, int& slices, int& mbs) {
  Driver drv;
  drv.mbs = true;
  if (!drv.Init()) {
    drv.Close();
    return false;
  }
  int i = 0;
  for (const std::vector<uint8_t>& c : chunks) drv.Chunk (c.data(), (int32_t)c.size(), i++);
  drv.Flush();
  out.swap (drv.d.s);
  slices = drv.slices;
  mbs = drv.mbRecs;
  drv.Close();
  return true;
}

bool MbDump (const std::vector<uint8_t>& s, std::string& out, int& slices, int& mbs) {
  return MbDumpChunks (SplitChunks (s), out, slices, mbs);
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
