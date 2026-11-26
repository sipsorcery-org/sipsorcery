// pslice_encoder.cpp: fixtures for the ported P-slice macroblock coding (E5a).
//
// Built with the encoder include paths only, like islice_encoder.cpp (the
// h264ref_enc_kernels object library in CMakeLists.txt).
//
// C# side, one test class per group (test/SIPSorcery.H264.UnitTest):
//   "mv_pred"           every function of mv_pred.cpp on random caches
//                       (mv_pred_unittest.cs).
//   "md"                MvdCostInit for each MV range of the P rows
//                       (md_unittest.cs).
//   "svc_encode_slice"  WelsCodePSlice over whole frames: every slice of the
//                       frame coded in turn on a hand-built P context
//                       (svc_encode_slice_unittest.cs).
//
// The P-slice context is the I-slice one of islice_ctx.h with what a camera P
// frame adds: the function list from InitFunctionPointers and
// PreprocessSliceCoding, the MVD cost table and MV range RequestMemorySvc
// sets, the reference picture WelsBuildRefList would pick, and the VAA state
// the preprocessing would leave, drawn here instead (E7 ports the
// preprocessing): the 8x8 SADs against the previous source, the background
// flags and the adaptive quantisation offsets. The C# side
// (PsliceFixtures.cs) must draw every value in the same order as below.

#include "islice_ctx.h"

#include "expand_pic.h"
#include "mv_pred.h"
#include "svc_motion_estimate.h"
#include "wels_preprocess.h"

namespace WelsEnc {
// Defined in encoder_ext.cpp, but no header declares it.
void PreprocessSliceCoding (sWelsEncCtx* pCtx);
}  // namespace WelsEnc

namespace {

int32_t Clip3 (int32_t v, int32_t lo, int32_t hi) {
  return std::min (hi, std::max (lo, v));
}

// A uniform draw in [-n, n].
int32_t Noise (Rng& g, int32_t n) {
  return (int32_t) (g.Next() % (uint32_t) (2 * n + 1)) - n;
}

// What a reference picture's coding left in its macroblock arrays, by type.
const uint32_t kRefMbTypesP[6] = {MB_TYPE_SKIP, MB_TYPE_BACKGROUND, MB_TYPE_16x16, MB_TYPE_8x8, MB_TYPE_INTRA4x4,
                                  MB_TYPE_INTRA16x16
                                 };

// The reference indices of a random motion vector cache: unavailable, intra,
// or one of two references.
const int8_t kCacheRefs[6] = {REF_NOT_AVAIL, REF_NOT_IN_LIST, 0, 0, 1, 1};

void FillMvCache (Rng& g, SMVComponentUnit* pMvComp) {
  g.Fill ((uint8_t*)pMvComp->sMotionVectorCache, sizeof pMvComp->sMotionVectorCache);
  for (int i = 0; i < 30; i++) pMvComp->iRefIndexCache[i] = kCacheRefs[g.Next() % 6];
}

void HashMvCache (Hasher& h, const SMVComponentUnit* pMvComp) {
  h.Add (pMvComp->sMotionVectorCache, 29 * sizeof (SMVUnitXY));
  h.Add (pMvComp->iRefIndexCache, 30);
}

// The context WelsCodePSlice reads (see the top), over the I-slice one.
struct PsliceCtx {
  IsliceCtx c;
  SPicture* pRefPic;   // the reference: the previous source coded, then padded
  SPicture* pOrigPic;  // the previous source, the VAA's reference planes
  SVAAFrameInfo* pVaa;
  int32_t* pSad8x8;
  int8_t* pBgFlag;
  int8_t* pAqDeltaQp;
  uint16_t* pMvdCostTable;

