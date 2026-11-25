//-----------------------------------------------------------------------------
// Filename: parameter_sets.cs
//
// Description: Port of codec/encoder/core/inc/parameter_sets.h: the
// encoder's SPS, subset SPS and PPS structures. These differ from the
// decoder's (decoder/parameter_sets.cs): the encoder keeps only what it
// writes.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// DISABLE_FMO_FEATURE is not defined upstream, so SWelsPPS keeps the slice
// group fields.
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

namespace SIPSorcery.H264.Encoder;

/* Sequence Parameter Set, refer to Page 57 in JVT X201wcm */
/// <summary>TagWelsSPS (PWelsSPS is SWelsSPS*).</summary>
internal struct SWelsSPS
{
    public uint32_t uiSpsId;
    public int16_t iMbWidth;
    public int16_t iMbHeight;
    public uint32_t uiLog2MaxFrameNum;
    public uint32_t uiPocType;
    /* POC type 0 */
    public int32_t iLog2MaxPocLsb;
    /* POC type 1 */
    // int32_t         iOffsetForNonRefPic;

    // int32_t         iOffsetForTopToBottomField;
    // int32_t         iNumRefFramesInPocCycle;
    // int8_t          iOffsetForRefFrame[256];
    public SCropOffset sFrameCrop;
    public int16_t iNumRefFrames;
    // uint32_t        uiNumUnitsInTick;
    // uint32_t        uiTimeScale;

    public uint8_t uiProfileIdc;
    public uint8_t iLevelIdc;
    // uint8_t         uiChromaFormatIdc;
    // uint8_t         uiChromaArrayType;              //support =1

    // uint8_t         uiBitDepthLuma;         //=8, only used in decoder, encoder in general_***; it can be removed when removed general up_sample
    // uint8_t         uiBitDepthChroma;               //=8
    /* TO BE CONTINUE: POC type 1 */
    // bool            bDeltaPicOrderAlwaysZeroFlag;
    public bool bGapsInFrameNumValueAllowedFlag;

    // bool            bFrameMbsOnlyFlag;
    // bool            bMbaffFlag;     // MB Adapative Frame Field
    // bool            bDirect8x8InferenceFlag;
    public bool bFrameCroppingFlag;

    public bool bVuiParamPresentFlag;
    // bool            bTimingInfoPresentFlag;
    // bool            bFixedFrameRateFlag;

    // Note: members bVideoSignalTypePresent through uiColorMatrix below are also defined in SSpatialLayerConfig in codec_app_def.h,
    // along with definitions for enumerators EVideoFormatSPS, EColorPrimaries, ETransferCharacteristics, and EColorMatrix.
    public bool bVideoSignalTypePresent;    // false => do not write any of the following information to the header
    public uint8_t uiVideoFormat;           // EVideoFormatSPS; 3 bits in header; 0-5 => component, kpal, ntsc, secam, mac, undef
    public bool bFullRange;                 // false => analog video data range [16, 235]; true => full data range [0,255]
    public bool bColorDescriptionPresent;   // false => do not write any of the following three items to the header
    public uint8_t uiColorPrimaries;        // EColorPrimaries; 8 bits in header; 0 - 9 => ???, bt709, undef, ???, bt470m, bt470bg,
                                            //    smpte170m, smpte240m, film, bt2020
    public uint8_t uiTransferCharacteristics; // ETransferCharacteristics; 8 bits in header; 0 - 15 => ???, bt709, undef, ???, bt470m, bt470bg, smpte170m,
                                              //   smpte240m, linear, log100, log316, iec61966-2-4, bt1361e, iec61966-2-1, bt2020-10, bt2020-12
    public uint8_t uiColorMatrix;           // EColorMatrix; 8 bits in header (corresponds to FFmpeg "colorspace"); 0 - 10 => GBR, bt709,
                                            //   undef, ???, fcc, bt470bg, smpte170m, smpte240m, YCgCo, bt2020nc, bt2020c

