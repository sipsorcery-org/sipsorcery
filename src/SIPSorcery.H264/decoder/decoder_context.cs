//-----------------------------------------------------------------------------
// Filename: decoder_context.cs
//
// Description: Port of codec/decoder/core/inc/decoder_context.h: the decoder
// context (SWelsDecoderContext) and the structures it is made of.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Arrays of function pointers are InlineArrayN<nint> storage behind a property
// that returns the alias pointer type, as for SExpandPicFunc. The anonymous
// sMb struct is named SWelsDecoderContextMb. WELS_MUTEX and the thread
// routine pointer are opaque (void*): the port decodes on the caller's thread.
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
 *     Copyright (c)  2009-2013, Cisco Systems
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
 *
 * \file    decoder_context.h
 *
 * \brief   mainly interface introduced in Wels decoder side
 *
 * \date    3/4/2009 Created
 *
 *************************************************************************************
 */

/*typedef for get intra predictor func pointer*/
global using unsafe PGetIntraPredFunc = delegate*<byte*, int, void>;
global using unsafe PIdctResAddPredFunc = delegate*<byte*, int, short*, void>;
global using unsafe PIdctFourResAddPredFunc = delegate*<byte*, int, short*, sbyte*, void>;
global using unsafe PGetIntraPred8x8Func = delegate*<byte*, int, bool, bool, void>;
global using unsafe PCopyFunc = delegate*<byte*, int, byte*, int, void>;
global using unsafe PDeblockingFilterMbFunc = delegate*<SIPSorcery.H264.Decoder.SDqLayer*, SIPSorcery.H264.Decoder.SDeblockingFilter*, int, void>;
global using unsafe PLumaDeblockingLT4Func = delegate*<byte*, int, int, int, sbyte*, void>;
global using unsafe PLumaDeblockingEQ4Func = delegate*<byte*, int, int, int, void>;
global using unsafe PChromaDeblockingLT4Func = delegate*<byte*, byte*, int, int, int, sbyte*, void>;
global using unsafe PChromaDeblockingEQ4Func = delegate*<byte*, byte*, int, int, int, void>;
global using unsafe PChromaDeblockingLT4Func2 = delegate*<byte*, int, int, int, sbyte*, void>;
global using unsafe PChromaDeblockingEQ4Func2 = delegate*<byte*, int, int, int, void>;
global using unsafe PWelsNonZeroCountFunc = delegate*<sbyte*, void>;
global using unsafe PWelsBlockZeroFunc = delegate*<short*, int, void>;
global using unsafe PWelsFillNeighborMbInfoIntra4x4Func = delegate*<SIPSorcery.H264.Decoder.SWelsNeighAvail*, byte*, sbyte*, SIPSorcery.H264.Decoder.SDqLayer*, void>;
global using unsafe PWelsMapNeighToSample = delegate*<SIPSorcery.H264.Decoder.SWelsNeighAvail*, int*, void>;
global using unsafe PWelsMap16NeighToSample = delegate*<SIPSorcery.H264.Decoder.SWelsNeighAvail*, byte*, void>;
global using unsafe PWelsParseIntra4x4ModeFunc = delegate*<SIPSorcery.H264.Decoder.SWelsNeighAvail*, sbyte*, SIPSorcery.H264.SBitStringAux*, SIPSorcery.H264.Decoder.SDqLayer*, int>;
global using unsafe PWelsParseIntra16x16ModeFunc = delegate*<SIPSorcery.H264.Decoder.SWelsNeighAvail*, SIPSorcery.H264.SBitStringAux*, SIPSorcery.H264.Decoder.SDqLayer*, int>;

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Decoder;

/// <summary>SWels_Cabac_Element (PWelsCabacCtx is SWelsCabacCtx*).</summary>
internal struct SWelsCabacCtx
{
    public uint8_t uiState;
    public uint8_t uiMPS;
}