  PsliceCtx (int32_t w, int32_t h, int slicing, int complexity, int bgd, int aq, int32_t qp,
             uint8_t uiChromaQpOffset, int32_t iMvRange)
    : c (w, h, slicing, complexity == LOW_COMPLEXITY, qp, uiChromaQpOffset) {
    sWelsEncCtx* pCtx = c.pCtx;
    SWelsSvcCodingParam* pParam = c.pParam;
    pParam->iUsageType = CAMERA_VIDEO_REAL_TIME;
    pParam->iComplexityMode = (ECOMPLEXITY_MODE)complexity;
    pParam->bEnableBackgroundDetection = bgd != 0;
    pParam->bEnableAdaptiveQuant = aq != 0;
    pCtx->eSliceType = P_SLICE;
    c.pSlice->sSliceHeaderExt.sSliceHeader.eSliceType = P_SLICE;

    // The whole function list afresh, as WelsInitEncoderExt and
    // WelsEncoderEncodeExt build it.
    memset (c.pFuncs, 0, sizeof (SWelsFuncPtrList));
    InitFunctionPointers (pCtx, pParam, 0);
    WelsRcInitFuncPointers (pCtx, RC_OFF_MODE);
    c.pRc->iMinQp = 6;
    c.pRc->iMaxQp = 45;

    // RequestMemorySvc's MVD cost table, for an MV range GetMvMvdRange
    // could give.
    pCtx->iMvRange = iMvRange;
    const int32_t kiMvdRange = WELS_MIN ((iMvRange + 1) << 1, CAMERA_MVD_RANGE);
    const int32_t kiMvdInterTableSize = kiMvdRange << 2;
    const int32_t kiMvdInterTableStride = 1 + (kiMvdInterTableSize << 1);
    pCtx->iMvdCostTableSize = kiMvdInterTableSize;
    pCtx->iMvdCostTableStride = kiMvdInterTableStride;
    pMvdCostTable = (uint16_t*)c.pMa->WelsMallocz (52 * kiMvdInterTableStride * sizeof (uint16_t), "pMvdCostTable");
    MvdCostInit (pMvdCostTable, kiMvdInterTableStride);
    pCtx->pMvdCostTable = pMvdCostTable;

    pRefPic = AllocPicture (c.pMa, w, h, true, 0);
    pOrigPic = AllocPicture (c.pMa, w, h, false, 0);
    pCtx->pRefPic = pRefPic;
    c.pDq->pRefPic = pRefPic;

    pSad8x8 = (int32_t*)calloc (c.iMbCount * 4, sizeof (int32_t));
    pBgFlag = (int8_t*)calloc (c.iMbCount, 1);
    pAqDeltaQp = (int8_t*)calloc (c.iMbCount, 1);
    pVaa = (SVAAFrameInfo*)calloc (1, sizeof (SVAAFrameInfo));
    pVaa->sVaaCalcInfo.pSad8x8 = (int32_t (*)[4])pSad8x8;
    pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp = pAqDeltaQp;
    pVaa->iPicWidth = w;
    pVaa->iPicHeight = h;
    pVaa->iPicStride = c.pEncPic->iLineSize[0];
    pVaa->iPicStrideUV = c.pEncPic->iLineSize[1];
    pVaa->pRefY = pOrigPic->pData[0];
    pVaa->pCurY = c.pEncPic->pData[0];
    pVaa->pRefU = pOrigPic->pData[1];
    pVaa->pCurU = c.pEncPic->pData[1];
    pVaa->pRefV = pOrigPic->pData[2];
    pVaa->pCurV = c.pEncPic->pData[2];
    pVaa->pVaaBackgroundMbFlag = pBgFlag;
    pCtx->pVaa = pVaa;

    PreprocessSliceCoding (pCtx);
  }

