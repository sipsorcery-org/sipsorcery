//-----------------------------------------------------------------------------
// Filename: au_set.cs
//
// Description: Port of codec/encoder/core/inc/au_set.h and
// codec/encoder/core/src/au_set.cpp: SPS, subset SPS and PPS initialisation
// from the coding parameters, level selection, reference count checks and
// the parameter set syntax writers.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// One deviation: WelsCheckRefFrameLimitationLevelIdcFirst indexes
// g_ksLevelLimits by uiLevelIdc - 1, which runs past the 17-entry table for
// any level above 1.3 (upstream reads whatever follows it). The port clamps
// the index to the last entry. That function only runs for the non-real-time
// usage types, which ParamValidationExt rejects first.
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
    private static int32_t WelsCheckLevelLimitation(SWelsSPS* kpSps, SLevelLimits* kpLevelLimit,
        float fFrameRate, int32_t iTargetBitRate)
    {
        uint32_t uiPicWidthInMBs = (uint32_t)kpSps->iMbWidth;
        uint32_t uiPicHeightInMBs = (uint32_t)kpSps->iMbHeight;
        uint32_t uiPicInMBs = uiPicWidthInMBs * uiPicHeightInMBs;
        uint32_t uiNumRefFrames = (uint32_t)kpSps->iNumRefFrames;

        if (kpLevelLimit->uiMaxMBPS < (uint32_t)(uiPicInMBs * fFrameRate))
            return 0;
        if (kpLevelLimit->uiMaxFS < uiPicInMBs)
            return 0;
        if ((kpLevelLimit->uiMaxFS << 3) < (uiPicWidthInMBs * uiPicWidthInMBs))
            return 0;
        if ((kpLevelLimit->uiMaxFS << 3) < (uiPicHeightInMBs * uiPicHeightInMBs))
            return 0;
        if (kpLevelLimit->uiMaxDPBMbs < uiNumRefFrames * uiPicInMBs)
            return 0;
        if ((iTargetBitRate != UNSPECIFIED_BIT_RATE)
            && ((int32_t)kpLevelLimit->uiMaxBR * 1200) < iTargetBitRate)    //RC enabled, considering bitrate constraint
            return 0;
        //add more checks here if needed in future

        return 1;
    }

    public static int32_t WelsAdjustLevel(SSpatialLayerConfig* pSpatialLayer, SLevelLimits* pCurLevel)
    {
        int32_t iMaxBitrate = pSpatialLayer->iMaxSpatialBitrate;
        do
        {
            if (iMaxBitrate <= (int32_t)(pCurLevel->uiMaxBR * CpbBrNalFactor))
            {
                pSpatialLayer->uiLevelIdc = pCurLevel->uiLevelIdc;
                return 0;
            }
            pCurLevel++;
        } while (pCurLevel->uiLevelIdc != LEVEL_5_2);
        return 1;
    }

    private static int32_t WelsCheckNumRefSetting(SLogContext* pLogCtx, SWelsSvcCodingParam* pParam, bool bStrictCheck)
    {
        // validate LTR num
        int32_t iCurrentSupportedLtrNum = (pParam->iUsageType == CAMERA_VIDEO_REAL_TIME) ? LONG_TERM_REF_NUM :
                                          LONG_TERM_REF_NUM_SCREEN;
        if ((pParam->bEnableLongTermReference) && (iCurrentSupportedLtrNum != pParam->iLTRRefNum))
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING, $"iLTRRefNum({pParam->iLTRRefNum}) does not equal to currently supported {iCurrentSupportedLtrNum}, will be reset");
            pParam->iLTRRefNum = iCurrentSupportedLtrNum;
        }
        else if (!pParam->bEnableLongTermReference)
        {
            pParam->iLTRRefNum = 0;
        }

        //TODO: here is a fix needed here, the most reasonable value should be:
        //        iCurrentStrNum = WELS_MAX (1, WELS_LOG2 (pParam->uiGopSize));
        //      but reference list updating need to be changed
        int32_t iCurrentStrNum = ((pParam->iUsageType == SCREEN_CONTENT_REAL_TIME && pParam->bEnableLongTermReference)
                                  ? (WELS_MAX(1, WELS_LOG2(pParam->uiGopSize)))
                                  : (int32_t)(WELS_MAX(1u, (pParam->uiGopSize >> 1))));
        int32_t iNeededRefNum = (pParam->uiIntraPeriod != 1) ? (iCurrentStrNum + pParam->iLTRRefNum) : 0;

        iNeededRefNum = WELS_CLIP3(iNeededRefNum,
                                   MIN_REF_PIC_COUNT,
                                   (pParam->iUsageType == CAMERA_VIDEO_REAL_TIME) ? MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA :
                                   MAX_REFERENCE_PICTURE_COUNT_NUM_SCREEN);
        // to adjust default or invalid input, in case pParam->iNumRefFrame do not have a valid value for the next step
        if (pParam->iNumRefFrame == AUTO_REF_PIC_COUNT)
        {
            pParam->iNumRefFrame = iNeededRefNum;
        }
        else if (pParam->iNumRefFrame < iNeededRefNum)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING,
                    $"iNumRefFrame({pParam->iNumRefFrame}) setting does not support the temporal and LTR setting, will be reset to {iNeededRefNum}");
            if (bStrictCheck)
            {
                return ENC_RETURN_UNSUPPORTED_PARA;
            }
            pParam->iNumRefFrame = iNeededRefNum;
        }

        // after adjustment, do the following:
        // if the setting is larger than needed, we will use the needed, and write the max into sps and for memory to wait for further expanding
        if (pParam->iMaxNumRefFrame < pParam->iNumRefFrame)
        {
            pParam->iMaxNumRefFrame = pParam->iNumRefFrame;
        }
        pParam->iNumRefFrame = iNeededRefNum;

        return ENC_RETURN_SUCCESS;
    }

    public static int32_t WelsCheckRefFrameLimitationNumRefFirst(SLogContext* pLogCtx, SWelsSvcCodingParam* pParam)
    {
        if (WelsCheckNumRefSetting(pLogCtx, pParam, false) != 0)
        {
            // we take num-ref as the honored setting but it conflicts with temporal and LTR
            return ENC_RETURN_UNSUPPORTED_PARA;
        }
        return ENC_RETURN_SUCCESS;
    }

    public static int32_t WelsCheckRefFrameLimitationLevelIdcFirst(SLogContext* pLogCtx, SWelsSvcCodingParam* pParam)
    {
        if ((pParam->iNumRefFrame == AUTO_REF_PIC_COUNT) || (pParam->iMaxNumRefFrame == AUTO_REF_PIC_COUNT))
        {
            //no need to do the checking
            return ENC_RETURN_SUCCESS;
        }

        WelsCheckNumRefSetting(pLogCtx, pParam, false);

        int32_t i = 0;
        int32_t iRefFrame;
        //get the number of reference frame according to level limitation.
        for (i = 0; i < pParam->iSpatialLayerNum; ++i)
        {
            SSpatialLayerConfig* pSpatialLayer = (SSpatialLayerConfig*)&pParam->sSpatialLayers + i;
            if (pSpatialLayer->uiLevelIdc == LEVEL_UNKNOWN)
            {
                continue;
            }

            uint32_t uiPicInMBs = (uint32_t)(((pSpatialLayer->iVideoHeight + 15) >> 4) * ((pSpatialLayer->iVideoWidth + 15) >> 4));
            // Deviation: upstream reads g_ksLevelLimits[uiLevelIdc - 1] unclamped (see the file header).
            int32_t iLevelIdx = Math.Min((int32_t)pSpatialLayer->uiLevelIdc - 1, LEVEL_NUMBER - 1);
            iRefFrame = (int32_t)(g_ksLevelLimits[iLevelIdx].uiMaxDPBMbs / uiPicInMBs);

            //check iMaxNumRefFrame
            if (iRefFrame < pParam->iMaxNumRefFrame)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, $"iMaxNumRefFrame({pParam->iMaxNumRefFrame}) adjusted to {iRefFrame} because of limitation from uiLevelIdc={(int)pSpatialLayer->uiLevelIdc}");
                pParam->iMaxNumRefFrame = iRefFrame;

                //check iNumRefFrame
                if (iRefFrame < pParam->iNumRefFrame)
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING, $"iNumRefFrame({pParam->iNumRefFrame}) adjusted to {iRefFrame} because of limitation from uiLevelIdc={(int)pSpatialLayer->uiLevelIdc}");
                    pParam->iNumRefFrame = iRefFrame;
                }
            }
            else
            {
                //because it is level first now, so adjust max-ref
                WelsLog(pLogCtx, WELS_LOG_INFO,
                        $"iMaxNumRefFrame({pParam->iMaxNumRefFrame}) adjusted to {iRefFrame} because of uiLevelIdc={(int)pSpatialLayer->uiLevelIdc} -- under level-idc first strategy ");
                pParam->iMaxNumRefFrame = iRefFrame;
            }
        }

        return ENC_RETURN_SUCCESS;
    }

    private static ELevelIdc WelsGetLevelIdc(SWelsSPS* kpSps, float fFrameRate, int32_t iTargetBitRate)
    {
        int32_t iOrder;
        for (iOrder = 0; iOrder < LEVEL_NUMBER; iOrder++)
        {
            if (WelsCheckLevelLimitation(kpSps, &(g_ksLevelLimits[iOrder]), fFrameRate, iTargetBitRate) != 0)
            {
                return (g_ksLevelLimits[iOrder].uiLevelIdc);
            }
        }
        return LEVEL_5_1; //final decision: select the biggest level
    }

    public static int32_t WelsWriteVUI(SWelsSPS* pSps, SBitStringAux* pBitStringAux)
    {
        SBitStringAux* pLocalBitStringAux = pBitStringAux;
        Debug.Assert(pSps != null && pBitStringAux != null);

        BsWriteOneBit(pLocalBitStringAux, pSps->bAspectRatioPresent); //aspect_ratio_info_present_flag
        if (pSps->bAspectRatioPresent)
        {
            BsWriteBits(pLocalBitStringAux, 8, (uint32_t)pSps->eAspectRatio); // aspect_ratio_idc
            if (pSps->eAspectRatio == ASP_EXT_SAR)
            {
                BsWriteBits(pLocalBitStringAux, 16, pSps->sAspectRatioExtWidth); // sar_width
                BsWriteBits(pLocalBitStringAux, 16, pSps->sAspectRatioExtHeight); // sar_height
            }
        }
        BsWriteOneBit(pLocalBitStringAux, false); //overscan_info_present_flag

        // See codec_app_def.h and parameter_sets.h for more info about members bVideoSignalTypePresent through uiColorMatrix.
        BsWriteOneBit(pLocalBitStringAux, pSps->bVideoSignalTypePresent); //video_signal_type_present_flag
        if (pSps->bVideoSignalTypePresent)
        {
            //write video signal type info to header

            BsWriteBits(pLocalBitStringAux, 3, pSps->uiVideoFormat);
            BsWriteOneBit(pLocalBitStringAux, pSps->bFullRange);
            BsWriteOneBit(pLocalBitStringAux, pSps->bColorDescriptionPresent);

            if (pSps->bColorDescriptionPresent)
            {
                //write color description info to header

                BsWriteBits(pLocalBitStringAux, 8, pSps->uiColorPrimaries);
                BsWriteBits(pLocalBitStringAux, 8, pSps->uiTransferCharacteristics);
                BsWriteBits(pLocalBitStringAux, 8, pSps->uiColorMatrix);

            }//write color description info to header

        }//write video signal type info to header

        BsWriteOneBit(pLocalBitStringAux, false); //chroma_loc_info_present_flag
        BsWriteOneBit(pLocalBitStringAux, false); //timing_info_present_flag
        BsWriteOneBit(pLocalBitStringAux, false); //nal_hrd_parameters_present_flag
        BsWriteOneBit(pLocalBitStringAux, false); //vcl_hrd_parameters_present_flag
        BsWriteOneBit(pLocalBitStringAux, false); //pic_struct_present_flag
        BsWriteOneBit(pLocalBitStringAux, true); //bitstream_restriction_flag

        //
        BsWriteOneBit(pLocalBitStringAux, true); //motion_vectors_over_pic_boundaries_flag
        BsWriteUE(pLocalBitStringAux, 0); //max_bytes_per_pic_denom
        BsWriteUE(pLocalBitStringAux, 0); //max_bits_per_mb_denom
        BsWriteUE(pLocalBitStringAux, 16); //log2_max_mv_length_horizontal
        BsWriteUE(pLocalBitStringAux, 16); //log2_max_mv_length_vertical

        BsWriteUE(pLocalBitStringAux, 0); //max_num_reorder_frames
        BsWriteUE(pLocalBitStringAux, (uint32_t)pSps->iNumRefFrames); //max_dec_frame_buffering

        return 0;
    }

    /*!
     *************************************************************************************
     * \brief   to set Sequence Parameter Set (SPS)
     *
     * \param   pSps            SWelsSPS to be wrote, update iSpsId dependency
     * \param   pBitStringAux   bitstream writer auxiliary
     *
     * \return  0 - successed
     *          1 - failed
     *
     * \note    Call it in case EWelsNalUnitType is SPS.
     *************************************************************************************
     */
    public static int32_t WelsWriteSpsSyntax(SWelsSPS* pSps, SBitStringAux* pBitStringAux, int32_t* pSpsIdDelta, bool bBaseLayer)
    {
        SBitStringAux* pLocalBitStringAux = pBitStringAux;

        Debug.Assert(pSps != null && pBitStringAux != null);

        BsWriteBits(pLocalBitStringAux, 8, pSps->uiProfileIdc);

        BsWriteOneBit(pLocalBitStringAux, pSps->bConstraintSet0Flag);        // bConstraintSet0Flag
        BsWriteOneBit(pLocalBitStringAux, pSps->bConstraintSet1Flag);        // bConstraintSet1Flag
        BsWriteOneBit(pLocalBitStringAux, pSps->bConstraintSet2Flag);        // bConstraintSet2Flag
        BsWriteOneBit(pLocalBitStringAux, pSps->bConstraintSet3Flag);        // bConstraintSet3Flag
        if ((uint8_t)PRO_HIGH == pSps->uiProfileIdc || (uint8_t)PRO_EXTENDED == pSps->uiProfileIdc ||
            (uint8_t)PRO_MAIN == pSps->uiProfileIdc)
        {
            BsWriteOneBit(pLocalBitStringAux, 1);        // bConstraintSet4Flag: If profile_idc is equal to 77, 88, or 100, constraint_set4_flag equal to 1 indicates that the value of frame_mbs_only_flag is equal to 1. constraint_set4_flag equal to 0 indicates that the value of frame_mbs_only_flag may or may not be equal to 1.
            BsWriteOneBit(pLocalBitStringAux, 1);        // bConstraintSet5Flag: If profile_idc is equal to 77, 88, or 100, constraint_set5_flag equal to 1 indicates that B slice types are not present in the coded video sequence. constraint_set5_flag equal to 0 indicates that B slice types may or may not be present in the coded video sequence.
            BsWriteBits(pLocalBitStringAux, 2, 0);                               // reserved_zero_2bits, equal to 0
        }
        else
        {
            BsWriteBits(pLocalBitStringAux, 4, 0);                               // reserved_zero_4bits, equal to 0
        }
        BsWriteBits(pLocalBitStringAux, 8, pSps->iLevelIdc);                 // iLevelIdc
        BsWriteUE(pLocalBitStringAux, pSps->uiSpsId + (uint32_t)pSpsIdDelta[pSps->uiSpsId]);        // seq_parameter_set_id

        if ((uint8_t)PRO_SCALABLE_BASELINE == pSps->uiProfileIdc || (uint8_t)PRO_SCALABLE_HIGH == pSps->uiProfileIdc ||
            (uint8_t)PRO_HIGH == pSps->uiProfileIdc || (uint8_t)PRO_HIGH10 == pSps->uiProfileIdc ||
            (uint8_t)PRO_HIGH422 == pSps->uiProfileIdc || (uint8_t)PRO_HIGH444 == pSps->uiProfileIdc ||
            (uint8_t)PRO_CAVLC444 == pSps->uiProfileIdc || 44 == pSps->uiProfileIdc)
        {
            BsWriteUE(pLocalBitStringAux, 1);  //uiChromaFormatIdc, now should be 1
            BsWriteUE(pLocalBitStringAux, 0); //uiBitDepthLuma
            BsWriteUE(pLocalBitStringAux, 0); //uiBitDepthChroma
            BsWriteOneBit(pLocalBitStringAux, 0); //qpprime_y_zero_transform_bypass_flag
            BsWriteOneBit(pLocalBitStringAux, 0); //seq_scaling_matrix_present_flag
        }

        BsWriteUE(pLocalBitStringAux, pSps->uiLog2MaxFrameNum - 4);  // log2_max_frame_num_minus4
        BsWriteUE(pLocalBitStringAux, pSps->uiPocType);              // pic_order_cnt_type
        if (pSps->uiPocType == 0)
        {
            BsWriteUE(pLocalBitStringAux, (uint32_t)(pSps->iLog2MaxPocLsb - 4));   // log2_max_pic_order_cnt_lsb_minus4
        }
        else if (pSps->uiPocType == 1)
        {
            // TODO: implement
            Debug.Assert(false);
        }
        else
        {
            // no-op for uiPocType 2.
        }

        BsWriteUE(pLocalBitStringAux, (uint32_t)pSps->iNumRefFrames);          // max_num_ref_frames
        BsWriteOneBit(pLocalBitStringAux, pSps->bGapsInFrameNumValueAllowedFlag); //gaps_in_frame_numvalue_allowed_flag
        BsWriteUE(pLocalBitStringAux, (uint32_t)(pSps->iMbWidth - 1));           // pic_width_in_mbs_minus1
        BsWriteUE(pLocalBitStringAux, (uint32_t)(pSps->iMbHeight - 1));          // pic_height_in_map_units_minus1
        BsWriteOneBit(pLocalBitStringAux, true/*pSps->bFrameMbsOnlyFlag*/);  // bFrameMbsOnlyFlag

        uint8_t d8x8 = 0;
        if (pSps->iLevelIdc >= 30)
            d8x8 = 1;
        BsWriteOneBit(pLocalBitStringAux, d8x8/*pSps->bDirect8x8InferenceFlag*/);       // direct_8x8_inference_flag

        BsWriteOneBit(pLocalBitStringAux, pSps->bFrameCroppingFlag); // bFrameCroppingFlag
        if (pSps->bFrameCroppingFlag)
        {
            BsWriteUE(pLocalBitStringAux, (uint32_t)pSps->sFrameCrop.iCropLeft);         // frame_crop_left_offset
            BsWriteUE(pLocalBitStringAux, (uint32_t)pSps->sFrameCrop.iCropRight);        // frame_crop_right_offset
            BsWriteUE(pLocalBitStringAux, (uint32_t)pSps->sFrameCrop.iCropTop);          // frame_crop_top_offset
            BsWriteUE(pLocalBitStringAux, (uint32_t)pSps->sFrameCrop.iCropBottom);       // frame_crop_bottom_offset
        }
        if (bBaseLayer)
        {
            BsWriteOneBit(pLocalBitStringAux, true);   // vui_parameters_present_flag
            WelsWriteVUI(pSps, pBitStringAux);
        }
        else
        {
            BsWriteOneBit(pLocalBitStringAux, false);
        }
        return 0;
    }

    /*!
     * \brief   to write Sequence Parameter Set (SPS)
     */
    public static int32_t WelsWriteSpsNal(SWelsSPS* pSps, SBitStringAux* pBitStringAux, int32_t* pSpsIdDelta)
    {
        WelsWriteSpsSyntax(pSps, pBitStringAux, pSpsIdDelta, true);

        BsRbspTrailingBits(pBitStringAux);

        return 0;
    }

    /*!
     *************************************************************************************
     * \brief   to write SubSet Sequence Parameter Set
     *
     * \param   sub_sps         subset pSps parsed
     * \param   pBitStringAux   bitstream writer auxiliary
     *
     * \return  0 - successed
     *          1 - failed
     *
     * \note    Call it in case EWelsNalUnitType is SubSet SPS.
     *************************************************************************************
     */
    public static int32_t WelsWriteSubsetSpsSyntax(SSubsetSps* pSubsetSps, SBitStringAux* pBitStringAux, int32_t* pSpsIdDelta)
    {
        SWelsSPS* pSps = &pSubsetSps->pSps;

        WelsWriteSpsSyntax(pSps, pBitStringAux, pSpsIdDelta, false);

        if (pSps->uiProfileIdc == (uint8_t)PRO_SCALABLE_BASELINE || pSps->uiProfileIdc == (uint8_t)PRO_SCALABLE_HIGH)
        {
            SSpsSvcExt* pSubsetSpsExt = &pSubsetSps->sSpsSvcExt;

            BsWriteOneBit(pBitStringAux, true/*pSubsetSpsExt->bInterLayerDeblockingFilterCtrlPresentFlag*/);
            BsWriteBits(pBitStringAux, 2, pSubsetSpsExt->iExtendedSpatialScalability);
            BsWriteOneBit(pBitStringAux, 0/*pSubsetSpsExt->uiChromaPhaseXPlus1Flag*/);
            BsWriteBits(pBitStringAux, 2, 1/*pSubsetSpsExt->uiChromaPhaseYPlus1*/);
            if (pSubsetSpsExt->iExtendedSpatialScalability == 1)
            {
                BsWriteOneBit(pBitStringAux, 0/*pSubsetSpsExt->uiSeqRefLayerChromaPhaseXPlus1Flag*/);
                BsWriteBits(pBitStringAux, 2, 1/*pSubsetSpsExt->uiSeqRefLayerChromaPhaseYPlus1*/);
                BsWriteSE(pBitStringAux, 0/*pSubsetSpsExt->sSeqScaledRefLayer.left_offset*/);
                BsWriteSE(pBitStringAux, 0/*pSubsetSpsExt->sSeqScaledRefLayer.top_offset*/);
                BsWriteSE(pBitStringAux, 0/*pSubsetSpsExt->sSeqScaledRefLayer.right_offset*/);
                BsWriteSE(pBitStringAux, 0/*pSubsetSpsExt->sSeqScaledRefLayer.bottom_offset*/);
            }
            BsWriteOneBit(pBitStringAux, pSubsetSpsExt->bSeqTcoeffLevelPredFlag);
            if (pSubsetSpsExt->bSeqTcoeffLevelPredFlag)
            {
                BsWriteOneBit(pBitStringAux, pSubsetSpsExt->bAdaptiveTcoeffLevelPredFlag);
            }
            BsWriteOneBit(pBitStringAux, pSubsetSpsExt->bSliceHeaderRestrictionFlag);

            BsWriteOneBit(pBitStringAux, false/*pSubsetSps->bSvcVuiParamPresentFlag*/);
        }
        BsWriteOneBit(pBitStringAux, false/*pSubsetSps->bAdditionalExtension2Flag*/);

        BsRbspTrailingBits(pBitStringAux);

        return 0;
    }

    /*!
     *************************************************************************************
     * \brief   to write Picture Parameter Set (PPS)
     *
     * \param   pPps            pPps
     * \param   pBitStringAux   bitstream writer auxiliary
     *
     * \return  0 - successed
     *          1 - failed
     *
     * \note    Call it in case EWelsNalUnitType is PPS.
     *************************************************************************************
     */
    public static int32_t WelsWritePpsSyntax(SWelsPPS* pPps, SBitStringAux* pBitStringAux,
                                             IWelsParametersetStrategy* pParametersetStrategy)
    {
        SBitStringAux* pLocalBitStringAux = pBitStringAux;

        BsWriteUE(pLocalBitStringAux, pPps->iPpsId + (uint32_t)pParametersetStrategy->GetPpsIdOffset((int32_t)pPps->iPpsId));
        BsWriteUE(pLocalBitStringAux, pPps->iSpsId + (uint32_t)pParametersetStrategy->GetSpsIdOffset((int32_t)pPps->iPpsId, (int32_t)pPps->iSpsId));

        BsWriteOneBit(pLocalBitStringAux, pPps->bEntropyCodingModeFlag);
        BsWriteOneBit(pLocalBitStringAux, false/*pPps->bPicOrderPresentFlag*/);

        // #ifdef DISABLE_FMO_FEATURE ... #else
        BsWriteUE(pLocalBitStringAux, pPps->uiNumSliceGroups - 1);
        if (pPps->uiNumSliceGroups > 1)
        {
            uint32_t i, uiNumBits;

            BsWriteUE(pLocalBitStringAux, pPps->uiSliceGroupMapType);

            switch (pPps->uiSliceGroupMapType)
            {
                case 0:
                    for (i = 0; i < pPps->uiNumSliceGroups; i++)
                    {
                        BsWriteUE(pLocalBitStringAux, pPps->uiRunLength[i] - 1);
                    }
                    break;
                case 2:
                    for (i = 0; i < pPps->uiNumSliceGroups; i++)
                    {
                        BsWriteUE(pLocalBitStringAux, pPps->uiTopLeft[i]);
                        BsWriteUE(pLocalBitStringAux, pPps->uiBottomRight[i]);
                    }
                    break;
                case 3:
                case 4:
                case 5:
                    BsWriteOneBit(pLocalBitStringAux, pPps->bSliceGroupChangeDirectionFlag);
                    BsWriteUE(pLocalBitStringAux, pPps->uiSliceGroupChangeRate - 1);
                    break;
                case 6:
                    BsWriteUE(pLocalBitStringAux, pPps->uiPicSizeInMapUnits - 1);
                    uiNumBits = 0;///////////////////WELS_CEILLOG2(pPps->uiPicSizeInMapUnits);
                    for (i = 0; i < pPps->uiPicSizeInMapUnits; i++)
                    {
                        BsWriteBits(pLocalBitStringAux, (int32_t)uiNumBits, pPps->uiSliceGroupId[i]);
                    }
                    break;
                default:
                    break;
            }
        }
        // #endif//!DISABLE_FMO_FEATURE

        BsWriteUE(pLocalBitStringAux, 0/*pPps->uiNumRefIdxL0Active - 1*/);
        BsWriteUE(pLocalBitStringAux, 0/*pPps->uiNumRefIdxL1Active - 1*/);

        BsWriteOneBit(pLocalBitStringAux, false/*pPps->bWeightedPredFlag*/);
        BsWriteBits(pLocalBitStringAux, 2, 0/*pPps->uiWeightedBiPredIdc*/);

        BsWriteSE(pLocalBitStringAux, pPps->iPicInitQp - 26);
        BsWriteSE(pLocalBitStringAux, pPps->iPicInitQs - 26);

        BsWriteSE(pLocalBitStringAux, pPps->uiChromaQpIndexOffset);
        BsWriteOneBit(pLocalBitStringAux, pPps->bDeblockingFilterControlPresentFlag);
        BsWriteOneBit(pLocalBitStringAux, false/*pPps->bConstainedIntraPredFlag*/);
        BsWriteOneBit(pLocalBitStringAux, false/*pPps->bRedundantPicCntPresentFlag*/);

        BsRbspTrailingBits(pLocalBitStringAux);

        return 0;
    }

    private static bool WelsGetPaddingOffset(int32_t iActualWidth, int32_t iActualHeight, int32_t iWidth,
        int32_t iHeight, ref SCropOffset pOffset)
    {
        if ((iWidth < iActualWidth) || (iHeight < iActualHeight))
            return false;

        // make actual size even
        iActualWidth -= (iActualWidth & 1);
        iActualHeight -= (iActualHeight & 1);

        pOffset.iCropLeft = 0;
        pOffset.iCropRight = (int16_t)((iWidth - iActualWidth) / 2);
        pOffset.iCropTop = 0;
        pOffset.iCropBottom = (int16_t)((iHeight - iActualHeight) / 2);

        return (iWidth > iActualWidth) || (iHeight > iActualHeight);
    }

    public static int32_t WelsInitSps(SWelsSPS* pSps, SSpatialLayerConfig* pLayerParam, SSpatialLayerInternal* pLayerParamInternal,
                                      uint32_t kuiIntraPeriod, int32_t kiNumRefFrame,
                                      uint32_t kuiSpsId, bool kbEnableFrameCropping, bool bEnableRc,
                                      int32_t kiDlayerCount, bool bSVCBaselayer)
    {
        *pSps = default; // memset (pSps, 0, sizeof (SWelsSPS));
        pSps->uiSpsId = kuiSpsId;
        pSps->iMbWidth = (int16_t)((pLayerParam->iVideoWidth + 15) >> 4);
        pSps->iMbHeight = (int16_t)((pLayerParam->iVideoHeight + 15) >> 4);

        //max value of both iFrameNum and POC are 2^16-1, in our encoder, iPOC=2*iFrameNum, so max of iFrameNum should be 2^15-1.--
        pSps->uiLog2MaxFrameNum = 15;//16;
        pSps->uiPocType = 2;
        pSps->iLog2MaxPocLsb = (int32_t)(1 + pSps->uiLog2MaxFrameNum);

        pSps->iNumRefFrames = (int16_t)kiNumRefFrame;        /* min pRef size when fifo pRef operation*/

        if (kbEnableFrameCropping)
        {
            // TODO: get frame_crop_left_offset, frame_crop_right_offset, frame_crop_top_offset, frame_crop_bottom_offset
            pSps->bFrameCroppingFlag = WelsGetPaddingOffset(pLayerParamInternal->iActualWidth, pLayerParamInternal->iActualHeight,
                                       pLayerParam->iVideoWidth, pLayerParam->iVideoHeight, ref pSps->sFrameCrop);
        }
        else
        {
            pSps->bFrameCroppingFlag = false;
        }
        pSps->uiProfileIdc = (uint8_t)(pLayerParam->uiProfileIdc != 0 ? pLayerParam->uiProfileIdc : PRO_BASELINE);
        if (pLayerParam->uiProfileIdc == PRO_BASELINE)
        {
            pSps->bConstraintSet0Flag = true;
        }
        if (pLayerParam->uiProfileIdc <= PRO_MAIN)
        {
            pSps->bConstraintSet1Flag = true;
        }
        if ((kiDlayerCount > 1) && bSVCBaselayer)
        {
            pSps->bConstraintSet2Flag = true;
        }

        ELevelIdc uiLevel = WelsGetLevelIdc(pSps, pLayerParamInternal->fOutputFrameRate, pLayerParam->iSpatialBitrate);
        //update level
        //for Scalable Baseline, Scalable High, and Scalable High Intra profiles.If level_idc is equal to 9, the indicated level is level 1b.
        //for the Baseline, Constrained Baseline, Main, and Extended profiles,If level_idc is equal to 11 and constraint_set3_flag is equal to 1, the indicated level is level 1b.
        if ((uiLevel == LEVEL_1_B) &&
            ((pSps->uiProfileIdc == (uint8_t)PRO_BASELINE) || (pSps->uiProfileIdc == (uint8_t)PRO_MAIN) || (pSps->uiProfileIdc == (uint8_t)PRO_EXTENDED)))
        {
            uiLevel = LEVEL_1_1;
            pSps->bConstraintSet3Flag = true;
        }
        if ((pLayerParam->uiLevelIdc == LEVEL_UNKNOWN) || (pLayerParam->uiLevelIdc < uiLevel))
        {
            pLayerParam->uiLevelIdc = uiLevel;
        }
        pSps->iLevelIdc = (uint8_t)pLayerParam->uiLevelIdc;

        //bGapsInFrameNumValueAllowedFlag is false when only spatial layer number and temporal layer number is 1, and ltr is 0.
        if ((kiDlayerCount == 1) && (pSps->iNumRefFrames == 1))
            pSps->bGapsInFrameNumValueAllowedFlag = false;
        else
            pSps->bGapsInFrameNumValueAllowedFlag = true;

        pSps->bVuiParamPresentFlag = true;

        pSps->bAspectRatioPresent = pLayerParam->bAspectRatioPresent;
        pSps->eAspectRatio = pLayerParam->eAspectRatio;
        pSps->sAspectRatioExtWidth = pLayerParam->sAspectRatioExtWidth;
        pSps->sAspectRatioExtHeight = pLayerParam->sAspectRatioExtHeight;

        // See codec_app_def.h and parameter_sets.h for more info about members bVideoSignalTypePresent through uiColorMatrix.
        pSps->bVideoSignalTypePresent = pLayerParam->bVideoSignalTypePresent;
        pSps->uiVideoFormat = pLayerParam->uiVideoFormat;
        pSps->bFullRange = pLayerParam->bFullRange;
        pSps->bColorDescriptionPresent = pLayerParam->bColorDescriptionPresent;
        pSps->uiColorPrimaries = pLayerParam->uiColorPrimaries;
        pSps->uiTransferCharacteristics = pLayerParam->uiTransferCharacteristics;
        pSps->uiColorMatrix = pLayerParam->uiColorMatrix;

        return 0;
    }

    public static int32_t WelsInitSubsetSps(SSubsetSps* pSubsetSps, SSpatialLayerConfig* pLayerParam,
                                            SSpatialLayerInternal* pLayerParamInternal,
                                            uint32_t kuiIntraPeriod, int32_t kiNumRefFrame,
                                            uint32_t kuiSpsId, bool kbEnableFrameCropping, bool bEnableRc,
                                            int32_t kiDlayerCount)
    {
        SWelsSPS* pSps = &pSubsetSps->pSps;

        *pSubsetSps = default; // memset (pSubsetSps, 0, sizeof (SSubsetSps));

        WelsInitSps(pSps, pLayerParam, pLayerParamInternal, kuiIntraPeriod, kiNumRefFrame, kuiSpsId, kbEnableFrameCropping,
                    bEnableRc, kiDlayerCount, false);

        pSps->uiProfileIdc = (uint8_t)pLayerParam->uiProfileIdc;

        pSubsetSps->sSpsSvcExt.iExtendedSpatialScalability = 0;    /* ESS is 0 in default */
        pSubsetSps->sSpsSvcExt.bAdaptiveTcoeffLevelPredFlag = false;
        pSubsetSps->sSpsSvcExt.bSeqTcoeffLevelPredFlag = false;
        pSubsetSps->sSpsSvcExt.bSliceHeaderRestrictionFlag = true;

        return 0;
    }

    public static int32_t WelsInitPps(SWelsPPS* pPps,
                                      SWelsSPS* pSps,
                                      SSubsetSps* pSubsetSps,
                                      uint32_t kuiPpsId,
                                      bool kbDeblockingFilterPresentFlag,
                                      bool kbUsingSubsetSps,
                                      bool kbEntropyCodingModeFlag)
    {
        SWelsSPS* pUsedSps = null;
        if (pPps == null || (pSps == null && pSubsetSps == null))
            return 1;
        if (!kbUsingSubsetSps)
        {
            Debug.Assert(pSps != null);
            if (null == pSps)
                return 1;
            pUsedSps = pSps;
        }
        else
        {
            Debug.Assert(pSubsetSps != null);
            if (null == pSubsetSps)
                return 1;
            pUsedSps = &pSubsetSps->pSps;
        }

        /* fill picture parameter set syntax */
        pPps->iPpsId = kuiPpsId;
        pPps->iSpsId = pUsedSps->uiSpsId;
        pPps->bEntropyCodingModeFlag = kbEntropyCodingModeFlag;
        // #if !defined(DISABLE_FMO_FEATURE)
        pPps->uiNumSliceGroups = 1; //param->qos_param.sliceGroupCount;
        if (pPps->uiNumSliceGroups > 1)
        {
            pPps->uiSliceGroupMapType = 0; //param->qos_param.sliceGroupType;
            if (pPps->uiSliceGroupMapType == 0)
            {
                uint32_t uiGroup = 0;
                while (uiGroup < pPps->uiNumSliceGroups)
                {
                    pPps->uiRunLength[uiGroup] = 25;
                    ++uiGroup;
                }
            }
            else if (pPps->uiSliceGroupMapType == 2)
            {
                memset(&pPps->uiTopLeft[0], 0, MAX_SLICEGROUP_IDS * sizeof(uint32_t));
                memset(&pPps->uiBottomRight[0], 0, MAX_SLICEGROUP_IDS * sizeof(uint32_t));
            }
            else if (pPps->uiSliceGroupMapType >= 3 &&
                     pPps->uiSliceGroupMapType <= 5)
            {
                pPps->bSliceGroupChangeDirectionFlag = false;
                pPps->uiSliceGroupChangeRate = 0;
            }
            else if (pPps->uiSliceGroupMapType == 6)
            {
                pPps->uiPicSizeInMapUnits = 1;
                memset(&pPps->uiSliceGroupId[0], 0, MAX_SLICEGROUP_IDS * sizeof(uint32_t));
            }
        }
        // #endif//!DISABLE_FMO_FEATURE

        pPps->iPicInitQp = 26;
        pPps->iPicInitQs = 26;

        pPps->uiChromaQpIndexOffset = 0;
        pPps->bDeblockingFilterControlPresentFlag = kbDeblockingFilterPresentFlag;

        return 0;
    }
}
