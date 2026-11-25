//-----------------------------------------------------------------------------
// Filename: paraset_strategy.cs
//
// Description: Port of codec/encoder/core/inc/paraset_strategy.h and
// codec/encoder/core/src/paraset_strategy.cpp: how SPS and PPS ids are
// assigned and rewritten across IDRs (eSpsPpsIdStrategy).
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The C++ interface IWelsParametersetStrategy and its five concrete classes
// all share one set of fields (declared in CWelsParametersetIdConstant), so
// the port makes them a single native struct. m_eClass records the dynamic
// type and each virtual method switches on it to the override the C++ vtable
// would pick; each class's own body is a private method named
// <Class>_<Method>. Instances live in native memory, as the C++ objects do
// (CreateParametersetStrategy / DeleteParametersetStrategy for WELS_NEW_OP /
// WELS_DELETE_OP). The _DEBUG-only checks (DebugPps, DebugSpsPps) are left
// out, as in the Release reference build.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (E1).
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
using System.Runtime.InteropServices;

namespace SIPSorcery.H264.Encoder;

/// <summary>Port addition: the dynamic type of an IWelsParametersetStrategy.</summary>
internal enum EParametersetStrategyClass
{
    CWelsParametersetIdConstant,
    CWelsParametersetIdIncreasing,
    CWelsParametersetSpsListing,
    CWelsParametersetSpsPpsListing,
    CWelsParametersetSpsListingPpsIncreasing,
}

/// <summary>
/// IWelsParametersetStrategy and its implementations (CWelsParametersetIdConstant,
/// CWelsParametersetIdNonConstant, CWelsParametersetIdIncreasing, CWelsParametersetSpsListing,
/// CWelsParametersetSpsPpsListing, CWelsParametersetSpsListingPpsIncreasing).
/// </summary>
internal unsafe struct IWelsParametersetStrategy
{
    public EParametersetStrategyClass m_eClass; // port addition: the C++ dynamic type

    // CWelsParametersetIdConstant (protected)
    public SParaSetOffset m_sParaSetOffset;
    public bool m_bSimulcastAVC;
    public int32_t m_iSpatialLayerNum;

    public uint32_t m_iBasicNeededSpsNum;
    public uint32_t m_iBasicNeededPpsNum;

    private readonly bool IsSpsListingFamily =>
        m_eClass is EParametersetStrategyClass.CWelsParametersetSpsListing
            or EParametersetStrategyClass.CWelsParametersetSpsPpsListing
            or EParametersetStrategyClass.CWelsParametersetSpsListingPpsIncreasing;

    public static IWelsParametersetStrategy* CreateParametersetStrategy(
      EParameterSetStrategy eSpsPpsIdStrategy, bool bSimulcastAVC,
      int32_t kiSpatialLayerNum)
    {
        IWelsParametersetStrategy* pParametersetStrategy =
            (IWelsParametersetStrategy*)NativeMemory.AllocZeroed((nuint)sizeof(IWelsParametersetStrategy));
        switch (eSpsPpsIdStrategy)
        {
            case INCREASING_ID:
                pParametersetStrategy->CWelsParametersetIdIncreasing_ctor(bSimulcastAVC, kiSpatialLayerNum);
                break;
            case SPS_LISTING:
                pParametersetStrategy->CWelsParametersetSpsListing_ctor(bSimulcastAVC, kiSpatialLayerNum);
                break;
            case SPS_LISTING_AND_PPS_INCREASING:
                pParametersetStrategy->CWelsParametersetSpsListingPpsIncreasing_ctor(bSimulcastAVC, kiSpatialLayerNum);
                break;
            case SPS_PPS_LISTING:
                pParametersetStrategy->CWelsParametersetSpsPpsListing_ctor(bSimulcastAVC, kiSpatialLayerNum);
                break;
            case CONSTANT_ID:
            default:
                pParametersetStrategy->CWelsParametersetIdConstant_ctor(bSimulcastAVC, kiSpatialLayerNum);
                break;
        }

        return pParametersetStrategy;
    }

    /// <summary>Port addition: WELS_DELETE_OP (pParametersetStrategy).</summary>
    public static void DeleteParametersetStrategy(ref IWelsParametersetStrategy* pParametersetStrategy)
    {
        if (pParametersetStrategy != null)
        {
            NativeMemory.Free(pParametersetStrategy);
            pParametersetStrategy = null;
        }
    }

    // ---- constructors ----

    private void CWelsParametersetIdConstant_ctor(bool bSimulcastAVC, int32_t kiSpatialLayerNum)
    {
        m_eClass = EParametersetStrategyClass.CWelsParametersetIdConstant;
        m_sParaSetOffset = default; // memset (&m_sParaSetOffset, 0, sizeof (m_sParaSetOffset));

        m_bSimulcastAVC = bSimulcastAVC;
        m_iSpatialLayerNum = kiSpatialLayerNum;

        m_iBasicNeededSpsNum = 1;
        m_iBasicNeededPpsNum = (uint32_t)(1 + m_iSpatialLayerNum);
    }

    private void CWelsParametersetIdIncreasing_ctor(bool bSimulcastAVC, int32_t kiSpatialLayerNum)
    {
        // CWelsParametersetIdNonConstant (bSimulcastAVC, kiSpatialLayerNum): CWelsParametersetIdConstant (...)
        CWelsParametersetIdConstant_ctor(bSimulcastAVC, kiSpatialLayerNum);
        m_eClass = EParametersetStrategyClass.CWelsParametersetIdIncreasing;
    }

    private void CWelsParametersetSpsListing_ctor(bool bSimulcastAVC, int32_t kiSpatialLayerNum)
    {
        CWelsParametersetIdConstant_ctor(bSimulcastAVC, kiSpatialLayerNum);
        m_eClass = EParametersetStrategyClass.CWelsParametersetSpsListing;
        m_sParaSetOffset = default;

        m_bSimulcastAVC = bSimulcastAVC;
        m_iSpatialLayerNum = kiSpatialLayerNum;

        m_iBasicNeededSpsNum = MAX_SPS_COUNT;
        m_iBasicNeededPpsNum = 1;
    }

    private void CWelsParametersetSpsPpsListing_ctor(bool bSimulcastAVC, int32_t kiSpatialLayerNum)
    {
        CWelsParametersetSpsListing_ctor(bSimulcastAVC, kiSpatialLayerNum);
        m_eClass = EParametersetStrategyClass.CWelsParametersetSpsPpsListing;
        m_sParaSetOffset = default;

        m_bSimulcastAVC = bSimulcastAVC;
        m_iSpatialLayerNum = kiSpatialLayerNum;

        m_iBasicNeededSpsNum = MAX_SPS_COUNT;
        m_iBasicNeededPpsNum = WelsEnc.MAX_PPS_COUNT;
    }

