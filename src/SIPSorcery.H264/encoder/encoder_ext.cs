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
// PrefetchReferencePicture, (E5b) GetMvMvdRange, (E6) WritePadding and (E7)
// AddPrefixNal. E8 adds the rest of the file that a single layer, single
// thread, CAVLC camera encoder runs: the frame rate, bit rate and LTR
// setters, AcquireLayersNals, RequestMemorySvc and FreeMemorySvc,
// WelsInitEncoderExt and WelsUninitEncoderExt, ForceCodingIDR, the frame
// loop WelsEncoderEncodeExt and WelsEncoderParamAdjust. Configurations the
// port does not code are refused at init (PortUnsupportedConfiguration);
// the slice thread and dynamic slice branches throw NotSupportedException.
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
// 26 Nov 2026  Claude          WritePadding (E6).
// 27 Nov 2026  Claude          AddPrefixNal (E7).
// 27 Nov 2026  Claude          The init, memory, frame loop and parameter changes (E8).
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

    public static int32_t AddPrefixNal(sWelsEncCtx* pCtx,
                                       SLayerBSInfo* pLayerBsInfo,
                                       int32_t* pNalLen,
                                       int32_t* pNalIdxInLayer,
                                       EWelsNalUnitType keNalType,
                                       EWelsNalRefIdc keNalRefIdc,
                                       ref int32_t iPayloadSize)
    {
        int32_t iReturn = ENC_RETURN_SUCCESS;
        iPayloadSize = 0;

        if (keNalRefIdc != EWelsNalRefIdc.NRI_PRI_LOWEST)
        {
            WelsLoadNal(pCtx->pOut, (int32_t)EWelsNalUnitType.NAL_UNIT_PREFIX, (int32_t)keNalRefIdc);

            WelsWriteSVCPrefixNal(&pCtx->pOut->sBsWrite, (int32_t)keNalRefIdc, (EWelsNalUnitType.NAL_UNIT_CODED_SLICE_IDR == keNalType));

            WelsUnloadNal(pCtx->pOut);

            iReturn = WelsEncodeNal(&pCtx->pOut->sNalList[pCtx->pOut->iNalIndex - 1],
                                    &pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt,
                                    pCtx->iFrameBsSize - pCtx->iPosBsBuffer,
                                    pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                    &pNalLen[*pNalIdxInLayer]);
            if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ
            iPayloadSize = pNalLen[*pNalIdxInLayer];

            pCtx->iPosBsBuffer += iPayloadSize;

            (*pNalIdxInLayer)++;
        }
        else
        { // No Prefix NAL Unit RBSP syntax here, but need add NAL Unit Header extension
            WelsLoadNal(pCtx->pOut, (int32_t)EWelsNalUnitType.NAL_UNIT_PREFIX, (int32_t)keNalRefIdc);
            // No need write any syntax of prefix NAL Unit RBSP here
            WelsUnloadNal(pCtx->pOut);

            iReturn = WelsEncodeNal(&pCtx->pOut->sNalList[pCtx->pOut->iNalIndex - 1],
                                    &pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt,
                                    pCtx->iFrameBsSize - pCtx->iPosBsBuffer,
                                    pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                    &pNalLen[*pNalIdxInLayer]);
            if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ
            iPayloadSize = pNalLen[*pNalIdxInLayer];

            pCtx->iPosBsBuffer += iPayloadSize;

            (*pNalIdxInLayer)++;
        }

        return ENC_RETURN_SUCCESS;
    }

    public static int32_t WritePadding(sWelsEncCtx* pCtx, int32_t iLen, ref int32_t iSize)
    {
        int32_t i = 0;
        int32_t iNal = 0;
        SBitStringAux* pBs = null;
        int32_t iNalLen;

        iSize = 0;
        iNal = pCtx->pOut->iNalIndex;
        pBs = &pCtx->pOut->sBsWrite;  // SBitStringAux instance for non VCL NALs decoding

        if ((pBs->pEndBuf - pBs->pCurBuf) < iLen || iNal >= pCtx->pOut->iCountNals)
        {
            // The GOM_TRACE_FLAG log is off.
            return ENC_RETURN_MEMOVERFLOWFOUND;
        }

        WelsLoadNal(pCtx->pOut, (int32_t)NAL_UNIT_FILLER_DATA, (int32_t)NRI_PRI_LOWEST);

        for (i = 0; i < iLen; i++)
        {
            BsWriteBits(pBs, 8, 0xff);
        }

        BsRbspTrailingBits(pBs);

        WelsUnloadNal(pCtx->pOut);
        int32_t iReturn = WelsEncodeNal(&pCtx->pOut->sNalList[iNal], null,
                                        pCtx->iFrameBsSize - pCtx->iPosBsBuffer,
                                        pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                        &iNalLen);
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn; // WELS_VERIFY_RETURN_IFNEQ

        pCtx->iPosBsBuffer += iNalLen;
        iSize += iNalLen;

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
    // RequestMemorySvc are in the E8 section below.

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

    // ---------------------------------------------------------------------------------
    // E8: the parameter changes, the memory, the init and uninit, the frame loop and the
    // parameter adjustment, in the upstream order.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Port addition: C's float or double to int32_t conversion as x64 compiles it
    /// (cvttss2si/cvttsd2si), which gives INT_MIN for NaN and out of range values, where
    /// C# saturates on .NET 9 and later. The bit rate scaling below can overflow.
    /// </summary>
    private static int32_t CTruncToInt32(double d) =>
        (d > -2147483649.0 && d < 2147483648.0) ? (int32_t)d : int32_t.MinValue;

    public static void WelsEncoderApplyFrameRate(SWelsSvcCodingParam* pParam)
    {
        SSpatialLayerInternal* pLayerParamInternal;
        SSpatialLayerConfig* pLayerParam;
        const float kfEpsn = 0.000001f;
        int32_t kiNumLayer = pParam->iSpatialLayerNum;
        int32_t i;
        float kfMaxFrameRate = pParam->fMaxFrameRate;
        float fRatio;
        float fTargetOutputFrameRate;

        //set input frame rate to each layer
        for (i = 0; i < kiNumLayer; i++)
        {
            pLayerParamInternal = &pParam->sDependencyLayers[i];
            pLayerParam = &pParam->sSpatialLayers[i];
            fRatio = pLayerParamInternal->fOutputFrameRate / pLayerParamInternal->fInputFrameRate;
            if ((kfMaxFrameRate - pLayerParamInternal->fInputFrameRate) > kfEpsn
                || (kfMaxFrameRate - pLayerParamInternal->fInputFrameRate) < -kfEpsn)
            {
                pLayerParamInternal->fInputFrameRate = kfMaxFrameRate;
                fTargetOutputFrameRate = kfMaxFrameRate * fRatio;
                pLayerParamInternal->fOutputFrameRate = (fTargetOutputFrameRate >= 6) ? fTargetOutputFrameRate :
                                                        (pLayerParamInternal->fInputFrameRate);
                pLayerParam->fFrameRate = pLayerParamInternal->fOutputFrameRate;
                //TODO:{Sijia} from design, there is no sense to have temporal layer when under 6fps even with such setting?
            }
        }
    }

    public static int32_t WelsEncoderApplyBitRate(SLogContext* pLogCtx, SWelsSvcCodingParam* pParam, int iLayer)
    {
        //TODO (Sijia):  this is a temporary solution which keep the ratio between layers
        //but it is also possible to fulfill the bitrate of lower layer first

        SSpatialLayerConfig* pLayerParam;
        int32_t iNumLayers = pParam->iSpatialLayerNum;
        int32_t i, iOrigTotalBitrate = 0;
        if (iLayer == (int)SPATIAL_LAYER_ALL)
        {
            //read old BR
            for (i = 0; i < iNumLayers; i++)
            {
                iOrigTotalBitrate += pParam->sSpatialLayers[i].iSpatialBitrate;
            }
            //write new BR
            float fRatio = 0.0f;
            for (i = 0; i < iNumLayers; i++)
            {
                pLayerParam = &pParam->sSpatialLayers[i];
                fRatio = pLayerParam->iSpatialBitrate / ((float)iOrigTotalBitrate);
                pLayerParam->iSpatialBitrate = CTruncToInt32(pParam->iTargetBitrate * fRatio);

                if (WelsBitRateVerification(pLogCtx, pLayerParam, i) != ENC_RETURN_SUCCESS)
                    return ENC_RETURN_UNSUPPORTED_PARA;
            }
        }
        else
        {
            return WelsBitRateVerification(pLogCtx, &pParam->sSpatialLayers[iLayer], iLayer);
        }
        return ENC_RETURN_SUCCESS;
    }

    public static int32_t WelsEncoderApplyBitVaryRang(SLogContext* pLogCtx, SWelsSvcCodingParam* pParam, int32_t iRang)
    {
        SSpatialLayerConfig* pLayerParam;
        int32_t iNumLayers = pParam->iSpatialLayerNum;
        for (int32_t i = 0; i < iNumLayers; i++)
        {
            pLayerParam = &pParam->sSpatialLayers[i];
            pLayerParam->iMaxSpatialBitrate = Math.Min(CTruncToInt32(pLayerParam->iSpatialBitrate * (1 + iRang / 100.0)),
                                                       pLayerParam->iMaxSpatialBitrate);
            if (WelsBitRateVerification(pLogCtx, pLayerParam, i) != ENC_RETURN_SUCCESS)
                return ENC_RETURN_UNSUPPORTED_PARA;
            WelsLog(pLogCtx, WELS_LOG_INFO,
                    $"WelsEncoderApplyBitVaryRang:UpdateMaxBitrate layerId= {i},iMaxSpatialBitrate = {pLayerParam->iMaxSpatialBitrate}");
        }
        return ENC_RETURN_SUCCESS;
    }

    /*!
     * \brief   acquire count number of layers and NALs based on configurable paramters dependency
     * \pParam  pCtx            sWelsEncCtx*
     * \pParam  pParam          SWelsSvcCodingParam*
     * \pParam  pCountLayers    pointer of count number of layers indeed
     * \pParam  iCountNals      pointer of count number of nals indeed
     * \return  0 - successful; otherwise failed
     */
    public static int32_t AcquireLayersNals(sWelsEncCtx** ppCtx, SWelsSvcCodingParam* pParam, int32_t* pCountLayers,
                                            int32_t* pCountNals)
    {
        int32_t iCountNumLayers = 0;
        int32_t iCountNumNals = 0;
        int32_t iNumDependencyLayers = 0;
        int32_t iDIndex = 0;

        if (null == pParam || null == ppCtx || null == *ppCtx)
            return 1;

        iNumDependencyLayers = pParam->iSpatialLayerNum;

        do
        {
            SSpatialLayerConfig* pDLayer = &pParam->sSpatialLayers[iDIndex];
            int32_t iOrgNumNals = iCountNumNals;

            //Note: Sep. 2010
            //Review this part and suggest no change, since the memory over-use
            //(1) counts little to the overall performance
            //(2) should not be critial even under mobile case
            if (SM_SIZELIMITED_SLICE == pDLayer->sSliceArgument.uiSliceMode)
            {
                iCountNumNals += MAX_SLICES_NUM;
                // plus prefix NALs
                if (iDIndex == 0)
                    iCountNumNals += MAX_SLICES_NUM;
                // MAX_SLICES_NUM < MAX_LAYER_NUM_OF_FRAME ensured at svc_enc_slice_segment.h
                if (iCountNumNals - iOrgNumNals > MAX_NAL_UNITS_IN_LAYER)
                {
                    WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_ERROR,
                            $"AcquireLayersNals(), num_of_slice({iCountNumNals}) > existing slice({iOrgNumNals}) at (iDid= {iDIndex}), max={MAX_NAL_UNITS_IN_LAYER}");
                    return 1;
                }
            }
            else /*if ( SM_SINGLE_SLICE != pDLayer->sSliceArgument.uiSliceMode )*/
            {
                int32_t kiNumOfSlice = GetInitialSliceNum(&pDLayer->sSliceArgument);

                // NEED check iCountNals value in case multiple slices is used
                iCountNumNals += kiNumOfSlice; // for pSlice VCL NALs
                // plus prefix NALs
                if (iDIndex == 0)
                    iCountNumNals += kiNumOfSlice;
                Debug.Assert(iCountNumNals - iOrgNumNals <= MAX_NAL_UNITS_IN_LAYER);
                if (kiNumOfSlice > MAX_SLICES_NUM)
                {
                    WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_ERROR,
                            $"AcquireLayersNals(), num_of_slice({kiNumOfSlice}) > MAX_SLICES_NUM({MAX_SLICES_NUM}) per (iDid= {iDIndex}, qid= {0}) settings!");
                    return 1;
                }
            }

            if (iCountNumNals - iOrgNumNals > MAX_NAL_UNITS_IN_LAYER)
            {
                WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_ERROR,
                        $"AcquireLayersNals(), num_of_nals({iCountNumNals - iOrgNumNals}) > MAX_NAL_UNITS_IN_LAYER({MAX_NAL_UNITS_IN_LAYER}) per (iDid= {iDIndex}, qid= {0}) settings!");
                return 1;
            }

            iCountNumLayers++;

            ++iDIndex;
        } while (iDIndex < iNumDependencyLayers);

        if (null == (*ppCtx)->pFuncList || null == (*ppCtx)->pFuncList->pParametersetStrategy)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_ERROR,
                    "AcquireLayersNals(), pFuncList and pParametersetStrategy needed to be initialized first!");
            return 1;
        }
        // count parasets
        iCountNumNals += 1 + iNumDependencyLayers + (iCountNumLayers << 1) +
                         iCountNumLayers // plus iCountNumLayers for reserved application
                         + (int32_t)(*ppCtx)->pFuncList->pParametersetStrategy->GetAllNeededParasetNum();

        // to check number of layers / nals / slices dependencies, 12/8/2010
        if (iCountNumLayers > MAX_LAYER_NUM_OF_FRAME)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_ERROR,
                    $"AcquireLayersNals(), iCountNumLayers({iCountNumLayers}) > MAX_LAYER_NUM_OF_FRAME({MAX_LAYER_NUM_OF_FRAME})!");
            return 1;
        }

        if (null != pCountLayers)
            *pCountLayers = iCountNumLayers;
        if (null != pCountNals)
            *pCountNals = iCountNumNals;
        return 0;
    }

    // RequestMemoryVaaScreen and ReleaseMemoryVaaScreen: screen content (Phase 4).

    public static int32_t RequestMemorySvc(sWelsEncCtx** ppCtx, SExistingParasetList* pExistingParasetList)
    {
        SWelsSvcCodingParam* pParam = (*ppCtx)->pSvcParam;
        CMemoryAlign* pMa = (*ppCtx)->pMemAlign;
        SSpatialLayerConfig* pFinalSpatial = null;
        int32_t iCountBsLen = 0;
        int32_t iCountNals = 0;
        int32_t iMaxPicWidth = 0;
        int32_t iMaxPicHeight = 0;
        int32_t iCountMaxMbNum = 0;
        int32_t iIndex = 0;
        int32_t iCountLayers = 0;
        int32_t iResult = 0;
        float fCompressRatioThr = .5f;
        int32_t kiNumDependencyLayers = pParam->iSpatialLayerNum;
        int32_t iVclLayersBsSizeCount = 0;
        int32_t iNonVclLayersBsSizeCount = 0;
        int32_t iTargetSpatialBsSize = 0;

        if (kiNumDependencyLayers < 1 || kiNumDependencyLayers > MAX_DEPENDENCY_LAYER)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_WARNING,
                    $"RequestMemorySvc() failed due to invalid iNumDependencyLayers({kiNumDependencyLayers})!");
            return 1;
        }

        if (pParam->uiGopSize == 0 || (pParam->uiIntraPeriod != 0 && ((pParam->uiIntraPeriod % pParam->uiGopSize) != 0)))
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_WARNING,
                    $"RequestMemorySvc() failed due to invalid uiIntraPeriod({pParam->uiIntraPeriod}) (=multipler of uiGopSize({pParam->uiGopSize})!");
            return 1;
        }

        pFinalSpatial = &pParam->sSpatialLayers[kiNumDependencyLayers - 1];
        iMaxPicWidth = pFinalSpatial->iVideoWidth;
        iMaxPicHeight = pFinalSpatial->iVideoHeight;
        iCountMaxMbNum = ((15 + iMaxPicWidth) >> 4) * ((15 + iMaxPicHeight) >> 4);

        iResult = AcquireLayersNals(ppCtx, pParam, &iCountLayers, &iCountNals);
        if (iResult != 0)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_WARNING, $"RequestMemorySvc(), AcquireLayersNals failed({iResult})!");
            return 1;
        }

        int32_t kiSpsSize = (int32_t)(*ppCtx)->pFuncList->pParametersetStrategy->GetNeededSpsNum() * SPS_BUFFER_SIZE;
        int32_t kiPpsSize = (int32_t)(*ppCtx)->pFuncList->pParametersetStrategy->GetNeededPpsNum() * PPS_BUFFER_SIZE;
        iNonVclLayersBsSizeCount = SSEI_BUFFER_SIZE + kiSpsSize + kiPpsSize;

        bool bDynamicSlice = false;
        uint32_t uiMaxSliceNumEstimation = 0;
        int32_t iSliceBufferSize = 0;
        int32_t iMaxSliceBufferSize = 0;
        int32_t iTotalLength = 0;
        int32_t iLayerBsSize = 0;
        iIndex = 0;
        while (iIndex < pParam->iSpatialLayerNum)
        {
            SSpatialLayerConfig* fDlp = &pParam->sSpatialLayers[iIndex];

            fCompressRatioThr = COMPRESS_RATIO_THR;

            iLayerBsSize = WELS_ROUND(((3 * fDlp->iVideoWidth * fDlp->iVideoHeight) >> 1) * fCompressRatioThr) +
                           MAX_MACROBLOCK_SIZE_IN_BYTE_x2;
            iLayerBsSize = WELS_ALIGN(iLayerBsSize, 4); // 4 bytes alinged
            int32_t iMaxLayerBsSize = 0;
            SSliceArgument* pSliceArgument = &fDlp->sSliceArgument;
            if (pSliceArgument->uiSliceMode == SM_SIZELIMITED_SLICE)
            {
                bDynamicSlice = true;
                uiMaxSliceNumEstimation = Math.Min((uint32_t)AVERSLICENUM_CONSTRAINT,
                                                   ((uint32_t)iLayerBsSize / pSliceArgument->uiSliceSizeConstraint) + 1);
                (*ppCtx)->iMaxSliceCount = Math.Max((*ppCtx)->iMaxSliceCount, (int)uiMaxSliceNumEstimation);
                iSliceBufferSize = (int32_t)((Math.Max(pSliceArgument->uiSliceSizeConstraint,
                                                       (uint32_t)iLayerBsSize / uiMaxSliceNumEstimation) << 1) + MAX_MACROBLOCK_SIZE_IN_BYTE_x2);
                iMaxLayerBsSize = (int32_t)((uint32_t)iSliceBufferSize * uiMaxSliceNumEstimation);
            }
            else
            {
                (*ppCtx)->iMaxSliceCount = Math.Max((*ppCtx)->iMaxSliceCount, (int)pSliceArgument->uiSliceNum);
                if (pParam->bUseLoadBalancing)
                {
                    iSliceBufferSize = iLayerBsSize + MAX_MACROBLOCK_SIZE_IN_BYTE_x2;
                }
                else
                {
                    iSliceBufferSize = (int32_t)(((uint32_t)iLayerBsSize / pSliceArgument->uiSliceNum) << 1) + MAX_MACROBLOCK_SIZE_IN_BYTE_x2;
                }
                iMaxLayerBsSize = (int32_t)((uint32_t)iSliceBufferSize * pSliceArgument->uiSliceNum);
            }
            iMaxLayerBsSize = Math.Max(iMaxLayerBsSize, iLayerBsSize);
            iVclLayersBsSizeCount += iMaxLayerBsSize;
            iMaxSliceBufferSize = Math.Max(iMaxSliceBufferSize, iSliceBufferSize);
            (*ppCtx)->iSliceBufferSize[iIndex] = iSliceBufferSize;
            ++iIndex;
        }
        iTargetSpatialBsSize = iVclLayersBsSizeCount;
        iCountBsLen = iNonVclLayersBsSizeCount + iVclLayersBsSizeCount;

        iMaxSliceBufferSize = Math.Min(iMaxSliceBufferSize, iTargetSpatialBsSize);
        iTotalLength = iCountBsLen;

        pParam->iNumRefFrame = WELS_CLIP3(pParam->iNumRefFrame, MIN_REF_PIC_COUNT,
                                          (pParam->iUsageType == CAMERA_VIDEO_REAL_TIME ? MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA :
                                           MAX_REFERENCE_PICTURE_COUNT_NUM_SCREEN));

        // Output
        (*ppCtx)->pOut = (SWelsEncoderOutput*)pMa->WelsMallocz((uint32_t)sizeof(SWelsEncoderOutput), "SWelsEncoderOutput");
        if (null == (*ppCtx)->pOut) return 1;
        (*ppCtx)->pOut->pBsBuffer = (uint8_t*)pMa->WelsMallocz((uint32_t)iCountBsLen, "pOut->pBsBuffer");
        if (null == (*ppCtx)->pOut->pBsBuffer) return 1;
        (*ppCtx)->pOut->uiSize = (uint32_t)iCountBsLen;
        (*ppCtx)->pOut->sNalList = (SWelsNalRaw*)pMa->WelsMallocz((uint32_t)(iCountNals * sizeof(SWelsNalRaw)), "pOut->sNalList");
        if (null == (*ppCtx)->pOut->sNalList) return 1;
        (*ppCtx)->pOut->pNalLen = (int32_t*)pMa->WelsMallocz((uint32_t)(iCountNals * sizeof(int32_t)), "pOut->pNalLen");
        if (null == (*ppCtx)->pOut->pNalLen) return 1;
        (*ppCtx)->pOut->iCountNals = iCountNals;
        (*ppCtx)->pOut->iNalIndex = 0;
        (*ppCtx)->pOut->iLayerBsIndex = 0;

        (*ppCtx)->pFrameBs = (uint8_t*)pMa->WelsMalloc((uint32_t)iTotalLength, "pFrameBs");
        if (null == (*ppCtx)->pFrameBs) return 1;
        (*ppCtx)->iFrameBsSize = iTotalLength;
        (*ppCtx)->iPosBsBuffer = 0;

        // for dynamic slice mode&& CABAC,allocate slice buffer to restore slice data
        if (bDynamicSlice && pParam->iEntropyCodingModeFlag != 0)
        {
            // pDynamicBsBuffer: WelsInitEncoderExt rejects CABAC and SM_SIZELIMITED_SLICE.
            throw new NotSupportedException("RequestMemorySvc: SM_SIZELIMITED_SLICE with CABAC is not ported (Phase 4).");
        }
        // for pSlice bs buffers
        if (pParam->iMultipleThreadIdc > 1)
        {
            // RequestMtResource: WelsInitEncoderExt rejects threads.
            throw new NotSupportedException("RequestMemorySvc: threading is not ported (Phase 4).");
        }

        (*ppCtx)->pReferenceStrategy = IWelsReferenceStrategy.CreateReferenceStrategy((*ppCtx), pParam->iUsageType,
                                       pParam->bEnableLongTermReference);
        if (null == (*ppCtx)->pReferenceStrategy) return 1;

        (*ppCtx)->pIntra4x4PredModeBlocks = (int8_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * INTRA_4x4_MODE_NUM),
                                            "pIntra4x4PredModeBlocks");
        if (null == (*ppCtx)->pIntra4x4PredModeBlocks) return 1;

        (*ppCtx)->pNonZeroCountBlocks = (int8_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * MB_LUMA_CHROMA_BLOCK4x4_NUM),
                                        "pNonZeroCountBlocks");
        if (null == (*ppCtx)->pNonZeroCountBlocks) return 1;

        (*ppCtx)->pMvUnitBlock4x4 = (SMVUnitXY*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * 2 * MB_BLOCK4x4_NUM * sizeof(SMVUnitXY)),
                                    "pMvUnitBlock4x4");
        if (null == (*ppCtx)->pMvUnitBlock4x4) return 1;

        (*ppCtx)->pRefIndexBlock4x4 = (int8_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * 2 * MB_BLOCK8x8_NUM * sizeof(int8_t)),
                                      "pRefIndexBlock4x4");
        if (null == (*ppCtx)->pRefIndexBlock4x4) return 1;

        (*ppCtx)->pSadCostMb = (int32_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * sizeof(int32_t)), "pSadCostMb");
        if (null == (*ppCtx)->pSadCostMb) return 1;

        (*ppCtx)->iGlobalQp = 26;   // global qp in default

        (*ppCtx)->pLtr = (SLTRState*)pMa->WelsMallocz((uint32_t)(kiNumDependencyLayers * sizeof(SLTRState)), "SLTRState");
        if (null == (*ppCtx)->pLtr) return 1;
        int32_t i = 0;
        for (i = 0; i < kiNumDependencyLayers; i++)
        {
            ResetLtrState(&(*ppCtx)->pLtr[i]);
        }

        // stride tables
        if (AllocStrideTables(ppCtx, kiNumDependencyLayers) != 0)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_WARNING, "RequestMemorySvc(), AllocStrideTables failed!");
            return 1;
        }

        //Rate control module memory allocation
        // only malloc once for RC pData, 12/14/2009
        (*ppCtx)->pWelsSvcRc = (SWelsSvcRc*)pMa->WelsMallocz((uint32_t)(kiNumDependencyLayers * sizeof(SWelsSvcRc)), "pWelsSvcRc");
        if (null == (*ppCtx)->pWelsSvcRc) return 1;
        //End of Rate control module memory allocation

        //pVaa memory allocation
        if (pParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            // SVAAFrameInfoExt and RequestMemoryVaaScreen: WelsInitEncoderExt rejects screen content.
            throw new NotSupportedException("RequestMemorySvc: screen content is not ported (Phase 4).");
        }
        else
        {
            (*ppCtx)->pVaa = (SVAAFrameInfo*)pMa->WelsMallocz((uint32_t)sizeof(SVAAFrameInfo), "pVaa");
            if (null == (*ppCtx)->pVaa) return 1;
        }

        if ((*ppCtx)->pSvcParam->bEnableAdaptiveQuant) //malloc mem
        {
            (*ppCtx)->pVaa->sAdaptiveQuantParam.pMotionTextureUnit = (SMotionTextureUnit*)pMa->WelsMallocz(
                (uint32_t)(iCountMaxMbNum * sizeof(SMotionTextureUnit)), "pVaa->sAdaptiveQuantParam.pMotionTextureUnit");
            if (null == (*ppCtx)->pVaa->sAdaptiveQuantParam.pMotionTextureUnit) return 1;
            (*ppCtx)->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp = (int8_t*)pMa->WelsMallocz(
                (uint32_t)(iCountMaxMbNum * sizeof(int8_t)), "pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp");
            if (null == (*ppCtx)->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp) return 1;
        }

        (*ppCtx)->pVaa->pVaaBackgroundMbFlag = (int8_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * sizeof(int8_t)),
                                               "pVaa->pVaaBackgroundMbFlag");
        if (null == (*ppCtx)->pVaa->pVaaBackgroundMbFlag) return 1;

        (*ppCtx)->pVaa->sVaaCalcInfo.pSad8x8 = (int32_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * 4 * sizeof(int32_t)),
                                               "pVaa->sVaaCalcInfo.sad8x8");
        if (null == (*ppCtx)->pVaa->sVaaCalcInfo.pSad8x8) return 1;
        (*ppCtx)->pVaa->sVaaCalcInfo.pSsd16x16 = (int32_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * sizeof(int32_t)),
                                                 "pVaa->sVaaCalcInfo.pSsd16x16");
        if (null == (*ppCtx)->pVaa->sVaaCalcInfo.pSsd16x16) return 1;
        (*ppCtx)->pVaa->sVaaCalcInfo.pSum16x16 = (int32_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * sizeof(int32_t)),
                                                 "pVaa->sVaaCalcInfo.pSum16x16");
        if (null == (*ppCtx)->pVaa->sVaaCalcInfo.pSum16x16) return 1;
        (*ppCtx)->pVaa->sVaaCalcInfo.pSumOfSquare16x16 = (int32_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * sizeof(int32_t)),
                                                         "pVaa->sVaaCalcInfo.pSumOfSquare16x16");
        if (null == (*ppCtx)->pVaa->sVaaCalcInfo.pSumOfSquare16x16) return 1;

        if ((*ppCtx)->pSvcParam->bEnableBackgroundDetection) //BGD control
        {
            (*ppCtx)->pVaa->sVaaCalcInfo.pSumOfDiff8x8 = (int32_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * 4 * sizeof(int32_t)),
                                                         "pVaa->sVaaCalcInfo.pSumOfDiff8x8");
            if (null == (*ppCtx)->pVaa->sVaaCalcInfo.pSumOfDiff8x8) return 1;
            (*ppCtx)->pVaa->sVaaCalcInfo.pMad8x8 = (uint8_t*)pMa->WelsMallocz((uint32_t)(iCountMaxMbNum * 4 * sizeof(uint8_t)),
                                                   "pVaa->sVaaCalcInfo.pMad8x8");
            if (null == (*ppCtx)->pVaa->sVaaCalcInfo.pMad8x8) return 1;
        }

        //End of pVaa memory allocation

        (*ppCtx)->ppRefPicListExt = (SRefList**)pMa->WelsMallocz((uint32_t)(kiNumDependencyLayers * sizeof(SRefList*)),
                                    "ppRefPicListExt");
        if (null == (*ppCtx)->ppRefPicListExt) return 1;

        (*ppCtx)->ppDqLayerList = (SDqLayer**)pMa->WelsMallocz((uint32_t)(kiNumDependencyLayers * sizeof(SDqLayer*)), "ppDqLayerList");
        if (null == (*ppCtx)->ppDqLayerList) return 1;

        iResult = InitDqLayers(ppCtx, pExistingParasetList);
        if (iResult != 0)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_WARNING, $"RequestMemorySvc(), InitDqLayers failed({iResult})!");
            return iResult;
        }

        if (InitMbListD(ppCtx) != 0)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_WARNING, "RequestMemorySvc(), InitMbListD failed!");
            return 1;
        }

        int32_t iMvdRange = 0;
        GetMvMvdRange(pParam, ref (*ppCtx)->iMvRange, ref iMvdRange);
        uint32_t kuiMvdInterTableSize = (uint32_t)(iMvdRange << 2); //intepel*4=qpel
        uint32_t kuiMvdInterTableStride = 1 + (kuiMvdInterTableSize << 1);//qpel_mv_range*2=(+/-);
        uint32_t kuiMvdCacheAlignedSize = kuiMvdInterTableStride * sizeof(uint16_t);

        (*ppCtx)->iMvdCostTableSize = (int32_t)kuiMvdInterTableSize;
        (*ppCtx)->iMvdCostTableStride = (int32_t)kuiMvdInterTableStride;
        (*ppCtx)->pMvdCostTable = (uint16_t*)pMa->WelsMallocz(52 * kuiMvdCacheAlignedSize, "pMvdCostTable");
        if (null == (*ppCtx)->pMvdCostTable) return 1;
        MvdCostInit((*ppCtx)->pMvdCostTable, (int32_t)kuiMvdInterTableStride);  //should put to a better place?

        if ((*ppCtx)->ppRefPicListExt[0] != null && !(*ppCtx)->ppRefPicListExt[0]->pRef[0].IsNull)
            (*ppCtx)->pDecPic = (*ppCtx)->ppRefPicListExt[0]->pRef[0];
        else
            (*ppCtx)->pDecPic = null; // error here

        (*ppCtx)->pSps = &(*ppCtx)->pSpsArray[0];
        (*ppCtx)->pPps = &(*ppCtx)->pPPSArray[0];

        return 0;
    }

    /*!
     * \brief   free memory in SVC core encoder
     * \pParam  pEncCtx     sWelsEncCtx*
     * \return  none
     */
    public static void FreeMemorySvc(sWelsEncCtx** ppCtx)
    {
        if (null != *ppCtx)
        {
            sWelsEncCtx* pCtx = *ppCtx;
            CMemoryAlign* pMa = pCtx->pMemAlign;
            SWelsSvcCodingParam* pParam = pCtx->pSvcParam;
            int32_t ilayer = 0;

            // SStrideTables
            if (null != pCtx->pStrideTab)
            {
                if (!pCtx->pStrideTab->pStrideDecBlockOffset[0][1].IsNull)
                {
                    pMa->WelsFree((int32_t*)pCtx->pStrideTab->pStrideDecBlockOffset[0][1], "pBase");
                    pCtx->pStrideTab->pStrideDecBlockOffset[0][1] = null;
                }
                pMa->WelsFree(pCtx->pStrideTab, "SStrideTables");
                pCtx->pStrideTab = null;
            }
            // pDq idc map
            if (null != pCtx->pDqIdcMap)
            {
                pMa->WelsFree(pCtx->pDqIdcMap, "pDqIdcMap");
                pCtx->pDqIdcMap = null;
            }

            if (null != pCtx->pOut)
            {
                // bs pBuffer
                if (null != pCtx->pOut->pBsBuffer)
                {
                    pMa->WelsFree(pCtx->pOut->pBsBuffer, "pOut->pBsBuffer");
                    pCtx->pOut->pBsBuffer = null;
                }
                // NALs list
                if (null != pCtx->pOut->sNalList)
                {
                    pMa->WelsFree(pCtx->pOut->sNalList, "pOut->sNalList");
                    pCtx->pOut->sNalList = null;
                }
                // NALs len
                if (null != pCtx->pOut->pNalLen)
                {
                    pMa->WelsFree(pCtx->pOut->pNalLen, "pOut->pNalLen");
                    pCtx->pOut->pNalLen = null;
                }
                pMa->WelsFree(pCtx->pOut, "SWelsEncoderOutput");
                pCtx->pOut = null;
            }

            // ReleaseMtResource: iMultipleThreadIdc > 1 is rejected at init.

            if (null != pCtx->pReferenceStrategy)
            {
                IWelsReferenceStrategy.DeleteReferenceStrategy(ref pCtx->pReferenceStrategy);
            }

            // frame bitstream pBuffer
            if (null != pCtx->pFrameBs)
            {
                pMa->WelsFree(pCtx->pFrameBs, "pFrameBs");
                pCtx->pFrameBs = null;
            }
            // pDynamicBsBuffer: never allocated (SM_SIZELIMITED_SLICE with CABAC is rejected at init).

            // pSpsArray
            if (null != pCtx->pSpsArray)
            {
                pMa->WelsFree(pCtx->pSpsArray, "pSpsArray");
                pCtx->pSpsArray = null;
            }
            // pPPSArray
            if (null != pCtx->pPPSArray)
            {
                pMa->WelsFree(pCtx->pPPSArray, "pPPSArray");
                pCtx->pPPSArray = null;
            }
            // subset_sps_array
            if (null != pCtx->pSubsetArray)
            {
                pMa->WelsFree(pCtx->pSubsetArray, "pSubsetArray");
                pCtx->pSubsetArray = null;
            }

            if (null != pCtx->pIntra4x4PredModeBlocks)
            {
                pMa->WelsFree(pCtx->pIntra4x4PredModeBlocks, "pIntra4x4PredModeBlocks");
                pCtx->pIntra4x4PredModeBlocks = null;
            }

            if (null != pCtx->pNonZeroCountBlocks)
            {
                pMa->WelsFree(pCtx->pNonZeroCountBlocks, "pNonZeroCountBlocks");
                pCtx->pNonZeroCountBlocks = null;
            }

            if (null != pCtx->pMvUnitBlock4x4)
            {
                pMa->WelsFree(pCtx->pMvUnitBlock4x4, "pMvUnitBlock4x4");
                pCtx->pMvUnitBlock4x4 = null;
            }

            if (null != pCtx->pRefIndexBlock4x4)
            {
                pMa->WelsFree(pCtx->pRefIndexBlock4x4, "pRefIndexBlock4x4");
                pCtx->pRefIndexBlock4x4 = null;
            }

            if (null != pCtx->ppMbListD)
            {
                if (null != pCtx->ppMbListD[0])
                {
                    pMa->WelsFree(pCtx->ppMbListD[0], "ppMbListD[0]");
                    (*ppCtx)->ppMbListD[0] = null;
                }
                pMa->WelsFree(pCtx->ppMbListD, "ppMbListD");
                pCtx->ppMbListD = null;
            }

            if (null != pCtx->pSadCostMb)
            {
                pMa->WelsFree(pCtx->pSadCostMb, "pSadCostMb");
                pCtx->pSadCostMb = null;
            }

            // SLTRState
            if (null != pCtx->pLtr)
            {
                pMa->WelsFree(pCtx->pLtr, "SLTRState");
                pCtx->pLtr = null;
            }

            // pDq layers list
            ilayer = 0;
            if (null != pCtx->ppDqLayerList && pParam != null)
            {
                while (ilayer < pParam->iSpatialLayerNum)
                {
                    SDqLayer* pDq = pCtx->ppDqLayerList[ilayer];
                    // pDq layers
                    if (null != pDq)
                    {
                        FreeDqLayer(&pDq, pMa);
                        pCtx->ppDqLayerList[ilayer] = null;
                    }
                    ++ilayer;
                }
                pMa->WelsFree(pCtx->ppDqLayerList, "ppDqLayerList");
                pCtx->ppDqLayerList = null;
            }
            // reference picture list extension
            if (null != pCtx->ppRefPicListExt && pParam != null)
            {
                ilayer = 0;
                while (ilayer < pParam->iSpatialLayerNum)
                {
                    FreeRefList(&pCtx->ppRefPicListExt[ilayer], pMa, pParam->iMaxNumRefFrame);
                    pCtx->ppRefPicListExt[ilayer] = null;
                    ++ilayer;
                }

                pMa->WelsFree(pCtx->ppRefPicListExt, "ppRefPicListExt");
                pCtx->ppRefPicListExt = null;
            }

            // VAA
            if (null != pCtx->pVaa)
            {
                if (pCtx->pSvcParam->bEnableAdaptiveQuant) //free mem
                {
                    pMa->WelsFree(pCtx->pVaa->sAdaptiveQuantParam.pMotionTextureUnit, "pVaa->sAdaptiveQuantParam.pMotionTextureUnit");
                    pCtx->pVaa->sAdaptiveQuantParam.pMotionTextureUnit = null;
                    pMa->WelsFree(pCtx->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp,
                                  "pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp");
                    pCtx->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp = null;
                }

                pMa->WelsFree(pCtx->pVaa->pVaaBackgroundMbFlag, "pVaa->pVaaBackgroundMbFlag");
                pCtx->pVaa->pVaaBackgroundMbFlag = null;
                pMa->WelsFree(pCtx->pVaa->sVaaCalcInfo.pSad8x8, "pVaa->sVaaCalcInfo.sad8x8");
                pCtx->pVaa->sVaaCalcInfo.pSad8x8 = null;
                pMa->WelsFree(pCtx->pVaa->sVaaCalcInfo.pSsd16x16, "pVaa->sVaaCalcInfo.pSsd16x16");
                pCtx->pVaa->sVaaCalcInfo.pSsd16x16 = null;
                pMa->WelsFree(pCtx->pVaa->sVaaCalcInfo.pSum16x16, "pVaa->sVaaCalcInfo.pSum16x16");
                pCtx->pVaa->sVaaCalcInfo.pSum16x16 = null;
                pMa->WelsFree(pCtx->pVaa->sVaaCalcInfo.pSumOfSquare16x16, "pVaa->sVaaCalcInfo.pSumOfSquare16x16");
                pCtx->pVaa->sVaaCalcInfo.pSumOfSquare16x16 = null;

                if (pCtx->pSvcParam->bEnableBackgroundDetection) //BGD control
                {
                    pMa->WelsFree(pCtx->pVaa->sVaaCalcInfo.pSumOfDiff8x8, "pVaa->sVaaCalcInfo.pSumOfDiff8x8");
                    pCtx->pVaa->sVaaCalcInfo.pSumOfDiff8x8 = null;
                    pMa->WelsFree(pCtx->pVaa->sVaaCalcInfo.pMad8x8, "pVaa->sVaaCalcInfo.pMad8x8");
                    pCtx->pVaa->sVaaCalcInfo.pMad8x8 = null;
                }
                // ReleaseMemoryVaaScreen: screen content is rejected at init.
                pMa->WelsFree(pCtx->pVaa, "pVaa");
                pCtx->pVaa = null;
            }

            // rate control module memory free
            if (null != pCtx->pWelsSvcRc)
            {
                WelsRcFreeMemory(pCtx);
                pMa->WelsFree(pCtx->pWelsSvcRc, "pWelsSvcRc");
                pCtx->pWelsSvcRc = null;
            }

            /* MVD cost tables for Inter */
            if (null != pCtx->pMvdCostTable)
            {
                pMa->WelsFree(pCtx->pMvdCostTable, "pMvdCostTable");
                pCtx->pMvdCostTable = null;
            }

            FreeCodingParam(&pCtx->pSvcParam, pMa);
            if (null != pCtx->pFuncList)
            {
                if (null != pCtx->pFuncList->pParametersetStrategy)
                {
                    IWelsParametersetStrategy.DeleteParametersetStrategy(ref pCtx->pFuncList->pParametersetStrategy);
                }

                pMa->WelsFree(pCtx->pFuncList, "SWelsFuncPtrList");
                pCtx->pFuncList = null;
            }

            if ((*ppCtx)->pMemAlign != null)
            {
                WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                        $"FreeMemorySvc(), verify memory usage ({(*ppCtx)->pMemAlign->WelsGetMemoryUsage()} bytes) after free..");
                WELS_DELETE_CMemoryAlign(ref (*ppCtx)->pMemAlign);
            }

            NativeMemory.Free(*ppCtx);
            *ppCtx = null;
        }
    }

    // InitSliceSettings, OutputCpuFeaturesLog (X86_ASM), StatOverallEncodingExt (STAT_OUTPUT)
    // and GetMultipleThreadIdc: see above.

    /*!
     * \brief   uninitialize Wels encoder core library
     * \pParam  pEncCtx     sWelsEncCtx*
     * \return  none
     */
    public static void WelsUninitEncoderExt(sWelsEncCtx** ppCtx)
    {
        if (null == ppCtx || null == *ppCtx)
            return;

        WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                $"WelsUninitEncoderExt(), pCtx= 0x{(nint)(*ppCtx):X}, iMultipleThreadIdc= {(*ppCtx)->pSvcParam->iMultipleThreadIdc}.");

        // The slice threads' join: iMultipleThreadIdc > 1 is rejected at init.

        if ((*ppCtx)->pVpp != null)
        {
            (*ppCtx)->pVpp->FreeSpatialPictures(*ppCtx);
            CWelsPreProcess.DeletePreProcess(ref (*ppCtx)->pVpp);
        }
        FreeMemorySvc(ppCtx);
        *ppCtx = null;
    }

    /*!
     * \brief   initialize Wels avc encoder core library
     * \pParam  ppCtx       sWelsEncCtx**
     * \pParam  pParam      SWelsSvcCodingParam*
     * \return  successful - 0; otherwise none 0 for failed
     */
    public static int32_t WelsInitEncoderExt(sWelsEncCtx** ppCtx, SWelsSvcCodingParam* pCodingParam, SLogContext* pLogCtx,
                                             SExistingParasetList* pExistingParasetList)
    {
        sWelsEncCtx* pCtx = null;
        int32_t iRet = 0;
        int16_t iSliceNum = 1;    // number of slices used
        int32_t iCacheLineSize = 16;   // on chip cache line size in byte
        uint32_t uiCpuFeatureFlags = 0;
        if (null == ppCtx || null == pCodingParam)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR,
                    $"WelsInitEncoderExt(), NULL == ppCtx(0x{(nint)ppCtx:X}) or NULL == pCodingParam(0x{(nint)pCodingParam:X}).");
            return 1;
        }

        iRet = ParamValidationExt(pLogCtx, pCodingParam);
        if (iRet != 0)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"WelsInitEncoderExt(), ParamValidationExt failed return {iRet}.");
            return iRet;
        }
        iRet = pCodingParam->DetermineTemporalSettings();
        if (iRet != ENC_RETURN_SUCCESS)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR,
                    $"WelsInitEncoderExt(), DetermineTemporalSettings failed return {iRet} (check in/out frame rate and temporal layer setting! -- in/out = 2^x, x <= temppral_layer_num)");
            return iRet;
        }
        iRet = GetMultipleThreadIdc(pLogCtx, pCodingParam, ref iSliceNum, ref iCacheLineSize, ref uiCpuFeatureFlags);
        if (iRet != 0)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"WelsInitEncoderExt(), GetMultipleThreadIdc failed return {iRet}.");
            return iRet;
        }

        // Port addition for SIPSorcery: what the port does not code yet, after the
        // validation has settled the configuration (GetMultipleThreadIdc resolves
        // iMultipleThreadIdc 0 to the core count).
        iRet = PortUnsupportedConfiguration(pLogCtx, pCodingParam);
        if (iRet != 0)
        {
            return iRet;
        }

        *ppCtx = null;

        pCtx = (sWelsEncCtx*)NativeMemory.Alloc((nuint)sizeof(sWelsEncCtx));

        if (null == pCtx) return 1;
        NativeMemory.Clear(pCtx, (nuint)sizeof(sWelsEncCtx));

        pCtx->sLogCtx = *pLogCtx;

        pCtx->pMemAlign = WELS_NEW_CMemoryAlign((uint32_t)iCacheLineSize);
        if (null == pCtx->pMemAlign)
        {
            WelsUninitEncoderExt(&pCtx);
            return 1;
        }

        iRet = AllocCodingParam(&pCtx->pSvcParam, pCtx->pMemAlign);
        if (iRet != 0)
        {
            WelsUninitEncoderExt(&pCtx);
            return iRet;
        }
        *pCtx->pSvcParam = *pCodingParam; // memcpy

        pCtx->pFuncList = (SWelsFuncPtrList*)pCtx->pMemAlign->WelsMallocz((uint32_t)sizeof(SWelsFuncPtrList), "SWelsFuncPtrList");
        if (null == pCtx->pFuncList)
        {
            WelsUninitEncoderExt(&pCtx);
            return 1;
        }
        InitFunctionPointers(pCtx, pCtx->pSvcParam, uiCpuFeatureFlags);

        pCtx->iActiveThreadsNum = (int16_t)pCodingParam->iMultipleThreadIdc;
        pCtx->iMaxSliceCount = iSliceNum;
        iRet = RequestMemorySvc(&pCtx, pExistingParasetList);
        if (iRet != 0)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, $"WelsInitEncoderExt(), RequestMemorySvc failed return {iRet}.");
            WelsUninitEncoderExt(&pCtx);
            return iRet;
        }

        // WelsCabacInit: CABAC is rejected above.
        WelsRcInitModule(pCtx, pCtx->pSvcParam->iRCMode);

        pCtx->pVpp = CWelsPreProcess.CreatePreProcess(pCtx);
        if (pCtx->pVpp == null)
        {
            iRet = 1;
            WelsLog(pLogCtx, WELS_LOG_ERROR, "WelsInitEncoderExt(), pOut of memory in case new CWelsPreProcess().");
            WelsUninitEncoderExt(&pCtx);
            return iRet;
        }
        if ((iRet = pCtx->pVpp->AllocSpatialPictures(pCtx, pCtx->pSvcParam)) != 0)
        {
            WelsLog(pLogCtx, WELS_LOG_ERROR, "WelsInitEncoderExt(), pVPP alloc spatial pictures failed");
            WelsUninitEncoderExt(&pCtx);
            return iRet;
        }

        pCtx->iStatisticsLogInterval = STATISTICS_LOG_INTERVAL_MS;
        pCtx->uiLastTimestamp = -1;
        pCtx->bDeliveryFlag = true;
        *ppCtx = pCtx;

        WelsLog(pLogCtx, WELS_LOG_INFO, $"WelsInitEncoderExt(), pCtx= 0x{(nint)pCtx:X}.");

        return 0;
    }

    /// <summary>
    /// Port addition for SIPSorcery: rejects, with an error log, the configurations whose
    /// code is not ported: screen content, CABAC, more than one spatial layer or simulcast,
    /// slice threads and SM_SIZELIMITED_SLICE (Phase 4). The API's Initialize then returns
    /// cmInitParaError, as for any other configuration WelsInitEncoderExt refuses.
    /// </summary>
    public static int32_t PortUnsupportedConfiguration(SLogContext* pLogCtx, SWelsSvcCodingParam* pCodingParam)
    {
        string what = null;
        if (pCodingParam->iUsageType != CAMERA_VIDEO_REAL_TIME)
            what = "screen content (iUsageType != CAMERA_VIDEO_REAL_TIME)";
        else if (pCodingParam->iEntropyCodingModeFlag != 0)
            what = "CABAC (iEntropyCodingModeFlag != 0)";
        else if (pCodingParam->iSpatialLayerNum != 1 || pCodingParam->bSimulcastAVC)
            what = "more than one spatial layer or simulcast";
        else if (pCodingParam->iMultipleThreadIdc != 1)
            what = $"slice threads (iMultipleThreadIdc = {pCodingParam->iMultipleThreadIdc})";
        else if (pCodingParam->sSpatialLayers[0].sSliceArgument.uiSliceMode == SM_SIZELIMITED_SLICE)
            what = "SM_SIZELIMITED_SLICE";
        if (what == null)
            return 0;
        WelsLog(pLogCtx, WELS_LOG_ERROR, $"WelsInitEncoderExt(), {what} is not supported by the SIPSorcery port.");
        return ENC_RETURN_UNSUPPORTED_PARA;
    }

    // GetTemporalLevel, the dynamic slicing set up (DynslcUpdateMbNeighbourInfoListForAllSlices,
    // PicPartitionNumDecision, WelsInitCurrentQBLayerMltslc, UpdateSlicepEncCtxWithPartition,
    // WelsInitCurrentDlayerMltslc: Phase 4), WelsInitCurrentLayer, SetMeMethod (screen content),
    // PreprocessSliceCoding, WelsSwapDqLayers, PrefetchReferencePicture, the parameter set
    // writers, AddPrefixNal and WritePadding: see above.

    /*
     * Force coding IDR as follows
     */
    public static int32_t ForceCodingIDR(sWelsEncCtx* pCtx, int32_t iLayerId)
    {
        if (null == pCtx)
            return 1;
        if ((iLayerId < 0) || (iLayerId >= MAX_SPATIAL_LAYER_NUM) || (!pCtx->pSvcParam->bSimulcastAVC))
        {
            for (int32_t iDid = 0; iDid < pCtx->pSvcParam->iSpatialLayerNum; iDid++)
            {
                SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[iDid];
                pParamInternal->iCodingIndex = 0;
                pParamInternal->iFrameIndex = 0;
                pParamInternal->iFrameNum = 0;
                pParamInternal->iPOC = 0;
                pParamInternal->bEncCurFrmAsIdrFlag = true;
                pCtx->sEncoderStatistics[0].uiIDRReqNum++;
            }
            WelsLog(&pCtx->sLogCtx, WELS_LOG_INFO,
                    $"ForceCodingIDR(iDid 0-{pCtx->pSvcParam->iSpatialLayerNum - 1})at InputFrameCount={pCtx->sEncoderStatistics[0].uiInputFrameCount}\n");
        }
        else
        {
            SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[iLayerId];
            pParamInternal->iCodingIndex = 0;
            pParamInternal->iFrameIndex = 0;
            pParamInternal->iFrameNum = 0;
            pParamInternal->iPOC = 0;
            pParamInternal->bEncCurFrmAsIdrFlag = true;
            pCtx->sEncoderStatistics[iLayerId].uiIDRReqNum++;
            WelsLog(&pCtx->sLogCtx, WELS_LOG_INFO,
                    $"ForceCodingIDR(iDid {iLayerId})at InputFrameCount={pCtx->sEncoderStatistics[iLayerId].uiInputFrameCount}\n");
        }
        pCtx->bCheckWindowStatusRefreshFlag = false;

        return 0;
    }

    public static void StackBackEncoderStatus(sWelsEncCtx* pEncCtx,
                                              EVideoFrameType keFrameType)
    {
        SSpatialLayerInternal* pParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId];
        // for bitstream writing
        pEncCtx->iPosBsBuffer = 0;   // reset bs pBuffer position
        pEncCtx->pOut->iNalIndex = 0;   // reset NAL index
        pEncCtx->pOut->iLayerBsIndex = 0;   // reset index of Layer Bs

        InitBits(&pEncCtx->pOut->sBsWrite, pEncCtx->pOut->pBsBuffer, (int32_t)pEncCtx->pOut->uiSize);
        if ((keFrameType == videoFrameTypeP) || (keFrameType == videoFrameTypeI))
        {
            pParamInternal->iFrameIndex--;
            if (pParamInternal->iPOC != 0)
            {
                pParamInternal->iPOC -= 2;
            }
            else
            {
                pParamInternal->iPOC = (1 << pEncCtx->pSps->iLog2MaxPocLsb) - 2;
            }

            LoadBackFrameNum(pEncCtx, pEncCtx->uiDependencyId);

            pEncCtx->eNalType = NAL_UNIT_CODED_SLICE;
            pEncCtx->eSliceType = P_SLICE;
            //pEncCtx->eNalPriority = pEncCtx->eLastNalPriority; //not need this since eNalPriority will be updated at the beginning of coding a frame
        }
        else if (keFrameType == videoFrameTypeIDR)
        {
            pParamInternal->uiIdrPicId--;

            //set the next frame to be IDR
            ForceCodingIDR(pEncCtx, pEncCtx->uiDependencyId);
        }
        else // B pictures are not supported now, any else?
        {
            Debug.Assert(false);
        }

        // no need to stack back RC info since the info is still useful for later RQ model calculation
        // no need to stack back MB slicing info for dynamic balancing, since the info is still refer-able
    }

    public static void ClearFrameBsInfo(sWelsEncCtx* pCtx, SFrameBSInfo* pFbi)
    {
        pFbi->sLayerInfo[0].pBsBuf = pCtx->pFrameBs;
        pFbi->sLayerInfo[0].pNalLengthInByte = pCtx->pOut->pNalLen;

        for (int i = 0; i < pFbi->iLayerNum; i++)
        {
            pFbi->sLayerInfo[i].iNalCount = 0;
            pFbi->sLayerInfo[i].eFrameType = videoFrameTypeSkip;
        }
        pFbi->iLayerNum = 0;
        pFbi->iFrameSizeInBytes = 0;
    }

    public static EVideoFrameType PrepareEncodeFrame(sWelsEncCtx* pCtx, ref SLayerBSInfo* pLayerBsInfo, int32_t iSpatialNum,
                                                     ref int8_t iCurDid, ref int32_t iCurTid,
                                                     ref int32_t iLayerNum, ref int32_t iFrameSize, long uiTimeStamp)
    {
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        SSpatialPicIndex* pSpatialIndexMap = (SSpatialPicIndex*)&pCtx->sSpatialIndexMap;

        bool bSkipFrameFlag = WelsRcCheckFrameStatus(pCtx, uiTimeStamp, iSpatialNum, iCurDid);
        EVideoFrameType eFrameType = DecideFrameType(pCtx, (int8_t)iSpatialNum, iCurDid, bSkipFrameFlag);
        if (eFrameType == videoFrameTypeSkip)
        {
            if (pSvcParam->bSimulcastAVC)
            {
                if (pCtx->pFuncList->pfRc.pfWelsUpdateBufferWhenSkip != null)
                    pCtx->pFuncList->pfRc.pfWelsUpdateBufferWhenSkip(pCtx, iCurDid);
                WelsLog(&pCtx->sLogCtx, WELS_LOG_DEBUG,
                        $"[Rc] Frame timestamp = {uiTimeStamp}, iDid = {iCurDid},skip one frame due to target_br, continual skipped {pCtx->pWelsSvcRc[iCurDid].iContinualSkipFrames} frames");
            }

            else
            {
                if (pCtx->pFuncList->pfRc.pfWelsUpdateBufferWhenSkip != null)
                {
                    for (int32_t i = 0; i < iSpatialNum; i++)
                    {
                        pCtx->pFuncList->pfRc.pfWelsUpdateBufferWhenSkip(pCtx, (pSpatialIndexMap + i)->iDid);
                    }
                }
                WelsLog(&pCtx->sLogCtx, WELS_LOG_DEBUG,
                        $"[Rc] Frame timestamp = {uiTimeStamp}, iDid = {iCurDid},skip one frame due to target_br, continual skipped {pCtx->pWelsSvcRc[iCurDid].iContinualSkipFrames} frames");
            }

        }
        else
        {
            SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[iCurDid];

            iCurTid = GetTemporalLevel(pParamInternal, pParamInternal->iCodingIndex,
                                       (int32_t)pSvcParam->uiGopSize);
            pCtx->uiTemporalId = (uint8_t)iCurTid;
            if (eFrameType == videoFrameTypeIDR)
            {
                // write parameter sets bitstream or SEI/SSEI (if any) here
                // TODO: use function pointer instead
                if (((int32_t)SPS_LISTING & (int32_t)pCtx->pSvcParam->eSpsPpsIdStrategy) == 0)
                {
                    if (pSvcParam->bSimulcastAVC)
                    {
                        pCtx->iEncoderError = WriteSavcParaset(pCtx, iCurDid, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
                        ++pParamInternal->uiIdrPicId;
                    }
                    else
                    {
                        pCtx->iEncoderError = WriteSsvcParaset(pCtx, iSpatialNum, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
                    }
                }
                else
                {
                    pCtx->iEncoderError = WriteSavcParaset_Listing(pCtx, iSpatialNum, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
                }
            }
        }
        return eFrameType;
    }

    /*!
     * \brief   core svc encoding process
     *
     * \pParam  pCtx            sWelsEncCtx*, encoder context
     * \pParam  pFbi            FrameBSInfo*
     * \pParam  pSrcPic         Source Picture
     * \return  EFrameType (videoFrameTypeIDR/videoFrameTypeI/videoFrameTypeP)
     */
    public static int32_t WelsEncoderEncodeExt(sWelsEncCtx* pCtx, SFrameBSInfo* pFbi, SSourcePicture* pSrcPic)
    {
        if (pCtx == null)
        {
            return ENC_RETURN_MEMALLOCERR;
        }
        SLayerBSInfo* pLayerBsInfo = (SLayerBSInfo*)&pFbi->sLayerInfo;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        SSpatialPicIndex* pSpatialIndexMap = (SSpatialPicIndex*)&pCtx->sSpatialIndexMap;
        SPicture* fsnr = null;
        SPicture* pEncPic = null; // to be decided later
        int32_t iLayerNum = 0;
        int32_t iLayerSize = 0;
        int32_t iSpatialNum =
            0; // available count number of spatial layers due to frame size changed in this given frame
        int32_t iSpatialIdx = 0; // iIndex of spatial layers due to frame size changed in this given frame
        int32_t iFrameSize = 0;
        int32_t iNalIdxInLayer = 0;
        int32_t iCountNal = 0;
        EVideoFrameType eFrameType = videoFrameTypeInvalid;
        int32_t iCurWidth = 0;
        int32_t iCurHeight = 0;
        EWelsNalUnitType eNalType = NAL_UNIT_UNSPEC_0;
        EWelsNalRefIdc eNalRefIdc = NRI_PRI_LOWEST;
        int8_t iCurDid = 0;
        int32_t iCurTid = 0;
        bool bAvcBased = false;
        SLogContext* pLogCtx = &pCtx->sLogCtx;
        float fSnrY = .0f, fSnrU = .0f, fSnrV = .0f;

        pCtx->iEncoderError = ENC_RETURN_SUCCESS;
        pCtx->bCurFrameMarkedAsSceneLtr = false;
        // Port addition: the reconstruction of this frame, set below where it is known.
        pCtx->pLastFsnr = null;
        pCtx->bLastFsnrDeblocked = false;
        pFbi->eFrameType = videoFrameTypeSkip;
        pFbi->iLayerNum = 0; // for initialization
        pFbi->uiTimeStamp = GetTimestampForRc(pSrcPic->uiTimeStamp, pCtx->uiLastTimestamp,
                                              pCtx->pSvcParam->sSpatialLayers[pCtx->pSvcParam->iSpatialLayerNum - 1].fFrameRate);
        for (int32_t iNalIdx = 0; iNalIdx < MAX_LAYER_NUM_OF_FRAME; iNalIdx++)
        {
            pFbi->sLayerInfo[iNalIdx].eFrameType = videoFrameTypeSkip;
            pFbi->sLayerInfo[iNalIdx].iNalCount = 0;
        }
        // perform csc/denoise/downsample/padding, generate spatial layers
        int32_t iRet = pCtx->pVpp->BuildSpatialPicList(pCtx, pSrcPic, &iSpatialNum);
        if (iRet != ENC_RETURN_SUCCESS)
        {
            WelsLog(&pCtx->sLogCtx, WELS_LOG_ERROR, $"Failed in BuildSpatialPicList, error={iRet}");
            return iRet;
        }

        if (pCtx->pFuncList->pfRc.pfWelsUpdateMaxBrWindowStatus != null)
        {
            pCtx->pFuncList->pfRc.pfWelsUpdateMaxBrWindowStatus(pCtx, iSpatialNum, pFbi->uiTimeStamp);
        }

        if (iSpatialNum < 1)
        {
            for (int32_t iDidIdx = 0; iDidIdx < pSvcParam->iSpatialLayerNum; iDidIdx++)
            {
                SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[iDidIdx];
                pParamInternal->iCodingIndex++;
            }
            pFbi->eFrameType = videoFrameTypeSkip;
            pLayerBsInfo->eFrameType = videoFrameTypeSkip;
            WelsLog(&pCtx->sLogCtx, WELS_LOG_DEBUG,
                    $"[Rc] Frame timestamp = {pSrcPic->uiTimeStamp}, skip one frame due to preprocessing return (temporal layer settings or else)");
            return ENC_RETURN_SUCCESS;
        }

        InitBitStream(pCtx);
        pLayerBsInfo->pBsBuf = pCtx->pFrameBs;
        pLayerBsInfo->pNalLengthInByte = pCtx->pOut->pNalLen;
        iCurDid = (int8_t)pSpatialIndexMap->iDid;
        pCtx->pCurDqLayer = pCtx->ppDqLayerList[iCurDid];
        pCtx->pCurDqLayer->pRefLayer = null;
        if (!pSvcParam->bSimulcastAVC)
        {
            eFrameType = PrepareEncodeFrame(pCtx, ref pLayerBsInfo, iSpatialNum, ref iCurDid, ref iCurTid, ref iLayerNum, ref iFrameSize,
                                            pFbi->uiTimeStamp);
            if (eFrameType == videoFrameTypeSkip)
            {
                pFbi->eFrameType = videoFrameTypeSkip;
                pLayerBsInfo->eFrameType = videoFrameTypeSkip;
                return ENC_RETURN_SUCCESS;
            }
        }
        else
        {
            for (int32_t iDidIdx = 0; iDidIdx < pSvcParam->iSpatialLayerNum; iDidIdx++)
            {
                SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[iDidIdx];
                int32_t iTemporalId = GetTemporalLevel(pParamInternal, pParamInternal->iCodingIndex,
                                                       (int32_t)pSvcParam->uiGopSize);
                if (iTemporalId == INVALID_TEMPORAL_ID)
                    pParamInternal->iCodingIndex++;
            }
        }

        while (iSpatialIdx < iSpatialNum)
        {
            iCurDid = (int8_t)(pSpatialIndexMap + iSpatialIdx)->iDid;
            SSpatialLayerConfig* pParam = &pSvcParam->sSpatialLayers[iCurDid];
            SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[iCurDid];
            int32_t iDecompositionStages = pSvcParam->sDependencyLayers[iCurDid].iDecompositionStages;
            pCtx->pCurDqLayer = pCtx->ppDqLayerList[iCurDid];
            pCtx->uiDependencyId = (uint8_t)iCurDid;

            if (pSvcParam->bSimulcastAVC)
            {
                eFrameType = PrepareEncodeFrame(pCtx, ref pLayerBsInfo, iSpatialNum, ref iCurDid, ref iCurTid, ref iLayerNum, ref iFrameSize,
                                                pFbi->uiTimeStamp);
                if (eFrameType == videoFrameTypeSkip)
                {
                    pLayerBsInfo->eFrameType = videoFrameTypeSkip;
                    ++iSpatialIdx;
                    continue;
                }
            }
            InitFrameCoding(pCtx, eFrameType, iCurDid);
            pCtx->pVpp->AnalyzeSpatialPic(pCtx, iCurDid);

            pCtx->pEncPic = pEncPic = (pSpatialIndexMap + iSpatialIdx)->pSrc;
            pCtx->pEncPic->iPictureType = (int32_t)pCtx->eSliceType;
            pCtx->pEncPic->iFramePoc = pParamInternal->iPOC;

            iCurWidth = pParam->iVideoWidth;
            iCurHeight = pParam->iVideoHeight;
            // Encoding this picture might mulitiple sQualityStat layers potentially be encoded as followed
            switch (pParam->sSliceArgument.uiSliceMode)
            {
                case SM_FIXEDSLCNUM_SLICE:
                {
                    if ((pSvcParam->iMultipleThreadIdc > 1) &&
                        (pSvcParam->bUseLoadBalancing
                         && pSvcParam->iMultipleThreadIdc >= pSvcParam->sSpatialLayers[iCurDid].sSliceArgument.uiSliceNum)
                       )
                    {
                        // AdjustEnhanceLayer/AdjustBaseLayer (slice_multi_threading): threads are rejected at init.
                        throw new NotSupportedException("WelsEncoderEncodeExt: the slice threads' load balancing is not ported (Phase 4).");
                    }

                    break;
                }
                case SM_SIZELIMITED_SLICE:
                {
                    // PicPartitionNumDecision and WelsInitCurrentDlayerMltslc: rejected at init.
                    throw new NotSupportedException("WelsEncoderEncodeExt: SM_SIZELIMITED_SLICE is not ported (Phase 4).");
                }
                default:
                {
                    break;
                }
            }

            /* coding each spatial layer, only one sQualityStat layer within spatial support */
            int32_t iSliceCount = 1;
            if (iLayerNum >= MAX_LAYER_NUM_OF_FRAME) // check available layer_bs_info writing as follows
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR,
                        $"WelsEncoderEncodeExt(), iLayerNum({iLayerNum}) overflow(max:{MAX_LAYER_NUM_OF_FRAME})!");
                return ENC_RETURN_UNSUPPORTED_PARA;
            }

            iNalIdxInLayer = 0;
            bAvcBased = ((pSvcParam->bSimulcastAVC) || (iCurDid == BASE_DEPENDENCY_ID));
            pCtx->bNeedPrefixNalFlag = ((!pSvcParam->bSimulcastAVC) && (bAvcBased &&
                                        (pSvcParam->bPrefixNalAddingCtrl ||
                                         (pSvcParam->iSpatialLayerNum > 1))));

            if (eFrameType == videoFrameTypeP)
            {
                eNalType = bAvcBased ? NAL_UNIT_CODED_SLICE : NAL_UNIT_CODED_SLICE_EXT;
            }
            else if (eFrameType == videoFrameTypeIDR)
            {
                eNalType = bAvcBased ? NAL_UNIT_CODED_SLICE_IDR : NAL_UNIT_CODED_SLICE_EXT;
            }
            if (iCurTid == 0 || pCtx->eSliceType == I_SLICE)
                eNalRefIdc = NRI_PRI_HIGHEST;
            else if (iCurTid == iDecompositionStages)
                eNalRefIdc = NRI_PRI_LOWEST;
            else if (1 + iCurTid == iDecompositionStages)
                eNalRefIdc = NRI_PRI_LOW;
            else if (2 + iCurTid == iDecompositionStages)
                eNalRefIdc = NRI_PRI_HIGH;
            else // more details for other temporal layers?
                eNalRefIdc = NRI_PRI_HIGHEST;
            pCtx->eNalType = eNalType;
            pCtx->eNalPriority = eNalRefIdc;

            pCtx->pDecPic = pCtx->ppRefPicListExt[iCurDid]->pNextBuffer;
            fsnr = pCtx->pDecPic;
            pCtx->pDecPic->iPictureType = (int32_t)pCtx->eSliceType;
            pCtx->pDecPic->iFramePoc = pParamInternal->iPOC;
            pCtx->bLastFsnrDeblocked = false; // port addition

            WelsInitCurrentLayer(pCtx, iCurWidth, iCurHeight);

            pCtx->pReferenceStrategy->MarkPic();
            if (!pCtx->pReferenceStrategy->BuildRefList(pParamInternal->iPOC, 0))
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"WelsEncoderEncodeExt(), WelsBuildRefList failed for P frames, pCtx->iNumRef0= {pCtx->iNumRef0}. ForceCodingIDR!");
                eFrameType = videoFrameTypeIDR;
                pCtx->iEncoderError = ENC_RETURN_CORRECTED;
                break;
            }
            if (pCtx->eSliceType != I_SLICE)
            {
                pCtx->pReferenceStrategy->AfterBuildRefList();
            }
            if (pSvcParam->iRCMode != RC_OFF_MODE)
                pCtx->pVpp->AnalyzePictureComplexity(pCtx, pCtx->pEncPic, ((pCtx->eSliceType == P_SLICE)
                                                     && (pCtx->iNumRef0 > 0)) ? pCtx->pRefList0[0] : null,
                                                     iCurDid, (pCtx->eSliceType == P_SLICE) && pSvcParam->bEnableBackgroundDetection);
            WelsUpdateRefSyntax(pCtx, pParamInternal->iPOC,
                                (int32_t)eFrameType); //get reordering syntax used for writing slice header and transmit to encoder.
            PrefetchReferencePicture(pCtx, eFrameType); // update reference picture for current pDq layer
            pCtx->pFuncList->pfRc.pfWelsRcPictureInit(pCtx, pFbi->uiTimeStamp);
            PreprocessSliceCoding(pCtx); // MUST be called after pfWelsRcPictureInit() and WelsInitCurrentLayer()

            //TODO Complexity Calculation here for screen content
            iLayerSize = 0;
            if (SM_SINGLE_SLICE == pParam->sSliceArgument.uiSliceMode) // only one slice within a sQualityStat layer
            {
                int32_t iSliceSize = 0;
                int32_t iPayloadSize = 0;
                SSlice* pCurSlice = &pCtx->pCurDqLayer->sSliceBufferInfo[0].pSliceBuffer[0];

                if (pCtx->bNeedPrefixNalFlag)
                {
                    pCtx->iEncoderError = AddPrefixNal(pCtx, pLayerBsInfo, &pLayerBsInfo->pNalLengthInByte[0], &iNalIdxInLayer, eNalType,
                                                       eNalRefIdc,
                                                       ref iPayloadSize);
                    if (pCtx->iEncoderError != ENC_RETURN_SUCCESS) return pCtx->iEncoderError;
                    iLayerSize += iPayloadSize;
                }

                WelsLoadNal(pCtx->pOut, (int32_t)eNalType, (int32_t)eNalRefIdc);
                Debug.Assert(0 == (int)pCurSlice->iSliceIdx);
                pCtx->iEncoderError = SetSliceBoundaryInfo(pCtx->pCurDqLayer, pCurSlice, 0);
                if (pCtx->iEncoderError != ENC_RETURN_SUCCESS) return pCtx->iEncoderError;

                pCtx->iEncoderError = WelsCodeOneSlice(pCtx, pCurSlice, (int32_t)eNalType);
                if (pCtx->iEncoderError != ENC_RETURN_SUCCESS) return pCtx->iEncoderError;

                WelsUnloadNal(pCtx->pOut);

                pCtx->iEncoderError = WelsEncodeNal(&pCtx->pOut->sNalList[pCtx->pOut->iNalIndex - 1],
                                                    &pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt,
                                                    pCtx->iFrameBsSize - pCtx->iPosBsBuffer,
                                                    pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                                    &pLayerBsInfo->pNalLengthInByte[iNalIdxInLayer]);
                if (pCtx->iEncoderError != ENC_RETURN_SUCCESS) return pCtx->iEncoderError;
                iSliceSize = pLayerBsInfo->pNalLengthInByte[iNalIdxInLayer];

                iLayerSize += iSliceSize;
                pCtx->iPosBsBuffer += iSliceSize;
                pLayerBsInfo->uiLayerType = (uint8_t)VIDEO_CODING_LAYER;
                pLayerBsInfo->uiSpatialId = (uint8_t)iCurDid;
                pLayerBsInfo->uiTemporalId = (uint8_t)iCurTid;
                pLayerBsInfo->uiQualityId = 0;
                pLayerBsInfo->iNalCount = ++iNalIdxInLayer;
                pLayerBsInfo->eFrameType = eFrameType;
                pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, eFrameType);
            }
            // for dynamic slicing single threading..
            else if ((SM_SIZELIMITED_SLICE == pParam->sSliceArgument.uiSliceMode) && (pSvcParam->iMultipleThreadIdc <= 1))
            {
                // WelsCodeOnePicPartition: rejected at init.
                throw new NotSupportedException("WelsEncoderEncodeExt: SM_SIZELIMITED_SLICE is not ported (Phase 4).");
            }
            else
            {
                //other multi-slice uiSliceMode
                // THREAD_FULLY_FIRE_MODE/THREAD_PICK_UP_MODE for any mode of non-SM_SIZELIMITED_SLICE
                if ((SM_SIZELIMITED_SLICE != pParam->sSliceArgument.uiSliceMode) && (pSvcParam->iMultipleThreadIdc > 1))
                {
                    // pTaskManage->ExecuteTasks and AppendSliceToFrameBs: threads are rejected at init.
                    throw new NotSupportedException("WelsEncoderEncodeExt: slice threads are not ported (Phase 4).");
                }
                // THREAD_FULLY_FIRE_MODE && SM_SIZELIMITED_SLICE
                else if ((SM_SIZELIMITED_SLICE == pParam->sSliceArgument.uiSliceMode) && (pSvcParam->iMultipleThreadIdc > 1))
                {
                    // InitAllSlicesInThread and SliceLayerInfoUpdate: rejected at init.
                    throw new NotSupportedException("WelsEncoderEncodeExt: threaded SM_SIZELIMITED_SLICE is not ported (Phase 4).");
                }
                else // for non-dynamic-slicing mode single threading branch..
                {
                    bool bNeedPrefix = pCtx->bNeedPrefixNalFlag;
                    int32_t iSliceIdx = 0;
                    SSlice* pCurSlice = null;

                    iSliceCount = GetCurrentSliceNum(pCtx->pCurDqLayer);
                    while (iSliceIdx < iSliceCount)
                    {
                        int32_t iSliceSize = 0;
                        int32_t iPayloadSize = 0;

                        if (bNeedPrefix)
                        {
                            pCtx->iEncoderError = AddPrefixNal(pCtx, pLayerBsInfo, &pLayerBsInfo->pNalLengthInByte[0], &iNalIdxInLayer, eNalType,
                                                               eNalRefIdc,
                                                               ref iPayloadSize);
                            if (pCtx->iEncoderError != ENC_RETURN_SUCCESS) return pCtx->iEncoderError;
                            iLayerSize += iPayloadSize;
                        }

                        WelsLoadNal(pCtx->pOut, (int32_t)eNalType, (int32_t)eNalRefIdc);

                        pCurSlice = &pCtx->pCurDqLayer->sSliceBufferInfo[0].pSliceBuffer[iSliceIdx];
                        Debug.Assert(iSliceIdx == pCurSlice->iSliceIdx);
                        pCtx->iEncoderError = SetSliceBoundaryInfo(pCtx->pCurDqLayer, pCurSlice, iSliceIdx);

                        pCtx->iEncoderError = WelsCodeOneSlice(pCtx, pCurSlice, (int32_t)eNalType);
                        if (pCtx->iEncoderError != ENC_RETURN_SUCCESS) return pCtx->iEncoderError;

                        WelsUnloadNal(pCtx->pOut);

                        pCtx->iEncoderError = WelsEncodeNal(&pCtx->pOut->sNalList[pCtx->pOut->iNalIndex - 1],
                                                            &pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt,
                                                            pCtx->iFrameBsSize - pCtx->iPosBsBuffer,
                                                            pCtx->pFrameBs + pCtx->iPosBsBuffer, &pLayerBsInfo->pNalLengthInByte[iNalIdxInLayer]);
                        if (pCtx->iEncoderError != ENC_RETURN_SUCCESS) return pCtx->iEncoderError;
                        iSliceSize = pLayerBsInfo->pNalLengthInByte[iNalIdxInLayer];

                        pCtx->iPosBsBuffer += iSliceSize;
                        iLayerSize += iSliceSize;

                        ++iNalIdxInLayer;
                        ++iSliceIdx;
                    }

                    pLayerBsInfo->uiLayerType = (uint8_t)VIDEO_CODING_LAYER;
                    pLayerBsInfo->uiSpatialId = (uint8_t)iCurDid;
                    pLayerBsInfo->uiTemporalId = (uint8_t)iCurTid;
                    pLayerBsInfo->uiQualityId = 0;
                    pLayerBsInfo->iNalCount = iNalIdxInLayer;
                    pLayerBsInfo->eFrameType = eFrameType;
                    pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, eFrameType);
                }
            }

            if (null != pCtx->pFuncList->pfRc.pfWelsRcPostFrameSkipping
                && pCtx->pFuncList->pfRc.pfWelsRcPostFrameSkipping(pCtx, iCurDid, pFbi->uiTimeStamp))
            {

                StackBackEncoderStatus(pCtx, eFrameType);
                ClearFrameBsInfo(pCtx, pFbi);

                iFrameSize = 0;
                iLayerSize = 0;
                iLayerNum = 0;

                if (pCtx->pFuncList->pfRc.pfWelsUpdateBufferWhenSkip != null)
                {
                    pCtx->pFuncList->pfRc.pfWelsUpdateBufferWhenSkip(pCtx, iSpatialNum);
                }

                WelsRcPostFrameSkippedUpdate(pCtx, iCurDid);
                pCtx->iEncoderError = ENC_RETURN_SUCCESS;
                return ENC_RETURN_SUCCESS;
            }

            // deblocking filter
            if (
                (!pCtx->pCurDqLayer->bDeblockingParallelFlag) &&
                ((eNalRefIdc != NRI_PRI_LOWEST) && (pSvcParam->sDependencyLayers[iCurDid].iHighestTemporalId == 0
                                                    || iCurTid < pSvcParam->sDependencyLayers[iCurDid].iHighestTemporalId)) &&
                true
            )
            {
                PerformDeblockingFilter(pCtx);
                pCtx->bLastFsnrDeblocked = true; // port addition
            }

            pCtx->pFuncList->pfRc.pfWelsRcPictureInfoUpdate(pCtx, iLayerSize);
            iFrameSize += iLayerSize;
            RcTraceFrameBits(pCtx, pFbi->uiTimeStamp, iFrameSize);
            pCtx->pDecPic->iFrameAverageQp = pCtx->pWelsSvcRc[iCurDid].iAverageFrameQp;

            //update scc related
            pCtx->pFuncList->pfUpdateFMESwitch(pCtx->pCurDqLayer);

            // reference picture list update
            if (eNalRefIdc != NRI_PRI_LOWEST)
            {
                if (!pCtx->pReferenceStrategy->UpdateRefList())
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING, "WelsEncoderEncodeExt(), WelsUpdateRefList failed. ForceCodingIDR!");
                    //the above is to set the next frame to be IDR
                    pCtx->iEncoderError = ENC_RETURN_CORRECTED;
                    break;
                }
            }

            //check MinCr
            {
                int32_t iMinCrFrameSize = (pParam->iVideoWidth * pParam->iVideoHeight * 3) >> 2; //MinCr = 2;
                if (pParam->uiLevelIdc == LEVEL_3_1 || pParam->uiLevelIdc == LEVEL_3_2 || pParam->uiLevelIdc == LEVEL_4_0)
                    iMinCrFrameSize >>= 1; //MinCr = 4
                if (iFrameSize > iMinCrFrameSize)
                    WelsLog(pLogCtx, WELS_LOG_WARNING,
                            "WelsEncoderEncodeExt()MinCr Checking,codec bitstream size is larger than Level limitation");
            }

            if (fsnr != null && (pSvcParam->bPsnrY || pSrcPic->bPsnrY))
            {
                fSnrY = WelsCalcPsnr(fsnr->pData[0],
                                     fsnr->iLineSize[0],
                                     pEncPic->pData[0],
                                     pEncPic->iLineSize[0],
                                     iCurWidth,
                                     iCurHeight);
            }
            if (fsnr != null && (pSvcParam->bPsnrU || pSrcPic->bPsnrU))
            {
                fSnrU = WelsCalcPsnr(fsnr->pData[1],
                                     fsnr->iLineSize[1],
                                     pEncPic->pData[1],
                                     pEncPic->iLineSize[1],
                                     (iCurWidth >> 1),
                                     (iCurHeight >> 1));
            }
            if (fsnr != null && (pSvcParam->bPsnrV || pSrcPic->bPsnrV))
            {
                fSnrV = WelsCalcPsnr(fsnr->pData[2],
                                     fsnr->iLineSize[2],
                                     pEncPic->pData[2],
                                     pEncPic->iLineSize[2],
                                     (iCurWidth >> 1),
                                     (iCurHeight >> 1));
            }

            pLayerBsInfo->rPsnr[0] = 0;
            pLayerBsInfo->rPsnr[1] = 0;
            pLayerBsInfo->rPsnr[2] = 0;
            if (pSrcPic->bPsnrY)
            {
                pLayerBsInfo->rPsnr[0] = fSnrY;
            }
            if (pSrcPic->bPsnrU)
            {
                pLayerBsInfo->rPsnr[1] = fSnrU;
            }
            if (pSrcPic->bPsnrV)
            {
                pLayerBsInfo->rPsnr[2] = fSnrV;
            }

            iCountNal = pLayerBsInfo->iNalCount;
            ++iLayerNum;
            ++pLayerBsInfo;
            ++pCtx->pOut->iLayerBsIndex;
            pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
            pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + iCountNal;

            if (pSvcParam->iPaddingFlag != 0 && pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingSize > 0)
            {
                int32_t iPaddingNalSize = 0;
                pCtx->iEncoderError = WritePadding(pCtx, pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingSize, ref iPaddingNalSize);
                if (pCtx->iEncoderError != ENC_RETURN_SUCCESS) return pCtx->iEncoderError;

                if (iPaddingNalSize <= 0)
                    return ENC_RETURN_UNEXPECTED;

                pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingBitrateStat += pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingSize;

                pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingSize = 0;

                pLayerBsInfo->uiSpatialId = 0;
                pLayerBsInfo->uiTemporalId = 0;
                pLayerBsInfo->uiQualityId = 0;
                pLayerBsInfo->uiLayerType = (uint8_t)NON_VIDEO_CODING_LAYER;
                pLayerBsInfo->iNalCount = 1;
                pLayerBsInfo->pNalLengthInByte[0] = iPaddingNalSize;
                pLayerBsInfo->eFrameType = eFrameType;
                pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, eFrameType);
                ++pLayerBsInfo;
                ++pCtx->pOut->iLayerBsIndex;
                pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
                pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + 1;
                ++iLayerNum;

                iFrameSize += iPaddingNalSize;
            }

            if ((pParam->sSliceArgument.uiSliceMode == SM_FIXEDSLCNUM_SLICE)
                && pSvcParam->bUseLoadBalancing
                && pSvcParam->iMultipleThreadIdc > 1 &&
                pSvcParam->iMultipleThreadIdc >= pParam->sSliceArgument.uiSliceNum)
            {
                // CalcSliceComplexRatio (slice_multi_threading): threads are rejected at init.
                throw new NotSupportedException("WelsEncoderEncodeExt: the slice threads' load balancing is not ported (Phase 4).");
            }

            pCtx->eLastNalPriority[iCurDid] = eNalRefIdc;
            ++iSpatialIdx;

            if (iCurDid + 1 < pSvcParam->iSpatialLayerNum)
            {
                //for next layer, note that iSpatialIdx has been ++ so it is pointer to next layer
                WelsSwapDqLayers(pCtx, (pSpatialIndexMap + iSpatialIdx)->iDid);
            }

            if (pCtx->pVpp->UpdateSpatialPictures(pCtx, pSvcParam, (int8_t)iCurTid, iCurDid) != 0)
            {
                ForceCodingIDR(pCtx, iCurDid);
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        "WelsEncoderEncodeExt(), Logic Error Found in Preprocess updating. ForceCodingIDR!");
                //the above is to set the next frame IDR
                pFbi->eFrameType = eFrameType;
                pLayerBsInfo->eFrameType = eFrameType;
                return ENC_RETURN_CORRECTED;
            }

            if (pSvcParam->bEnableLongTermReference && ((pCtx->pLtr[pCtx->uiDependencyId].bLTRMarkingFlag
                && (pCtx->pLtr[pCtx->uiDependencyId].iLTRMarkMode == LTR_DIRECT_MARK)) || eFrameType == videoFrameTypeIDR))
            {
                pCtx->bRefOfCurTidIsLtr[iCurDid * MAX_TEMPORAL_LEVEL + iCurTid] = true;
            }
            if (pSvcParam->bSimulcastAVC)
                ++pParamInternal->iCodingIndex;
        }//end of (iSpatialIdx/iSpatialNum)

        // Port addition: where ENABLE_FRAME_DUMP's DumpDependencyRec writes fsnr.
        pCtx->pLastFsnr = fsnr;

        if (!pSvcParam->bSimulcastAVC)
        {
            for (int32_t i = 0; i < pSvcParam->iSpatialLayerNum; i++)
            {
                SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[i];
                pParamInternal->iCodingIndex++;
            }
        }

        if (ENC_RETURN_CORRECTED == pCtx->iEncoderError)
        {
            pCtx->pVpp->UpdateSpatialPictures(pCtx, pSvcParam, (int8_t)iCurTid, (pSpatialIndexMap + iSpatialIdx)->iDid);
            ForceCodingIDR(pCtx, (pSpatialIndexMap + iSpatialIdx)->iDid);
            WelsLog(pLogCtx, WELS_LOG_ERROR, "WelsEncoderEncodeExt(), Logic Error Found in temporal level. ForceCodingIDR!");
            //the above is to set the next frame IDR
            pFbi->eFrameType = eFrameType;
            pLayerBsInfo->eFrameType = eFrameType;
            return ENC_RETURN_CORRECTED;
        }

        // to check number of layers / nals / slices dependencies
        if (iLayerNum > MAX_LAYER_NUM_OF_FRAME)
        {
            WelsLog(&pCtx->sLogCtx, WELS_LOG_ERROR,
                    $"WelsEncoderEncodeExt(), iLayerNum({iLayerNum}) > MAX_LAYER_NUM_OF_FRAME({MAX_LAYER_NUM_OF_FRAME})!");
            return 1;
        }

        pFbi->iLayerNum = iLayerNum;

        WelsLog(pLogCtx, WELS_LOG_DEBUG, $"WelsEncoderEncodeExt() OutputInfo iLayerNum = {iLayerNum},iFrameSize = {iFrameSize}");
        for (int32_t i = 0; i < iLayerNum; i++)
            WelsLog(pLogCtx, WELS_LOG_DEBUG,
                    $"WelsEncoderEncodeExt() OutputInfo iLayerId = {i},iNalType = {pFbi->sLayerInfo[i].uiLayerType},iNalCount = {pFbi->sLayerInfo[i].iNalCount}, first Nal Length={pFbi->sLayerInfo[i].pNalLengthInByte[0]},uiSpatialId = {pFbi->sLayerInfo[i].uiSpatialId},uiTemporalId = {pFbi->sLayerInfo[i].uiTemporalId},iSubSeqId = {pFbi->sLayerInfo[i].iSubSeqId}");
        // WelsEmms: no MMX state in the port.

        pLayerBsInfo->eFrameType = eFrameType;
        pFbi->iFrameSizeInBytes = iFrameSize;
        pFbi->eFrameType = eFrameType;
        for (int32_t k = 0; k < pFbi->iLayerNum; k++)
        {
            if (pFbi->eFrameType != pFbi->sLayerInfo[k].eFrameType)
            {
                pFbi->eFrameType = videoFrameTypeIPMixed;
            }
        }
        // The _DEBUG checks are not in the release build the reference uses.
        return ENC_RETURN_SUCCESS;
    }

    /*!
     * \brief   Wels SVC encoder parameters adjustment
     *          SVC adjustment results in new requirement in memory blocks adjustment
     */
    public static int32_t WelsEncoderParamAdjust(sWelsEncCtx** ppCtx, SWelsSvcCodingParam* pNewParam)
    {
        SWelsSvcCodingParam* pOldParam = null;
        int32_t iReturn = ENC_RETURN_SUCCESS;
        int8_t iIndexD = 0;
        bool bNeedReset = false;
        int16_t iSliceNum = 1; // number of slices used
        int32_t iCacheLineSize = 16; // on chip cache line size in byte
        uint32_t uiCpuFeatureFlags = 0;

        if (null == ppCtx || null == *ppCtx || null == pNewParam) return 1;

        /* Check validation in new parameters */
        iReturn = ParamValidationExt(&(*ppCtx)->sLogCtx, pNewParam);
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn;

        iReturn = GetMultipleThreadIdc(&(*ppCtx)->sLogCtx, pNewParam, ref iSliceNum, ref iCacheLineSize, ref uiCpuFeatureFlags);
        if (iReturn != ENC_RETURN_SUCCESS)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_ERROR, $"WelsEncoderParamAdjust(), GetMultipleThreadIdc failed return {iReturn}.");
            return iReturn;
        }

        pOldParam = (*ppCtx)->pSvcParam;

        if (pOldParam->iUsageType != pNewParam->iUsageType)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_ERROR,
                    $"WelsEncoderParamAdjust(), does not expect in-middle change of iUsgaeType from {(int)pOldParam->iUsageType} to {(int)pNewParam->iUsageType}");
            return ENC_RETURN_UNSUPPORTED_PARA;
        }

        /* Decide whether need reset for IDR frame based on adjusting prarameters changed */
        /* Temporal levels, spatial settings and/ or quality settings changed need update parameter sets related. */
        bNeedReset = (pOldParam == null) ||
                     (pOldParam->bSimulcastAVC != pNewParam->bSimulcastAVC) ||
                     (pOldParam->iSpatialLayerNum != pNewParam->iSpatialLayerNum) ||
                     (pOldParam->iPicWidth != pNewParam->iPicWidth
                      || pOldParam->iPicHeight != pNewParam->iPicHeight) ||
                     (pOldParam->SUsedPicRect.iWidth != pNewParam->SUsedPicRect.iWidth
                      || pOldParam->SUsedPicRect.iHeight != pNewParam->SUsedPicRect.iHeight) ||
                     (pOldParam->bEnableLongTermReference != pNewParam->bEnableLongTermReference) ||
                     (pOldParam->iLTRRefNum != pNewParam->iLTRRefNum) ||
                     (pOldParam->iMultipleThreadIdc != pNewParam->iMultipleThreadIdc) ||
                     (pOldParam->bEnableBackgroundDetection != pNewParam->bEnableBackgroundDetection) ||
                     (pOldParam->bEnableAdaptiveQuant != pNewParam->bEnableAdaptiveQuant) ||
                     (pOldParam->eSpsPpsIdStrategy != pNewParam->eSpsPpsIdStrategy);
        if ((pNewParam->iMaxNumRefFrame > pOldParam->iMaxNumRefFrame) ||
            ((pOldParam->iMaxNumRefFrame == 1) && (pOldParam->iTemporalLayerNum == 1) && (pNewParam->iTemporalLayerNum == 2)))
        {
            bNeedReset = true;
        }
        if (bNeedReset)
        {
            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                    $"WelsEncoderParamAdjust(),bSimulcastAVC({(pOldParam->bSimulcastAVC ? 1 : 0)},{(pNewParam->bSimulcastAVC ? 1 : 0)}),iSpatialLayerNum({pOldParam->iSpatialLayerNum},{pNewParam->iSpatialLayerNum}),iPicWidth({pOldParam->iPicWidth},{pNewParam->iPicWidth}),iPicHeight({pOldParam->iPicHeight},{pNewParam->iPicHeight}),Rect.iWidth({pOldParam->SUsedPicRect.iWidth},{pNewParam->SUsedPicRect.iWidth}),Rect.iHeight({pOldParam->SUsedPicRect.iHeight},{pNewParam->SUsedPicRect.iHeight})");

            WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                    $"WelsEncoderParamAdjust(),bEnableLongTermReference({(pOldParam->bEnableLongTermReference ? 1 : 0)},{(pNewParam->bEnableLongTermReference ? 1 : 0)}),iLTRRefNum({pOldParam->iLTRRefNum},{pNewParam->iLTRRefNum}),iMultipleThreadIdc({pOldParam->iMultipleThreadIdc},{pNewParam->iMultipleThreadIdc}),bEnableBackgroundDetection({(pOldParam->bEnableBackgroundDetection ? 1 : 0)},{(pNewParam->bEnableBackgroundDetection ? 1 : 0)}),bEnableAdaptiveQuant({(pOldParam->bEnableAdaptiveQuant ? 1 : 0)},{(pNewParam->bEnableAdaptiveQuant ? 1 : 0)}),eSpsPpsIdStrategy({(int)pOldParam->eSpsPpsIdStrategy},{(int)pNewParam->eSpsPpsIdStrategy}),iMaxNumRefFrame({pOldParam->iMaxNumRefFrame},{pNewParam->iMaxNumRefFrame}),iTemporalLayerNum({pOldParam->iTemporalLayerNum},{pNewParam->iTemporalLayerNum})");
        }
        if (!bNeedReset) // Check its picture resolutions/quality settings respectively in each dependency layer
        {
            iIndexD = 0;
            Debug.Assert(pOldParam->iSpatialLayerNum == pNewParam->iSpatialLayerNum);
            do
            {
                SSpatialLayerInternal* kpOldDlp = &pOldParam->sDependencyLayers[iIndexD];
                SSpatialLayerInternal* kpNewDlp = &pNewParam->sDependencyLayers[iIndexD];
                float fT1 = .0f;
                float fT2 = .0f;

                // check frame size settings
                if (pOldParam->sSpatialLayers[iIndexD].iVideoWidth != pNewParam->sSpatialLayers[iIndexD].iVideoWidth ||
                    pOldParam->sSpatialLayers[iIndexD].iVideoHeight != pNewParam->sSpatialLayers[iIndexD].iVideoHeight ||
                    kpOldDlp->iActualWidth != kpNewDlp->iActualWidth ||
                    kpOldDlp->iActualHeight != kpNewDlp->iActualHeight)
                {
                    bNeedReset = true;
                    WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                            $"WelsEncoderParamAdjust(),iIndexD = {iIndexD},sSpatialLayers.wxh_old({pOldParam->sSpatialLayers[iIndexD].iVideoWidth},{pOldParam->sSpatialLayers[iIndexD].iVideoHeight}),sSpatialLayers.wxh_new({pNewParam->sSpatialLayers[iIndexD].iVideoWidth},{pNewParam->sSpatialLayers[iIndexD].iVideoHeight}),iActualwxh_old({kpOldDlp->iActualWidth},{kpOldDlp->iActualHeight}),iActualwxh_new({kpNewDlp->iActualWidth},{kpNewDlp->iActualHeight})");
                    break;
                }

                if (pOldParam->sSpatialLayers[iIndexD].sSliceArgument.uiSliceMode !=
                    pNewParam->sSpatialLayers[iIndexD].sSliceArgument.uiSliceMode
                    ||
                    pOldParam->sSpatialLayers[iIndexD].sSliceArgument.uiSliceNum !=
                    pNewParam->sSpatialLayers[iIndexD].sSliceArgument.uiSliceNum)
                {

                    bNeedReset = true;
                    WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                            $"WelsEncoderParamAdjust(),iIndexD = {iIndexD},uiSliceMode ({(int)pOldParam->sSpatialLayers[iIndexD].sSliceArgument.uiSliceMode},{(int)pNewParam->sSpatialLayers[iIndexD].sSliceArgument.uiSliceMode}),uiSliceNum({pOldParam->sSpatialLayers[iIndexD].sSliceArgument.uiSliceNum},{pNewParam->sSpatialLayers[iIndexD].sSliceArgument.uiSliceNum})");

                    break;
                }

                // check frame rate
                // we can not check whether corresponding fFrameRate is equal or not,
                // only need to check d_max/d_min and max_fr/d_max whether it is equal or not
                if (kpNewDlp->fInputFrameRate > EPSN && kpOldDlp->fInputFrameRate > EPSN)
                    fT1 = kpNewDlp->fOutputFrameRate / kpNewDlp->fInputFrameRate - kpOldDlp->fOutputFrameRate / kpOldDlp->fInputFrameRate;
                if (kpNewDlp->fOutputFrameRate > EPSN && kpOldDlp->fOutputFrameRate > EPSN)
                    fT2 = pNewParam->fMaxFrameRate / kpNewDlp->fOutputFrameRate - pOldParam->fMaxFrameRate / kpOldDlp->fOutputFrameRate;
                if (fT1 > EPSN || fT1 < -EPSN || fT2 > EPSN || fT2 < -EPSN)
                {
                    bNeedReset = true;
                    WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                            $"WelsEncoderParamAdjust() iIndexD = {iIndexD},fInputFrameRate({kpOldDlp->fInputFrameRate:F6},{kpNewDlp->fInputFrameRate:F6}),fOutputFrameRate({kpOldDlp->fOutputFrameRate:F6},{kpNewDlp->fOutputFrameRate:F6}),fMaxFrameRate({pOldParam->fMaxFrameRate:F6},{pNewParam->fMaxFrameRate:F6})");
                    break;
                }
                if (pOldParam->sSpatialLayers[iIndexD].uiProfileIdc != pNewParam->sSpatialLayers[iIndexD].uiProfileIdc)
                {
                    bNeedReset = true;
                    WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                            $"WelsEncoderParamAdjust(),iIndexD = {iIndexD},uiProfileIdc({(int)pOldParam->sSpatialLayers[iIndexD].uiProfileIdc},{(int)pNewParam->sSpatialLayers[iIndexD].uiProfileIdc})");
                    break;
                }
                //check level change,if new level is smaller than old level,don't reset encoder. still use old level.

                if (pNewParam->sSpatialLayers[iIndexD].uiLevelIdc > pOldParam->sSpatialLayers[iIndexD].uiLevelIdc)
                {
                    bNeedReset = true;
                    WelsLog(&(*ppCtx)->sLogCtx, WELS_LOG_INFO,
                            $"WelsEncoderParamAdjust(),iIndexD = {iIndexD},uiLevelIdc({(int)pOldParam->sSpatialLayers[iIndexD].uiLevelIdc},{(int)pNewParam->sSpatialLayers[iIndexD].uiLevelIdc})");
                    break;
                }
                ++iIndexD;
            } while (iIndexD < pOldParam->iSpatialLayerNum);
        }

        if (bNeedReset)
        {
            SLogContext sLogCtx = (*ppCtx)->sLogCtx;

            int32_t iOldSpsPpsIdStrategy = (int32_t)pOldParam->eSpsPpsIdStrategy;
            SParaSetOffsetVariable* sTmpPsoVariable = stackalloc SParaSetOffsetVariable[PARA_SET_TYPE];
            int32_t* iTmpPpsIdList = stackalloc int32_t[MAX_DQ_LAYER_NUM * MAX_PPS_COUNT];
            //for LTR or SPS,PPS ID update
            uint16_t uiMaxIdrPicId = 0;
            for (iIndexD = 0; iIndexD < pOldParam->iSpatialLayerNum; iIndexD++)
            {
                if (pOldParam->sDependencyLayers[iIndexD].uiIdrPicId > uiMaxIdrPicId)
                    uiMaxIdrPicId = pOldParam->sDependencyLayers[iIndexD].uiIdrPicId;
            }

            //for sEncoderStatistics
            InlineArray4<SEncoderStatistics> sTempEncoderStatistics = (*ppCtx)->sEncoderStatistics; // memcpy
            int64_t uiStartTimestamp = (*ppCtx)->uiStartTimestamp;
            int32_t iStatisticsLogInterval = (*ppCtx)->iStatisticsLogInterval;
            int64_t iLastStatisticsLogTs = (*ppCtx)->iLastStatisticsLogTs;
            //for sEncoderStatistics

            SExistingParasetList sExistingParasetList;
            SExistingParasetList* pExistingParasetList = null;

            if (((int32_t)CONSTANT_ID != iOldSpsPpsIdStrategy) && (CONSTANT_ID != pNewParam->eSpsPpsIdStrategy))
            {
                (*ppCtx)->pFuncList->pParametersetStrategy->OutputCurrentStructure(sTmpPsoVariable, iTmpPpsIdList, (*ppCtx),
                    &sExistingParasetList);

                if ((((int32_t)SPS_LISTING & iOldSpsPpsIdStrategy) != 0)
                    && (((int32_t)SPS_LISTING & (int32_t)pNewParam->eSpsPpsIdStrategy) != 0))
                {
                    pExistingParasetList = &sExistingParasetList;
                }
            }

            WelsUninitEncoderExt(ppCtx);

            /* Update new parameters */
            if (WelsInitEncoderExt(ppCtx, pNewParam, &sLogCtx, pExistingParasetList) != 0)
                return 1;
            //if WelsInitEncoderExt succeed
            //for LTR or SPS,PPS ID update
            for (iIndexD = 0; iIndexD < pNewParam->iSpatialLayerNum; iIndexD++)
            {
                (*ppCtx)->pSvcParam->sDependencyLayers[iIndexD].uiIdrPicId = uiMaxIdrPicId;
            }

            //for sEncoderStatistics
            (*ppCtx)->sEncoderStatistics = sTempEncoderStatistics; // memcpy
            (*ppCtx)->uiStartTimestamp = uiStartTimestamp;
            (*ppCtx)->iStatisticsLogInterval = iStatisticsLogInterval;
            (*ppCtx)->iLastStatisticsLogTs = iLastStatisticsLogTs;
            //for sEncoderStatistics

            //load back the needed structure for eSpsPpsIdStrategy
            if ((((int32_t)CONSTANT_ID != iOldSpsPpsIdStrategy) && (CONSTANT_ID != pNewParam->eSpsPpsIdStrategy))
                || (((int32_t)SPS_PPS_LISTING == iOldSpsPpsIdStrategy)
                    && (SPS_PPS_LISTING == pNewParam->eSpsPpsIdStrategy)))
            {
                (*ppCtx)->pFuncList->pParametersetStrategy->LoadPreviousStructure(sTmpPsoVariable, iTmpPpsIdList);
            }
        }
        else
        {
            /* maybe adjustment introduced in bitrate or little settings adjustment and so on.. */
            pNewParam->iNumRefFrame = WELS_CLIP3(pNewParam->iNumRefFrame, MIN_REF_PIC_COUNT,
                                                 (pNewParam->iUsageType == CAMERA_VIDEO_REAL_TIME ? MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA :
                                                  MAX_REFERENCE_PICTURE_COUNT_NUM_SCREEN));
            pNewParam->iLoopFilterDisableIdc = WELS_CLIP3(pNewParam->iLoopFilterDisableIdc, 0, 6);
            pNewParam->iLoopFilterAlphaC0Offset = WELS_CLIP3(pNewParam->iLoopFilterAlphaC0Offset, -6, 6);
            pNewParam->iLoopFilterBetaOffset = WELS_CLIP3(pNewParam->iLoopFilterBetaOffset, -6, 6);
            pNewParam->fMaxFrameRate = WELS_CLIP3(pNewParam->fMaxFrameRate, (float)MIN_FRAME_RATE, (float)MAX_FRAME_RATE);

            // we can not use direct struct based memcpy due some fields need keep unchanged as before
            pOldParam->fMaxFrameRate = pNewParam->fMaxFrameRate;             // maximal frame rate [Hz / fps]
            pOldParam->iComplexityMode = pNewParam->iComplexityMode;                   // color space of input sequence
            pOldParam->uiIntraPeriod = pNewParam->uiIntraPeriod;             // intra period (multiple of GOP size as desired)
            pOldParam->eSpsPpsIdStrategy = pNewParam->eSpsPpsIdStrategy;
            pOldParam->bPrefixNalAddingCtrl = pNewParam->bPrefixNalAddingCtrl;
            pOldParam->iNumRefFrame = pNewParam->iNumRefFrame;              // number of reference frame used
            pOldParam->uiGopSize = pNewParam->uiGopSize;
            if (pOldParam->iTemporalLayerNum != pNewParam->iTemporalLayerNum)
            {
                pOldParam->iTemporalLayerNum = pNewParam->iTemporalLayerNum;
                for (int32_t iIndexD2 = 0; iIndexD2 < MAX_DEPENDENCY_LAYER; iIndexD2++)
                    pOldParam->sDependencyLayers[iIndexD2].iCodingIndex = 0;
            }
            pOldParam->iDecompStages = pNewParam->iDecompStages;
            /* denoise control */
            pOldParam->bEnableDenoise = pNewParam->bEnableDenoise;

            /* background detection control */
            pOldParam->bEnableBackgroundDetection = pNewParam->bEnableBackgroundDetection;

            /* adaptive quantization control */
            pOldParam->bEnableAdaptiveQuant = pNewParam->bEnableAdaptiveQuant;

            /* int32_t term reference control */
            pOldParam->bEnableLongTermReference = pNewParam->bEnableLongTermReference;
            pOldParam->iLtrMarkPeriod = pNewParam->iLtrMarkPeriod;

            // keep below values unchanged as before
            pOldParam->bEnableSSEI = pNewParam->bEnableSSEI;
            pOldParam->bSimulcastAVC = pNewParam->bSimulcastAVC;
            pOldParam->bEnableFrameCroppingFlag = pNewParam->bEnableFrameCroppingFlag;  // enable frame cropping flag

            /* Motion search */

            /* Deblocking loop filter */
            pOldParam->iLoopFilterDisableIdc =
                pNewParam->iLoopFilterDisableIdc;     // 0: on, 1: off, 2: on except for slice boundaries
            pOldParam->iLoopFilterAlphaC0Offset = pNewParam->iLoopFilterAlphaC0Offset;// AlphaOffset: valid range [-6, 6], default 0
            pOldParam->iLoopFilterBetaOffset =
                pNewParam->iLoopFilterBetaOffset;     // BetaOffset:  valid range [-6, 6], default 0

            /* Rate Control */
            pOldParam->iRCMode = pNewParam->iRCMode;
            pOldParam->iTargetBitrate =
                pNewParam->iTargetBitrate;                    // overall target bitrate introduced in RC module
            pOldParam->iPaddingFlag = pNewParam->iPaddingFlag;

            /* Layer definition */
            pOldParam->bPrefixNalAddingCtrl = pNewParam->bPrefixNalAddingCtrl;

            // d
            iIndexD = 0;
            do
            {
                SSpatialLayerInternal* pOldDlpInternal = &pOldParam->sDependencyLayers[iIndexD];
                SSpatialLayerInternal* pNewDlpInternal = &pNewParam->sDependencyLayers[iIndexD];

                SSpatialLayerConfig* pOldDlp = &pOldParam->sSpatialLayers[iIndexD];
                SSpatialLayerConfig* pNewDlp = &pNewParam->sSpatialLayers[iIndexD];

                pOldDlpInternal->fInputFrameRate = pNewDlpInternal->fInputFrameRate;     // input frame rate
                pOldDlpInternal->fOutputFrameRate = pNewDlpInternal->fOutputFrameRate;    // output frame rate
                pOldDlp->iSpatialBitrate = pNewDlp->iSpatialBitrate;
                pOldDlp->iMaxSpatialBitrate = pNewDlp->iMaxSpatialBitrate;
                pOldDlp->uiProfileIdc =
                    pNewDlp->uiProfileIdc;                        // value of profile IDC (0 for auto-detection)
                pOldDlp->iDLayerQp = pNewDlp->iDLayerQp;

                /* Derived variants below */
                pOldDlpInternal->iTemporalResolution = pNewDlpInternal->iTemporalResolution;
                pOldDlpInternal->iDecompositionStages = pNewDlpInternal->iDecompositionStages;
                const int kiCodingIdx2TemporalIdSize = (1 << MAX_TEMPORAL_LEVEL) + 1;
                Buffer.MemoryCopy(pNewDlpInternal->uiCodingIdx2TemporalId, pOldDlpInternal->uiCodingIdx2TemporalId,
                                  kiCodingIdx2TemporalIdSize, kiCodingIdx2TemporalIdSize); // confirmed_safe_unsafe_usage
                ++iIndexD;
            } while (iIndexD < pOldParam->iSpatialLayerNum);
        }

        /* Any else initialization/reset for rate control here? */

        return 0;
    }

    public static int32_t WelsEncoderApplyLTR(SLogContext* pLogCtx, sWelsEncCtx** ppCtx, SLTRConfig* pLTRValue)
    {
        SWelsSvcCodingParam sConfig;
        int32_t iNumRefFrame = 1;
        int32_t iRet = 0;
        sConfig = *(*ppCtx)->pSvcParam; // memcpy
        sConfig.bEnableLongTermReference = pLTRValue->bEnableLongTermReference;
        sConfig.iLTRRefNum = pLTRValue->iLTRRefNum;
        int32_t uiGopSize = 1 << (sConfig.iTemporalLayerNum - 1);
        if (sConfig.iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            if (sConfig.bEnableLongTermReference)
            {
                sConfig.iLTRRefNum = LONG_TERM_REF_NUM_SCREEN;//WELS_CLIP3 (sConfig.iLTRRefNum, 1, LONG_TERM_REF_NUM_SCREEN);
                iNumRefFrame = Math.Max(1, WELS_LOG2((uint32_t)uiGopSize)) + sConfig.iLTRRefNum;
            }
            else
            {
                sConfig.iLTRRefNum = 0;
                iNumRefFrame = Math.Max(1, uiGopSize >> 1);
            }
        }
        else
        {
            if (sConfig.bEnableLongTermReference)
            {
                sConfig.iLTRRefNum = LONG_TERM_REF_NUM;//WELS_CLIP3 (sConfig.iLTRRefNum, 1, LONG_TERM_REF_NUM);
            }
            else
            {
                sConfig.iLTRRefNum = 0;
            }
            iNumRefFrame = ((uiGopSize >> 1) > 1) ? ((uiGopSize >> 1) + sConfig.iLTRRefNum) : (MIN_REF_PIC_COUNT +
                           sConfig.iLTRRefNum);
            iNumRefFrame = WELS_CLIP3(iNumRefFrame, MIN_REF_PIC_COUNT, MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA);

        }
        if (iNumRefFrame > sConfig.iMaxNumRefFrame)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $" CWelsH264SVCEncoder::SetOption LTR flag = {(sConfig.bEnableLongTermReference ? 1 : 0)} and number = {sConfig.iLTRRefNum}: Required number of reference increased to {iNumRefFrame} and iMaxNumRefFrame is adjusted (from {sConfig.iMaxNumRefFrame})");
            sConfig.iMaxNumRefFrame = iNumRefFrame;
        }

        if (sConfig.iNumRefFrame < iNumRefFrame)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $" CWelsH264SVCEncoder::SetOption LTR flag = {(sConfig.bEnableLongTermReference ? 1 : 0)} and number = {sConfig.iLTRRefNum}, Required number of reference increased from Old = {sConfig.iNumRefFrame} to New = {iNumRefFrame} because of LTR setting");
            sConfig.iNumRefFrame = iNumRefFrame;
        }
        WelsLog(pLogCtx, WELS_LOG_INFO,
                $"CWelsH264SVCEncoder::SetOption enable LTR = {(sConfig.bEnableLongTermReference ? 1 : 0)},ltrnum = {sConfig.iLTRRefNum}");
        iRet = WelsEncoderParamAdjust(ppCtx, &sConfig);
        return iRet;
    }

    // DynSliceRealloc and WelsCodeOnePicPartition: SM_SIZELIMITED_SLICE (Phase 4).
}
