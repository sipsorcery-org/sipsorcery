// islice_ctx.h: the hand-built I-slice context and the content helpers that
// islice_encoder.cpp (E4a) and pslice_encoder.cpp (E5a) share.
//
// The I-slice context is what WelsInitCurrentLayer, InitDqLayers,
// InitMbInfo and PreprocessSliceCoding would set for a single spatial layer
// with rate control off: the function list from the individual init
// functions (InitFunctionPointers also builds the parameter set strategy,
// which the I-slice rows predate; the P-slice context calls it),
// SetFastCodingFunc or SetNormalCodingFunc (static inline upstream, so
// replicated here), the pictures from AllocPicture, and the stride tables
// from AllocStrideTables plus the encoder offsets InitDqLayers adds.
// Pictures, macroblock arrays and bitstream buffers start random, and the
// hashes cover them whole, so a stray write anywhere counts. The C# side
// (IsliceFixtures.cs) must draw every value in the same order as here.
//
// Each file that includes this gets its own copy (the helpers live in an
// unnamed namespace), so it is included once, after nothing but the
// standard headers.

#ifndef H264REF_ISLICE_CTX_H
#define H264REF_ISLICE_CTX_H

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

#endif  // H264REF_ISLICE_CTX_H
