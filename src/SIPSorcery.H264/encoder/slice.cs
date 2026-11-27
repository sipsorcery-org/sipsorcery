//-----------------------------------------------------------------------------
// Filename: slice.cs
//
// Description: Port of codec/encoder/core/inc/slice.h: the encoder's slice
// header syntax (reordering, reference marking), the slice-level rate control
// statistics and the slice structure.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// C's anonymous element structs (SReorderingSyntax[], SMmcoRef[]) are named
// after the field they make up, with an "Entry" suffix, as in the decoder.
// The encoder builds with DISABLE_FMO_FEATURE (as264_common.h), so
// SSliceHeader has no iSliceGroupChangeCycle.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E3).
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

/*******************************sub struct of slice header****************************/

/// <summary>TagRefPicListReorderSyntax's anonymous SReorderingSyntax[] element.</summary>
internal struct SReorderingSyntaxEntry
{
    public uint32_t uiAbsDiffPicNumMinus1;      //uiAbsDiffPicNumMinus1 SHOULD be in the range of [4, (1<<pSps->uiLog2MaxFrameNum)-1], {p104, JVT-X201wcm1}
    //but int8_t can't cover the range, SHOULD modify it.
    public uint16_t iLongTermPicNum;
    public uint16_t uiReorderingOfPicNumsIdc;   //in order to pack 2-uint16_t into 1-(u)int32_t, so modify the type into uint16_t.
}

/*
 *  Reference picture list reordering syntax, refer to page 64 in JVT X201wcm
 */
/// <summary>TagRefPicListReorderSyntax.</summary>
internal struct SRefPicListReorderSyntax
{
    public InlineArray2<SReorderingSyntaxEntry> SReorderingSyntax; // [MAX_REFERENCE_REORDER_COUNT_NUM]   // MAX_REF_PIC_COUNT
}

/// <summary>TagRefPicMarking's anonymous SMmcoRef[] element.</summary>
internal struct SMmcoRefEntry
{
    public int32_t iMmcoType;
    public int32_t iShortFrameNum;
    public int32_t iDiffOfPicNum;
    public int32_t iLongTermPicNum;
    public int32_t iLongTermFrameIdx;
    public int32_t iMaxLongTermFrameIdx;
}

/* Decoded reference picture marking syntax, refer to Page 66 in JVT X201wcm */
/// <summary>TagRefPicMarking.</summary>
internal struct SRefPicMarking
{
    public InlineArray4<SMmcoRefEntry> SMmcoRef; // [MAX_REFERENCE_MMCO_COUNT_NUM]       // MAX_MMCO_COUNT

    // int32_t         mmco_index;
    public uint8_t uiMmcoCount;
    public bool bNoOutputOfPriorPicsFlag;
    public bool bLongTermRefFlag;
    public bool bAdaptiveRefPicMarkingModeFlag;
}

// slice level rc statistic info
/// <summary>TagRCSlicing.</summary>
internal struct SRCSlicing
{
    public int32_t iComplexityIndexSlice;
    public int32_t iCalculatedQpSlice;
    public int32_t iStartMbSlice;
    public int32_t iEndMbSlice;
    public int32_t iTotalQpSlice;
    public int32_t iTotalMbSlice;
    public int32_t iTargetBitsSlice;
    public int32_t iBsPosSlice;
    public int32_t iFrameBitsSlice;
    public int32_t iGomBitsSlice;
    public int32_t iGomTargetBits;
    //int32_t   gom_coded_mb;
}

/* Header of slice syntax elements, refer to Page 63 in JVT X201wcm */
/// <summary>TagSliceHeader (PSliceHeader is SSliceHeader*).</summary>
internal unsafe struct SSliceHeader
{
    /*****************************slice header syntax and generated****************************/
    public int32_t iFirstMbInSlice;
    // uint32_t        pic_parameter_set_id;
    public int32_t iFrameNum;
    public int32_t iPicOrderCntLsb;

    // int32_t         delta_pic_order_cnt_bottom;
    // int32_t         delta_pic_order_cnt[2];
    // int32_t         redundant_pic_cnt;

