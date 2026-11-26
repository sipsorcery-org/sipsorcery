// islice_encoder.cpp: fixtures for the ported I-slice macroblock coding (E4a).
//
// Built with the encoder include paths only, like kernels_encoder.cpp (the
// h264ref_enc_kernels object library in CMakeLists.txt).
//
// C# side, one test class per group (test/SIPSorcery.H264.UnitTest):
//   "md"                the intra halves of md.cpp: FillNeighborCacheIntra,
//                       MdInterAnalysisVaaInfo_c (with ties),
//                       AnalysisVaaInfoIntra_c and MdIntraAnalysisVaaInfo at
//                       its threshold (md_unittest.cs).
//   "svc_base_layer_md" PredIntra4x4Mode (svc_base_layer_md_unittest.cs).
//   "picture_handle"    the layout AllocPicture makes
//                       (picture_handle_unittest.cs).
//   "encoder_ext"       the stride tables of AllocStrideTables
//                       (encoder_ext_unittest.cs).
//   "svc_encode_slice"  InitSliceBoundaryInfo and UpdateMbNeighbor over the
//                       slice maps, and
//                       WelsISliceMdEnc over whole frames: every slice of
//                       the frame coded in turn on a hand-built context
//                       (svc_encode_slice_unittest.cs).
//
// The I-slice context is what WelsInitCurrentLayer, InitDqLayers,
// InitMbInfo and PreprocessSliceCoding would set for a single spatial layer
// with rate control off: the function list from the individual init
// functions (InitFunctionPointers itself also builds the parameter set
// strategy, which comes with E4b), SetFastCodingFunc or SetNormalCodingFunc
// (static inline upstream, so replicated below), the pictures from
// AllocPicture, and the stride tables from AllocStrideTables plus the
// encoder offsets InitDqLayers adds. Pictures, macroblock arrays and
// bitstream buffers start random, and the hashes cover them whole, so a
// stray write anywhere counts. The C# side (IsliceFixtures.cs) must draw
// every value in the same order as below.

#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "fixture.h"

#include "wels_common_defs.h"
#include "decode_mb_aux.h"
#include "encode_mb_aux.h"
#include "encoder.h"
#include "encoder_context.h"
#include "get_intra_predictor.h"
#include "md.h"
#include "picture_handle.h"
#include "rc.h"
#include "sample.h"
#include "set_mb_syn_cavlc.h"
#include "svc_base_layer_md.h"
#include "svc_enc_frame.h"
#include "svc_enc_golomb.h"
#include "svc_enc_slice_segment.h"
#include "svc_encode_slice.h"
#include "svc_mode_decision.h"
#include "svc_set_mb_syn.h"
#include "wels_func_ptr_def.h"

namespace WelsEnc {
// Defined in encoder_ext.cpp, svc_enc_slice_segment.cpp, svc_base_layer_md.cpp
// and md.cpp, but no header declares them.
int32_t AllocStrideTables (sWelsEncCtx** ppCtx, const int32_t kiNumSpatialLayers);
int32_t InitSliceSegment (SDqLayer* pCurDq, CMemoryAlign* pMa, SSliceArgument* pSliceArgument,
                          const int32_t kiMbWidth, const int32_t kiMbHeight);
void UninitSliceSegment (SDqLayer* pCurDq, CMemoryAlign* pMa);
int32_t PredIntra4x4Mode (int8_t* pIntraPredMode, int32_t iIdx4);
int32_t AnalysisVaaInfoIntra_c (uint8_t* pDataY, const int32_t kiLineSize);
}  // namespace WelsEnc

using namespace fixture;
using namespace WelsEnc;

