//-----------------------------------------------------------------------------
// Filename: slice.cs
//
// Description: Port of codec/decoder/core/inc/slice.h: slice header syntax
// (reordering, prediction weights, reference marking) and the slice structure.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// C's anonymous element structs (sReorderingSyn[][], sPredList[], sMmcoRef[],
// mmco_base[]) are named after the field they make up, with an "Entry" suffix.
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

/// <summary>Element of SRefPicListReorderSyn.sReorderingSyn.</summary>
internal struct SReorderingSynEntry
{
    public uint32_t uiAbsDiffPicNumMinus1;
    public uint16_t uiLongTermPicNum;
    public uint16_t uiReorderingOfPicNumsIdc;
}

/*
 *  Reference picture list reordering syntax, refer to page 64 in JVT X201wcm
 */
/// <summary>TagRefPicListReorderSyntax (PRefPicListReorderSyn is SRefPicListReorderSyn*).</summary>
internal unsafe struct SRefPicListReorderSyn
{
    public InlineArray2<InlineArray17<SReorderingSynEntry>> sReorderingSyn; // [LIST_A][MAX_REF_PIC_COUNT + 1]
    public fixed bool bRefPicListReorderingFlag[2]; // [LIST_A]
}

/// <summary>Element of SPredWeightTabSyn.sPredList.</summary>
internal unsafe struct SPredListEntry
{
    public fixed int iLumaWeight[WelsDec.MAX_REF_PIC_COUNT];
    public fixed int iLumaOffset[WelsDec.MAX_REF_PIC_COUNT];
    public InlineArray16<InlineArray2<int32_t>> iChromaWeight; // [MAX_REF_PIC_COUNT][2]
    public InlineArray16<InlineArray2<int32_t>> iChromaOffset; // [MAX_REF_PIC_COUNT][2]
    public bool bLumaWeightFlag;
    public bool bChromaWeightFlag;
}

/*
 *  Prediction weight table syntax, refer to page 65 in JVT X201wcm
 */
/// <summary>TagPredWeightTabSyntax (PPredWeightTabSyn is SPredWeightTabSyn*).</summary>
internal struct SPredWeightTabSyn
{
    public uint32_t uiLumaLog2WeightDenom;
    public uint32_t uiChromaLog2WeightDenom;
    public InlineArray2<SPredListEntry> sPredList; // [LIST_A]
    public InlineArray16<InlineArray16<int32_t>> iImplicitWeight; // [MAX_REF_PIC_COUNT][MAX_REF_PIC_COUNT]
}

/// <summary>Element of SRefPicMarking.sMmcoRef.</summary>
internal struct SMmcoRefEntry
{
    public uint32_t uiMmcoType;
    public int32_t iShortFrameNum;
    public int32_t iDiffOfPicNum;
    public uint32_t uiLongTermPicNum;
    public int32_t iLongTermFrameIdx;
    public int32_t iMaxLongTermFrameIdx;
}

/* Decoded reference picture marking syntax, refer to Page 66 in JVT X201wcm */
/// <summary>TagRefPicMarking (PRefPicMarking is SRefPicMarking*).</summary>
internal struct SRefPicMarking
{
    public InlineArray66<SMmcoRefEntry> sMmcoRef; // [MAX_MMCO_COUNT]

    public bool bNoOutputOfPriorPicsFlag;
    public bool bLongTermRefFlag;
    public bool bAdaptiveRefPicMarkingModeFlag;
}

/// <summary>Element of SRefBasePicMarking.mmco_base.</summary>
internal struct SMmcoBaseEntry
{
    public uint32_t uiMmcoType;
    public int32_t iShortFrameNum;
    public uint32_t uiDiffOfPicNums;
    public uint32_t uiLongTermPicNum; //should uint32_t, cover larger range of iFrameNum.
}

/* Decode reference base picture marking syntax in Page 396 of JVT X201wcm */
/// <summary>TagRefBasePicMarkingSyn (PRefBasePicMarking is SRefBasePicMarking*).</summary>
internal struct SRefBasePicMarking
{
    public InlineArray66<SMmcoBaseEntry> mmco_base; // MAX_REF_PIC for reference picture based on frame

    public bool bAdaptiveRefBasePicMarkingModeFlag;
}