/// <summary>SWelsCabacDecEngine (PWelsCabacDecEngine is SWelsCabacDecEngine*).</summary>
internal unsafe struct SWelsCabacDecEngine
{
    public uint64_t uiRange;
    public uint64_t uiOffset;
    public int32_t iBitsLeft;
    public uint8_t* pBuffStart;
    public uint8_t* pBuffCurr;
    public uint8_t* pBuffEnd;
}

/// <summary>TagDataBuffer.</summary>
internal unsafe struct SDataBuffer
{
    public uint8_t* pHead;
    public uint8_t* pEnd;

    public uint8_t* pStartPos;
    public uint8_t* pCurPos;
}

/// <summary>TagSpsBsInfo.</summary>
internal unsafe struct SSpsBsInfo
{
    public fixed byte pSpsBsBuf[WelsDec.SPS_PPS_BS_SIZE];
    public int32_t iSpsId;
    public uint16_t uiSpsBsLen;
}

/// <summary>TagPpsBsInfo.</summary>
internal unsafe struct SPpsBsInfo
{
    public fixed byte pPpsBsBuf[WelsDec.SPS_PPS_BS_SIZE];
    public int32_t iPpsId;
    public uint16_t uiPpsBsLen;
}

/**/
/// <summary>TagRefPic (PRefPic is SRefPic*).</summary>
internal unsafe struct SRefPic
{
    public InlineArray2<PtrArray17<SPicture>> pRefList;      // [LIST_A][MAX_DPB_COUNT] reference picture marking plus FIFO scheme
    public InlineArray2<PtrArray17<SPicture>> pShortRefList; // [LIST_A][MAX_DPB_COUNT]
    public InlineArray2<PtrArray17<SPicture>> pLongRefList;  // [LIST_A][MAX_DPB_COUNT]
    public fixed byte uiRefCount[2];
    public fixed byte uiShortRefCount[2];
    public fixed byte uiLongRefCount[2]; // dependend on ref pic module
    public int32_t iMaxLongTermFrameIdx;
}

/// <summary>TagCopyFunc.</summary>
internal unsafe struct SCopyFunc
{
    public PCopyFunc pCopyLumaFunc;
    public PCopyFunc pCopyChromaFunc;
}

//deblock module defination
/// <summary>tagDeblockingFilter (PDeblockingFilter is SDeblockingFilter*).</summary>
internal unsafe struct SDeblockingFilter
{
    public PtrArray3<uint8_t> pCsData;     // pointer to reconstructed picture data
    public fixed int iCsStride[2];   // Cs stride
    public EWelsSliceType eSliceType;
    public int8_t iSliceAlphaC0Offset;
    public int8_t iSliceBetaOffset;
    public fixed sbyte iChromaQP[2];
    public int8_t iLumaQP;
    public SDeblockingFunc* pLoopf;
    public PtrArray2<Ptr<SPicture>> pRefPics; // PPicture* pRefPics[LIST_A]
}

/// <summary>TagDeblockingFunc (PDeblockingFunc is SDeblockingFunc*).</summary>
internal unsafe struct SDeblockingFunc
{
    public PLumaDeblockingLT4Func pfLumaDeblockingLT4Ver;
    public PLumaDeblockingEQ4Func pfLumaDeblockingEQ4Ver;
    public PLumaDeblockingLT4Func pfLumaDeblockingLT4Hor;
    public PLumaDeblockingEQ4Func pfLumaDeblockingEQ4Hor;

    public PChromaDeblockingLT4Func pfChromaDeblockingLT4Ver;
    public PChromaDeblockingEQ4Func pfChromaDeblockingEQ4Ver;
    public PChromaDeblockingLT4Func pfChromaDeblockingLT4Hor;
    public PChromaDeblockingEQ4Func pfChromaDeblockingEQ4Hor;

    public PChromaDeblockingLT4Func2 pfChromaDeblockingLT4Ver2;
    public PChromaDeblockingEQ4Func2 pfChromaDeblockingEQ4Ver2;
    public PChromaDeblockingLT4Func2 pfChromaDeblockingLT4Hor2;
    public PChromaDeblockingEQ4Func2 pfChromaDeblockingEQ4Hor2;
}