    private void CWelsParametersetSpsListingPpsIncreasing_ctor(bool bSimulcastAVC, int32_t kiSpatialLayerNum)
    {
        CWelsParametersetSpsListing_ctor(bSimulcastAVC, kiSpatialLayerNum);
        m_eClass = EParametersetStrategyClass.CWelsParametersetSpsListingPpsIncreasing;
    }

    // ---- virtual methods (dispatch) ----

    public int32_t GetPpsIdOffset(int32_t iPpsId) => m_eClass switch
    {
        EParametersetStrategyClass.CWelsParametersetIdIncreasing => CWelsParametersetIdIncreasing_GetPpsIdOffset(iPpsId),
        EParametersetStrategyClass.CWelsParametersetSpsListingPpsIncreasing => CWelsParametersetSpsListingPpsIncreasing_GetPpsIdOffset(iPpsId),
        _ => CWelsParametersetIdConstant_GetPpsIdOffset(iPpsId),
    };

    public int32_t GetSpsIdOffset(int32_t iPpsId, int32_t iSpsId) => m_eClass switch
    {
        EParametersetStrategyClass.CWelsParametersetIdIncreasing => CWelsParametersetIdIncreasing_GetSpsIdOffset(iPpsId, iSpsId),
        _ => CWelsParametersetIdConstant_GetSpsIdOffset(iPpsId, iSpsId),
    };

    public int32_t* GetSpsIdOffsetList(int iParasetType) => CWelsParametersetIdConstant_GetSpsIdOffsetList(iParasetType);

    public uint32_t GetAllNeededParasetNum() => CWelsParametersetIdConstant_GetAllNeededParasetNum();

    public uint32_t GetNeededSpsNum() => CWelsParametersetIdConstant_GetNeededSpsNum();

    public uint32_t GetNeededSubsetSpsNum() => IsSpsListingFamily
        ? CWelsParametersetSpsListing_GetNeededSubsetSpsNum()
        : CWelsParametersetIdConstant_GetNeededSubsetSpsNum();

    public uint32_t GetNeededPpsNum() => CWelsParametersetIdConstant_GetNeededPpsNum();

    public void LoadPrevious(SExistingParasetList* pExistingParasetList, SWelsSPS* pSpsArray,
                             SSubsetSps* pSubsetArray,
                             SWelsPPS* pPpsArray)
    {
        if (IsSpsListingFamily)
            CWelsParametersetSpsListing_LoadPrevious(pExistingParasetList, pSpsArray, pSubsetArray, pPpsArray);
        else
            CWelsParametersetIdConstant_LoadPrevious(pExistingParasetList, pSpsArray, pSubsetArray, pPpsArray);
    }

    public void Update(uint32_t kuiId, int iParasetType)
    {
        switch (m_eClass)
        {
            case EParametersetStrategyClass.CWelsParametersetIdIncreasing:
                CWelsParametersetIdIncreasing_Update(kuiId, iParasetType);
                break;
            case EParametersetStrategyClass.CWelsParametersetSpsListingPpsIncreasing:
                CWelsParametersetSpsListingPpsIncreasing_Update(kuiId, iParasetType);
                break;
            default:
                CWelsParametersetIdConstant_Update(kuiId, iParasetType);
                break;
        }
    }

    public void UpdatePpsList(sWelsEncCtx* pCtx)
    {
        if (m_eClass == EParametersetStrategyClass.CWelsParametersetSpsPpsListing)
            CWelsParametersetSpsPpsListing_UpdatePpsList(pCtx);
        // CWelsParametersetIdConstant::UpdatePpsList (sWelsEncCtx* pCtx) {};
    }

    public bool CheckParamCompatibility(SWelsSvcCodingParam* pCodingParam, SLogContext* pLogCtx) => IsSpsListingFamily
        ? CWelsParametersetSpsListing_CheckParamCompatibility(pCodingParam, pLogCtx)
        : true; // CWelsParametersetIdConstant::CheckParamCompatibility

    public uint32_t GenerateNewSps(sWelsEncCtx* pCtx, bool kbUseSubsetSps, int32_t iDlayerIndex,
                                   int32_t iDlayerCount,
                                   uint32_t kuiSpsId,
                                   ref SWelsSPS* pSps, ref SSubsetSps* pSubsetSps, bool bSVCBaselayer) => IsSpsListingFamily
        ? CWelsParametersetSpsListing_GenerateNewSps(pCtx, kbUseSubsetSps, iDlayerIndex, iDlayerCount, kuiSpsId, ref pSps, ref pSubsetSps, bSVCBaselayer)
        : CWelsParametersetIdConstant_GenerateNewSps(pCtx, kbUseSubsetSps, iDlayerIndex, iDlayerCount, kuiSpsId, ref pSps, ref pSubsetSps, bSVCBaselayer);

    public uint32_t InitPps(sWelsEncCtx* pCtx, uint32_t kiSpsId,
                            SWelsSPS* pSps,
                            SSubsetSps* pSubsetSps,
                            uint32_t kuiPpsId,
                            bool kbDeblockingFilterPresentFlag,
                            bool kbUsingSubsetSps,
                            bool kbEntropyCodingModeFlag) => m_eClass == EParametersetStrategyClass.CWelsParametersetSpsPpsListing
        ? CWelsParametersetSpsPpsListing_InitPps(pCtx, kiSpsId, pSps, pSubsetSps, kuiPpsId, kbDeblockingFilterPresentFlag, kbUsingSubsetSps, kbEntropyCodingModeFlag)
        : CWelsParametersetIdConstant_InitPps(pCtx, kiSpsId, pSps, pSubsetSps, kuiPpsId, kbDeblockingFilterPresentFlag, kbUsingSubsetSps, kbEntropyCodingModeFlag);

    public void SetUseSubsetFlag(uint32_t iPpsId, bool bUseSubsetSps) => CWelsParametersetIdConstant_SetUseSubsetFlag(iPpsId, bUseSubsetSps);