  // The random starting state: the I-slice context's (both pictures, its
  // macroblock arrays and the reference QPs), the current picture's other
  // macroblock arrays, the reference and previous source pictures whole,
  // then what the reference's coding left (its type, and per macroblock its
  // type, QP, motion vector and skip SAD) and the VAA's flags and offsets.
  // Then the content: the previous source (content k), the current source
  // moved from it (motion m, see below) plus noise, the reference from it
  // plus coding noise, padded as WelsUpdateRefList pads, and the 8x8 SADs
  // VAACalcSad would take of the two sources.
  void Fill (Rng& g, int content, int motion, int32_t noise, int reftype, int32_t qp) {
    const int32_t n = c.iMbCount;
    g.Fill (c.pEncPic->pBuffer, PictureBufferSize (c.pEncPic));
    g.Fill (c.pDecPic->pBuffer, PictureBufferSize (c.pDecPic));
    g.Fill ((uint8_t*)c.pNzc, n * MB_LUMA_CHROMA_BLOCK4x4_NUM);
    g.Fill ((uint8_t*)c.pI4, n * INTRA_4x4_MODE_NUM);
    g.Fill ((uint8_t*)c.pRefIdx, n * MB_BLOCK8x8_NUM);
    g.Fill ((uint8_t*)c.pMv, n * MB_BLOCK4x4_NUM * sizeof (SMVUnitXY));
    g.Fill ((uint8_t*)c.pSad, n * sizeof (int32_t));
    g.Fill (c.pDecPic->pRefMbQp, n);
    g.Fill ((uint8_t*)c.pDecPic->uiRefMbType, n * sizeof (uint32_t));
    g.Fill ((uint8_t*)c.pDecPic->sMvList, n * sizeof (SMVUnitXY));
    g.Fill ((uint8_t*)c.pDecPic->pMbSkipSad, n * sizeof (int32_t));
    g.Fill (pRefPic->pBuffer, PictureBufferSize (pRefPic));
    g.Fill (pOrigPic->pBuffer, PictureBufferSize (pOrigPic));

    pRefPic->iPictureType = reftype ? P_SLICE : I_SLICE;
    for (int32_t i = 0; i < n; i++) {
      const uint32_t kuiType = kRefMbTypesP[g.Next() % 6];
      pRefPic->uiRefMbType[i] = reftype ? kuiType : (kuiType == MB_TYPE_INTRA16x16 ? MB_TYPE_INTRA16x16 : MB_TYPE_INTRA4x4);
      pRefPic->pRefMbQp[i] = (uint8_t)Clip3 (qp + Noise (g, 8), 0, 51);
      pRefPic->sMvList[i].iMvX = (int16_t)Noise (g, 64);
      pRefPic->sMvList[i].iMvY = (int16_t)Noise (g, 64);
      pRefPic->pMbSkipSad[i] = (int32_t) (g.Next() % 1024);
    }
    for (int32_t i = 0; i < n; i++) pBgFlag[i] = (int8_t) (g.Next() & 1);
    for (int32_t i = 0; i < n; i++) pAqDeltaQp[i] = (int8_t)Noise (g, 6);

    const int32_t kiW = c.iMbWidth << 4, kiH = c.iMbHeight << 4;
    for (int p = 0; p < 3; p++)
      FillPlane (g, pOrigPic->pData[p], pOrigPic->iLineSize[p], p ? kiW >> 1 : kiW, p ? kiH >> 1 : kiH, content);

    // The motion: 0 none, 1 one shift of the whole frame, 2 that blended
    // half a pixel right, down or both, 3 a shift per 8x8 luma block, 4 new
    // content (no motion to find). Chroma moves half as far.
    int32_t dx = 0, dy = 0, hx = 0, hy = 0;
    if (motion == 1 || motion == 2) {
      dx = Noise (g, 12);
      dy = Noise (g, 12);
    }
    if (motion == 2) {
      const int32_t k = (int32_t) (g.Next() % 3);
      hx = k != 1;
      hy = k != 0;
    }
    const int32_t kiBlocksX = kiW >> 3, kiBlocksY = kiH >> 3;
    std::vector<int32_t> blockMv (motion == 3 ? kiBlocksX * kiBlocksY * 2 : 0);
    for (size_t i = 0; i < blockMv.size(); i++) blockMv[i] = Noise (g, 4);

    for (int p = 0; p < 3; p++) {
      const int32_t kiPw = p ? kiW >> 1 : kiW, kiPh = p ? kiH >> 1 : kiH;
      uint8_t* pSrc = c.pEncPic->pData[p];
      const int32_t kiSrcStride = c.pEncPic->iLineSize[p];
      const uint8_t* pOrg = pOrigPic->pData[p];
      const int32_t kiOrgStride = pOrigPic->iLineSize[p];
      if (motion == 4) {
        FillPlane (g, pSrc, kiSrcStride, kiPw, kiPh, content);
        continue;
      }
      for (int32_t y = 0; y < kiPh; y++)
        for (int32_t x = 0; x < kiPw; x++) {
          int32_t mx = dx, my = dy;
          if (motion == 3) {
            const int32_t b = ((p ? y << 1 : y) >> 3) * kiBlocksX + ((p ? x << 1 : x) >> 3);
            mx = blockMv[2 * b];
            my = blockMv[2 * b + 1];
          }
          if (p) {
            mx >>= 1;
            my >>= 1;
          }
          const int32_t sx = Clip3 (x + mx, 0, kiPw - 1), sy = Clip3 (y + my, 0, kiPh - 1);
          int32_t v = pOrg[sy * kiOrgStride + sx];
          if (hx | hy) {
            const int32_t sx2 = Clip3 (sx + hx, 0, kiPw - 1), sy2 = Clip3 (sy + hy, 0, kiPh - 1);
            v = (v + pOrg[sy2 * kiOrgStride + sx2] + 1) >> 1;
          }
          pSrc[y * kiSrcStride + x] = (uint8_t)Clip255 (v + Noise (g, noise));
        }
    }

    for (int p = 0; p < 3; p++) {
      const int32_t kiPw = p ? kiW >> 1 : kiW, kiPh = p ? kiH >> 1 : kiH;
      uint8_t* pRef = pRefPic->pData[p];
      const uint8_t* pOrg = pOrigPic->pData[p];
      for (int32_t y = 0; y < kiPh; y++)
        for (int32_t x = 0; x < kiPw; x++)
          pRef[y * pRefPic->iLineSize[p] + x] =
            (uint8_t)Clip255 (pOrg[y * pOrigPic->iLineSize[p] + x] + Noise (g, noise >> 1));
    }
    ExpandReferencingPicture (pRefPic->pData, pRefPic->iWidthInPixel, pRefPic->iHeightInPixel, pRefPic->iLineSize,
                              c.pFuncs->sExpandPicFunc.pfExpandLumaPicture, c.pFuncs->sExpandPicFunc.pfExpandChromaPicture);

    for (int32_t i = 0; i < n; i++)
      for (int k = 0; k < 4; k++) {
        const int32_t kiX = (c.pMbs[i].iMbX << 4) + ((k & 1) << 3), kiY = (c.pMbs[i].iMbY << 4) + ((k >> 1) << 3);
        int32_t iSad = 0;
        for (int32_t y = kiY; y < kiY + 8; y++)
          for (int32_t x = kiX; x < kiX + 8; x++)
            iSad += abs (c.pEncPic->pData[0][y * c.pEncPic->iLineSize[0] + x] - pOrigPic->pData[0][y * pOrigPic->iLineSize[0] + x]);
        pSad8x8[i * 4 + k] = iSad;
      }
  }

