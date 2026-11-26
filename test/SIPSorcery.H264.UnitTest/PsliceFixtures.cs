//-----------------------------------------------------------------------------
// Filename: PsliceFixtures.cs
//
// Description: The inputs the P-slice fixtures share with the C harness
// (thirdparty/openh264-dotnet-port/refharness/src/pslice_encoder.cpp): the
// random motion vector caches, and the hand-built P-slice context (the
// I-slice one of IsliceFixtures.cs with what a camera P frame adds: the
// function list from InitFunctionPointers and PreprocessSliceCoding, the MVD
// cost table and MV range, a reference picture, and the VAA state the
// preprocessing would leave) with its content and hashes. Every draw must
// come in the same order as on the C side.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5).
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

internal static unsafe class PsliceFixtures
{
    public static int Clip3(int v, int lo, int hi) => Math.Min(hi, Math.Max(lo, v));

    /// <summary>fixture Noise: a uniform draw in [-n, n].</summary>
    public static int Noise(ref FixtureRng g, int n) => (int)(g.Next() % (uint)(2 * n + 1)) - n;

    /// <summary>fixture kRefMbTypesP: what a reference picture's coding left, by type.</summary>
    public static readonly uint[] RefMbTypesP =
    {
        MB_TYPE_SKIP, MB_TYPE_BACKGROUND, MB_TYPE_16x16, MB_TYPE_8x8, MB_TYPE_INTRA4x4, MB_TYPE_INTRA16x16,
    };

    /// <summary>fixture kCacheRefs: unavailable, intra, or one of two references.</summary>
    public static readonly sbyte[] CacheRefs = { REF_NOT_AVAIL, REF_NOT_IN_LIST, 0, 0, 1, 1 };

    public static void FillMvCache(ref FixtureRng g, SMVComponentUnit* pMvComp)
    {
        g.Fill(new Span<byte>(&pMvComp->sMotionVectorCache, 29 * sizeof(SMVUnitXY)));
        for (int i = 0; i < 30; i++)
        {
            pMvComp->iRefIndexCache[i] = CacheRefs[g.Next() % 6];
        }
    }

    public static void HashMvCache(FixtureHasher h, SMVComponentUnit* pMvComp)
    {
        h.Add(new ReadOnlySpan<byte>(&pMvComp->sMotionVectorCache, 29 * sizeof(SMVUnitXY)));
        h.Add(new ReadOnlySpan<byte>(pMvComp->iRefIndexCache, 30));
    }
}

/// <summary>fixture PsliceCtx: the context WelsCodePSlice reads, over the I-slice one.</summary>
internal sealed unsafe class PsliceCtx : IDisposable
{
    private SPicture* _pRefPic;
    private SPicture* _pOrigPic;

    public IsliceCtx C { get; }
    /// <summary>The reference: the previous source coded, then padded.</summary>
    public SPicture* RefPic => _pRefPic;
    /// <summary>The previous source, the VAA's reference planes.</summary>
    public SPicture* OrigPic => _pOrigPic;
    public SVAAFrameInfo* Vaa { get; private set; }
    public int* Sad8x8 { get; private set; }
    public sbyte* BgFlag { get; private set; }
    public sbyte* AqDeltaQp { get; private set; }
    public ushort* MvdCostTable { get; private set; }