    public void UpdateParaSetNum(sWelsEncCtx* pCtx)
    {
        switch (m_eClass)
        {
            case EParametersetStrategyClass.CWelsParametersetSpsPpsListing:
                CWelsParametersetSpsPpsListing_UpdateParaSetNum(pCtx);
                break;
            case EParametersetStrategyClass.CWelsParametersetSpsListing:
            case EParametersetStrategyClass.CWelsParametersetSpsListingPpsIncreasing:
                CWelsParametersetSpsListing_UpdateParaSetNum(pCtx);
                break;
            default:
                // CWelsParametersetIdConstant::UpdateParaSetNum (sWelsEncCtx* pCtx) {};
                break;
        }
    }

    public int32_t GetCurrentPpsId(int32_t iPpsId, int32_t iIdrLoop) => m_eClass == EParametersetStrategyClass.CWelsParametersetSpsPpsListing
        ? CWelsParametersetSpsPpsListing_GetCurrentPpsId(iPpsId, iIdrLoop)
        : iPpsId; // CWelsParametersetIdConstant::GetCurrentPpsId

    public void OutputCurrentStructure(SParaSetOffsetVariable* pParaSetOffsetVariable, int32_t* pPpsIdList,
                                       sWelsEncCtx* pCtx, SExistingParasetList* pExistingParasetList)
    {
        switch (m_eClass)
        {
            case EParametersetStrategyClass.CWelsParametersetSpsPpsListing:
                CWelsParametersetSpsPpsListing_OutputCurrentStructure(pParaSetOffsetVariable, pPpsIdList, pCtx, pExistingParasetList);
                break;
            case EParametersetStrategyClass.CWelsParametersetSpsListing:
            case EParametersetStrategyClass.CWelsParametersetSpsListingPpsIncreasing:
                CWelsParametersetSpsListing_OutputCurrentStructure(pParaSetOffsetVariable, pPpsIdList, pCtx, pExistingParasetList);
                break;
            case EParametersetStrategyClass.CWelsParametersetIdIncreasing:
                CWelsParametersetIdNonConstant_OutputCurrentStructure(pParaSetOffsetVariable, pPpsIdList, pCtx, pExistingParasetList);
                break;
            default:
                // CWelsParametersetIdConstant::OutputCurrentStructure (...) {};
                break;
        }
    }

    public void LoadPreviousStructure(SParaSetOffsetVariable* pParaSetOffsetVariable, int32_t* pPpsIdList)
    {
        switch (m_eClass)
        {
            case EParametersetStrategyClass.CWelsParametersetSpsPpsListing:
                CWelsParametersetSpsPpsListing_LoadPreviousStructure(pParaSetOffsetVariable, pPpsIdList);
                break;
            case EParametersetStrategyClass.CWelsParametersetIdIncreasing:
            case EParametersetStrategyClass.CWelsParametersetSpsListing:
            case EParametersetStrategyClass.CWelsParametersetSpsListingPpsIncreasing:
                CWelsParametersetIdNonConstant_LoadPreviousStructure(pParaSetOffsetVariable, pPpsIdList);
                break;
            default:
                // CWelsParametersetIdConstant::LoadPreviousStructure (...) {};
                break;
        }
    }

    public int32_t GetSpsIdx(int32_t iIdx) => IsSpsListingFamily
        ? iIdx  // CWelsParametersetSpsListing::GetSpsIdx
        : 0;    // CWelsParametersetIdConstant::GetSpsIdx

    // protected virtuals

    private void LoadPreviousSps(SExistingParasetList* pExistingParasetList, SWelsSPS* pSpsArray,
                                 SSubsetSps* pSubsetArray)
    {
        if (IsSpsListingFamily)
            CWelsParametersetSpsListing_LoadPreviousSps(pExistingParasetList, pSpsArray, pSubsetArray);
        // CWelsParametersetIdConstant::LoadPreviousSps (...) {};
    }

    private void LoadPreviousPps(SExistingParasetList* pExistingParasetList, SWelsPPS* pPpsArray)
    {
        if (m_eClass == EParametersetStrategyClass.CWelsParametersetSpsPpsListing)
            CWelsParametersetSpsPpsListing_LoadPreviousPps(pExistingParasetList, pPpsArray);
        // CWelsParametersetIdConstant::LoadPreviousPps (...) {};
    }

    private bool CheckPpsGenerating() => m_eClass == EParametersetStrategyClass.CWelsParametersetSpsPpsListing
        ? CWelsParametersetSpsPpsListing_CheckPpsGenerating()
        : CWelsParametersetSpsListing_CheckPpsGenerating();

    private int32_t SpsReset(sWelsEncCtx* pCtx, bool kbUseSubsetSps) => m_eClass == EParametersetStrategyClass.CWelsParametersetSpsPpsListing
        ? CWelsParametersetSpsPpsListing_SpsReset(pCtx, kbUseSubsetSps)
        : CWelsParametersetSpsListing_SpsReset(pCtx, kbUseSubsetSps);

    // ---- free functions ----

    private static int32_t WelsGenerateNewSps(sWelsEncCtx* pCtx, bool kbUseSubsetSps, int32_t iDlayerIndex,
                                              int32_t iDlayerCount, int32_t kiSpsId,
                                              ref SWelsSPS* pSps, ref SSubsetSps* pSubsetSps, bool bSVCBaselayer)
    {
        int32_t iRet = 0;

        if (!kbUseSubsetSps)
        {
            pSps = &(pCtx->pSpsArray[kiSpsId]);
        }
        else
        {
            pSubsetSps = &(pCtx->pSubsetArray[kiSpsId]);
            pSps = &pSubsetSps->pSps;
        }

        SWelsSvcCodingParam* pParam = pCtx->pSvcParam;
        SSpatialLayerConfig* pDlayerParam = (SSpatialLayerConfig*)&pParam->sSpatialLayers + iDlayerIndex;
        SSpatialLayerInternal* pDlayerInternal = (SSpatialLayerInternal*)&pParam->sDependencyLayers + iDlayerIndex;
        // Need port pSps/pPps initialization due to spatial scalability changed
        if (!kbUseSubsetSps)
        {
            iRet = WelsEnc.WelsInitSps(pSps, pDlayerParam, pDlayerInternal, pParam->uiIntraPeriod,
                                       pParam->iMaxNumRefFrame,
                                       (uint32_t)kiSpsId, pParam->bEnableFrameCroppingFlag, pParam->iRCMode != RC_OFF_MODE, iDlayerCount,
                                       bSVCBaselayer);
        }
        else
        {
            iRet = WelsEnc.WelsInitSubsetSps(pSubsetSps, pDlayerParam, pDlayerInternal, pParam->uiIntraPeriod,
                                             pParam->iMaxNumRefFrame,
                                             (uint32_t)kiSpsId, pParam->bEnableFrameCroppingFlag, pParam->iRCMode != RC_OFF_MODE, iDlayerCount);
        }
        return iRet;
    }