/* Header of slice syntax elements, refer to Page 63 in JVT X201wcm */
/// <summary>TagSliceHeaders (PSliceHeader is SSliceHeader*).</summary>
internal unsafe struct SSliceHeader
{
    /*****************************slice header syntax and generated****************************/
    public int32_t iFirstMbInSlice;
    public int32_t iFrameNum;
    public int32_t iPicOrderCntLsb;
    public int32_t iDeltaPicOrderCntBottom;
    public fixed int iDeltaPicOrderCnt[2];
    public int32_t iRedundantPicCnt;
    public int32_t iDirectSpatialMvPredFlag; //!< Direct Mode type to be used (0: Temporal, 1: Spatial)
    public fixed int uiRefCount[2]; // [LIST_A]
    public int32_t iSliceQpDelta;  //no use for iSliceQp is used directly
    public int32_t iSliceQp;
    public int32_t iSliceQsDelta;  // For SP/SI slices
    public uint32_t uiDisableDeblockingFilterIdc;
    public int32_t iSliceAlphaC0Offset;
    public int32_t iSliceBetaOffset;
    public int32_t iSliceGroupChangeCycle;

    public SSps* pSps;
    public SPps* pPps;
    public int32_t iSpsId;
    public int32_t iPpsId;
    public bool bIdrFlag;

    /*********************got from other layer for efficency if possible*********************/
    public SRefPicListReorderSyn pRefPicListReordering;  // Reference picture list reordering syntaxs
    public SPredWeightTabSyn sPredWeightTable;
    public int32_t iCabacInitIdc;
    public int32_t iMbWidth;       //from?
    public int32_t iMbHeight; //from?
    public SRefPicMarking sRefMarking;    // Decoded reference picture marking syntaxs

    public uint16_t uiIdrPicId;
    public EWelsSliceType eSliceType;
    public bool bNumRefIdxActiveOverrideFlag;
    public bool bFieldPicFlag;          //not supported in base profile
    public bool bBottomFiledFlag;               //not supported in base profile
    public uint8_t uiPadding1Byte;
    public bool bSpForSwitchFlag;                       // For SP/SI slices
    public int16_t iPadding2Bytes;
}

/* Slice header in scalable extension syntax, refer to Page 394 in JVT X201wcm */
/// <summary>TagSliceHeaderExt (PSliceHeaderExt is SSliceHeaderExt*).</summary>
internal unsafe struct SSliceHeaderExt
{
    public SSliceHeader sSliceHeader;
    public SSubsetSps* pSubsetSps;

    public uint32_t uiDisableInterLayerDeblockingFilterIdc;
    public int32_t iInterLayerSliceAlphaC0Offset;
    public int32_t iInterLayerSliceBetaOffset;

    //SPosOffset sScaledRefLayer;
    public int32_t iScaledRefLayerPicWidthInSampleLuma;
    public int32_t iScaledRefLayerPicHeightInSampleLuma;

    public SRefBasePicMarking sRefBasePicMarking;
    public bool bBasePredWeightTableFlag;
    public bool bStoreRefBasePicFlag;
    public bool bConstrainedIntraResamplingFlag;
    public bool bSliceSkipFlag;

    public bool bAdaptiveBaseModeFlag;
    public bool bDefaultBaseModeFlag;
    public bool bAdaptiveMotionPredFlag;
    public bool bDefaultMotionPredFlag;
    public bool bAdaptiveResidualPredFlag;
    public bool bDefaultResidualPredFlag;
    public bool bTCoeffLevelPredFlag;
    public uint8_t uiRefLayerChromaPhaseXPlus1Flag;

    public uint8_t uiRefLayerChromaPhaseYPlus1;
    public uint8_t uiRefLayerDqId;
    public uint8_t uiScanIdxStart;
    public uint8_t uiScanIdxEnd;
}

/// <summary>TagSlice (PSlice is SSlice*).</summary>
internal struct SSlice
{
    /*******************************slice_header****************************/
    public SSliceHeaderExt sSliceHeaderExt;

    /*******************************use for future****************************/
    // for Macroblock coding within slice
    public int32_t iLastMbQp;              // stored qp for last mb coded, maybe more efficient for mb skip detection etc.

    /*******************************slice_data****************************/
    /*slice_data_ext()*/
    public int32_t iMbSkipRun;
    public int32_t iTotalMbInCurSlice; //record the total number of MB in current slice.

    /*slice_data_ext() generate*/

    /*******************************misc use****************************/
    public bool bSliceHeaderExtFlag; // Indicate which slice header is used, avc or ext?
    /*************got from other layer for effiency if possible***************/
    /*from lower layer: slice header*/
    public uint8_t eSliceType;
    public InlineArray2<uint8_t> uiPadding;
    public int32_t iLastDeltaQp;
    public InlineArray2<InlineArray17<int16_t>> iMvScale; // [LIST_A][MAX_DPB_COUNT] Moton vector scale For Temporal Direct Mode Type
}