/// <summary>TagBlockFunc.</summary>
internal unsafe struct SBlockFunc
{
    public PWelsNonZeroCountFunc pWelsSetNonZeroCountFunc;
    public PWelsBlockZeroFunc pWelsBlockZero16x16Func;
    public PWelsBlockZeroFunc pWelsBlockZero8x8Func;
}

//Decoder SPS and PPS global CTX
/// <summary>tagWelsWelsDecoderSpsPpsCTX (PWelsDecoderSpsPpsCTX is SWelsDecoderSpsPpsCTX*).</summary>
internal unsafe struct SWelsDecoderSpsPpsCTX
{
    public SPosOffset sFrameCrop;

    public InlineArray33<SSps> sSpsBuffer;   // [MAX_SPS_COUNT + 1]
    public InlineArray257<SPps> sPpsBuffer;  // [MAX_PPS_COUNT + 1]

    public InlineArray33<SSubsetSps> sSubsetSpsBuffer; // [MAX_SPS_COUNT + 1]
    public SNalUnit sPrefixNal;

    public PtrArray8<SSps> pActiveLayerSps; // [MAX_LAYER_NUM]
    public bool bAvcBasedFlag;          // For decoding bitstream:

    // for EC parameter sets
    public bool bSpsExistAheadFlag;     // whether does SPS NAL exist ahead of sequence?
    public bool bSubspsExistAheadFlag;// whether does Subset SPS NAL exist ahead of sequence?
    public bool bPpsExistAheadFlag;     // whether does PPS NAL exist ahead of sequence?

    public int32_t iSpsErrorIgnored;
    public int32_t iSubSpsErrorIgnored;
    public int32_t iPpsErrorIgnored;

    public fixed bool bSpsAvailFlags[WelsCommon.MAX_SPS_COUNT];
    public fixed bool bSubspsAvailFlags[WelsCommon.MAX_SPS_COUNT];
    public fixed bool bPpsAvailFlags[WelsDec.MAX_PPS_COUNT];
    public int32_t iPPSLastInvalidId;
    public int32_t iPPSInvalidNum;
    public int32_t iSPSLastInvalidId;
    public int32_t iSPSInvalidNum;
    public int32_t iSubSPSLastInvalidId;
    public int32_t iSubSPSInvalidNum;
    public int32_t iSeqId; //sequence id
    public int iOverwriteFlags;
}

//Last Decoded Picture Info
/// <summary>tagSWelsLastDecPicInfo (PWelsLastDecPicInfo is SWelsLastDecPicInfo*).</summary>
internal unsafe struct SWelsLastDecPicInfo
{
    // Save the last nal header info
    public SNalUnitHeaderExt sLastNalHdrExt;
    public SSliceHeader sLastSliceHeader;
    public int32_t iPrevPicOrderCntMsb;
    public int32_t iPrevPicOrderCntLsb;
    public SPicture* pPreviousDecodedPictureInDpb; //pointer to previously decoded picture in DPB for error concealment
    public int32_t iPrevFrameNum;// frame number of previous frame well decoded for non-truncated mode yet
    public bool bLastHasMmco5;
    public uint32_t uiDecodingTimeStamp; //represent relative decoding time stamps
}

/// <summary>tagPictInfo (PPictInfo is SPictInfo*).</summary>
internal struct SPictInfo
{
    public SBufferInfo sBufferInfo;
    public int32_t iPOC;
    public int32_t iPicBuffIdx;
    public uint32_t uiDecodingTimeStamp;
    public int32_t iSeqNum;
}

/// <summary>tagPictReoderingStatus (PPictReoderingStatus is SPictReoderingStatus*).</summary>
internal struct SPictReoderingStatus
{
    public int32_t iPictInfoIndex;
    public int32_t iMinSeqNum;
    public int32_t iMinPOC;
    public int32_t iNumOfPicts;
    public int32_t iLastWrittenSeqNum;
    public int32_t iLastWrittenPOC;
    public int32_t iLargestBufferedPicIndex;
    public bool bHasBSlice;
}

