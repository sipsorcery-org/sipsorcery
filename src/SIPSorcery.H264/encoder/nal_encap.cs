//-----------------------------------------------------------------------------
// Filename: nal_encap.cs
//
// Description: Port of codec/encoder/core/inc/nal_encap.h and
// codec/encoder/core/src/nal_encap.cpp: the raw NAL list the encoder writes
// into, and NAL encapsulation (start code, header, emulation prevention).
// OpenH264 commit 78783f19 (v2.7.0-1).
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

/*
 *  Raw payload pData for NAL unit, AVC/SVC compatible
 */
/// <summary>TagWelsNalRaw.</summary>
internal unsafe struct SWelsNalRaw
{
    public uint8_t* pRawData;       // pRawNal payload for slice pData
    public int32_t iPayloadSize;    // size of pRawNal pData

    public SNalUnitHeaderExt sNalExt; // NAL header information

    public int32_t iStartPos; //NAL start position in buffer
}

/*
 *  Encoder majoy output pData
 */
/// <summary>TagWelsEncoderOutput.</summary>
internal unsafe struct SWelsEncoderOutput
{
    public uint8_t* pBsBuffer;      // overall bitstream pBuffer allocation for a coded picture, recycling use intend.
    public uint32_t uiSize;         // size of allocation pBuffer above

    public SBitStringAux sBsWrite;

    // SWelsNalRaw             raw_nals[MAX_DEPENDENCY_LAYER*2+MAX_DEPENDENCY_LAYER*MAX_QUALITY_LEVEL]; // AVC: max up to SPS+PPS+max_slice_idc (2 + 8) for FMO;
    public SWelsNalRaw* sNalList;   // nal list, adaptive for AVC/SVC in case single slice, multiple slices or fmo
    public int32_t* pNalLen;
    public int32_t iCountNals;      // count number of NAL in list
    // SVC: num_sps (MAX_D) + num_pps (MAX_D) + num_vcl (MAX_D * MAX_Q)
    public int32_t iNalIndex;       // coding NAL currently, 0 based
    public int32_t iLayerBsIndex;   // layer index of  bit stream for SFrameBsIfo
    // bool            bAnnexBFlag;            // annexeb flag, to figure it pOut the packetization mode whether need 4 bytes (0 0 0 1) of start code prefix
}

//#define MT_DEBUG_BS_WR        0       // for MT debugging if needed

/// <summary>TagWelsSliceBs.</summary>
internal unsafe struct SWelsSliceBs
{
    public uint8_t* pBs;            // output bitstream, pBitStringAux not needed for slice 0 due to no dependency of pFrameBs available
    public uint32_t uiBsSize;       // size of pBs buffer
    public uint32_t uiBsPos;        // position of output bitstream
    public uint8_t* pBsBuffer;      // overall bitstream pBuffer allocation for a coded slice, recycling use intend.
    public uint32_t uiSize;         // size of allocation pBuffer above

    public SBitStringAux sBsWrite;

    public InlineArray2<SWelsNalRaw> sNalList; // nal list, PREFIX NAL(if applicable) + SLICE NAL
    // int32_t         iCountNals;             // count number of NAL in list
    public fixed int iNalLen[2];
    public int32_t iNalIndex;       // coding NAL currently, 0 based

    // bool            bAnnexBFlag;            // annexeb flag, to figure it pOut the packetization mode whether need 4 bytes (0 0 0 1) of start code prefix
}

internal static unsafe partial class WelsEnc
{
    public const int NAL_HEADER_SIZE = 4;