namespace {

std::string Fmt (const char* fmt, ...) {
  char s[256];
  va_list ap;
  va_start (ap, fmt);
  vsnprintf (s, sizeof s, fmt, ap);
  va_end (ap);
  return s;
}

// encoder_ext.cpp's static inline SetFastCodingFunc and SetNormalCodingFunc.
void SetFastCodingFunc (SWelsFuncPtrList* pFuncList) {
  pFuncList->pfIntraFineMd = WelsMdIntraFinePartitionVaa;
  pFuncList->sSampleDealingFuncs.pfMdCost = pFuncList->sSampleDealingFuncs.pfSampleSad;
  pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3 = pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3Sad;
  pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3 = pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3Sad;
}
void SetNormalCodingFunc (SWelsFuncPtrList* pFuncList) {
  pFuncList->pfIntraFineMd = WelsMdIntraFinePartition;
  pFuncList->sSampleDealingFuncs.pfMdCost = pFuncList->sSampleDealingFuncs.pfSampleSatd;
  pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3 =
    pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3Satd;
  pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3 =
    pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3Satd;
  pFuncList->sSampleDealingFuncs.pfIntra4x4Combined3 =
    pFuncList->sSampleDealingFuncs.pfIntra4x4Combined3Satd;
}

int32_t Clip255 (int32_t v) {
  return std::min (255, std::max (0, v));
}

// Fills a w x h plane (w, h multiples of 4) with content k: 0 noise, 1 a
// gentle gradient with a little noise, 2 4x4 blocks that are each flat or
// ramp one way (so every intra mode has something to win on).
void FillPlane (Rng& g, uint8_t* p, int32_t s, int32_t w, int32_t h, int k) {
  if (k == 0) {
    for (int32_t y = 0; y < h; y++)
      for (int32_t x = 0; x < w; x++) p[y * s + x] = g.NextByte();
  } else if (k == 1) {
    const int32_t a = (int32_t) (g.Next() % 9) - 4;
    const int32_t b = (int32_t) (g.Next() % 9) - 4;
    const int32_t c = g.NextByte();
    for (int32_t y = 0; y < h; y++)
      for (int32_t x = 0; x < w; x++)
        p[y * s + x] = (uint8_t)Clip255 (c + (a * x + b * y) / 2 + (int32_t) (g.Next() & 3));
  } else {
    for (int32_t by = 0; by < h; by += 4)
      for (int32_t bx = 0; bx < w; bx += 4) {
        const int32_t v = g.NextByte();
        const int32_t dir = (int32_t) (g.Next() % 4);
        const int32_t step = (int32_t) (g.Next() % 33) - 16;
        for (int32_t j = 0; j < 4; j++)
          for (int32_t i = 0; i < 4; i++) {
            const int32_t d = dir == 0 ? 0 : dir == 1 ? i : dir == 2 ? j : i + j;
            p[(by + j) * s + bx + i] = (uint8_t)Clip255 (v + step * d);
          }
      }
  }
}

// The bytes AllocPicture gives pBuffer.
int32_t PictureBufferSize (const SPicture* pPic) {
  const int32_t kiHeight = WELS_ALIGN (pPic->iHeightInPixel, MB_HEIGHT_LUMA) + (PADDING_LENGTH << 1);
  return pPic->iLineSize[0] * kiHeight + 2 * pPic->iLineSize[1] * (kiHeight >> 1);
}

// Where p points: 1 + its offset in [base, base + n), or 0 if it is null, or
// -1 if it points elsewhere (whose address would differ between builds).
int32_t Region (const void* p, const void* base, int32_t n) {
  if (p == NULL) return 0;
  const intptr_t d = (const uint8_t*)p - (const uint8_t*)base;
  return (d >= 0 && d < n) ? (int32_t)d + 1 : -1;
}

// Slice modes of the fixture: 0 one slice, 1 one per row (raster, MB count 0),
// 2 three raster runs of about a third of the frame each, so slices start
// mid-row.
void SliceArgument (SSliceArgument* pArg, int slicing, int32_t iMbWidth, int32_t iMbHeight) {
  memset (pArg, 0, sizeof *pArg);
  const int32_t kiCount = iMbWidth * iMbHeight;
  if (slicing == 0) {
    pArg->uiSliceMode = SM_SINGLE_SLICE;
    pArg->uiSliceNum = 1;
  } else if (slicing == 1) {
    pArg->uiSliceMode = SM_RASTER_SLICE;
    pArg->uiSliceNum = iMbHeight;
    pArg->uiSliceMbNum[0] = 0;
  } else {
    pArg->uiSliceMode = SM_RASTER_SLICE;
    pArg->uiSliceNum = 3;
    pArg->uiSliceMbNum[0] = kiCount / 3 + 1;
    pArg->uiSliceMbNum[1] = kiCount / 3;
    pArg->uiSliceMbNum[2] = kiCount - pArg->uiSliceMbNum[0] - pArg->uiSliceMbNum[1];
  }
}

// The context WelsISliceMdEnc reads, for one w x h layer (see the top).
struct IsliceCtx {
  CMemoryAlign* pMa;
  SWelsSvcCodingParam* pParam;
  SWelsFuncPtrList* pFuncs;
  SWelsPPS* pPps;
  SWelsSvcRc* pRc;
  sWelsEncCtx* pCtx;
  SDqLayer* pDq;
  SSlice* pSlice;
  SMB* pMbs;
  int8_t* pNzc;
  int8_t* pI4;
  int8_t* pRefIdx;
  SMVUnitXY* pMv;
  int32_t* pSad;
  SPicture* pEncPic;
  SPicture* pDecPic;
  int32_t iMbWidth, iMbHeight, iMbCount, iSliceNum, iBoundaryRet;