    private static bool CheckMatchedSps(SWelsSPS* pSps1, SWelsSPS* pSps2)
    {
        if ((pSps1->iMbWidth != pSps2->iMbWidth)
            || (pSps1->iMbHeight != pSps2->iMbHeight))
        {
            return false;
        }

        if ((pSps1->uiLog2MaxFrameNum != pSps2->uiLog2MaxFrameNum)
            || (pSps1->iLog2MaxPocLsb != pSps2->iLog2MaxPocLsb))
        {
            return false;
        }

        if (pSps1->iNumRefFrames != pSps2->iNumRefFrames)
        {
            return false;
        }

        if ((pSps1->bFrameCroppingFlag != pSps2->bFrameCroppingFlag)
            || (pSps1->sFrameCrop.iCropLeft != pSps2->sFrameCrop.iCropLeft)
            || (pSps1->sFrameCrop.iCropRight != pSps2->sFrameCrop.iCropRight)
            || (pSps1->sFrameCrop.iCropTop != pSps2->sFrameCrop.iCropTop)
            || (pSps1->sFrameCrop.iCropBottom != pSps2->sFrameCrop.iCropBottom)
           )
        {
            return false;
        }

        if ((pSps1->uiProfileIdc != pSps2->uiProfileIdc)
            || (pSps1->bConstraintSet0Flag != pSps2->bConstraintSet0Flag)
            || (pSps1->bConstraintSet1Flag != pSps2->bConstraintSet1Flag)
            || (pSps1->bConstraintSet2Flag != pSps2->bConstraintSet2Flag)
            || (pSps1->bConstraintSet3Flag != pSps2->bConstraintSet3Flag)
            || (pSps1->iLevelIdc != pSps2->iLevelIdc))
        {
            return false;
        }

        return true;
    }

    private static bool CheckMatchedSubsetSps(SSubsetSps* pSubsetSps1, SSubsetSps* pSubsetSps2)
    {
        if (!CheckMatchedSps(&pSubsetSps1->pSps, &pSubsetSps2->pSps))
        {
            return false;
        }

        if ((pSubsetSps1->sSpsSvcExt.iExtendedSpatialScalability != pSubsetSps2->sSpsSvcExt.iExtendedSpatialScalability)
            || (pSubsetSps1->sSpsSvcExt.bAdaptiveTcoeffLevelPredFlag != pSubsetSps2->sSpsSvcExt.bAdaptiveTcoeffLevelPredFlag)
            || (pSubsetSps1->sSpsSvcExt.bSeqTcoeffLevelPredFlag != pSubsetSps2->sSpsSvcExt.bSeqTcoeffLevelPredFlag)
            || (pSubsetSps1->sSpsSvcExt.bSliceHeaderRestrictionFlag != pSubsetSps2->sSpsSvcExt.bSliceHeaderRestrictionFlag))
        {
            return false;
        }

        return true;
    }

    /*!
     * \brief   check if the current parameter can found a presenting sps
     * \param   pParam          the current encoding paramter in SWelsSvcCodingParam
     * \param   kbUseSubsetSps  bool
     * \param   iDlayerIndex    int, the index of current D layer
     * \param   iDlayerCount    int, the number of total D layer
     * \param pSpsArray         array of all the stored SPSs
     * \param   pSubsetArray    array of all the stored Subset-SPSs
     * \return  0 - successful
     *         -1 - cannot find existing SPS for current encoder parameter
     */
    public static int32_t FindExistingSps(SWelsSvcCodingParam* pParam, bool kbUseSubsetSps, int32_t iDlayerIndex,
                                          int32_t iDlayerCount, int32_t iSpsNumInUse,
                                          SWelsSPS* pSpsArray,
                                          SSubsetSps* pSubsetArray, bool bSVCBaseLayer)
    {
        SSpatialLayerConfig* pDlayerParam = (SSpatialLayerConfig*)&pParam->sSpatialLayers + iDlayerIndex;
        SSpatialLayerInternal* pDlayerInternal = (SSpatialLayerInternal*)&pParam->sDependencyLayers + iDlayerIndex;

        Debug.Assert(iSpsNumInUse <= MAX_SPS_COUNT);
        if (!kbUseSubsetSps)
        {
            SWelsSPS sTmpSps;
            WelsEnc.WelsInitSps(&sTmpSps, pDlayerParam, pDlayerInternal, pParam->uiIntraPeriod,
                                pParam->iMaxNumRefFrame,
                                0, pParam->bEnableFrameCroppingFlag, pParam->iRCMode != RC_OFF_MODE, iDlayerCount,
                                bSVCBaseLayer);
            for (int32_t iId = 0; iId < iSpsNumInUse; iId++)
            {
                if (CheckMatchedSps(&sTmpSps, &pSpsArray[iId]))
                {
                    return iId;
                }
            }
        }
        else
        {
            SSubsetSps sTmpSubsetSps;
            WelsEnc.WelsInitSubsetSps(&sTmpSubsetSps, pDlayerParam, pDlayerInternal, pParam->uiIntraPeriod,
                                      pParam->iMaxNumRefFrame,
                                      0, pParam->bEnableFrameCroppingFlag, pParam->iRCMode != RC_OFF_MODE, iDlayerCount);

            for (int32_t iId = 0; iId < iSpsNumInUse; iId++)
            {
                if (CheckMatchedSubsetSps(&sTmpSubsetSps, &pSubsetArray[iId]))
                {
                    return iId;
                }
            }
        }

        return WelsEnc.INVALID_ID;
    }

    // ---- CWelsParametersetIdConstant ----

    private int32_t CWelsParametersetIdConstant_GetPpsIdOffset(int32_t iPpsId)
    {
        return 0;
    }

    private int32_t CWelsParametersetIdConstant_GetSpsIdOffset(int32_t iPpsId, int32_t iSpsId)
    {
        return 0;
    }

    private int32_t* CWelsParametersetIdConstant_GetSpsIdOffsetList(int iParasetType)
    {
        fixed (SParaSetOffset* p = &m_sParaSetOffset)
        {
            // m_sParaSetOffset lives in native memory (the strategy object), so the pointer stays valid.
            return &((SParaSetOffsetVariable*)&p->sParaSetOffsetVariable + iParasetType)->iParaSetIdDelta[0];
        }
    }