  // The macroblock cache after a slice: the I-slice context's, and the inter
  // state (the motion vector cache, the neighbours' SADs and skip flags, the
  // predictors, the reference's type here) with where the inter pointers
  // point.
  void HashCache (Hasher& h) {
    c.HashCache (h);
    SMbCache* m = &c.pSlice->sMbCacheInfo;
    HashMvCache (h, &m->sMvComponents);
    h.Add (m->iSadCost, sizeof m->iSadCost);
    h.Add (m->sMbMvp, sizeof m->sMbMvp);
    h.Add (m->iSadCostSkip, sizeof m->iSadCostSkip);
    h.Add (m->bMbTypeSkip, sizeof m->bMbTypeSkip);
    h.AddI32 (m->bCollocatedPredFlag);
    h.AddI32 ((int32_t)m->uiRefMbType);
    h.AddI32 (Region (m->pEncSad, c.pDecPic->pMbSkipSad, c.iMbCount * (int32_t)sizeof (int32_t)));
    for (int i = 0; i < 3; i++) h.AddI32 (Region (m->SPicData.pRefMb[i], pRefPic->pBuffer, PictureBufferSize (pRefPic)));
  }

  // The slice's motion search state after it (the last macroblock's).
  void HashSlice (Hasher& h) {
    SSlice* s = c.pSlice;
    h.AddI32 (s->uiMvcNum);
    h.Add (s->sMvc, sizeof s->sMvc);
    h.Add (&s->sMvStartMin, sizeof (SMVUnitXY));
    h.Add (&s->sMvStartMax, sizeof (SMVUnitXY));
  }