    public PsliceCtx(int w, int h, int slicing, int complexity, int bgd, int aq, int qp, byte uiChromaQpOffset, int iMvRange)
    {
        C = new IsliceCtx(w, h, slicing, complexity == (int)ECOMPLEXITY_MODE.LOW_COMPLEXITY ? 1 : 0, qp, uiChromaQpOffset);
        sWelsEncCtx* pCtx = C.Ctx;
        SWelsSvcCodingParam* pParam = C.Param;
        pParam->iUsageType = EUsageType.CAMERA_VIDEO_REAL_TIME;
        pParam->iComplexityMode = (ECOMPLEXITY_MODE)complexity;
        pParam->bEnableBackgroundDetection = bgd != 0;
        pParam->bEnableAdaptiveQuant = aq != 0;
        pCtx->eSliceType = EWelsSliceType.P_SLICE;
        C.Slice->sSliceHeaderExt.sSliceHeader.eSliceType = EWelsSliceType.P_SLICE;

        // The whole function list afresh, as WelsInitEncoderExt and
        // WelsEncoderEncodeExt build it.
        *C.Funcs = default;
        InitFunctionPointers(pCtx, pParam, 0);
        WelsRcInitFuncPointers(pCtx, RC_MODES.RC_OFF_MODE);
        C.Rc->iMinQp = 6;
        C.Rc->iMaxQp = 45;

        // RequestMemorySvc's MVD cost table, for an MV range GetMvMvdRange
        // could give.
        pCtx->iMvRange = iMvRange;
        int kiMvdRange = Math.Min((iMvRange + 1) << 1, CAMERA_MVD_RANGE);
        int kiMvdInterTableSize = kiMvdRange << 2;
        int kiMvdInterTableStride = 1 + (kiMvdInterTableSize << 1);
        pCtx->iMvdCostTableSize = kiMvdInterTableSize;
        pCtx->iMvdCostTableStride = kiMvdInterTableStride;
        MvdCostTable = (ushort*)C.Ma->WelsMallocz((uint)(52 * kiMvdInterTableStride * sizeof(ushort)), "pMvdCostTable");
        MvdCostInit(MvdCostTable, kiMvdInterTableStride);
        pCtx->pMvdCostTable = MvdCostTable;

        _pRefPic = AllocPicture(C.Ma, w, h, true, 0);
        _pOrigPic = AllocPicture(C.Ma, w, h, false, 0);
        pCtx->pRefPic = _pRefPic;
        C.Dq->pRefPic = _pRefPic;

        Sad8x8 = IsliceFixtures.AllocArray<int>(C.MbCount * 4);
        BgFlag = IsliceFixtures.AllocArray<sbyte>(C.MbCount);
        AqDeltaQp = IsliceFixtures.AllocArray<sbyte>(C.MbCount);
        SVAAFrameInfo* pVaa = IsliceFixtures.Alloc<SVAAFrameInfo>();
        Vaa = pVaa;
        pVaa->sVaaCalcInfo.pSad8x8 = Sad8x8;
        pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp = AqDeltaQp;
        pVaa->iPicWidth = w;
        pVaa->iPicHeight = h;
        pVaa->iPicStride = C.EncPic->iLineSize[0];
        pVaa->iPicStrideUV = C.EncPic->iLineSize[1];
        pVaa->pRefY = _pOrigPic->pData[0];
        pVaa->pCurY = C.EncPic->pData[0];
        pVaa->pRefU = _pOrigPic->pData[1];
        pVaa->pCurU = C.EncPic->pData[1];
        pVaa->pRefV = _pOrigPic->pData[2];
        pVaa->pCurV = C.EncPic->pData[2];
        pVaa->pVaaBackgroundMbFlag = BgFlag;
        pCtx->pVaa = pVaa;

        PreprocessSliceCoding(pCtx);
    }

