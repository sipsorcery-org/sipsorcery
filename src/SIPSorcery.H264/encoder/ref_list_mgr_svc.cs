//-----------------------------------------------------------------------------
// Filename: ref_list_mgr_svc.cs
//
// Description: Port of codec/encoder/core/inc/ref_list_mgr_svc.h and
// codec/encoder/core/src/ref_list_mgr_svc.cpp, the camera half (E5b): the
// reference list reset, update, build and syntax, the long term reference
// (LTR) marking, and the reference strategy classes. The screen content
// functions (WelsUpdateRefListScreen, WelsBuildRefListScreen,
// WelsMarkPicScreen, LTRMarkProcessScreen, UpdateBlockStatic and the source
// list updates) read the preprocessor's screen state and come with Phase 4;
// the strategy methods that would call them throw. FilterLTRRecoveryRequest
// and FilterLTRMarkingFeedback, which handle the API's LTR feedback options,
// came with the API (E8). LONG_TERM_REF_DUMP's DumpRef is debug only.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The C++ interface IWelsReferenceStrategy and its three classes share one
// field (CWelsReference_TemporalLayer::m_pEncoderCtx), so, as with
// IWelsParametersetStrategy, the port makes them a single native struct whose
// m_eClass records the dynamic type and whose methods switch on it.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5b).
// 27 Nov 2026  Claude          FilterLTRRecoveryRequest and FilterLTRMarkingFeedback (E8).
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
 *
 * \file    ref_list_mgr_svc.h
 *
 * \brief   list management for reference picture
 *
 * \date    2009.05.14 Created
 *
 *************************************************************************************
 */

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SIPSorcery.H264.Encoder;

/// <summary>Port addition: the C++ dynamic type of an IWelsReferenceStrategy.</summary>
internal enum EReferenceStrategyClass
{
    CWelsReference_TemporalLayer,
    CWelsReference_Screen,
    CWelsReference_LosslessWithLtr,
}

/// <summary>
/// IWelsReferenceStrategy and its classes (CWelsReference_TemporalLayer, CWelsReference_Screen,
/// CWelsReference_LosslessWithLtr), see the file header.
/// </summary>
internal unsafe struct IWelsReferenceStrategy
{
    public EReferenceStrategyClass m_eClass;   // port addition: the C++ dynamic type

    // CWelsReference_TemporalLayer (protected)
    public sWelsEncCtx* m_pEncoderCtx;

    public static IWelsReferenceStrategy* CreateReferenceStrategy(sWelsEncCtx* pCtx, EUsageType keUsageType,
        bool kbLtrEnabled)
    {
        IWelsReferenceStrategy* pReferenceStrategy =
            (IWelsReferenceStrategy*)NativeMemory.AllocZeroed((nuint)sizeof(IWelsReferenceStrategy));
        switch (keUsageType)
        {
            case SCREEN_CONTENT_REAL_TIME:
                if (kbLtrEnabled)
                {
                    pReferenceStrategy->m_eClass = EReferenceStrategyClass.CWelsReference_LosslessWithLtr;
                }
                else
                {
                    pReferenceStrategy->m_eClass = EReferenceStrategyClass.CWelsReference_Screen;
                }
                break;
            case CAMERA_VIDEO_REAL_TIME:
            case CAMERA_VIDEO_NON_REAL_TIME:
            default:
                pReferenceStrategy->m_eClass = EReferenceStrategyClass.CWelsReference_TemporalLayer;
                break;
        }
        pReferenceStrategy->Init(pCtx);
        return pReferenceStrategy;
    }

    /// <summary>Port addition: WELS_DELETE_OP (pReferenceStrategy).</summary>
    public static void DeleteReferenceStrategy(ref IWelsReferenceStrategy* pReferenceStrategy)
    {
        if (pReferenceStrategy != null)
        {
            NativeMemory.Free(pReferenceStrategy);
            pReferenceStrategy = null;
        }
    }

    // CWelsReference_TemporalLayer::Init, which the other two classes inherit.
    private void Init(sWelsEncCtx* pCtx)
    {
        m_pEncoderCtx = pCtx;
    }

    public bool BuildRefList(int32_t iPOC, int32_t iBestLtrRefIdx) => m_eClass switch
    {
        EReferenceStrategyClass.CWelsReference_LosslessWithLtr => throw ScreenContent(nameof(BuildRefList)),
        _ => WelsEnc.WelsBuildRefList(m_pEncoderCtx, iPOC, iBestLtrRefIdx),
    };

    public void MarkPic()
    {
        if (m_eClass == EReferenceStrategyClass.CWelsReference_LosslessWithLtr)
        {
            throw ScreenContent(nameof(MarkPic));
        }
        WelsEnc.WelsMarkPic(m_pEncoderCtx);
    }

    public bool UpdateRefList() => m_eClass switch
    {
        EReferenceStrategyClass.CWelsReference_LosslessWithLtr => throw ScreenContent(nameof(UpdateRefList)),
        _ => WelsEnc.WelsUpdateRefList(m_pEncoderCtx),
    };

    public void EndofUpdateRefList()
    {
        if (m_eClass != EReferenceStrategyClass.CWelsReference_TemporalLayer)
        {
            // CWelsReference_Screen: UpdateSrcPicList; CWelsReference_LosslessWithLtr:
            // UpdateSrcPicListLosslessScreenRefSelectionWithLtr.
            throw ScreenContent(nameof(EndofUpdateRefList));
        }
        WelsEnc.PrefetchNextBuffer(m_pEncoderCtx);
    }

    public void AfterBuildRefList()
    {
        if (m_eClass != EReferenceStrategyClass.CWelsReference_TemporalLayer)
        {
            // CWelsReference_Screen (and CWelsReference_LosslessWithLtr, which inherits it): UpdateBlockStatic.
            throw ScreenContent(nameof(AfterBuildRefList));
        }
        WelsEnc.DoNothing(m_pEncoderCtx);
    }