  // The frame after all its slices: the I-slice context's, the macroblocks'
  // inter fields, the current picture's other macroblock arrays, the
  // reference (which must not change), the previous source (the background
  // macroblocks copy into it) and the background flags.
  void HashFrame (Hasher& h) {
    c.HashFrame (h);
    const int32_t n = c.iMbCount;
    for (int32_t i = 0; i < n; i++) {
      h.Add (&c.pMbs[i].sP16x16Mv, sizeof (SMVUnitXY));
      h.Add (c.pMbs[i].uiSubMbType, 4);
    }
    h.Add (c.pDecPic->uiRefMbType, n * sizeof (uint32_t));
    h.Add (c.pDecPic->sMvList, n * sizeof (SMVUnitXY));
    h.Add (c.pDecPic->pMbSkipSad, n * sizeof (int32_t));
    h.Add (pRefPic->pBuffer, PictureBufferSize (pRefPic));
    h.Add (pOrigPic->pBuffer, PictureBufferSize (pOrigPic));
    h.Add (pBgFlag, n);
  }

  ~PsliceCtx() {
    WELS_DELETE_OP (c.pFuncs->pParametersetStrategy);
    free (pVaa);
    free (pAqDeltaQp);
    free (pBgFlag);
    free (pSad8x8);
    FreePicture (c.pMa, &pOrigPic);
    FreePicture (c.pMa, &pRefPic);
    c.pMa->WelsFree (pMvdCostTable, "pMvdCostTable");
  }
};

// One WelsCodePSlice row's parameters.
struct PsliceCase {
  int32_t w, h;
  int slicing, complexity, bgd, aq;
  int32_t qp;
  uint8_t offset;
  int32_t mvrange;
  int content, motion;
  int32_t noise;
  int reftype, seed, buf;
};

// WelsCodePSlice over each slice of a frame in turn, hashed as the
// WelsISliceMdEnc rows are, with the inter state added: per slice the
// return, the bitstream, the macroblock cache and the slice's search state,
// then the whole frame. The small buffer (buf=1) fails CheckBitstreamBuffer
// after the first macroblocks, so the loop retries at higher QPs and gives
// up past 50.
void PsliceRow (const PsliceCase& k) {
  PsliceCtx p (k.w, k.h, k.slicing, k.complexity, k.bgd, k.aq, k.qp, k.offset, k.mvrange);
  Rng g (k.seed);
  p.Fill (g, k.content, k.motion, k.noise, k.reftype, k.qp);
  const int32_t kiLogical = k.buf ? 1500 : p.c.iMbCount * 1024 + 4096;
  Hasher h;
  for (int32_t iSlice = 0; iSlice < p.c.iSliceNum; iSlice++) {
    std::vector<uint8_t> bits (kiLogical + 64);
    g.Fill (bits.data(), bits.size());
    SBitStringAux bs;
    InitBits (&bs, bits.data(), kiLogical);
    SSlice* pSlice = p.c.pSlice;
    pSlice->pSliceBsa = &bs;
    pSlice->iSliceIdx = iSlice;
    h.AddI32 (SetSliceBoundaryInfo (p.c.pDq, pSlice, iSlice));
    pSlice->uiLastMbQp = (uint8_t)k.qp;
    h.AddI32 (WelsCodePSlice (p.c.pCtx, pSlice));
    WelsWriteSliceEndSyn (pSlice, false);
    h.AddI32 ((int32_t) (bs.pCurBuf - bs.pStartBuf));
    h.AddI32 ((int32_t)bs.uiCurBits);
    h.AddI32 (bs.iLeftBits);
    h.Add (bits.data(), bits.size());
    h.AddI32 (pSlice->uiLastMbQp);
    h.AddI32 (pSlice->iMbSkipRun);
    p.HashCache (h);
    p.HashSlice (h);
    pSlice->pSliceBsa = NULL;
  }
  p.HashFrame (h);
  Emit ("svc_encode_slice", "WelsCodePSlice",
        Fmt ("w=%d,h=%d,slicing=%d,complexity=%d,bgd=%d,aq=%d,qp=%d,offset=%d,mvrange=%d,content=%d,motion=%d,"
             "noise=%d,reftype=%d,seed=%d,buf=%d", k.w, k.h, k.slicing, k.complexity, k.bgd, k.aq, k.qp, k.offset,
             k.mvrange, k.content, k.motion, k.noise, k.reftype, k.seed, k.buf), h);
}

}  // namespace