    private uint32_t CWelsParametersetIdConstant_GetAllNeededParasetNum()
    {
        return (GetNeededSpsNum()
                + GetNeededSubsetSpsNum()
                + GetNeededPpsNum());
    }

    private uint32_t CWelsParametersetIdConstant_GetNeededSpsNum()
    {
        if (0 >= m_sParaSetOffset.uiNeededSpsNum)
        {
            m_sParaSetOffset.uiNeededSpsNum = (uint32_t)(m_iBasicNeededSpsNum * ((m_bSimulcastAVC) ? (m_iSpatialLayerNum) : (1)));
        }
        return m_sParaSetOffset.uiNeededSpsNum;
    }

    private uint32_t CWelsParametersetIdConstant_GetNeededSubsetSpsNum()
    {
        if (0 >= m_sParaSetOffset.uiNeededSubsetSpsNum)
        {
            m_sParaSetOffset.uiNeededSubsetSpsNum = (uint32_t)(m_bSimulcastAVC ? 0 : (m_iSpatialLayerNum - 1));
        }
        return m_sParaSetOffset.uiNeededSubsetSpsNum;
    }

    private uint32_t CWelsParametersetIdConstant_GetNeededPpsNum()
    {
        if (0 == m_sParaSetOffset.uiNeededPpsNum)
        {
            m_sParaSetOffset.uiNeededPpsNum = (uint32_t)(m_iBasicNeededPpsNum * ((m_bSimulcastAVC) ? (m_iSpatialLayerNum) :
                                              (1)));
        }
        return m_sParaSetOffset.uiNeededPpsNum;
    }

    private void CWelsParametersetIdConstant_LoadPrevious(SExistingParasetList* pExistingParasetList, SWelsSPS* pSpsArray,
        SSubsetSps* pSubsetArray, SWelsPPS* pPpsArray)
    {
        return;
    }

    private void CWelsParametersetIdConstant_Update(uint32_t kuiId, int iParasetType)
    {
        m_sParaSetOffset = default; // memset (&m_sParaSetOffset, 0, sizeof (SParaSetOffset));
    }

    private uint32_t CWelsParametersetIdConstant_GenerateNewSps(sWelsEncCtx* pCtx, bool kbUseSubsetSps,
        int32_t iDlayerIndex,
        int32_t iDlayerCount, uint32_t kuiSpsId,
        ref SWelsSPS* pSps, ref SSubsetSps* pSubsetSps, bool bSVCBaselayer)
    {
        WelsGenerateNewSps(pCtx, kbUseSubsetSps, iDlayerIndex,
                           iDlayerCount, (int32_t)kuiSpsId,
                           ref pSps, ref pSubsetSps, bSVCBaselayer);
        return kuiSpsId;
    }

    private uint32_t CWelsParametersetIdConstant_InitPps(sWelsEncCtx* pCtx, uint32_t kiSpsId,
        SWelsSPS* pSps,
        SSubsetSps* pSubsetSps,
        uint32_t kuiPpsId,
        bool kbDeblockingFilterPresentFlag,
        bool kbUsingSubsetSps,
        bool kbEntropyCodingModeFlag)
    {
        WelsEnc.WelsInitPps(&pCtx->pPPSArray[kuiPpsId], pSps, pSubsetSps, kuiPpsId, true, kbUsingSubsetSps, kbEntropyCodingModeFlag);
        SetUseSubsetFlag(kuiPpsId, kbUsingSubsetSps);
        return kuiPpsId;
    }

    private void CWelsParametersetIdConstant_SetUseSubsetFlag(uint32_t iPpsId, bool bUseSubsetSps)
    {
        m_sParaSetOffset.bPpsIdMappingIntoSubsetsps[(int)iPpsId] = bUseSubsetSps;
    }

    // ---- CWelsParametersetIdNonConstant ----

    private void CWelsParametersetIdNonConstant_OutputCurrentStructure(SParaSetOffsetVariable* pParaSetOffsetVariable,
        int32_t* pPpsIdList, sWelsEncCtx* pCtx, SExistingParasetList* pExistingParasetList)
    {
        for (int32_t k = 0; k < WelsEnc.PARA_SET_TYPE; k++)
        {
            fixed (bool* pUsed = m_sParaSetOffset.sParaSetOffsetVariable[k].bUsedParaSetIdInBs)
            {
                memset(pUsed, 0, WelsEnc.MAX_PPS_COUNT * sizeof(bool));
            }
        }
        fixed (SParaSetOffsetVariable* pSrc = &m_sParaSetOffset.sParaSetOffsetVariable[0])
        {
            memcpy(pParaSetOffsetVariable, pSrc,
                   (WelsEnc.PARA_SET_TYPE) * sizeof(SParaSetOffsetVariable)); // confirmed_safe_unsafe_usage
        }
    }

    private void CWelsParametersetIdNonConstant_LoadPreviousStructure(SParaSetOffsetVariable* pParaSetOffsetVariable,
        int32_t* pPpsIdList)
    {
        fixed (SParaSetOffsetVariable* pDst = &m_sParaSetOffset.sParaSetOffsetVariable[0])
        {
            memcpy(pDst, pParaSetOffsetVariable,
                   (WelsEnc.PARA_SET_TYPE) * sizeof(SParaSetOffsetVariable)); // confirmed_safe_unsafe_usage
        }
    }

    // ---- CWelsParametersetIdIncreasing ----

    public static void ParasetIdAdditionIdAdjust(SParaSetOffsetVariable* sParaSetOffsetVariable,
                                                 int32_t kiCurEncoderParaSetId,
                                                 uint32_t kuiMaxIdInBs)
    { //paraset_type = 0: SPS; =1: PPS
        //SPS_ID in avc_sps and pSubsetSps will be different using this
        //SPS_ID case example:
        //1st enter:  next_spsid_in_bs == 0; spsid == 0; delta==0;            //actual spsid_in_bs == 0
        //1st finish: next_spsid_in_bs == 1;
        //2nd enter:  next_spsid_in_bs == 1; spsid == 0; delta==1;            //actual spsid_in_bs == 1
        //2nd finish: next_spsid_in_bs == 2;
        //31st enter: next_spsid_in_bs == 31; spsid == 0~2; delta==31~29;     //actual spsid_in_bs == 31
        //31st finish:next_spsid_in_bs == 0;
        //31st enter: next_spsid_in_bs == 0; spsid == 0~2; delta==-2~0;       //actual spsid_in_bs == 0
        //31st finish:next_spsid_in_bs == 1;

        int32_t kiEncId = kiCurEncoderParaSetId;
        uint32_t uiNextIdInBs = sParaSetOffsetVariable->uiNextParaSetIdToUseInBs;

        //update current layer's pCodingParam
        sParaSetOffsetVariable->iParaSetIdDelta[kiEncId] = (int32_t)(uiNextIdInBs -
            (uint32_t)kiEncId);  //for current parameter set, change its id_delta
        //write pso pData for next update:
        sParaSetOffsetVariable->bUsedParaSetIdInBs[uiNextIdInBs] = true; //   update current used_id

        //prepare for next update:
        //   find the next avaibable iId
        ++uiNextIdInBs;
        if (uiNextIdInBs >= kuiMaxIdInBs)
        {
            uiNextIdInBs = 0;//ensure the SPS_ID wound not exceed MAX_SPS_COUNT
        }
        //   update next_id
        sParaSetOffsetVariable->uiNextParaSetIdToUseInBs = uiNextIdInBs;
    }