    private readonly NotSupportedException ScreenContent(string method) =>
        new($"{m_eClass}::{method}: the screen content reference lists come with Phase 4.");
}

internal static unsafe partial class WelsEnc
{
    // LTR_MARKING_PROCESS_MODE
    public const int LTR_DIRECT_MARK = 0;
    public const int LTR_DELAY_MARK = 1;

    // COMPARE_FRAME_NUM
    public const int FRAME_NUM_EQUAL = 0x01;
    public const int FRAME_NUM_BIGGER = 0x02;
    public const int FRAME_NUM_SMALLER = 0x04;
    public const int FRAME_NUM_OVER_MAX = 0x08;

    private static SPicture** ShortRefList(SRefList* pRefList) => (SPicture**)&pRefList->pShortRefList;
    private static SPicture** LongRefList(SRefList* pRefList) => (SPicture**)&pRefList->pLongRefList;
    private static SPicture** RefPics(SRefList* pRefList) => (SPicture**)&pRefList->pRef;

    // #define STR_ROOM 1 (unused)

    /*
    *   reset LTR marking , recovery ,feedback state to default
    */
    public static void ResetLtrState(SLTRState* pLtr)
    {
        pLtr->bReceivedT0LostFlag = false;
        pLtr->iLastRecoverFrameNum = 0;
        pLtr->iLastCorFrameNumDec = -1;
        pLtr->iCurFrameNumInDec = -1;

        // LTR mark
        pLtr->iLTRMarkMode = LTR_DIRECT_MARK;
        pLtr->iLTRMarkSuccessNum = 0; //successful marked num
        pLtr->bLTRMarkingFlag = false; //decide whether current frame marked as LTR
        pLtr->bLTRMarkEnable = false; //when LTR is confirmed and the interval is no smaller than the marking period
        pLtr->iCurLtrIdx = 0;
        memset(pLtr->iLastLtrIdx, 0, sizeof(int32_t) * codec_app_def.MAX_TEMPORAL_LAYER_NUM);
        pLtr->uiLtrMarkInterval = 0;

        // LTR mark feedback
        pLtr->uiLtrMarkState = (uint32_t)NO_LTR_MARKING_FEEDBACK;
        pLtr->iLtrMarkFbFrameNum = -1;
    }

    /*
     *  reset reference picture list
     */
    public static void WelsResetRefList(sWelsEncCtx* pCtx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        int32_t i;

        for (i = 0; i < MAX_SHORT_REF_COUNT + 1; i++)
        {
            pRefList->pShortRefList[i] = null;
        }
        for (i = 0; i < pCtx->pSvcParam->iLTRRefNum + 1; i++)
        {
            pRefList->pLongRefList[i] = null;
        }
        for (i = 0; i < pCtx->pSvcParam->iNumRefFrame + 1; i++)
        {
            RefPics(pRefList)[i]->SetUnref();
        }

        pRefList->uiLongRefCount = 0;
        pRefList->uiShortRefCount = 0;
        pRefList->pNextBuffer = pRefList->pRef[0];
    }

    private static void DeleteLTRFromLongList(sWelsEncCtx* pCtx, int32_t iIdx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        int32_t k;

        for (k = iIdx; k < pRefList->uiLongRefCount - 1; k++)
        {
            pRefList->pLongRefList[k] = pRefList->pLongRefList[k + 1];
        }
        pRefList->pLongRefList[k] = null;
        pRefList->uiLongRefCount--;
    }

    private static void DeleteSTRFromShortList(sWelsEncCtx* pCtx, int32_t iIdx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        int32_t k;

        for (k = iIdx; k < pRefList->uiShortRefCount - 1; k++)
        {
            pRefList->pShortRefList[k] = pRefList->pShortRefList[k + 1];
        }
        pRefList->pShortRefList[k] = null;
        pRefList->uiShortRefCount--;
    }

    private static void DeleteNonSceneLTR(sWelsEncCtx* pCtx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        for (int32_t i = 0; i < pCtx->pSvcParam->iNumRefFrame; ++i)
        {
            SPicture* pRef = pRefList->pLongRefList[i];
            if (pRef != null && pRef->bUsedAsRef && pRef->bIsLongRef && (!pRef->bIsSceneLTR) &&
                (pCtx->uiTemporalId < pRef->uiTemporalId || pCtx->bCurFrameMarkedAsSceneLtr))
            {
                //this is our strategy to Unref all non-sceneLTR when the the current frame is sceneLTR
                pRef->SetUnref();
                DeleteLTRFromLongList(pCtx, i);
                i--;
            }
        }
    }

    private static int32_t CompareFrameNum(int32_t iFrameNumA, int32_t iFrameNumB, int32_t iMaxFrameNumPlus1)
    {
        int64_t iNumA, iNumB, iDiffAB, iDiffMin;
        if (iFrameNumA > iMaxFrameNumPlus1 || iFrameNumB > iMaxFrameNumPlus1)
        {
            return -2;
        }
        // #define  WelsAbsDiffInt64(a,b) ( (a) > (b) )?( a - b ):( b - a )
        static int64_t WelsAbsDiffInt64(int64_t a, int64_t b) => a > b ? a - b : b - a;

        iDiffAB = WelsAbsDiffInt64(iFrameNumA, iFrameNumB);

        iDiffMin = iDiffAB;
        if (iDiffMin == 0)
        {
            return FRAME_NUM_EQUAL;
        }

        iNumA = WelsAbsDiffInt64((int64_t)iFrameNumA + iMaxFrameNumPlus1, iFrameNumB);
        if (iNumA == 0)
        {
            return FRAME_NUM_EQUAL;
        }
        else if (iDiffMin > iNumA)
        {
            return FRAME_NUM_BIGGER;
        }

        iNumB = WelsAbsDiffInt64((int64_t)iFrameNumB + iMaxFrameNumPlus1, iFrameNumA);
        if (iNumB == 0)
        {
            return FRAME_NUM_EQUAL;
        }
        else if (iDiffMin > iNumB)
        {
            return FRAME_NUM_SMALLER;
        }

        return (iFrameNumA > iFrameNumB) ? (FRAME_NUM_BIGGER) : (FRAME_NUM_SMALLER);
    }