void KernelsMvPred() {
  // PredMv at every partition place it is called for: the 16x16 and 16x8
  // partitions (width 4), the 8x8 and 8x4 ones (width 2) and the 4x8 and 4x4
  // ones (width 1), each row over random caches and both references.
  const int8_t kWidths[3] = {4, 2, 1};
  for (int8_t iPartW : kWidths)
    for (int8_t iPartIdx = 0; iPartIdx < 16; iPartIdx++) {
      if (iPartW == 4 && iPartIdx != 0 && iPartIdx != 8) continue;
      if (iPartW == 2 && (iPartIdx & 1)) continue;
      Hasher h;
      for (int seed = 0; seed < 8; seed++) {
        Rng g (seed);
        SMVComponentUnit sMvComp;
        FillMvCache (g, &sMvComp);
        for (int32_t iRef = 0; iRef < 2; iRef++) {
          SMVUnitXY sMvp;
          g.Fill ((uint8_t*)&sMvp, sizeof sMvp);
          PredMv (&sMvComp, iPartIdx, iPartW, iRef, &sMvp);
          h.Add (&sMvp, sizeof sMvp);
        }
      }
      Emit ("mv_pred", "PredMv", Fmt ("w=%d,idx=%d", iPartW, iPartIdx), h);
    }

  // PredInter16x8Mv, PredInter8x16Mv (both partitions) and PredSkipMv.
  for (int kind = 0; kind < 3; kind++)
    for (int32_t iPartIdx : {0, 4, 8}) {
      if ((kind == 0 && iPartIdx == 4) || (kind == 1 && iPartIdx == 8) || (kind == 2 && iPartIdx != 0)) continue;
      Hasher h;
      for (int seed = 0; seed < 16; seed++) {
        Rng g (seed);
        SMbCache cache;
        memset (&cache, 0, sizeof cache);
        FillMvCache (g, &cache.sMvComponents);
        for (int8_t iRef = 0; iRef < 2; iRef++) {
          SMVUnitXY sMvp;
          g.Fill ((uint8_t*)&sMvp, sizeof sMvp);
          if (kind == 0)
            PredInter16x8Mv (&cache, iPartIdx, iRef, &sMvp);
          else if (kind == 1)
            PredInter8x16Mv (&cache, iPartIdx, iRef, &sMvp);
          else if (iRef == 0)
            PredSkipMv (&cache, &sMvp);
          h.Add (&sMvp, sizeof sMvp);
        }
      }
      const char* kNames[3] = {"PredInter16x8Mv", "PredInter8x16Mv", "PredSkipMv"};
      Emit ("mv_pred", kNames[kind], Fmt ("idx=%d", iPartIdx), h);
    }

  // The motion info updates, into the cache and the macroblock's motion
  // vectors and reference indices (kind 0..6), and into the cache alone
  // (kind 7..12), at every partition place of their shape.
  struct Update {
    const char* name;
    uint32_t places;  // bit i: partition place i
  };
  const Update kUpdates[13] = {
    {"UpdateP16x16MotionInfo", 0x0001}, {"UpdateP16x8MotionInfo", 0x0101}, {"update_P8x16_motion_info", 0x0011},
    {"UpdateP8x8MotionInfo", 0x1111},   {"UpdateP4x4MotionInfo", 0xffff},  {"UpdateP8x4MotionInfo", 0x5555},
    {"UpdateP4x8MotionInfo", 0x3333},   {"UpdateP16x8Motion2Cache", 0x0101}, {"UpdateP8x16Motion2Cache", 0x0011},
    {"UpdateP8x8Motion2Cache", 0x1111}, {"UpdateP4x4Motion2Cache", 0xffff}, {"UpdateP8x4Motion2Cache", 0x5555},
    {"UpdateP4x8Motion2Cache", 0x3333},
  };
  for (int kind = 0; kind < 13; kind++)
    for (int32_t iPartIdx = 0; iPartIdx < 16; iPartIdx++) {
      if (! (kUpdates[kind].places & (1u << iPartIdx))) continue;
      Hasher h;
      for (int seed = 0; seed < 4; seed++) {
        Rng g (seed);
        SMbCache cache;
        memset (&cache, 0, sizeof cache);
        FillMvCache (g, &cache.sMvComponents);
        SMVUnitXY sMvs[MB_BLOCK4x4_NUM];
        int8_t iRefIdx[MB_BLOCK8x8_NUM];
        g.Fill ((uint8_t*)sMvs, sizeof sMvs);
        g.Fill ((uint8_t*)iRefIdx, sizeof iRefIdx);
        SMB sMb;
        memset (&sMb, 0, sizeof sMb);
        sMb.sMv = sMvs;
        sMb.pRefIndex = iRefIdx;
        SMVUnitXY sMv;
        g.Fill ((uint8_t*)&sMv, sizeof sMv);
        const int8_t kiRef = kCacheRefs[2 + g.Next() % 4];
        switch (kind) {
        case 0: UpdateP16x16MotionInfo (&cache, &sMb, kiRef, &sMv); break;
        case 1: UpdateP16x8MotionInfo (&cache, &sMb, iPartIdx, kiRef, &sMv); break;
        case 2: update_P8x16_motion_info (&cache, &sMb, iPartIdx, kiRef, &sMv); break;
        case 3: UpdateP8x8MotionInfo (&cache, &sMb, iPartIdx, kiRef, &sMv); break;
        case 4: UpdateP4x4MotionInfo (&cache, &sMb, iPartIdx, kiRef, &sMv); break;
        case 5: UpdateP8x4MotionInfo (&cache, &sMb, iPartIdx, kiRef, &sMv); break;
        case 6: UpdateP4x8MotionInfo (&cache, &sMb, iPartIdx, kiRef, &sMv); break;
        case 7: UpdateP16x8Motion2Cache (&cache, iPartIdx, kiRef, &sMv); break;
        case 8: UpdateP8x16Motion2Cache (&cache, iPartIdx, kiRef, &sMv); break;
        case 9: UpdateP8x8Motion2Cache (&cache, iPartIdx, kiRef, &sMv); break;
        case 10: UpdateP4x4Motion2Cache (&cache, iPartIdx, kiRef, &sMv); break;
        case 11: UpdateP8x4Motion2Cache (&cache, iPartIdx, kiRef, &sMv); break;
        default: UpdateP4x8Motion2Cache (&cache, iPartIdx, kiRef, &sMv); break;
        }
        HashMvCache (h, &cache.sMvComponents);
        h.Add (sMvs, sizeof sMvs);
        h.Add (iRefIdx, sizeof iRefIdx);
      }
      Emit ("mv_pred", kUpdates[kind].name, Fmt ("idx=%d", iPartIdx), h);
    }
}

