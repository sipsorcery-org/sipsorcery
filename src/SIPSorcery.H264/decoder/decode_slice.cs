//-----------------------------------------------------------------------------
// Filename: decode_slice.cs
//
// Description: Port of codec/decoder/core/inc/decode_slice.h and
// src/decode_slice.cpp: slice data decoding.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// So far (D2) the parse side for CAVLC I and P slices: WelsDecodeSlice and the
// WelsDecodeMbCavlcI/PSlice macroblock parsers, the intra mode parsers, the
// neighbour-to-sample maps, the dequant scaling list setup and the two DC
// transforms the residual parser calls (WelsLumaDcDequantIdct,
// WelsChromaDcIdct). D3 adds intra macroblock reconstruction
// (WelsMbIntraPredictionConstruction, WelsTargetMbConstruction), and D4 the
// P macroblocks (CheckRefPics, WelsMbInterPrediction, WelsMbInterConstruction,
// WelsMbInterSampleConstruction) with WelsBlockFuncInit and its block-zero
// kernels; their B slice branches log and return ERR_INFO_UNSUPPORTED_BIPRED.
// D5 adds the slice loop, WelsTargetSliceConstruction, which reconstructs the
// slice and then deblocks it. WelsDecodeAndConstructSlice, the threaded
// decoder's parse-and-construct loop, is not ported, as the threaded decoder
// is not. CABAC is not ported yet: WelsDecodeSlice logs and
// returns ERR_INFO_UNSUPPORTED_CABAC_EL for a CABAC PPS, and the CABAC
// branches of the intra mode parsers (unreachable from the CAVLC parsers)
// return the same. CAVLC B slices are not ported yet either:
// WelsDecodeMbCavlcBSlice logs and returns ERR_INFO_UNSUPPORTED_BIPRED. The
// MB parsers' uninitialised C stack caches (ENFORCE_STACK_ALIGN_1D, the
// iMotionVector/iRefIndex arrays) are zero-filled stackallocs here; the parse
// reads only entries it has written, so the contents never differ.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D2).
// 25 Nov 2026  Claude          D3: intra macroblock reconstruction.
// 25 Nov 2026  Claude          D4: inter macroblock reconstruction.
// 25 Nov 2026  Claude          D5: WelsTargetSliceConstruction.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2008-2013, Cisco Systems
 *     All rights reserved.
 *
 *     Redistribution and use in source and binary forms, with or without
 *     modification, are permitted provided that the following conditions
 *     are met:
 *
 *        * Redistributions of source code must retain the above copyright
 *          notice, this list of conditions and the following disclaimer.
 *
 *        * Redistributions in binary form must reproduce the above copyright
 *          notice, this list of conditions and the following disclaimer in
 *          the documentation and/or other materials provided with the
 *          distribution.
 *
 *     THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 *     "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 *     LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
 *     FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
 *     COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
 *     INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
 *     BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 *     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 *     CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 *     LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
 *     ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 *     POSSIBILITY OF SUCH DAMAGE.
 *
 *
 *  Abstract
 *      current slice decoding
 *
 *  History
 *      07/10/2008 Created
 *      08/09/2013 Modified
 *
 *****************************************************************************/