  IsliceCtx (int32_t w, int32_t h, int slicing, int fast, int32_t qp, uint8_t uiChromaQpOffset) {
    pMa = new CMemoryAlign (16);
    iMbWidth = (w + 15) >> 4;
    iMbHeight = (h + 15) >> 4;
    iMbCount = iMbWidth * iMbHeight;

    pParam = (SWelsSvcCodingParam*)calloc (1, sizeof (SWelsSvcCodingParam));
    pParam->iSpatialLayerNum = 1;
    pParam->iTemporalLayerNum = 1;
    pParam->sSpatialLayers[0].iVideoWidth = w;
    pParam->sSpatialLayers[0].iVideoHeight = h;
    pParam->sSpatialLayers[0].iDLayerQp = qp;

    pFuncs = (SWelsFuncPtrList*)calloc (1, sizeof (SWelsFuncPtrList));
    pFuncs->pfSetMemZeroSize8 = WelsSetMemZero_c;
    pFuncs->pfSetMemZeroSize64Aligned16 = WelsSetMemZero_c;
    pFuncs->pfSetMemZeroSize64 = WelsSetMemZero_c;
    WelsInitIntraPredFuncs (pFuncs, 0);
    WelsInitSampleSadFunc (pFuncs, 0);
    pFuncs->pfMdBackgroundInfoUpdate = WelsMdUpdateBGDInfo;
    InitIntraAnalysisVaaInfo (pFuncs, 0);
    InitCoeffFunc (pFuncs, 0, 0);
    WelsInitEncodingFuncs (pFuncs, 0);
    WelsInitReconstructionFuncs (pFuncs, 0);
    if (fast)
      SetFastCodingFunc (pFuncs);
    else
      SetNormalCodingFunc (pFuncs);

    pPps = (SWelsPPS*)calloc (1, sizeof (SWelsPPS));
    pPps->uiChromaQpIndexOffset = uiChromaQpOffset;
    pRc = (SWelsSvcRc*)calloc (1, sizeof (SWelsSvcRc));

    pDq = (SDqLayer*)calloc (1, sizeof (SDqLayer));
    pDq->iMbWidth = iMbWidth;
    pDq->iMbHeight = iMbHeight;
    pDq->sLayerInfo.pPpsP = pPps;

    pCtx = (sWelsEncCtx*)calloc (1, sizeof (sWelsEncCtx));
    pCtx->pSvcParam = pParam;
    pCtx->pFuncList = pFuncs;
    pCtx->pCurDqLayer = pDq;
    pCtx->pMemAlign = pMa;
    pCtx->pWelsSvcRc = pRc;
    pCtx->eSliceType = I_SLICE;
    pCtx->iGlobalQp = qp;
    WelsRcInitFuncPointers (pCtx, RC_OFF_MODE);
    AllocStrideTables (&pCtx, 1);

    pEncPic = AllocPicture (pMa, w, h, false, 0);
    pDecPic = AllocPicture (pMa, w, h, true, 0);
    WelsGetEncBlockStrideOffset (pCtx->pStrideTab->pStrideEncBlockOffset[0], pEncPic->iLineSize[0],
                                 pEncPic->iLineSize[1]);
    pCtx->pEncPic = pEncPic;
    pCtx->pDecPic = pDecPic;
    pDq->pDecPic = pDecPic;
    for (int i = 0; i < 3; i++) {
      pDq->pEncData[i] = pEncPic->pData[i];
      pDq->iEncStride[i] = pEncPic->iLineSize[i];
      pDq->pCsData[i] = pDecPic->pData[i];
      pDq->iCsStride[i] = pDecPic->iLineSize[i];
    }

    SSliceArgument sArg;
    SliceArgument (&sArg, slicing, iMbWidth, iMbHeight);
    InitSliceSegment (pDq, pMa, &sArg, iMbWidth, iMbHeight);
    // InitSliceInLayer's boundary tables (sized by pointers, as upstream).
    iSliceNum = pDq->sSliceEncCtx.iSliceNumInFrame;
    pDq->iMaxSliceNum = iSliceNum;
    pDq->pFirstMbIdxOfSlice = (int32_t*)pMa->WelsMallocz (sizeof (int32_t*) * iSliceNum, "pFirstMbIdxOfSlice");
    pDq->pCountMbNumInSlice = (int32_t*)pMa->WelsMallocz (sizeof (int32_t*) * iSliceNum, "pCountMbNumInSlice");
    iBoundaryRet = InitSliceBoundaryInfo (pDq, &sArg, iSliceNum);

    pMbs = (SMB*)calloc (iMbCount, sizeof (SMB));
    pNzc = (int8_t*)calloc (iMbCount, MB_LUMA_CHROMA_BLOCK4x4_NUM);
    pI4 = (int8_t*)calloc (iMbCount, INTRA_4x4_MODE_NUM);
    pRefIdx = (int8_t*)calloc (iMbCount, MB_BLOCK8x8_NUM);
    pMv = (SMVUnitXY*)calloc (iMbCount * MB_BLOCK4x4_NUM, sizeof (SMVUnitXY));
    pSad = (int32_t*)calloc (iMbCount, sizeof (int32_t));
    for (int32_t i = 0; i < iMbCount; i++) {
      SMB* pMb = &pMbs[i];
      pMb->iMbX = pCtx->pStrideTab->pMbIndexX[0][i];
      pMb->iMbY = pCtx->pStrideTab->pMbIndexY[0][i];
      pMb->iMbXY = i;
      pMb->sMv = &pMv[i * MB_BLOCK4x4_NUM];
      pMb->pRefIndex = &pRefIdx[i * MB_BLOCK8x8_NUM];
      pMb->pSadCost = &pSad[i];
      pMb->pIntra4x4PredMode = &pI4[i * INTRA_4x4_MODE_NUM];
      pMb->pNonZeroCount = &pNzc[i * MB_LUMA_CHROMA_BLOCK4x4_NUM];
      UpdateMbNeighbor (pDq, pMb, iMbWidth, WelsMbToSliceIdc (pDq, i));
    }
    pDq->sMbDataP = pMbs;

    pSlice = (SSlice*)calloc (1, sizeof (SSlice));
    pSlice->sSliceHeaderExt.sSliceHeader.eSliceType = I_SLICE;
    AllocMbCacheAligned (&pSlice->sMbCacheInfo, pMa);
  }

