//-----------------------------------------------------------------------------
// Filename: parameter_sets.cs
//
// Description: Port of codec/decoder/core/inc/parameter_sets.h: the decoder's
// SPS, subset SPS, PPS and VUI structures.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The two-dimensional scaling lists (uint8_t [6][16] and [6][64]) are nested
// inline arrays, so &pSps->iScalingList4x4[i] is still a row pointer.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
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

namespace SIPSorcery.H264.Decoder;

/* VUI syntax in Sequence Parameter Set, refer to E.1 in Rec */
/// <summary>TagVui (PVui is SVui*).</summary>
internal struct SVui
{
    public bool bAspectRatioInfoPresentFlag;
    public uint32_t uiAspectRatioIdc;
    public uint32_t uiSarWidth;
    public uint32_t uiSarHeight;
    public bool bOverscanInfoPresentFlag;
    public bool bOverscanAppropriateFlag;
    public bool bVideoSignalTypePresentFlag;
    public uint8_t uiVideoFormat;
    public bool bVideoFullRangeFlag;
    public bool bColourDescripPresentFlag;
    public uint8_t uiColourPrimaries;
    public uint8_t uiTransferCharacteristics;
    public uint8_t uiMatrixCoeffs;
    public bool bChromaLocInfoPresentFlag;
    public uint32_t uiChromaSampleLocTypeTopField;
    public uint32_t uiChromaSampleLocTypeBottomField;
    public bool bTimingInfoPresentFlag;
    public uint32_t uiNumUnitsInTick;
    public uint32_t uiTimeScale;
    public bool bFixedFrameRateFlag;
    public bool bNalHrdParamPresentFlag;
    public bool bVclHrdParamPresentFlag;
    public bool bPicStructPresentFlag;
    public bool bBitstreamRestrictionFlag;
    public bool bMotionVectorsOverPicBoundariesFlag;
    public uint32_t uiMaxBytesPerPicDenom;
    public uint32_t uiMaxBitsPerMbDenom;
    public uint32_t uiLog2MaxMvLengthHorizontal;
    public uint32_t uiLog2MaxMvLengthVertical;
    public uint32_t uiMaxNumReorderFrames;
    public uint32_t uiMaxDecFrameBuffering;
}

/* Sequence Parameter Set, refer to Page 57 in JVT X201wcm */
/// <summary>TagSps (PSps is SSps*).</summary>
internal unsafe struct SSps
{
    public int32_t iSpsId;
    public uint32_t iMbWidth;
    public uint32_t iMbHeight;
    public uint32_t uiTotalMbCount; //used in decode_slice_data()

    public uint32_t uiLog2MaxFrameNum;
    public uint32_t uiPocType;
    /* POC type 0 */
    public int32_t iLog2MaxPocLsb;
    /* POC type 1 */
    public int32_t iOffsetForNonRefPic;

    public int32_t iOffsetForTopToBottomField;
    public int32_t iNumRefFramesInPocCycle;
    public fixed sbyte iOffsetForRefFrame[256];
    public int32_t iNumRefFrames;

    public SPosOffset sFrameCrop;

    public ProfileIdc uiProfileIdc;
    public uint8_t uiLevelIdc;
    public uint8_t uiChromaFormatIdc;
    public uint8_t uiChromaArrayType;

    public uint8_t uiBitDepthLuma;
    public uint8_t uiBitDepthChroma;
    /* TO BE CONTINUE: POC type 1 */
    public bool bDeltaPicOrderAlwaysZeroFlag;
    public bool bGapsInFrameNumValueAllowedFlag;

    public bool bFrameMbsOnlyFlag;
    public bool bMbaffFlag;     // MB Adapative Frame Field
    public bool bDirect8x8InferenceFlag;
    public bool bFrameCroppingFlag;

