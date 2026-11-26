//-----------------------------------------------------------------------------
// Filename: encoder.cs
//
// Description: Port of codec/encoder/core/src/encoder.cpp, part 1 (E1):
// InitBitStream (E1), WelsSetMemZero_c (E4), and UpdateFrameNum,
// LoadBackFrameNum and InitFrameCoding (E4b). InitPic, WelsInitBGDFunc,
// InitFunctionPointers and DecideFrameType follow with what they install or
// read (the motion search, MC and VAA in E5 and E7).
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (E1).
// 26 Nov 2026  Claude          WelsSetMemZero_c (E4).
// 26 Nov 2026  Claude          UpdateFrameNum, LoadBackFrameNum and InitFrameCoding (E4b).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2013, Cisco Systems
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
 */

using System.Diagnostics;

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    // InitPic, WelsInitBGDFunc and InitFunctionPointers come with E5.

    public static void UpdateFrameNum(sWelsEncCtx* pEncCtx, int32_t kiDidx)
    {
        SSpatialLayerInternal* pParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[kiDidx];
        bool bNeedFrameNumIncreasing = false;

        if (NRI_PRI_LOWEST != pEncCtx->eLastNalPriority[kiDidx])
        {
            bNeedFrameNumIncreasing = true;
        }

        if (bNeedFrameNumIncreasing)
        {
            if (pParamInternal->iFrameNum < (1 << (int32_t)pEncCtx->pSps->uiLog2MaxFrameNum) - 1)
            {
                ++pParamInternal->iFrameNum;
            }
            else
            {
                pParamInternal->iFrameNum = 0;    // if iFrameNum overflow
            }
        }

        pEncCtx->eLastNalPriority[kiDidx] = NRI_PRI_LOWEST;
    }


    public static void LoadBackFrameNum(sWelsEncCtx* pEncCtx, int32_t kiDidx)
    {
        SSpatialLayerInternal* pParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[kiDidx];
        bool bNeedFrameNumIncreasing = false;

        if (NRI_PRI_LOWEST != pEncCtx->eLastNalPriority[kiDidx])
        {
            bNeedFrameNumIncreasing = true;
        }

        if (bNeedFrameNumIncreasing)
        {
            if (pParamInternal->iFrameNum != 0)
            {
                pParamInternal->iFrameNum--;
            }
            else
            {
                pParamInternal->iFrameNum = (1 << (int32_t)pEncCtx->pSps->uiLog2MaxFrameNum) - 1;
            }
        }
    }

    /*!
     * \brief   initialize bitstream writing
     *
     * \param   pEncCtx     sWelsEncCtx*
     *
     * \return  void
     */
    public static void InitBitStream(sWelsEncCtx* pEncCtx)
    {
        // for bitstream writing
        pEncCtx->iPosBsBuffer = 0;    // reset bs pBuffer position
        pEncCtx->pOut->iNalIndex = 0;    // reset NAL index
        pEncCtx->pOut->iLayerBsIndex = 0;    // reset index of Layer Bs

        InitBits(&pEncCtx->pOut->sBsWrite, pEncCtx->pOut->pBsBuffer, (int32_t)pEncCtx->pOut->uiSize);
    }

    /*!
     * \brief   initialize frame coding
     */
    public static void InitFrameCoding(sWelsEncCtx* pEncCtx, EVideoFrameType keFrameType, int32_t kiDidx)
    {
        SSpatialLayerInternal* pParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[kiDidx];
        if (keFrameType == videoFrameTypeP)
        {
            ++pParamInternal->iFrameIndex;

            if (pParamInternal->iPOC < (1 << pEncCtx->pSps->iLog2MaxPocLsb) -
                2)     // if iPOC type is no 0, this need be modification
            {
                pParamInternal->iPOC += 2;   // for POC type 0
            }
            else
            {
                pParamInternal->iPOC = 0;
            }

            UpdateFrameNum(pEncCtx, kiDidx);

            pEncCtx->eNalType = NAL_UNIT_CODED_SLICE;
            pEncCtx->eSliceType = P_SLICE;
            pEncCtx->eNalPriority = NRI_PRI_HIGH;
        }
        else if (keFrameType == videoFrameTypeIDR)
        {
            pParamInternal->iFrameNum = 0;
            pParamInternal->iPOC = 0;
            pParamInternal->bEncCurFrmAsIdrFlag = false;
            pParamInternal->iFrameIndex = 0;

            pEncCtx->eNalType = NAL_UNIT_CODED_SLICE_IDR;
            pEncCtx->eSliceType = I_SLICE;
            pEncCtx->eNalPriority = NRI_PRI_HIGHEST;

            pParamInternal->iCodingIndex = 0;

            // reset_ref_list

            // rc_init_gop
        }
        else if (keFrameType == videoFrameTypeI)
        {
            if (pParamInternal->iPOC < (1 << pEncCtx->pSps->iLog2MaxPocLsb) -
                2)     // if iPOC type is no 0, this need be modification
            {
                pParamInternal->iPOC += 2;   // for POC type 0
            }
            else
            {
                pParamInternal->iPOC = 0;
            }

            UpdateFrameNum(pEncCtx, kiDidx);

            pEncCtx->eNalType = NAL_UNIT_CODED_SLICE;
            pEncCtx->eSliceType = I_SLICE;
            pEncCtx->eNalPriority = NRI_PRI_HIGHEST;

            // rc_init_gop
        }
        else
        { // B pictures are not supported now, any else?
            Debug.Assert(false);
        }

        // #if defined(STAT_OUTPUT) memset (&pEncCtx->sPerInfo, 0, sizeof (SStatSliceInfo)); #endif
    }

    // DecideFrameType reads the VAA's IDR period and scene change flags and comes with E5. The
    // DumpDependencyRec and DumpRecFrame dumps are ENABLE_FRAME_DUMP only, which is off.

    /***********************************************************************************/
    public static void WelsSetMemZero_c(void* pDst, int32_t iSize)
    { // confirmed_safe_unsafe_usage
        memset(pDst, 0, iSize);
    }
}