/// <summary>The anonymous TagWelsDecoderContext::sMb struct; every array is [LAYER_NUM_EXCHANGEABLE].</summary>
internal unsafe struct SWelsDecoderContextMb
{
    public PtrArray1<uint32_t> pMbType;                      /* mb type */
    public InlineArray1<PtrArray2<InlineArray16<InlineArray2<int16_t>>>> pMv; //[LAYER_NUM_EXCHANGEABLE   MB_BLOCK4x4_NUM*]
    public InlineArray1<PtrArray2<InlineArray16<int8_t>>> pRefIndex;
    public PtrArray1<InlineArray16<int8_t>> pDirect;
    public PtrArray1<bool> pNoSubMbPartSizeLessThan8x8Flag;
    public PtrArray1<bool> pTransformSize8x8Flag;
    public PtrArray1<int8_t> pLumaQp;        /*mb luma_qp*/
    public PtrArray1<InlineArray2<int8_t>> pChromaQp;                                         /*mb chroma_qp*/
    public InlineArray1<PtrArray2<InlineArray16<InlineArray2<int16_t>>>> pMvd; //[LAYER_NUM_EXCHANGEABLE   MB_BLOCK4x4_NUM*]
    public PtrArray1<uint16_t> pCbfDc;
    public PtrArray1<InlineArray24<int8_t>> pNzc;
    public PtrArray1<InlineArray24<int8_t>> pNzcRs;
    public PtrArray1<InlineArray384<int16_t>> pScaledTCoeff; /*need be aligned*/
    public PtrArray1<InlineArray8<int8_t>> pIntraPredMode;  //0~3 top4x4 ; 4~6 left 4x4; 7 intra16x16
    public PtrArray1<InlineArray16<int8_t>> pIntra4x4FinalMode;
    public PtrArray1<uint8_t> pIntraNxNAvailFlag;
    public PtrArray1<int8_t> pChromaPredMode;
    public PtrArray1<int8_t> pCbp;
    public InlineArray1<PtrArray2<InlineArray4<uint8_t>>> pMotionPredFlag; // [MB_PARTITION_SIZE] 8x8
    public PtrArray1<InlineArray4<uint32_t>> pSubMbType;
    public PtrArray1<int32_t> pSliceIdc;         // using int32_t for slice_idc
    public PtrArray1<int8_t> pResidualPredFlag;
    public PtrArray1<int8_t> pInterPredictionDoneFlag;
    public PtrArray1<bool> pMbCorrectlyDecodedFlag;
    public PtrArray1<bool> pMbRefConcealedFlag;
    public uint32_t iMbWidth;
    public uint32_t iMbHeight;
}

/*
 *  SWelsDecoderContext: to maintail all modules data over decoder@framework
 */

/// <summary>TagWelsDecoderContext (PWelsDecoderContext is SWelsDecoderContext*).</summary>
internal unsafe struct SWelsDecoderContext
{
    public SLogContext sLogCtx;
    // Input
    public void* pArgDec;                        // structured arguments for decoder, reserved here for extension in the future

    public SDataBuffer sRawData;
    public SDataBuffer sSavedData; //for parse only purpose

    // Configuration
    public SDecodingParam* pParam;
    public uint32_t uiCpuFlag;                      // CPU compatibility detected

    public VIDEO_BITSTREAM_TYPE eVideoType; //indicate the type of video to decide whether or not to do qp_delta error detection.
    public bool bHaveGotMemory; // global memory for decoder context related ever requested?

    public int32_t iImgWidthInPixel;       // width of image in pixel reconstruction picture to be output
    public int32_t iImgHeightInPixel;// height of image in pixel reconstruction picture to be output
    public int32_t iLastImgWidthInPixel;   // width of image in last successful pixel reconstruction picture to be output
    public int32_t iLastImgHeightInPixel;// height of image in last successful pixel reconstruction picture to be output
    public bool bFreezeOutput; // indicating current frame freezing. Default: true