    public bool bVuiParamPresentFlag;
    //  bool          bTimingInfoPresentFlag;
    //  bool          bFixedFrameRateFlag;
    public bool bConstraintSet0Flag;
    public bool bConstraintSet1Flag;
    public bool bConstraintSet2Flag;
    public bool bConstraintSet3Flag;
    public bool bSeparateColorPlaneFlag;
    public bool bQpPrimeYZeroTransfBypassFlag;
    public bool bSeqScalingMatrixPresentFlag;
    public fixed bool bSeqScalingListPresentFlag[12];
    //Add scaling list supporting
    public InlineArray6<InlineArray16<uint8_t>> iScalingList4x4;
    public InlineArray6<InlineArray64<uint8_t>> iScalingList8x8;
    public SVui sVui;
    public SLevelLimits* pSLevelLimits;
}

/* Sequence Parameter Set extension syntax, refer to Page 391 in JVT X201wcm */
/// <summary>TagSpsSvcExt (PSpsSvcExt is SSpsSvcExt*).</summary>
internal struct SSpsSvcExt
{
    public SPosOffset sSeqScaledRefLayer;

    public uint8_t uiExtendedSpatialScalability;   // ESS
    public uint8_t uiChromaPhaseXPlus1Flag;
    public uint8_t uiChromaPhaseYPlus1;
    public uint8_t uiSeqRefLayerChromaPhaseXPlus1Flag;
    public uint8_t uiSeqRefLayerChromaPhaseYPlus1;
    public bool bInterLayerDeblockingFilterCtrlPresentFlag;
    public bool bSeqTCoeffLevelPredFlag;
    public bool bAdaptiveTCoeffLevelPredFlag;
    public bool bSliceHeaderRestrictionFlag;
}

/* Subset sequence parameter set syntax, refer to Page 391 in JVT X201wcm */
/// <summary>TagSubsetSps (PSubsetSps is SSubsetSps*).</summary>
internal struct SSubsetSps
{
    public SSps sSps;
    public SSpsSvcExt sSpsSvcExt;
    public bool bSvcVuiParamPresentFlag;
    public bool bAdditionalExtension2Flag;
    public bool bAdditionalExtension2DataFlag;
}

/* Picture parameter set syntax, refer to Page 59 in JVT X201wcm */
/// <summary>TagPps (PPps is SPps*).</summary>
internal unsafe struct SPps
{
    public int32_t iSpsId;
    public int32_t iPpsId;

    public uint32_t uiNumSliceGroups;
    public uint32_t uiSliceGroupMapType;
    /* slice_group_map_type = 0 */
    public fixed uint uiRunLength[WelsDec.MAX_SLICEGROUP_IDS];
    /* slice_group_map_type = 2 */
    public fixed uint uiTopLeft[WelsDec.MAX_SLICEGROUP_IDS];
    public fixed uint uiBottomRight[WelsDec.MAX_SLICEGROUP_IDS];
    /* slice_group_map_type = 3, 4 or 5 */
    public uint32_t uiSliceGroupChangeRate;
    /* slice_group_map_type = 6 */
    public uint32_t uiPicSizeInMapUnits;
    public fixed uint uiSliceGroupId[WelsDec.MAX_SLICEGROUP_IDS];

    public uint32_t uiNumRefIdxL0Active;
    public uint32_t uiNumRefIdxL1Active;

    public int32_t iPicInitQp;
    public int32_t iPicInitQs;
    public fixed int iChromaQpIndexOffset[2];//cb,cr

    public bool bEntropyCodingModeFlag;
    public bool bPicOrderPresentFlag;
    /* slice_group_map_type = 3, 4 or 5 */
    public bool bSliceGroupChangeDirectionFlag;
    public bool bDeblockingFilterControlPresentFlag;

    public bool bConstainedIntraPredFlag;
    public bool bRedundantPicCntPresentFlag;
    public bool bWeightedPredFlag;
    public uint8_t uiWeightedBipredIdc;

    public bool bTransform8x8ModeFlag;
    //Add for scalinglist support
    public bool bPicScalingMatrixPresentFlag;
    public fixed bool bPicScalingListPresentFlag[12];
    public InlineArray6<InlineArray16<uint8_t>> iScalingList4x4;
    public InlineArray6<InlineArray64<uint8_t>> iScalingList8x8;

    public int32_t iSecondChromaQPIndexOffset; //second_chroma_qp_index_offset
}
