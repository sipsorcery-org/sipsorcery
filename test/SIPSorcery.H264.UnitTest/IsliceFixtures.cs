//-----------------------------------------------------------------------------
// Filename: IsliceFixtures.cs
//
// Description: The inputs the I-slice fixtures share with the C harness
// (thirdparty/openh264-dotnet-port/refharness/src/islice_encoder.cpp): the
// picture content, the slice maps, and the hand-built I-slice context (what
// WelsInitCurrentLayer, InitDqLayers, InitMbInfo and PreprocessSliceCoding
// would set for one spatial layer with rate control off) with its hashes.
// Every draw must come in the same order as on the C side.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using SIPSorcery.H264.Encoder;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

internal static unsafe class IsliceFixtures
{
    /// <summary>fixture kSizes: the frame sizes of the I-slice rows.</summary>
    public static readonly (int W, int H)[] Sizes = { (16, 16), (40, 24), (80, 48), (176, 144) };

    public static int Clip255(int v) => Math.Min(255, Math.Max(0, v));

    /// <summary>
    /// fixture FillPlane: a w x h plane (w, h multiples of 4) of content k: 0
    /// noise, 1 a gentle gradient with a little noise, 2 4x4 blocks that are each
    /// flat or ramp one way.
    /// </summary>
    public static void FillPlane(ref FixtureRng g, byte* p, int s, int w, int h, int k)
    {
        if (k == 0)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    p[y * s + x] = g.NextByte();
                }
            }
        }
        else if (k == 1)
        {
            int a = (int)(g.Next() % 9) - 4;
            int b = (int)(g.Next() % 9) - 4;
            int c = g.NextByte();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    p[y * s + x] = (byte)Clip255(c + (a * x + b * y) / 2 + (int)(g.Next() & 3));
                }
            }
        }
        else
        {
            for (int by = 0; by < h; by += 4)
            {
                for (int bx = 0; bx < w; bx += 4)
                {
                    int v = g.NextByte();
                    int dir = (int)(g.Next() % 4);
                    int step = (int)(g.Next() % 33) - 16;
                    for (int j = 0; j < 4; j++)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            int d = dir == 0 ? 0 : dir == 1 ? i : dir == 2 ? j : i + j;
                            p[(by + j) * s + bx + i] = (byte)Clip255(v + step * d);
                        }
                    }
                }
            }
        }
    }

    /// <summary>fixture PictureBufferSize: the bytes AllocPicture gives pBuffer.</summary>
    public static int PictureBufferSize(SPicture* pPic)
    {
        int kiHeight = WELS_ALIGN(pPic->iHeightInPixel, MB_HEIGHT_LUMA) + (PADDING_LENGTH << 1);
        return pPic->iLineSize[0] * kiHeight + 2 * pPic->iLineSize[1] * (kiHeight >> 1);
    }

    /// <summary>
    /// fixture Region: 1 + p's offset in [base, base + n), 0 for null, -1 for
    /// anywhere else.
    /// </summary>
    public static int Region(void* p, void* pBase, int n)
    {
        if (p == null)
        {
            return 0;
        }
        long d = (byte*)p - (byte*)pBase;
        return (d >= 0 && d < n) ? (int)d + 1 : -1;
    }

    /// <summary>
    /// fixture SliceArgument: 0 one slice, 1 one per row, 2 three raster runs of
    /// about a third of the frame each.
    /// </summary>
    public static void SliceArgument(SSliceArgument* pArg, int slicing, int iMbWidth, int iMbHeight)
    {
        *pArg = default;
        int kiCount = iMbWidth * iMbHeight;
        if (slicing == 0)
        {
            pArg->uiSliceMode = SliceModeEnum.SM_SINGLE_SLICE;
            pArg->uiSliceNum = 1;
        }
        else if (slicing == 1)
        {
            pArg->uiSliceMode = SliceModeEnum.SM_RASTER_SLICE;
            pArg->uiSliceNum = (uint)iMbHeight;
            pArg->uiSliceMbNum[0] = 0;
        }
        else
        {
            pArg->uiSliceMode = SliceModeEnum.SM_RASTER_SLICE;
            pArg->uiSliceNum = 3;
            pArg->uiSliceMbNum[0] = (uint)(kiCount / 3 + 1);
            pArg->uiSliceMbNum[1] = (uint)(kiCount / 3);
            pArg->uiSliceMbNum[2] = (uint)kiCount - pArg->uiSliceMbNum[0] - pArg->uiSliceMbNum[1];
        }
    }

    public static T* Alloc<T>() where T : unmanaged => (T*)NativeMemory.AllocZeroed((nuint)sizeof(T));

    public static T* AllocArray<T>(int count) where T : unmanaged => (T*)NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(T));
}

