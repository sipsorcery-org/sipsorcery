//-----------------------------------------------------------------------------
// Filename: encoder_context.cs
//
// Description: Port of codec/encoder/core/inc/encoder_context.h, partial:
// SRefList, SLTRState, SSpatialPicIndex, SStrideTables and the sWelsEncCtx
// fields that parameter set generation and writing (E1), the CAVLC
// macroblock writer (E3), the I-slice macroblock coder (E4), the frame set-up
// (E4b), the P-slice coder (E5), the reference list manager (E5b), the rate
// control (E6), the preprocessing (E7) and the init, frame loop and
// statistics (E8) use. The
// remaining fields arrive with the code that reads them, in the upstream
// order. The port never copies or
// memsets the context as a whole, so its layout only needs to hold these
// fields, not mirror the C one.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (E1).
// 26 Nov 2026  Claude          pCurDqLayer and eSliceType (E3).
// 26 Nov 2026  Claude          SStrideTables, the picture pointers and the RC fields (E4).
// 26 Nov 2026  Claude          SRefList, SSpatialPicIndex and the layer, list and NAL fields (E4b).
// 26 Nov 2026  Claude          The MVD cost table fields and pVaa (E5).
// 26 Nov 2026  Claude          SLTRState, pReferenceStrategy, pLtr and bRefOfCurTidIsLtr (E5b).
// 26 Nov 2026  Claude          The rate control check window fields (E6).
// 27 Nov 2026  Claude          pVpp (E7).
// 27 Nov 2026  Claude          The statistics fields and the port's pLastFsnr (E8).
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

/// <summary>TagRefList.</summary>
internal unsafe struct SRefList
{
    public PtrArray9<SPicture> pShortRefList;   // [1 + MAX_SHORT_REF_COUNT] reference list 0 - int16_t
    public PtrArray17<SPicture> pLongRefList;   // [1 + MAX_REF_PIC_COUNT] reference list 1 - int32_t
    public SPicture* pNextBuffer;
    public PtrArray17<SPicture> pRef;           // [1 + MAX_REF_PIC_COUNT] plus 1 for swap intend
    public uint8_t uiShortRefCount;
    public uint8_t uiLongRefCount;              // dependend on pRef pic module
}

/// <summary>TagLTRState.</summary>
internal unsafe struct SLTRState
{
    // LTR mark feedback
    public uint32_t uiLtrMarkState;             // LTR mark state, indicate whether there is a LTR mark feedback unsolved
    public int32_t iLtrMarkFbFrameNum;          // the unsolved LTR mark feedback, the marked iFrameNum feedback from decoder

    // LTR used as recovery reference
    public int32_t iLastRecoverFrameNum;        // reserve the last LTR or IDR recover iFrameNum
    public int32_t iLastCorFrameNumDec;         // reserved the last correct position in decoder side, use to select valid LTR to recover or to decide the LTR mark validation
    public int32_t iCurFrameNumInDec;           // current iFrameNum in decoder side, use to select valid LTR to recover or to decide the LTR mark validation

    // LTR mark
    public int32_t iLTRMarkMode;                // direct mark or delay mark
    public int32_t iLTRMarkSuccessNum;          //successful marked num, for mark mode switch
    public int32_t iCurLtrIdx;                  // current int32_t term reference index to mark
    public fixed int32_t iLastLtrIdx[codec_app_def.MAX_TEMPORAL_LAYER_NUM];
    public int32_t iSceneLtrIdx;                // related to Scene LTR, used by screen content

    public uint32_t uiLtrMarkInterval;          // the interval from the last int32_t term pRef mark

    public bool bLTRMarkingFlag;                //decide whether current frame marked as LTR
    public bool bLTRMarkEnable;                 //when LTR is confirmed and the interval is no smaller than the marking period
    public bool bReceivedT0LostFlag;            // indicate whether a t0 lost feedback is recieved, for LTR recovery
}


/// <summary>TagSpatialPicIndex.</summary>
internal unsafe struct SSpatialPicIndex
{
    public SPicture* pSrc;                      // I420 based and after color space converted
    public int32_t iDid;                        // dependency id
}