    private void CWelsParametersetIdIncreasing_Update(uint32_t kuiId, int iParasetType)
    {
        fixed (SParaSetOffsetVariable* pVar = &m_sParaSetOffset.sParaSetOffsetVariable[iParasetType])
        {
            ParasetIdAdditionIdAdjust(pVar,
                                      (int32_t)kuiId,
                                      (uint32_t)((iParasetType != WelsEnc.PARA_SET_TYPE_PPS) ? MAX_SPS_COUNT : WelsEnc.MAX_PPS_COUNT));
        }
    }
    //((SPS_PPS_LISTING != pEncCtx->pSvcParam->eSpsPpsIdStrategy) ? (&
    //  (pEncCtx->sPSOVector.sParaSetOffsetVariable[PARA_SET_TYPE_PPS].iParaSetIdDelta[0])) : NULL)

    private int32_t CWelsParametersetIdIncreasing_GetPpsIdOffset(int32_t kiPpsId)
    {
        return (m_sParaSetOffset.sParaSetOffsetVariable[WelsEnc.PARA_SET_TYPE_PPS].iParaSetIdDelta[kiPpsId]);
    }

    private int32_t CWelsParametersetIdIncreasing_GetSpsIdOffset(int32_t kiPpsId, int32_t kiSpsId)
    {
        int32_t kiParameterSetType = (m_sParaSetOffset.bPpsIdMappingIntoSubsetsps[kiPpsId] ?
                                      WelsEnc.PARA_SET_TYPE_SUBSETSPS : WelsEnc.PARA_SET_TYPE_AVCSPS);
        return (m_sParaSetOffset.sParaSetOffsetVariable[kiParameterSetType].iParaSetIdDelta[kiSpsId]);
    }

    // ---- CWelsParametersetSpsListing ----

    private uint32_t CWelsParametersetSpsListing_GetNeededSubsetSpsNum()
    {
        if (0 >= m_sParaSetOffset.uiNeededSubsetSpsNum)
        {
            //      sPSOVector.uiNeededSubsetSpsNum = ((pSvcParam->bSimulcastAVC) ? (0) :((SPS_LISTING & pSvcParam->eSpsPpsIdStrategy) ? (MAX_SPS_COUNT) : (pSvcParam->iSpatialLayerNum - 1)));
            m_sParaSetOffset.uiNeededSubsetSpsNum = (uint32_t)((m_bSimulcastAVC) ? (0) :
                                                    (MAX_SPS_COUNT));
        }
        return m_sParaSetOffset.uiNeededSubsetSpsNum;
    }

    private void CWelsParametersetSpsListing_LoadPreviousSps(SExistingParasetList* pExistingParasetList, SWelsSPS* pSpsArray,
        SSubsetSps* pSubsetArray)
    {
        //if ((SPS_LISTING & pParam->eSpsPpsIdStrategy) && (NULL != pExistingParasetList)) {
        m_sParaSetOffset.uiInUseSpsNum = pExistingParasetList->uiInUseSpsNum;
        memcpy(pSpsArray, &pExistingParasetList->sSps, MAX_SPS_COUNT * sizeof(SWelsSPS));

        if (GetNeededSubsetSpsNum() > 0)
        {
            m_sParaSetOffset.uiInUseSubsetSpsNum = pExistingParasetList->uiInUseSubsetSpsNum;
            memcpy(pSubsetArray, &pExistingParasetList->sSubsetSps, MAX_SPS_COUNT * sizeof(SSubsetSps));
        }
        else
        {
            m_sParaSetOffset.uiInUseSubsetSpsNum = 0;
        }
        //}
    }

    private void CWelsParametersetSpsListing_LoadPrevious(SExistingParasetList* pExistingParasetList, SWelsSPS* pSpsArray,
        SSubsetSps* pSubsetArray, SWelsPPS* pPpsArray)
    {
        if (null == pExistingParasetList)
        {
            return;
        }
        LoadPreviousSps(pExistingParasetList, pSpsArray, pSubsetArray);
        LoadPreviousPps(pExistingParasetList, pPpsArray);
    }