    public EWelsSliceType eSliceType;
    public uint8_t uiNumRefIdxL0Active;         //
    //int32_t         num_ref_idx_l1_active_minus1    //B frame is not supported
    public uint8_t uiRefCount;
    //Ref_Pic         *ref_pic;
    public uint8_t uiRefIndex;                  // exact reference picture index for slice

    public int8_t iSliceQpDelta;
    // int32_t         slice_qp;
    // int32_t         slice_qs_delta;         // For SP/SI slices
    public uint8_t uiDisableDeblockingFilterIdc;
    public int8_t iSliceAlphaC0Offset;
    public int8_t iSliceBetaOffset;
    // int32_t iSliceGroupChangeCycle: not with DISABLE_FMO_FEATURE

    public SWelsSPS* pSps;
    public SWelsPPS* pPps;
    public int32_t iSpsId;
    public int32_t iPpsId;

    public uint16_t uiIdrPicId;
    // uint8_t         color_plane_id;//from?

    public bool bNumRefIdxActiveOverrideFlag;
    // bool            field_pic_flag;         //not supported in base profile
    // bool            bottom_field_flag;              //not supported in base profile
    public uint8_t uiPadding1Bytes;

    public SRefPicMarking sRefMarking;          // Decoded reference picture marking syntaxs

    public SRefPicListReorderSyntax sRefReordering; // Reference picture list reordering syntaxs
}


/* SSlice header in scalable extension syntax, refer to Page 394 in JVT X201wcm */
/// <summary>TagSliceHeaderExt (PSliceHeaderExt is SSliceHeaderExt*).</summary>
internal unsafe struct SSliceHeaderExt
{
    public SSliceHeader sSliceHeader;

    public SSubsetSps* pSubsetSps;

    public uint32_t uiNumMbsInSlice;

    public bool bStoreRefBasePicFlag;
    public bool bConstrainedIntraResamplingFlag;
    public bool bSliceSkipFlag;

    public bool bAdaptiveBaseModeFlag;
    public bool bDefaultBaseModeFlag;
    public bool bAdaptiveMotionPredFlag;
    public bool bDefaultMotionPredFlag;

    public bool bAdaptiveResidualPredFlag;
    public bool bDefaultResidualPredFlag;
    public bool bTcoeffLevelPredFlag;
    public uint8_t uiDisableInterLayerDeblockingFilterIdc;
}


/// <summary>TagSlice (PSlice is SSlice*).</summary>
internal unsafe struct SSlice
{
    // mainly for multiple threads imp.
    public SMbCache sMbCacheInfo;               // MBCache is introduced within slice dependency
    public SBitStringAux* pSliceBsa;
    public SWelsSliceBs sSliceBs;

    /*******************************sSliceHeader****************************/
    public SSliceHeaderExt sSliceHeaderExt;

    public SMVUnitXY sMvStartMin;
    public SMVUnitXY sMvStartMax;
    public InlineArray5<SMVUnitXY> sMvc;        // [5]
    public uint8_t uiMvcNum;
    public uint8_t sScaleShift;

    public int32_t iSliceIdx;
    public uint32_t uiBufferIdx;
    public bool bSliceHeaderExtFlag;            // Indicate which slice header is used, avc or ext?
    public uint8_t uiLastMbQp;                  // stored qp for last mb coded, maybe more efficient for mb skip detection etc.

    public bool bDynamicSlicingSliceSizeCtrlFlag;
    public uint8_t uiAssumeLog2BytePerMb;

    public uint32_t uiSliceFMECostDown;         //TODO: for FME switch under MT, to opt after ME final?

    public uint8_t uiReservedFillByte;          // reserved to meet 4 bytes alignment

    public SCabacCtx sCabacCtx;
    public int32_t iCabacInitIdc;
    public int32_t iMbSkipRun;

    public int32_t iCountMbNumInSlice;
    public uint32_t uiSliceConsumeTime;
    public int32_t iSliceComplexRatio;

    public SRCSlicing sSlicingOverRc;           //slice level rc statistic info
}