  // The random starting state: both pictures whole, the macroblock arrays,
  // the reference QPs, then the source content in the macroblock area.
  void Fill (Rng& g, int content) {
    g.Fill (pEncPic->pBuffer, PictureBufferSize (pEncPic));
    g.Fill (pDecPic->pBuffer, PictureBufferSize (pDecPic));
    g.Fill ((uint8_t*)pNzc, iMbCount * MB_LUMA_CHROMA_BLOCK4x4_NUM);
    g.Fill ((uint8_t*)pI4, iMbCount * INTRA_4x4_MODE_NUM);
    g.Fill ((uint8_t*)pRefIdx, iMbCount * MB_BLOCK8x8_NUM);
    g.Fill ((uint8_t*)pMv, iMbCount * MB_BLOCK4x4_NUM * sizeof (SMVUnitXY));
    g.Fill ((uint8_t*)pSad, iMbCount * sizeof (int32_t));
    g.Fill (pDecPic->pRefMbQp, iMbCount);
    const int32_t kiW = iMbWidth << 4, kiH = iMbHeight << 4;
    FillPlane (g, pEncPic->pData[0], pEncPic->iLineSize[0], kiW, kiH, content);
    FillPlane (g, pEncPic->pData[1], pEncPic->iLineSize[1], kiW >> 1, kiH >> 1, content);
    FillPlane (g, pEncPic->pData[2], pEncPic->iLineSize[2], kiW >> 1, kiH >> 1, content);
  }

  // The macroblock cache after a slice: its arrays, modes and buffers, and
  // where its prediction pointers point.
  void HashCache (Hasher& h) {
    SMbCache* c = &pSlice->sMbCacheInfo;
    h.Add (c->iNonZeroCoeffCount, sizeof c->iNonZeroCoeffCount);
    h.Add (c->iIntraPredMode, sizeof c->iIntraPredMode);
    h.AddI32 (c->uiLumaI16x16Mode);
    h.AddI32 (c->uiChmaI8x8Mode);
    h.AddI32 (c->uiNeighborIntra);
    h.Add (c->pMemPredMb, 2 * 256);
    h.Add (c->pMemPredBlk4, 2 * 16);
    h.Add (c->pCoeffLevel, MB_COEFF_LIST_SIZE * sizeof (int16_t));
    h.Add (c->pSkipMb, 384);
    h.Add (c->pBufferInterPredMe, 4 * 640);
    h.Add (c->pPrevIntra4x4PredModeFlag, 16 * sizeof (bool));
    h.Add (c->pRemIntra4x4PredModeFlag, 16);
    h.Add (c->pDct, sizeof (SDCTCoeff));
    h.AddI32 (Region (c->pMemPredLuma, c->pMemPredMb, 512));
    h.AddI32 (Region (c->pMemPredChroma, c->pMemPredMb, 512));
    h.AddI32 (Region (c->pBestPredIntraChroma, c->pMemPredMb, 512));
    h.AddI32 (Region (c->pBestPredI4x4Blk4, c->pMemPredBlk4, 32));
  }

  // The frame after all its slices: the macroblocks' fields and arrays, the
  // reference QPs and both pictures whole.
  void HashFrame (Hasher& h) {
    for (int32_t i = 0; i < iMbCount; i++) {
      const SMB* pMb = &pMbs[i];
      h.AddI32 ((int32_t)pMb->uiMbType);
      h.AddI32 (pMb->uiCbp);
      h.AddI32 (pMb->uiLumaQp);
      h.AddI32 (pMb->uiChromaQp);
      h.AddI32 (pMb->uiSliceIdc);
      h.AddI32 (pMb->uiNeighborAvail);
      h.AddI32 ((int32_t)pMb->uiChromPredMode);
      h.AddI32 (pMb->iLumaDQp);
      h.AddI32 (pMb->iCbpDc);
    }
    h.Add (pNzc, iMbCount * MB_LUMA_CHROMA_BLOCK4x4_NUM);
    h.Add (pI4, iMbCount * INTRA_4x4_MODE_NUM);
    h.Add (pRefIdx, iMbCount * MB_BLOCK8x8_NUM);
    h.Add (pMv, iMbCount * MB_BLOCK4x4_NUM * sizeof (SMVUnitXY));
    h.Add (pSad, iMbCount * sizeof (int32_t));
    h.Add (pDecPic->pRefMbQp, iMbCount);
    h.Add (pEncPic->pBuffer, PictureBufferSize (pEncPic));
    h.Add (pDecPic->pBuffer, PictureBufferSize (pDecPic));
  }