    /*
    *   delete failed mark according LTR recovery pRequest
    */
    private static void DeleteInvalidLTR(sWelsEncCtx* pCtx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        SPicture** pLongRefList = LongRefList(pRefList);
        SLTRState* pLtr = &pCtx->pLtr[pCtx->uiDependencyId];
        int32_t iMaxFrameNumPlus1 = (1 << (int32_t)pCtx->pSps->uiLog2MaxFrameNum);
        int32_t i;
        SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[pCtx->uiDependencyId];
        SLogContext* pLogCtx = &(pCtx->sLogCtx);

        for (i = 0; i < LONG_TERM_REF_NUM; i++)
        {
            if (pLongRefList[i] != null)
            {
                if (CompareFrameNum(pLongRefList[i]->iFrameNum, pLtr->iLastCorFrameNumDec, iMaxFrameNumPlus1) == FRAME_NUM_BIGGER
                    && (CompareFrameNum(pLongRefList[i]->iFrameNum, pLtr->iCurFrameNumInDec,
                                        iMaxFrameNumPlus1) & (FRAME_NUM_EQUAL | FRAME_NUM_SMALLER)) != 0)
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING, $"LTR ,invalid LTR delete ,long_term_idx = {pLongRefList[i]->iLongTermPicNum} , iFrameNum ={pLongRefList[i]->iFrameNum} ");
                    pLongRefList[i]->SetUnref();
                    DeleteLTRFromLongList(pCtx, i);
                    pLtr->bLTRMarkEnable = true;
                    if (pRefList->uiLongRefCount == 0)
                    {
                        pParamInternal->bEncCurFrmAsIdrFlag = true;
                    }
                }
                else if (CompareFrameNum(pLongRefList[i]->iMarkFrameNum, pLtr->iLastCorFrameNumDec,
                                         iMaxFrameNumPlus1) == FRAME_NUM_BIGGER
                         && (CompareFrameNum(pLongRefList[i]->iMarkFrameNum, pLtr->iCurFrameNumInDec,
                                             iMaxFrameNumPlus1) & (FRAME_NUM_EQUAL | FRAME_NUM_SMALLER)) != 0
                         && pLtr->iLTRMarkMode == LTR_DELAY_MARK)
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING, $"LTR ,iMarkFrameNum invalid LTR delete ,long_term_idx = {pLongRefList[i]->iLongTermPicNum} , iFrameNum ={pLongRefList[i]->iFrameNum} ");
                    pLongRefList[i]->SetUnref();
                    DeleteLTRFromLongList(pCtx, i);
                    pLtr->bLTRMarkEnable = true;
                    if (pRefList->uiLongRefCount == 0)
                    {
                        pParamInternal->bEncCurFrmAsIdrFlag = true;
                    }
                }
            }
        }
    }

    /*
    *   handle LTR Mark feedback message
    */
    private static void HandleLTRMarkFeedback(sWelsEncCtx* pCtx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        SPicture** pLongRefList = LongRefList(pRefList);
        SLTRState* pLtr = &pCtx->pLtr[pCtx->uiDependencyId];
        SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[pCtx->uiDependencyId];
        int32_t i, j;

        if (pLtr->uiLtrMarkState == (uint32_t)LTR_MARKING_SUCCESS)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                    $"pLtr->uiLtrMarkState = {pLtr->uiLtrMarkState}, pLtr.iCurLtrIdx = {pLtr->iCurLtrIdx} , pLtr->iLtrMarkFbFrameNum = {pLtr->iLtrMarkFbFrameNum} ,pCtx->iFrameNum = {pParamInternal->iFrameNum} ");
            for (i = 0; i < pRefList->uiLongRefCount; i++)
            {
                if (pLongRefList[i]->iFrameNum == pLtr->iLtrMarkFbFrameNum && pLongRefList[i]->uiRecieveConfirmed != RECIEVE_SUCCESS)
                {
                    pLongRefList[i]->uiRecieveConfirmed = RECIEVE_SUCCESS;
                    pCtx->pVaa->uiValidLongTermPicIdx = (uint8_t)pLongRefList[i]->iLongTermPicNum;

                    pLtr->iCurFrameNumInDec =
                        pLtr->iLastRecoverFrameNum =
                            pLtr->iLastCorFrameNumDec = pLtr->iLtrMarkFbFrameNum;

                    for (j = 0; j < pRefList->uiLongRefCount; j++)
                    {
                        if (pLongRefList[j]->iLongTermPicNum != pLtr->iCurLtrIdx)
                        {
                            pLongRefList[j]->SetUnref();
                            DeleteLTRFromLongList(pCtx, j);
                        }
                    }

                    pLtr->iLTRMarkSuccessNum++;
                    pLtr->iCurLtrIdx = (pLtr->iCurLtrIdx + 1) % LONG_TERM_REF_NUM;
                    pLtr->iLTRMarkMode = (pLtr->iLTRMarkSuccessNum >= (LONG_TERM_REF_NUM)) ? (LTR_DELAY_MARK) : (LTR_DIRECT_MARK);
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"LTR mark mode ={pLtr->iLTRMarkMode}");
                    pLtr->bLTRMarkEnable = true;
                    break;
                }
            }
            pLtr->uiLtrMarkState = (uint32_t)NO_LTR_MARKING_FEEDBACK;
        }
        else if (pLtr->uiLtrMarkState == (uint32_t)LTR_MARKING_FAILED)
        {
            for (i = 0; i < pRefList->uiLongRefCount; i++)
            {
                if (pLongRefList[i]->iFrameNum == pLtr->iLtrMarkFbFrameNum)
                {
                    pLongRefList[i]->SetUnref();
                    DeleteLTRFromLongList(pCtx, i);
                    break;
                }
            }
            pLtr->uiLtrMarkState = (uint32_t)NO_LTR_MARKING_FEEDBACK;
            pLtr->bLTRMarkEnable = true;

            if (pLtr->iLTRMarkSuccessNum == 0)
            {
                pParamInternal->bEncCurFrmAsIdrFlag = true; // no LTR , means IDR recieve failed, force next frame IDR
            }
        }
    }

    /*
     *  LTR mark process
     */
    private static void LTRMarkProcess(sWelsEncCtx* pCtx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        SPicture** pLongRefList = LongRefList(pRefList);
        SPicture** pShortRefList = ShortRefList(pRefList);
        SLTRState* pLtr = &pCtx->pLtr[pCtx->uiDependencyId];
        int32_t iGoPFrameNumInterval = ((pCtx->pSvcParam->uiGopSize >> 1) > 1) ? (int32_t)(pCtx->pSvcParam->uiGopSize >> 1) : (1);
        int32_t iMaxFrameNumPlus1 = (1 << (int32_t)pCtx->pSps->uiLog2MaxFrameNum);
        int32_t i = 0;
        int32_t j = 0;
        bool bMoveLtrFromShortToLong = false;
        SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[pCtx->uiDependencyId];

        if (pCtx->eSliceType == I_SLICE)
        {
            i = 0;
            pShortRefList[i]->uiRecieveConfirmed = RECIEVE_SUCCESS;
        }
        else if (pLtr->bLTRMarkingFlag)
        {
            pCtx->pVaa->uiMarkLongTermPicIdx = (uint8_t)pLtr->iCurLtrIdx;

            if (pLtr->iLTRMarkMode == LTR_DELAY_MARK)
            {
                for (i = 0; i < pRefList->uiShortRefCount; i++)
                {
                    if (CompareFrameNum(pParamInternal->iFrameNum, pShortRefList[i]->iFrameNum + iGoPFrameNumInterval,
                                        iMaxFrameNumPlus1) == FRAME_NUM_EQUAL)
                    {
                        break;
                    }
                }
            }
        }

        if (pCtx->eSliceType == I_SLICE || pLtr->bLTRMarkingFlag)
        {
            pShortRefList[i]->bIsLongRef = true;
            pShortRefList[i]->iLongTermPicNum = pLtr->iCurLtrIdx;
            pShortRefList[i]->iMarkFrameNum = pParamInternal->iFrameNum;
        }

        // delay one gop to move LTR from int16_t list to int32_t list
        if (pLtr->iLTRMarkMode == LTR_DIRECT_MARK && pCtx->eSliceType != I_SLICE && !pLtr->bLTRMarkingFlag)
        {
            for (j = 0; j < pRefList->uiShortRefCount; j++)
            {
                if (pShortRefList[j]->bIsLongRef)
                {
                    i = j;
                    bMoveLtrFromShortToLong = true;
                    break;
                }
            }
        }

        if ((pLtr->iLTRMarkMode == LTR_DELAY_MARK && pLtr->bLTRMarkingFlag)
            || ((pLtr->iLTRMarkMode == LTR_DIRECT_MARK) && (bMoveLtrFromShortToLong)))
        {
            pCtx->bRefOfCurTidIsLtr[pCtx->uiDependencyId * MAX_TEMPORAL_LEVEL + pCtx->uiTemporalId] = true;

            if (pRefList->uiLongRefCount > 0)
            {
                memmove(&pLongRefList[1], &pLongRefList[0],
                        pRefList->uiLongRefCount * sizeof(SPicture*)); // confirmed_safe_unsafe_usage
            }
            pLongRefList[0] = pShortRefList[i];
            pRefList->uiLongRefCount++;
            if (pRefList->uiLongRefCount > pCtx->pSvcParam->iLTRRefNum)
            {
                pLongRefList[pRefList->uiLongRefCount - 1]->SetUnref();
                DeleteLTRFromLongList(pCtx, pRefList->uiLongRefCount - 1);
            }
            DeleteSTRFromShortList(pCtx, i);
        }
    }

    // LTRMarkProcessScreen: screen content (Phase 4).

    public static void PrefetchNextBuffer(sWelsEncCtx* pCtx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        int32_t kiNumRef = pCtx->pSvcParam->iNumRefFrame;
        int32_t i;

        pRefList->pNextBuffer = null;
        for (i = 0; i < kiNumRef + 1; i++)
        {
            if (!RefPics(pRefList)[i]->bUsedAsRef)
            {
                pRefList->pNextBuffer = pRefList->pRef[i];
                break;
            }
        }

        if (pRefList->pNextBuffer == null && pRefList->uiShortRefCount > 0)
        {
            pRefList->pNextBuffer = pRefList->pShortRefList[pRefList->uiShortRefCount - 1];
            pRefList->pNextBuffer->SetUnref();
        }

        pCtx->pDecPic = pRefList->pNextBuffer;
    }

    /*
     *  update reference picture list
     */
    public static bool WelsUpdateRefList(sWelsEncCtx* pCtx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        SLTRState* pLtr = &pCtx->pLtr[pCtx->uiDependencyId];
        SSpatialLayerInternal* pParamD = &pCtx->pSvcParam->sDependencyLayers[pCtx->uiDependencyId];

        int32_t iRefIdx = 0;
        uint8_t kuiTid = pCtx->uiTemporalId;
        uint8_t kuiDid = pCtx->uiDependencyId;
        EWelsSliceType keSliceType = pCtx->eSliceType;
        uint32_t i = 0;
        // Need update pRef list in case store base layer or target dependency layer construction
        if (null == pCtx->pCurDqLayer)
        {
            return false;
        }

        if (null == pRefList || pRefList->pRef[0].IsNull)
        {
            return false;
        }

        if (null != pCtx->pDecPic)
        {
            // #if !defined(ENABLE_FRAME_DUMP) // to save complexity, 1/6/2009
            if ((pParamD->iHighestTemporalId == 0) || (kuiTid < pParamD->iHighestTemporalId))
            {
                // Expanding picture for future reference
                ExpandReferencingPicture((uint8_t**)&pCtx->pDecPic->pData, pCtx->pDecPic->iWidthInPixel, pCtx->pDecPic->iHeightInPixel,
                                         pCtx->pDecPic->iLineSize,
                                         pCtx->pFuncList->sExpandPicFunc.pfExpandLumaPicture, pCtx->pFuncList->sExpandPicFunc.pfExpandChromaPicture);
            }

            // move picture in list
            pCtx->pDecPic->uiTemporalId = kuiTid;
            pCtx->pDecPic->uiSpatialId = kuiDid;
            pCtx->pDecPic->iFrameNum = pParamD->iFrameNum;
            pCtx->pDecPic->iFramePoc = pParamD->iPOC;
            pCtx->pDecPic->uiRecieveConfirmed = RECIEVE_UNKOWN;
            pCtx->pDecPic->bUsedAsRef = true;

            for (iRefIdx = pRefList->uiShortRefCount - 1; iRefIdx >= 0; --iRefIdx)
            {
                pRefList->pShortRefList[iRefIdx + 1] = pRefList->pShortRefList[iRefIdx];
            }
            pRefList->pShortRefList[0] = pCtx->pDecPic;
            pRefList->uiShortRefCount++;
        }

        if (keSliceType == P_SLICE)
        {
            if (pCtx->uiTemporalId == 0)
            {
                if (pCtx->pSvcParam->bEnableLongTermReference)
                {
                    LTRMarkProcess(pCtx);
                    DeleteInvalidLTR(pCtx);
                    HandleLTRMarkFeedback(pCtx);

                    pLtr->bReceivedT0LostFlag = false; // reset to false due to the recovery is finished
                    pLtr->bLTRMarkingFlag = false;
                    ++pLtr->uiLtrMarkInterval;
                }

                for (i = (uint32_t)(pRefList->uiShortRefCount - 1); i > 0; i--)
                {
                    ShortRefList(pRefList)[i]->SetUnref();
                    DeleteSTRFromShortList(pCtx, (int32_t)i);
                }
                if (pRefList->uiShortRefCount > 0 && (ShortRefList(pRefList)[0]->uiTemporalId > 0
                                                      || ShortRefList(pRefList)[0]->iFrameNum != pParamD->iFrameNum))
                {
                    ShortRefList(pRefList)[0]->SetUnref();
                    DeleteSTRFromShortList(pCtx, 0);
                }
            }
        }
        else
        { // in case IDR currently coding
            if (pCtx->pSvcParam->bEnableLongTermReference)
            {
                LTRMarkProcess(pCtx);

                pLtr->iCurLtrIdx = (pLtr->iCurLtrIdx + 1) % LONG_TERM_REF_NUM;
                pLtr->iLTRMarkSuccessNum = 1; //IDR default suceess
                pLtr->bLTRMarkEnable = true;
                pLtr->uiLtrMarkInterval = 0;

                pCtx->pVaa->uiValidLongTermPicIdx = 0;
                pCtx->pVaa->uiMarkLongTermPicIdx = 0;
            }
        }
        pCtx->pReferenceStrategy->EndofUpdateRefList();
        return true;
    }

    public static bool CheckCurMarkFrameNumUsed(sWelsEncCtx* pCtx)
    {
        SLTRState* pLtr = &pCtx->pLtr[pCtx->uiDependencyId];
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        SPicture** pLongRefList = LongRefList(pRefList);
        int32_t iGoPFrameNumInterval = ((pCtx->pSvcParam->uiGopSize >> 1) > 1) ? (int32_t)(pCtx->pSvcParam->uiGopSize >> 1) : (1);
        int32_t iMaxFrameNumPlus1 = (1 << (int32_t)pCtx->pSps->uiLog2MaxFrameNum);
        SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[pCtx->uiDependencyId];
        int32_t i;

        for (i = 0; i < pRefList->uiLongRefCount; i++)
        {
            if ((pParamInternal->iFrameNum == pLongRefList[i]->iFrameNum && pLtr->iLTRMarkMode == LTR_DIRECT_MARK) ||
                (CompareFrameNum(pParamInternal->iFrameNum + iGoPFrameNumInterval, pLongRefList[i]->iFrameNum,
                                 iMaxFrameNumPlus1) == FRAME_NUM_EQUAL && pLtr->iLTRMarkMode == LTR_DELAY_MARK))
            {
                return false;
            }
        }

        return true;
    }

    private static void WelsMarkMMCORefInfoWithBase(SSlice** ppSliceList, SSlice* pBaseSlice, int32_t kiCountSliceNum)
    {
        int32_t iSliceIdx = 0;
        SSliceHeaderExt* pSliceHdrExt = null;
        SSliceHeaderExt* pBaseSHExt = &pBaseSlice->sSliceHeaderExt;

        for (iSliceIdx = 0; iSliceIdx < kiCountSliceNum; iSliceIdx++)
        {
            pSliceHdrExt = &ppSliceList[iSliceIdx]->sSliceHeaderExt;
            pSliceHdrExt->sSliceHeader.sRefMarking = pBaseSHExt->sSliceHeader.sRefMarking;
        }
    }

    public static void WelsMarkMMCORefInfo(sWelsEncCtx* pCtx, SLTRState* pLtr, SSlice** ppSliceList, int32_t kiCountSliceNum)
    {
        SSlice* pBaseSlice = ppSliceList[0];
        SRefPicMarking* pRefPicMark = &pBaseSlice->sSliceHeaderExt.sSliceHeader.sRefMarking;
        int32_t iGoPFrameNumInterval = ((pCtx->pSvcParam->uiGopSize >> 1) > 1) ? (int32_t)(pCtx->pSvcParam->uiGopSize >> 1) : (1);

        *pRefPicMark = default;

        if (pCtx->pSvcParam->bEnableLongTermReference && pLtr->bLTRMarkingFlag)
        {
            if (pLtr->iLTRMarkMode == LTR_DIRECT_MARK)
            {
                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount].iMaxLongTermFrameIdx = LONG_TERM_REF_NUM - 1;
                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount++].iMmcoType = (int32_t)MMCO_SET_MAX_LONG;

                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount].iDiffOfPicNum = iGoPFrameNumInterval;
                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount++].iMmcoType = (int32_t)MMCO_SHORT2UNUSED;

                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount].iLongTermFrameIdx = pLtr->iCurLtrIdx;
                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount++].iMmcoType = (int32_t)MMCO_LONG;
            }
            else if (pLtr->iLTRMarkMode == LTR_DELAY_MARK)
            {
                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount].iDiffOfPicNum = iGoPFrameNumInterval;
                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount].iLongTermFrameIdx = pLtr->iCurLtrIdx;
                pRefPicMark->SMmcoRef[pRefPicMark->uiMmcoCount++].iMmcoType = (int32_t)MMCO_SHORT2LONG;
            }
        }

        WelsMarkMMCORefInfoWithBase(ppSliceList, pBaseSlice, kiCountSliceNum);
    }

    public static void WelsMarkPic(sWelsEncCtx* pCtx)
    {
        SLTRState* pLtr = &pCtx->pLtr[pCtx->uiDependencyId];
        int32_t kiCountSliceNum = pCtx->pCurDqLayer->iMaxSliceNum;

        if (pCtx->pSvcParam->bEnableLongTermReference && pLtr->bLTRMarkEnable && pCtx->uiTemporalId == 0)
        {
            if (!pLtr->bReceivedT0LostFlag && pLtr->uiLtrMarkInterval > pCtx->pSvcParam->iLtrMarkPeriod
                && CheckCurMarkFrameNumUsed(pCtx))
            {
                pLtr->bLTRMarkingFlag = true;
                pLtr->bLTRMarkEnable = false;
                pLtr->uiLtrMarkInterval = 0;
                for (int32_t i = 0; i < codec_app_def.MAX_TEMPORAL_LAYER_NUM; ++i)
                {
                    if (pCtx->uiTemporalId < i || pCtx->uiTemporalId == 0)
                    {
                        pLtr->iLastLtrIdx[i] = pLtr->iCurLtrIdx;
                    }
                }
            }
            else
            {
                pLtr->bLTRMarkingFlag = false;
            }
        }

        WelsMarkMMCORefInfo(pCtx, pLtr, pCtx->pCurDqLayer->ppSliceInLayer, kiCountSliceNum);
    }

    public static int32_t FilterLTRRecoveryRequest(sWelsEncCtx* pCtx, SLTRRecoverRequest* pLTRRecoverRequest)
    {
        //if disable LTR, force IDR
        if (!pCtx->pSvcParam->bEnableLongTermReference)
        {
            for (int32_t iDid = 0; iDid < pCtx->pSvcParam->iSpatialLayerNum; iDid++)
            {
                SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[iDid];
                pParamInternal->bEncCurFrmAsIdrFlag = true;
            }
        }
        else
        {
            SLTRRecoverRequest* pRequest = pLTRRecoverRequest;
            int32_t iLayerId = pLTRRecoverRequest->iLayerId;
            if ((iLayerId < 0) || (iLayerId >= pCtx->pSvcParam->iSpatialLayerNum))
                return 0; // false

            SLTRState* pLtr = &pCtx->pLtr[iLayerId];
            int32_t iMaxFrameNumPlus1 = (1 << (int32_t)pCtx->pSps->uiLog2MaxFrameNum);
            SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[iLayerId];
            if (pRequest->uiFeedbackType == (uint32_t)LTR_RECOVERY_REQUEST && pRequest->uiIDRPicId == pParamInternal->uiIdrPicId)
            {
                if (pRequest->iLastCorrectFrameNum == -1)
                {
                    pParamInternal->bEncCurFrmAsIdrFlag = true;
                    return 1; // true
                }
                else if (pRequest->iCurrentFrameNum == -1)
                {
                    pLtr->bReceivedT0LostFlag = true;
                    return 1; // true
                }
                else if ((CompareFrameNum(pLtr->iLastRecoverFrameNum, pRequest->iLastCorrectFrameNum,
                                          iMaxFrameNumPlus1) & (FRAME_NUM_EQUAL | FRAME_NUM_SMALLER)) != 0 // t0 lost
                         || ((CompareFrameNum(pLtr->iLastRecoverFrameNum, pRequest->iCurrentFrameNum,
                                              iMaxFrameNumPlus1) & (FRAME_NUM_EQUAL | FRAME_NUM_SMALLER)) != 0 &&
                             CompareFrameNum(pLtr->iLastRecoverFrameNum, pRequest->iLastCorrectFrameNum,
                                             iMaxFrameNumPlus1) == FRAME_NUM_BIGGER)) // recovery failed
                {
                    pLtr->bReceivedT0LostFlag = true;
                    pLtr->iLastCorFrameNumDec = pRequest->iLastCorrectFrameNum;
                    pLtr->iCurFrameNumInDec = pRequest->iCurrentFrameNum;
                    WelsLog(&pCtx->sLogCtx, WELS_LOG_INFO,
                            $"Receive valid LTR recovery pRequest,feedback_type = {pRequest->uiFeedbackType} ,uiIdrPicId = {pRequest->uiIDRPicId} , current_frame_num = {pRequest->iCurrentFrameNum} , last correct frame num = {pRequest->iLastCorrectFrameNum}");
                }

                WelsLog(&pCtx->sLogCtx, WELS_LOG_INFO,
                        $"Receive LTR recovery pRequest,feedback_type = {pRequest->uiFeedbackType} ,uiIdrPicId = {pRequest->uiIDRPicId} , current_frame_num = {pRequest->iCurrentFrameNum} , last correct frame num = {pRequest->iLastCorrectFrameNum}");
            }
        }

        return 1; // true
    }

    public static void FilterLTRMarkingFeedback(sWelsEncCtx* pCtx, SLTRMarkingFeedback* pLTRMarkingFeedback)
    {
        int32_t iLayerId = pLTRMarkingFeedback->iLayerId;
        if ((iLayerId < 0) || (iLayerId >= pCtx->pSvcParam->iSpatialLayerNum))
        {
            return;
        }
        SLTRState* pLtr = &pCtx->pLtr[iLayerId];
        Debug.Assert(pLTRMarkingFeedback != null);
        if (pCtx->pSvcParam->bEnableLongTermReference)
        {
            SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[iLayerId];
            if (pLTRMarkingFeedback->uiIDRPicId == pParamInternal->uiIdrPicId
                && (pLTRMarkingFeedback->uiFeedbackType == (uint32_t)LTR_MARKING_SUCCESS
                    || pLTRMarkingFeedback->uiFeedbackType == (uint32_t)LTR_MARKING_FAILED)) // avoid error pData
            {
                pLtr->uiLtrMarkState = pLTRMarkingFeedback->uiFeedbackType;
                pLtr->iLtrMarkFbFrameNum = pLTRMarkingFeedback->iLTRFrameNum;
                WelsLog(&pCtx->sLogCtx, WELS_LOG_INFO,
                        $"Receive valid LTR marking feedback, feedback_type = {pLTRMarkingFeedback->uiFeedbackType} , uiIdrPicId = {pLTRMarkingFeedback->uiIDRPicId} , LTR_frame_num = {pLTRMarkingFeedback->iLTRFrameNum} , cur_idr_pic_id = {pParamInternal->uiIdrPicId}");
            }
            else
            {
                WelsLog(&pCtx->sLogCtx, WELS_LOG_INFO,
                        $"Receive LTR marking feedback, feedback_type = {pLTRMarkingFeedback->uiFeedbackType} , uiIdrPicId = {pLTRMarkingFeedback->uiIDRPicId} , LTR_frame_num = {pLTRMarkingFeedback->iLTRFrameNum} , cur_idr_pic_id = {pParamInternal->uiIdrPicId}");
            }
        }
    }

    /*
     *  build reference picture list
     */
    public static bool WelsBuildRefList(sWelsEncCtx* pCtx, int32_t iPOC, int32_t iBestLtrRefIdx)
    {
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        SLTRState* pLtr = &pCtx->pLtr[pCtx->uiDependencyId];
        int32_t kiNumRef = pCtx->pSvcParam->iNumRefFrame;
        uint8_t kuiTid = pCtx->uiTemporalId;
        uint32_t i = 0;
        SSpatialLayerInternal* pParamD = &pCtx->pSvcParam->sDependencyLayers[pCtx->uiDependencyId];
        // to support any type of cur_dq->mgs_control
        //    [ 0:    using current layer to do ME/MC;
        //     -1:    using store base layer to do ME/MC;
        //      2:    using highest layer to do ME/MC; ]

        // build reference list 0/1 if applicable

        pCtx->iNumRef0 = 0;
        if (pCtx->eSliceType != I_SLICE)
        {
            if (pCtx->pSvcParam->bEnableLongTermReference && pLtr->bReceivedT0LostFlag && pCtx->uiTemporalId == 0)
            {
                for (i = 0; i < pRefList->uiLongRefCount; i++)
                {
                    if (LongRefList(pRefList)[i]->uiRecieveConfirmed == RECIEVE_SUCCESS)
                    {
                        pCtx->pCurDqLayer->pRefOri[pCtx->iNumRef0] = pRefList->pLongRefList[(int32_t)i];
                        pCtx->pRefList0[pCtx->iNumRef0++] = pRefList->pLongRefList[(int32_t)i];
                        pLtr->iLastRecoverFrameNum = pParamD->iFrameNum;
                        WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO,
                                $"pRef is int32_t !iLastRecoverFrameNum = {pLtr->iLastRecoverFrameNum}, pRef iFrameNum = {((SPicture*)pCtx->pRefList0[0])->iFrameNum},LTR number = {pRefList->uiLongRefCount},");
                        break;
                    }
                }
            }
            else
            {
                for (i = 0; i < pRefList->uiShortRefCount; ++i)
                {
                    SPicture* pRef = pRefList->pShortRefList[(int32_t)i];
                    if (pRef != null && pRef->bUsedAsRef && pRef->iFramePoc >= 0 && pRef->uiTemporalId <= kuiTid)
                    {
                        pCtx->pCurDqLayer->pRefOri[pCtx->iNumRef0] = pRef;
                        pCtx->pRefList0[pCtx->iNumRef0++] = pRef;
                        WelsLog(&(pCtx->sLogCtx), WELS_LOG_DETAIL,
                                $"WelsBuildRefList pCtx->uiTemporalId = {pCtx->uiTemporalId},pRef->iFrameNum = {pRef->iFrameNum},pRef->uiTemporalId = {pRef->uiTemporalId}");
                    }
                }
            }
        }
        else
        { // safe for IDR
            WelsResetRefList(pCtx);  //for IDR, SHOULD reset pRef list.
            ResetLtrState(&pCtx->pLtr[pCtx->uiDependencyId]); //SHOULD update it when IDR.
            for (int32_t k = 0; k < MAX_TEMPORAL_LEVEL; k++)
            {
                pCtx->bRefOfCurTidIsLtr[pCtx->uiDependencyId * MAX_TEMPORAL_LEVEL + k] = false;
            }
            pCtx->pRefList0[0] = null;
        }

        if (pCtx->iNumRef0 > kiNumRef)
        {
            pCtx->iNumRef0 = (uint8_t)kiNumRef;
        }
        return (pCtx->iNumRef0 > 0 || pCtx->eSliceType == I_SLICE) ? (true) : (false);
    }

    // UpdateBlockStatic: screen content (Phase 4).

    public static void WelsUpdateSliceHeaderSyntax(sWelsEncCtx* pCtx, int32_t iAbsDiffPicNumMinus1,
                                                   SSlice** ppSliceList, int32_t uiFrameType)
    {
        int32_t kiCountSliceNum = pCtx->pCurDqLayer->iMaxSliceNum;
        SLTRState* pLtr = &pCtx->pLtr[pCtx->uiDependencyId];
        int32_t iIdx = 0;

        Debug.Assert(kiCountSliceNum > 0);

        for (iIdx = 0; iIdx < kiCountSliceNum; iIdx++)
        {
            SSliceHeaderExt* pSliceHdrExt = &ppSliceList[iIdx]->sSliceHeaderExt;
            SSliceHeader* pSliceHdr = &pSliceHdrExt->sSliceHeader;
            SRefPicListReorderSyntax* pRefReorder = &pSliceHdr->sRefReordering;
            SRefPicMarking* pRefPicMark = &pSliceHdr->sRefMarking;

            /*syntax for num_ref_idx_l0_active_minus1*/
            pSliceHdr->uiRefCount = pCtx->iNumRef0;
            if (pCtx->iNumRef0 > 0)
            {
                if ((!((SPicture*)pCtx->pRefList0[0])->bIsLongRef) || (!pCtx->pSvcParam->bEnableLongTermReference))
                {
                    pRefReorder->SReorderingSyntax[0].uiReorderingOfPicNumsIdc = 0;
                    pRefReorder->SReorderingSyntax[0].uiAbsDiffPicNumMinus1 = (uint32_t)iAbsDiffPicNumMinus1;
                    pRefReorder->SReorderingSyntax[1].uiReorderingOfPicNumsIdc = 3;
                }
                else
                {
                    // SReorderingSyntax has MAX_REFERENCE_REORDER_COUNT_NUM (2) entries, so with two
                    // references the terminator lands past the array: upstream writes into whatever
                    // follows, where the port's InlineArray throws.
                    int32_t iRefIdx = 0;
                    for (iRefIdx = 0; iRefIdx < pCtx->iNumRef0; iRefIdx++)
                    {
                        pRefReorder->SReorderingSyntax[iRefIdx].uiReorderingOfPicNumsIdc = 2;
                        pRefReorder->SReorderingSyntax[iRefIdx].iLongTermPicNum = (uint16_t)((SPicture*)pCtx->pRefList0[iRefIdx])->iLongTermPicNum;
                    }
                    pRefReorder->SReorderingSyntax[iRefIdx].uiReorderingOfPicNumsIdc = 3;
                }
            }

            /*syntax for dec_ref_pic_marking()*/
            if ((int32_t)videoFrameTypeIDR == uiFrameType)
            {
                pRefPicMark->bNoOutputOfPriorPicsFlag = false;
                pRefPicMark->bLongTermRefFlag = pCtx->pSvcParam->bEnableLongTermReference;
            }
            else
            {
                if (pCtx->pSvcParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
                {
                    pRefPicMark->bAdaptiveRefPicMarkingModeFlag = pCtx->pSvcParam->bEnableLongTermReference;
                }
                else
                {
                    pRefPicMark->bAdaptiveRefPicMarkingModeFlag = (pCtx->pSvcParam->bEnableLongTermReference
                        && pLtr->bLTRMarkingFlag) ? (true) : (false);
                }
            }
        }
    }

    /*
     *  update syntax for reference base related
     */
    public static void WelsUpdateRefSyntax(sWelsEncCtx* pCtx, int32_t iPOC, int32_t uiFrameType)
    {
        int32_t iAbsDiffPicNumMinus1 = -1;
        SSpatialLayerInternal* pParamD = &pCtx->pSvcParam->sDependencyLayers[pCtx->uiDependencyId];
        /*syntax for ref_pic_list_reordering()*/
        if (pCtx->iNumRef0 > 0)
        {
            iAbsDiffPicNumMinus1 = pParamD->iFrameNum - (((SPicture*)pCtx->pRefList0[0])->iFrameNum) - 1;

            if (iAbsDiffPicNumMinus1 < 0)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO, $"WelsUpdateRefSyntax():::uiAbsDiffPicNumMinus1:{iAbsDiffPicNumMinus1}");
                iAbsDiffPicNumMinus1 += (1 << (int32_t)(pCtx->pSps->uiLog2MaxFrameNum));
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO, $"WelsUpdateRefSyntax():::uiAbsDiffPicNumMinus1< 0, update as:{iAbsDiffPicNumMinus1}");
            }
        }

        WelsUpdateSliceHeaderSyntax(pCtx, iAbsDiffPicNumMinus1, pCtx->pCurDqLayer->ppSliceInLayer, uiFrameType);
    }

    // UpdateOriginalPicInfo, UpdateSrcPicListLosslessScreenRefSelectionWithLtr, UpdateSrcPicList,
    // WelsUpdateRefListScreen, WelsBuildRefListScreen, IsValidFrameNum, WelsMarkPicScreen and
    // WelsMarkMMCORefInfoScreen: screen content (Phase 4).

    public static void DoNothing(sWelsEncCtx* pointer)
    {
    }
}
