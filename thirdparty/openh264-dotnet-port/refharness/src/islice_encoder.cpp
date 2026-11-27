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
// The I-slice context and the content helpers are in islice_ctx.h, which
// pslice_encoder.cpp shares.

#include "islice_ctx.h"

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