    /// <summary>
    /// fixture PsliceCtx::Fill: the random starting state, what the
    /// reference's coding left and the VAA's flags and offsets, then the two
    /// sources (motion m), the reference and the 8x8 SADs.
    /// </summary>
    public void Fill(ref FixtureRng g, int content, int motion, int noise, int reftype, int qp)
    {
        int n = C.MbCount;
        SPicture* pRefPic = _pRefPic, pOrigPic = _pOrigPic, pEncPic = C.EncPic, pDecPic = C.DecPic;
        g.Fill(new Span<byte>(pEncPic->pBuffer, IsliceFixtures.PictureBufferSize(pEncPic)));
        g.Fill(new Span<byte>(pDecPic->pBuffer, IsliceFixtures.PictureBufferSize(pDecPic)));
        g.Fill(new Span<byte>(C.Nzc, n * MB_LUMA_CHROMA_BLOCK4x4_NUM));
        g.Fill(new Span<byte>(C.I4, n * INTRA_4x4_MODE_NUM));
        g.Fill(new Span<byte>(C.RefIdx, n * MB_BLOCK8x8_NUM));
        g.Fill(new Span<byte>(C.Mv, n * MB_BLOCK4x4_NUM * sizeof(SMVUnitXY)));
        g.Fill(new Span<byte>(C.Sad, n * sizeof(int)));
        g.Fill(new Span<byte>(pDecPic->pRefMbQp, n));
        g.Fill(new Span<byte>(pDecPic->uiRefMbType, n * sizeof(uint)));
        g.Fill(new Span<byte>(pDecPic->sMvList, n * sizeof(SMVUnitXY)));
        g.Fill(new Span<byte>(pDecPic->pMbSkipSad, n * sizeof(int)));
        g.Fill(new Span<byte>(pRefPic->pBuffer, IsliceFixtures.PictureBufferSize(pRefPic)));
        g.Fill(new Span<byte>(pOrigPic->pBuffer, IsliceFixtures.PictureBufferSize(pOrigPic)));

        pRefPic->iPictureType = (int)(reftype != 0 ? EWelsSliceType.P_SLICE : EWelsSliceType.I_SLICE);
        for (int i = 0; i < n; i++)
        {
            uint kuiType = PsliceFixtures.RefMbTypesP[g.Next() % 6];
            pRefPic->uiRefMbType[i] = reftype != 0
                ? kuiType
                : (kuiType == MB_TYPE_INTRA16x16 ? (uint)MB_TYPE_INTRA16x16 : MB_TYPE_INTRA4x4);
            pRefPic->pRefMbQp[i] = (byte)PsliceFixtures.Clip3(qp + PsliceFixtures.Noise(ref g, 8), 0, 51);
            pRefPic->sMvList[i].iMvX = (short)PsliceFixtures.Noise(ref g, 64);
            pRefPic->sMvList[i].iMvY = (short)PsliceFixtures.Noise(ref g, 64);
            pRefPic->pMbSkipSad[i] = (int)(g.Next() % 1024);
        }
        for (int i = 0; i < n; i++)
        {
            BgFlag[i] = (sbyte)(g.Next() & 1);
        }
        for (int i = 0; i < n; i++)
        {
            AqDeltaQp[i] = (sbyte)PsliceFixtures.Noise(ref g, 6);
        }

        int kiW = C.MbWidth << 4, kiH = C.MbHeight << 4;
        for (int p = 0; p < 3; p++)
        {
            IsliceFixtures.FillPlane(ref g, pOrigPic->pData[p], pOrigPic->iLineSize[p], p != 0 ? kiW >> 1 : kiW,
                                     p != 0 ? kiH >> 1 : kiH, content);
        }

        // The motion: 0 none, 1 one shift of the whole frame, 2 that blended
        // half a pixel right, down or both, 3 a shift per 8x8 luma block, 4 new
        // content. Chroma moves half as far.
        int dx = 0, dy = 0, hx = 0, hy = 0;
        if (motion == 1 || motion == 2)
        {
            dx = PsliceFixtures.Noise(ref g, 12);
            dy = PsliceFixtures.Noise(ref g, 12);
        }
        if (motion == 2)
        {
            int k = (int)(g.Next() % 3);
            hx = k != 1 ? 1 : 0;
            hy = k != 0 ? 1 : 0;
        }
        int kiBlocksX = kiW >> 3, kiBlocksY = kiH >> 3;
        int[] blockMv = new int[motion == 3 ? kiBlocksX * kiBlocksY * 2 : 0];
        for (int i = 0; i < blockMv.Length; i++)
        {
            blockMv[i] = PsliceFixtures.Noise(ref g, 4);
        }

        for (int p = 0; p < 3; p++)
        {
            int kiPw = p != 0 ? kiW >> 1 : kiW, kiPh = p != 0 ? kiH >> 1 : kiH;
            byte* pSrc = pEncPic->pData[p];
            int kiSrcStride = pEncPic->iLineSize[p];
            byte* pOrg = pOrigPic->pData[p];
            int kiOrgStride = pOrigPic->iLineSize[p];
            if (motion == 4)
            {
                IsliceFixtures.FillPlane(ref g, pSrc, kiSrcStride, kiPw, kiPh, content);
                continue;
            }
            for (int y = 0; y < kiPh; y++)
            {
                for (int x = 0; x < kiPw; x++)
                {
                    int mx = dx, my = dy;
                    if (motion == 3)
                    {
                        int b = ((p != 0 ? y << 1 : y) >> 3) * kiBlocksX + ((p != 0 ? x << 1 : x) >> 3);
                        mx = blockMv[2 * b];
                        my = blockMv[2 * b + 1];
                    }
                    if (p != 0)
                    {
                        mx >>= 1;
                        my >>= 1;
                    }
                    int sx = PsliceFixtures.Clip3(x + mx, 0, kiPw - 1), sy = PsliceFixtures.Clip3(y + my, 0, kiPh - 1);
                    int v = pOrg[sy * kiOrgStride + sx];
                    if ((hx | hy) != 0)
                    {
                        int sx2 = PsliceFixtures.Clip3(sx + hx, 0, kiPw - 1), sy2 = PsliceFixtures.Clip3(sy + hy, 0, kiPh - 1);
                        v = (v + pOrg[sy2 * kiOrgStride + sx2] + 1) >> 1;
                    }
                    pSrc[y * kiSrcStride + x] = (byte)IsliceFixtures.Clip255(v + PsliceFixtures.Noise(ref g, noise));
                }
            }
        }

        for (int p = 0; p < 3; p++)
        {
            int kiPw = p != 0 ? kiW >> 1 : kiW, kiPh = p != 0 ? kiH >> 1 : kiH;
            byte* pRef = pRefPic->pData[p];
            byte* pOrg = pOrigPic->pData[p];
            for (int y = 0; y < kiPh; y++)
            {
                for (int x = 0; x < kiPw; x++)
                {
                    pRef[y * pRefPic->iLineSize[p] + x] =
                        (byte)IsliceFixtures.Clip255(pOrg[y * pOrigPic->iLineSize[p] + x] + PsliceFixtures.Noise(ref g, noise >> 1));
                }
            }
        }
        ExpandReferencingPicture((byte**)&pRefPic->pData, pRefPic->iWidthInPixel, pRefPic->iHeightInPixel, pRefPic->iLineSize,
                                 C.Funcs->sExpandPicFunc.pfExpandLumaPicture, C.Funcs->sExpandPicFunc.pfExpandChromaPicture);

        byte* pEncY = pEncPic->pData[0], pOrigY = pOrigPic->pData[0];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                int kiX = (C.Mbs[i].iMbX << 4) + ((k & 1) << 3), kiY = (C.Mbs[i].iMbY << 4) + ((k >> 1) << 3);
                int iSad = 0;
                for (int y = kiY; y < kiY + 8; y++)
                {
                    for (int x = kiX; x < kiX + 8; x++)
                    {
                        iSad += Math.Abs(pEncY[y * pEncPic->iLineSize[0] + x] - pOrigY[y * pOrigPic->iLineSize[0] + x]);
                    }
                }
                Sad8x8[i * 4 + k] = iSad;
            }
        }
    }

    /// <summary>fixture PsliceCtx::HashCache: the macroblock cache after a slice, inter state included.</summary>
    public void HashCache(FixtureHasher h)
    {
        C.HashCache(h);
        SMbCache* m = &C.Slice->sMbCacheInfo;
        PsliceFixtures.HashMvCache(h, &m->sMvComponents);
        h.Add(new ReadOnlySpan<byte>(m->iSadCost, 4 * sizeof(int)));
        h.Add(new ReadOnlySpan<byte>(&m->sMbMvp, 16 * sizeof(SMVUnitXY)));
        h.Add(new ReadOnlySpan<byte>(m->iSadCostSkip, 4 * sizeof(int)));
        h.Add(new ReadOnlySpan<byte>(m->bMbTypeSkip, 4));
        h.AddI32(m->bCollocatedPredFlag ? 1 : 0);
        h.AddI32((int)m->uiRefMbType);
        h.AddI32(IsliceFixtures.Region(m->pEncSad, C.DecPic->pMbSkipSad, C.MbCount * sizeof(int)));
        for (int i = 0; i < 3; i++)
        {
            h.AddI32(IsliceFixtures.Region((byte*)m->SPicData.pRefMb[i], _pRefPic->pBuffer, IsliceFixtures.PictureBufferSize(_pRefPic)));
        }
    }

    /// <summary>fixture PsliceCtx::HashSlice: the slice's motion search state (the last macroblock's).</summary>
    public void HashSlice(FixtureHasher h)
    {
        SSlice* s = C.Slice;
        h.AddI32(s->uiMvcNum);
        h.Add(new ReadOnlySpan<byte>(&s->sMvc, 5 * sizeof(SMVUnitXY)));
        h.Add(new ReadOnlySpan<byte>(&s->sMvStartMin, sizeof(SMVUnitXY)));
        h.Add(new ReadOnlySpan<byte>(&s->sMvStartMax, sizeof(SMVUnitXY)));
    }

    /// <summary>fixture PsliceCtx::HashFrame: the frame after all its slices, inter state included.</summary>
    public void HashFrame(FixtureHasher h)
    {
        C.HashFrame(h);
        int n = C.MbCount;
        for (int i = 0; i < n; i++)
        {
            h.Add(new ReadOnlySpan<byte>(&C.Mbs[i].sP16x16Mv, sizeof(SMVUnitXY)));
            h.Add(new ReadOnlySpan<byte>(C.Mbs[i].uiSubMbType, 4));
        }
        h.Add(new ReadOnlySpan<byte>(C.DecPic->uiRefMbType, n * sizeof(uint)));
        h.Add(new ReadOnlySpan<byte>(C.DecPic->sMvList, n * sizeof(SMVUnitXY)));
        h.Add(new ReadOnlySpan<byte>(C.DecPic->pMbSkipSad, n * sizeof(int)));
        h.Add(new ReadOnlySpan<byte>(_pRefPic->pBuffer, IsliceFixtures.PictureBufferSize(_pRefPic)));
        h.Add(new ReadOnlySpan<byte>(_pOrigPic->pBuffer, IsliceFixtures.PictureBufferSize(_pOrigPic)));
        h.Add(new ReadOnlySpan<byte>(BgFlag, n));
    }

    public void Dispose()
    {
        if (Vaa == null)
        {
            return;
        }
        IWelsParametersetStrategy.DeleteParametersetStrategy(ref C.Funcs->pParametersetStrategy);
        NativeMemory.Free(Vaa);
        NativeMemory.Free(AqDeltaQp);
        NativeMemory.Free(BgFlag);
        NativeMemory.Free(Sad8x8);
        Vaa = null;
        SPicture* pOrig = _pOrigPic, pRef = _pRefPic;
        FreePicture(C.Ma, &pOrig);
        FreePicture(C.Ma, &pRef);
        _pOrigPic = _pRefPic = null;
        C.Ma->WelsFree(MvdCostTable, "pMvdCostTable");
        MvdCostTable = null;
        C.Dispose();
    }
}
