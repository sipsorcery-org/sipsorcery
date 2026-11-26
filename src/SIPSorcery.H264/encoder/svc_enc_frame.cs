//-----------------------------------------------------------------------------
// Filename: svc_enc_frame.cs
//
// Description: Port of codec/encoder/core/inc/svc_enc_frame.h (E3, E4):
// the layer information and the DQ layer. SDqLayer is partial: the picture
// pointers (pRefPic, pDecPic, pRefOri) arrived with SPicture in E4, and
// pFeatureSearchPreparation (with SFeatureSearchPreparation) comes with the
// screen content feature search in Phase 4. As with sWelsEncCtx, the port never copies or memsets a whole
// SDqLayer, so the missing fields don't move any that are read.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E3).
// 26 Nov 2026  Claude          The SDqLayer picture pointers (E4).
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

/*
 *  Frame level in SVC DQLayer instead.
 *  Dependency-Quaility layer struction definition for SVC extension of H.264/AVC
 */

///////////////////////////////////DQ Layer level///////////////////////////////////

// typedef struct TagFeatureSearchPreparation SFeatureSearchPreparation comes with Phase 4 (screen content).

/// <summary>TagSliceBufferInfo.</summary>
internal unsafe struct SSliceBufferInfo
{
    public SSlice* pSliceBuffer;                // slice buffer for multi thread,
    public int32_t iMaxSliceNum;
    public int32_t iCodedSliceNum;
}

/// <summary>TagLayerInfo.</summary>
internal unsafe struct SLayerInfo
{
    public SNalUnitHeaderExt sNalHeaderExt;
    public SSubsetSps* pSubsetSpsP;             // current pSubsetSps used, memory alloc in external
    public SWelsSPS* pSpsP;                     // current pSps based avc used, memory alloc in external
    public SWelsPPS* pPpsP;                     // current pPps used
}

/* Layer Representation */
/// <summary>TagDqLayer (pDqLayer and SWelsSvcFrame are SDqLayer; partial, see the file header).</summary>
internal unsafe struct SDqLayer
{
    public SLayerInfo sLayerInfo;
    public InlineArray4<SSliceBufferInfo> sSliceBufferInfo; // [MAX_THREADS_NUM]
    public SSlice** ppSliceInLayer;
    public SSliceCtx sSliceEncCtx;              // current slice context
    public PtrArray3<uint8_t> pCsData;          // pointer to reconstructed picture pData
    public fixed int iCsStride[3];              // Cs stride

    public PtrArray3<uint8_t> pEncData;         // pData picture to be encoded in current layer
    public fixed int iEncStride[3];             // pData picture stride

    public SMB* sMbDataP;                       // pointer to mb of mbAddr equal to 0 in slice, mb_data_ptr = mb_base_ptr + (1+iMbStride).
    public int16_t iMbWidth;                    // MB width of this picture, equal to pSps.iMbWidth
    public int16_t iMbHeight;                   // MB height of this picture, equal to pSps.iMbHeight;

    public bool bBaseLayerAvailableFlag;        // whether base layer is available for prediction?
    public bool bSatdInMdFlag;                  // whether SATD is calculated in ME and integer-pel MD

    public uint8_t iLoopFilterDisableIdc;       // 0: on, 1: off, 2: on except for slice boundaries
    public int8_t iLoopFilterAlphaC0Offset;     // AlphaOffset: valid range [-6, 6], default 0
    public int8_t iLoopFilterBetaOffset;        // BetaOffset:  valid range [-6, 6], default 0
    public uint8_t uiDisableInterLayerDeblockingFilterIdc;
    public int8_t iInterLayerSliceAlphaC0Offset;
    public int8_t iInterLayerSliceBetaOffset;
    public bool bDeblockingParallelFlag;        //parallel_deblocking_flag

    public SPicture* pRefPic;                   // reference picture pointer
    public SPicture* pDecPic;                   // reconstruction picture pointer for layer
    public PtrArray16<SPicture> pRefOri;        // [MAX_REF_PIC_COUNT]

    public bool bThreadSlcBufferFlag;
    public bool bSliceBsBufferFlag;
    public int32_t iMaxSliceNum;
    public fixed int NumSliceCodedOfPartition[4];   // [MAX_THREADS_NUM] for dynamic slicing mode
    public fixed int LastCodedMbIdxOfPartition[4];  // [MAX_THREADS_NUM] for dynamic slicing mode
    public fixed int FirstMbIdxOfPartition[4];      // [MAX_THREADS_NUM] for dynamic slicing mode
    public fixed int EndMbIdxOfPartition[4];        // [MAX_THREADS_NUM] for dynamic slicing mode
    public int32_t* pFirstMbIdxOfSlice;
    public int32_t* pCountMbNumInSlice;

    public bool bNeedAdjustingSlicing;

    // SFeatureSearchPreparation* pFeatureSearchPreparation comes with Phase 4 (screen content).

    public SDqLayer* pRefLayer;                 // pointer to referencing dq_layer of current layer to be decoded
}