  ~IsliceCtx() {
    FreeMbCache (&pSlice->sMbCacheInfo, pMa);
    free (pSlice);
    free (pSad);
    free (pMv);
    free (pRefIdx);
    free (pI4);
    free (pNzc);
    free (pMbs);
    pMa->WelsFree (pDq->pCountMbNumInSlice, "pCountMbNumInSlice");
    pMa->WelsFree (pDq->pFirstMbIdxOfSlice, "pFirstMbIdxOfSlice");
    UninitSliceSegment (pDq, pMa);
    FreePicture (pMa, &pDecPic);
    FreePicture (pMa, &pEncPic);
    pMa->WelsFree (pCtx->pStrideTab->pStrideDecBlockOffset[0][1], "pBase");
    pMa->WelsFree (pCtx->pStrideTab, "SStrideTables");
    free (pCtx);
    free (pDq);
    free (pRc);
    free (pPps);
    free (pFuncs);
    free (pParam);
    delete pMa;
  }
};

struct FrameSize {
  int32_t w, h;
};
const FrameSize kSizes[] = {{16, 16}, {40, 24}, {80, 48}, {176, 144}};

}  // namespace

void KernelsMdIntra() {
  // AnalysisVaaInfoIntra_c on a 16x16 block of each content kind, in a
  // random 48-stride buffer.
  for (int content = 0; content < 3; content++)
    for (int seed = 0; seed < 8; seed++) {
      Rng g (seed);
      std::vector<uint8_t> buf (48 * 20);
      g.Fill (buf.data(), buf.size());
      FillPlane (g, buf.data() + 2 * 48 + 8, 48, 16, 16, content);
      Hasher h;
      h.AddI32 (AnalysisVaaInfoIntra_c (buf.data() + 2 * 48 + 8, 48));
      Emit ("md", "AnalysisVaaInfoIntra_c", Fmt ("content=%d,seed=%d", content, seed), h);
    }

  // MdInterAnalysisVaaInfo_c on four 8x8 SADs below range (range 1 makes them
  // all 0, the flat case).
  for (int32_t range : {1, 64, 1024, 16384, 65536})
    for (int seed = 0; seed < 8; seed++) {
      Rng g (seed);
      int32_t iSad[4];
      for (int i = 0; i < 4; i++) iSad[i] = (int32_t) (g.Next() % (uint32_t)range);
      Hasher h;
      h.AddI32 (MdInterAnalysisVaaInfo_c (iSad));
      Emit ("md", "MdInterAnalysisVaaInfo_c", Fmt ("range=%d,seed=%d", range, seed), h);
    }

  // MdInterAnalysisVaaInfo_c with blocks k and k + 3 equal to the average and
  // the other two d either side, d large enough to pass the variance
  // threshold, so each sign bit's > meets a tie.
  for (int k = 0; k < 4; k++)
    for (int seed = 0; seed < 2; seed++) {
      Rng g (seed);
      const int32_t avg = 4160 + (int32_t) (g.Next() % 4096);
      const int32_t d = 64 * (4 + (int32_t) (g.Next() % 60));
      int32_t iSad[4];
      iSad[k] = avg;
      iSad[(k + 1) % 4] = avg + d;
      iSad[(k + 2) % 4] = avg - d;
      iSad[(k + 3) % 4] = avg;
      Hasher h;
      h.AddI32 (MdInterAnalysisVaaInfo_c (iSad));
      Emit ("md", "MdInterAnalysisVaaInfo_c", Fmt ("tie=%d,seed=%d", k, seed), h);
    }

  // MdIntraAnalysisVaaInfo at its threshold (150): flat 4x4 blocks of base,
  // three of them raised by the offsets that make the variance 149, 150 and
  // 151, starting at sub-block place.
  const int32_t kVarianceOffsets[3][3] = {{1, 7, 11}, {0, 7, 11}, {0, 5, 12}};
  for (int v = 0; v < 3; v++)
    for (int32_t base : {0, 100, 243})
      for (int place = 0; place < 16; place += 5) {
        uint8_t blk[16 * 32];
        memset (blk, 0, sizeof blk);
        for (int k = 0; k < 16; k++) {
          const int r = (k - place + 16) % 16;
          const int32_t e = r < 3 ? kVarianceOffsets[v][r] : 0;
          for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++) blk[((k >> 2) * 4 + j) * 32 + (k & 3) * 4 + i] = (uint8_t) (base + e);
        }
        SWelsFuncPtrList* pFuncs = (SWelsFuncPtrList*)calloc (1, sizeof (SWelsFuncPtrList));
        SDqLayer* pDq = (SDqLayer*)calloc (1, sizeof (SDqLayer));
        sWelsEncCtx* pCtx = (sWelsEncCtx*)calloc (1, sizeof (sWelsEncCtx));
        InitIntraAnalysisVaaInfo (pFuncs, 0);
        pDq->iEncStride[0] = 32;
        pCtx->pFuncList = pFuncs;
        pCtx->pCurDqLayer = pDq;
        Hasher h;
        h.AddI32 (AnalysisVaaInfoIntra_c (blk, 32));
        h.AddI32 (MdIntraAnalysisVaaInfo (pCtx, blk));
        free (pCtx);
        free (pDq);
        free (pFuncs);
        Emit ("md", "MdIntraAnalysisVaaInfo", Fmt ("variance=%d,base=%d,place=%d", 149 + v, base, place), h);
      }

  // FillNeighborCacheIntra for every neighbour set, on the middle macroblock
  // of a frame's second row; neighbours are intra 4x4, intra 16x16 or inter
  // at random, and the cache starts random.
  const uint32_t kTypes[3] = {MB_TYPE_INTRA4x4, MB_TYPE_INTRA16x16, MB_TYPE_16x16};
  for (int32_t iMbWidth : {1, 3, 11})
    for (uint32_t avail = 0; avail < 16; avail++)
      for (int seed = 0; seed < 2; seed++) {
        Rng g (seed);
        const int32_t kiCount = 2 * iMbWidth;
        std::vector<SMB> mbs (kiCount);
        std::vector<int8_t> nzc (kiCount * MB_LUMA_CHROMA_BLOCK4x4_NUM);
        std::vector<int8_t> i4 (kiCount * INTRA_4x4_MODE_NUM);
        memset (mbs.data(), 0, kiCount * sizeof (SMB));
        g.Fill ((uint8_t*)nzc.data(), nzc.size());
        g.Fill ((uint8_t*)i4.data(), i4.size());
        for (int32_t i = 0; i < kiCount; i++) {
          mbs[i].uiMbType = kTypes[g.Next() % 3];
          mbs[i].pNonZeroCount = &nzc[i * MB_LUMA_CHROMA_BLOCK4x4_NUM];
          mbs[i].pIntra4x4PredMode = &i4[i * INTRA_4x4_MODE_NUM];
        }
        SMB* pCur = &mbs[iMbWidth + iMbWidth / 2];
        pCur->uiNeighborAvail = (uint8_t)avail;
        SMbCache cache;
        memset (&cache, 0, sizeof cache);
        g.Fill ((uint8_t*)cache.iNonZeroCoeffCount, sizeof cache.iNonZeroCoeffCount);
        g.Fill ((uint8_t*)cache.iIntraPredMode, sizeof cache.iIntraPredMode);
        FillNeighborCacheIntra (&cache, pCur, iMbWidth);
        Hasher h;
        h.Add (cache.iNonZeroCoeffCount, sizeof cache.iNonZeroCoeffCount);
        h.Add (cache.iIntraPredMode, sizeof cache.iIntraPredMode);
        h.AddI32 (cache.uiNeighborIntra);
        Emit ("md", "FillNeighborCacheIntra", Fmt ("width=%d,avail=%u,seed=%d", iMbWidth, avail, seed), h);
      }
}