    // Derived common elements
    public SNalUnitHeader sCurNalHead;
    public EWelsSliceType eSliceType;                     // Slice type
    public bool bUsedAsRef;                     //flag as ref
    public int32_t iFrameNum;
    public int32_t iErrorCode;                     // error code return while decoding in case packets lost
    public InlineArray256<SFmo> sFmoList;        // [MAX_PPS_COUNT] list for FMO storage
    public SFmo* pFmo;                           // current fmo context after parsed slice_header
    public int32_t iActiveFmoNum;          // active count number of fmo context in list

    /*needed info by decode slice level and mb level*/
    public fixed int iDecBlockOffsetArray[24];     // address talbe for sub 4x4 block in intra4x4_mb, so no need to caculta the address every time.

    public SWelsDecoderContextMb sMb;


    // reconstruction picture
    public SPicture* pDec;                   //pointer to current picture being reconstructed

    public SPicture* pTempDec;               //pointer to temp decoder picture to be used only for Bi Prediction.

    // reference pictures
    public SRefPic sRefPic;
    public SRefPic sTmpRefPic; //used to temporarily save RefPic for next active thread
    public SVlcTable* pVlcTable;               // vlc table

    public SBitStringAux sBs;
    public int32_t iMaxBsBufferSizeInByte; //actual memory size for BS buffer

    /* Global memory external */
    public SWelsDecoderSpsPpsCTX sSpsPpsCtx;
    public bool bHasNewSps;

    public SPosOffset sFrameCrop;

    public SSliceHeader* pSliceHeader;

    public SPicBuff* pPicBuff;       // Initially allocated memory for pictures which are used in decoding.
    public int32_t iPicQueueNumber;

    public SAccessUnit* pAccessUnitList;        // current access unit list to be performed
    //PSps                          pActiveLayerSps[MAX_LAYER_NUM];
    public SSps* pSps;   // used by current AU
    public SPps* pPps;   // used by current AU
    // Memory for pAccessUnitList is dynamically held till decoder destruction.
    public SDqLayer* pCurDqLayer;            // current DQ layer representation, also carry reference base layer if applicable
    public PtrArray1<SDqLayer> pDqLayersList;  // [LAYER_NUM_EXCHANGEABLE] DQ layers list with memory allocated
    public SNalUnit* pNalCur;          // point to current NAL Nnit
    public uint8_t uiNalRefIdc;      // NalRefIdc for easy access;
    public int32_t iPicWidthReq;             // picture width have requested the memory
    public int32_t iPicHeightReq;            // picture height have requested the memory

    public uint8_t uiTargetDqId;           // maximal DQ ID in current access unit, meaning target layer ID
    //bool                          bAvcBasedFlag;          // For decoding bitstream:
    public bool bEndOfStreamFlag;       // Flag on end of stream requested by external application layer
    public bool bInstantDecFlag;        // Flag for no-delay decoding
    public bool bInitialDqLayersMem;    // dq layers related memory is available?

    public bool bOnlyOneLayerInCurAuFlag; //only one layer in current AU: 1

    public bool bReferenceLostAtT0Flag;
    public int32_t iTotalNumMbRec; //record current number of decoded MB
    // LONG_TERM_REF
    public bool bParamSetsLostFlag;     //sps or pps do not exist or not correct

    public bool bCurAuContainLtrMarkSeFlag; //current AU has the LTR marking syntax element, mark the previous frame or self
    public int32_t iFrameNumOfAuMarkedLtr; //if bCurAuContainLtrMarkSeFlag==true, SHOULD set this variable

    public uint16_t uiCurIdrPicId;
    // end LONG_TERM_REF
    public bool bNewSeqBegin;
    public bool bNextNewSeqBegin;
    public int32_t* pStreamSeqNum;
    public int32_t iSeqNum;