/// <summary>TagStrideTables.</summary>
internal unsafe struct SStrideTables
{
    public InlineArray4<PtrArray2<int32_t>> pStrideDecBlockOffset; // [MAX_DEPENDENCY_LAYER][2]: [iDid][tid==0][24 x 4]: luma+chroma= 24 x 4
    public PtrArray4<int32_t> pStrideEncBlockOffset;    // [MAX_DEPENDENCY_LAYER]: [iDid][24 x 4]: luma+chroma= 24 x 4
    public PtrArray4<int16_t> pMbIndexX;                // [MAX_DEPENDENCY_LAYER]: [iDid][iMbX]: map for iMbX in each spatial layer coding
    public PtrArray4<int16_t> pMbIndexY;                // [MAX_DEPENDENCY_LAYER]: [iDid][iMbY]: map for iMbY in each spatial layer coding
}

/*
 *  Encoder Context
 */
/// <summary>TagWelsEncCtx (partial, see the file header).</summary>
internal unsafe struct sWelsEncCtx
{
    public SLogContext sLogCtx;
    // Input
    public SWelsSvcCodingParam* pSvcParam;      // SVC parameter, WelsSVCParamConfig in svc_param_settings.h

    public int32_t* pSadCostMb;
    /* MVD cost tables for Inter MB */
    public int32_t iMvRange;
    public uint16_t* pMvdCostTable;             //[52];  // adaptive to spatial layers
    public int32_t iMvdCostTableSize;           //the size of above table
    public int32_t iMvdCostTableStride;         //the stride of above table
    public SMVUnitXY* pMvUnitBlock4x4;          // (*pMvUnitBlock4x4[2])[MB_BLOCK4x4_NUM];          // for store each 4x4 blocks' mv unit, the two swap after different d layer
    public int8_t* pRefIndexBlock4x4;           // (*pRefIndexBlock4x4[2])[MB_BLOCK8x8_NUM];        // for store each 4x4 blocks' pRef index, the two swap after different d layer
    public int8_t* pNonZeroCountBlocks;         // (*pNonZeroCountBlocks)[MB_LUMA_CHROMA_BLOCK4x4_NUM];
    public int8_t* pIntra4x4PredModeBlocks;     // (*pIntra4x4PredModeBlocks)[INTRA_4x4_MODE_NUM];  //last byte is not used; the first 4 byte is for the bottom 12,13,14,15 4x4 block intra mode, and 3 byte for (3,7,11)

    public SMB** ppMbListD;                     // [MAX_DEPENDENCY_LAYER];
    public SStrideTables* pStrideTab;           // stride tables for internal coding used
    public SWelsFuncPtrList* pFuncList;

    // pSliceThreading and pTaskManage come with the threading work.
    public IWelsReferenceStrategy* pReferenceStrategy;

    // pointers
    public SPicture* pEncPic;                   // pointer to current picture to be encoded
    public SPicture* pDecPic;                   // pointer to current picture being reconstructed
    public SPicture* pRefPic;                   // pointer to current reference picture

    public SDqLayer* pCurDqLayer;               // DQ layer context used to being encoded currently, for reference base layer to refer: pCurDqLayer->pRefLayer if applicable
    public SDqLayer** ppDqLayerList;            // overall DQ layers encoded for storage

    public SRefList** ppRefPicListExt;          // reference picture list for SVC
    public PtrArray16<SPicture> pRefList0;      // [16]
    public SLTRState* pLtr;                     //[MAX_DEPENDENCY_LAYER];

    public bool bCurFrameMarkedAsSceneLtr;
    // Derived

    public EWelsSliceType eSliceType;           // currently coding slice type
    public EWelsNalUnitType eNalType;           // NAL type
    public EWelsNalRefIdc eNalPriority;         // NAL_Reference_Idc currently
    public InlineArray4<EWelsNalRefIdc> eLastNalPriority; // [MAX_DEPENDENCY_LAYER] NAL_Reference_Idc in last frame
    public uint8_t iNumRef0;