    private bool CWelsParametersetSpsListing_CheckParamCompatibility(SWelsSvcCodingParam* pCodingParam, SLogContext* pLogCtx)
    {
        if (pCodingParam->iSpatialLayerNum > 1 && (!pCodingParam->bSimulcastAVC))
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $"ParamValidationExt(), eSpsPpsIdStrategy setting ({(int)pCodingParam->eSpsPpsIdStrategy}) with multiple svc SpatialLayers ({pCodingParam->iSpatialLayerNum}) not supported! eSpsPpsIdStrategy adjusted to CONSTANT_ID");
            pCodingParam->eSpsPpsIdStrategy = CONSTANT_ID;
            return false;
        }
        return true;
    }

    private bool CWelsParametersetSpsListing_CheckPpsGenerating()
    {
        return true;
    }

    private int32_t CWelsParametersetSpsListing_SpsReset(sWelsEncCtx* pCtx, bool kbUseSubsetSps)
    {
        // reset current list
        if (!kbUseSubsetSps)
        {
            m_sParaSetOffset.uiInUseSpsNum = 1;
            memset(pCtx->pSpsArray, 0, MAX_SPS_COUNT * sizeof(SWelsSPS));
        }
        else
        {
            m_sParaSetOffset.uiInUseSubsetSpsNum = 1;
            memset(pCtx->pSubsetArray, 0, MAX_SPS_COUNT * sizeof(SSubsetSps));
        }

        //iSpsId = 0;
        return 0;
    }

    private uint32_t CWelsParametersetSpsListing_GenerateNewSps(sWelsEncCtx* pCtx, bool kbUseSubsetSps,
        int32_t iDlayerIndex,
        int32_t iDlayerCount, uint32_t kuiSpsId,
        ref SWelsSPS* pSps, ref SSubsetSps* pSubsetSps, bool bSvcBaselayer)
    {
        //check if the current param can fit in an existing SPS
        int32_t kiFoundSpsId = FindExistingSps(pCtx->pSvcParam, kbUseSubsetSps, iDlayerIndex, iDlayerCount,
                                   (int32_t)(kbUseSubsetSps ? (m_sParaSetOffset.uiInUseSubsetSpsNum) : (m_sParaSetOffset.uiInUseSpsNum)),
                                   pCtx->pSpsArray,
                                   pCtx->pSubsetArray, bSvcBaselayer);

        if (WelsEnc.INVALID_ID != kiFoundSpsId)
        {
            //if yes, set pSps or pSubsetSps to it
            kuiSpsId = (uint32_t)kiFoundSpsId;
            if (!kbUseSubsetSps)
            {
                pSps = &(pCtx->pSpsArray[kiFoundSpsId]);
            }
            else
            {
                pSubsetSps = &(pCtx->pSubsetArray[kiFoundSpsId]);
            }
        }
        else
        {
            //if no, generate a new SPS as usual
            if (!CheckPpsGenerating())
            {
                return unchecked((uint32_t)(-1));
            }

            kuiSpsId = (!kbUseSubsetSps) ? (m_sParaSetOffset.uiInUseSpsNum++) : (m_sParaSetOffset.uiInUseSubsetSpsNum++);
            if (kuiSpsId >= MAX_SPS_COUNT)
            {
                if (SpsReset(pCtx, kbUseSubsetSps) < 0)
                {
                    return unchecked((uint32_t)(-1));
                }
                kuiSpsId = 0;
            }

            WelsGenerateNewSps(pCtx, kbUseSubsetSps, iDlayerIndex,
                               iDlayerCount, (int32_t)kuiSpsId, ref pSps, ref pSubsetSps, bSvcBaselayer);
        }
        return kuiSpsId;
    }

    private void CWelsParametersetSpsListing_UpdateParaSetNum(sWelsEncCtx* pCtx)
    {
        pCtx->iSpsNum = (int32_t)m_sParaSetOffset.uiInUseSpsNum;
        pCtx->iSubsetSpsNum = (int32_t)m_sParaSetOffset.uiInUseSubsetSpsNum;
    }

    private void CWelsParametersetSpsListing_OutputCurrentStructure(SParaSetOffsetVariable* pParaSetOffsetVariable,
        int32_t* pPpsIdList, sWelsEncCtx* pCtx, SExistingParasetList* pExistingParasetList)
    {
        CWelsParametersetIdNonConstant_OutputCurrentStructure(pParaSetOffsetVariable, pPpsIdList, pCtx, pExistingParasetList);
        pExistingParasetList->uiInUseSpsNum = m_sParaSetOffset.uiInUseSpsNum;

        memcpy(&pExistingParasetList->sSps, pCtx->pSpsArray, MAX_SPS_COUNT * sizeof(SWelsSPS));
        if (null != pCtx->pSubsetArray)
        {
            pExistingParasetList->uiInUseSubsetSpsNum = m_sParaSetOffset.uiInUseSubsetSpsNum;
            memcpy(&pExistingParasetList->sSubsetSps, pCtx->pSubsetArray, MAX_SPS_COUNT * sizeof(SSubsetSps));
        }
        else
        {
            pExistingParasetList->uiInUseSubsetSpsNum = 0;
        }
    }

    // ---- CWelsParametersetSpsPpsListing ----

    private void CWelsParametersetSpsPpsListing_LoadPreviousPps(SExistingParasetList* pExistingParasetList, SWelsPPS* pPpsArray)
    {
        // copy from existing if the pointer exists
        //if ((SPS_PPS_LISTING == pParam->eSpsPpsIdStrategy) && (NULL != pExistingParasetList)) {
        m_sParaSetOffset.uiInUsePpsNum = pExistingParasetList->uiInUsePpsNum;
        memcpy(pPpsArray, &pExistingParasetList->sPps, WelsEnc.MAX_PPS_COUNT * sizeof(SWelsPPS));
        //}
    }

    /*  if ((SPS_PPS_LISTING == pCtx->pSvcParam->eSpsPpsIdStrategy) && (pCtx->iPpsNum < MAX_PPS_COUNT)) {
     UpdatePpsList (pCtx);
     }*/
    private void CWelsParametersetSpsPpsListing_UpdatePpsList(sWelsEncCtx* pCtx)
    {
        if (pCtx->iPpsNum >= WelsEnc.MAX_PPS_COUNT)
        {
            return;
        }
        Debug.Assert(pCtx->iPpsNum <= WelsEnc.MAX_DQ_LAYER_NUM);

        //Generate PPS LIST
        int32_t iPpsId = 0, iUsePpsNum = pCtx->iPpsNum;

        for (int32_t iIdrRound = 0; iIdrRound < WelsEnc.MAX_PPS_COUNT; iIdrRound++)
        {
            for (iPpsId = 0; iPpsId < pCtx->iPpsNum; iPpsId++)
            {
                m_sParaSetOffset.iPpsIdList[iPpsId][iIdrRound] = ((iIdrRound * iUsePpsNum + iPpsId) % WelsEnc.MAX_PPS_COUNT);
            }
        }

        for (iPpsId = iUsePpsNum; iPpsId < WelsEnc.MAX_PPS_COUNT; iPpsId++)
        {
            memcpy(&(pCtx->pPPSArray[iPpsId]), &(pCtx->pPPSArray[iPpsId % iUsePpsNum]), sizeof(SWelsPPS));
            pCtx->pPPSArray[iPpsId].iPpsId = (uint32_t)iPpsId;
            pCtx->iPpsNum++;
        }

        Debug.Assert(pCtx->iPpsNum == WelsEnc.MAX_PPS_COUNT);
        m_sParaSetOffset.uiInUsePpsNum = (uint32_t)pCtx->iPpsNum;
    }

    private bool CWelsParametersetSpsPpsListing_CheckPpsGenerating()
    {
        /*if ((SPS_PPS_LISTING == pCtx->pSvcParam->eSpsPpsIdStrategy) && (MAX_PPS_COUNT <= pCtx->sPSOVector.uiInUsePpsNum)) {
          //check if we can generate new SPS or not
          WelsLog (& pCtx->sLogCtx, WELS_LOG_ERROR,
                   "InitDqLayers(), cannot generate new SPS under the SPS_PPS_LISTING mode!");
          return ENC_RETURN_UNSUPPORTED_PARA;
        }*/
        if (WelsEnc.MAX_PPS_COUNT <= m_sParaSetOffset.uiInUsePpsNum)
        {
            return false;
        }

        return true;
    }

    private int32_t CWelsParametersetSpsPpsListing_SpsReset(sWelsEncCtx* pCtx, bool kbUseSubsetSps)
    {
        /*        if (SPS_PPS_LISTING == pParam->eSpsPpsIdStrategy) {
         WelsLog (& (*ppCtx)->sLogCtx, WELS_LOG_ERROR,
         "InitDqLayers(), cannot generate new SPS under the SPS_PPS_LISTING mode!");
         return ENC_RETURN_UNSUPPORTED_PARA;
         }*/
        return -1;
    }

    public static int32_t FindExistingPps(SWelsSPS* pSps, SSubsetSps* pSubsetSps, bool kbUseSubsetSps, int32_t iSpsId,
                                          bool kbEntropyCodingFlag, int32_t iPpsNumInUse,
                                          SWelsPPS* pPpsArray)
    {
        // #if !defined(DISABLE_FMO_FEATURE)
        // feature not supported yet
        return WelsEnc.INVALID_ID;
        // #endif//!DISABLE_FMO_FEATURE
        // (the PPS matching that follows upstream is unreachable in this configuration)
    }

    private uint32_t CWelsParametersetSpsPpsListing_InitPps(sWelsEncCtx* pCtx, uint32_t kiSpsId,
        SWelsSPS* pSps,
        SSubsetSps* pSubsetSps,
        uint32_t kuiPpsId,
        bool kbDeblockingFilterPresentFlag,
        bool kbUsingSubsetSps,
        bool kbEntropyCodingModeFlag)
    {
        int32_t kiFoundPpsId = FindExistingPps(pSps, pSubsetSps, kbUsingSubsetSps, (int32_t)kiSpsId,
                                   kbEntropyCodingModeFlag,
                                   (int32_t)m_sParaSetOffset.uiInUsePpsNum,
                                   pCtx->pPPSArray);

        if (WelsEnc.INVALID_ID != kiFoundPpsId)
        {
            //if yes, set pPps to it
            kuiPpsId = (uint32_t)kiFoundPpsId;
        }
        else
        {
            kuiPpsId = (m_sParaSetOffset.uiInUsePpsNum++);
            WelsEnc.WelsInitPps(&pCtx->pPPSArray[kuiPpsId], pSps, pSubsetSps, kuiPpsId, true, kbUsingSubsetSps, kbEntropyCodingModeFlag);
        }
        SetUseSubsetFlag(kuiPpsId, kbUsingSubsetSps);
        return kuiPpsId;
    }

    private void CWelsParametersetSpsPpsListing_UpdateParaSetNum(sWelsEncCtx* pCtx)
    {
        CWelsParametersetSpsListing_UpdateParaSetNum(pCtx);

        //UpdatePpsList (pCtx);
        pCtx->iPpsNum = (int32_t)m_sParaSetOffset.uiInUsePpsNum;
    }

    private int32_t CWelsParametersetSpsPpsListing_GetCurrentPpsId(int32_t iPpsId, int32_t iIdrLoop)
    {
        return m_sParaSetOffset.iPpsIdList[iPpsId][iIdrLoop];
    }

    private void CWelsParametersetSpsPpsListing_LoadPreviousStructure(SParaSetOffsetVariable* pParaSetOffsetVariable,
        int32_t* pPpsIdList)
    {
        fixed (SParaSetOffsetVariable* pDst = &m_sParaSetOffset.sParaSetOffsetVariable[0])
        {
            memcpy(pDst, pParaSetOffsetVariable,
                   (WelsEnc.PARA_SET_TYPE) * sizeof(SParaSetOffsetVariable)); // confirmed_safe_unsafe_usage
        }

        fixed (InlineArray57<int32_t>* pList = &m_sParaSetOffset.iPpsIdList[0])
        {
            memcpy(pList, pPpsIdList, WelsEnc.MAX_DQ_LAYER_NUM * WelsEnc.MAX_PPS_COUNT * sizeof(int32_t));
        }
    }

    private void CWelsParametersetSpsPpsListing_OutputCurrentStructure(SParaSetOffsetVariable* pParaSetOffsetVariable,
        int32_t* pPpsIdList, sWelsEncCtx* pCtx, SExistingParasetList* pExistingParasetList)
    {
        CWelsParametersetSpsListing_OutputCurrentStructure(pParaSetOffsetVariable, pPpsIdList, pCtx, pExistingParasetList);

        pExistingParasetList->uiInUsePpsNum = m_sParaSetOffset.uiInUsePpsNum;
        // upstream copies from pCtx->pPps (the current PPS), not pCtx->pPPSArray
        memcpy(&pExistingParasetList->sPps, pCtx->pPps, WelsEnc.MAX_PPS_COUNT * sizeof(SWelsPPS));
        fixed (InlineArray57<int32_t>* pList = &m_sParaSetOffset.iPpsIdList[0])
        {
            memcpy(pPpsIdList, pList, WelsEnc.MAX_DQ_LAYER_NUM * WelsEnc.MAX_PPS_COUNT * sizeof(int32_t));
        }
    }

    // ---- CWelsParametersetSpsListingPpsIncreasing ----

    private int32_t CWelsParametersetSpsListingPpsIncreasing_GetPpsIdOffset(int32_t kiPpsId)
    {
        //same as CWelsParametersetIdIncreasing::GetPpsIdOffset
        return (m_sParaSetOffset.sParaSetOffsetVariable[WelsEnc.PARA_SET_TYPE_PPS].iParaSetIdDelta[kiPpsId]);
    }

    private void CWelsParametersetSpsListingPpsIncreasing_Update(uint32_t kuiId, int iParasetType)
    {
        //same as CWelsParametersetIdIncreasing::Update
        fixed (SParaSetOffsetVariable* pVar = &m_sParaSetOffset.sParaSetOffsetVariable[iParasetType])
        {
            ParasetIdAdditionIdAdjust(pVar,
                                      (int32_t)kuiId,
                                      (uint32_t)((iParasetType != WelsEnc.PARA_SET_TYPE_PPS) ? MAX_SPS_COUNT : WelsEnc.MAX_PPS_COUNT));
        }
    }
}