// typedef int32_t (*PWelsDecMbFunc) (PWelsDecoderContext pCtx, PNalUnit pNalCur, uint32_t& uiEosFlag);
global using unsafe PWelsDecMbFunc = delegate*<SIPSorcery.H264.Decoder.SWelsDecoderContext*, SIPSorcery.H264.Decoder.SNalUnit*, ref uint, int>;

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    // Raw byte size of one 4:2:0 8-bit I_PCM macroblock copied verbatim from the
    // bitstream: 16x16 luma + 2 x (8x8) chroma.
    public const int I_PCM_MB_SIZE_IN_BYTE = (16 * 16 + 2 * (8 * 8));

    private static bool CheckRefPics(SWelsDecoderContext* pCtx)
    {
        int32_t listCount = 1;
        if (pCtx->eSliceType == B_SLICE)
        {
            ++listCount;
        }
        for (int32_t list = (int32_t)LIST_0; list < listCount; ++list)
        {
            int32_t shortRefCount = pCtx->sRefPic.uiShortRefCount[list];
            for (int32_t refIdx = 0; refIdx < shortRefCount; ++refIdx)
            {
                if (pCtx->sRefPic.pShortRefList[list][refIdx].p == null)
                {
                    return false;
                }
            }
            int32_t longRefCount = pCtx->sRefPic.uiLongRefCount[list];
            for (int32_t refIdx = 0; refIdx < longRefCount; ++refIdx)
            {
                if (pCtx->sRefPic.pLongRefList[list][refIdx].p == null)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public static int32_t WelsTargetSliceConstruction(SWelsDecoderContext* pCtx)
    {
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SSlice* pCurSlice = &pCurDqLayer->sLayerInfo.sSliceInLayer;
        SSliceHeader* pSliceHeader = &pCurSlice->sSliceHeaderExt.sSliceHeader;

        int32_t iTotalMbTargetLayer = (int32_t)pSliceHeader->pSps->uiTotalMbCount;

        int32_t iCurLayerWidth = pCurDqLayer->iMbWidth << 4;
        int32_t iCurLayerHeight = pCurDqLayer->iMbHeight << 4;

        int32_t iNextMbXyIndex = 0;
        SFmo* pFmo = pCtx->pFmo;

        int32_t iTotalNumMb = pCurSlice->iTotalMbInCurSlice;
        int32_t iCountNumMb = 0;
        PDeblockingFilterMbFunc pDeblockMb = &WelsDeblockingMb;

        if (!pCtx->sSpsPpsCtx.bAvcBasedFlag && iCurLayerWidth != pCtx->iCurSeqIntervalMaxPicWidth)
        {
            return ERR_INFO_WIDTH_MISMATCH;
        }

        iNextMbXyIndex = pSliceHeader->iFirstMbInSlice;
        pCurDqLayer->iMbX = iNextMbXyIndex % pCurDqLayer->iMbWidth;
        pCurDqLayer->iMbY = iNextMbXyIndex / pCurDqLayer->iMbWidth;
        pCurDqLayer->iMbXyIndex = iNextMbXyIndex;

        if (0 == iNextMbXyIndex)
        {
            pCurDqLayer->pDec->iSpsId = pCtx->pSps->iSpsId;
            pCurDqLayer->pDec->iPpsId = pCtx->pPps->iPpsId;

            pCurDqLayer->pDec->uiQualityId = pCurDqLayer->sLayerInfo.sNalHeaderExt.uiQualityId;
        }

        do
        {
            if (iCountNumMb >= iTotalNumMb)
            {
                break;
            }

            if (!pCtx->pParam->bParseOnly)
            { //for parse only, actual recon MB unnecessary
                if (WelsTargetMbConstruction(pCtx) != 0)
                {
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                        $"WelsTargetSliceConstruction():::MB({pCurDqLayer->iMbX}, {pCurDqLayer->iMbY}) construction error. pCurSlice_type:{pCurSlice->eSliceType}");

                    return ERR_INFO_MB_RECON_FAIL;
                }
            }

            ++iCountNumMb;
            if (!pCurDqLayer->pMbCorrectlyDecodedFlag[iNextMbXyIndex])
            { //already con-ed, overwrite
                pCurDqLayer->pMbCorrectlyDecodedFlag[iNextMbXyIndex] = true;
                pCtx->pDec->iMbEcedPropNum += (pCurDqLayer->pMbRefConcealedFlag[iNextMbXyIndex] ? 1 : 0);
                ++pCtx->iTotalNumMbRec;
            }

            if (pCtx->iTotalNumMbRec > iTotalMbTargetLayer)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                    $"WelsTargetSliceConstruction():::pCtx->iTotalNumMbRec:{pCtx->iTotalNumMbRec}, iTotalMbTargetLayer:{iTotalMbTargetLayer}");

                return ERR_INFO_MB_NUM_EXCEED_FAIL;
            }

            if (pSliceHeader->pPps->uiNumSliceGroups > 1)
            {
                iNextMbXyIndex = FmoNextMb(pFmo, iNextMbXyIndex);
            }
            else
            {
                ++iNextMbXyIndex;
            }
            if (-1 == iNextMbXyIndex || iNextMbXyIndex >= iTotalMbTargetLayer)
            { // slice group boundary or end of a frame
                break;
            }
            pCurDqLayer->iMbX = iNextMbXyIndex % pCurDqLayer->iMbWidth;
            pCurDqLayer->iMbY = iNextMbXyIndex / pCurDqLayer->iMbWidth;
            pCurDqLayer->iMbXyIndex = iNextMbXyIndex;
        } while (true);

        pCtx->pDec->iWidthInPixel = iCurLayerWidth;
        pCtx->pDec->iHeightInPixel = iCurLayerHeight;

        if ((pCurSlice->eSliceType != (uint8_t)I_SLICE) && (pCurSlice->eSliceType != (uint8_t)P_SLICE)
            && (pCurSlice->eSliceType != (uint8_t)B_SLICE))
        {
            return ERR_NONE; //no error but just ignore the type unsupported
        }

        if (pCtx->pParam->bParseOnly)
        { //for parse only, deblocking should not go on
            return ERR_NONE;
        }

        if (1 == pSliceHeader->uiDisableDeblockingFilterIdc
            || pCtx->pCurDqLayer->sLayerInfo.sSliceInLayer.iTotalMbInCurSlice <= 0)
        {
            return ERR_NONE;//NO_SUPPORTED_FILTER_IDX
        }
        else
        {
            WelsDeblockingFilterSlice(pCtx, pDeblockMb);
        }
        // any other filter_idc not supported here, 7/22/2010

        return ERR_NONE;
    }

    public static int32_t WelsMbInterSampleConstruction(SWelsDecoderContext* pCtx, SDqLayer* pCurDqLayer,
        uint8_t* pDstY, uint8_t* pDstU, uint8_t* pDstV, int32_t iStrideL, int32_t iStrideC)
    {
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        int32_t i, iIndex, iOffset;

        if (pCurDqLayer->pTransformSize8x8Flag[iMbXy])
        {
            for (i = 0; i < 4; i++)
            {
                iIndex = g_kuiMbCountScan4Idx[i << 2];
                if (pCurDqLayer->pNzc[iMbXy][iIndex] != 0 || pCurDqLayer->pNzc[iMbXy][iIndex + 1] != 0 || pCurDqLayer->pNzc[iMbXy][iIndex + 4] != 0
                    || pCurDqLayer->pNzc[iMbXy][iIndex + 5] != 0)
                {
                    iOffset = ((iIndex >> 2) << 2) * iStrideL + ((iIndex % 4) << 2);
                    pCtx->pIdctResAddPredFunc8x8(pDstY + iOffset, iStrideL, (int16_t*)&pCurDqLayer->pScaledTCoeff[iMbXy] + (i << 6));
                }
            }
        }
        else
        {
            // luma.
            int8_t* pNzcL = (int8_t*)&pCurDqLayer->pNzc[iMbXy];
            int16_t* pScaledTCoeffL = (int16_t*)&pCurDqLayer->pScaledTCoeff[iMbXy];
            pCtx->pIdctFourResAddPredFunc(pDstY + 0 * iStrideL + 0, iStrideL, pScaledTCoeffL + 0 * 64, pNzcL + 0);
            pCtx->pIdctFourResAddPredFunc(pDstY + 0 * iStrideL + 8, iStrideL, pScaledTCoeffL + 1 * 64, pNzcL + 2);
            pCtx->pIdctFourResAddPredFunc(pDstY + 8 * iStrideL + 0, iStrideL, pScaledTCoeffL + 2 * 64, pNzcL + 8);
            pCtx->pIdctFourResAddPredFunc(pDstY + 8 * iStrideL + 8, iStrideL, pScaledTCoeffL + 3 * 64, pNzcL + 10);
        }

        int8_t* pNzc = (int8_t*)&pCurDqLayer->pNzc[iMbXy];
        int16_t* pScaledTCoeff = (int16_t*)&pCurDqLayer->pScaledTCoeff[iMbXy];
        // Cb.
        pCtx->pIdctFourResAddPredFunc(pDstU, iStrideC, pScaledTCoeff + 4 * 64, pNzc + 16);
        // Cr.
        pCtx->pIdctFourResAddPredFunc(pDstV, iStrideC, pScaledTCoeff + 5 * 64, pNzc + 18);

        return ERR_NONE;
    }

    // The B slice branch (pTempDec, GetInterBPred) is not ported yet: it logs
    // and returns ERR_INFO_UNSUPPORTED_BIPRED.
    public static int32_t WelsMbInterConstruction(SWelsDecoderContext* pCtx, SDqLayer* pCurDqLayer)
    {
        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbY = pCurDqLayer->iMbY;
        uint8_t* pDstY, pDstCb, pDstCr;

        int32_t iLumaStride = pCtx->pDec->iLinesize[0];
        int32_t iChromaStride = pCtx->pDec->iLinesize[1];

        pDstY = pCurDqLayer->pDec->pData[0] + ((iMbY * iLumaStride + iMbX) << 4);
        pDstCb = pCurDqLayer->pDec->pData[1] + ((iMbY * iChromaStride + iMbX) << 3);
        pDstCr = pCurDqLayer->pDec->pData[2] + ((iMbY * iChromaStride + iMbX) << 3);

        if (pCtx->eSliceType == P_SLICE)
        {
            uint32_t uiRetTmp = (uint32_t)GetInterPred(pDstY, pDstCb, pDstCr, pCtx);
            if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; // WELS_B_MB_REC_VERIFY
        }
        else
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "WelsMbInterConstruction()::::B slices are not supported by this port yet.");
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_BIPRED);
        }
        WelsMbInterSampleConstruction(pCtx, pCurDqLayer, pDstY, pDstCb, pDstCr, iLumaStride, iChromaStride);

        if (GetThreadCount(pCtx) <= 1)
        {
            pCtx->sBlockFunc.pWelsSetNonZeroCountFunc(
                (int8_t*)&pCurDqLayer->pNzc[pCurDqLayer->iMbXyIndex]); // set all none-zero nzc to 1; dbk can be opti!
        }
        return ERR_NONE;
    }

    // WelsLumaDcDequantIdct's static const tables (STRIDE 16).
    private static readonly int32_t* s_kiLumaDcXOffset = StaticTable.Of<int32_t>(4, [0, 16, 16 << 2, 5 * 16]);
    private static readonly int32_t* s_kiLumaDcYOffset = StaticTable.Of<int32_t>(4, [0, 16 << 1, 16 << 3, 10 * 16]);

    public static void WelsLumaDcDequantIdct(int16_t* pBlock, int32_t iQp, SWelsDecoderContext* pCtx)
    {
        int32_t kiQMul = pCtx->bUseScalingList ? pCtx->pDequant_coeff4x4[0].p[iQp][0] : (g_kuiDequantCoeff[iQp][0] << 4);
        const int32_t STRIDE = 16;
        int32_t i;
        int32_t* iTemp = stackalloc int32_t[16]; //FIXME check if this is a good idea
        int16_t* pBlk = pBlock;
        int32_t* kiXOffset = s_kiLumaDcXOffset;
        int32_t* kiYOffset = s_kiLumaDcYOffset;

        for (i = 0; i < 4; i++)
        {
            int32_t kiOffset = kiYOffset[i];
            int32_t kiX1 = kiOffset + kiXOffset[2];
            int32_t kiX2 = STRIDE + kiOffset;
            int32_t kiX3 = kiOffset + kiXOffset[3];
            int32_t kiI4 = i << 2; // 4*i
            int32_t kiZ0 = pBlk[kiOffset] + pBlk[kiX1];
            int32_t kiZ1 = pBlk[kiOffset] - pBlk[kiX1];
            int32_t kiZ2 = pBlk[kiX2] - pBlk[kiX3];
            int32_t kiZ3 = pBlk[kiX2] + pBlk[kiX3];

            iTemp[kiI4] = kiZ0 + kiZ3;
            iTemp[1 + kiI4] = kiZ1 + kiZ2;
            iTemp[2 + kiI4] = kiZ1 - kiZ2;
            iTemp[3 + kiI4] = kiZ0 - kiZ3;
        }

        for (i = 0; i < 4; i++)
        {
            int32_t kiOffset = kiXOffset[i];
            int32_t kiI4 = 4 + i;
            int32_t kiZ0 = iTemp[i] + iTemp[4 + kiI4];
            int32_t kiZ1 = iTemp[i] - iTemp[4 + kiI4];
            int32_t kiZ2 = iTemp[kiI4] - iTemp[8 + kiI4];
            int32_t kiZ3 = iTemp[kiI4] + iTemp[8 + kiI4];

            pBlk[kiOffset] = (int16_t)(((kiZ0 + kiZ3) * kiQMul + (1 << 5)) >> 6); //FIXME think about merging this into decode_resdual
            pBlk[kiYOffset[1] + kiOffset] = (int16_t)(((kiZ1 + kiZ2) * kiQMul + (1 << 5)) >> 6);
            pBlk[kiYOffset[2] + kiOffset] = (int16_t)(((kiZ1 - kiZ2) * kiQMul + (1 << 5)) >> 6);
            pBlk[kiYOffset[3] + kiOffset] = (int16_t)(((kiZ0 - kiZ3) * kiQMul + (1 << 5)) >> 6);
        }
    }

    public static int32_t WelsMbIntraPredictionConstruction(SWelsDecoderContext* pCtx, SDqLayer* pCurDqLayer, bool bOutput)
    {
        //seems IPCM should not enter this path
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;

        WelsFillRecNeededMbInfo(pCtx, bOutput, pCurDqLayer);

        if (IS_INTRA16x16(pCurDqLayer->pDec->pMbType[iMbXy]))
        {
            RecI16x16Mb(iMbXy, pCtx, (int16_t*)&pCurDqLayer->pScaledTCoeff[iMbXy], pCurDqLayer);
        }
        else if (IS_INTRA8x8(pCurDqLayer->pDec->pMbType[iMbXy]))
        {
            RecI8x8Mb(iMbXy, pCtx, (int16_t*)&pCurDqLayer->pScaledTCoeff[iMbXy], pCurDqLayer);
        }
        else if (IS_INTRA4x4(pCurDqLayer->pDec->pMbType[iMbXy]))
        {
            RecI4x4Mb(iMbXy, pCtx, (int16_t*)&pCurDqLayer->pScaledTCoeff[iMbXy], pCurDqLayer);
        }
        return ERR_NONE;
    }

    // The B slice branch (pTempDec, GetInterBPred) is not ported yet: it logs
    // and returns ERR_INFO_UNSUPPORTED_BIPRED.
    public static int32_t WelsMbInterPrediction(SWelsDecoderContext* pCtx, SDqLayer* pCurDqLayer)
    {
        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbY = pCurDqLayer->iMbY;
        uint8_t* pDstY, pDstCb, pDstCr;

        int32_t iLumaStride = pCtx->pDec->iLinesize[0];
        int32_t iChromaStride = pCtx->pDec->iLinesize[1];

        pDstY = pCurDqLayer->pDec->pData[0] + ((iMbY * iLumaStride + iMbX) << 4);
        pDstCb = pCurDqLayer->pDec->pData[1] + ((iMbY * iChromaStride + iMbX) << 3);
        pDstCr = pCurDqLayer->pDec->pData[2] + ((iMbY * iChromaStride + iMbX) << 3);

        if (pCtx->eSliceType == P_SLICE)
        {
            uint32_t uiRetTmp = (uint32_t)GetInterPred(pDstY, pDstCb, pDstCr, pCtx);
            if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; // WELS_B_MB_REC_VERIFY
        }
        else
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "WelsMbInterPrediction()::::B slices are not supported by this port yet.");
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_BIPRED);
        }
        return ERR_NONE;
    }

    public static int32_t WelsTargetMbConstruction(SWelsDecoderContext* pCtx)
    {
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        if (MB_TYPE_INTRA_PCM == pCurDqLayer->pDec->pMbType[pCurDqLayer->iMbXyIndex])
        {
            //already decoded and reconstructed when parsing
            return ERR_NONE;
        }
        else if (IS_INTRA(pCurDqLayer->pDec->pMbType[pCurDqLayer->iMbXyIndex]))
        {
            WelsMbIntraPredictionConstruction(pCtx, pCurDqLayer, true);
        }
        else if (IS_INTER(pCurDqLayer->pDec->pMbType[pCurDqLayer->iMbXyIndex]))
        { //InterMB
            if (0 == pCurDqLayer->pCbp[pCurDqLayer->iMbXyIndex])
            { //uiCbp==0 include SKIP
                if (!CheckRefPics(pCtx))
                {
                    return ERR_INFO_MB_RECON_FAIL;
                }
                return WelsMbInterPrediction(pCtx, pCurDqLayer);
            }
            else
            {
                WelsMbInterConstruction(pCtx, pCurDqLayer);
            }
        }
        else
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                    $"WelsTargetMbConstruction():::::Unknown MB type: {pCurDqLayer->pDec->pMbType[pCurDqLayer->iMbXyIndex]}");
            return ERR_INFO_MB_RECON_FAIL;
        }

        return ERR_NONE;
    }

    public static void WelsChromaDcIdct(int16_t* pBlock)
    {
        int32_t iStride = 32;
        int32_t iXStride = 16;
        int32_t iStride1 = iXStride + iStride;
        int16_t* pBlk = pBlock;
        int32_t iA, iB, iC, iD, iE;

        iA = pBlk[0];
        iB = pBlk[iXStride];
        iC = pBlk[iStride];
        iD = pBlk[iStride1];

        iE = iA - iB;
        iA += iB;
        iB = iC - iD;
        iC += iD;

        pBlk[0] = (int16_t)(iA + iC);
        pBlk[iXStride] = (int16_t)(iE + iB);
        pBlk[iStride] = (int16_t)(iA - iC);
        pBlk[iStride1] = (int16_t)(iE - iB);
    }

    public static void WelsMapNxNNeighToSampleNormal(SWelsNeighAvail* pNeighAvail, int32_t* pSampleAvail)
    {
        if (pNeighAvail->iLeftAvail != 0)
        {  //left
            pSampleAvail[6] =
              pSampleAvail[12] =
                pSampleAvail[18] =
                  pSampleAvail[24] = 1;
        }
        if (pNeighAvail->iLeftTopAvail != 0)
        { //top_left
            pSampleAvail[0] = 1;
        }
        if (pNeighAvail->iTopAvail != 0)
        { //top
            pSampleAvail[1] =
              pSampleAvail[2] =
                pSampleAvail[3] =
                  pSampleAvail[4] = 1;
        }
        if (pNeighAvail->iRightTopAvail != 0)
        { //top_right
            pSampleAvail[5] = 1;
        }
    }

    public static void WelsMapNxNNeighToSampleConstrain1(SWelsNeighAvail* pNeighAvail, int32_t* pSampleAvail)
    {
        if (pNeighAvail->iLeftAvail != 0 && IS_INTRA((uint32_t)pNeighAvail->iLeftType))
        {   //left
            pSampleAvail[6] =
              pSampleAvail[12] =
                pSampleAvail[18] =
                  pSampleAvail[24] = 1;
        }
        if (pNeighAvail->iLeftTopAvail != 0 && IS_INTRA((uint32_t)pNeighAvail->iLeftTopType))
        {  //top_left
            pSampleAvail[0] = 1;
        }
        if (pNeighAvail->iTopAvail != 0 && IS_INTRA((uint32_t)pNeighAvail->iTopType))
        {  //top
            pSampleAvail[1] =
              pSampleAvail[2] =
                pSampleAvail[3] =
                  pSampleAvail[4] = 1;
        }
        if (pNeighAvail->iRightTopAvail != 0 && IS_INTRA((uint32_t)pNeighAvail->iRightTopType))
        {  //top_right
            pSampleAvail[5] = 1;
        }
    }
    public static void WelsMap16x16NeighToSampleNormal(SWelsNeighAvail* pNeighAvail, uint8_t* pSampleAvail)
    {
        if (pNeighAvail->iLeftAvail != 0)
        {
            *pSampleAvail = (1 << 2);
        }
        if (pNeighAvail->iLeftTopAvail != 0)
        {
            *pSampleAvail |= (1 << 1);
        }
        if (pNeighAvail->iTopAvail != 0)
        {
            *pSampleAvail |= 1;
        }
    }

    public static void WelsMap16x16NeighToSampleConstrain1(SWelsNeighAvail* pNeighAvail, uint8_t* pSampleAvail)
    {
        if (pNeighAvail->iLeftAvail != 0 && IS_INTRA((uint32_t)pNeighAvail->iLeftType))
        {
            *pSampleAvail = (1 << 2);
        }
        if (pNeighAvail->iLeftTopAvail != 0 && IS_INTRA((uint32_t)pNeighAvail->iLeftTopType))
        {
            *pSampleAvail |= (1 << 1);
        }
        if (pNeighAvail->iTopAvail != 0 && IS_INTRA((uint32_t)pNeighAvail->iTopType))
        {
            *pSampleAvail |= 1;
        }
    }

    public static int32_t ParseIntra4x4Mode(SWelsDecoderContext* pCtx, SWelsNeighAvail* pNeighAvail, int8_t* pIntraPredMode,
                                            SBitStringAux* pBs,
                                            SDqLayer* pCurDqLayer)
    {
        int32_t* iSampleAvail = stackalloc int32_t[5 * 6];
        memset(iSampleAvail, 0, 5 * 6 * sizeof(int32_t)); //initialize as 0
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        int32_t iFinalMode, i;

        uint8_t uiNeighAvail = 0;
        uint32_t uiCode;
        uint32_t uiRetTmp; // WELS_READ_VERIFY
        pCtx->pMapNxNNeighToSampleFunc(pNeighAvail, iSampleAvail);
        uiNeighAvail = (uint8_t)((iSampleAvail[6] << 2) | (iSampleAvail[0] << 1) | (iSampleAvail[1]));
        for (i = 0; i < 16; i++)
        {
            int32_t iPrevIntra4x4PredMode = 0;
            if (pCurDqLayer->sLayerInfo.pPps->bEntropyCodingModeFlag)
            {
                // ParseIntraPredModeLumaCabac: not ported yet (CABAC).
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_CABAC_EL);
            }
            else
            {
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                iPrevIntra4x4PredMode = (int32_t)uiCode;
            }
            int32_t kiPredMode = PredIntra4x4Mode(pIntraPredMode, i);

            int8_t iBestMode;
            if (pCurDqLayer->sLayerInfo.pPps->bEntropyCodingModeFlag)
            {
                if (iPrevIntra4x4PredMode == -1)
                    iBestMode = (int8_t)kiPredMode;
                else
                    iBestMode = (int8_t)(iPrevIntra4x4PredMode + ((iPrevIntra4x4PredMode >= kiPredMode) ? 1 : 0));
            }
            else
            {
                if (iPrevIntra4x4PredMode != 0)
                {
                    iBestMode = (int8_t)kiPredMode;
                }
                else
                {
                    if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 3, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                    iBestMode = (int8_t)(uiCode + (((int32_t)uiCode >= kiPredMode) ? 1u : 0u));
                }
            }

            iFinalMode = CheckIntraNxNPredMode(&iSampleAvail[0], &iBestMode, i, false);
            if (iFinalMode == GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INVALID_INTRA4X4_MODE))
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I4x4_PRED_MODE);
            }

            pCurDqLayer->pIntra4x4FinalMode[iMbXy][g_kuiScan4[i]] = (int8_t)iFinalMode;

            pIntraPredMode[g_kuiScan8[i]] = iBestMode;

            iSampleAvail[g_kuiCache30ScanIdx[i]] = 1;
        }
        ST32(&pCurDqLayer->pIntraPredMode[iMbXy][0], LD32(&pIntraPredMode[1 + 8 * 4]));
        pCurDqLayer->pIntraPredMode[iMbXy][4] = pIntraPredMode[4 + 8 * 1];
        pCurDqLayer->pIntraPredMode[iMbXy][5] = pIntraPredMode[4 + 8 * 2];
        pCurDqLayer->pIntraPredMode[iMbXy][6] = pIntraPredMode[4 + 8 * 3];

        if (pCtx->pSps->uiChromaFormatIdc == 0)//no need parse chroma
            return ERR_NONE;

        if (pCurDqLayer->sLayerInfo.pPps->bEntropyCodingModeFlag)
        {
            // ParseIntraPredModeChromaCabac: not ported yet (CABAC).
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_CABAC_EL);
        }
        else
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //intra_chroma_pred_mode
            if (uiCode > MAX_PRED_MODE_ID_CHROMA)
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I_CHROMA_PRED_MODE);
            }
            pCurDqLayer->pChromaPredMode[iMbXy] = (int8_t)uiCode;
        }

        if (-1 == pCurDqLayer->pChromaPredMode[iMbXy]
            || CheckIntraChromaPredMode(uiNeighAvail, &pCurDqLayer->pChromaPredMode[iMbXy]) != 0)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I_CHROMA_PRED_MODE);
        }
        return ERR_NONE;
    }

    public static int32_t ParseIntra8x8Mode(SWelsDecoderContext* pCtx, SWelsNeighAvail* pNeighAvail, int8_t* pIntraPredMode,
                                            SBitStringAux* pBs,
                                            SDqLayer* pCurDqLayer)
    {
        // Similar with Intra_4x4, can put them together when needed
        int32_t* iSampleAvail = stackalloc int32_t[5 * 6];
        memset(iSampleAvail, 0, 5 * 6 * sizeof(int32_t)); //initialize as 0
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        int32_t iFinalMode, i;

        uint8_t uiNeighAvail = 0;
        uint32_t uiCode;
        uint32_t uiRetTmp; // WELS_READ_VERIFY
        pCtx->pMapNxNNeighToSampleFunc(pNeighAvail, iSampleAvail);
        // Top-Right : Left : Top-Left : Top
        uiNeighAvail = (uint8_t)((iSampleAvail[5] << 3) | (iSampleAvail[6] << 2) | (iSampleAvail[0] << 1) | (iSampleAvail[1]));

        pCurDqLayer->pIntraNxNAvailFlag[iMbXy] = uiNeighAvail;

        for (i = 0; i < 4; i++)
        {
            int32_t iPrevIntra4x4PredMode = 0;
            if (pCurDqLayer->sLayerInfo.pPps->bEntropyCodingModeFlag)
            {
                // ParseIntraPredModeLumaCabac: not ported yet (CABAC).
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_CABAC_EL);
            }
            else
            {
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                iPrevIntra4x4PredMode = (int32_t)uiCode;
            }
            int32_t kiPredMode = PredIntra4x4Mode(pIntraPredMode, i << 2);

            int8_t iBestMode;
            if (pCurDqLayer->sLayerInfo.pPps->bEntropyCodingModeFlag)
            {
                if (iPrevIntra4x4PredMode == -1)
                    iBestMode = (int8_t)kiPredMode;
                else
                    iBestMode = (int8_t)(iPrevIntra4x4PredMode + ((iPrevIntra4x4PredMode >= kiPredMode) ? 1 : 0));
            }
            else
            {
                if (iPrevIntra4x4PredMode != 0)
                {
                    iBestMode = (int8_t)kiPredMode;
                }
                else
                {
                    if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 3, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                    iBestMode = (int8_t)(uiCode + (((int32_t)uiCode >= kiPredMode) ? 1u : 0u));
                }
            }

            iFinalMode = CheckIntraNxNPredMode(&iSampleAvail[0], &iBestMode, i << 2, true);

            if (iFinalMode == GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INVALID_INTRA4X4_MODE))
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I4x4_PRED_MODE);
            }

            for (int j = 0; j < 4; j++)
            {
                pCurDqLayer->pIntra4x4FinalMode[iMbXy][g_kuiScan4[(i << 2) + j]] = (int8_t)iFinalMode;
                pIntraPredMode[g_kuiScan8[(i << 2) + j]] = iBestMode;
                iSampleAvail[g_kuiCache30ScanIdx[(i << 2) + j]] = 1;
            }
        }
        ST32(&pCurDqLayer->pIntraPredMode[iMbXy][0], LD32(&pIntraPredMode[1 + 8 * 4]));
        pCurDqLayer->pIntraPredMode[iMbXy][4] = pIntraPredMode[4 + 8 * 1];
        pCurDqLayer->pIntraPredMode[iMbXy][5] = pIntraPredMode[4 + 8 * 2];
        pCurDqLayer->pIntraPredMode[iMbXy][6] = pIntraPredMode[4 + 8 * 3];

        if (pCtx->pSps->uiChromaFormatIdc == 0)
            return ERR_NONE;

        if (pCurDqLayer->sLayerInfo.pPps->bEntropyCodingModeFlag)
        {
            // ParseIntraPredModeChromaCabac: not ported yet (CABAC).
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_CABAC_EL);
        }
        else
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //intra_chroma_pred_mode
            if (uiCode > MAX_PRED_MODE_ID_CHROMA)
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I_CHROMA_PRED_MODE);
            }
            pCurDqLayer->pChromaPredMode[iMbXy] = (int8_t)uiCode;
        }

        if (-1 == pCurDqLayer->pChromaPredMode[iMbXy]
            || CheckIntraChromaPredMode(uiNeighAvail, &pCurDqLayer->pChromaPredMode[iMbXy]) != 0)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I_CHROMA_PRED_MODE);
        }

        return ERR_NONE;
    }

    public static int32_t ParseIntra16x16Mode(SWelsDecoderContext* pCtx, SWelsNeighAvail* pNeighAvail, SBitStringAux* pBs,
                                              SDqLayer* pCurDqLayer)
    {
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        uint8_t uiNeighAvail = 0; //0x07 = 0 1 1 1, means left, top-left, top avail or not. (1: avail, 0: unavail)
        uint32_t uiCode;
        uint32_t uiRetTmp; // WELS_READ_VERIFY
        pCtx->pMap16x16NeighToSampleFunc(pNeighAvail, &uiNeighAvail);

        if (CheckIntra16x16PredMode(uiNeighAvail,
                                    &pCurDqLayer->pIntraPredMode[iMbXy][7]) != 0)
        { //invalid iPredMode, must stop decoding
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I16x16_PRED_MODE);
        }
        if (pCtx->pSps->uiChromaFormatIdc == 0)
            return ERR_NONE;

        if (pCurDqLayer->sLayerInfo.pPps->bEntropyCodingModeFlag)
        {
            // ParseIntraPredModeChromaCabac: not ported yet (CABAC).
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_CABAC_EL);
        }
        else
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //intra_chroma_pred_mode
            if (uiCode > MAX_PRED_MODE_ID_CHROMA)
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I_CHROMA_PRED_MODE);
            }
            pCurDqLayer->pChromaPredMode[iMbXy] = (int8_t)uiCode;
        }
        if (-1 == pCurDqLayer->pChromaPredMode[iMbXy]
            || CheckIntraChromaPredMode(uiNeighAvail, &pCurDqLayer->pChromaPredMode[iMbXy]) != 0)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I_CHROMA_PRED_MODE);
        }

        return ERR_NONE;
    }

    // WelsDecodeMbCabacISliceBaseMode0 .. WelsDecodeMbCabacBSlice: not ported yet (CABAC).

    // Calculate deqaunt coeff scaling list value
    public static int32_t WelsCalcDeqCoeffScalingList(SWelsDecoderContext* pCtx)
    {
        if (pCtx->pSps->bSeqScalingMatrixPresentFlag || pCtx->pPps->bPicScalingMatrixPresentFlag)
        {
            pCtx->bUseScalingList = true;

            if (!pCtx->bDequantCoeff4x4Init || (pCtx->iDequantCoeffPpsid != pCtx->pPps->iPpsId))
            {
                int i, q, x, y;
                //Init dequant coeff value for different QP
                for (i = 0; i < 6; i++)
                {
                    pCtx->pDequant_coeff4x4[i] = (InlineArray16<uint16_t>*)&pCtx->pDequant_coeff_buffer4x4[i];
                    pCtx->pDequant_coeff8x8[i] = (InlineArray64<uint16_t>*)&pCtx->pDequant_coeff_buffer8x8[i];
                    for (q = 0; q < 51; q++)
                    {
                        for (x = 0; x < 16; x++)
                        {
                            pCtx->pDequant_coeff4x4[i].p[q][x] = (uint16_t)(pCtx->pPps->bPicScalingMatrixPresentFlag ? pCtx->pPps->iScalingList4x4[i][x] *
                                                               g_kuiDequantCoeff[q][x & 0x07] : pCtx->pSps->iScalingList4x4[i][x] * g_kuiDequantCoeff[q][x & 0x07]);
                        }
                        for (y = 0; y < 64; y++)
                        {
                            pCtx->pDequant_coeff8x8[i].p[q][y] = (uint16_t)(pCtx->pPps->bPicScalingMatrixPresentFlag ? pCtx->pPps->iScalingList8x8[i][y] *
                                                               g_kuiMatrixV[q % 6][y / 8][y % 8] : pCtx->pSps->iScalingList8x8[i][y] * g_kuiMatrixV[q % 6][y / 8][y % 8]);
                        }
                    }
                }
                pCtx->bDequantCoeff4x4Init = true;
                pCtx->iDequantCoeffPpsid = pCtx->pPps->iPpsId;
            }
        }
        else
            pCtx->bUseScalingList = false;
        return ERR_NONE;
    }

    public static int32_t WelsDecodeSlice(SWelsDecoderContext* pCtx, bool bFirstSliceInLayer, SNalUnit* pNalCur)
    {
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SFmo* pFmo = pCtx->pFmo;
        int32_t iRet;
        int32_t iNextMbXyIndex, iSliceIdc;

        SSlice* pSlice = &pCurDqLayer->sLayerInfo.sSliceInLayer;
        SSliceHeaderExt* pSliceHeaderExt = &pSlice->sSliceHeaderExt;
        SSliceHeader* pSliceHeader = &pSliceHeaderExt->sSliceHeader;
        int32_t iMbX, iMbY;
        int32_t kiCountNumMb = (int32_t)pSliceHeader->pSps->uiTotalMbCount; //need to be correct when fmo or multi slice
        uint32_t uiEosFlag = 0;
        PWelsDecMbFunc pDecMbFunc;

        pSlice->iTotalMbInCurSlice = 0; //initialize at the starting of slice decoding.

        if (pCtx->pPps->bEntropyCodingModeFlag)
        {
            if (pSlice->sSliceHeaderExt.bAdaptiveMotionPredFlag ||
                pSlice->sSliceHeaderExt.bAdaptiveBaseModeFlag ||
                pSlice->sSliceHeaderExt.bAdaptiveResidualPredFlag)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR,
                        "WelsDecodeSlice()::::ILP flag exist, not supported with CABAC enabled!");
                pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                return (int32_t)dsBitstreamError;
            }
            // C picks WelsDecodeMbCabacP/B/ISlice here, then initialises the CABAC
            // contexts and engine below. Not ported yet.
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "WelsDecodeSlice()::::CABAC is not supported by this port yet.");
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_DATA, ERR_INFO_UNSUPPORTED_CABAC_EL);
        }
        else
        {
            if (P_SLICE == pSliceHeader->eSliceType)
            {
                pDecMbFunc = &WelsDecodeMbCavlcPSlice;
            }
            else if (B_SLICE == pSliceHeader->eSliceType)
            {
                pDecMbFunc = &WelsDecodeMbCavlcBSlice;
            }
            else
            { //I_SLICE
                pDecMbFunc = &WelsDecodeMbCavlcISlice;
            }
        }

        if (pSliceHeader->pPps->bConstainedIntraPredFlag)
        {
            pCtx->pFillInfoCacheIntraNxNFunc = &WelsFillCacheConstrain1IntraNxN;
            pCtx->pMapNxNNeighToSampleFunc = &WelsMapNxNNeighToSampleConstrain1;
            pCtx->pMap16x16NeighToSampleFunc = &WelsMap16x16NeighToSampleConstrain1;
        }
        else
        {
            pCtx->pFillInfoCacheIntraNxNFunc = &WelsFillCacheConstrain0IntraNxN;
            pCtx->pMapNxNNeighToSampleFunc = &WelsMapNxNNeighToSampleNormal;
            pCtx->pMap16x16NeighToSampleFunc = &WelsMap16x16NeighToSampleNormal;
        }

        pCtx->eSliceType = pSliceHeader->eSliceType;
        // C initialises CABAC here when the PPS has it (WelsCabacContextInit,
        // InitCabacDecEngineFromBS); a CABAC PPS has already returned above.
        //try to calculate  the dequant_coeff
        WelsCalcDeqCoeffScalingList(pCtx);

        iNextMbXyIndex = pSliceHeader->iFirstMbInSlice;
        iMbX = iNextMbXyIndex % pCurDqLayer->iMbWidth;
        iMbY = iNextMbXyIndex / pCurDqLayer->iMbWidth; // error is introduced by multiple slices case, 11/23/2009
        pSlice->iMbSkipRun = -1;
        iSliceIdc = (pSliceHeader->iFirstMbInSlice << 7) + pCurDqLayer->uiLayerDqId;

        pCurDqLayer->iMbX = iMbX;
        pCurDqLayer->iMbY = iMbY;
        pCurDqLayer->iMbXyIndex = iNextMbXyIndex;

        do
        {
            if ((-1 == iNextMbXyIndex) || (iNextMbXyIndex >= kiCountNumMb))
            { // slice group boundary or end of a frame
                break;
            }

            pCurDqLayer->pSliceIdc[iNextMbXyIndex] = iSliceIdc;
            pCtx->bMbRefConcealed = false;
            iRet = pDecMbFunc(pCtx, pNalCur, ref uiEosFlag);
            pCurDqLayer->pMbRefConcealedFlag[iNextMbXyIndex] = pCtx->bMbRefConcealed;
            if (iRet != ERR_NONE)
            {
                return iRet;
            }

            ++pSlice->iTotalMbInCurSlice;
            if (uiEosFlag != 0)
            { //end of slice
                break;
            }
            if (pSliceHeader->pPps->uiNumSliceGroups > 1)
            {
                iNextMbXyIndex = FmoNextMb(pFmo, iNextMbXyIndex);
            }
            else
            {
                ++iNextMbXyIndex;
            }
            iMbX = iNextMbXyIndex % pCurDqLayer->iMbWidth;
            iMbY = iNextMbXyIndex / pCurDqLayer->iMbWidth;
            pCurDqLayer->iMbX = iMbX;
            pCurDqLayer->iMbY = iMbY;
            pCurDqLayer->iMbXyIndex = iNextMbXyIndex;
        } while (true);

        return ERR_NONE;
    }

    // WelsDecodeAndConstructSlice: with reconstruction.

    public static int32_t WelsActualDecodeMbCavlcISlice(SWelsDecoderContext* pCtx)
    {
        SVlcTable* pVlcTable = pCtx->pVlcTable;
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SBitStringAux* pBs = pCurDqLayer->pBitStringAux;
        SSlice* pSlice = &pCurDqLayer->sLayerInfo.sSliceInLayer;
        SSliceHeader* pSliceHeader = &pSlice->sSliceHeaderExt.sSliceHeader;

        SWelsNeighAvail sNeighAvail;
        int32_t iMbResProperty;

        int32_t iScanIdxStart = pSlice->sSliceHeaderExt.uiScanIdxStart;
        int32_t iScanIdxEnd = pSlice->sSliceHeaderExt.uiScanIdxEnd;

        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbY = pCurDqLayer->iMbY;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        int8_t* pNzc = (int8_t*)&pCurDqLayer->pNzc[iMbXy];
        int32_t i;
        int32_t iRet = ERR_NONE;
        uint32_t uiMbType = 0, uiCbp = 0, uiCbpL = 0, uiCbpC = 0;
        uint32_t uiCode;
        int32_t iCode;
        uint32_t uiRetTmp; // WELS_READ_VERIFY

        uint8_t* pNonZeroCount = stackalloc uint8_t[48]; // ENFORCE_STACK_ALIGN_1D (uint8_t, pNonZeroCount, 48, 16)
        GetNeighborAvailMbType(&sNeighAvail, pCurDqLayer);
        pCurDqLayer->pInterPredictionDoneFlag[iMbXy] = 0;
        pCurDqLayer->pResidualPredFlag[iMbXy] = (int8_t)(pSlice->sSliceHeaderExt.bDefaultResidualPredFlag ? 1 : 0);

        pCurDqLayer->pNoSubMbPartSizeLessThan8x8Flag[iMbXy] = true;
        pCurDqLayer->pTransformSize8x8Flag[iMbXy] = false;

        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //uiMbType
        uiMbType = uiCode;
        if (uiMbType > 25)
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_MB_TYPE);
        if (pCtx->pSps->uiChromaFormatIdc == 0 && ((uiMbType >= 5 && uiMbType <= 12) || (uiMbType >= 17 && uiMbType <= 24)))
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_MB_TYPE);

        if (25 == uiMbType)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_DEBUG, "I_PCM mode exists in I slice!");
            int32_t iDecStrideL = pCurDqLayer->pDec->iLinesize[0];
            int32_t iDecStrideC = pCurDqLayer->pDec->iLinesize[1];

            int32_t iOffsetL = (iMbX + iMbY * iDecStrideL) << 4;
            int32_t iOffsetC = (iMbX + iMbY * iDecStrideC) << 3;

            uint8_t* pDecY = pCurDqLayer->pDec->pData[0] + iOffsetL;
            uint8_t* pDecU = pCurDqLayer->pDec->pData[1] + iOffsetC;
            uint8_t* pDecV = pCurDqLayer->pDec->pData[2] + iOffsetC;

            uint8_t* pTmpBsBuf;


            // C declares a block-local int32_t i here, shadowing the one above.
            int32_t iCopySizeY = (sizeof(uint8_t) << 4);
            int32_t iCopySizeUV = (sizeof(uint8_t) << 3);

            int32_t iIndex = ((-pBs->iLeftBits) >> 3) + 2;

            pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_INTRA_PCM;

            //step 1: locating bit-stream pointer [must align into integer byte]
            pBs->pCurBuf -= iIndex;

            //bounds check: I_PCM copies I_PCM_MB_SIZE_IN_BYTE (256 luma + 128 chroma) bytes
            //directly from the bitstream buffer; reject when fewer bytes remain to avoid
            //an out-of-bounds read (mirrors ParseIPCMInfoCabac).
            if (pBs->pEndBuf - pBs->pCurBuf < I_PCM_MB_SIZE_IN_BYTE)
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_BS_INCOMPLETE);
            }

            //step 2: copy pixel from bit-stream into fdec [reconstruction]
            pTmpBsBuf = pBs->pCurBuf;
            if (!pCtx->pParam->bParseOnly)
            {
                for (i = 0; i < 16; i++)
                { //luma
                    memcpy(pDecY, pTmpBsBuf, iCopySizeY);
                    pDecY += iDecStrideL;
                    pTmpBsBuf += 16;
                }
                for (i = 0; i < 8; i++)
                { //cb
                    memcpy(pDecU, pTmpBsBuf, iCopySizeUV);
                    pDecU += iDecStrideC;
                    pTmpBsBuf += 8;
                }
                for (i = 0; i < 8; i++)
                { //cr
                    memcpy(pDecV, pTmpBsBuf, iCopySizeUV);
                    pDecV += iDecStrideC;
                    pTmpBsBuf += 8;
                }
            }

            pBs->pCurBuf += I_PCM_MB_SIZE_IN_BYTE;

            //step 3: update QP and pNonZeroCount
            pCurDqLayer->pLumaQp[iMbXy] = 0;
            memset(&pCurDqLayer->pChromaQp[iMbXy], 0, sizeof(InlineArray2<int8_t>));
            memset(pNzc, 16, sizeof(InlineArray24<int8_t>));   //Rec. 9.2.1 for PCM, nzc=16
            if ((uiRetTmp = (uint32_t)InitReadBits(pBs, 0)) != ERR_NONE) return (int32_t)uiRetTmp;
            return ERR_NONE;
        }
        else if (0 == uiMbType)
        { //reference to JM
            int8_t* pIntraPredMode = stackalloc int8_t[48]; // ENFORCE_STACK_ALIGN_1D (int8_t, pIntraPredMode, 48, 16)
            pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_INTRA4x4;
            if (pCtx->pPps->bTransform8x8ModeFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //transform_size_8x8_flag
                pCurDqLayer->pTransformSize8x8Flag[iMbXy] = uiCode != 0;
                if (pCurDqLayer->pTransformSize8x8Flag[iMbXy])
                {
                    uiMbType = pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_INTRA8x8;
                }
            }
            if (!pCurDqLayer->pTransformSize8x8Flag[iMbXy])
            {
                pCtx->pFillInfoCacheIntraNxNFunc(&sNeighAvail, pNonZeroCount, pIntraPredMode, pCurDqLayer);
                if ((uiRetTmp = (uint32_t)ParseIntra4x4Mode(pCtx, &sNeighAvail, pIntraPredMode, pBs, pCurDqLayer)) != ERR_NONE) return (int32_t)uiRetTmp;
            }
            else
            {
                pCtx->pFillInfoCacheIntraNxNFunc(&sNeighAvail, pNonZeroCount, pIntraPredMode, pCurDqLayer);
                if ((uiRetTmp = (uint32_t)ParseIntra8x8Mode(pCtx, &sNeighAvail, pIntraPredMode, pBs, pCurDqLayer)) != ERR_NONE) return (int32_t)uiRetTmp;
            }

            //uiCbp
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //coded_block_pattern
            uiCbp = uiCode;
            //G.9.1 Alternative parsing process for coded pBlock pattern
            if (pCtx->pSps->uiChromaFormatIdc != 0 && (uiCbp > 47))
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_CBP);
            if (pCtx->pSps->uiChromaFormatIdc == 0 && (uiCbp > 15))
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_CBP);

            if (pCtx->pSps->uiChromaFormatIdc != 0)
                uiCbp = g_kuiIntra4x4CbpTable[uiCbp];
            else
                uiCbp = g_kuiIntra4x4CbpTable400[uiCbp];
            pCurDqLayer->pCbp[iMbXy] = (int8_t)uiCbp;
            uiCbpC = uiCbp >> 4;
            uiCbpL = uiCbp & 15;
        }
        else
        { //I_PCM exclude, we can ignore it
            pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_INTRA16x16;
            pCurDqLayer->pTransformSize8x8Flag[iMbXy] = false;
            pCurDqLayer->pNoSubMbPartSizeLessThan8x8Flag[iMbXy] = true;
            pCurDqLayer->pIntraPredMode[iMbXy][7] = (int8_t)((uiMbType - 1) & 3);
            pCurDqLayer->pCbp[iMbXy] = (int8_t)g_kuiI16CbpTable[(uiMbType - 1) >> 2];
            uiCbpC = pCtx->pSps->uiChromaFormatIdc != 0 ? (uint32_t)(pCurDqLayer->pCbp[iMbXy] >> 4) : 0;
            uiCbpL = (uint32_t)(pCurDqLayer->pCbp[iMbXy] & 15);
            WelsFillCacheNonZeroCount(&sNeighAvail, pNonZeroCount, pCurDqLayer);
            if ((uiRetTmp = (uint32_t)ParseIntra16x16Mode(pCtx, &sNeighAvail, pBs, pCurDqLayer)) != ERR_NONE) return (int32_t)uiRetTmp;
        }

        ST32A4(&pNzc[0], 0);
        ST32A4(&pNzc[4], 0);
        ST32A4(&pNzc[8], 0);
        ST32A4(&pNzc[12], 0);
        ST32A4(&pNzc[16], 0);
        ST32A4(&pNzc[20], 0);

        if (pCurDqLayer->pCbp[iMbXy] == 0 && IS_INTRANxN(pCurDqLayer->pDec->pMbType[iMbXy]))
        {
            pCurDqLayer->pLumaQp[iMbXy] = (int8_t)pSlice->iLastMbQp;
            for (i = 0; i < 2; i++)
            {
                pCurDqLayer->pChromaQp[iMbXy][i] = (int8_t)g_kuiChromaQpTable[WELS_CLIP3(pCurDqLayer->pLumaQp[iMbXy] +
                                                   pSliceHeader->pPps->iChromaQpIndexOffset[i], 0, 51)];
            }

        }

        if (pCurDqLayer->pCbp[iMbXy] != 0 || MB_TYPE_INTRA16x16 == pCurDqLayer->pDec->pMbType[iMbXy])
        {
            memset(&pCurDqLayer->pScaledTCoeff[iMbXy], 0, 384 * sizeof(int16_t));
            int32_t iQpDelta, iId8x8, iId4x4;
            int16_t* pScaledTCoeff = (int16_t*)&pCurDqLayer->pScaledTCoeff[iMbXy];

            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mb_qp_delta
            iQpDelta = iCode;

            if (iQpDelta > 25 || iQpDelta < -26)
            { //out of iQpDelta range
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_QP);
            }

            pCurDqLayer->pLumaQp[iMbXy] = (int8_t)((pSlice->iLastMbQp + iQpDelta + 52) % 52); //update last_mb_qp
            pSlice->iLastMbQp = pCurDqLayer->pLumaQp[iMbXy];
            for (i = 0; i < 2; i++)
            {
                pCurDqLayer->pChromaQp[iMbXy][i] = (int8_t)g_kuiChromaQpTable[WELS_CLIP3(pSlice->iLastMbQp +
                                                   pSliceHeader->pPps->iChromaQpIndexOffset[i], 0,
                                                   51)];
            }


            BsStartCavlc(pBs);

            if (MB_TYPE_INTRA16x16 == pCurDqLayer->pDec->pMbType[iMbXy])
            {
                //step1: Luma DC
                if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, 0, 16, g_kuiLumaDcZigzagScan, I16_LUMA_DC,
                                                   pScaledTCoeff, (uint8_t)pCurDqLayer->pLumaQp[iMbXy], pCtx)) != ERR_NONE)
                {
                    return iRet;//abnormal
                }
                //step2: Luma AC
                if (uiCbpL != 0)
                {
                    for (i = 0; i < 16; i++)
                    {
                        if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, i, iScanIdxEnd - WELS_MAX(iScanIdxStart, 1) + 1,
                                                           g_kuiZigzagScan + WELS_MAX(iScanIdxStart, 1), I16_LUMA_AC, pScaledTCoeff + (i << 4),
                                                           (uint8_t)pCurDqLayer->pLumaQp[iMbXy], pCtx)) != ERR_NONE)
                        {
                            return iRet;//abnormal
                        }
                    }
                    ST32A4(&pNzc[0], LD32(&pNonZeroCount[1 + 8 * 1]));
                    ST32A4(&pNzc[4], LD32(&pNonZeroCount[1 + 8 * 2]));
                    ST32A4(&pNzc[8], LD32(&pNonZeroCount[1 + 8 * 3]));
                    ST32A4(&pNzc[12], LD32(&pNonZeroCount[1 + 8 * 4]));
                }
            }
            else
            { //non-MB_TYPE_INTRA16x16
                if (pCurDqLayer->pTransformSize8x8Flag[iMbXy])
                {
                    for (iId8x8 = 0; iId8x8 < 4; iId8x8++)
                    {
                        iMbResProperty = (IS_INTRA(pCurDqLayer->pDec->pMbType[iMbXy])) ? LUMA_DC_AC_INTRA_8 : LUMA_DC_AC_INTER_8;
                        if ((uiCbpL & (1u << iId8x8)) != 0)
                        {
                            int32_t iIndex = (iId8x8 << 2);
                            for (iId4x4 = 0; iId4x4 < 4; iId4x4++)
                            {
                                if ((iRet = WelsResidualBlockCavlc8x8(pVlcTable, pNonZeroCount, pBs, iIndex, iScanIdxEnd - iScanIdxStart + 1,
                                                                      g_kuiZigzagScan8x8 + iScanIdxStart, iMbResProperty, pScaledTCoeff + (iId8x8 << 6), iId4x4,
                                                                      (uint8_t)pCurDqLayer->pLumaQp[iMbXy], pCtx)) != ERR_NONE)
                                {
                                    return iRet;
                                }
                                iIndex++;
                            }
                        }
                        else
                        {
                            ST16(&pNonZeroCount[g_kuiCache48CountScan4Idx[iId8x8 << 2]], 0);
                            ST16(&pNonZeroCount[g_kuiCache48CountScan4Idx[(iId8x8 << 2) + 2]], 0);
                        }
                    }
                    ST32A4(&pNzc[0], LD32(&pNonZeroCount[1 + 8 * 1]));
                    ST32A4(&pNzc[4], LD32(&pNonZeroCount[1 + 8 * 2]));
                    ST32A4(&pNzc[8], LD32(&pNonZeroCount[1 + 8 * 3]));
                    ST32A4(&pNzc[12], LD32(&pNonZeroCount[1 + 8 * 4]));
                }
                else
                {
                    for (iId8x8 = 0; iId8x8 < 4; iId8x8++)
                    {
                        if ((uiCbpL & (1u << iId8x8)) != 0)
                        {
                            int32_t iIndex = (iId8x8 << 2);
                            for (iId4x4 = 0; iId4x4 < 4; iId4x4++)
                            {
                                //Luma (DC and AC decoding together)
                                if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, iIndex, iScanIdxEnd - iScanIdxStart + 1,
                                                                   g_kuiZigzagScan + iScanIdxStart, LUMA_DC_AC_INTRA, pScaledTCoeff + (iIndex << 4),
                                                                   (uint8_t)pCurDqLayer->pLumaQp[iMbXy], pCtx)) != ERR_NONE)
                                {
                                    return iRet;//abnormal
                                }
                                iIndex++;
                            }
                        }
                        else
                        {
                            ST16(&pNonZeroCount[g_kuiCache48CountScan4Idx[(iId8x8 << 2)]], 0);
                            ST16(&pNonZeroCount[g_kuiCache48CountScan4Idx[(iId8x8 << 2) + 2]], 0);
                        }
                    }
                    ST32A4(&pNzc[0], LD32(&pNonZeroCount[1 + 8 * 1]));
                    ST32A4(&pNzc[4], LD32(&pNonZeroCount[1 + 8 * 2]));
                    ST32A4(&pNzc[8], LD32(&pNonZeroCount[1 + 8 * 3]));
                    ST32A4(&pNzc[12], LD32(&pNonZeroCount[1 + 8 * 4]));
                }
            }

            //chroma
            //step1: DC
            if (1 == uiCbpC || 2 == uiCbpC)
            {
                for (i = 0; i < 2; i++)
                { //Cb Cr
                    iMbResProperty = i != 0 ? CHROMA_DC_V : CHROMA_DC_U;
                    if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, 16 + (i << 2), 4, g_kuiChromaDcScan, iMbResProperty,
                                                       pScaledTCoeff + 256 + (i << 6), (uint8_t)pCurDqLayer->pChromaQp[iMbXy][i], pCtx)) != ERR_NONE)
                    {
                        return iRet;//abnormal
                    }
                }
            }

            //step2: AC
            if (2 == uiCbpC)
            {
                for (i = 0; i < 2; i++)
                { //Cb Cr
                    iMbResProperty = i != 0 ? CHROMA_AC_V : CHROMA_AC_U;
                    int32_t iIndex = 16 + (i << 2);
                    for (iId4x4 = 0; iId4x4 < 4; iId4x4++)
                    {
                        if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, iIndex, iScanIdxEnd - WELS_MAX(iScanIdxStart,
                                                           1) + 1, g_kuiZigzagScan + WELS_MAX(iScanIdxStart, 1), iMbResProperty,
                                                           pScaledTCoeff + (iIndex << 4),
                                                           (uint8_t)pCurDqLayer->pChromaQp[iMbXy][i], pCtx)) != ERR_NONE)
                        {
                            return iRet;//abnormal
                        }
                        iIndex++;
                    }
                }
                ST16A2(&pNzc[16], LD16A2(&pNonZeroCount[6 + 8 * 1]));
                ST16A2(&pNzc[20], LD16A2(&pNonZeroCount[6 + 8 * 2]));
                ST16A2(&pNzc[18], LD16A2(&pNonZeroCount[6 + 8 * 4]));
                ST16A2(&pNzc[22], LD16A2(&pNonZeroCount[6 + 8 * 5]));
            }
            BsEndCavlc(pBs);
        }

        return ERR_NONE;
    }

    public static int32_t WelsDecodeMbCavlcISlice(SWelsDecoderContext* pCtx, SNalUnit* pNalCur, ref uint32_t uiEosFlag)
    {
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SBitStringAux* pBs = pCurDqLayer->pBitStringAux;
        SSliceHeaderExt* pSliceHeaderExt = &pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt;
        int32_t iBaseModeFlag;
        int32_t iRet = 0; //should have the return value to indicate decoding error or not, It's NECESSARY--2010.4.15
        uint32_t uiCode;
        uint32_t uiRetTmp; // WELS_READ_VERIFY
        intX_t iUsedBits;
        if (pSliceHeaderExt->bAdaptiveBaseModeFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //base_mode_flag
            iBaseModeFlag = (int32_t)uiCode;
        }
        else
        {
            iBaseModeFlag = pSliceHeaderExt->bDefaultBaseModeFlag ? 1 : 0;
        }
        if (iBaseModeFlag == 0)
        {
            iRet = WelsActualDecodeMbCavlcISlice(pCtx);
        }
        else
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"iBaseModeFlag ({iBaseModeFlag}) != 0, inter-layer prediction not supported.");
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_UNSUPPORTED_ILP);
        }
        if (iRet != 0)
        { //occur error when parsing, MUST STOP decoding
            return iRet;
        }

        // check whether there is left bits to read next time in case multiple slices
        iUsedBits = (intX_t)(((pBs->pCurBuf - pBs->pStartBuf) << 3) - (16 - pBs->iLeftBits));
        // sub 1, for stop bit
        if ((iUsedBits == (pBs->iBits - 1)) && (0 >= pCurDqLayer->sLayerInfo.sSliceInLayer.iMbSkipRun))
        { // slice boundary
            uiEosFlag = 1;
        }
        if (iUsedBits > (pBs->iBits -
                         1))
        { //When BS incomplete, as long as find it, SHOULD stop decoding to avoid mosaic or crash.
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                    $"WelsDecodeMbCavlcISlice()::::pBs incomplete, iUsedBits:{(int64_t)iUsedBits} > pBs->iBits:{pBs->iBits}, MUST stop decoding.");
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_BS_INCOMPLETE);
        }
        return ERR_NONE;
    }

    public static int32_t WelsActualDecodeMbCavlcPSlice(SWelsDecoderContext* pCtx)
    {
        SVlcTable* pVlcTable = pCtx->pVlcTable;
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SBitStringAux* pBs = pCurDqLayer->pBitStringAux;
        SSlice* pSlice = &pCurDqLayer->sLayerInfo.sSliceInLayer;
        SSliceHeader* pSliceHeader = &pSlice->sSliceHeaderExt.sSliceHeader;

        int32_t iScanIdxStart = pSlice->sSliceHeaderExt.uiScanIdxStart;
        int32_t iScanIdxEnd = pSlice->sSliceHeaderExt.uiScanIdxEnd;

        SWelsNeighAvail sNeighAvail;
        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbY = pCurDqLayer->iMbY;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        int8_t* pNzc = (int8_t*)&pCurDqLayer->pNzc[iMbXy];
        int32_t i;
        int32_t iRet = ERR_NONE;
        uint32_t uiMbType = 0, uiCbp = 0, uiCbpL = 0, uiCbpC = 0;
        uint32_t uiCode;
        int32_t iCode;
        int32_t iMbResProperty;
        uint32_t uiRetTmp; // WELS_READ_VERIFY

        GetNeighborAvailMbType(&sNeighAvail, pCurDqLayer);
        uint8_t* pNonZeroCount = stackalloc uint8_t[48]; // ENFORCE_STACK_ALIGN_1D (uint8_t, pNonZeroCount, 48, 16)
        pCurDqLayer->pInterPredictionDoneFlag[iMbXy] = 0;//2009.10.23
        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //uiMbType
        uiMbType = uiCode;
        if (uiMbType < 5)
        { //inter MB type
            // int16_t iMotionVector[LIST_A][30][MV_A]; int8_t iRefIndex[LIST_A][30];
            InlineArray30<InlineArray2<int16_t>>* iMotionVector = stackalloc InlineArray30<InlineArray2<int16_t>>[(int32_t)LIST_A];
            InlineArray30<int8_t>* iRefIndex = stackalloc InlineArray30<int8_t>[(int32_t)LIST_A];
            pCurDqLayer->pDec->pMbType[iMbXy] = g_ksInterPMbTypeInfo[uiMbType].iType;
            WelsFillCacheInter(&sNeighAvail, pNonZeroCount, iMotionVector, iRefIndex, pCurDqLayer);

            if ((iRet = ParseInterInfo(pCtx, iMotionVector, iRefIndex, pBs)) != ERR_NONE)
            {
                return iRet;//abnormal
            }

            if (pSlice->sSliceHeaderExt.bAdaptiveResidualPredFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //residual_prediction_flag
                pCurDqLayer->pResidualPredFlag[iMbXy] = (int8_t)uiCode;
            }
            else
            {
                pCurDqLayer->pResidualPredFlag[iMbXy] = (int8_t)(pSlice->sSliceHeaderExt.bDefaultResidualPredFlag ? 1 : 0);
            }

            if (pCurDqLayer->pResidualPredFlag[iMbXy] == 0)
            {
                pCurDqLayer->pInterPredictionDoneFlag[iMbXy] = 0;
            }
            else
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "residual_pred_flag = 1 not supported.");
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_ILP);
            }
        }
        else
        { //intra MB type
            uiMbType -= 5;
            if (uiMbType > 25)
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_MB_TYPE);
            if (pCtx->pSps->uiChromaFormatIdc == 0 && ((uiMbType >= 5 && uiMbType <= 12) || (uiMbType >= 17 && uiMbType <= 24)))
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_MB_TYPE);

            if (25 == uiMbType)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_DEBUG, "I_PCM mode exists in P slice!");
                int32_t iDecStrideL = pCurDqLayer->pDec->iLinesize[0];
                int32_t iDecStrideC = pCurDqLayer->pDec->iLinesize[1];

                int32_t iOffsetL = (iMbX + iMbY * iDecStrideL) << 4;
                int32_t iOffsetC = (iMbX + iMbY * iDecStrideC) << 3;

                uint8_t* pDecY = pCurDqLayer->pDec->pData[0] + iOffsetL;
                uint8_t* pDecU = pCurDqLayer->pDec->pData[1] + iOffsetC;
                uint8_t* pDecV = pCurDqLayer->pDec->pData[2] + iOffsetC;

                uint8_t* pTmpBsBuf;

                // C declares a block-local int32_t i here, shadowing the one above.
                int32_t iCopySizeY = (sizeof(uint8_t) << 4);
                int32_t iCopySizeUV = (sizeof(uint8_t) << 3);

                int32_t iIndex = ((-pBs->iLeftBits) >> 3) + 2;

                pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_INTRA_PCM;

                //step 1: locating bit-stream pointer [must align into integer byte]
                pBs->pCurBuf -= iIndex;

                //bounds check: I_PCM copies I_PCM_MB_SIZE_IN_BYTE (256 luma + 128 chroma) bytes
                //directly from the bitstream buffer; reject when fewer bytes remain to avoid
                //an out-of-bounds read (mirrors ParseIPCMInfoCabac).
                if (pBs->pEndBuf - pBs->pCurBuf < I_PCM_MB_SIZE_IN_BYTE)
                {
                    return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_BS_INCOMPLETE);
                }

                //step 2: copy pixel from bit-stream into fdec [reconstruction]
                pTmpBsBuf = pBs->pCurBuf;
                if (!pCtx->pParam->bParseOnly)
                {
                    for (i = 0; i < 16; i++)
                    { //luma
                        memcpy(pDecY, pTmpBsBuf, iCopySizeY);
                        pDecY += iDecStrideL;
                        pTmpBsBuf += 16;
                    }

                    for (i = 0; i < 8; i++)
                    { //cb
                        memcpy(pDecU, pTmpBsBuf, iCopySizeUV);
                        pDecU += iDecStrideC;
                        pTmpBsBuf += 8;
                    }
                    for (i = 0; i < 8; i++)
                    { //cr
                        memcpy(pDecV, pTmpBsBuf, iCopySizeUV);
                        pDecV += iDecStrideC;
                        pTmpBsBuf += 8;
                    }
                }

                pBs->pCurBuf += I_PCM_MB_SIZE_IN_BYTE;

                //step 3: update QP and pNonZeroCount
                pCurDqLayer->pLumaQp[iMbXy] = 0;
                pCurDqLayer->pChromaQp[iMbXy][0] = pCurDqLayer->pChromaQp[iMbXy][1] = 0;
                //Rec. 9.2.1 for PCM, nzc=16
                ST32A4(&pNzc[0], 0x10101010);
                ST32A4(&pNzc[4], 0x10101010);
                ST32A4(&pNzc[8], 0x10101010);
                ST32A4(&pNzc[12], 0x10101010);
                ST32A4(&pNzc[16], 0x10101010);
                ST32A4(&pNzc[20], 0x10101010);
                if ((uiRetTmp = (uint32_t)InitReadBits(pBs, 0)) != ERR_NONE) return (int32_t)uiRetTmp;
                return ERR_NONE;
            }
            else
            {
                if (0 == uiMbType)
                {
                    int8_t* pIntraPredMode = stackalloc int8_t[48]; // ENFORCE_STACK_ALIGN_1D (int8_t, pIntraPredMode, 48, 16)
                    pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_INTRA4x4;
                    if (pCtx->pPps->bTransform8x8ModeFlag)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //transform_size_8x8_flag
                        pCurDqLayer->pTransformSize8x8Flag[iMbXy] = uiCode != 0;
                        if (pCurDqLayer->pTransformSize8x8Flag[iMbXy])
                        {
                            uiMbType = pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_INTRA8x8;
                        }
                    }
                    if (!pCurDqLayer->pTransformSize8x8Flag[iMbXy])
                    {
                        pCtx->pFillInfoCacheIntraNxNFunc(&sNeighAvail, pNonZeroCount, pIntraPredMode, pCurDqLayer);
                        if ((uiRetTmp = (uint32_t)ParseIntra4x4Mode(pCtx, &sNeighAvail, pIntraPredMode, pBs, pCurDqLayer)) != ERR_NONE) return (int32_t)uiRetTmp;
                    }
                    else
                    {
                        pCtx->pFillInfoCacheIntraNxNFunc(&sNeighAvail, pNonZeroCount, pIntraPredMode, pCurDqLayer);
                        if ((uiRetTmp = (uint32_t)ParseIntra8x8Mode(pCtx, &sNeighAvail, pIntraPredMode, pBs, pCurDqLayer)) != ERR_NONE) return (int32_t)uiRetTmp;
                    }
                }
                else
                { //I_PCM exclude, we can ignore it
                    pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_INTRA16x16;
                    pCurDqLayer->pTransformSize8x8Flag[iMbXy] = false;
                    pCurDqLayer->pNoSubMbPartSizeLessThan8x8Flag[iMbXy] = true;
                    pCurDqLayer->pIntraPredMode[iMbXy][7] = (int8_t)((uiMbType - 1) & 3);
                    pCurDqLayer->pCbp[iMbXy] = (int8_t)g_kuiI16CbpTable[(uiMbType - 1) >> 2];
                    uiCbpC = pCtx->pSps->uiChromaFormatIdc != 0 ? (uint32_t)(pCurDqLayer->pCbp[iMbXy] >> 4) : 0;
                    uiCbpL = (uint32_t)(pCurDqLayer->pCbp[iMbXy] & 15);
                    WelsFillCacheNonZeroCount(&sNeighAvail, pNonZeroCount, pCurDqLayer);
                    if ((iRet = ParseIntra16x16Mode(pCtx, &sNeighAvail, pBs, pCurDqLayer)) != ERR_NONE)
                    {
                        return iRet;
                    }
                }
            }
        }

        if (MB_TYPE_INTRA16x16 != pCurDqLayer->pDec->pMbType[iMbXy])
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //coded_block_pattern
            uiCbp = uiCode;
            {
                if (pCtx->pSps->uiChromaFormatIdc != 0 && (uiCbp > 47))
                    return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_CBP);
                if (pCtx->pSps->uiChromaFormatIdc == 0 && (uiCbp > 15))
                    return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_CBP);
                if (MB_TYPE_INTRA4x4 == pCurDqLayer->pDec->pMbType[iMbXy] || MB_TYPE_INTRA8x8 == pCurDqLayer->pDec->pMbType[iMbXy])
                {

                    uiCbp = pCtx->pSps->uiChromaFormatIdc != 0 ? g_kuiIntra4x4CbpTable[uiCbp] : g_kuiIntra4x4CbpTable400[uiCbp];
                }
                else //inter
                    uiCbp = pCtx->pSps->uiChromaFormatIdc != 0 ? g_kuiInterCbpTable[uiCbp] : g_kuiInterCbpTable400[uiCbp];
            }

            pCurDqLayer->pCbp[iMbXy] = (int8_t)uiCbp;
            uiCbpC = (uint32_t)(pCurDqLayer->pCbp[iMbXy] >> 4);
            uiCbpL = (uint32_t)(pCurDqLayer->pCbp[iMbXy] & 15);

            // Need modification when B picutre add in
            bool bNeedParseTransformSize8x8Flag =
              (((pCurDqLayer->pDec->pMbType[iMbXy] >= MB_TYPE_16x16 && pCurDqLayer->pDec->pMbType[iMbXy] <= MB_TYPE_8x16)
                || pCurDqLayer->pNoSubMbPartSizeLessThan8x8Flag[iMbXy])
               && (pCurDqLayer->pDec->pMbType[iMbXy] != MB_TYPE_INTRA8x8)
               && (pCurDqLayer->pDec->pMbType[iMbXy] != MB_TYPE_INTRA4x4)
               && (uiCbpL > 0)
               && (pCtx->pPps->bTransform8x8ModeFlag));

            if (bNeedParseTransformSize8x8Flag)
            {
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //transform_size_8x8_flag
                pCurDqLayer->pTransformSize8x8Flag[iMbXy] = uiCode != 0;
            }
        }

        ST32A4(&pNzc[0], 0);
        ST32A4(&pNzc[4], 0);
        ST32A4(&pNzc[8], 0);
        ST32A4(&pNzc[12], 0);
        ST32A4(&pNzc[16], 0);
        ST32A4(&pNzc[20], 0);
        if (pCurDqLayer->pCbp[iMbXy] == 0 && !IS_INTRA16x16(pCurDqLayer->pDec->pMbType[iMbXy])
            && !IS_I_BL(pCurDqLayer->pDec->pMbType[iMbXy]))
        {
            pCurDqLayer->pLumaQp[iMbXy] = (int8_t)pSlice->iLastMbQp;
            for (i = 0; i < 2; i++)
            {
                pCurDqLayer->pChromaQp[iMbXy][i] = (int8_t)g_kuiChromaQpTable[WELS_CLIP3(pCurDqLayer->pLumaQp[iMbXy] +
                                                   pSliceHeader->pPps->iChromaQpIndexOffset[i], 0, 51)];
            }
        }

        if (pCurDqLayer->pCbp[iMbXy] != 0 || MB_TYPE_INTRA16x16 == pCurDqLayer->pDec->pMbType[iMbXy])
        {
            int32_t iQpDelta, iId8x8, iId4x4;
            int16_t* pScaledTCoeff = (int16_t*)&pCurDqLayer->pScaledTCoeff[iMbXy];
            memset(pScaledTCoeff, 0, MB_COEFF_LIST_SIZE * sizeof(int16_t));
            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mb_qp_delta
            iQpDelta = iCode;

            if (iQpDelta > 25 || iQpDelta < -26)
            { //out of iQpDelta range
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_QP);
            }

            pCurDqLayer->pLumaQp[iMbXy] = (int8_t)((pSlice->iLastMbQp + iQpDelta + 52) % 52); //update last_mb_qp
            pSlice->iLastMbQp = pCurDqLayer->pLumaQp[iMbXy];
            for (i = 0; i < 2; i++)
            {
                pCurDqLayer->pChromaQp[iMbXy][i] = (int8_t)g_kuiChromaQpTable[WELS_CLIP3(pSlice->iLastMbQp +
                                                   pSliceHeader->pPps->iChromaQpIndexOffset[i], 0,
                                                   51)];
            }

            BsStartCavlc(pBs);

            if (MB_TYPE_INTRA16x16 == pCurDqLayer->pDec->pMbType[iMbXy])
            {
                //step1: Luma DC
                if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, 0, 16, g_kuiLumaDcZigzagScan, I16_LUMA_DC,
                                                   pScaledTCoeff, (uint8_t)pCurDqLayer->pLumaQp[iMbXy], pCtx)) != ERR_NONE)
                {
                    return iRet;//abnormal
                }
                //step2: Luma AC
                if (uiCbpL != 0)
                {
                    for (i = 0; i < 16; i++)
                    {
                        if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, i, iScanIdxEnd - WELS_MAX(iScanIdxStart, 1) + 1,
                                                           g_kuiZigzagScan + WELS_MAX(iScanIdxStart, 1), I16_LUMA_AC, pScaledTCoeff + (i << 4),
                                                           (uint8_t)pCurDqLayer->pLumaQp[iMbXy], pCtx)) != ERR_NONE)
                        {
                            return iRet;//abnormal
                        }
                    }
                    ST32A4(&pNzc[0], LD32(&pNonZeroCount[1 + 8 * 1]));
                    ST32A4(&pNzc[4], LD32(&pNonZeroCount[1 + 8 * 2]));
                    ST32A4(&pNzc[8], LD32(&pNonZeroCount[1 + 8 * 3]));
                    ST32A4(&pNzc[12], LD32(&pNonZeroCount[1 + 8 * 4]));
                }
            }
            else
            { //non-MB_TYPE_INTRA16x16
                if (pCurDqLayer->pTransformSize8x8Flag[iMbXy])
                {
                    for (iId8x8 = 0; iId8x8 < 4; iId8x8++)
                    {
                        iMbResProperty = (IS_INTRA(pCurDqLayer->pDec->pMbType[iMbXy])) ? LUMA_DC_AC_INTRA_8 : LUMA_DC_AC_INTER_8;
                        if ((uiCbpL & (1u << iId8x8)) != 0)
                        {
                            int32_t iIndex = (iId8x8 << 2);
                            for (iId4x4 = 0; iId4x4 < 4; iId4x4++)
                            {
                                if ((iRet = WelsResidualBlockCavlc8x8(pVlcTable, pNonZeroCount, pBs, iIndex, iScanIdxEnd - iScanIdxStart + 1,
                                                                      g_kuiZigzagScan8x8 + iScanIdxStart, iMbResProperty, pScaledTCoeff + (iId8x8 << 6), iId4x4,
                                                                      (uint8_t)pCurDqLayer->pLumaQp[iMbXy], pCtx)) != ERR_NONE)
                                {
                                    return iRet;
                                }
                                iIndex++;
                            }
                        }
                        else
                        {
                            ST16(&pNonZeroCount[g_kuiCache48CountScan4Idx[iId8x8 << 2]], 0);
                            ST16(&pNonZeroCount[g_kuiCache48CountScan4Idx[(iId8x8 << 2) + 2]], 0);
                        }
                    }
                    ST32A4(&pNzc[0], LD32(&pNonZeroCount[1 + 8 * 1]));
                    ST32A4(&pNzc[4], LD32(&pNonZeroCount[1 + 8 * 2]));
                    ST32A4(&pNzc[8], LD32(&pNonZeroCount[1 + 8 * 3]));
                    ST32A4(&pNzc[12], LD32(&pNonZeroCount[1 + 8 * 4]));
                }
                else
                { // Normal T4x4
                    for (iId8x8 = 0; iId8x8 < 4; iId8x8++)
                    {
                        iMbResProperty = (IS_INTRA(pCurDqLayer->pDec->pMbType[iMbXy])) ? LUMA_DC_AC_INTRA : LUMA_DC_AC_INTER;
                        if ((uiCbpL & (1u << iId8x8)) != 0)
                        {
                            int32_t iIndex = (iId8x8 << 2);
                            for (iId4x4 = 0; iId4x4 < 4; iId4x4++)
                            {
                                //Luma (DC and AC decoding together)
                                if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, iIndex, iScanIdxEnd - iScanIdxStart + 1,
                                                                   g_kuiZigzagScan + iScanIdxStart, iMbResProperty, pScaledTCoeff + (iIndex << 4),
                                                                   (uint8_t)pCurDqLayer->pLumaQp[iMbXy], pCtx)) != ERR_NONE)
                                {
                                    return iRet;//abnormal
                                }
                                iIndex++;
                            }
                        }
                        else
                        {
                            ST16(&pNonZeroCount[g_kuiCache48CountScan4Idx[iId8x8 << 2]], 0);
                            ST16(&pNonZeroCount[g_kuiCache48CountScan4Idx[(iId8x8 << 2) + 2]], 0);
                        }
                    }
                    ST32A4(&pNzc[0], LD32(&pNonZeroCount[1 + 8 * 1]));
                    ST32A4(&pNzc[4], LD32(&pNonZeroCount[1 + 8 * 2]));
                    ST32A4(&pNzc[8], LD32(&pNonZeroCount[1 + 8 * 3]));
                    ST32A4(&pNzc[12], LD32(&pNonZeroCount[1 + 8 * 4]));
                }
            }


            //chroma
            //step1: DC
            if (1 == uiCbpC || 2 == uiCbpC)
            {
                for (i = 0; i < 2; i++)
                { //Cb Cr
                    if (IS_INTRA(pCurDqLayer->pDec->pMbType[iMbXy]))
                        iMbResProperty = i != 0 ? CHROMA_DC_V : CHROMA_DC_U;
                    else
                        iMbResProperty = i != 0 ? CHROMA_DC_V_INTER : CHROMA_DC_U_INTER;

                    if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, 16 + (i << 2), 4, g_kuiChromaDcScan, iMbResProperty,
                                                       pScaledTCoeff + 256 + (i << 6), (uint8_t)pCurDqLayer->pChromaQp[iMbXy][i], pCtx)) != ERR_NONE)
                    {
                        return iRet;//abnormal
                    }
                }
            }
            else
            {
            }
            //step2: AC
            if (2 == uiCbpC)
            {
                for (i = 0; i < 2; i++)
                { //Cb Cr
                    if (IS_INTRA(pCurDqLayer->pDec->pMbType[iMbXy]))
                        iMbResProperty = i != 0 ? CHROMA_AC_V : CHROMA_AC_U;
                    else
                        iMbResProperty = i != 0 ? CHROMA_AC_V_INTER : CHROMA_AC_U_INTER;

                    int32_t iIndex = 16 + (i << 2);
                    for (iId4x4 = 0; iId4x4 < 4; iId4x4++)
                    {
                        if ((iRet = WelsResidualBlockCavlc(pVlcTable, pNonZeroCount, pBs, iIndex, iScanIdxEnd - WELS_MAX(iScanIdxStart,
                                                           1) + 1, g_kuiZigzagScan + WELS_MAX(iScanIdxStart, 1), iMbResProperty,
                                                           pScaledTCoeff + (iIndex << 4),
                                                           (uint8_t)pCurDqLayer->pChromaQp[iMbXy][i], pCtx)) != ERR_NONE)
                        {
                            return iRet;//abnormal
                        }
                        iIndex++;
                    }
                }
                ST16A2(&pNzc[16], LD16A2(&pNonZeroCount[6 + 8 * 1]));
                ST16A2(&pNzc[20], LD16A2(&pNonZeroCount[6 + 8 * 2]));
                ST16A2(&pNzc[18], LD16A2(&pNonZeroCount[6 + 8 * 4]));
                ST16A2(&pNzc[22], LD16A2(&pNonZeroCount[6 + 8 * 5]));
            }
            BsEndCavlc(pBs);
        }

        return ERR_NONE;
    }

    public static int32_t WelsDecodeMbCavlcPSlice(SWelsDecoderContext* pCtx, SNalUnit* pNalCur, ref uint32_t uiEosFlag)
    {
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SBitStringAux* pBs = pCurDqLayer->pBitStringAux;
        SSlice* pSlice = &pCurDqLayer->sLayerInfo.sSliceInLayer;
        SSliceHeader* pSliceHeader = &pSlice->sSliceHeaderExt.sSliceHeader;
        SPicture** ppRefPic = (SPicture**)&pCtx->sRefPic.pRefList[(int32_t)LIST_0];
        intX_t iUsedBits;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        int8_t* pNzc = (int8_t*)&pCurDqLayer->pNzc[iMbXy];
        int32_t iBaseModeFlag, i;
        int32_t iRet = 0; //should have the return value to indicate decoding error or not, It's NECESSARY--2010.4.15
        uint32_t uiCode;
        uint32_t uiRetTmp; // WELS_READ_VERIFY

        pCurDqLayer->pNoSubMbPartSizeLessThan8x8Flag[iMbXy] = true;
        pCurDqLayer->pTransformSize8x8Flag[iMbXy] = false;

        if (-1 == pSlice->iMbSkipRun)
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mb_skip_run
            pSlice->iMbSkipRun = (int32_t)uiCode;
            if (-1 == pSlice->iMbSkipRun)
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_MB_SKIP_RUN);
            }
        }
        if (pSlice->iMbSkipRun-- != 0)
        {
            int16_t* iMv = stackalloc int16_t[2];

            pCurDqLayer->pDec->pMbType[iMbXy] = MB_TYPE_SKIP;
            ST32A4(&pNzc[0], 0);
            ST32A4(&pNzc[4], 0);
            ST32A4(&pNzc[8], 0);
            ST32A4(&pNzc[12], 0);
            ST32A4(&pNzc[16], 0);
            ST32A4(&pNzc[20], 0);

            pCurDqLayer->pInterPredictionDoneFlag[iMbXy] = 0;
            memset(&pCurDqLayer->pDec->pRefIndex[0].p[iMbXy], 0, sizeof(int8_t) * 16);
            bool bIsPending = GetThreadCount(pCtx) > 1;
            pCtx->bMbRefConcealed = pCtx->bRPLRError || pCtx->bMbRefConcealed || !(ppRefPic[0] != null && (ppRefPic[0]->bIsComplete
                                    || bIsPending));
            //predict iMv
            PredPSkipMvFromNeighbor(pCurDqLayer, iMv);
            for (i = 0; i < 16; i++)
            {
                ST32A2(&pCurDqLayer->pDec->pMv[0].p[iMbXy][i], *(uint32_t*)iMv);
            }

            //if (!pSlice->sSliceHeaderExt.bDefaultResidualPredFlag) {
            //  memset (pCurDqLayer->pScaledTCoeff[iMbXy], 0, 384 * sizeof (int16_t));
            //}

            //reset rS
            if (!pSlice->sSliceHeaderExt.bDefaultResidualPredFlag ||
                (pNalCur->sNalHeaderExt.uiQualityId == 0 && pNalCur->sNalHeaderExt.uiDependencyId == 0))
            {
                pCurDqLayer->pLumaQp[iMbXy] = (int8_t)pSlice->iLastMbQp;
                for (i = 0; i < 2; i++)
                {
                    pCurDqLayer->pChromaQp[iMbXy][i] = (int8_t)g_kuiChromaQpTable[WELS_CLIP3(pCurDqLayer->pLumaQp[iMbXy] +
                                                       pSliceHeader->pPps->iChromaQpIndexOffset[i], 0, 51)];
                }
            }

            pCurDqLayer->pCbp[iMbXy] = 0;
        }
        else
        {
            if (pSlice->sSliceHeaderExt.bAdaptiveBaseModeFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //base_mode_flag
                iBaseModeFlag = (int32_t)uiCode;
            }
            else
            {
                iBaseModeFlag = pSlice->sSliceHeaderExt.bDefaultBaseModeFlag ? 1 : 0;
            }
            if (iBaseModeFlag == 0)
            {
                iRet = WelsActualDecodeMbCavlcPSlice(pCtx);
            }
            else
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"iBaseModeFlag ({iBaseModeFlag}) != 0, inter-layer prediction not supported.");
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_UNSUPPORTED_ILP);
            }
            if (iRet != 0)
            { //occur error when parsing, MUST STOP decoding
                return iRet;
            }
        }
        // check whether there is left bits to read next time in case multiple slices
        iUsedBits = (intX_t)(((pBs->pCurBuf - pBs->pStartBuf) << 3) - (16 - pBs->iLeftBits));
        // sub 1, for stop bit
        if ((iUsedBits == (pBs->iBits - 1)) && (0 >= pCurDqLayer->sLayerInfo.sSliceInLayer.iMbSkipRun))
        { // slice boundary
            uiEosFlag = 1;
        }
        if (iUsedBits > (pBs->iBits -
                         1))
        { //When BS incomplete, as long as find it, SHOULD stop decoding to avoid mosaic or crash.
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                    $"WelsDecodeMbCavlcISlice()::::pBs incomplete, iUsedBits:{(int64_t)iUsedBits} > pBs->iBits:{pBs->iBits}, MUST stop decoding.");
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_BS_INCOMPLETE);
        }
        return ERR_NONE;
    }

    // WelsActualDecodeMbCavlcBSlice: not ported yet (B slices).
    public static int32_t WelsDecodeMbCavlcBSlice(SWelsDecoderContext* pCtx, SNalUnit* pNalCur, ref uint32_t uiEosFlag)
    {
        WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "WelsDecodeMbCavlcBSlice()::::B slices are not supported by this port yet.");
        return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_BIPRED);
    }

    public static void WelsBlockFuncInit(SBlockFunc* pFunc, int32_t iCpu)
    {
        pFunc->pWelsSetNonZeroCountFunc = &WelsNonZeroCount_c;
        pFunc->pWelsBlockZero16x16Func = &WelsBlockZero16x16_c;
        pFunc->pWelsBlockZero8x8Func = &WelsBlockZero8x8_c;
        // HAVE_NEON / HAVE_NEON_AARCH64 / X86_ASM variants: not ported.
    }

    public static void WelsBlockInit(int16_t* pBlock, int iW, int iH, int iStride, uint8_t uiVal)
    {
        int32_t i;
        int16_t* pDst = pBlock;

        for (i = 0; i < iH; i++)
        {
            memset(pDst, uiVal, iW * sizeof(int16_t));
            pDst += iStride;
        }
    }

    public static void WelsBlockZero16x16_c(int16_t* pBlock, int32_t iStride)
    {
        WelsBlockInit(pBlock, 16, 16, iStride, 0);
    }

    public static void WelsBlockZero8x8_c(int16_t* pBlock, int32_t iStride)
    {
        WelsBlockInit(pBlock, 8, 8, iStride, 0);
    }
}