/// <summary>fixture IsliceCtx: the context WelsISliceMdEnc reads, for one w x h layer.</summary>
internal sealed unsafe class IsliceCtx : IDisposable
{
    private CMemoryAlign* _pMa;

    /// <summary>The context's allocator (pCtx->pMemAlign).</summary>
    public CMemoryAlign* Ma => _pMa;

    public SWelsSvcCodingParam* Param { get; }
    public SWelsFuncPtrList* Funcs { get; }
    public SWelsPPS* Pps { get; }
    public SWelsSvcRc* Rc { get; }
    public sWelsEncCtx* Ctx { get; }
    public SDqLayer* Dq { get; }
    public SSlice* Slice { get; }
    public SMB* Mbs { get; }
    public sbyte* Nzc { get; }
    public sbyte* I4 { get; }
    public sbyte* RefIdx { get; }
    public SMVUnitXY* Mv { get; }
    public int* Sad { get; }
    public SPicture* EncPic { get; private set; }
    public SPicture* DecPic { get; private set; }
    public int MbWidth { get; }
    public int MbHeight { get; }
    public int MbCount { get; }
    public int SliceNum { get; }
    public int BoundaryRet { get; }

    public IsliceCtx(int w, int h, int slicing, int fast, int qp, byte uiChromaQpOffset)
    {
        _pMa = WELS_NEW_CMemoryAlign(16);
        CMemoryAlign* pMa = _pMa;
        MbWidth = (w + 15) >> 4;
        MbHeight = (h + 15) >> 4;
        MbCount = MbWidth * MbHeight;

        Param = IsliceFixtures.Alloc<SWelsSvcCodingParam>();
        Param->iSpatialLayerNum = 1;
        Param->iTemporalLayerNum = 1;
        Param->sSpatialLayers[0].iVideoWidth = w;
        Param->sSpatialLayers[0].iVideoHeight = h;
        Param->sSpatialLayers[0].iDLayerQp = qp;

        Funcs = IsliceFixtures.Alloc<SWelsFuncPtrList>();
        Funcs->pfSetMemZeroSize8 = &WelsSetMemZero_c;
        Funcs->pfSetMemZeroSize64Aligned16 = &WelsSetMemZero_c;
        Funcs->pfSetMemZeroSize64 = &WelsSetMemZero_c;
        WelsInitIntraPredFuncs(Funcs, 0);
        WelsInitSampleSadFunc(Funcs, 0);
        Funcs->pfMdBackgroundInfoUpdate = &WelsMdUpdateBGDInfo;
        InitIntraAnalysisVaaInfo(Funcs, 0);
        InitCoeffFunc(Funcs, 0, 0);
        WelsInitEncodingFuncs(Funcs, 0);
        WelsInitReconstructionFuncs(Funcs, 0);
        if (fast != 0)
        {
            SetFastCodingFunc(Funcs);
        }
        else
        {
            SetNormalCodingFunc(Funcs);
        }

        Pps = IsliceFixtures.Alloc<SWelsPPS>();
        Pps->uiChromaQpIndexOffset = uiChromaQpOffset;
        Rc = IsliceFixtures.Alloc<SWelsSvcRc>();

        Dq = IsliceFixtures.Alloc<SDqLayer>();
        Dq->iMbWidth = (short)MbWidth;
        Dq->iMbHeight = (short)MbHeight;
        Dq->sLayerInfo.pPpsP = Pps;

        sWelsEncCtx* pCtx = IsliceFixtures.Alloc<sWelsEncCtx>();
        Ctx = pCtx;
        pCtx->pSvcParam = Param;
        pCtx->pFuncList = Funcs;
        pCtx->pCurDqLayer = Dq;
        pCtx->pMemAlign = pMa;
        pCtx->pWelsSvcRc = Rc;
        pCtx->eSliceType = EWelsSliceType.I_SLICE;
        pCtx->iGlobalQp = qp;
        WelsRcInitFuncPointers(pCtx, RC_MODES.RC_OFF_MODE);
        AllocStrideTables(&pCtx, 1);

        EncPic = AllocPicture(pMa, w, h, false, 0);
        DecPic = AllocPicture(pMa, w, h, true, 0);
        WelsGetEncBlockStrideOffset(pCtx->pStrideTab->pStrideEncBlockOffset[0], EncPic->iLineSize[0], EncPic->iLineSize[1]);
        pCtx->pEncPic = EncPic;
        pCtx->pDecPic = DecPic;
        Dq->pDecPic = DecPic;
        for (int i = 0; i < 3; i++)
        {
            Dq->pEncData[i] = EncPic->pData[i];
            Dq->iEncStride[i] = EncPic->iLineSize[i];
            Dq->pCsData[i] = DecPic->pData[i];
            Dq->iCsStride[i] = DecPic->iLineSize[i];
        }

        SSliceArgument sArg;
        IsliceFixtures.SliceArgument(&sArg, slicing, MbWidth, MbHeight);
        InitSliceSegment(Dq, pMa, &sArg, MbWidth, MbHeight);
        // InitSliceInLayer's boundary tables (sized by pointers, as upstream).
        SliceNum = Dq->sSliceEncCtx.iSliceNumInFrame;
        Dq->iMaxSliceNum = SliceNum;
        Dq->pFirstMbIdxOfSlice = (int*)pMa->WelsMallocz((uint)(sizeof(int*) * SliceNum), "pFirstMbIdxOfSlice");
        Dq->pCountMbNumInSlice = (int*)pMa->WelsMallocz((uint)(sizeof(int*) * SliceNum), "pCountMbNumInSlice");
        BoundaryRet = InitSliceBoundaryInfo(Dq, &sArg, SliceNum);

        Mbs = IsliceFixtures.AllocArray<SMB>(MbCount);
        Nzc = IsliceFixtures.AllocArray<sbyte>(MbCount * MB_LUMA_CHROMA_BLOCK4x4_NUM);
        I4 = IsliceFixtures.AllocArray<sbyte>(MbCount * INTRA_4x4_MODE_NUM);
        RefIdx = IsliceFixtures.AllocArray<sbyte>(MbCount * MB_BLOCK8x8_NUM);
        Mv = IsliceFixtures.AllocArray<SMVUnitXY>(MbCount * MB_BLOCK4x4_NUM);
        Sad = IsliceFixtures.AllocArray<int>(MbCount);
        for (int i = 0; i < MbCount; i++)
        {
            SMB* pMb = &Mbs[i];
            pMb->iMbX = ((short*)pCtx->pStrideTab->pMbIndexX[0])[i];
            pMb->iMbY = ((short*)pCtx->pStrideTab->pMbIndexY[0])[i];
            pMb->iMbXY = i;
            pMb->sMv = &Mv[i * MB_BLOCK4x4_NUM];
            pMb->pRefIndex = &RefIdx[i * MB_BLOCK8x8_NUM];
            pMb->pSadCost = &Sad[i];
            pMb->pIntra4x4PredMode = &I4[i * INTRA_4x4_MODE_NUM];
            pMb->pNonZeroCount = &Nzc[i * MB_LUMA_CHROMA_BLOCK4x4_NUM];
            UpdateMbNeighbor(Dq, pMb, MbWidth, WelsMbToSliceIdc(Dq, i));
        }
        Dq->sMbDataP = Mbs;

        Slice = IsliceFixtures.Alloc<SSlice>();
        Slice->sSliceHeaderExt.sSliceHeader.eSliceType = EWelsSliceType.I_SLICE;
        AllocMbCacheAligned(&Slice->sMbCacheInfo, pMa);
    }

