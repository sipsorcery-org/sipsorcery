//-----------------------------------------------------------------------------
// Filename: encoder_ext.cs
//
// Description: Port of codec/encoder/core/src/encoder_ext.cpp, part 1 (E1):
// parameter validation (ParamValidation, ParamValidationExt and their
// helpers), the slice and thread settings applied at init
// (InitSliceSettings, GetMultipleThreadIdc) and the SPS/PPS writers
// (WelsWriteOneSPS/PPS, WelsWriteParameterSets,
// WelsEncoderEncodeParameterSets, WriteSsvcParaset, WriteSavcParaset,
// WriteSavcParaset_Listing). The rest of the file follows with the layers
// it drives (E4 onwards).
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
}