    //for Parse only
    public bool bFramePending;
    public bool bFrameFinish;
    public int32_t iNalNum;
    public int32_t iMaxNalNum; //permitted max NAL num stored in parser
    public InlineArray32<SSpsBsInfo> sSpsBsInfo;        // [MAX_SPS_COUNT]
    public InlineArray256<SSpsBsInfo> sSubsetSpsBsInfo; // [MAX_PPS_COUNT]
    public InlineArray256<SPpsBsInfo> sPpsBsInfo;       // [MAX_PPS_COUNT]
    public SParserBsInfo* pParserBsInfo;

#pragma warning disable CS0649 // written through the function-pointer array properties below
    //PPicture pPreviousDecodedPictureInDpb; //pointer to previously decoded picture in DPB for error concealment
    private InlineArray7<nint> _pGetI16x16LumaPredFunc;          //h264_predict_copy_16x16;
    private InlineArray14<nint> _pGetI4x4LumaPredFunc;           // h264_predict_4x4_t
    private InlineArray7<nint> _pGetIChromaPredFunc;             // h264_predict_8x8_t
    public PIdctResAddPredFunc pIdctResAddPredFunc;
    public PIdctFourResAddPredFunc pIdctFourResAddPredFunc;
    public SMcFunc sMcFunc;
    //Transform8x8
    private InlineArray14<nint> _pGetI8x8LumaPredFunc;
#pragma warning restore CS0649
    public PIdctResAddPredFunc pIdctResAddPredFunc8x8;

    //For error concealment
    public SCopyFunc sCopyFunc;
    /* For Deblocking */
    public SDeblockingFunc sDeblockingFunc;
    public SExpandPicFunc sExpandPicFunc;

    /* For Block */
    public SBlockFunc sBlockFunc;

    public int32_t iCurSeqIntervalTargetDependId;
    public int32_t iCurSeqIntervalMaxPicWidth;
    public int32_t iCurSeqIntervalMaxPicHeight;

    public PWelsFillNeighborMbInfoIntra4x4Func pFillInfoCacheIntraNxNFunc;
    public PWelsMapNeighToSample pMapNxNNeighToSampleFunc;
    public PWelsMap16NeighToSample pMap16x16NeighToSampleFunc;

    //feedback whether or not have VCL in current AU, and the temporal ID
    public int32_t iFeedbackVclNalInAu;
    public int32_t iFeedbackTidInAu;
    public int32_t iFeedbackNalRefIdc;

    public bool bAuReadyFlag;   // true: one au is ready for decoding; false: default value

    public bool bPrintFrameErrorTraceFlag; //true: can print info for upper layer
    public int32_t iIgnoredErrorInfoPacketCount; //store the packet number with error decoding info
    //trace handle
    public void* pTraceHandle;

    public SWelsLastDecPicInfo* pLastDecPicInfo;

    public InlineArray4<InlineArray52<InlineArray460<SWelsCabacCtx>>> sWelsCabacContexts; // [4][WELS_QP_MAX + 1][WELS_CONTEXT_COUNT]
    public bool bCabacInited;
    public InlineArray460<SWelsCabacCtx> pCabacCtx; // [WELS_CONTEXT_COUNT]
    public SWelsCabacDecEngine* pCabacDecEngine;
    public double dDecTime;
    public SDecoderStatistics* pDecoderStatistics; // For real time debugging
    public int32_t iMbEcedNum;
    public int32_t iMbEcedPropNum;
    public int32_t iMbNum;
    public bool bMbRefConcealed;
    public bool bRPLRError;
    public InlineArray16<InlineArray2<int32_t>> iECMVs;
    public PtrArray16<SPicture> pECRefPic;
    public ulong uiTimeStamp;
    public uint32_t uiDecodingTimeStamp; //represent relative decoding time stamps
    // To support scaling list HP
    public InlineArray6<InlineArray52<InlineArray16<uint16_t>>> pDequant_coeff_buffer4x4;
    public InlineArray6<InlineArray52<InlineArray64<uint16_t>>> pDequant_coeff_buffer8x8;
    public PtrArray6<InlineArray16<uint16_t>> pDequant_coeff4x4;// 4x4 sclaing list value pointer
    public PtrArray6<InlineArray64<uint16_t>> pDequant_coeff8x8;//64 residual coeff ,with 6 kinds of residual type, 52 qp level
    public int iDequantCoeffPpsid;//When a new pps actived, reinitialised the scaling list value
    public bool bDequantCoeff4x4Init;
    public bool bUseScalingList;
    public CMemoryAlign* pMemAlign;
    public void* pThreadCtx;
    public void* pLastThreadCtx;
    public void* pCsDecoder;  // WELS_MUTEX*
    public InlineArray2<InlineArray16<int16_t>> lastReadyHeightOffset; // [LIST_A][MAX_REF_PIC_COUNT] last ready reference MB offset
    public SPictInfo* pPictInfoList;
    public SPictReoderingStatus* pPictReoderingStatus;