    public uint8_t uiDependencyId;              // Idc of dependecy layer to be coded
    public uint8_t uiTemporalId;                // Idc of temporal layer to be coded
    public bool bNeedPrefixNalFlag;             // whether add prefix nal

    // Rate control routine
    public SWelsSvcRc* pWelsSvcRc;
    public bool bCheckWindowStatusRefreshFlag;
    public int64_t iCheckWindowStartTs;
    public int64_t iCheckWindowCurrentTs;
    public int32_t iCheckWindowInterval;
    public int32_t iCheckWindowIntervalShift;
    public bool bCheckWindowShiftResetFlag;
    public int32_t iGlobalQp;                   // global qp

    public SVAAFrameInfo* pVaa;                 // VAA information of reference
    public CWelsPreProcess* pVpp;

    public SWelsSPS* pSpsArray;                 // MAX_SPS_COUNT by standard compatible
    public SWelsSPS* pSps;
    public SWelsPPS* pPPSArray;                 // MAX_PPS_COUNT by standard compatible
    public SWelsPPS* pPps;
    /* SVC only */
    public SSubsetSps* pSubsetArray;            // MAX_SPS_COUNT by standard compatible
    public SSubsetSps* pSubsetSps;
    public int32_t iSpsNum;                     // number of pSps used
    public int32_t iSubsetSpsNum;               // number of pSps used
    public int32_t iPpsNum;                     // number of pPps used

    // Output
    public SWelsEncoderOutput* pOut;            // for NAL raw pData (need allocating memory for sNalList internal)
    public uint8_t* pFrameBs;                   // restoring bitstream pBuffer of all NALs in a frame
    public int32_t iFrameBsSize;                // count size of frame bs in bytes allocated
    public int32_t iPosBsBuffer;                // current writing position of frame bs pBuffer

    public InlineArray4<SSpatialPicIndex> sSpatialIndexMap; // [MAX_DEPENDENCY_LAYER]
    public fixed int32_t iSliceBufferSize[WelsEnc.MAX_DEPENDENCY_LAYER];

    public fixed bool bRefOfCurTidIsLtr[WelsEnc.MAX_DEPENDENCY_LAYER * WelsEnc.MAX_TEMPORAL_LEVEL]; // [MAX_DEPENDENCY_LAYER][MAX_TEMPORAL_LEVEL]

    public int32_t iMaxSliceCount;              // maximal count number of slices for all layers observation
    public int16_t iActiveThreadsNum;           // number of threads active so far

    /*
     * DQ layer idc map for svc encoding, might be a better scheme than that of design before,
     * can aware idc of referencing layer and that idc of successive layer to be coded
     */
    /* SVC only */
    public SDqIdc* pDqIdcMap;                   // overall DQ map of full scalability in specific frame (All full D/T/Q layers involved)

    public SParaSetOffset sPSOVector;
    public SParaSetOffset* pPSOVector;
    public CMemoryAlign* pMemAlign;

    //related to Statistics
    public int64_t uiStartTimestamp;
    public InlineArray4<SEncoderStatistics> sEncoderStatistics; // [MAX_DEPENDENCY_LAYER]
    public int32_t iStatisticsLogInterval;
    public int64_t iLastStatisticsLogTs;

    public int32_t iEncoderError;
    // mutexEncoderError guards iEncoderError between slice threads, which the port does not run.
    public bool bDeliveryFlag;
    // sWelsCabacContexts comes with CABAC (Phase 4).

    public int64_t uiLastTimestamp;
    // pDynamicBsBuffer comes with the threaded dynamic slicing.

    // Port addition for SIPSorcery: the reconstruction of the last coded
    // layer. WelsEncoderEncodeExt sets these where upstream's frame dump
    // writes fsnr, since pDecPic has moved on to the next buffer by the time
    // EncodeFrame returns. bLastFsnrDeblocked is false when the encoder left
    // the picture unfiltered: a non-reference frame of the top temporal
    // layer, or a frame that was not coded.
    public SPicture* pLastFsnr;
    public bool bLastFsnrDeblocked;
}