void KernelsSvcBaseLayerMd() {
  // PredIntra4x4Mode at each 4x4 block's place in the 48-entry cache, the
  // modes drawn from -1 (unavailable) to 8.
  for (int seed = 0; seed < 8; seed++) {
    Rng g (seed);
    int8_t iModes[48];
    for (int i = 0; i < 48; i++) iModes[i] = (int8_t) ((int32_t) (g.Next() % 10) - 1);
    Hasher h;
    for (int i = 0; i < 16; i++) h.AddI32 (PredIntra4x4Mode (iModes, g_kuiCache48CountScan4Idx[i]));
    Emit ("svc_base_layer_md", "PredIntra4x4Mode", Fmt ("seed=%d", seed), h);
  }
}

void KernelsPictureHandle() {
  // AllocPicture's layout: strides, plane offsets, the fields it sets, and
  // the macroblock arrays it zeroes when asked for them.
  for (const FrameSize& s : {FrameSize{16, 16}, FrameSize{40, 24}, FrameSize{176, 144}, FrameSize{33, 17},
                              FrameSize{1920, 1080}})
    for (int mbinfo = 0; mbinfo < 2; mbinfo++) {
      CMemoryAlign ma (16);
      SPicture* pPic = AllocPicture (&ma, s.w, s.h, mbinfo != 0, 0);
      const int32_t kiMbCount = ((s.w + 15) >> 4) * ((s.h + 15) >> 4);
      Hasher h;
      for (int i = 0; i < 3; i++) {
        h.AddI32 (pPic->iLineSize[i]);
        h.AddI32 ((int32_t) (pPic->pData[i] - pPic->pBuffer));
      }
      h.AddI32 (PictureBufferSize (pPic));
      h.AddI32 (pPic->iWidthInPixel);
      h.AddI32 (pPic->iHeightInPixel);
      h.AddI32 (pPic->iFrameNum);
      h.AddI32 (pPic->bIsLongRef);
      h.AddI32 (pPic->iLongTermPicNum);
      h.AddI32 ((int32_t)pPic->uiRecieveConfirmed);
      h.AddI32 (pPic->iMarkFrameNum);
      h.AddI32 (pPic->uiRefMbType != NULL);
      h.AddI32 (pPic->pRefMbQp != NULL);
      h.AddI32 (pPic->sMvList != NULL);
      h.AddI32 (pPic->pMbSkipSad != NULL);
      if (mbinfo) {
        h.Add (pPic->uiRefMbType, kiMbCount * sizeof (uint32_t));
        h.Add (pPic->pRefMbQp, kiMbCount);
        h.Add (pPic->sMvList, kiMbCount * sizeof (SMVUnitXY));
        h.Add (pPic->pMbSkipSad, kiMbCount * sizeof (int32_t));
      }
      h.AddI32 (pPic->pScreenBlockFeatureStorage != NULL);
      FreePicture (&ma, &pPic);
      h.AddI32 (pPic != NULL);
      h.AddI32 ((int32_t)ma.WelsGetMemoryUsage());
      Emit ("picture_handle", "AllocPicture", Fmt ("w=%d,h=%d,mbinfo=%d", s.w, s.h, mbinfo), h);
    }
}