    /// <summary>PGetIntraPredFunc pGetI16x16LumaPredFunc[7].</summary>
    public PGetIntraPredFunc* pGetI16x16LumaPredFunc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PGetIntraPredFunc*)Unsafe.AsPointer(ref _pGetI16x16LumaPredFunc);
    }

    /// <summary>PGetIntraPredFunc pGetI4x4LumaPredFunc[14].</summary>
    public PGetIntraPredFunc* pGetI4x4LumaPredFunc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PGetIntraPredFunc*)Unsafe.AsPointer(ref _pGetI4x4LumaPredFunc);
    }

    /// <summary>PGetIntraPredFunc pGetIChromaPredFunc[7].</summary>
    public PGetIntraPredFunc* pGetIChromaPredFunc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PGetIntraPredFunc*)Unsafe.AsPointer(ref _pGetIChromaPredFunc);
    }

    /// <summary>PGetIntraPred8x8Func pGetI8x8LumaPredFunc[14].</summary>
    public PGetIntraPred8x8Func* pGetI8x8LumaPredFunc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PGetIntraPred8x8Func*)Unsafe.AsPointer(ref _pGetI8x8LumaPredFunc);
    }
}

/// <summary>tagSWelsDecThread (PWelsDecThreadInfo is SWelsDecThreadInfo*).</summary>
internal unsafe struct SWelsDecThreadInfo
{
    public SWelsDecSemphore* sIsBusy;
    public SWelsDecSemphore sIsActivated;
    public SWelsDecSemphore sIsIdle;
    public SWelsDecThread sThrHandle;
    public uint32_t uiCommand;
    public uint32_t uiThrNum;
    public uint32_t uiThrMaxNum;
    public uint32_t uiThrStackSize;
    public void* pThrProcMain; // DECLARE_PROCTHREAD_PTR (pThrProcMain)
}

/// <summary>tagSWelsDecThreadCtx (PWelsDecoderThreadCTX is SWelsDecoderThreadCTX*).</summary>
internal unsafe struct SWelsDecoderThreadCTX
{
    public SWelsDecThreadInfo sThreadInfo;
    public SWelsDecoderContext* pCtx;
    public void* threadCtxOwner;
    public uint8_t* kpSrc;
    public int32_t kiSrcLen;
    public uint8_t** ppDst;
    public SBufferInfo sDstInfo;
    public SPicture* pDec;
    public SWelsDecEvent sImageReady;
    public SWelsDecEvent sSliceDecodeStart;
    public SWelsDecEvent sSliceDecodeFinish;
    public int32_t iPicBuffIdx; //picBuff Index
    // Per-thread snapshot of pPreviousDecodedPictureInDpb captured before sSliceDecodeFinish
    // is signaled. Prevents concurrent workers from overwriting the shared pLastDecPicInfo
    // field before BufferingReadyPicture() reads it.
    public SPicture* pPreviousDecodedPictureInDpb;
}

internal static unsafe partial class WelsDec
{
    public const int MAX_PRED_MODE_ID_I16x16 = 3;
    public const int MAX_PRED_MODE_ID_CHROMA = 3;
    public const int MAX_PRED_MODE_ID_I4x4 = 8;
    public const int WELS_QP_MAX = 51;

    public const int IMinInt32 = -0x7FFFFFFF;