    /// <summary>fixture IsliceCtx::Fill: the random starting state, then the source content.</summary>
    public void Fill(ref FixtureRng g, int content)
    {
        g.Fill(new Span<byte>(EncPic->pBuffer, IsliceFixtures.PictureBufferSize(EncPic)));
        g.Fill(new Span<byte>(DecPic->pBuffer, IsliceFixtures.PictureBufferSize(DecPic)));
        g.Fill(new Span<byte>(Nzc, MbCount * MB_LUMA_CHROMA_BLOCK4x4_NUM));
        g.Fill(new Span<byte>(I4, MbCount * INTRA_4x4_MODE_NUM));
        g.Fill(new Span<byte>(RefIdx, MbCount * MB_BLOCK8x8_NUM));
        g.Fill(new Span<byte>(Mv, MbCount * MB_BLOCK4x4_NUM * sizeof(SMVUnitXY)));
        g.Fill(new Span<byte>(Sad, MbCount * sizeof(int)));
        g.Fill(new Span<byte>(DecPic->pRefMbQp, MbCount));
        int kiW = MbWidth << 4, kiH = MbHeight << 4;
        IsliceFixtures.FillPlane(ref g, EncPic->pData[0], EncPic->iLineSize[0], kiW, kiH, content);
        IsliceFixtures.FillPlane(ref g, EncPic->pData[1], EncPic->iLineSize[1], kiW >> 1, kiH >> 1, content);
        IsliceFixtures.FillPlane(ref g, EncPic->pData[2], EncPic->iLineSize[2], kiW >> 1, kiH >> 1, content);
    }