void KernelsEncoderExt() {
  // AllocStrideTables over growing spatial layer sets with one or two
  // temporal levels: every decoder offset table (or that it is null), the
  // encoder tables (left for InitDqLayers to fill) and the macroblock index
  // maps.
  const FrameSize kLayers[4][4] = {
    {{176, 144}},
    {{88, 72}, {176, 144}},
    {{40, 24}, {80, 48}, {176, 144}},
    {{16, 16}, {40, 24}, {88, 72}, {320, 240}},
  };
  for (int layers = 1; layers <= 4; layers++)
    for (int temporal = 1; temporal <= 2; temporal++) {
      CMemoryAlign ma (16);
      SWelsSvcCodingParam* pParam = (SWelsSvcCodingParam*)calloc (1, sizeof (SWelsSvcCodingParam));
      pParam->iSpatialLayerNum = layers;
      pParam->iTemporalLayerNum = temporal;
      for (int d = 0; d < layers; d++) {
        pParam->sSpatialLayers[d].iVideoWidth = kLayers[layers - 1][d].w;
        pParam->sSpatialLayers[d].iVideoHeight = kLayers[layers - 1][d].h;
      }
      sWelsEncCtx* pCtx = (sWelsEncCtx*)calloc (1, sizeof (sWelsEncCtx));
      pCtx->pMemAlign = &ma;
      pCtx->pSvcParam = pParam;
      Hasher h;
      h.AddI32 (AllocStrideTables (&pCtx, layers));
      SStrideTables* t = pCtx->pStrideTab;
      for (int d = 0; d < MAX_DEPENDENCY_LAYER; d++) {
        for (int tid = 0; tid < 2; tid++) {
          h.AddI32 (t->pStrideDecBlockOffset[d][tid] != NULL);
          if (t->pStrideDecBlockOffset[d][tid] != NULL) h.Add (t->pStrideDecBlockOffset[d][tid], 24 * sizeof (int32_t));
        }
        h.AddI32 (t->pStrideEncBlockOffset[d] != NULL);
        h.AddI32 (t->pMbIndexX[d] != NULL);
        h.AddI32 (t->pMbIndexY[d] != NULL);
        if (d < layers) {
          const int32_t kiCount = ((kLayers[layers - 1][d].w + 15) >> 4) * ((kLayers[layers - 1][d].h + 15) >> 4);
          h.Add (t->pStrideEncBlockOffset[d], 24 * sizeof (int32_t));
          h.Add (t->pMbIndexX[d], kiCount * sizeof (int16_t));
          h.Add (t->pMbIndexY[d], kiCount * sizeof (int16_t));
        }
      }
      ma.WelsFree (t->pStrideDecBlockOffset[0][1], "pBase");
      ma.WelsFree (t, "SStrideTables");
      h.AddI32 ((int32_t)ma.WelsGetMemoryUsage());
      free (pCtx);
      free (pParam);
      Emit ("encoder_ext", "AllocStrideTables", Fmt ("layers=%d,temporal=%d", layers, temporal), h);
    }
}