    public const int NEW_CTX_OFFSET_MB_TYPE_I = 3;
    public const int NEW_CTX_OFFSET_SKIP = 11;
    public const int NEW_CTX_OFFSET_SUBMB_TYPE = 21;
    public const int NEW_CTX_OFFSET_B_SUBMB_TYPE = 36;
    public const int NEW_CTX_OFFSET_MVD = 40;
    public const int NEW_CTX_OFFSET_REF_NO = 54;
    public const int NEW_CTX_OFFSET_DELTA_QP = 60;
    public const int NEW_CTX_OFFSET_IPR = 68;
    public const int NEW_CTX_OFFSET_CIPR = 64;
    public const int NEW_CTX_OFFSET_CBP = 73;
    public const int NEW_CTX_OFFSET_CBF = 85;
    public const int NEW_CTX_OFFSET_MAP = 105;
    public const int NEW_CTX_OFFSET_LAST = 166;
    public const int NEW_CTX_OFFSET_ONE = 227;
    public const int NEW_CTX_OFFSET_ABS = 232;
    public const int NEW_CTX_OFFSET_TS_8x8_FLAG = 399;
    public const int CTX_NUM_MVD = 7;
    public const int CTX_NUM_CBP = 4;
    // Table 9-34 in Page 270
    public const int NEW_CTX_OFFSET_TRANSFORM_SIZE_8X8_FLAG = 399;
    public const int NEW_CTX_OFFSET_MAP_8x8 = 402;
    public const int NEW_CTX_OFFSET_LAST_8x8 = 417;
    public const int NEW_CTX_OFFSET_ONE_8x8 = 426;
    public const int NEW_CTX_OFFSET_ABS_8x8 = 431; // Puzzle, where is the definition?

    //limit size for SPS PPS total permitted size for parse_only
    public const int SPS_PPS_BS_SIZE = 128;

    public const int OVERWRITE_NONE = 0;
    public const int OVERWRITE_PPS = 1;
    public const int OVERWRITE_SPS = 1 << 1;
    public const int OVERWRITE_SUBSETSPS = 1 << 2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ResetActiveSPSForEachLayer(SWelsDecoderContext* pCtx)
    {
        if (pCtx->iTotalNumMbRec == 0)
        {
            for (int i = 0; i < MAX_LAYER_NUM; i++)
            {
                pCtx->sSpsPpsCtx.pActiveLayerSps[i] = null;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t GetThreadCount(SWelsDecoderContext* pCtx)
    {
        int32_t iThreadCount = 0;
        if (pCtx->pThreadCtx != null)
        {
            SWelsDecoderThreadCTX* pThreadCtx = (SWelsDecoderThreadCTX*)pCtx->pThreadCtx;
            iThreadCount = (int32_t)pThreadCtx->sThreadInfo.uiThrMaxNum;
        }
        return iThreadCount;
    }

    //GetPrevFrameNum only applies when thread count >= 2
    public static int32_t GetPrevFrameNum(SWelsDecoderContext* pCtx)
    {
        if (pCtx->uiDecodingTimeStamp > 0)
        {
            SWelsDecoderThreadCTX* pThreadCtx = (SWelsDecoderThreadCTX*)pCtx->pThreadCtx;
            int32_t iThreadCount = (int32_t)(pThreadCtx->sThreadInfo.uiThrMaxNum);
            int32_t uiThrNum = (int32_t)(pThreadCtx->sThreadInfo.uiThrNum);
            for (int32_t i = 0; i < iThreadCount; ++i)
            {
                int32_t id = i - uiThrNum;
                if (id != 0 && pThreadCtx[id].pCtx->uiDecodingTimeStamp == pCtx->uiDecodingTimeStamp - 1)
                {
                    if (pThreadCtx[id].pCtx->pDec != null)
                    {
                        int32_t iFrameNum = pThreadCtx[id].pCtx->pDec->iFrameNum;
                        if (iFrameNum >= 0) return iFrameNum;
                    }
                    return pThreadCtx[id].pCtx->iFrameNum;
                }
            }
        }
        return pCtx->pLastDecPicInfo->iPrevFrameNum;
    }
}