    public bool bConstraintSet0Flag;
    public bool bConstraintSet1Flag;
    public bool bConstraintSet2Flag;
    public bool bConstraintSet3Flag;
    // bool            bSeparateColorPlaneFlag;  // =false,: only used in decoder, encoder in general_***; it can be removed when removed general up_sample

    // aspect ratio in VUI
    public bool bAspectRatioPresent;
    public ESampleAspectRatio eAspectRatio;
    public uint16_t sAspectRatioExtWidth;
    public uint16_t sAspectRatioExtHeight;
}

/* Sequence Parameter Set SVC extension syntax, refer to Page 391 in JVT X201wcm */
/// <summary>TagSpsSvcExt (PSpsSvcExt is SSpsSvcExt*).</summary>
internal struct SSpsSvcExt
{
    // SCropOffset     sSeqScaledRefLayer;

    public uint8_t iExtendedSpatialScalability;    // ESS
    // uint8_t         uiChromaPhaseXPlus1Flag;
    // uint8_t         uiChromaPhaseYPlus1;
    // uint8_t         uiSeqRefLayerChromaPhaseXPlus1Flag;
    // uint8_t         uiSeqRefLayerChromaPhaseYPlus1;
    // bool            bInterLayerDeblockingFilterCtrlPresentFlag;
    public bool bSeqTcoeffLevelPredFlag;
    public bool bAdaptiveTcoeffLevelPredFlag;
    public bool bSliceHeaderRestrictionFlag;
}

/* Subset sequence parameter set syntax, refer to Page 391 in JVT X201wcm */
/// <summary>TagSubsetSps (PSubsetSps is SSubsetSps*).</summary>
internal struct SSubsetSps
{
    public SWelsSPS pSps;
    public SSpsSvcExt sSpsSvcExt;

    // bool            bSvcVuiParamPresentFlag;
    // bool            bAdditionalExtension2Flag;
    // bool            bAdditionalExtension2DataFlag;
}

/* Picture parameter set syntax, refer to Page 59 in JVT X201wcm */
/// <summary>TagWelsPPS (PWelsPPPS is SWelsPPS*).</summary>
internal unsafe struct SWelsPPS
{
    public uint32_t iSpsId;
    public uint32_t iPpsId;

    // #if !defined(DISABLE_FMO_FEATURE)
    public uint32_t uiNumSliceGroups;
    public uint32_t uiSliceGroupMapType;
    /* uiSliceGroupMapType = 0 */
    public fixed uint uiRunLength[WelsEnc.MAX_SLICEGROUP_IDS];
    /* uiSliceGroupMapType = 2 */
    public fixed uint uiTopLeft[WelsEnc.MAX_SLICEGROUP_IDS];
    public fixed uint uiBottomRight[WelsEnc.MAX_SLICEGROUP_IDS];
    /* uiSliceGroupMapType = 3, 4 or 5 */
    /* uiSliceGroupMapType = 3, 4 or 5 */
    public bool bSliceGroupChangeDirectionFlag;
    public uint32_t uiSliceGroupChangeRate;
    /* uiSliceGroupMapType = 6 */
    public uint32_t uiPicSizeInMapUnits;
    public fixed uint uiSliceGroupId[WelsEnc.MAX_SLICEGROUP_IDS];
    // #endif//!DISABLE_FMO_FEATURE

    // uint32_t        uiNumRefIdxL0Active;
    // uint32_t        uiNumRefIdxL1Active;

    public int8_t iPicInitQp;
    public int8_t iPicInitQs;
    public uint8_t uiChromaQpIndexOffset;

    /* potential application for High profile */
    // int32_t         iSecondChromaQpIndexOffset;
    // /* potential application for High profile */

    // bool            bPicOrderPresentFlag;
    public bool bEntropyCodingModeFlag;
    public bool bDeblockingFilterControlPresentFlag;

    // bool            bConstainedIntraPredFlag;
    // bool            bRedundantPicCntPresentFlag;
    // bool            bWeightedPredFlag;
    // uint8_t         uiWeightedBiPredIdc;
}