void KernelsSvcEncodeSlice() {
  // The slice set up of the I-slice context below, over each slice map:
  // InitSliceBoundaryInfo's tables (and WelsGetFirstMbOfSlice over them), and
  // UpdateMbNeighbor's slice and neighbour flags for every macroblock.
  for (const FrameSize& s : kSizes)
    for (int slicing = 0; slicing < 3; slicing++) {
      if (slicing == 2 && ((s.w + 15) >> 4) * ((s.h + 15) >> 4) < 3) continue;
      IsliceCtx c (s.w, s.h, slicing, 0, 26, 0);
      const std::string kase = Fmt ("w=%d,h=%d,slicing=%d", s.w, s.h, slicing);
      Hasher hb;
      hb.AddI32 (c.pDq->sSliceEncCtx.iSliceNumInFrame);
      hb.AddI32 (c.iBoundaryRet);
      for (int32_t i = 0; i < c.iSliceNum; i++) {
        hb.AddI32 (c.pDq->pFirstMbIdxOfSlice[i]);
        hb.AddI32 (c.pDq->pCountMbNumInSlice[i]);
        hb.AddI32 (WelsGetFirstMbOfSlice (c.pDq, i));
      }
      Emit ("svc_encode_slice", "InitSliceBoundaryInfo", kase, hb);
      Hasher h;
      for (int32_t i = 0; i < c.iMbCount; i++) {
        h.AddI32 (c.pMbs[i].iMbX);
        h.AddI32 (c.pMbs[i].iMbY);
        h.AddI32 (c.pMbs[i].uiSliceIdc);
        h.AddI32 (c.pMbs[i].uiNeighborAvail);
      }
      Emit ("svc_encode_slice", "UpdateMbNeighbor", kase, h);
    }

  // WelsISliceMdEnc over each slice of a frame in turn. A row's hash takes,
  // per slice, the return, the slice's bitstream and the macroblock cache,
  // then the whole frame. The small buffer (buf=1) leaves too little room
  // after the first macroblocks, so CheckBitstreamBuffer fails them, the
  // loop retries at higher QPs and gives up past 50.
  const int32_t kQps[6] = {0, 1, 12, 26, 39, 51};
  const uint8_t kOffsets[4] = {0, 6, 12, 250};
  for (int buf = 0; buf < 2; buf++)
    for (const FrameSize& s : kSizes)
      for (int slicing = 0; slicing < 3; slicing++)
        for (int fast = 0; fast < 2; fast++)
          for (int qi = 0; qi < 6; qi++)
            for (int content = 0; content < 3; content++)
              for (int seed = 0; seed < 2; seed++) {
                const int32_t kiMbCount = ((s.w + 15) >> 4) * ((s.h + 15) >> 4);
                if (slicing == 2 && kiMbCount < 3) continue;
                if (seed == 1 && kiMbCount > 15) continue;
                if (buf == 1 && (qi != 2 || content != 0 || seed != 0)) continue;
                const int32_t qp = kQps[qi];
                const uint8_t offset = kOffsets[(qi + seed + content) % 4];
                IsliceCtx c (s.w, s.h, slicing, fast, qp, offset);
                Rng g (seed);
                c.Fill (g, content);
                const int32_t kiLogical = buf ? 1500 : c.iMbCount * 1024 + 4096;
                Hasher h;
                for (int32_t iSlice = 0; iSlice < c.iSliceNum; iSlice++) {
                  std::vector<uint8_t> bits (kiLogical + 64);
                  g.Fill (bits.data(), bits.size());
                  SBitStringAux bs;
                  InitBits (&bs, bits.data(), kiLogical);
                  c.pSlice->pSliceBsa = &bs;
                  c.pSlice->iSliceIdx = iSlice;
                  h.AddI32 (SetSliceBoundaryInfo (c.pDq, c.pSlice, iSlice));
                  c.pSlice->uiLastMbQp = (uint8_t)qp;
                  c.pSlice->iMbSkipRun = 0;
                  h.AddI32 (WelsISliceMdEnc (c.pCtx, c.pSlice));
                  WelsWriteSliceEndSyn (c.pSlice, false);
                  h.AddI32 ((int32_t) (bs.pCurBuf - bs.pStartBuf));
                  h.AddI32 ((int32_t)bs.uiCurBits);
                  h.AddI32 (bs.iLeftBits);
                  h.Add (bits.data(), bits.size());
                  h.AddI32 (c.pSlice->uiLastMbQp);
                  h.AddI32 (c.pSlice->iMbSkipRun);
                  c.HashCache (h);
                }
                c.HashFrame (h);
                Emit ("svc_encode_slice", "WelsISliceMdEnc",
                      Fmt ("w=%d,h=%d,slicing=%d,fast=%d,qp=%d,offset=%d,content=%d,seed=%d,buf=%d", s.w, s.h, slicing,
                           fast, qp, offset, content, seed, buf), h);
              }
}
