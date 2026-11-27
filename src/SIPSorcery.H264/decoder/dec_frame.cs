//-----------------------------------------------------------------------------
// Filename: dec_frame.cs
//
// Description: Port of codec/decoder/core/inc/dec_frame.h: the layer and
// dependency/quality (DQ) layer representations.
// OpenH264 commit 78783f19 (v2.7.0-1).
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

///////////////////////////////////DQ Layer level///////////////////////////////////
/// <summary>TagLayerInfo (PLayerInfo is SLayerInfo*).</summary>
internal unsafe struct SLayerInfo
{
    public SNalUnitHeaderExt sNalHeaderExt;
    public SSlice sSliceInLayer;  // Here Slice identify to Frame on concept
    public SSubsetSps* pSubsetSps;     // current pSubsetSps used, memory alloc in external
    public SSps* pSps;           // current sps based avc used, memory alloc in external
    public SPps* pPps;           // current pps used
}

/* Layer Representation */
/// <summary>TagDqLayer (PDqLayer is SDqLayer*).</summary>
internal unsafe struct SDqLayer
{
    public SLayerInfo sLayerInfo;

    public SBitStringAux* pBitStringAux;  // pointer to SBitStringAux
    public SFmo* pFmo;           // Current fmo context pointer used
    public uint32_t* pMbType;
    public int32_t* pSliceIdc;                           // using int32_t for slice_idc
    public PtrArray2<InlineArray16<InlineArray2<int16_t>>> pMv;  // [LIST_A] -> [MB_BLOCK4x4_NUM][MV_A]
    public PtrArray2<InlineArray16<InlineArray2<int16_t>>> pMvd; // [LIST_A] -> [MB_BLOCK4x4_NUM][MV_A]
    public PtrArray2<InlineArray16<int8_t>> pRefIndex;           // [LIST_A] -> [MB_BLOCK4x4_NUM]
    public InlineArray16<int8_t>* pDirect;                       // [MB_BLOCK4x4_NUM]
    public bool* pNoSubMbPartSizeLessThan8x8Flag;
    public bool* pTransformSize8x8Flag;
    public int8_t* pLumaQp;
    public InlineArray2<int8_t>* pChromaQp;
    public int8_t* pCbp;
    public uint16_t* pCbfDc;
    public InlineArray24<int8_t>* pNzc;
    public InlineArray24<int8_t>* pNzcRs;
    public int8_t* pResidualPredFlag;
    public int8_t* pInterPredictionDoneFlag;
    public bool* pMbCorrectlyDecodedFlag;
    public bool* pMbRefConcealedFlag;
    public InlineArray384<int16_t>* pScaledTCoeff;           // [MB_COEFF_LIST_SIZE]
    public InlineArray8<int8_t>* pIntraPredMode;  //0~3 top4x4 ; 4~6 left 4x4; 7 intra16x16
    public InlineArray16<int8_t>* pIntra4x4FinalMode;        // [MB_BLOCK4x4_NUM]
    public uint8_t* pIntraNxNAvailFlag;
    public int8_t* pChromaPredMode;
    //uint8_t (*motion_pred_flag[LIST_A])[MB_PARTITION_SIZE]; // 8x8
    public InlineArray4<uint32_t>* pSubMbType;               // [MB_SUB_PARTITION_SIZE]
    public int32_t iLumaStride;
    public int32_t iChromaStride;
    public PtrArray3<uint8_t> pPred;
    public int32_t iMbX;
    public int32_t iMbY;
    public int32_t iMbXyIndex;
    public int32_t iMbWidth;               // MB width of this picture, equal to sSps.iMbWidth
    public int32_t iMbHeight;              // MB height of this picture, equal to sSps.iMbHeight;

    /* Common syntax elements across all slices of a DQLayer */
    public int32_t iSliceIdcBackup;
    public uint32_t uiSpsId;
    public uint32_t uiPpsId;
    public uint32_t uiDisableInterLayerDeblockingFilterIdc;
    public int32_t iInterLayerSliceAlphaC0Offset;
    public int32_t iInterLayerSliceBetaOffset;
    //SPosOffset              sScaledRefLayer;
    public int32_t iSliceGroupChangeCycle;

    public SRefPicListReorderSyn* pRefPicListReordering;
    public SPredWeightTabSyn* pPredWeightTable;
    public SRefPicMarking* pRefPicMarking; // Decoded reference picture marking syntaxs
    public SRefBasePicMarking* pRefPicBaseMarking;

    public SPicture* pRef;                   // reference picture pointer
    public SPicture* pDec;                   // reconstruction picture pointer for layer

    public InlineArray2<InlineArray16<InlineArray2<int16_t>>> iColocMv;     //Colocated MV cache
    public InlineArray2<InlineArray16<int8_t>> iColocRefIndex;  //Colocated RefIndex cache
    public fixed sbyte iColocIntra[16];			  //Colocated Intra cache

    public bool bUseWeightPredictionFlag;
    public bool bUseWeightedBiPredIdc;
    public bool bStoreRefBasePicFlag;                           // iCurTid == 0 && iCurQid = 0 && bEncodeKeyPic = 1
    public bool bTCoeffLevelPredFlag;
    public bool bConstrainedIntraResamplingFlag;
    public uint8_t uiRefLayerDqId;
    public uint8_t uiRefLayerChromaPhaseXPlus1Flag;
    public uint8_t uiRefLayerChromaPhaseYPlus1;
    public uint8_t uiLayerDqId;                    // dq_id of current layer
    public bool bUseRefBasePicFlag;     // whether reference pic or reference base pic is referred?
}

/// <summary>TagGpuAvcLayer (PGpuAvcDqLayer is SGpuAvcDqLayer*).</summary>
internal unsafe struct SGpuAvcDqLayer
{
    public SLayerInfo sLayerInfo;
    public SBitStringAux* pBitStringAux;  // pointer to SBitStringAux

    public uint32_t* pMbType;
    public int32_t* pSliceIdc;      // using int32_t for slice_idc
    public int8_t* pLumaQp;
    public int8_t* pCbp;
    public InlineArray24<int8_t>* pNzc;
    public InlineArray8<int8_t>* pIntraPredMode;     //0~3 top4x4 ; 4~6 left 4x4; 7 intra16x16
    public int32_t iMbX;
    public int32_t iMbY;
    public int32_t iMbXyIndex;
    public int32_t iMbWidth;               // MB width of this picture, equal to sSps.iMbWidth
    public int32_t iMbHeight;              // MB height of this picture, equal to sSps.iMbHeight;
}