    /*!
     * \brief   load an initialize NAL pRawNal pData
     */
    public static void WelsLoadNal(SWelsEncoderOutput* pEncoderOuput, int32_t/*EWelsNalUnitType*/ kiType,
                                   int32_t/*EWelsNalRefIdc*/ kiNalRefIdc)
    {
        SWelsEncoderOutput* pWelsEncoderOuput = pEncoderOuput;
        SWelsNalRaw* pRawNal = &pWelsEncoderOuput->sNalList[pWelsEncoderOuput->iNalIndex];
        SNalUnitHeader* sNalUnitHeader = &pRawNal->sNalExt.sNalUnitHeader;
        int32_t kiStartPos = (BsGetBitsPos(&pWelsEncoderOuput->sBsWrite) >> 3);

        sNalUnitHeader->eNalUnitType = (EWelsNalUnitType)kiType;
        sNalUnitHeader->uiNalRefIdc = (uint8_t)kiNalRefIdc;
        sNalUnitHeader->uiForbiddenZeroBit = 0;

        pRawNal->pRawData = &pWelsEncoderOuput->pBsBuffer[kiStartPos];
        pRawNal->iStartPos = kiStartPos;
        pRawNal->iPayloadSize = 0;
    }

    /*!
     * \brief   unload pRawNal NAL
     */
    public static void WelsUnloadNal(SWelsEncoderOutput* pEncoderOuput)
    {
        SWelsEncoderOutput* pWelsEncoderOuput = pEncoderOuput;
        int32_t* pIdx = &pWelsEncoderOuput->iNalIndex;
        SWelsNalRaw* pRawNal = &pWelsEncoderOuput->sNalList[*pIdx];
        int32_t kiEndPos = (BsGetBitsPos(&pWelsEncoderOuput->sBsWrite) >> 3);

        /* count payload size of pRawNal NAL */
        pRawNal->iPayloadSize = kiEndPos - pRawNal->iStartPos;

        ++(*pIdx);
    }

    /*!
     * \brief   load an initialize NAL pRawNal pData
     */
    public static void WelsLoadNalForSlice(SWelsSliceBs* pSliceBs, int32_t/*EWelsNalUnitType*/ kiType,
                                           int32_t/*EWelsNalRefIdc*/ kiNalRefIdc)
    {
        SWelsNalRaw* pRawNal = (SWelsNalRaw*)&pSliceBs->sNalList + pSliceBs->iNalIndex;
        SNalUnitHeader* sNalUnitHeader = &pRawNal->sNalExt.sNalUnitHeader;
        SBitStringAux* pBitStringAux = &pSliceBs->sBsWrite;
        int32_t kiStartPos = (BsGetBitsPos(pBitStringAux) >> 3);

        sNalUnitHeader->eNalUnitType = (EWelsNalUnitType)kiType;
        sNalUnitHeader->uiNalRefIdc = (uint8_t)kiNalRefIdc;
        sNalUnitHeader->uiForbiddenZeroBit = 0;

        pRawNal->pRawData = &pSliceBs->pBsBuffer[kiStartPos];
        pRawNal->iStartPos = kiStartPos;
        pRawNal->iPayloadSize = 0;
    }

    /*!
     * \brief   unload pRawNal NAL
     */
    public static void WelsUnloadNalForSlice(SWelsSliceBs* pSliceBs)
    {
        int32_t* pIdx = &pSliceBs->iNalIndex;
        SWelsNalRaw* pRawNal = (SWelsNalRaw*)&pSliceBs->sNalList + *pIdx;
        SBitStringAux* pBitStringAux = &pSliceBs->sBsWrite;
        int32_t kiEndPos = (BsGetBitsPos(pBitStringAux) >> 3);

        /* count payload size of pRawNal NAL */
        pRawNal->iPayloadSize = kiEndPos - pRawNal->iStartPos;
        ++(*pIdx);
    }

