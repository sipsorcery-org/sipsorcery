//-----------------------------------------------------------------------------
// Filename: encoder_ext.cs
//
// Description: Port of codec/encoder/core/src/encoder_ext.cpp, part 1 (E1):
// parameter validation (ParamValidation, ParamValidationExt and their
// helpers), the slice and thread settings applied at init
// (InitSliceSettings, GetMultipleThreadIdc) and the SPS/PPS writers
// (WelsWriteOneSPS/PPS, WelsWriteParameterSets,
// WelsEncoderEncodeParameterSets, WriteSsvcParaset, WriteSavcParaset,
// WriteSavcParaset_Listing) (E1), and AllocStrideTables, SetFastCodingFunc
// and SetNormalCodingFunc (E4). Then (E4b) the layer and
// macroblock list set up (InitMbInfo, InitMbListD, InitDqLayers and the
// FreeDqLayer/FreeRefList they fail through), GetTemporalLevel,
// WelsInitCurrentLayer, PreprocessSliceCoding (I and, from E5, P slices;
// screen content throws until Phase 4), WelsSwapDqLayers and
// PrefetchReferencePicture, and (E5b) GetMvMvdRange.
// RequestMemorySvc, WelsInitEncoderExt and WelsEncoderEncodeExt come with E8.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The reference is the C-only build, where WelsCPUFeatureDetect returns 0
// and leaves the core count at 0, so the core count always comes from
// DynamicDetectCpuCores. That is Environment.ProcessorCount here; upstream
// on Windows it is GetSystemInfo's dwNumberOfProcessors, which can differ
// when the process affinity is restricted. Only iMultipleThreadIdc == 0 and
// SM_FIXEDSLCNUM_SLICE with uiSliceNum == 0 read it.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (E1).
// 26 Nov 2026  Claude          AllocStrideTables and the fast/normal coding setters (E4).
// 26 Nov 2026  Claude          The layer set up, WelsInitCurrentLayer and PreprocessSliceCoding (E4b).
// 26 Nov 2026  Claude          The P-slice pointers in PreprocessSliceCoding (E5).
// 26 Nov 2026  Claude          GetMvMvdRange (E5b).
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
    /// <summary>
    /// WelsThreadLib's DynamicDetectCpuCores (see the file header).
    /// </summary>
    public static int32_t DynamicDetectCpuCores() => Environment.ProcessorCount;

    public static int32_t WelsBitRateVerification(SLogContext* pLogCtx, SSpatialLayerConfig* pLayerParam, int32_t iLayerId)
    {
        if ((pLayerParam->iSpatialBitrate <= 0)
            || ((float)(pLayerParam->iSpatialBitrate) < pLayerParam->fFrameRate))
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"Invalid bitrate settings in layer {iLayerId}, bitrate= {pLayerParam->iSpatialBitrate} at FrameRate({pLayerParam->fFrameRate:F6})");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }

        // deal with LEVEL_MAX_BR and MAX_BR setting
        SLevelLimits* pCurLevelLimit = g_ksLevelLimits;
        while ((pCurLevelLimit->uiLevelIdc != LEVEL_5_2) && (pCurLevelLimit->uiLevelIdc != pLayerParam->uiLevelIdc))
            pCurLevelLimit++;
        int32_t iLevelMaxBitrate = unchecked((int32_t)(pCurLevelLimit->uiMaxBR * (uint32_t)CpbBrNalFactor));
        int32_t iLevel52MaxBitrate = unchecked((int32_t)(g_ksLevelLimits[LEVEL_NUMBER - 1].uiMaxBR * (uint32_t)CpbBrNalFactor));
        if (UNSPECIFIED_BIT_RATE != iLevelMaxBitrate)
        {
            if ((pLayerParam->iMaxSpatialBitrate == UNSPECIFIED_BIT_RATE)
                || (pLayerParam->iMaxSpatialBitrate > iLevel52MaxBitrate))
            {
                pLayerParam->iMaxSpatialBitrate = iLevelMaxBitrate;
                WelsLog(pLogCtx, WELS_LOG_INFO,
                        $"Current MaxSpatialBitrate is invalid (UNSPECIFIED_BIT_RATE or larger than LEVEL5_2) but level setting is valid, set iMaxSpatialBitrate to {pLayerParam->iMaxSpatialBitrate} from level ({(int)pLayerParam->uiLevelIdc})");
            }
            else if (pLayerParam->iMaxSpatialBitrate > iLevelMaxBitrate)
            {
                ELevelIdc iCurLevel = pLayerParam->uiLevelIdc;
                WelsAdjustLevel(pLayerParam, pCurLevelLimit);
                WelsLog(pLogCtx, WELS_LOG_INFO,
                        $"LevelIdc is changed from ({(int)iCurLevel}) to ({(int)pLayerParam->uiLevelIdc}) according to the iMaxSpatialBitrate({pLayerParam->iMaxSpatialBitrate})");
            }
        }
        else if ((pLayerParam->iMaxSpatialBitrate != UNSPECIFIED_BIT_RATE)
                 && (pLayerParam->iMaxSpatialBitrate > iLevel52MaxBitrate))
        {
            // no level limitation, just need to check if iMaxSpatialBitrate is too big from reasonable
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $"No LevelIdc setting and iMaxSpatialBitrate ({pLayerParam->iMaxSpatialBitrate}) is considered too big to be valid, changed to UNSPECIFIED_BIT_RATE");
            pLayerParam->iMaxSpatialBitrate = UNSPECIFIED_BIT_RATE;
        }

        // deal with iSpatialBitrate and iMaxSpatialBitrate setting
        if (pLayerParam->iMaxSpatialBitrate != UNSPECIFIED_BIT_RATE)
        {
            if (pLayerParam->iMaxSpatialBitrate == pLayerParam->iSpatialBitrate)
            {
                WelsLog(pLogCtx, WELS_LOG_INFO,
                        $"Setting MaxSpatialBitrate ({pLayerParam->iMaxSpatialBitrate}) the same at SpatialBitrate ({pLayerParam->iSpatialBitrate}) will make the actual bit rate lower than SpatialBitrate");
            }
            else if (pLayerParam->iMaxSpatialBitrate < pLayerParam->iSpatialBitrate)
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR,
                        $"MaxSpatialBitrate ({pLayerParam->iMaxSpatialBitrate}) should be larger than SpatialBitrate ({pLayerParam->iSpatialBitrate}), considering it as error setting");
                return ENC_RETURN_UNSUPPORTED_PARA;
            }
        }
        return ENC_RETURN_SUCCESS;
    }

    public static void CheckProfileSetting(SLogContext* pLogCtx, SWelsSvcCodingParam* pParam, int32_t iLayer, EProfileIdc uiProfileIdc)
    {
        SSpatialLayerConfig* pLayerInfo = (SSpatialLayerConfig*)&pParam->sSpatialLayers + iLayer;
        pLayerInfo->uiProfileIdc = uiProfileIdc;
        if (pParam->bSimulcastAVC)
        {
            if ((uiProfileIdc != PRO_BASELINE) && (uiProfileIdc != PRO_MAIN) && (uiProfileIdc != PRO_HIGH))
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, $"layerId({iLayer}) doesn't support profile({(int)uiProfileIdc}), change to UNSPECIFIC profile");
                pLayerInfo->uiProfileIdc = PRO_UNKNOWN;
            }
        }
        else
        {
            if (iLayer == (int32_t)SPATIAL_LAYER_0)
            {
                if ((uiProfileIdc != PRO_BASELINE) && (uiProfileIdc != PRO_MAIN) && (uiProfileIdc != PRO_HIGH))
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING, $"layerId({iLayer}) doesn't support profile({(int)uiProfileIdc}), change to UNSPECIFIC profile");
                    pLayerInfo->uiProfileIdc = PRO_UNKNOWN;
                }
            }
            else
            {
                if ((uiProfileIdc != PRO_SCALABLE_BASELINE) && (uiProfileIdc != PRO_SCALABLE_HIGH))
                {
                    pLayerInfo->uiProfileIdc = PRO_SCALABLE_BASELINE;
                    WelsLog(pLogCtx, WELS_LOG_WARNING, $"layerId({iLayer}) doesn't support profile({(int)uiProfileIdc}), change to scalable baseline profile");
                }
            }
        }
    }

    public static void CheckLevelSetting(SLogContext* pLogCtx, SWelsSvcCodingParam* pParam, int32_t iLayer, ELevelIdc uiLevelIdc)
    {
        SSpatialLayerConfig* pLayerInfo = (SSpatialLayerConfig*)&pParam->sSpatialLayers + iLayer;
        pLayerInfo->uiLevelIdc = LEVEL_UNKNOWN;
        int32_t iLevelIdx = LEVEL_NUMBER - 1;
        do
        {
            if (g_ksLevelLimits[iLevelIdx].uiLevelIdc == uiLevelIdc)
            {
                pLayerInfo->uiLevelIdc = uiLevelIdc;
                break;
            }
            iLevelIdx--;
        } while (iLevelIdx >= 0);
    }

    public static void CheckReferenceNumSetting(SLogContext* pLogCtx, SWelsSvcCodingParam* pParam, int32_t iNumRef)
    {
        int32_t iRefUpperBound = (pParam->iUsageType == CAMERA_VIDEO_REAL_TIME) ?
                                 MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA : MAX_REFERENCE_PICTURE_COUNT_NUM_SCREEN;
        pParam->iNumRefFrame = iNumRef;
        if ((iNumRef < MIN_REF_PIC_COUNT) || (iNumRef > iRefUpperBound))
        {
            pParam->iNumRefFrame = AUTO_REF_PIC_COUNT;
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $"doesn't support the number of reference frame({iNumRef}) change to auto select mode");
        }
    }

    public static int32_t SliceArgumentValidationFixedSliceMode(SLogContext* pLogCtx,
        SSliceArgument* pSliceArgument, RC_MODES kiRCMode,
        int32_t kiPicWidth, int32_t kiPicHeight)
    {
        int32_t iCpuCores = 0;
        int32_t iIdx = 0;
        int32_t iMbWidth = (kiPicWidth + 15) >> 4;
        int32_t iMbHeight = (kiPicHeight + 15) >> 4;
        int32_t iMbNumInFrame = iMbWidth * iMbHeight;
        bool bSingleMode = false;

        pSliceArgument->uiSliceSizeConstraint = 0;

        if (pSliceArgument->uiSliceNum == 0)
        {
            // WelsCPUFeatureDetect (&iCpuCores): C-only build, leaves iCpuCores at 0
            if (0 == iCpuCores)
            {
                // cpuid not supported or doesn't expose the number of cores,
                // use high level system API as followed to detect number of pysical/logic processor
                iCpuCores = DynamicDetectCpuCores();
            }
            pSliceArgument->uiSliceNum = (uint32_t)iCpuCores;
        }

        if (pSliceArgument->uiSliceNum <= 1)
        {
            WelsLog(pLogCtx, WELS_LOG_INFO,
                    $"SliceArgumentValidationFixedSliceMode(), uiSliceNum({pSliceArgument->uiSliceNum}) you set for SM_FIXEDSLCNUM_SLICE, now turn to SM_SINGLE_SLICE type!");
            bSingleMode = true;
        }

        // considering the coding efficient and performance,
        // iCountMbNum constraint by MIN_NUM_MB_PER_SLICE condition of multi-pSlice mode settting
        if (iMbNumInFrame <= MIN_NUM_MB_PER_SLICE)
        {
            WelsLog(pLogCtx, WELS_LOG_INFO,
                    $"SliceArgumentValidationFixedSliceMode(), uiSliceNum({pSliceArgument->uiSliceNum}) you set for SM_FIXEDSLCNUM_SLICE, now turn to SM_SINGLE_SLICE type as CountMbNum less than MIN_NUM_MB_PER_SLICE!");
            bSingleMode = true;
        }

        if (bSingleMode)
        {
            pSliceArgument->uiSliceMode = SM_SINGLE_SLICE;
            pSliceArgument->uiSliceNum = 1;
            for (iIdx = 0; iIdx < MAX_SLICES_NUM; iIdx++)
            {
                pSliceArgument->uiSliceMbNum[iIdx] = 0;
            }
            return ENC_RETURN_SUCCESS;
        }

        if (pSliceArgument->uiSliceNum > MAX_SLICES_NUM)
        {
            pSliceArgument->uiSliceNum = MAX_SLICES_NUM;
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $"SliceArgumentValidationFixedSliceMode(), uiSliceNum exceed MAX_SLICES_NUM! So setting slice num eqaul to MAX_SLICES_NUM({pSliceArgument->uiSliceNum})!");
        }

        if (kiRCMode != RC_OFF_MODE)
        { // multiple slices verify with gom
            //check uiSliceNum and set uiSliceMbNum with current uiSliceNum
            if (!GomValidCheckSliceNum(iMbWidth, iMbHeight, &pSliceArgument->uiSliceNum))
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"SliceArgumentValidationFixedSliceMode(), unsupported setting with Resolution and uiSliceNum combination under RC on! So uiSliceNum is changed to {pSliceArgument->uiSliceNum}!");
            }

            if (pSliceArgument->uiSliceNum <= 1 ||
                !GomValidCheckSliceMbNum(iMbWidth, iMbHeight, pSliceArgument))
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR,
                        $"SliceArgumentValidationFixedSliceMode(), unsupported setting with Resolution and uiSliceNum ({pSliceArgument->uiSliceNum}) combination  under RC on! Consider setting single slice with this resolution!");
                return ENC_RETURN_UNSUPPORTED_PARA;
            }
        }
        else if (!CheckFixedSliceNumMultiSliceSetting(iMbNumInFrame, pSliceArgument))
        {
            //check uiSliceMbNum with current uiSliceNum
            WelsLog(pLogCtx, WELS_LOG_ERROR,
                    $"SliceArgumentValidationFixedSliceMode(), invalid uiSliceMbNum ({pSliceArgument->uiSliceMbNum[0]}) settings!,now turn to SM_SINGLE_SLICE type");
            pSliceArgument->uiSliceMode = SM_SINGLE_SLICE;
            pSliceArgument->uiSliceNum = 1;
            for (iIdx = 0; iIdx < MAX_SLICES_NUM; iIdx++)
            {
                pSliceArgument->uiSliceMbNum[iIdx] = 0;
            }
        }

        return ENC_RETURN_SUCCESS;
    }

    /*!
     * \brief   validate checking in parameter configuration
     * \pParam  pParam      SWelsSvcCodingParam*
     * \return  successful - 0; otherwise none 0 for failed
     */
    public static int32_t ParamValidation(SLogContext* pLogCtx, SWelsSvcCodingParam* pCfg)
    {
        const float fEpsn = 0.000001f;
        int32_t i = 0;

        Debug.Assert(pCfg != null);

        if (!(pCfg->iUsageType < INPUT_CONTENT_TYPE_ALL))
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidation(),Invalid usage type = {(int)pCfg->iUsageType}");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }
        if (pCfg->iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            if (pCfg->iSpatialLayerNum > 1)
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidation(),Invalid the number of Spatial layer({pCfg->iSpatialLayerNum})for screen content");
                return ENC_RETURN_UNSUPPORTED_PARA;
            }
            if (pCfg->bEnableAdaptiveQuant)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"ParamValidation(), AdaptiveQuant({(pCfg->bEnableAdaptiveQuant ? 1 : 0)}) is not supported yet for screen content, auto turned off");
                pCfg->bEnableAdaptiveQuant = false;
            }
            if (pCfg->bEnableBackgroundDetection)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"ParamValidation(), BackgroundDetection({(pCfg->bEnableBackgroundDetection ? 1 : 0)}) is not supported yet for screen content, auto turned off");
                pCfg->bEnableBackgroundDetection = false;
            }
            if (pCfg->bEnableSceneChangeDetect == false)
            {
                pCfg->bEnableSceneChangeDetect = true;
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"ParamValidation(), screen change detection should be turned on, change bEnableSceneChangeDetect as true");
            }
        }

        //turn off adaptive quant now, algorithms needs to be refactored
        pCfg->bEnableAdaptiveQuant = false;

        if (pCfg->iSpatialLayerNum > 1)
        {
            for (i = pCfg->iSpatialLayerNum - 1; i > 0; i--)
            {
                SSpatialLayerConfig* fDlpUp = (SSpatialLayerConfig*)&pCfg->sSpatialLayers + i;
                SSpatialLayerConfig* fDlp = (SSpatialLayerConfig*)&pCfg->sSpatialLayers + (i - 1);
                if ((fDlp->iVideoWidth > fDlpUp->iVideoWidth) || (fDlp->iVideoHeight > fDlpUp->iVideoHeight))
                {
                    WelsLog(pLogCtx, WELS_LOG_ERROR,
                            $"ParamValidation,Invalid resolution layer({i}) resolution({fDlp->iVideoWidth} x {fDlp->iVideoHeight}) should be less than the upper spatial layer resolution({fDlpUp->iVideoWidth} x {fDlpUp->iVideoHeight}) ");
                    return ENC_RETURN_UNSUPPORTED_PARA;
                }
            }
        }

        // CheckInRangeCloseOpen takes int16_t, so the int32_t settings are truncated as in C.
        if (!CheckInRangeCloseOpen((int16_t)pCfg->iLoopFilterDisableIdc, DEBLOCKING_IDC_0, DEBLOCKING_IDC_2 + 1) ||
            !CheckInRangeCloseOpen((int16_t)pCfg->iLoopFilterAlphaC0Offset, DEBLOCKING_OFFSET_MINUS, DEBLOCKING_OFFSET + 1) ||
            !CheckInRangeCloseOpen((int16_t)pCfg->iLoopFilterBetaOffset, DEBLOCKING_OFFSET_MINUS, DEBLOCKING_OFFSET + 1))
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR,
                    $"ParamValidation, Invalid iLoopFilterDisableIdc({pCfg->iLoopFilterDisableIdc}) or iLoopFilterAlphaC0Offset({pCfg->iLoopFilterAlphaC0Offset}) or iLoopFilterBetaOffset({pCfg->iLoopFilterBetaOffset})!");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }

        for (i = 0; i < pCfg->iSpatialLayerNum; ++i)
        {
            SSpatialLayerInternal* fDlp = (SSpatialLayerInternal*)&pCfg->sDependencyLayers + i;
            SSpatialLayerConfig* pConfig = (SSpatialLayerConfig*)&pCfg->sSpatialLayers + i;
            if (fDlp->fOutputFrameRate > fDlp->fInputFrameRate || (fDlp->fInputFrameRate >= -fEpsn
                && fDlp->fInputFrameRate <= fEpsn)
                || (fDlp->fOutputFrameRate >= -fEpsn && fDlp->fOutputFrameRate <= fEpsn))
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR,
                        $"Invalid settings in input frame rate({fDlp->fInputFrameRate:F6}) or output frame rate({fDlp->fOutputFrameRate:F6}) of layer #{i} config file..");
                return ENC_RETURN_INVALIDINPUT;
            }
            if (uint.MaxValue == GetLogFactor(fDlp->fOutputFrameRate, fDlp->fInputFrameRate))
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"AUTO CORRECT: Invalid settings in input frame rate({fDlp->fInputFrameRate:F6}) and output frame rate({fDlp->fOutputFrameRate:F6}) of layer #{i} config file: iResult of output frame rate divided by input frame rate should be power of 2(i.e,in/pOut=2^n). \n Auto correcting Output Framerate to Input Framerate {fDlp->fInputFrameRate:F6}!\n");
                fDlp->fOutputFrameRate = fDlp->fInputFrameRate;
                pConfig->fFrameRate = fDlp->fOutputFrameRate;
            }
        }

        if ((pCfg->iRCMode != RC_OFF_MODE) && (pCfg->iRCMode != RC_QUALITY_MODE) && (pCfg->iRCMode != RC_BUFFERBASED_MODE)
            && (pCfg->iRCMode != RC_BITRATE_MODE) && (pCfg->iRCMode != RC_TIMESTAMP_MODE))
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidation(),Invalid iRCMode = {(int)pCfg->iRCMode}");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }
        //bitrate setting validation
        if (pCfg->iRCMode != RC_OFF_MODE)
        {
            int32_t iTotalBitrate = 0;
            if (pCfg->iTargetBitrate <= 0)
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR, $"Invalid bitrate settings in total configure, bitrate= {pCfg->iTargetBitrate}");
                return ENC_RETURN_INVALIDINPUT;
            }
            for (i = 0; i < pCfg->iSpatialLayerNum; ++i)
            {
                SSpatialLayerConfig* pSpatialLayer = (SSpatialLayerConfig*)&pCfg->sSpatialLayers + i;
                iTotalBitrate += pSpatialLayer->iSpatialBitrate;

                if (WelsBitRateVerification(pLogCtx, pSpatialLayer, i) != ENC_RETURN_SUCCESS)
                    return ENC_RETURN_INVALIDINPUT;
            }
            if (iTotalBitrate > pCfg->iTargetBitrate)
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR,
                        $"Invalid settings in bitrate. the sum of each layer bitrate({iTotalBitrate}) is larger than total bitrate setting({pCfg->iTargetBitrate})");
                return ENC_RETURN_INVALIDINPUT;
            }
            if ((pCfg->iRCMode == RC_QUALITY_MODE) || (pCfg->iRCMode == RC_BITRATE_MODE) || (pCfg->iRCMode == RC_TIMESTAMP_MODE))
                if (!pCfg->bEnableFrameSkip)
                    WelsLog(pLogCtx, WELS_LOG_WARNING,
                            $"bEnableFrameSkip = {(pCfg->bEnableFrameSkip ? 1 : 0)},bitrate can't be controlled for RC_QUALITY_MODE,RC_BITRATE_MODE and RC_TIMESTAMP_MODE without enabling skip frame.");
            if ((pCfg->iMaxQp <= 0) || (pCfg->iMinQp <= 0))
            {
                if (pCfg->iUsageType == SCREEN_CONTENT_REAL_TIME)
                {
                    WelsLog(pLogCtx, WELS_LOG_INFO, $"Change QP Range from({pCfg->iMinQp},{pCfg->iMaxQp}) to ({MIN_SCREEN_QP},{MAX_SCREEN_QP})");
                    pCfg->iMinQp = MIN_SCREEN_QP;
                    pCfg->iMaxQp = MAX_SCREEN_QP;
                }
                else
                {
                    WelsLog(pLogCtx, WELS_LOG_INFO, $"Change QP Range from({pCfg->iMinQp},{pCfg->iMaxQp}) to ({GOM_MIN_QP_MODE},{MAX_LOW_BR_QP})");
                    pCfg->iMinQp = GOM_MIN_QP_MODE;
                    pCfg->iMaxQp = MAX_LOW_BR_QP;
                }
            }
            pCfg->iMinQp = WELS_CLIP3(pCfg->iMinQp, GOM_MIN_QP_MODE, QP_MAX_VALUE);
            pCfg->iMaxQp = WELS_CLIP3(pCfg->iMaxQp, pCfg->iMinQp, QP_MAX_VALUE);
        }
        // ref-frames validation
        if (((pCfg->iUsageType == CAMERA_VIDEO_REAL_TIME) || (pCfg->iUsageType == SCREEN_CONTENT_REAL_TIME))
            ? WelsCheckRefFrameLimitationNumRefFirst(pLogCtx, pCfg) != 0
            : WelsCheckRefFrameLimitationLevelIdcFirst(pLogCtx, pCfg) != 0)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"WelsCheckRefFrameLimitation failed");
            return ENC_RETURN_INVALIDINPUT;
        }
        return ENC_RETURN_SUCCESS;
    }

    public static int32_t ParamValidationExt(SLogContext* pLogCtx, SWelsSvcCodingParam* pCodingParam)
    {
        int8_t i = 0;
        int32_t iIdx = 0;

        Debug.Assert(pCodingParam != null);
        if (null == pCodingParam)
            return ENC_RETURN_INVALIDINPUT;

        if ((pCodingParam->iUsageType != CAMERA_VIDEO_REAL_TIME) && (pCodingParam->iUsageType != SCREEN_CONTENT_REAL_TIME))
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(),Invalid usage type = {(int)pCodingParam->iUsageType}");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }
        if ((pCodingParam->iUsageType == SCREEN_CONTENT_REAL_TIME) && (!pCodingParam->bIsLosslessLink
            && pCodingParam->bEnableLongTermReference))
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $"ParamValidationExt(), setting lossy link for LTR under screen, which is not supported yet! Auto disabled LTR!");
            pCodingParam->bEnableLongTermReference = false;
        }
        if (pCodingParam->iSpatialLayerNum < 1 || pCodingParam->iSpatialLayerNum > MAX_DEPENDENCY_LAYER)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), monitor invalid pCodingParam->iSpatialLayerNum: {pCodingParam->iSpatialLayerNum}!");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }

        if (pCodingParam->iTemporalLayerNum < 1 || pCodingParam->iTemporalLayerNum > MAX_TEMPORAL_LEVEL)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), monitor invalid pCodingParam->iTemporalLayerNum: {pCodingParam->iTemporalLayerNum}!");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }

        if (pCodingParam->uiGopSize < 1 || pCodingParam->uiGopSize > MAX_GOP_SIZE)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), monitor invalid pCodingParam->uiGopSize: {pCodingParam->uiGopSize}!");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }

        if (pCodingParam->uiIntraPeriod != 0 && pCodingParam->uiIntraPeriod < pCodingParam->uiGopSize)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR,
                    $"ParamValidationExt(), uiIntraPeriod({pCodingParam->uiIntraPeriod}) should be not less than that of uiGopSize({pCodingParam->uiGopSize}) or -1 specified!");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }

        if (pCodingParam->uiIntraPeriod != 0 && (pCodingParam->uiIntraPeriod & (pCodingParam->uiGopSize - 1)) != 0)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR,
                    $"ParamValidationExt(), uiIntraPeriod({pCodingParam->uiIntraPeriod}) should be multiple of uiGopSize({pCodingParam->uiGopSize}) or -1 specified!");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }

        //about iMultipleThreadIdc, bDeblockingParallelFlag, iLoopFilterDisableIdc, & uiSliceMode
        // (1) Single Thread
        //    if (THREAD==1)//single thread
        //            no parallel_deblocking: bDeblockingParallelFlag = 0;
        // (2) Multi Thread: see uiSliceMode decision
        if (pCodingParam->iMultipleThreadIdc == 1)
        {
            //now is single thread. no parallel deblocking, set flag=0
            pCodingParam->bDeblockingParallelFlag = false;
        }
        else
        {
            pCodingParam->bDeblockingParallelFlag = true;
        }

        // eSpsPpsIdStrategy checkings
        if (pCodingParam->iSpatialLayerNum > 1 && (!pCodingParam->bSimulcastAVC)
            && (((int)SPS_LISTING & (int)pCodingParam->eSpsPpsIdStrategy) != 0))
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $"ParamValidationExt(), eSpsPpsIdStrategy setting ({(int)pCodingParam->eSpsPpsIdStrategy}) with multiple svc SpatialLayers ({pCodingParam->iSpatialLayerNum}) not supported! eSpsPpsIdStrategy adjusted to CONSTANT_ID");
            pCodingParam->eSpsPpsIdStrategy = CONSTANT_ID;
        }
        if (pCodingParam->iUsageType == SCREEN_CONTENT_REAL_TIME && (((int)SPS_LISTING & (int)pCodingParam->eSpsPpsIdStrategy) != 0))
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $"ParamValidationExt(), eSpsPpsIdStrategy setting ({(int)pCodingParam->eSpsPpsIdStrategy}) with iUsageType ({(int)pCodingParam->iUsageType}) not supported! eSpsPpsIdStrategy adjusted to CONSTANT_ID");
            pCodingParam->eSpsPpsIdStrategy = CONSTANT_ID;
        }

        if (pCodingParam->bSimulcastAVC && (((int)SPS_LISTING & (int)pCodingParam->eSpsPpsIdStrategy) != 0))
        {
            WelsLog(pLogCtx, WELS_LOG_INFO,
                    $"ParamValidationExt(), eSpsPpsIdStrategy({(int)pCodingParam->eSpsPpsIdStrategy}) under bSimulcastAVC({(pCodingParam->bSimulcastAVC ? 1 : 0)}) not supported yet, adjusted to INCREASING_ID");
            pCodingParam->eSpsPpsIdStrategy = INCREASING_ID;
        }

        if (pCodingParam->bSimulcastAVC && pCodingParam->bPrefixNalAddingCtrl)
        {
            // upstream passes eSpsPpsIdStrategy and bSimulcastAVC to this message
            WelsLog(pLogCtx, WELS_LOG_INFO,
                    $"ParamValidationExt(), bSimulcastAVC({(int)pCodingParam->eSpsPpsIdStrategy}) is not compatible with bPrefixNalAddingCtrl({(pCodingParam->bSimulcastAVC ? 1 : 0)}) true, adjusted bPrefixNalAddingCtrl to false");
            pCodingParam->bPrefixNalAddingCtrl = false;
        }

        for (i = 0; i < pCodingParam->iSpatialLayerNum; ++i)
        {
            SSpatialLayerConfig* pSpatialLayer = (SSpatialLayerConfig*)&pCodingParam->sSpatialLayers + i;
            int32_t kiPicWidth = pSpatialLayer->iVideoWidth;
            int32_t kiPicHeight = pSpatialLayer->iVideoHeight;
            const int64_t kiMaxPixelsPerFrame = ((int64_t)(MAX_MBS_PER_FRAME) << 8);
            uint32_t iMbWidth = 0;
            uint32_t iMbHeight = 0;
            int32_t iMbNumInFrame = 0;
            uint32_t iMaxSliceNum = MAX_SLICES_NUM;
            int32_t iReturn = 0;

            if ((pCodingParam->iPicWidth > 0) && (pCodingParam->iPicHeight > 0)
                && (kiPicWidth == 0) && (kiPicHeight == 0)
                && (pCodingParam->iSpatialLayerNum == 1))
            {
                kiPicWidth = pSpatialLayer->iVideoWidth = pCodingParam->iPicWidth;
                kiPicHeight = pSpatialLayer->iVideoHeight = pCodingParam->iPicHeight;
                WelsLog(pLogCtx, WELS_LOG_DEBUG,
                        $"ParamValidationExt(), layer resolution is not set, set to general resolution {pSpatialLayer->iVideoWidth} x {pSpatialLayer->iVideoHeight}");
            }

            if ((kiPicWidth <= 0) || (kiPicHeight <= 0)
                || ((int64_t)(kiPicWidth) * kiPicHeight > kiMaxPixelsPerFrame))
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR,
                        $"ParamValidationExt(), width > 0, height > 0, width * height <= {(MAX_MBS_PER_FRAME << 8)}, invalid {kiPicWidth} x {kiPicHeight} in dependency layer settings!");
                return ENC_RETURN_UNSUPPORTED_PARA;
            }
            if ((kiPicWidth & 0x0F) != 0 || (kiPicHeight & 0x0F) != 0)
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR,
                        $"ParamValidationExt(), in layer #{i} iWidth x iHeight({kiPicWidth} x {kiPicHeight}) both should be multiple of 16, can not support with arbitrary size currently!");
                return ENC_RETURN_UNSUPPORTED_PARA;
            }

            if (pSpatialLayer->sSliceArgument.uiSliceMode >= SM_RESERVED)
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), invalid uiSliceMode ({(int)pSpatialLayer->sSliceArgument.uiSliceMode}) settings!");
                return ENC_RETURN_UNSUPPORTED_PARA;
            }
            if ((pCodingParam->uiMaxNalSize != 0) && (pSpatialLayer->sSliceArgument.uiSliceMode != SM_SIZELIMITED_SLICE))
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"ParamValidationExt(), current layer {i} uiSliceMode ({(int)pSpatialLayer->sSliceArgument.uiSliceMode}) settings may not fulfill MaxNalSize = {pCodingParam->uiMaxNalSize}");
            }
            CheckProfileSetting(pLogCtx, pCodingParam, i, pSpatialLayer->uiProfileIdc);
            CheckLevelSetting(pLogCtx, pCodingParam, i, pSpatialLayer->uiLevelIdc);
            //check pSlice settings under multi-pSlice
            if (kiPicWidth <= 16 && kiPicHeight <= 16)
            {
                //only have one MB, set to single_slice
                pSpatialLayer->sSliceArgument.uiSliceMode = SM_SINGLE_SLICE;
            }
            switch (pSpatialLayer->sSliceArgument.uiSliceMode)
            {
                case SM_SINGLE_SLICE:
                    pSpatialLayer->sSliceArgument.uiSliceNum = 1;
                    pSpatialLayer->sSliceArgument.uiSliceSizeConstraint = 0;
                    for (iIdx = 0; iIdx < MAX_SLICES_NUM; iIdx++)
                    {
                        pSpatialLayer->sSliceArgument.uiSliceMbNum[iIdx] = 0;
                    }
                    break;
                case SM_FIXEDSLCNUM_SLICE:
                    {
                        iReturn = SliceArgumentValidationFixedSliceMode(pLogCtx, &pSpatialLayer->sSliceArgument, pCodingParam->iRCMode,
                                  kiPicWidth, kiPicHeight);
                        if (iReturn != 0)
                            return ENC_RETURN_UNSUPPORTED_PARA;
                    }
                    break;
                case SM_RASTER_SLICE:
                    {
                        pSpatialLayer->sSliceArgument.uiSliceSizeConstraint = 0;

                        iMbWidth = (uint32_t)((kiPicWidth + 15) >> 4);
                        iMbHeight = (uint32_t)((kiPicHeight + 15) >> 4);
                        iMbNumInFrame = (int32_t)(iMbWidth * iMbHeight);
                        iMaxSliceNum = MAX_SLICES_NUM;
                        if (pSpatialLayer->sSliceArgument.uiSliceMbNum[0] == 0)
                        {
                            if (iMbHeight > iMaxSliceNum)
                            {
                                WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), invalid uiSliceNum ({iMbHeight}) settings more than MAX({MAX_SLICES_NUM})!");
                                return ENC_RETURN_UNSUPPORTED_PARA;
                            }
                            pSpatialLayer->sSliceArgument.uiSliceNum = iMbHeight;
                            for (uint32_t j = 0; j < iMbHeight; j++)
                            {
                                pSpatialLayer->sSliceArgument.uiSliceMbNum[j] = iMbWidth;
                            }
                            if (!CheckRowMbMultiSliceSetting((int32_t)iMbWidth,
                                                             &pSpatialLayer->sSliceArgument))
                            { // verify interleave mode settings
                                WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), invalid uiSliceMbNum ({pSpatialLayer->sSliceArgument.uiSliceMbNum[0]}) settings!");
                                return ENC_RETURN_UNSUPPORTED_PARA;
                            }
                            break;
                        }

                        if (!CheckRasterMultiSliceSetting(iMbNumInFrame,
                                                          &pSpatialLayer->sSliceArgument))
                        { // verify interleave mode settings
                            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), invalid uiSliceMbNum ({pSpatialLayer->sSliceArgument.uiSliceMbNum[0]}) settings!");
                            return ENC_RETURN_UNSUPPORTED_PARA;
                        }
                        if (pSpatialLayer->sSliceArgument.uiSliceNum <= 0
                            || pSpatialLayer->sSliceArgument.uiSliceNum > iMaxSliceNum)
                        { // verify interleave mode settings
                            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), invalid uiSliceNum ({pSpatialLayer->sSliceArgument.uiSliceNum}) in SM_RASTER_SLICE settings!");
                            return ENC_RETURN_UNSUPPORTED_PARA;
                        }
                        if (pSpatialLayer->sSliceArgument.uiSliceNum == 1)
                        {
                            WelsLog(pLogCtx, WELS_LOG_WARNING,
                                    $"ParamValidationExt(), pSlice setting for SM_RASTER_SLICE now turn to SM_SINGLE_SLICE!");
                            pSpatialLayer->sSliceArgument.uiSliceMode = SM_SINGLE_SLICE;
                            break;
                        }
                        if ((pCodingParam->iRCMode != RC_OFF_MODE) && pSpatialLayer->sSliceArgument.uiSliceNum > 1)
                        {
                            WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), WARNING: GOM based RC do not support SM_RASTER_SLICE!");
                        }
                        // considering the coding efficient and performance, iCountMbNum constraint by MIN_NUM_MB_PER_SLICE condition of multi-pSlice mode settting
                        if (iMbNumInFrame <= MIN_NUM_MB_PER_SLICE)
                        {
                            pSpatialLayer->sSliceArgument.uiSliceMode = SM_SINGLE_SLICE;
                            pSpatialLayer->sSliceArgument.uiSliceNum = 1;
                            break;
                        }
                    }
                    break;
                case SM_SIZELIMITED_SLICE:
                    {
                        iMbWidth = (uint32_t)((kiPicWidth + 15) >> 4);
                        iMbHeight = (uint32_t)((kiPicHeight + 15) >> 4);
                        if (pSpatialLayer->sSliceArgument.uiSliceSizeConstraint <= MAX_MACROBLOCK_SIZE_IN_BYTE)
                        {
                            WelsLog(pLogCtx, WELS_LOG_ERROR,
                                    $"ParamValidationExt(), invalid iSliceSize ({pSpatialLayer->sSliceArgument.uiSliceSizeConstraint}) settings!should be larger than  MAX_MACROBLOCK_SIZE_IN_BYTE({MAX_MACROBLOCK_SIZE_IN_BYTE})");
                            return ENC_RETURN_UNSUPPORTED_PARA;
                        }

                        if (pCodingParam->uiMaxNalSize > 0)
                        {
                            if (pCodingParam->uiMaxNalSize < (NAL_HEADER_ADD_0X30BYTES + MAX_MACROBLOCK_SIZE_IN_BYTE))
                            {
                                WelsLog(pLogCtx, WELS_LOG_ERROR,
                                        $"ParamValidationExt(), invalid uiMaxNalSize ({pCodingParam->uiMaxNalSize}) settings! should be larger than (NAL_HEADER_ADD_0X30BYTES + MAX_MACROBLOCK_SIZE_IN_BYTE)({(NAL_HEADER_ADD_0X30BYTES + MAX_MACROBLOCK_SIZE_IN_BYTE)})");
                                return ENC_RETURN_UNSUPPORTED_PARA;
                            }

                            if (pSpatialLayer->sSliceArgument.uiSliceSizeConstraint > (uint32_t)(pCodingParam->uiMaxNalSize -
                                (uint32_t)NAL_HEADER_ADD_0X30BYTES))
                            {
                                WelsLog(pLogCtx, WELS_LOG_WARNING,
                                        $"ParamValidationExt(), slice mode = SM_SIZELIMITED_SLICE, uiSliceSizeConstraint = {pSpatialLayer->sSliceArgument.uiSliceSizeConstraint} ,uiMaxNalsize = {pCodingParam->uiMaxNalSize}, will take uiMaxNalsize!");
                                pSpatialLayer->sSliceArgument.uiSliceSizeConstraint = pCodingParam->uiMaxNalSize - (uint32_t)NAL_HEADER_ADD_0X30BYTES;
                            }
                        }
                        pSpatialLayer->sSliceArgument.uiSliceSizeConstraint -= (uint32_t)NAL_HEADER_ADD_0X30BYTES;
                    }
                    break;
                default:
                    {
                        WelsLog(pLogCtx, WELS_LOG_ERROR, $"ParamValidationExt(), invalid uiSliceMode ({(int)pCodingParam->sSpatialLayers[0].sSliceArgument.uiSliceMode}) settings!");
                        return ENC_RETURN_UNSUPPORTED_PARA;
                    }
            }
        }
        for (i = 0; i < pCodingParam->iSpatialLayerNum; ++i)
        {
            SSpatialLayerConfig* pLayerInfo = (SSpatialLayerConfig*)&pCodingParam->sSpatialLayers + i;
            if ((pLayerInfo->uiProfileIdc == PRO_BASELINE) || (pLayerInfo->uiProfileIdc == PRO_SCALABLE_BASELINE))
            {
                if (pCodingParam->iEntropyCodingModeFlag != 0)
                {
                    pCodingParam->iEntropyCodingModeFlag = 0;
                    WelsLog(pLogCtx, WELS_LOG_WARNING, $"layerId({i}) Profile is baseline, Change CABAC to CAVLC");
                }
            }
            else if (pLayerInfo->uiProfileIdc == PRO_UNKNOWN)
            {
                if ((i == 0) || pCodingParam->bSimulcastAVC)
                {
                    pLayerInfo->uiProfileIdc = (pCodingParam->iEntropyCodingModeFlag != 0) ? PRO_HIGH : PRO_BASELINE;
                }
                else
                {
                    pLayerInfo->uiProfileIdc = PRO_SCALABLE_BASELINE;
                }
            }
        }
        return ParamValidation(pLogCtx, pCodingParam);
    }

    public static int32_t InitSliceSettings(SLogContext* pLogCtx, SWelsSvcCodingParam* pCodingParam,
                                            int32_t kiCpuCores, int16_t* pMaxSliceCount)
    {
        int32_t iSpatialIdx = 0, iSpatialNum = pCodingParam->iSpatialLayerNum;
        uint16_t iMaxSliceCount = 0;

        do
        {
            SSpatialLayerConfig* pDlp = (SSpatialLayerConfig*)&pCodingParam->sSpatialLayers + iSpatialIdx;
            SSliceArgument* pSliceArgument = &pDlp->sSliceArgument;
            int32_t iReturn = 0;

            switch (pSliceArgument->uiSliceMode)
            {
                case SM_SIZELIMITED_SLICE:
                    iMaxSliceCount = AVERSLICENUM_CONSTRAINT;
                    break; // go through for SM_SIZELIMITED_SLICE?
                case SM_FIXEDSLCNUM_SLICE:
                    {
                        iReturn = SliceArgumentValidationFixedSliceMode(pLogCtx, &pDlp->sSliceArgument, pCodingParam->iRCMode,
                                  pDlp->iVideoWidth, pDlp->iVideoHeight);
                        if (iReturn != 0)
                            return ENC_RETURN_UNSUPPORTED_PARA;

                        if (pSliceArgument->uiSliceNum > iMaxSliceCount)
                        {
                            iMaxSliceCount = (uint16_t)pSliceArgument->uiSliceNum;
                        }
                    }
                    break;
                case SM_SINGLE_SLICE:
                    if (pSliceArgument->uiSliceNum > iMaxSliceCount)
                        iMaxSliceCount = (uint16_t)pSliceArgument->uiSliceNum;
                    break;
                case SM_RASTER_SLICE:
                    if (pSliceArgument->uiSliceNum > iMaxSliceCount)
                        iMaxSliceCount = (uint16_t)pSliceArgument->uiSliceNum;
                    break;
                default:
                    break;
            }

            ++iSpatialIdx;
        } while (iSpatialIdx < iSpatialNum);

        pCodingParam->iMultipleThreadIdc = (uint16_t)WELS_MIN(kiCpuCores, (int32_t)iMaxSliceCount);
        if (pCodingParam->iLoopFilterDisableIdc == 0
            && pCodingParam->iMultipleThreadIdc != 1) // Loop filter requested to be enabled, with threading enabled
            pCodingParam->iLoopFilterDisableIdc =
              2; // Disable loop filter on slice boundaries since that's not allowed with multithreading
        *pMaxSliceCount = (int16_t)iMaxSliceCount;

        return ENC_RETURN_SUCCESS;
    }

    public static int32_t GetMultipleThreadIdc(SLogContext* pLogCtx, SWelsSvcCodingParam* pCodingParam, ref int16_t iSliceNum,
                                               ref int32_t iCacheLineSize, ref uint32_t uiCpuFeatureFlags)
    {
        // for cpu features detection, Only detect once??
        int32_t uiCpuCores =
          0; // number of logic processors on physical processor package, zero logic processors means HTT not supported
        uiCpuFeatureFlags = 0; // WelsCPUFeatureDetect (&uiCpuCores): C-only build, no features and no core count

        // #ifdef X86_ASM ... #else
        iCacheLineSize = 16; // 16 bytes aligned in default
        // #endif//X86_ASM

        if (0 == pCodingParam->iMultipleThreadIdc && uiCpuCores == 0)
        {
            // cpuid not supported or doesn't expose the number of cores,
            // use high level system API as followed to detect number of pysical/logic processor
            uiCpuCores = DynamicDetectCpuCores();
        }

        if (0 == pCodingParam->iMultipleThreadIdc)
            pCodingParam->iMultipleThreadIdc = (uint16_t)((uiCpuCores > 0) ? uiCpuCores : 1);

        // So far so many cpu cores up to MAX_THREADS_NUM mean for server platforms,
        // for client application here it is constrained by maximal to MAX_THREADS_NUM
        pCodingParam->iMultipleThreadIdc = (uint16_t)WELS_CLIP3((int32_t)pCodingParam->iMultipleThreadIdc, 1, MAX_THREADS_NUM);
        uiCpuCores = pCodingParam->iMultipleThreadIdc;

        fixed (int16_t* pSliceNum = &iSliceNum)
        {
            if (InitSliceSettings(pLogCtx, pCodingParam, uiCpuCores, pSliceNum) != 0)
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR, $"GetMultipleThreadIdc(), InitSliceSettings failed.");
                return 1;
            }
        }
        return 0;
    }

    public static int32_t WelsWriteOneSPS(sWelsEncCtx* pCtx, int32_t kiSpsIdx, ref int32_t iNalSize)
    {
        int iNal = pCtx->pOut->iNalIndex;
        WelsLoadNal(pCtx->pOut, (int32_t)NAL_UNIT_SPS, (int32_t)NRI_PRI_HIGHEST);

        WelsWriteSpsNal(&pCtx->pSpsArray[kiSpsIdx], &pCtx->pOut->sBsWrite,
                        pCtx->pFuncList->pParametersetStrategy->GetSpsIdOffsetList(PARA_SET_TYPE_AVCSPS));
        WelsUnloadNal(pCtx->pOut);

        int32_t iReturn;
        fixed (int32_t* pNalSize = &iNalSize)
        {
            iReturn = WelsEncodeNal(&pCtx->pOut->sNalList[iNal], null,
                                    pCtx->iFrameBsSize - pCtx->iPosBsBuffer,//available buffer to be written, so need to substract the used length
                                    pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                    pNalSize);
        }
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ

        pCtx->iPosBsBuffer += iNalSize;
        return ENC_RETURN_SUCCESS;
    }

    public static int32_t WelsWriteOnePPS(sWelsEncCtx* pCtx, int32_t kiPpsIdx, ref int32_t iNalSize)
    {
        //TODO
        int32_t iNal = pCtx->pOut->iNalIndex;
        /* generate picture parameter set */
        WelsLoadNal(pCtx->pOut, (int32_t)NAL_UNIT_PPS, (int32_t)NRI_PRI_HIGHEST);

        WelsWritePpsSyntax(&pCtx->pPPSArray[kiPpsIdx], &pCtx->pOut->sBsWrite,
                           pCtx->pFuncList->pParametersetStrategy);
        WelsUnloadNal(pCtx->pOut);

        int32_t iReturn;
        fixed (int32_t* pNalSize = &iNalSize)
        {
            iReturn = WelsEncodeNal(&pCtx->pOut->sNalList[iNal], null,
                                    pCtx->iFrameBsSize - pCtx->iPosBsBuffer,
                                    pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                    pNalSize);
        }
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ

        pCtx->iPosBsBuffer += iNalSize;
        return ENC_RETURN_SUCCESS;
    }

    /*!
     * \brief   write all parameter sets introduced in SVC extension
     * \return  writing results, success or error
     */
    public static int32_t WelsWriteParameterSets(sWelsEncCtx* pCtx, int32_t* pNalLen, int32_t* pNumNal, int32_t* pTotalLength)
    {
        int32_t iSize = 0;
        int32_t iNal = 0;
        int32_t iIdx = 0;
        int32_t iId = 0;
        int32_t iCountNal = 0;
        int32_t iNalLength = 0;
        int32_t iReturn = ENC_RETURN_SUCCESS;

        if (null == pCtx || null == pNalLen || null == pNumNal || null == pCtx->pFuncList->pParametersetStrategy)
            return ENC_RETURN_UNEXPECTED;

        *pTotalLength = 0;
        /* write all SPS */
        iIdx = 0;
        while (iIdx < pCtx->iSpsNum)
        {
            pCtx->pFuncList->pParametersetStrategy->Update(pCtx->pSpsArray[iIdx].uiSpsId, PARA_SET_TYPE_AVCSPS);
            /* generate sequence parameters set */
            iId = pCtx->pFuncList->pParametersetStrategy->GetSpsIdx(iIdx);

            WelsWriteOneSPS(pCtx, iId, ref iNalLength);

            pNalLen[iCountNal] = iNalLength;
            iSize += iNalLength;

            ++iIdx;
            ++iCountNal;
        }

        /* write all Subset SPS */
        iIdx = 0;
        while (iIdx < pCtx->iSubsetSpsNum)
        {
            iNal = pCtx->pOut->iNalIndex;

            pCtx->pFuncList->pParametersetStrategy->Update(pCtx->pSubsetArray[iIdx].pSps.uiSpsId, PARA_SET_TYPE_SUBSETSPS);

            iId = iIdx;

            /* generate Subset SPS */
            WelsLoadNal(pCtx->pOut, (int32_t)NAL_UNIT_SUBSET_SPS, (int32_t)NRI_PRI_HIGHEST);

            WelsWriteSubsetSpsSyntax(&pCtx->pSubsetArray[iId], &pCtx->pOut->sBsWrite,
                                     pCtx->pFuncList->pParametersetStrategy->GetSpsIdOffsetList(PARA_SET_TYPE_SUBSETSPS));
            WelsUnloadNal(pCtx->pOut);

            iReturn = WelsEncodeNal(&pCtx->pOut->sNalList[iNal], null,
                                    pCtx->iFrameBsSize - pCtx->iPosBsBuffer,//available buffer to be written, so need to substract the used length
                                    pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                    &iNalLength);
            if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ
            pNalLen[iCountNal] = iNalLength;

            pCtx->iPosBsBuffer += iNalLength;
            iSize += iNalLength;

            ++iIdx;
            ++iCountNal;
        }

        pCtx->pFuncList->pParametersetStrategy->UpdatePpsList(pCtx);

        iIdx = 0;
        while (iIdx < pCtx->iPpsNum)
        {
            pCtx->pFuncList->pParametersetStrategy->Update(pCtx->pPPSArray[iIdx].iPpsId, PARA_SET_TYPE_PPS);

            WelsWriteOnePPS(pCtx, iIdx, ref iNalLength);

            pNalLen[iCountNal] = iNalLength;
            iSize += iNalLength;

            ++iIdx;
            ++iCountNal;
        }

        *pNumNal = iCountNal;
        *pTotalLength = iSize;

        return ENC_RETURN_SUCCESS;
    }

    public static int32_t WelsEncoderEncodeParameterSets(sWelsEncCtx* pCtx, void* pDst)
    {
        if (null == pCtx || null == pDst)
        {
            return ENC_RETURN_UNEXPECTED;
        }

        SFrameBSInfo* pFbi = (SFrameBSInfo*)pDst;
        SLayerBSInfo* pLayerBsInfo = (SLayerBSInfo*)&pFbi->sLayerInfo;
        int32_t iCountNal = 0;
        int32_t iTotalLength = 0;

        pLayerBsInfo->pBsBuf = pCtx->pFrameBs;
        pLayerBsInfo->pNalLengthInByte = pCtx->pOut->pNalLen;
        InitBits(&pCtx->pOut->sBsWrite, pCtx->pOut->pBsBuffer, (int32_t)pCtx->pOut->uiSize);

        pCtx->iPosBsBuffer = 0;
        int32_t iReturn = WelsWriteParameterSets(pCtx, &pLayerBsInfo->pNalLengthInByte[0], &iCountNal, &iTotalLength);
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ

        pLayerBsInfo->uiSpatialId = 0;
        pLayerBsInfo->uiTemporalId = 0;
        pLayerBsInfo->uiQualityId = 0;
        pLayerBsInfo->uiLayerType = (uint8_t)NON_VIDEO_CODING_LAYER;
        pLayerBsInfo->iNalCount = iCountNal;
        pLayerBsInfo->eFrameType = videoFrameTypeInvalid;
        pLayerBsInfo->iSubSeqId = 0;
        //pCtx->eLastNalPriority      = NRI_PRI_HIGHEST;
        pFbi->iLayerNum = 1;
        pFbi->eFrameType = videoFrameTypeInvalid;
        // WelsEmms (): no-op without X86_ASM

        return ENC_RETURN_SUCCESS;
    }

    public static int32_t GetSubSequenceId(sWelsEncCtx* pCtx, EVideoFrameType eFrameType)
    {
        int32_t iSubSeqId = 0;
        if (eFrameType == videoFrameTypeIDR)
            iSubSeqId = 0;
        else if (eFrameType == videoFrameTypeI)
            iSubSeqId = 1;
        else if (eFrameType == videoFrameTypeP)
        {
            if (pCtx->bCurFrameMarkedAsSceneLtr)
                iSubSeqId = 2;
            else
                iSubSeqId = 3 + pCtx->uiTemporalId; //T0:3 T1:4 T2:5 T3:6
        }
        else
            iSubSeqId = 3 + MAX_TEMPORAL_LAYER_NUM;
        return iSubSeqId;
    }

    // writing parasets for (simulcast) svc
    public static int32_t WriteSsvcParaset(sWelsEncCtx* pCtx, int32_t kiSpatialNum,
                                           ref SLayerBSInfo* pLayerBsInfo, ref int32_t iLayerNum, ref int32_t iFrameSize)
    {
        int32_t iNonVclSize = 0, iCountNal = 0, iReturn = ENC_RETURN_SUCCESS;
        iReturn = WelsWriteParameterSets(pCtx, &pLayerBsInfo->pNalLengthInByte[0], &iCountNal, &iNonVclSize);
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ
        for (int32_t iSpatialId = 0; iSpatialId < kiSpatialNum; iSpatialId++)
        {
            SSpatialLayerInternal* pParamInternal = (SSpatialLayerInternal*)&pCtx->pSvcParam->sDependencyLayers + iSpatialId;
            if (pParamInternal->uiIdrPicId < 65535)
            {
                ++pParamInternal->uiIdrPicId;
            }
            else
            {
                pParamInternal->uiIdrPicId = 0;
            }
        }
        pLayerBsInfo->uiSpatialId = 0;
        pLayerBsInfo->uiTemporalId = 0;
        pLayerBsInfo->uiQualityId = 0;
        pLayerBsInfo->uiLayerType = (uint8_t)NON_VIDEO_CODING_LAYER;
        pLayerBsInfo->iNalCount = iCountNal;
        pLayerBsInfo->eFrameType = videoFrameTypeIDR;
        pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, videoFrameTypeIDR);
        //point to next pLayerBsInfo
        ++pLayerBsInfo;
        ++pCtx->pOut->iLayerBsIndex;
        pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
        pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + iCountNal;

        //update for external countings
        ++iLayerNum;
        iFrameSize += iNonVclSize;
        return iReturn;
    }

    // writing parasets for simulcast avc
    public static int32_t WriteSavcParaset(sWelsEncCtx* pCtx, int32_t iIdx,
                                           ref SLayerBSInfo* pLayerBsInfo, ref int32_t iLayerNum, ref int32_t iFrameSize)
    {
        int32_t iNonVclSize = 0, iCountNal = 0, iReturn = ENC_RETURN_SUCCESS;

        // write SPS
        iNonVclSize = 0;

        //writing one NAL
        int32_t iNalSize = 0;
        iCountNal = 0;

        if (pCtx->pFuncList->pParametersetStrategy != null)
        {
            pCtx->pFuncList->pParametersetStrategy->Update(pCtx->pSpsArray[iIdx].uiSpsId, PARA_SET_TYPE_AVCSPS);
        }

        iReturn = WelsWriteOneSPS(pCtx, iIdx, ref iNalSize);
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ

        pLayerBsInfo->pNalLengthInByte[iCountNal] = iNalSize;
        iNonVclSize += iNalSize;
        iCountNal = 1;

        //finish writing one NAL

        pLayerBsInfo->uiSpatialId = (uint8_t)iIdx;
        pLayerBsInfo->uiTemporalId = 0;
        pLayerBsInfo->uiQualityId = 0;
        pLayerBsInfo->uiLayerType = (uint8_t)NON_VIDEO_CODING_LAYER;
        pLayerBsInfo->iNalCount = iCountNal;
        pLayerBsInfo->eFrameType = videoFrameTypeIDR;
        pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, videoFrameTypeIDR);
        //point to next pLayerBsInfo
        ++pLayerBsInfo;
        ++pCtx->pOut->iLayerBsIndex;
        pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
        pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + iCountNal;
        //update for external countings
        ++iLayerNum;

        // write PPS

        //TODO: under new strategy, will PPS be correctly updated?

        //writing one NAL
        iNalSize = 0;
        iCountNal = 0;

        if (pCtx->pFuncList->pParametersetStrategy != null)
        {
            pCtx->pFuncList->pParametersetStrategy->Update(pCtx->pPPSArray[iIdx].iPpsId, PARA_SET_TYPE_PPS);
        }

        iReturn = WelsWriteOnePPS(pCtx, iIdx, ref iNalSize);
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ

        pLayerBsInfo->pNalLengthInByte[iCountNal] = iNalSize;
        iNonVclSize += iNalSize;
        iCountNal = 1;
        //finish writing one NAL

        pLayerBsInfo->uiSpatialId = (uint8_t)iIdx;
        pLayerBsInfo->uiTemporalId = 0;
        pLayerBsInfo->uiQualityId = 0;
        pLayerBsInfo->uiLayerType = (uint8_t)NON_VIDEO_CODING_LAYER;
        pLayerBsInfo->iNalCount = iCountNal;
        pLayerBsInfo->eFrameType = videoFrameTypeIDR;
        pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, videoFrameTypeIDR);
        //point to next pLayerBsInfo
        ++pLayerBsInfo;
        ++pCtx->pOut->iLayerBsIndex;
        pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
        pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + iCountNal;
        //update for external countings
        ++iLayerNum;

        // to check number of layers / nals / slices dependencies
        if (iLayerNum > MAX_LAYER_NUM_OF_FRAME)
        {
            WelsLog(&pCtx->sLogCtx, WELS_LOG_ERROR, $"WriteSavcParaset(), iLayerNum({iLayerNum}) > MAX_LAYER_NUM_OF_FRAME({MAX_LAYER_NUM_OF_FRAME})!");
            return 1;
        }

        iFrameSize += iNonVclSize;
        return iReturn;
    }

    //cover the logic of simulcast avc + sps_pps_listing
    public static int32_t WriteSavcParaset_Listing(sWelsEncCtx* pCtx, int32_t kiSpatialNum,
                                                   ref SLayerBSInfo* pLayerBsInfo, ref int32_t iLayerNum, ref int32_t iFrameSize)
    {
        int32_t iNonVclSize = 0, iCountNal = 0, iReturn = ENC_RETURN_SUCCESS;

        // write SPS
        iNonVclSize = 0;

        for (int32_t iSpatialId = 0; iSpatialId < kiSpatialNum; iSpatialId++)
        {
            SSpatialLayerInternal* pParamInternal = (SSpatialLayerInternal*)&pCtx->pSvcParam->sDependencyLayers + iSpatialId;
            if (pParamInternal->uiIdrPicId < 65535)
            {
                ++pParamInternal->uiIdrPicId;
            }
            else
            {
                pParamInternal->uiIdrPicId = 0;
            }

            iCountNal = 0;

            for (int32_t iIdx = 0; iIdx < pCtx->iSpsNum; iIdx++)
            {
                //writing one NAL
                int32_t iNalSize = 0;
                iReturn = WelsWriteOneSPS(pCtx, iIdx, ref iNalSize);
                if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ

                pLayerBsInfo->pNalLengthInByte[iCountNal] = iNalSize;
                iNonVclSize += iNalSize;
                iCountNal++;
                //finish writing one NAL
            }

            pLayerBsInfo->uiSpatialId = (uint8_t)iSpatialId;
            pLayerBsInfo->uiTemporalId = 0;
            pLayerBsInfo->uiQualityId = 0;
            pLayerBsInfo->uiLayerType = (uint8_t)NON_VIDEO_CODING_LAYER;
            pLayerBsInfo->iNalCount = iCountNal;
            pLayerBsInfo->eFrameType = videoFrameTypeIDR;
            pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, videoFrameTypeIDR);
            //point to next pLayerBsInfo
            ++pLayerBsInfo;
            ++pCtx->pOut->iLayerBsIndex;
            pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
            pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + iCountNal;
            //update for external countings
            ++iLayerNum;
        }

        // write PPS
        pCtx->pFuncList->pParametersetStrategy->UpdatePpsList(pCtx);

        //TODO: under new strategy, will PPS be correctly updated?
        for (int32_t iSpatialId = 0; iSpatialId < kiSpatialNum; iSpatialId++)
        {
            iCountNal = 0;
            for (int32_t iIdx = 0; iIdx < pCtx->iPpsNum; iIdx++)
            {
                //writing one NAL
                int32_t iNalSize = 0;
                iReturn = WelsWriteOnePPS(pCtx, iIdx, ref iNalSize);
                if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ

                pLayerBsInfo->pNalLengthInByte[iCountNal] = iNalSize;
                iNonVclSize += iNalSize;
                iCountNal++;
                //finish writing one NAL
            }

            pLayerBsInfo->uiSpatialId = (uint8_t)iSpatialId;
            pLayerBsInfo->uiTemporalId = 0;
            pLayerBsInfo->uiQualityId = 0;
            pLayerBsInfo->uiLayerType = (uint8_t)NON_VIDEO_CODING_LAYER;
            pLayerBsInfo->iNalCount = iCountNal;
            pLayerBsInfo->eFrameType = videoFrameTypeIDR;
            pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, videoFrameTypeIDR);
            //point to next pLayerBsInfo
            ++pLayerBsInfo;
            ++pCtx->pOut->iLayerBsIndex;
            pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
            pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + iCountNal;
            //update for external countings
            ++iLayerNum;
        }

        // to check number of layers / nals / slices dependencies
        if (iLayerNum > MAX_LAYER_NUM_OF_FRAME)
        {
            WelsLog(&pCtx->sLogCtx, WELS_LOG_ERROR, $"WriteSavcParaset(), iLayerNum({iLayerNum}) > MAX_LAYER_NUM_OF_FRAME({MAX_LAYER_NUM_OF_FRAME})!");
            return ENC_RETURN_UNEXPECTED;
        }

        iFrameSize += iNonVclSize;
        return iReturn;
    }

    // E4: AllocStrideTables (encoder_ext.cpp:1226) and the static SetFastCodingFunc and
    // SetNormalCodingFunc (encoder_ext.cpp:2625) that PreprocessSliceCoding picks between.

    /// <summary>sMbSizeMap's anonymous element type in AllocStrideTables.</summary>
    private struct SMbSizeMapEntry
    {
        public int32_t iMbWidth;
        public int32_t iCountMbNum;                // count number of SMB in each spatial
        public int32_t iSizeAllMbAlignCache;       // cache line size aligned in each spatial
    }

    public static int32_t AllocStrideTables(sWelsEncCtx** ppCtx, int32_t kiNumSpatialLayers)
    {
        CMemoryAlign* pMa = (*ppCtx)->pMemAlign;
        SWelsSvcCodingParam* pParam = (*ppCtx)->pSvcParam;
        SStrideTables* pPtr = null;
        int16_t* pTmpRow = null, pRowX = null, pRowY = null, p = null;
        uint8_t* pBase = null;
        uint8_t* pBaseDec = null, pBaseEnc = null, pBaseMbX = null, pBaseMbY = null;
        SMbSizeMapEntry* sMbSizeMap = stackalloc SMbSizeMapEntry[MAX_DEPENDENCY_LAYER];
        int32_t* iLineSizeY = stackalloc int32_t[MAX_DEPENDENCY_LAYER * 2];     // [MAX_DEPENDENCY_LAYER][2]
        int32_t* iLineSizeUV = stackalloc int32_t[MAX_DEPENDENCY_LAYER * 2];    // [MAX_DEPENDENCY_LAYER][2]
        int32_t* iMapSpatialIdx = stackalloc int32_t[MAX_DEPENDENCY_LAYER * 2]; // [MAX_DEPENDENCY_LAYER][2]
        int32_t iSizeDec = 0;
        int32_t iSizeEnc = 0;
        int32_t* iCountLayersNeedCs = stackalloc int32_t[2];
        int32_t kiUnit1Size = 24 * sizeof(int32_t);
        int32_t iUnit2Size = 0;
        int32_t iNeedAllocSize = 0;
        int32_t iRowSize = 0;
        int16_t iMaxMbWidth = 0;
        int16_t iMaxMbHeight = 0;
        int32_t i = 0;
        int32_t iSpatialIdx = 0;
        int32_t iTemporalIdx = 0;
        int32_t iCntTid = 0;

        // The C initialisers zero these.
        new Span<byte>(sMbSizeMap, MAX_DEPENDENCY_LAYER * sizeof(SMbSizeMapEntry)).Clear();
        new Span<int32_t>(iLineSizeY, MAX_DEPENDENCY_LAYER * 2).Clear();
        new Span<int32_t>(iLineSizeUV, MAX_DEPENDENCY_LAYER * 2).Clear();
        new Span<int32_t>(iMapSpatialIdx, MAX_DEPENDENCY_LAYER * 2).Clear();
        iCountLayersNeedCs[0] = iCountLayersNeedCs[1] = 0;

        if (kiNumSpatialLayers <= 0 || kiNumSpatialLayers > MAX_DEPENDENCY_LAYER)
            return 1;

        pPtr = (SStrideTables*)pMa->WelsMallocz((uint32_t)sizeof(SStrideTables), "SStrideTables");
        if (null == pPtr)
            return 1;
        (*ppCtx)->pStrideTab = pPtr;

        iCntTid = pParam->iTemporalLayerNum > 1 ? 2 : 1;

        iSpatialIdx = 0;
        while (iSpatialIdx < kiNumSpatialLayers)
        {
            int32_t kiTmpWidth = (pParam->sSpatialLayers[iSpatialIdx].iVideoWidth + 15) >> 4;
            int32_t kiTmpHeight = (pParam->sSpatialLayers[iSpatialIdx].iVideoHeight + 15) >> 4;
            int32_t iNumMb = kiTmpWidth * kiTmpHeight;

            sMbSizeMap[iSpatialIdx].iMbWidth = kiTmpWidth;
            sMbSizeMap[iSpatialIdx].iCountMbNum = iNumMb;

            iNumMb *= sizeof(int16_t);
            sMbSizeMap[iSpatialIdx].iSizeAllMbAlignCache = iNumMb;
            iUnit2Size += iNumMb;

            ++iSpatialIdx;
        }

        // Adaptive size_cs, size_fdec by implementation dependency
        iTemporalIdx = 0;
        while (iTemporalIdx < iCntTid)
        {
            int32_t kbBaseTemporalFlag = (iTemporalIdx == 0) ? 1 : 0;

            iSpatialIdx = 0;
            while (iSpatialIdx < kiNumSpatialLayers)
            {
                SSpatialLayerConfig* fDlp = &pParam->sSpatialLayers[iSpatialIdx];

                int32_t kiWidthPad = WELS_ALIGN(fDlp->iVideoWidth, 16) + (PADDING_LENGTH << 1);
                iLineSizeY[iSpatialIdx * 2 + kbBaseTemporalFlag] = WELS_ALIGN(kiWidthPad, 32);
                iLineSizeUV[iSpatialIdx * 2 + kbBaseTemporalFlag] = WELS_ALIGN((kiWidthPad >> 1), 16);

                iMapSpatialIdx[iCountLayersNeedCs[kbBaseTemporalFlag] * 2 + kbBaseTemporalFlag] = iSpatialIdx;
                ++iCountLayersNeedCs[kbBaseTemporalFlag];
                ++iSpatialIdx;
            }
            ++iTemporalIdx;
        }
        iSizeDec = kiUnit1Size * (iCountLayersNeedCs[0] + iCountLayersNeedCs[1]);
        iSizeEnc = kiUnit1Size * kiNumSpatialLayers;

        iNeedAllocSize = iSizeDec + iSizeEnc + (iUnit2Size << 1);

        pBase = (uint8_t*)pMa->WelsMallocz((uint32_t)iNeedAllocSize, "pBase");
        if (null == pBase)
        {
            return 1;
        }

        pBaseDec = pBase;                     // iCountLayersNeedCs
        pBaseEnc = pBaseDec + iSizeDec;       // iNumSpatialLayers
        pBaseMbX = pBaseEnc + iSizeEnc;       // iNumSpatialLayers
        pBaseMbY = pBaseMbX + iUnit2Size;     // iNumSpatialLayers

        iTemporalIdx = 0;
        while (iTemporalIdx < iCntTid)
        {
            int32_t kbBaseTemporalFlag = (iTemporalIdx == 0) ? 1 : 0;

            iSpatialIdx = 0;
            while (iSpatialIdx < iCountLayersNeedCs[kbBaseTemporalFlag])
            {
                int32_t kiActualSpatialIdx = iMapSpatialIdx[iSpatialIdx * 2 + kbBaseTemporalFlag];
                int32_t kiLumaWidth = iLineSizeY[kiActualSpatialIdx * 2 + kbBaseTemporalFlag];
                int32_t kiChromaWidth = iLineSizeUV[kiActualSpatialIdx * 2 + kbBaseTemporalFlag];

                WelsGetEncBlockStrideOffset((int32_t*)pBaseDec, kiLumaWidth, kiChromaWidth);

                pPtr->pStrideDecBlockOffset[kiActualSpatialIdx][kbBaseTemporalFlag] = (int32_t*)pBaseDec;
                pBaseDec += kiUnit1Size;

                ++iSpatialIdx;
            }
            ++iTemporalIdx;
        }
        iTemporalIdx = 0;
        while (iTemporalIdx < iCntTid)
        {
            int32_t kbBaseTemporalFlag = (iTemporalIdx == 0) ? 1 : 0;

            iSpatialIdx = 0;
            while (iSpatialIdx < kiNumSpatialLayers)
            {
                int32_t iMatchIndex = 0;
                bool bInMap = false;
                bool bMatchFlag = false;

                i = 0;
                while (i < iCountLayersNeedCs[kbBaseTemporalFlag])
                {
                    int32_t kiActualIdx = iMapSpatialIdx[i * 2 + kbBaseTemporalFlag];
                    if (kiActualIdx == iSpatialIdx)
                    {
                        bInMap = true;
                        break;
                    }
                    if (!bMatchFlag)
                    {
                        iMatchIndex = kiActualIdx;
                        bMatchFlag = true;
                    }
                    ++i;
                }

                if (bInMap)
                {
                    ++iSpatialIdx;
                    continue;
                }

                // not in spatial map and assign match one to it
                pPtr->pStrideDecBlockOffset[iSpatialIdx][kbBaseTemporalFlag] =
                  pPtr->pStrideDecBlockOffset[iMatchIndex][kbBaseTemporalFlag];

                ++iSpatialIdx;
            }
            ++iTemporalIdx;
        }

        iSpatialIdx = 0;
        while (iSpatialIdx < kiNumSpatialLayers)
        {
            int32_t kiAllocMbSize = sMbSizeMap[iSpatialIdx].iSizeAllMbAlignCache;

            pPtr->pStrideEncBlockOffset[iSpatialIdx] = (int32_t*)pBaseEnc;

            pPtr->pMbIndexX[iSpatialIdx] = (int16_t*)pBaseMbX;
            pPtr->pMbIndexY[iSpatialIdx] = (int16_t*)pBaseMbY;

            pBaseEnc += kiUnit1Size;
            pBaseMbX += kiAllocMbSize;
            pBaseMbY += kiAllocMbSize;

            ++iSpatialIdx;
        }

        while (iSpatialIdx < MAX_DEPENDENCY_LAYER)
        {
            pPtr->pStrideDecBlockOffset[iSpatialIdx][0] = null;
            pPtr->pStrideDecBlockOffset[iSpatialIdx][1] = null;
            pPtr->pStrideEncBlockOffset[iSpatialIdx] = null;
            pPtr->pMbIndexX[iSpatialIdx] = null;
            pPtr->pMbIndexY[iSpatialIdx] = null;

            ++iSpatialIdx;
        }

        // initialize pMbIndexX and pMbIndexY tables as below

        iMaxMbWidth = (int16_t)sMbSizeMap[kiNumSpatialLayers - 1].iMbWidth;
        iMaxMbWidth = (int16_t)WELS_ALIGN(iMaxMbWidth, 4);  // 4 loops for int16_t required introduced as below
        iRowSize = iMaxMbWidth * sizeof(int16_t);

        pTmpRow = (int16_t*)pMa->WelsMallocz((uint32_t)iRowSize, "pTmpRow");
        if (null == pTmpRow)
        {
            return 1;
        }
        pRowX = pTmpRow;
        pRowY = pRowX;
        // initialize pRowX & pRowY
        i = 0;
        p = pRowX;
        while (i < iMaxMbWidth)
        {
            *p = (int16_t)i;
            *(p + 1) = (int16_t)(1 + i);
            *(p + 2) = (int16_t)(2 + i);
            *(p + 3) = (int16_t)(3 + i);

            p += 4;
            i += 4;
        }

        iSpatialIdx = kiNumSpatialLayers;
        while (--iSpatialIdx >= 0)
        {
            int16_t* pMbIndexX = pPtr->pMbIndexX[iSpatialIdx];
            int32_t kiMbWidth = sMbSizeMap[iSpatialIdx].iMbWidth;
            int32_t kiMbHeight = sMbSizeMap[iSpatialIdx].iCountMbNum / kiMbWidth;
            int32_t kiLineSize = kiMbWidth * sizeof(int16_t);

            i = 0;
            while (i < kiMbHeight)
            {
                memcpy(pMbIndexX, pRowX, kiLineSize); // confirmed_safe_unsafe_usage

                pMbIndexX += kiMbWidth;
                ++i;
            }
        }

        memset(pRowY, 0, iRowSize);
        iMaxMbHeight = (int16_t)(sMbSizeMap[kiNumSpatialLayers - 1].iCountMbNum / sMbSizeMap[kiNumSpatialLayers - 1].iMbWidth);
        i = 0;
        int16_t* t = stackalloc int16_t[4];
        for (; ; )
        {
            int32_t t32 = 0;
            int16_t j = 0;

            for (iSpatialIdx = kiNumSpatialLayers - 1; iSpatialIdx >= 0; --iSpatialIdx)
            {
                int32_t kiMbWidth = sMbSizeMap[iSpatialIdx].iMbWidth;
                int32_t kiMbHeight = sMbSizeMap[iSpatialIdx].iCountMbNum / kiMbWidth;
                int32_t kiLineSize = kiMbWidth * sizeof(int16_t);
                int16_t* pMbIndexY = pPtr->pMbIndexY[iSpatialIdx] + i * kiMbWidth;

                if (i < kiMbHeight)
                {
                    memcpy(pMbIndexY, pRowY, kiLineSize); // confirmed_safe_unsafe_usage
                }
            }
            ++i;
            if (i >= iMaxMbHeight)
                break;

            t32 = i | (i << 16);
            ST32(t, (uint32_t)t32);
            ST32(t + 2, (uint32_t)t32);

            p = pRowY;
            while (j < iMaxMbWidth)
            {
                ST64(p, LD64(t));

                p += 4;
                j += 4;
            }
        }

        pMa->WelsFree(pTmpRow, "pTmpRow");
        pTmpRow = null;

        return 0;
    }

    // E5b: GetMvMvdRange (encoder_ext.cpp:1510), which RequestMemorySvc sizes the MVD cost table with.
    // RequestMemoryVaaScreen and ReleaseMemoryVaaScreen come with screen content (Phase 4).

    public static void GetMvMvdRange(SWelsSvcCodingParam* pParam, ref int32_t iMvRange, ref int32_t iMvdRange)
    {
        ELevelIdc iMinLevelIdc = LEVEL_5_2;
        int32_t iMinMv = 0;
        int32_t iMaxMv = 0;
        int32_t iFixMvRange = pParam->iUsageType != 0 ? EXPANDED_MV_RANGE : CAMERA_STARTMV_RANGE;
        int32_t iFixMvdRange = (pParam->iUsageType != 0 ? EXPANDED_MVD_RANGE : ((pParam->iSpatialLayerNum == 1) ? CAMERA_MVD_RANGE :
                                CAMERA_HIGHLAYER_MVD_RANGE));
        for (int32_t iLayer = 0; iLayer < pParam->iSpatialLayerNum; iLayer++)
        {
            if (pParam->sSpatialLayers[iLayer].uiLevelIdc < iMinLevelIdc)
            {
                iMinLevelIdc = pParam->sSpatialLayers[iLayer].uiLevelIdc;
            }
        }
        SLevelLimits* pLevelLimit = g_ksLevelLimits;
        while ((pLevelLimit->uiLevelIdc != LEVEL_5_2) && (pLevelLimit->uiLevelIdc != iMinLevelIdc))
        {
            pLevelLimit++;
        }
        iMinMv = (pLevelLimit->iMinVmv) >> 2;
        iMaxMv = (pLevelLimit->iMaxVmv) >> 2;

        iMvRange = WELS_MIN(WELS_ABS(iMinMv), iMaxMv);

        iMvRange = WELS_MIN(iMvRange, iFixMvRange);

        iMvdRange = (iMvRange + 1) << 1;

        iMvdRange = WELS_MIN(iMvdRange, iFixMvdRange);
    }

    public static void SetFastCodingFunc(SWelsFuncPtrList* pFuncList)
    {
        pFuncList->pfIntraFineMd = &WelsMdIntraFinePartitionVaa;
        pFuncList->sSampleDealingFuncs.pfMdCost = pFuncList->sSampleDealingFuncs.pfSampleSad;
        pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3 = pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3Sad;
        pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3 = pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3Sad;
    }

    public static void SetNormalCodingFunc(SWelsFuncPtrList* pFuncList)
    {
        pFuncList->pfIntraFineMd = &WelsMdIntraFinePartition;
        pFuncList->sSampleDealingFuncs.pfMdCost = pFuncList->sSampleDealingFuncs.pfSampleSatd;
        pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3 =
          pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3Satd;
        pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3 =
          pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3Satd;
        pFuncList->sSampleDealingFuncs.pfIntra4x4Combined3 =
          pFuncList->sSampleDealingFuncs.pfIntra4x4Combined3Satd;
    }

    // E4b: the layer and macroblock list set up (InitMbInfo .. InitDqLayers, encoder_ext.cpp:837-1225),
    // GetTemporalLevel (:2395), WelsInitCurrentLayer (:2536), PreprocessSliceCoding (:2667),
    // WelsSwapDqLayers (:2799) and PrefetchReferencePicture (:2810). AcquireLayersNals and
    // RequestMemorySvc come with E8.

    // static void InitMbInfo: public so the tests can run it.
    public static void InitMbInfo(sWelsEncCtx* pEnc, SMB* pList, SDqLayer* pLayer, int32_t kiDlayerId,
                                  int32_t kiMaxMbNum)
    {
        int32_t iMbWidth = pLayer->iMbWidth;
        int32_t iMbHeight = pLayer->iMbHeight;
        int32_t iIdx;
        int32_t iMbNum = iMbWidth * iMbHeight;
        uint32_t uiNeighborAvail;
        int32_t kiOffset = (kiDlayerId & 0x01) * kiMaxMbNum;
        // SMVUnitXY (*pLayerMvUnitBlock4x4)[MB_BLOCK4x4_NUM]
        SMVUnitXY* pLayerMvUnitBlock4x4 = &pEnc->pMvUnitBlock4x4[MB_BLOCK4x4_NUM * kiOffset];
        // int8_t (*pLayerRefIndexBlock8x8)[MB_BLOCK8x8_NUM]
        int8_t* pLayerRefIndexBlock8x8 = &pEnc->pRefIndexBlock4x4[MB_BLOCK8x8_NUM * kiOffset];

        for (iIdx = 0; iIdx < iMbNum; iIdx++)
        {
            bool bLeft;
            bool bTop;
            bool bLeftTop;
            bool bRightTop;
            int32_t iLeftXY, iTopXY, iLeftTopXY, iRightTopXY;
            uint16_t uiSliceIdc; //[0..65535] > 36864 of LEVEL5.2

            pList[iIdx].iMbX = ((int16_t*)pEnc->pStrideTab->pMbIndexX[kiDlayerId])[iIdx];
            pList[iIdx].iMbY = ((int16_t*)pEnc->pStrideTab->pMbIndexY[kiDlayerId])[iIdx];
            pList[iIdx].iMbXY = iIdx;

            uiSliceIdc = WelsMbToSliceIdc(pLayer, iIdx);
            iLeftXY = iIdx - 1;
            iTopXY = iIdx - iMbWidth;
            iLeftTopXY = iTopXY - 1;
            iRightTopXY = iTopXY + 1;

            bLeft = (pList[iIdx].iMbX > 0) && (uiSliceIdc == WelsMbToSliceIdc(pLayer, iLeftXY));
            bTop = (pList[iIdx].iMbY > 0) && (uiSliceIdc == WelsMbToSliceIdc(pLayer, iTopXY));
            bLeftTop = (pList[iIdx].iMbX > 0) && (pList[iIdx].iMbY > 0) && (uiSliceIdc ==
                       WelsMbToSliceIdc(pLayer, iLeftTopXY));
            bRightTop = (pList[iIdx].iMbX < (iMbWidth - 1)) && (pList[iIdx].iMbY > 0) && (uiSliceIdc ==
                        WelsMbToSliceIdc(pLayer, iRightTopXY));

            uiNeighborAvail = 0;
            if (bLeft)
            {
                uiNeighborAvail |= LEFT_MB_POS;
            }
            if (bTop)
            {
                uiNeighborAvail |= TOP_MB_POS;
            }
            if (bLeftTop)
            {
                uiNeighborAvail |= TOPLEFT_MB_POS;
            }
            if (bRightTop)
            {
                uiNeighborAvail |= TOPRIGHT_MB_POS;
            }
            pList[iIdx].uiSliceIdc = uiSliceIdc; // merge from svc_hd_opt_b for multiple slices coding
            pList[iIdx].uiNeighborAvail = (uint8_t)uiNeighborAvail;
            // The base layer motion vector availability below is computed and then dropped upstream too.
            uiNeighborAvail = 0;
            if (pList[iIdx].iMbX >= BASE_MV_MB_NMB)
            {
                uiNeighborAvail |= LEFT_MB_POS;
            }
            if (pList[iIdx].iMbX <= (iMbWidth - 1 - BASE_MV_MB_NMB))
            {
                uiNeighborAvail |= RIGHT_MB_POS;
            }
            if (pList[iIdx].iMbY >= BASE_MV_MB_NMB)
            {
                uiNeighborAvail |= TOP_MB_POS;
            }
            if (pList[iIdx].iMbY <= (iMbHeight - 1 - BASE_MV_MB_NMB))
            {
                uiNeighborAvail |= BOTTOM_MB_POS;
            }

            pList[iIdx].sMv = &pLayerMvUnitBlock4x4[iIdx * MB_BLOCK4x4_NUM];
            pList[iIdx].pRefIndex = &pLayerRefIndexBlock8x8[iIdx * MB_BLOCK8x8_NUM];
            pList[iIdx].pSadCost = &pEnc->pSadCostMb[iIdx];
            pList[iIdx].pIntra4x4PredMode = &pEnc->pIntra4x4PredModeBlocks[iIdx * INTRA_4x4_MODE_NUM];
            pList[iIdx].pNonZeroCount = &pEnc->pNonZeroCountBlocks[iIdx * MB_LUMA_CHROMA_BLOCK4x4_NUM];
        }
    }

    public static int32_t InitMbListD(sWelsEncCtx** ppCtx)
    {
        int32_t iNumDlayer = (*ppCtx)->pSvcParam->iSpatialLayerNum;
        int32_t* iMbSize = stackalloc int32_t[MAX_DEPENDENCY_LAYER]; // = { 0 }; stackalloc zeroes it
        int32_t iOverallMbNum = 0;
        int32_t iMbWidth = 0;
        int32_t iMbHeight = 0;
        int32_t i;

        if (iNumDlayer > MAX_DEPENDENCY_LAYER)
        {
            return 1;
        }

        for (i = 0; i < iNumDlayer; i++)
        {
            iMbWidth = ((*ppCtx)->pSvcParam->sSpatialLayers[i].iVideoWidth + 15) >> 4;
            iMbHeight = ((*ppCtx)->pSvcParam->sSpatialLayers[i].iVideoHeight + 15) >> 4;
            iMbSize[i] = iMbWidth * iMbHeight;
            iOverallMbNum += iMbSize[i];
        }

        (*ppCtx)->ppMbListD = (SMB**)(*ppCtx)->pMemAlign->WelsMallocz((uint32_t)(iNumDlayer * sizeof(SMB*)), "ppMbListD");
        // Upstream writes ppMbListD[0] = NULL before this check, which faults on a failed allocation.
        if ((*ppCtx)->ppMbListD == null)
        {
            return 1;
        }
        (*ppCtx)->ppMbListD[0] = null;
        (*ppCtx)->ppMbListD[0] = (SMB*)(*ppCtx)->pMemAlign->WelsMallocz((uint32_t)(iOverallMbNum * sizeof(SMB)),
                                 "ppMbListD[0]");
        if ((*ppCtx)->ppMbListD[0] == null)
        {
            return 1;
        }
        (*ppCtx)->ppDqLayerList[0]->sMbDataP = (*ppCtx)->ppMbListD[0];
        InitMbInfo(*ppCtx, (*ppCtx)->ppMbListD[0], (*ppCtx)->ppDqLayerList[0], 0, iMbSize[iNumDlayer - 1]);
        for (i = 1; i < iNumDlayer; i++)
        {
            (*ppCtx)->ppMbListD[i] = (*ppCtx)->ppMbListD[i - 1] + iMbSize[i - 1];
            (*ppCtx)->ppDqLayerList[i]->sMbDataP = (*ppCtx)->ppMbListD[i];
            InitMbInfo(*ppCtx, (*ppCtx)->ppMbListD[i], (*ppCtx)->ppDqLayerList[i], i, iMbSize[iNumDlayer - 1]);
        }

        return 0;
    }

    public static void FreeSliceInLayer(SDqLayer* pDq, CMemoryAlign* pMa)
    {
        int32_t iIdx = 0;
        for (; iIdx < MAX_THREADS_NUM; iIdx++)
        {
            SSlice* pSliceBuffer = pDq->sSliceBufferInfo[iIdx].pSliceBuffer;
            FreeSliceBuffer(&pSliceBuffer,
                            pDq->sSliceBufferInfo[iIdx].iMaxSliceNum,
                            pMa, "pSliceBuffer");
            pDq->sSliceBufferInfo[iIdx].pSliceBuffer = pSliceBuffer;
        }
    }

    // SDqLayer*& pDq is SDqLayer** here.
    public static void FreeDqLayer(SDqLayer** pDq, CMemoryAlign* pMa)
    {
        if (null == *pDq)
        {
            return;
        }

        FreeSliceInLayer(*pDq, pMa);

        if ((*pDq)->ppSliceInLayer != null)
        {
            pMa->WelsFree((*pDq)->ppSliceInLayer, "ppSliceInLayer");
            (*pDq)->ppSliceInLayer = null;
        }

        if ((*pDq)->pFirstMbIdxOfSlice != null)
        {
            pMa->WelsFree((*pDq)->pFirstMbIdxOfSlice, "pFirstMbIdxOfSlice");
            (*pDq)->pFirstMbIdxOfSlice = null;
        }

        if ((*pDq)->pCountMbNumInSlice != null)
        {
            pMa->WelsFree((*pDq)->pCountMbNumInSlice, "pCountMbNumInSlice");
            (*pDq)->pCountMbNumInSlice = null;
        }

        // pFeatureSearchPreparation (screen content) comes with Phase 4.

        UninitSlicePEncCtx(*pDq, pMa);
        (*pDq)->iMaxSliceNum = 0;

        pMa->WelsFree(*pDq, "pDqLayer");
        *pDq = null;
    }

    // SRefList*& pRefList is SRefList** here.
    public static void FreeRefList(SRefList** pRefList, CMemoryAlign* pMa, int iMaxNumRefFrame)
    {
        if (null == *pRefList)
        {
            return;
        }

        int32_t iRef = 0;
        do
        {
            if (!(*pRefList)->pRef[iRef].IsNull)
            {
                SPicture* pRef = (*pRefList)->pRef[iRef];
                FreePicture(pMa, &pRef);
                (*pRefList)->pRef[iRef] = pRef;
            }
            ++iRef;
        } while (iRef < 1 + iMaxNumRefFrame);

        pMa->WelsFree(*pRefList, "pRefList");
        *pRefList = null;
    }

    /*!
     * \brief   initialize ppDqLayerList and slicepEncCtx_list due to count number of layers available
     * \pParam  pCtx            sWelsEncCtx*
     * \return  0 - successful; otherwise failed
     */
    // static inline int32_t InitDqLayers: public so the tests can run it.
    public static int32_t InitDqLayers(sWelsEncCtx** ppCtx, SExistingParasetList* pExistingParasetList)
    {
        SWelsSvcCodingParam* pParam = null;
        SWelsSPS* pSps = null;
        SSubsetSps* pSubsetSps = null;
        SWelsPPS* pPps = null;
        CMemoryAlign* pMa = null;
        int32_t iDlayerCount = 0;
        int32_t iDlayerIndex = 0;
        int32_t iSpsId = 0;
        uint32_t iPpsId = 0;
        uint32_t iNumRef = 0;
        int32_t iResult = 0;

        if (null == ppCtx || null == *ppCtx)
        {
            return 1;
        }

        pMa = (*ppCtx)->pMemAlign;
        pParam = (*ppCtx)->pSvcParam;
        iDlayerCount = pParam->iSpatialLayerNum;
        iNumRef = (uint32_t)pParam->iMaxNumRefFrame;

        int32_t kiFeatureStrategyIndex = FME_DEFAULT_FEATURE_INDEX;
        int32_t kiMe16x16 = ME_DIA_CROSS;
        int32_t kiMe8x8 = ME_DIA_CROSS_FME;
        int32_t kiNeedFeatureStorage = (pParam->iUsageType != SCREEN_CONTENT_REAL_TIME) ? 0 :
                                       ((kiFeatureStrategyIndex << 16) + ((kiMe16x16 & 0x00FF) << 8) + (kiMe8x8 & 0x00FF));
        if (kiNeedFeatureStorage != 0)
        {
            // The feature storage and pFeatureSearchPreparation (screen content) come with Phase 4.
            throw new NotSupportedException("InitDqLayers: screen content coding is not ported yet.");
        }

        iDlayerIndex = 0;
        while (iDlayerIndex < iDlayerCount)
        {
            SRefList* pRefList = null;
            uint32_t i = 0;
            int32_t kiWidth = pParam->sSpatialLayers[iDlayerIndex].iVideoWidth;
            int32_t kiHeight = pParam->sSpatialLayers[iDlayerIndex].iVideoHeight;
            int32_t iPicWidth = WELS_ALIGN(kiWidth, MB_WIDTH_LUMA) + (PADDING_LENGTH << 1);  // with iWidth of horizon
            int32_t iPicChromaWidth = iPicWidth >> 1;

            iPicWidth = WELS_ALIGN(iPicWidth,
                                   32); // 32(or 16 for chroma below) to match original imp. here instead of iCacheLineSize
            iPicChromaWidth = WELS_ALIGN(iPicChromaWidth, 16);

            WelsGetEncBlockStrideOffset((*ppCtx)->pStrideTab->pStrideEncBlockOffset[iDlayerIndex], iPicWidth, iPicChromaWidth);

            // pRef list
            pRefList = (SRefList*)pMa->WelsMallocz((uint32_t)sizeof(SRefList), "pRefList");
            if (null == pRefList)
            {
                return 1;
            }
            do
            {
                pRefList->pRef[(int)i] = AllocPicture(pMa, kiWidth, kiHeight, true,
                                                      (iDlayerIndex == iDlayerCount - 1) ? kiNeedFeatureStorage : 0); // to use actual size of current layer
                if (pRefList->pRef[(int)i].IsNull)
                {
                    FreeRefList(&pRefList, pMa, (int)iNumRef);
                    return 1;
                }
                ++i;
            } while (i < 1 + iNumRef);

            pRefList->pNextBuffer = pRefList->pRef[0];
            (*ppCtx)->ppRefPicListExt[iDlayerIndex] = pRefList;
            ++iDlayerIndex;
        }

        iDlayerIndex = 0;
        while (iDlayerIndex < iDlayerCount)
        {
            SDqLayer* pDqLayer = null;
            SSpatialLayerConfig* pDlayer = &pParam->sSpatialLayers[iDlayerIndex];
            SSpatialLayerInternal* pParamInternal = &pParam->sDependencyLayers[iDlayerIndex];
            int32_t kiMbW = (pDlayer->iVideoWidth + 0x0f) >> 4;
            int32_t kiMbH = (pDlayer->iVideoHeight + 0x0f) >> 4;

            pParamInternal->iCodingIndex = 0;
            pParamInternal->iFrameIndex = 0;
            pParamInternal->iFrameNum = 0;
            pParamInternal->iPOC = 0;
            pParamInternal->uiIdrPicId = 0;
            pParamInternal->bEncCurFrmAsIdrFlag = true;  // make sure first frame is IDR
            // pDq layers list
            pDqLayer = (SDqLayer*)pMa->WelsMallocz((uint32_t)sizeof(SDqLayer), "pDqLayer");
            if (null == pDqLayer)
            {
                FreeDqLayer(&pDqLayer, pMa);
                return 1;
            }

            pDqLayer->bNeedAdjustingSlicing = false;

            pDqLayer->iMbWidth = (int16_t)kiMbW;
            pDqLayer->iMbHeight = (int16_t)kiMbH;

            int32_t iMaxSliceNum = 1;
            int32_t kiSliceNum = GetInitialSliceNum(&pDlayer->sSliceArgument);
            if (iMaxSliceNum < kiSliceNum)
            {
                iMaxSliceNum = kiSliceNum;
            }
            pDqLayer->iMaxSliceNum = iMaxSliceNum;

            iResult = InitSliceInLayer(*ppCtx, pDqLayer, iDlayerIndex, pMa);
            if (iResult != 0)
            {
                WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_WARNING, $"InitDqLayers(), InitSliceInLayer failed({iResult})!");
                FreeDqLayer(&pDqLayer, pMa);
                return iResult;
            }

            //deblocking parameters initialization
            //target-layer deblocking
            pDqLayer->iLoopFilterDisableIdc = (uint8_t)pParam->iLoopFilterDisableIdc;
            pDqLayer->iLoopFilterAlphaC0Offset = (int8_t)((pParam->iLoopFilterAlphaC0Offset) << 1);
            pDqLayer->iLoopFilterBetaOffset = (int8_t)((pParam->iLoopFilterBetaOffset) << 1);
            //parallel deblocking
            pDqLayer->bDeblockingParallelFlag = pParam->bDeblockingParallelFlag;

            //deblocking parameter adjustment
            if (SM_SINGLE_SLICE == pDlayer->sSliceArgument.uiSliceMode)
            {
                //iLoopFilterDisableIdc: will be 0 or 1 under single_slice
                if (2 == pParam->iLoopFilterDisableIdc)
                {
                    pDqLayer->iLoopFilterDisableIdc = 0;
                }
                //bDeblockingParallelFlag
                pDqLayer->bDeblockingParallelFlag = false;
            }
            else
            {
                //multi-pSlice
                if (0 == pDqLayer->iLoopFilterDisableIdc)
                {
                    pDqLayer->bDeblockingParallelFlag = false;
                }
            }

            // if (kiNeedFeatureStorage && iDlayerIndex == iDlayerCount - 1) { pFeatureSearchPreparation } comes
            // with Phase 4 (rejected above); otherwise pFeatureSearchPreparation = NULL.

            (*ppCtx)->ppDqLayerList[iDlayerIndex] = pDqLayer;

            ++iDlayerIndex;
        }

        // for dynamically malloc for parameter sets memory instead of maximal items for standard to reduce size, 3/18/2010
        if (null == (*ppCtx)->pFuncList)
        {
            return 1;
        }
        if (null == (*ppCtx)->pFuncList->pParametersetStrategy)
        {
            return 1;
        }
        int32_t kiNeededSpsNum = (int32_t)(*ppCtx)->pFuncList->pParametersetStrategy->GetNeededSpsNum();
        int32_t kiNeededSubsetSpsNum = (int32_t)(*ppCtx)->pFuncList->pParametersetStrategy->GetNeededSubsetSpsNum();
        (*ppCtx)->pSpsArray = (SWelsSPS*)pMa->WelsMallocz((uint32_t)(kiNeededSpsNum * sizeof(SWelsSPS)), "pSpsArray");
        if (null == (*ppCtx)->pSpsArray)
        {
            return 1;
        }
        if (kiNeededSubsetSpsNum > 0)
        {
            (*ppCtx)->pSubsetArray = (SSubsetSps*)pMa->WelsMallocz((uint32_t)(kiNeededSubsetSpsNum * sizeof(SSubsetSps)), "pSubsetArray");
            if (null == (*ppCtx)->pSubsetArray)
            {
                return 1;
            }
        }
        else
        {
            (*ppCtx)->pSubsetArray = null;
        }

        // PPS
        int32_t kiNeededPpsNum = (int32_t)(*ppCtx)->pFuncList->pParametersetStrategy->GetNeededPpsNum();
        (*ppCtx)->pPPSArray = (SWelsPPS*)pMa->WelsMallocz((uint32_t)(kiNeededPpsNum * sizeof(SWelsPPS)), "pPPSArray");
        if (null == (*ppCtx)->pPPSArray)
        {
            return 1;
        }

        (*ppCtx)->pFuncList->pParametersetStrategy->LoadPrevious(pExistingParasetList, (*ppCtx)->pSpsArray,
            (*ppCtx)->pSubsetArray, (*ppCtx)->pPPSArray);


        (*ppCtx)->pDqIdcMap = (SDqIdc*)pMa->WelsMallocz((uint32_t)(iDlayerCount * sizeof(SDqIdc)), "pDqIdcMap");
        if (null == (*ppCtx)->pDqIdcMap)
        {
            return 1;
        }

        iDlayerIndex = 0;
        while (iDlayerIndex < iDlayerCount)
        {
            SDqIdc* pDqIdc = &(*ppCtx)->pDqIdcMap[iDlayerIndex];
            bool bUseSubsetSps = (!pParam->bSimulcastAVC) && (iDlayerIndex > BASE_DEPENDENCY_ID);
            SSpatialLayerConfig* pDlayerParam = &pParam->sSpatialLayers[iDlayerIndex];
            bool bSvcBaselayer = (!pParam->bSimulcastAVC) && (iDlayerCount > BASE_DEPENDENCY_ID)
                                 && (iDlayerIndex == BASE_DEPENDENCY_ID);
            pDqIdc->uiSpatialId = (int8_t)iDlayerIndex;

            iSpsId = (int32_t)(*ppCtx)->pFuncList->pParametersetStrategy->GenerateNewSps(*ppCtx, bUseSubsetSps, iDlayerIndex,
                     iDlayerCount, (uint32_t)iSpsId, ref pSps, ref pSubsetSps, bSvcBaselayer);
            if (0 > iSpsId)
            {
                return ENC_RETURN_UNSUPPORTED_PARA;
            }
            if (!bUseSubsetSps)
            {
                pSps = &((*ppCtx)->pSpsArray[iSpsId]);
            }
            else
            {
                pSubsetSps = &((*ppCtx)->pSubsetArray[iSpsId]);
            }

            iPpsId = (*ppCtx)->pFuncList->pParametersetStrategy->InitPps((*ppCtx), (uint32_t)iSpsId, pSps, pSubsetSps, iPpsId, true,
                     bUseSubsetSps, pParam->iEntropyCodingModeFlag != 0);
            pPps = &((*ppCtx)->pPPSArray[iPpsId]);

            // Not using FMO in SVC coding so far, come back if need FMO
            {
                iResult = InitSlicePEncCtx((*ppCtx)->ppDqLayerList[iDlayerIndex],
                                           (*ppCtx)->pMemAlign,
                                           false,
                                           pSps->iMbWidth,
                                           pSps->iMbHeight,
                                           &(pDlayerParam->sSliceArgument),
                                           pPps);
                if (iResult != 0)
                {
                    WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_WARNING, $"InitDqLayers(), InitSlicePEncCtx failed({iResult})!");
                    return iResult;
                }
            }
            pDqIdc->iSpsId = (uint8_t)iSpsId;
            pDqIdc->iPpsId = (uint16_t)iPpsId;

            if ((pParam->bSimulcastAVC) || (bUseSubsetSps))
            {
                ++iSpsId;
            }
            ++iPpsId;
            if (bUseSubsetSps)
            {
                ++(*ppCtx)->iSubsetSpsNum;
            }
            else
            {
                ++(*ppCtx)->iSpsNum;
            }
            ++(*ppCtx)->iPpsNum;

            ++iDlayerIndex;
        }

        (*ppCtx)->pFuncList->pParametersetStrategy->UpdateParaSetNum((*ppCtx));
        return ENC_RETURN_SUCCESS;
    }

    public static int32_t GetTemporalLevel(SSpatialLayerInternal* fDlp, int32_t kiFrameNum, int32_t kiGopSize)
    {
        int32_t kiCodingIdx = kiFrameNum & (kiGopSize - 1);

        return fDlp->uiCodingIdx2TemporalId[kiCodingIdx];
    }

    public static void WelsInitCurrentLayer(sWelsEncCtx* pCtx,
                                            int32_t kiWidth,
                                            int32_t kiHeight)
    {
        SWelsSvcCodingParam* pParam = pCtx->pSvcParam;
        SPicture* pEncPic = pCtx->pEncPic;
        SPicture* pDecPic = pCtx->pDecPic;
        SDqLayer* pCurDq = pCtx->pCurDqLayer;
        SSlice* pBaseSlice = pCurDq->ppSliceInLayer[0];
        uint8_t kiCurDid = pCtx->uiDependencyId;
        bool kbUseSubsetSpsFlag = (!pParam->bSimulcastAVC) && (kiCurDid > BASE_DEPENDENCY_ID);
        SNalUnitHeaderExt* pNalHdExt = &pCurDq->sLayerInfo.sNalHeaderExt;
        SNalUnitHeader* pNalHd = &pNalHdExt->sNalUnitHeader;
        SDqIdc* pDqIdc = &pCtx->pDqIdcMap[kiCurDid];
        int32_t iIdx = 0;
        int32_t iSliceCount = pCurDq->iMaxSliceNum;
        SSpatialLayerInternal* pParamInternal = &pParam->sDependencyLayers[kiCurDid];
        if (null == pCurDq || null == pBaseSlice)
        {
            return;
        }

        pCurDq->pDecPic = pDecPic;

        Debug.Assert(iSliceCount > 0);

        int32_t iCurPpsId = pDqIdc->iPpsId;
        int32_t iCurSpsId = pDqIdc->iSpsId;

        iCurPpsId = pCtx->pFuncList->pParametersetStrategy->GetCurrentPpsId(iCurPpsId,
                    WELS_ABS(pParamInternal->uiIdrPicId - 1) % MAX_PPS_COUNT);

        pBaseSlice->sSliceHeaderExt.sSliceHeader.iPpsId = iCurPpsId;
        pCurDq->sLayerInfo.pPpsP =
            pBaseSlice->sSliceHeaderExt.sSliceHeader.pPps = &pCtx->pPPSArray[iCurPpsId];

        pBaseSlice->sSliceHeaderExt.sSliceHeader.iSpsId = iCurSpsId;
        if (kbUseSubsetSpsFlag)
        {
            pCurDq->sLayerInfo.pSubsetSpsP = &pCtx->pSubsetArray[iCurSpsId];
            pCurDq->sLayerInfo.pSpsP =
                pBaseSlice->sSliceHeaderExt.sSliceHeader.pSps = &pCurDq->sLayerInfo.pSubsetSpsP->pSps;
        }
        else
        {
            pCurDq->sLayerInfo.pSubsetSpsP = null;
            pCurDq->sLayerInfo.pSpsP =
                pBaseSlice->sSliceHeaderExt.sSliceHeader.pSps = &pCtx->pSpsArray[iCurSpsId];
        }

        pBaseSlice->bSliceHeaderExtFlag = (NAL_UNIT_CODED_SLICE_EXT == pCtx->eNalType);

        iIdx = 1;
        while (iIdx < iSliceCount)
        {
            InitSliceHeadWithBase(pCurDq->ppSliceInLayer[iIdx], pBaseSlice);
            ++iIdx;
        }

        *pNalHdExt = default; // memset (pNalHdExt, 0, sizeof (SNalUnitHeaderExt));
        pNalHd->uiNalRefIdc = (uint8_t)pCtx->eNalPriority;
        pNalHd->eNalUnitType = pCtx->eNalType;

        pNalHdExt->uiDependencyId = kiCurDid;
        pNalHdExt->bDiscardableFlag = (pCtx->bNeedPrefixNalFlag) ? (pNalHd->uiNalRefIdc == (uint8_t)NRI_PRI_LOWEST) : false;
        pNalHdExt->bIdrFlag = (pParamInternal->iFrameNum == 0)
                              && ((pCtx->eNalType == NAL_UNIT_CODED_SLICE_IDR)
                                  || (pCtx->eSliceType == I_SLICE));
        pNalHdExt->uiTemporalId = pCtx->uiTemporalId;

        // pEncPic pData
        pCurDq->pEncData[0] = pEncPic->pData[0];
        pCurDq->pEncData[1] = pEncPic->pData[1];
        pCurDq->pEncData[2] = pEncPic->pData[2];
        pCurDq->iEncStride[0] = pEncPic->iLineSize[0];
        pCurDq->iEncStride[1] = pEncPic->iLineSize[1];
        pCurDq->iEncStride[2] = pEncPic->iLineSize[2];
        // cs pData
        pCurDq->pCsData[0] = pDecPic->pData[0];
        pCurDq->pCsData[1] = pDecPic->pData[1];
        pCurDq->pCsData[2] = pDecPic->pData[2];
        pCurDq->iCsStride[0] = pDecPic->iLineSize[0];
        pCurDq->iCsStride[1] = pDecPic->iLineSize[1];
        pCurDq->iCsStride[2] = pDecPic->iLineSize[2];

        if (pCurDq->pRefLayer != null)
        {
            pCurDq->bBaseLayerAvailableFlag = true;
        }
        else
        {
            pCurDq->bBaseLayerAvailableFlag = false;
        }

        // if (pCtx->pTaskManage) pCtx->pTaskManage->InitFrame (kiCurDid): the task manager comes with the
        // threading work.
    }

    public static void PreprocessSliceCoding(sWelsEncCtx* pCtx)
    {
        SDqLayer* pCurLayer = pCtx->pCurDqLayer;
        //const bool kbBaseAvail      = pCurLayer->bBaseLayerAvailableFlag;
        bool bFastMode = (pCtx->pSvcParam->iComplexityMode == LOW_COMPLEXITY);
        SWelsFuncPtrList* pFuncList = pCtx->pFuncList;
        /* function pointers conditional assignment under sWelsEncCtx, layer_mb_enc_rec (in stack) is exclusive */
        if ((pCtx->pSvcParam->iUsageType == CAMERA_VIDEO_REAL_TIME && bFastMode) ||
            (pCtx->pSvcParam->iUsageType == SCREEN_CONTENT_REAL_TIME && P_SLICE == pCtx->eSliceType
             && bFastMode) //TODO: here is for sync with the origin code, consider the design again with more tests
           )
        {
            SetFastCodingFunc(pFuncList);
        }
        else
        {
            SetNormalCodingFunc(pFuncList);
        }

        if (P_SLICE == pCtx->eSliceType)
        {
            for (int i = 0; i < (int)BLOCK_STATIC_IDC_ALL; i++)
            {
                pFuncList->pfMotionSearch[i] = &WelsMotionEstimateSearch;
            }
            pFuncList->pfSearchMethod[BLOCK_16x16] =
                pFuncList->pfSearchMethod[BLOCK_16x8] =
                    pFuncList->pfSearchMethod[BLOCK_8x16] =
                        pFuncList->pfSearchMethod[BLOCK_8x8] =
                            pFuncList->pfSearchMethod[BLOCK_4x4] =
                                pFuncList->pfSearchMethod[BLOCK_8x4] =
                                    pFuncList->pfSearchMethod[BLOCK_4x8] = &WelsDiamondSearch;
            pFuncList->pfFirstIntraMode = &WelsMdFirstIntraMode;
            pFuncList->sSampleDealingFuncs.pfMeCost = pCtx->pFuncList->sSampleDealingFuncs.pfSampleSatd;
            pFuncList->pfSetScrollingMv = &SetScrollingMvToMdNull;

            if (bFastMode)
            {
                pFuncList->pfCalculateSatd = &NotCalculateSatdCost;
                pFuncList->pfInterFineMd = &WelsMdInterFinePartitionVaa;
            }
            else
            {
                pFuncList->pfCalculateSatd = &CalculateSatdCost;
                pFuncList->pfInterFineMd = &WelsMdInterFinePartition;
            }
        }
        else
        {
            pFuncList->sSampleDealingFuncs.pfMeCost = null;
        }

        //to init at each frame will be needed when dealing with hybrid content (camera+screen)
        if (pCtx->pSvcParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            // The screen content pointers and the FME state come with Phase 4.
            throw new NotSupportedException("PreprocessSliceCoding: screen content coding is not ported yet.");
        }

        // update some layer dependent variable to save judgements in mb-level
        pCurLayer->bSatdInMdFlag = ((pFuncList->sSampleDealingFuncs.pfMeCost == pFuncList->sSampleDealingFuncs.pfSampleSatd)
                                    && (pFuncList->sSampleDealingFuncs.pfMdCost == pFuncList->sSampleDealingFuncs.pfSampleSatd));

        int32_t kiCurDid = pCtx->uiDependencyId;
        int32_t kiCurTid = pCtx->uiTemporalId;
        if (pCurLayer->bDeblockingParallelFlag && (pCurLayer->iLoopFilterDisableIdc != 1)
            // #if !defined(ENABLE_FRAME_DUMP)
            && (NRI_PRI_LOWEST != pCtx->eNalPriority)
            && (pCtx->pSvcParam->sDependencyLayers[kiCurDid].iHighestTemporalId == 0
                || kiCurTid < pCtx->pSvcParam->sDependencyLayers[kiCurDid].iHighestTemporalId)
            // #endif// !ENABLE_FRAME_DUMP
           )
        {
            pFuncList->pfDeblocking.pfDeblockingFilterSlice = &DeblockingFilterSliceAvcbase;
        }
        else
        {
            pFuncList->pfDeblocking.pfDeblockingFilterSlice = &DeblockingFilterSliceAvcbaseNull;
        }
    }

    /*!
     * \brief   swap pDq layers between current pDq layer and reference pDq layer
     */
    // static inline: public so the tests can run it.
    public static void WelsSwapDqLayers(sWelsEncCtx* pCtx, int32_t kiNextDqIdx)
    {
        // swap and assign reference
        SDqLayer* pTmpLayer = pCtx->ppDqLayerList[kiNextDqIdx];
        SDqLayer* pRefLayer = pCtx->pCurDqLayer;
        pCtx->pCurDqLayer = pTmpLayer;
        pCtx->pCurDqLayer->pRefLayer = pRefLayer;
    }

    /*!
     * \brief   prefetch reference picture after WelsBuildRefList
     */
    // static inline: public so the tests can run it.
    public static void PrefetchReferencePicture(sWelsEncCtx* pCtx, EVideoFrameType keFrameType)
    {
        int32_t kiSliceCount = pCtx->pCurDqLayer->iMaxSliceNum;
        int32_t iIdx = 0;
        uint8_t uiRefIdx = unchecked((uint8_t)(-1));

        Debug.Assert(kiSliceCount > 0);
        if (keFrameType != videoFrameTypeIDR)
        {
            Debug.Assert(pCtx->iNumRef0 > 0);
            pCtx->pRefPic = pCtx->pRefList0[0];   // always get item 0 due to reordering done
            pCtx->pCurDqLayer->pRefPic = pCtx->pRefPic;
            uiRefIdx = 0;                         // reordered reference iIndex
        }
        else
        { // safe for IDR coding
            pCtx->pRefPic = null;
            pCtx->pCurDqLayer->pRefPic = null;
        }

        iIdx = 0;
        while (iIdx < kiSliceCount)
        {
            pCtx->pCurDqLayer->ppSliceInLayer[iIdx]->sSliceHeaderExt.sSliceHeader.uiRefIndex = uiRefIdx;
            ++iIdx;
        }
    }
}