void KernelsMdInter() {
  // MvdCostInit's whole table (52 QP rows) for each MV range of the P rows.
  for (int32_t iMvRange : {CAMERA_STARTMV_RANGE, 6}) {
    const int32_t kiMvdRange = WELS_MIN ((iMvRange + 1) << 1, CAMERA_MVD_RANGE);
    const int32_t kiStride = 1 + ((kiMvdRange << 2) << 1);
    std::vector<uint16_t> table (52 * kiStride + 8);
    Rng g (iMvRange);
    g.Fill ((uint8_t*)table.data(), table.size() * sizeof (uint16_t));
    MvdCostInit (table.data(), kiStride);
    Hasher h;
    h.Add (table.data(), table.size() * sizeof (uint16_t));
    Emit ("md", "MvdCostInit", Fmt ("mvrange=%d", iMvRange), h);
  }
}

void KernelsSvcEncodePSlice() {
  const int32_t kQps[5] = {12, 20, 26, 32, 40};
  const int32_t kNoises[3] = {0, 2, 6};
  const uint8_t kOffsets[4] = {0, 6, 12, 250};

  // The sweep: every size, complexity (MEDIUM and HIGH take the same camera
  // path, so they alternate), motion and content, with the other parameters
  // spread across them.
  for (const FrameSize& s : kSizes)
    for (int fast = 1; fast >= 0; fast--)
      for (int motion = 0; motion < 5; motion++)
        for (int content = 0; content < 3; content++)
          for (int seed = 0; seed < 2; seed++) {
            const int32_t kiMbCount = ((s.w + 15) >> 4) * ((s.h + 15) >> 4);
            if (seed == 1 && kiMbCount > 15) continue;
            PsliceCase k;
            k.w = s.w;
            k.h = s.h;
            k.slicing = (motion + content) % 3;
            if (k.slicing == 2 && kiMbCount < 3) k.slicing = 1;
            k.complexity = fast ? LOW_COMPLEXITY : (motion & 1) ? HIGH_COMPLEXITY : MEDIUM_COMPLEXITY;
            const int qi = (motion + 2 * content + fast + seed) % 5;
            k.qp = kQps[qi];
            k.offset = kOffsets[(qi + content) % 4];
            k.bgd = (content + fast + seed) & 1;
            k.aq = (motion + fast) % 3 == 0;
            k.mvrange = (motion + content + fast) % 4 == 3 ? 6 : CAMERA_STARTMV_RANGE;
            k.content = content;
            k.motion = motion;
            k.noise = kNoises[(content + motion + seed) % 3];
            k.reftype = (content + motion + fast + 1) & 1;
            k.seed = seed;
            k.buf = 0;
            PsliceRow (k);
          }

  // The QP ends, with background detection on, and adaptive quantisation
  // too at 51 (at 0 its QP floor would lift every macroblock off 0).
  for (const FrameSize& s : {FrameSize{40, 24}, FrameSize{176, 144}})
    for (int32_t qp : {0, 51})
      for (int fast = 1; fast >= 0; fast--)
        for (int motion : {1, 3}) {
          PsliceCase k = {s.w, s.h, 2, fast ? LOW_COMPLEXITY : MEDIUM_COMPLEXITY, 1, qp != 0, qp, 6,
                          qp ? 6 : CAMERA_STARTMV_RANGE, 2, motion, 2, 1, 1, 0
                         };
          PsliceRow (k);
        }

  // The small buffer.
  for (const FrameSize& s : {FrameSize{80, 48}, FrameSize{176, 144}})
    for (int fast = 1; fast >= 0; fast--) {
      PsliceCase k = {s.w, s.h, 0, fast ? LOW_COMPLEXITY : MEDIUM_COMPLEXITY, 0, 0, 12, 0, CAMERA_STARTMV_RANGE, 0, 3,
                      6, 0, 0, 1
                     };
      PsliceRow (k);
    }
}