    /// <summary>fixture IsliceCtx::HashCache: the macroblock cache after a slice.</summary>
    public void HashCache(FixtureHasher h)
    {
        SMbCache* c = &Slice->sMbCacheInfo;
        h.Add(new ReadOnlySpan<byte>(c->iNonZeroCoeffCount, 48));
        h.Add(new ReadOnlySpan<byte>(c->iIntraPredMode, 48));
        h.AddI32(c->uiLumaI16x16Mode);
        h.AddI32(c->uiChmaI8x8Mode);
        h.AddI32(c->uiNeighborIntra);
        h.Add(new ReadOnlySpan<byte>(c->pMemPredMb, 2 * 256));
        h.Add(new ReadOnlySpan<byte>(c->pMemPredBlk4, 2 * 16));
        h.Add(new ReadOnlySpan<byte>(c->pCoeffLevel, MB_COEFF_LIST_SIZE * sizeof(short)));
        h.Add(new ReadOnlySpan<byte>(c->pSkipMb, 384));
        h.Add(new ReadOnlySpan<byte>(c->pBufferInterPredMe, 4 * 640));
        h.Add(new ReadOnlySpan<byte>(c->pPrevIntra4x4PredModeFlag, 16 * sizeof(bool)));
        h.Add(new ReadOnlySpan<byte>(c->pRemIntra4x4PredModeFlag, 16));
        h.Add(new ReadOnlySpan<byte>(c->pDct, sizeof(SDCTCoeff)));
        h.AddI32(IsliceFixtures.Region(c->pMemPredLuma, c->pMemPredMb, 512));
        h.AddI32(IsliceFixtures.Region(c->pMemPredChroma, c->pMemPredMb, 512));
        h.AddI32(IsliceFixtures.Region(c->pBestPredIntraChroma, c->pMemPredMb, 512));
        h.AddI32(IsliceFixtures.Region(c->pBestPredI4x4Blk4, c->pMemPredBlk4, 32));
    }

    /// <summary>fixture IsliceCtx::HashFrame: the frame after all its slices.</summary>
    public void HashFrame(FixtureHasher h)
    {
        for (int i = 0; i < MbCount; i++)
        {
            SMB* pMb = &Mbs[i];
            h.AddI32((int)pMb->uiMbType);
            h.AddI32(pMb->uiCbp);
            h.AddI32(pMb->uiLumaQp);
            h.AddI32(pMb->uiChromaQp);
            h.AddI32(pMb->uiSliceIdc);
            h.AddI32(pMb->uiNeighborAvail);
            h.AddI32((int)pMb->uiChromPredMode);
            h.AddI32(pMb->iLumaDQp);
            h.AddI32(pMb->iCbpDc);
        }
        h.Add(new ReadOnlySpan<byte>(Nzc, MbCount * MB_LUMA_CHROMA_BLOCK4x4_NUM));
        h.Add(new ReadOnlySpan<byte>(I4, MbCount * INTRA_4x4_MODE_NUM));
        h.Add(new ReadOnlySpan<byte>(RefIdx, MbCount * MB_BLOCK8x8_NUM));
        h.Add(new ReadOnlySpan<byte>(Mv, MbCount * MB_BLOCK4x4_NUM * sizeof(SMVUnitXY)));
        h.Add(new ReadOnlySpan<byte>(Sad, MbCount * sizeof(int)));
        h.Add(new ReadOnlySpan<byte>(DecPic->pRefMbQp, MbCount));
        h.Add(new ReadOnlySpan<byte>(EncPic->pBuffer, IsliceFixtures.PictureBufferSize(EncPic)));
        h.Add(new ReadOnlySpan<byte>(DecPic->pBuffer, IsliceFixtures.PictureBufferSize(DecPic)));
    }

    public void Dispose()
    {
        CMemoryAlign* pMa = _pMa;
        if (pMa == null)
        {
            return;
        }
        FreeMbCache(&Slice->sMbCacheInfo, pMa);
        NativeMemory.Free(Slice);
        NativeMemory.Free(Sad);
        NativeMemory.Free(Mv);
        NativeMemory.Free(RefIdx);
        NativeMemory.Free(I4);
        NativeMemory.Free(Nzc);
        NativeMemory.Free(Mbs);
        pMa->WelsFree(Dq->pCountMbNumInSlice, "pCountMbNumInSlice");
        pMa->WelsFree(Dq->pFirstMbIdxOfSlice, "pFirstMbIdxOfSlice");
        UninitSliceSegment(Dq, pMa);
        SPicture* pDec = DecPic, pEnc = EncPic;
        FreePicture(pMa, &pDec);
        FreePicture(pMa, &pEnc);
        DecPic = EncPic = null;
        pMa->WelsFree(Ctx->pStrideTab->pStrideDecBlockOffset[0][1], "pBase");
        pMa->WelsFree(Ctx->pStrideTab, "SStrideTables");
        NativeMemory.Free(Ctx);
        NativeMemory.Free(Dq);
        NativeMemory.Free(Rc);
        NativeMemory.Free(Pps);
        NativeMemory.Free(Funcs);
        NativeMemory.Free(Param);
        WELS_DELETE_CMemoryAlign(ref _pMa);
    }
}