    /*!
     * \brief   encode NAL with emulation forbidden three bytes checking
     * \param   pDst        pDst NAL pData
     * \param   pDstLen     length of pDst NAL output
     * \param   annexeb     annexeb flag
     * \param   pRawNal     pRawNal NAL pData
     * \return  ERRCODE
     */
    //TODO 1: refactor the calling of this func in multi-thread
    //TODO 2: complete the realloc&copy
    public static int32_t WelsEncodeNal(SWelsNalRaw* pRawNal, void* pNalHeaderExt, int32_t kiDstBufferLen, void* pDst,
                                        int32_t* pDstLen)
    {
        bool kbNALExt = pRawNal->sNalExt.sNalUnitHeader.eNalUnitType == NAL_UNIT_PREFIX
                        || pRawNal->sNalExt.sNalUnitHeader.eNalUnitType == NAL_UNIT_CODED_SLICE_EXT;
        int32_t iAssumedNeededLength = NAL_HEADER_SIZE + (kbNALExt ? 3 : 0) + pRawNal->iPayloadSize + 1;
        if (iAssumedNeededLength <= 0)
        {
            return ENC_RETURN_UNEXPECTED;
        }

        //since for each 0x000 need a 0x03, so the needed length will not exceed (iAssumeNeedLenth + iAssumeNeedLength/3), here adjust to >>1 to omit division
        if (kiDstBufferLen < (iAssumedNeededLength + (iAssumedNeededLength >> 1)))
        {
            return ENC_RETURN_MEMALLOCERR;
            //TODO: call the realloc&copy instead
        }
        uint8_t* pDstStart = (uint8_t*)pDst;
        uint8_t* pDstPointer = pDstStart;
        uint8_t* pSrcPointer = pRawNal->pRawData;
        uint8_t* pSrcEnd = pRawNal->pRawData + pRawNal->iPayloadSize;
        int32_t iZeroCount = 0;
        int32_t iNalLength = 0;
        *pDstLen = 0;

        // static const uint8_t kuiStartCodePrefix[NAL_HEADER_SIZE] = { 0, 0, 0, 1 };
        pDstPointer[0] = 0;
        pDstPointer[1] = 0;
        pDstPointer[2] = 0;
        pDstPointer[3] = 1;
        pDstPointer += 4;

        /* NAL Unit Header */
        *pDstPointer++ = (uint8_t)((pRawNal->sNalExt.sNalUnitHeader.uiNalRefIdc << 5) | ((int)pRawNal->sNalExt.sNalUnitHeader.eNalUnitType &
                         0x1f));

        if (kbNALExt)
        {
            SNalUnitHeaderExt* sNalExt = (SNalUnitHeaderExt*)pNalHeaderExt;

            /* NAL UNIT Extension Header */
            *pDstPointer++ = (uint8_t)((0x80) |
                             ((sNalExt->bIdrFlag ? 1 : 0) << 6));

            *pDstPointer++ = (uint8_t)((0x80) |
                             (sNalExt->uiDependencyId << 4));

            *pDstPointer++ = (uint8_t)((sNalExt->uiTemporalId << 5) |
                             ((sNalExt->bDiscardableFlag ? 1 : 0) << 3) |
                             (0x07));
        }

        while (pSrcPointer < pSrcEnd)
        {
            if (iZeroCount == 2 && *pSrcPointer <= 3)
            {
                //add the code 03
                *pDstPointer++ = 3;
                iZeroCount = 0;
            }
            if (*pSrcPointer == 0)
            {
                ++iZeroCount;
            }
            else
            {
                iZeroCount = 0;
            }
            *pDstPointer++ = *pSrcPointer++;
        }

        /* count length of NAL Unit */
        iNalLength = (int32_t)(pDstPointer - pDstStart);
        if (null != pDstLen)
            *pDstLen = iNalLength;

        return ENC_RETURN_SUCCESS;
    }

    /*!
     * \brief   write prefix nal
     */
    public static int32_t WelsWriteSVCPrefixNal(SBitStringAux* pBitStringAux, int32_t kiNalRefIdc,
                                                bool kbIdrFlag)
    {
        if (0 < kiNalRefIdc)
        {
            BsWriteOneBit(pBitStringAux, 0/*bStoreRefBasePicFlag*/);
            BsWriteOneBit(pBitStringAux, 0);
            BsRbspTrailingBits(pBitStringAux);
        }
        return 0;
    }
}
