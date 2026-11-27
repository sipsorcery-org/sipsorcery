//-----------------------------------------------------------------------------
// Filename: au_parser.cs
//
// Description: Port of codec/decoder/core/inc/au_parser.h and
// src/au_parser.cpp: start code detection, NAL unit header parsing, access unit
// boundary detection, and the SPS / subset SPS / PPS / VUI / scaling list
// parsers.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Upstream builds with _PARSE_NALHRD_VCLHRD_PARAMS_ defined, so ParseVui reads
// (and discards) HRD parameters; as upstream, cpb_cnt_minus1 is assigned
// BsGetUe's return code rather than the value read, so the loop runs once.
// ParseSps returns false (0, ERR_NONE) for an unsupported profile_idc, as
// upstream does.
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
 * \file    au_parser.c
 *
 * \brief   Interfaces introduced in Access Unit level based parser
 *
 * \date    03/10/2009 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    /*!
     *************************************************************************************
     * \brief   Start Code Prefix (0x 00 00 00 01) detection
     *
     * \param   pBuf        bitstream payload buffer
     * \param   pOffset     offset between NAL rbsp and original bitsteam that
     *                      start code prefix is seperated from.
     * \param   iBufSize    count size of buffer
     *
     * \return  RBSP buffer of start code prefix exclusive
     *
     * \note    N/A
     *************************************************************************************
     */
    public static uint8_t* DetectStartCodePrefix(uint8_t* kpBuf, int32_t* pOffset, int32_t iBufSize)
    {
        uint8_t* pBits = (uint8_t*)kpBuf;

        do
        {
            int32_t iIdx = 0;
            while ((iIdx < iBufSize) && ((*pBits) == 0))
            {
                ++pBits;
                ++iIdx;
            }
            if (iIdx >= iBufSize) break;

            ++iIdx;
            ++pBits;

            if ((iIdx >= 3) && ((*(pBits - 1)) == 0x1))
            {
                *pOffset = (int32_t)(((uintptr_t)pBits) - ((uintptr_t)kpBuf));
                return pBits;
            }

            iBufSize -= iIdx;
        } while (true);

        return null;
    }

    /*!
     *************************************************************************************
     * \brief   to parse nal unit
     *
     * \param   pCtx            decoder context
     * \param   pNalUnitHeader  parsed result of NAL Unit Header to output
     * \param   pSrcRbsp        bitstream buffer to input
     * \param   iSrcRbspLen     length size of bitstream buffer payload
     * \param   pSrcNal
     * \param   iSrcNalLen
     * \param   pConsumedBytes  consumed bytes during parsing
     *
     * \return  decoded bytes payload, might be (pSrcRbsp+1) if no escapes
     *
     * \note    N/A
     *************************************************************************************
     */
    public static uint8_t* ParseNalHeader(SWelsDecoderContext* pCtx, SNalUnitHeader* pNalUnitHeader, uint8_t* pSrcRbsp,
                                          int32_t iSrcRbspLen, uint8_t* pSrcNal, int32_t iSrcNalLen, int32_t* pConsumedBytes)
    {
        SNalUnit* pCurNal = null;
        uint8_t* pNal = pSrcRbsp;
        int32_t iNalSize = iSrcRbspLen;
        SBitStringAux* pBs = null;
        bool bExtensionFlag = false;
        int32_t iErr = ERR_NONE;
        int32_t iBitSize = 0;
        SDataBuffer* pSavedData = &pCtx->sSavedData;
        SLogContext* pLogCtx = &(pCtx->sLogCtx);
        pNalUnitHeader->eNalUnitType = NAL_UNIT_UNSPEC_0;//SHOULD init it. because pCtx->sCurNalHead is common variable.

        //remove the consecutive ZERO at the end of current NAL in the reverse order.--2011.6.1
        {
            int32_t iIndex = iSrcRbspLen - 1;
            uint8_t uiBsZero = 0;
            while (iIndex >= 0)
            {
                uiBsZero = pSrcRbsp[iIndex];
                if (0 == uiBsZero)
                {
                    --iNalSize;
                    ++(*pConsumedBytes);
                    --iIndex;
                }
                else
                {
                    break;
                }
            }
        }

        pNalUnitHeader->uiForbiddenZeroBit = (uint8_t)(pNal[0] >> 7); // uiForbiddenZeroBit
        if (pNalUnitHeader->uiForbiddenZeroBit != 0)
        { //2010.4.14
            pCtx->iErrorCode |= (int32_t)dsBitstreamError;
            return null; //uiForbiddenZeroBit should always equal to 0
        }

        pNalUnitHeader->uiNalRefIdc = (uint8_t)(pNal[0] >> 5);             // uiNalRefIdc
        pNalUnitHeader->eNalUnitType = (EWelsNalUnitType)(pNal[0] & 0x1f);  // eNalUnitType

        ++pNal;
        --iNalSize;
        ++(*pConsumedBytes);

        if (!(IS_SEI_NAL(pNalUnitHeader->eNalUnitType) || IS_SPS_NAL(pNalUnitHeader->eNalUnitType)
               || IS_AU_DELIMITER_NAL(pNalUnitHeader->eNalUnitType) || pCtx->sSpsPpsCtx.bSpsExistAheadFlag))
        {
            if (pCtx->bPrintFrameErrorTraceFlag && pCtx->sSpsPpsCtx.iSpsErrorIgnored == 0)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"parse_nal(), no exist Sequence Parameter Sets ahead of sequence when try to decode NAL(type:{(int32_t)pNalUnitHeader->eNalUnitType}).");
            }
            else
            {
                pCtx->sSpsPpsCtx.iSpsErrorIgnored++;
            }
            pCtx->pDecoderStatistics->iSpsNoExistNalNum++;
            pCtx->iErrorCode = (int32_t)dsNoParamSets;
            return null;
        }
        pCtx->sSpsPpsCtx.iSpsErrorIgnored = 0;
        if (!(IS_SEI_NAL(pNalUnitHeader->eNalUnitType) || IS_PARAM_SETS_NALS(pNalUnitHeader->eNalUnitType)
               || IS_AU_DELIMITER_NAL(pNalUnitHeader->eNalUnitType) || pCtx->sSpsPpsCtx.bPpsExistAheadFlag))
        {
            if (pCtx->bPrintFrameErrorTraceFlag && pCtx->sSpsPpsCtx.iPpsErrorIgnored == 0)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"parse_nal(), no exist Picture Parameter Sets ahead of sequence when try to decode NAL(type:{(int32_t)pNalUnitHeader->eNalUnitType}).");
            }
            else
            {
                pCtx->sSpsPpsCtx.iPpsErrorIgnored++;
            }
            pCtx->pDecoderStatistics->iPpsNoExistNalNum++;
            pCtx->iErrorCode = (int32_t)dsNoParamSets;
            return null;
        }
        pCtx->sSpsPpsCtx.iPpsErrorIgnored = 0;
        if ((IS_VCL_NAL_AVC_BASE(pNalUnitHeader->eNalUnitType) && !(pCtx->sSpsPpsCtx.bSpsExistAheadFlag
             || pCtx->sSpsPpsCtx.bPpsExistAheadFlag)) ||
            (IS_NEW_INTRODUCED_SVC_NAL(pNalUnitHeader->eNalUnitType) && !(pCtx->sSpsPpsCtx.bSpsExistAheadFlag
                || pCtx->sSpsPpsCtx.bSubspsExistAheadFlag
                || pCtx->sSpsPpsCtx.bPpsExistAheadFlag)))
        {
            if (pCtx->bPrintFrameErrorTraceFlag && pCtx->sSpsPpsCtx.iSubSpsErrorIgnored == 0)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"ParseNalHeader(), no exist Parameter Sets ahead of sequence when try to decode slice(type:{(int32_t)pNalUnitHeader->eNalUnitType}).");
            }
            else
            {
                pCtx->sSpsPpsCtx.iSubSpsErrorIgnored++;
            }
            pCtx->pDecoderStatistics->iSubSpsNoExistNalNum++;
            pCtx->iErrorCode |= (int32_t)dsNoParamSets;
            return null;
        }
        pCtx->sSpsPpsCtx.iSubSpsErrorIgnored = 0;

        switch (pNalUnitHeader->eNalUnitType)
        {
            case NAL_UNIT_AU_DELIMITER:
            case NAL_UNIT_SEI:
                if (pCtx->pAccessUnitList->uiAvailUnitsNum > 0)
                {
                    pCtx->pAccessUnitList->uiEndPos = pCtx->pAccessUnitList->uiAvailUnitsNum - 1;
                    pCtx->bAuReadyFlag = true;
                }
                break;

            case NAL_UNIT_PREFIX:
                pCurNal = &pCtx->sSpsPpsCtx.sPrefixNal;
                pCurNal->uiTimeStamp = pCtx->uiTimeStamp;

                if (iNalSize < NAL_UNIT_HEADER_EXT_SIZE)
                {
                    SAccessUnit* pCurAu = pCtx->pAccessUnitList;
                    uint32_t uiAvailNalNum = pCurAu->uiAvailUnitsNum;

                    if (uiAvailNalNum > 0)
                    {
                        pCurAu->uiEndPos = uiAvailNalNum - 1;
                        if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                        {
                            pCtx->bAuReadyFlag = true;
                        }
                    }
                    pCurNal->sNalData.sPrefixNal.bPrefixNalCorrectFlag = false;
                    pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                    return null;
                }

                DecodeNalHeaderExt(pCurNal, pNal);
                if ((pCurNal->sNalHeaderExt.uiQualityId != 0) || pCurNal->sNalHeaderExt.bUseRefBasePicFlag)
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING,
                            $"ParseNalHeader() in Prefix Nal Unit:uiQualityId ({pCurNal->sNalHeaderExt.uiQualityId}) != 0, bUseRefBasePicFlag ({(pCurNal->sNalHeaderExt.bUseRefBasePicFlag ? 1 : 0)}) != 0, not supported!");
                    SAccessUnit* pCurAu = pCtx->pAccessUnitList;
                    uint32_t uiAvailNalNum = pCurAu->uiAvailUnitsNum;

                    if (uiAvailNalNum > 0)
                    {
                        pCurAu->uiEndPos = uiAvailNalNum - 1;
                        if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                        {
                            pCtx->bAuReadyFlag = true;
                        }
                    }
                    pCurNal->sNalData.sPrefixNal.bPrefixNalCorrectFlag = false;
                    pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                    return null;
                }

                pNal += NAL_UNIT_HEADER_EXT_SIZE;
                iNalSize -= NAL_UNIT_HEADER_EXT_SIZE;
                *pConsumedBytes += NAL_UNIT_HEADER_EXT_SIZE;

                pCurNal->sNalHeaderExt.sNalUnitHeader.uiForbiddenZeroBit = pNalUnitHeader->uiForbiddenZeroBit;
                pCurNal->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc = pNalUnitHeader->uiNalRefIdc;
                pCurNal->sNalHeaderExt.sNalUnitHeader.eNalUnitType = pNalUnitHeader->eNalUnitType;
                if (pNalUnitHeader->uiNalRefIdc != 0)
                {
                    pBs = &pCtx->sBs;
                    iBitSize = (iNalSize << 3) - BsGetTrailingBits(pNal + iNalSize - 1); // convert into bit

                    iErr = DecInitBits(pBs, pNal, iBitSize);
                    if (iErr != 0)
                    {
                        WelsLog(pLogCtx, WELS_LOG_ERROR, "NAL_UNIT_PREFIX: DecInitBits() fail due invalid access.");
                        pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                        return null;
                    }
                    ParsePrefixNalUnit(pCtx, pBs);
                }
                pCurNal->sNalData.sPrefixNal.bPrefixNalCorrectFlag = true;

                break;
            case NAL_UNIT_CODED_SLICE_EXT:
                bExtensionFlag = true;
                goto case NAL_UNIT_CODED_SLICE;
            case NAL_UNIT_CODED_SLICE:
            case NAL_UNIT_CODED_SLICE_IDR:
                {
                    SAccessUnit* pCurAu = null;
                    uint32_t uiAvailNalNum;
                    pCurNal = MemGetNextNal(&pCtx->pAccessUnitList, pCtx->pMemAlign);
                    if (null == pCurNal)
                    {
                        WelsLog(pLogCtx, WELS_LOG_ERROR, "MemGetNextNal() fail due out of memory.");
                        pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
                        return null;
                    }
                    pCurNal->uiTimeStamp = pCtx->uiTimeStamp;
                    pCurNal->sNalHeaderExt.sNalUnitHeader.uiForbiddenZeroBit = pNalUnitHeader->uiForbiddenZeroBit;
                    pCurNal->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc = pNalUnitHeader->uiNalRefIdc;
                    pCurNal->sNalHeaderExt.sNalUnitHeader.eNalUnitType = pNalUnitHeader->eNalUnitType;
                    pCurAu = pCtx->pAccessUnitList;
                    uiAvailNalNum = pCurAu->uiAvailUnitsNum;


                    if (pNalUnitHeader->eNalUnitType == NAL_UNIT_CODED_SLICE_EXT)
                    {
                        if (iNalSize < NAL_UNIT_HEADER_EXT_SIZE)
                        {
                            ForceClearCurrentNal(pCurAu);

                            if (uiAvailNalNum > 1)
                            {
                                pCurAu->uiEndPos = uiAvailNalNum - 2;
                                if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                                {
                                    pCtx->bAuReadyFlag = true;
                                }
                            }
                            pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                            return null;
                        }

                        DecodeNalHeaderExt(pCurNal, pNal);
                        if (pCurNal->sNalHeaderExt.uiQualityId != 0 ||
                            pCurNal->sNalHeaderExt.bUseRefBasePicFlag)
                        {
                            if (pCurNal->sNalHeaderExt.uiQualityId != 0)
                                WelsLog(pLogCtx, WELS_LOG_WARNING, $"ParseNalHeader():uiQualityId ({pCurNal->sNalHeaderExt.uiQualityId}) != 0, MGS not supported!");
                            if (pCurNal->sNalHeaderExt.bUseRefBasePicFlag)
                                WelsLog(pLogCtx, WELS_LOG_WARNING, $"ParseNalHeader():bUseRefBasePicFlag ({(pCurNal->sNalHeaderExt.bUseRefBasePicFlag ? 1 : 0)}) != 0, MGS not supported!");

                            ForceClearCurrentNal(pCurAu);

                            if (uiAvailNalNum > 1)
                            {
                                pCurAu->uiEndPos = uiAvailNalNum - 2;
                                if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                                {
                                    pCtx->bAuReadyFlag = true;
                                }
                            }
                            pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                            return null;
                        }
                        pNal += NAL_UNIT_HEADER_EXT_SIZE;
                        iNalSize -= NAL_UNIT_HEADER_EXT_SIZE;
                        *pConsumedBytes += NAL_UNIT_HEADER_EXT_SIZE;

                        if (pCtx->pParam->bParseOnly)
                        {
                            int32_t iTrailingZeroByte = 0;
                            while (pSrcNal[iSrcNalLen - iTrailingZeroByte - 1] == 0x0) //remove final trailing 0 bytes
                                iTrailingZeroByte++;
                            int32_t iActualLen = iSrcNalLen - iTrailingZeroByte;
                            //unify start code as 0x0001
                            int32_t iCurrStartByte = 4; //4 for 0x0001, 3 for 0x001
                            if (pSrcNal[0] == 0x0 && pSrcNal[1] == 0x0 && pSrcNal[2] == 0x1)
                            { //if 0x001
                                iCurrStartByte = 3;
                            }
                            int32_t iOffset = iCurrStartByte + 1 + NAL_UNIT_HEADER_EXT_SIZE;
                            int32_t iWriteLen = 5 + (iActualLen - iOffset); // 4-byte start code + NAL type byte + payload
                            // Bounds check: ensure write fits in sSavedData buffer
                            if (pSavedData->pCurPos + iWriteLen > pSavedData->pEnd)
                            {
                                pSavedData->pCurPos = pSavedData->pHead;
                            }
                            pCurNal->sNalData.sVclNal.pNalPos = pSavedData->pCurPos;
                            pCurNal->sNalData.sVclNal.iNalLength = iActualLen - NAL_UNIT_HEADER_EXT_SIZE;
                            if (iCurrStartByte == 3)
                            {
                                pCurNal->sNalData.sVclNal.iNalLength++;
                            }
                            if (pCurNal->sNalHeaderExt.bIdrFlag)
                            {
                                *(pSrcNal + iCurrStartByte) &= 0xE0;
                                *(pSrcNal + iCurrStartByte) |= 0x05;
                            }
                            else
                            {
                                *(pSrcNal + iCurrStartByte) &= 0xE0;
                                *(pSrcNal + iCurrStartByte) |= 0x01;
                            }
                            pSavedData->pCurPos[0] = pSavedData->pCurPos[1] = pSavedData->pCurPos[2] = 0x0;
                            pSavedData->pCurPos[3] = 0x1;
                            pSavedData->pCurPos[4] = *(pSrcNal + iCurrStartByte);
                            pSavedData->pCurPos += 5;
                            memcpy(pSavedData->pCurPos, pSrcNal + iOffset, iActualLen - iOffset);
                            pSavedData->pCurPos += iActualLen - iOffset;
                        }
                    }
                    else
                    {
                        if (pCtx->pParam->bParseOnly)
                        {
                            int32_t iTrailingZeroByte = 0;
                            while (pSrcNal[iSrcNalLen - iTrailingZeroByte - 1] == 0x0) //remove final trailing 0 bytes
                                iTrailingZeroByte++;
                            int32_t iActualLen = iSrcNalLen - iTrailingZeroByte;
                            //unify start code as 0x0001
                            int32_t iStartDeltaByte = 0; //0 for 0x0001, 1 for 0x001
                            if (pSrcNal[0] == 0x0 && pSrcNal[1] == 0x0 && pSrcNal[2] == 0x1)
                            { //if 0x001
                                iStartDeltaByte = 1;
                            }
                            int32_t iWriteLen = iStartDeltaByte + iActualLen;
                            // Bounds check: ensure write fits in sSavedData buffer
                            if (pSavedData->pCurPos + iWriteLen > pSavedData->pEnd)
                            {
                                pSavedData->pCurPos = pSavedData->pHead;
                            }
                            pCurNal->sNalData.sVclNal.pNalPos = pSavedData->pCurPos;
                            pCurNal->sNalData.sVclNal.iNalLength = iActualLen;
                            if (iStartDeltaByte != 0)
                            {
                                pSavedData->pCurPos[0] = 0x0;
                                pCurNal->sNalData.sVclNal.iNalLength++;
                            }
                            memcpy(pSavedData->pCurPos + iStartDeltaByte, pSrcNal, iActualLen);
                            pSavedData->pCurPos += iStartDeltaByte + iActualLen;
                        }
                        if (NAL_UNIT_PREFIX == pCtx->sSpsPpsCtx.sPrefixNal.sNalHeaderExt.sNalUnitHeader.eNalUnitType)
                        {
                            if (pCtx->sSpsPpsCtx.sPrefixNal.sNalData.sPrefixNal.bPrefixNalCorrectFlag)
                            {
                                PrefetchNalHeaderExtSyntax(pCtx, pCurNal, &pCtx->sSpsPpsCtx.sPrefixNal);
                            }
                        }

                        pCurNal->sNalHeaderExt.bIdrFlag = (NAL_UNIT_CODED_SLICE_IDR == pNalUnitHeader->eNalUnitType) ? true :
                                                          false;   //SHOULD update this flag for AVC if no prefix NAL
                        pCurNal->sNalHeaderExt.iNoInterLayerPredFlag = 1;
                    }

                    pBs = &pCurAu->pNalUnitsList[uiAvailNalNum - 1]->sNalData.sVclNal.sSliceBitsRead;
                    iBitSize = (iNalSize << 3) - BsGetTrailingBits(pNal + iNalSize - 1); // convert into bit
                    iErr = DecInitBits(pBs, pNal, iBitSize);
                    if (iErr != 0)
                    {
                        ForceClearCurrentNal(pCurAu);
                        if (uiAvailNalNum > 1)
                        {
                            pCurAu->uiEndPos = uiAvailNalNum - 2;
                            if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                            {
                                pCtx->bAuReadyFlag = true;
                            }
                        }
                        WelsLog(pLogCtx, WELS_LOG_ERROR, "NAL_UNIT_CODED_SLICE: DecInitBits() fail due invalid access.");
                        pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                        return null;
                    }
                    iErr = ParseSliceHeaderSyntaxs(pCtx, pBs, bExtensionFlag);
                    if (iErr != ERR_NONE)
                    {
                        if ((uiAvailNalNum == 1) && (pCurNal->sNalHeaderExt.bIdrFlag))
                        { //IDR parse error
                            ResetActiveSPSForEachLayer(pCtx);
                        }
                        //if current NAL occur error when parsing, should clean it from pNalUnitsList
                        //otherwise, when Next good NAL decoding, this corrupt NAL is considered as normal NAL and lead to decoder crash
                        ForceClearCurrentNal(pCurAu);

                        if (uiAvailNalNum > 1)
                        {
                            pCurAu->uiEndPos = uiAvailNalNum - 2;
                            if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                            {
                                pCtx->bAuReadyFlag = true;
                            }
                        }
                        pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                        return null;
                    }

                    if ((uiAvailNalNum == 1)
                        && CheckNextAuNewSeq(pCtx, pCurNal, pCurNal->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.pSps))
                    {
                        ResetActiveSPSForEachLayer(pCtx);
                    }
                    if ((uiAvailNalNum > 1) &&
                        CheckAccessUnitBoundary(pCtx, pCurAu->pNalUnitsList[uiAvailNalNum - 1], pCurAu->pNalUnitsList[uiAvailNalNum - 2],
                                                pCurAu->pNalUnitsList[uiAvailNalNum - 1]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.pSps))
                    {
                        pCurAu->uiEndPos = uiAvailNalNum - 2;
                        pCtx->bAuReadyFlag = true;
                        pCtx->bNextNewSeqBegin = CheckNextAuNewSeq(pCtx, pCurNal, pCurNal->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.pSps);

                    }
                }
                break;
            default:
                break;
        }

        return pNal;
    }


    public static bool CheckAccessUnitBoundaryExt(SNalUnitHeaderExt* pLastNalHdrExt, SNalUnitHeaderExt* pCurNalHeaderExt,
                                                  SSliceHeader* pLastSliceHeader, SSliceHeader* pCurSliceHeader)
    {
        SSps* kpSps = pCurSliceHeader->pSps;

        //Sub-clause 7.1.4.1.1 temporal_id
        if (pLastNalHdrExt->uiTemporalId != pCurNalHeaderExt->uiTemporalId)
        {
            return true;
        }

        // Subclause 7.4.1.2.5
        if (pLastSliceHeader->iRedundantPicCnt > pCurSliceHeader->iRedundantPicCnt)
            return true;

        // Subclause G7.4.1.2.4
        if (pLastNalHdrExt->uiDependencyId > pCurNalHeaderExt->uiDependencyId)
            return true;
        if (pLastNalHdrExt->uiQualityId > pCurNalHeaderExt->uiQualityId)
            return true;

        // Subclause 7.4.1.2.4
        if (pLastSliceHeader->iFrameNum != pCurSliceHeader->iFrameNum)
            return true;
        if (pLastSliceHeader->iPpsId != pCurSliceHeader->iPpsId)
            return true;
        if (pLastSliceHeader->pSps->iSpsId != pCurSliceHeader->pSps->iSpsId)
            return true;
        if (pLastSliceHeader->bFieldPicFlag != pCurSliceHeader->bFieldPicFlag)
            return true;
        if (pLastSliceHeader->bBottomFiledFlag != pCurSliceHeader->bBottomFiledFlag)
            return true;
        if ((pLastNalHdrExt->sNalUnitHeader.uiNalRefIdc != (uint8_t)NRI_PRI_LOWEST) != (pCurNalHeaderExt->sNalUnitHeader.uiNalRefIdc !=
            (uint8_t)NRI_PRI_LOWEST))
            return true;
        if (pLastNalHdrExt->bIdrFlag != pCurNalHeaderExt->bIdrFlag)
            return true;
        if (pCurNalHeaderExt->bIdrFlag)
        {
            if (pLastSliceHeader->uiIdrPicId != pCurSliceHeader->uiIdrPicId)
                return true;
        }
        if (kpSps->uiPocType == 0)
        {
            if (pLastSliceHeader->iPicOrderCntLsb != pCurSliceHeader->iPicOrderCntLsb)
                return true;
            if (pLastSliceHeader->iDeltaPicOrderCntBottom != pCurSliceHeader->iDeltaPicOrderCntBottom)
                return true;
        }
        else if (kpSps->uiPocType == 1)
        {
            if (pLastSliceHeader->iDeltaPicOrderCnt[0] != pCurSliceHeader->iDeltaPicOrderCnt[0])
                return true;
            if (pLastSliceHeader->iDeltaPicOrderCnt[1] != pCurSliceHeader->iDeltaPicOrderCnt[1])
                return true;
        }
        if (memcmp(pLastSliceHeader->pPps, pCurSliceHeader->pPps, sizeof(SPps)) != 0
            || memcmp(pLastSliceHeader->pSps, pCurSliceHeader->pSps, sizeof(SSps)) != 0)
        {
            return true;
        }
        return false;
    }


    public static bool CheckAccessUnitBoundary(SWelsDecoderContext* pCtx, SNalUnit* kpCurNal, SNalUnit* kpLastNal,
                                               SSps* kpSps)
    {
        SNalUnitHeaderExt* kpLastNalHeaderExt = &kpLastNal->sNalHeaderExt;
        SNalUnitHeaderExt* kpCurNalHeaderExt = &kpCurNal->sNalHeaderExt;
        SSliceHeader* kpLastSliceHeader = &kpLastNal->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader;
        SSliceHeader* kpCurSliceHeader = &kpCurNal->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader;
        if (pCtx->sSpsPpsCtx.pActiveLayerSps[kpCurNalHeaderExt->uiDependencyId].p != null
            && pCtx->sSpsPpsCtx.pActiveLayerSps[kpCurNalHeaderExt->uiDependencyId].p != kpSps)
        {
            return true; // the active sps changed, new sequence begins, so the current au is ready
        }

        //Sub-clause 7.1.4.1.1 temporal_id
        if (kpLastNalHeaderExt->uiTemporalId != kpCurNalHeaderExt->uiTemporalId)
        {
            return true;
        }
        if (kpLastSliceHeader->iFrameNum != kpCurSliceHeader->iFrameNum)
            return true;
        // Subclause 7.4.1.2.5
        if (kpLastSliceHeader->iRedundantPicCnt > kpCurSliceHeader->iRedundantPicCnt)
            return true;

        // Subclause G7.4.1.2.4
        if (kpLastNalHeaderExt->uiDependencyId > kpCurNalHeaderExt->uiDependencyId)
            return true;
        // Subclause 7.4.1.2.4
        if (kpLastNalHeaderExt->uiDependencyId == kpCurNalHeaderExt->uiDependencyId
            && kpLastSliceHeader->iPpsId != kpCurSliceHeader->iPpsId)
            return true;
        if (kpLastSliceHeader->bFieldPicFlag != kpCurSliceHeader->bFieldPicFlag)
            return true;
        if (kpLastSliceHeader->bBottomFiledFlag != kpCurSliceHeader->bBottomFiledFlag)
            return true;
        if ((kpLastNalHeaderExt->sNalUnitHeader.uiNalRefIdc != (uint8_t)NRI_PRI_LOWEST) != (kpCurNalHeaderExt->sNalUnitHeader.uiNalRefIdc
            != (uint8_t)NRI_PRI_LOWEST))
            return true;
        if (kpLastNalHeaderExt->bIdrFlag != kpCurNalHeaderExt->bIdrFlag)
            return true;
        if (kpCurNalHeaderExt->bIdrFlag)
        {
            if (kpLastSliceHeader->uiIdrPicId != kpCurSliceHeader->uiIdrPicId)
                return true;
        }
        if (kpSps->uiPocType == 0)
        {
            if (kpLastSliceHeader->iPicOrderCntLsb != kpCurSliceHeader->iPicOrderCntLsb)
                return true;
            if (kpLastSliceHeader->iDeltaPicOrderCntBottom != kpCurSliceHeader->iDeltaPicOrderCntBottom)
                return true;
        }
        else if (kpSps->uiPocType == 1)
        {
            if (kpLastSliceHeader->iDeltaPicOrderCnt[0] != kpCurSliceHeader->iDeltaPicOrderCnt[0])
                return true;
            if (kpLastSliceHeader->iDeltaPicOrderCnt[1] != kpCurSliceHeader->iDeltaPicOrderCnt[1])
                return true;
        }

        return false;
    }

    public static bool CheckNextAuNewSeq(SWelsDecoderContext* pCtx, SNalUnit* kpCurNal, SSps* kpSps)
    {
        SNalUnitHeaderExt* kpCurNalHeaderExt = &kpCurNal->sNalHeaderExt;
        if (pCtx->sSpsPpsCtx.pActiveLayerSps[kpCurNalHeaderExt->uiDependencyId].p != null
            && pCtx->sSpsPpsCtx.pActiveLayerSps[kpCurNalHeaderExt->uiDependencyId].p != kpSps)
            return true;
        if (kpCurNalHeaderExt->bIdrFlag)
            return true;

        return false;
    }

    /*!
     *************************************************************************************
     * \brief   to parse NON VCL NAL Units
     *
     * \param   pCtx        decoder context
     * \param   rbsp        rbsp buffer of NAL Unit
     * \param   src_len     length of rbsp buffer
     *
     * \return  0 - successed
     *          1 - failed
     *
     *************************************************************************************
     */
    public static int32_t ParseNonVclNal(SWelsDecoderContext* pCtx, uint8_t* pRbsp, int32_t kiSrcLen, uint8_t* pSrcNal,
                                         int32_t kSrcNalLen)
    {
        SBitStringAux* pBs = null;
        EWelsNalUnitType eNalType = NAL_UNIT_UNSPEC_0; // make initial value as unspecified
        int32_t iPicWidth = 0;
        int32_t iPicHeight = 0;
        int32_t iBitSize = 0;
        int32_t iErr = ERR_NONE;
        if (kiSrcLen <= 0)
            return iErr;

        pBs = &pCtx->sBs; // SBitStringAux instance for non VCL NALs decoding
        iBitSize = (kiSrcLen << 3) - BsGetTrailingBits(pRbsp + kiSrcLen - 1); // convert into bit
        eNalType = pCtx->sCurNalHead.eNalUnitType;

        switch (eNalType)
        {
            case NAL_UNIT_SPS:
            case NAL_UNIT_SUBSET_SPS:
                if (iBitSize > 0)
                {
                    iErr = DecInitBits(pBs, pRbsp, iBitSize);
                    if (ERR_NONE != iErr)
                    {
                        if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                            pCtx->iErrorCode |= (int32_t)dsNoParamSets;
                        else
                            pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                        return iErr;
                    }
                }
                iErr = ParseSps(pCtx, pBs, &iPicWidth, &iPicHeight, pSrcNal, kSrcNalLen);
                if (ERR_NONE != iErr)
                { // modified for pSps/pSubsetSps invalid, 12/1/2009
                    if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                        pCtx->iErrorCode |= (int32_t)dsNoParamSets;
                    else
                        pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                    return iErr;
                }
                pCtx->bHasNewSps = true;
                break;

            case NAL_UNIT_PPS:
                if (iBitSize > 0)
                {
                    iErr = DecInitBits(pBs, pRbsp, iBitSize);
                    if (ERR_NONE != iErr)
                    {
                        if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                            pCtx->iErrorCode |= (int32_t)dsNoParamSets;
                        else
                            pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                        return iErr;
                    }
                }
                iErr = ParsePps(pCtx, &pCtx->sSpsPpsCtx.sPpsBuffer[0], pBs, pSrcNal, kSrcNalLen);
                if (ERR_NONE != iErr)
                { // modified for pps invalid, 12/1/2009
                    if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                        pCtx->iErrorCode |= (int32_t)dsNoParamSets;
                    else
                        pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                    pCtx->bHasNewSps = false;
                    return iErr;
                }

                pCtx->sSpsPpsCtx.bPpsExistAheadFlag = true;
                ++(pCtx->sSpsPpsCtx.iSeqId);
                break;

            case NAL_UNIT_SEI:

                break;

            case NAL_UNIT_PREFIX:
                break;
            case NAL_UNIT_CODED_SLICE_DPA:
            case NAL_UNIT_CODED_SLICE_DPB:
            case NAL_UNIT_CODED_SLICE_DPC:

                break;

            default:
                break;
        }

        return iErr;
    }

    public static int32_t ParseRefBasePicMarking(SBitStringAux* pBs, SRefBasePicMarking* pRefBasePicMarking)
    {
        uint32_t uiRetTmp;
        uint32_t uiCode;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //adaptive_ref_base_pic_marking_mode_flag
        bool kbAdaptiveMarkingModeFlag = uiCode != 0;
        pRefBasePicMarking->bAdaptiveRefBasePicMarkingModeFlag = kbAdaptiveMarkingModeFlag;
        if (kbAdaptiveMarkingModeFlag)
        {
            int32_t iIdx = 0;
            do
            {
                if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //MMCO_base
                uint32_t kuiMmco = uiCode;

                pRefBasePicMarking->mmco_base[iIdx].uiMmcoType = kuiMmco;

                if (kuiMmco == (uint32_t)MMCO_END)
                    break;

                if (kuiMmco == (uint32_t)MMCO_SHORT2UNUSED)
                {
                    if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //difference_of_base_pic_nums_minus1
                    pRefBasePicMarking->mmco_base[iIdx].uiDiffOfPicNums = 1 + uiCode;
                    pRefBasePicMarking->mmco_base[iIdx].iShortFrameNum = 0;
                }
                else if (kuiMmco == (uint32_t)MMCO_LONG2UNUSED)
                {
                    if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //long_term_base_pic_num
                    pRefBasePicMarking->mmco_base[iIdx].uiLongTermPicNum = uiCode;
                }
                ++iIdx;
            } while (iIdx < MAX_MMCO_COUNT);
        }
        return ERR_NONE;
    }

    public static int32_t ParsePrefixNalUnit(SWelsDecoderContext* pCtx, SBitStringAux* pBs)
    {
        uint32_t uiRetTmp;
        SNalUnit* pCurNal = &pCtx->sSpsPpsCtx.sPrefixNal;
        uint32_t uiCode;

        if (pCurNal->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc != 0)
        {
            SNalUnitHeaderExt* head_ext = &pCurNal->sNalHeaderExt;
            SPrefixNalUnit* sPrefixNal = &pCurNal->sNalData.sPrefixNal;
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //store_ref_base_pic_flag
            sPrefixNal->bStoreRefBasePicFlag = uiCode != 0;
            if ((head_ext->bUseRefBasePicFlag || sPrefixNal->bStoreRefBasePicFlag) && !head_ext->bIdrFlag)
            {
                if ((uiRetTmp = (uint32_t)ParseRefBasePicMarking(pBs, &sPrefixNal->sRefPicBaseMarking)) != ERR_NONE) return (int32_t)uiRetTmp;
            }
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //additional_prefix_nal_unit_extension_flag
            sPrefixNal->bPrefixNalUnitAdditionalExtFlag = uiCode != 0;
            if (sPrefixNal->bPrefixNalUnitAdditionalExtFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //additional_prefix_nal_unit_extension_data_flag
                sPrefixNal->bPrefixNalUnitExtFlag = uiCode != 0;
            }
        }
        return ERR_NONE;
    }

    public const int SUBSET_SPS_SEQ_SCALED_REF_LAYER_LEFT_OFFSET_MIN = -32768;
    public const int SUBSET_SPS_SEQ_SCALED_REF_LAYER_LEFT_OFFSET_MAX = 32767;
    public const int SUBSET_SPS_SEQ_SCALED_REF_LAYER_TOP_OFFSET_MIN = -32768;
    public const int SUBSET_SPS_SEQ_SCALED_REF_LAYER_TOP_OFFSET_MAX = 32767;
    public const int SUBSET_SPS_SEQ_SCALED_REF_LAYER_RIGHT_OFFSET_MIN = -32768;
    public const int SUBSET_SPS_SEQ_SCALED_REF_LAYER_RIGHT_OFFSET_MAX = 32767;
    public const int SUBSET_SPS_SEQ_SCALED_REF_LAYER_BOTTOM_OFFSET_MIN = -32768;
    public const int SUBSET_SPS_SEQ_SCALED_REF_LAYER_BOTTOM_OFFSET_MAX = 32767;




    public static int32_t DecodeSpsSvcExt(SWelsDecoderContext* pCtx, SSubsetSps* pSpsExt, SBitStringAux* pBs)
    {
        uint32_t uiRetTmp;
        SSpsSvcExt* pExt = null;
        uint32_t uiCode;
        int32_t iCode;

        pExt = &pSpsExt->sSpsSvcExt;

        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //inter_layer_deblocking_filter_control_present_flag
        pExt->bInterLayerDeblockingFilterCtrlPresentFlag = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 2, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //extended_spatial_scalability_idc
        pExt->uiExtendedSpatialScalability = (uint8_t)uiCode;
        if (pExt->uiExtendedSpatialScalability > 2)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                    $"DecodeSpsSvcExt():extended_spatial_scalability ({pExt->uiExtendedSpatialScalability}) != 0, ESS not supported!");
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_ESS);
        }

        pExt->uiChromaPhaseXPlus1Flag =
          0;  // FIXME: Incoherent with JVT X201 standard (= 1), but conformance to JSVM (= 0) implementation.
        pExt->uiChromaPhaseYPlus1 = 1;

        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //chroma_phase_x_plus1_flag
        pExt->uiChromaPhaseXPlus1Flag = (uint8_t)uiCode;
        if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 2, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //chroma_phase_y_plus1
        pExt->uiChromaPhaseYPlus1 = (uint8_t)uiCode;

        pExt->uiSeqRefLayerChromaPhaseXPlus1Flag = pExt->uiChromaPhaseXPlus1Flag;
        pExt->uiSeqRefLayerChromaPhaseYPlus1 = pExt->uiChromaPhaseYPlus1;
        memset(&pExt->sSeqScaledRefLayer, 0, sizeof(SPosOffset));

        if (pExt->uiExtendedSpatialScalability == 1)
        {
            SPosOffset* kpPos = &pExt->sSeqScaledRefLayer;
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_ref_layer_chroma_phase_x_plus1_flag
            pExt->uiSeqRefLayerChromaPhaseXPlus1Flag = (uint8_t)uiCode;
            if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 2, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_ref_layer_chroma_phase_y_plus1
            pExt->uiSeqRefLayerChromaPhaseYPlus1 = (uint8_t)uiCode;

            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_scaled_ref_layer_left_offset
            kpPos->iLeftOffset = iCode;
            WELS_CHECK_SE_BOTH_WARNING(pCtx, kpPos->iLeftOffset, SUBSET_SPS_SEQ_SCALED_REF_LAYER_LEFT_OFFSET_MIN,
                                       SUBSET_SPS_SEQ_SCALED_REF_LAYER_LEFT_OFFSET_MAX, "seq_scaled_ref_layer_left_offset");
            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_scaled_ref_layer_top_offset
            kpPos->iTopOffset = iCode;
            WELS_CHECK_SE_BOTH_WARNING(pCtx, kpPos->iTopOffset, SUBSET_SPS_SEQ_SCALED_REF_LAYER_TOP_OFFSET_MIN,
                                       SUBSET_SPS_SEQ_SCALED_REF_LAYER_TOP_OFFSET_MAX, "seq_scaled_ref_layer_top_offset");
            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_scaled_ref_layer_right_offset
            kpPos->iRightOffset = iCode;
            WELS_CHECK_SE_BOTH_WARNING(pCtx, kpPos->iRightOffset, SUBSET_SPS_SEQ_SCALED_REF_LAYER_RIGHT_OFFSET_MIN,
                                       SUBSET_SPS_SEQ_SCALED_REF_LAYER_RIGHT_OFFSET_MAX, "seq_scaled_ref_layer_right_offset");
            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_scaled_ref_layer_bottom_offset
            kpPos->iBottomOffset = iCode;
            WELS_CHECK_SE_BOTH_WARNING(pCtx, kpPos->iBottomOffset, SUBSET_SPS_SEQ_SCALED_REF_LAYER_BOTTOM_OFFSET_MIN,
                                       SUBSET_SPS_SEQ_SCALED_REF_LAYER_BOTTOM_OFFSET_MAX, "seq_scaled_ref_layer_bottom_offset");
        }

        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_tcoeff_level_prediction_flag
        pExt->bSeqTCoeffLevelPredFlag = uiCode != 0;
        pExt->bAdaptiveTCoeffLevelPredFlag = false;
        if (pExt->bSeqTCoeffLevelPredFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //adaptive_tcoeff_level_prediction_flag
            pExt->bAdaptiveTCoeffLevelPredFlag = uiCode != 0;
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //slice_header_restriction_flag
        pExt->bSliceHeaderRestrictionFlag = uiCode != 0;



        return ERR_NONE;
    }

    public static SLevelLimits* GetLevelLimits(int32_t iLevelIdx, bool bConstraint3)
    {
        switch (iLevelIdx)
        {
            case 9:
                return &g_ksLevelLimits[1];
            case 10:
                return &g_ksLevelLimits[0];
            case 11:
                if (bConstraint3)
                    return &g_ksLevelLimits[1];
                else
                    return &g_ksLevelLimits[2];
            case 12:
                return &g_ksLevelLimits[3];
            case 13:
                return &g_ksLevelLimits[4];
            case 20:
                return &g_ksLevelLimits[5];
            case 21:
                return &g_ksLevelLimits[6];
            case 22:
                return &g_ksLevelLimits[7];
            case 30:
                return &g_ksLevelLimits[8];
            case 31:
                return &g_ksLevelLimits[9];
            case 32:
                return &g_ksLevelLimits[10];
            case 40:
                return &g_ksLevelLimits[11];
            case 41:
                return &g_ksLevelLimits[12];
            case 42:
                return &g_ksLevelLimits[13];
            case 50:
                return &g_ksLevelLimits[14];
            case 51:
                return &g_ksLevelLimits[15];
            case 52:
                return &g_ksLevelLimits[16];
            default:
                return null;
        }
    }

    public static bool CheckSpsActive(SWelsDecoderContext* pCtx, SSps* pSps, bool bUseSubsetFlag)
    {
        for (int i = 0; i < MAX_LAYER_NUM; i++)
        {
            if (pCtx->sSpsPpsCtx.pActiveLayerSps[i].p == pSps)
                return true;
        }
        // Pre-active, will be used soon
        if (bUseSubsetFlag)
        {
            if (pSps->iMbWidth > 0 && pSps->iMbHeight > 0 && pCtx->sSpsPpsCtx.bSubspsAvailFlags[pSps->iSpsId])
            {
                if (pCtx->iTotalNumMbRec > 0)
                {
                    return true;
                }
                if (pCtx->pAccessUnitList->uiAvailUnitsNum > 0)
                {
                    int i = 0, iNum = (int32_t)pCtx->pAccessUnitList->uiAvailUnitsNum;
                    while (i < iNum)
                    {
                        SNalUnit* pNalUnit = pCtx->pAccessUnitList->pNalUnitsList[i];
                        if (pNalUnit->sNalData.sVclNal.bSliceHeaderExtFlag)
                        { //ext data
                            SSps* pNextUsedSps = pNalUnit->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.pSps;
                            if (pNextUsedSps->iSpsId == pSps->iSpsId)
                                return true;
                        }
                        ++i;
                    }
                }
            }
        }
        else
        {
            if (pSps->iMbWidth > 0 && pSps->iMbHeight > 0 && pCtx->sSpsPpsCtx.bSpsAvailFlags[pSps->iSpsId])
            {
                if (pCtx->iTotalNumMbRec > 0)
                {
                    return true;
                }
                if (pCtx->pAccessUnitList->uiAvailUnitsNum > 0)
                {
                    int i = 0, iNum = (int32_t)pCtx->pAccessUnitList->uiAvailUnitsNum;
                    while (i < iNum)
                    {
                        SNalUnit* pNalUnit = pCtx->pAccessUnitList->pNalUnitsList[i];
                        if (!pNalUnit->sNalData.sVclNal.bSliceHeaderExtFlag)
                        { //non-ext data
                            SSps* pNextUsedSps = pNalUnit->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.pSps;
                            if (pNextUsedSps->iSpsId == pSps->iSpsId)
                                return true;
                        }
                        ++i;
                    }
                }
            }
        }
        return false;
    }

    public const int SPS_LOG2_MAX_FRAME_NUM_MINUS4_MAX = 12;
    public const int SPS_LOG2_MAX_PIC_ORDER_CNT_LSB_MINUS4_MAX = 12;
    public const int SPS_NUM_REF_FRAMES_IN_PIC_ORDER_CNT_CYCLE_MAX = 255;
    public const int SPS_MAX_NUM_REF_FRAMES_MAX = 16;
    public const int PPS_PIC_INIT_QP_QS_MIN = 0;
    public const int PPS_PIC_INIT_QP_QS_MAX = 51;
    public const int PPS_CHROMA_QP_INDEX_OFFSET_MIN = -12;
    public const int PPS_CHROMA_QP_INDEX_OFFSET_MAX = 12;
    public const int SCALING_LIST_DELTA_SCALE_MAX = 127;
    public const int SCALING_LIST_DELTA_SCALE_MIN = -128;

    /*!
     *************************************************************************************
     * \brief   to parse Sequence Parameter Set (SPS)
     *
     * \param   pCtx        Decoder context
     * \param   pBsAux      bitstream reader auxiliary
     * \param   pPicWidth   picture width current Sps represented
     * \param   pPicHeight  picture height current Sps represented
     *
     * \return  0 - successed
     *      1 - failed
     *
     * \note    Call it in case eNalUnitType is SPS.
     *************************************************************************************
     */

    public static int32_t ParseSps(SWelsDecoderContext* pCtx, SBitStringAux* pBsAux, int32_t* pPicWidth, int32_t* pPicHeight,
                                   uint8_t* pSrcNal, int32_t kSrcNalLen)
    {
        uint32_t uiRetTmp;
        SBitStringAux* pBs = pBsAux;
        SSubsetSps sTempSubsetSps;
        SSps* pSps = null;
        SSubsetSps* pSubsetSps = null;
        SNalUnitHeader* pNalHead = &pCtx->sCurNalHead;
        ProfileIdc uiProfileIdc;
        uint8_t uiLevelIdc;
        int32_t iSpsId;
        uint32_t uiCode;
        int32_t iCode;
        int32_t iRet = ERR_NONE;
        bool* bConstraintSetFlags = stackalloc bool[6] { false, false, false, false, false, false };
        bool kbUseSubsetFlag = IS_SUBSET_SPS_NAL(pNalHead->eNalUnitType);

        if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 8, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //profile_idc
        uiProfileIdc = (ProfileIdc)uiCode;
        if ((EProfileIdc)uiProfileIdc != PRO_BASELINE && (EProfileIdc)uiProfileIdc != PRO_MAIN && (EProfileIdc)uiProfileIdc != PRO_SCALABLE_BASELINE
            && (EProfileIdc)uiProfileIdc != PRO_SCALABLE_HIGH
            && (EProfileIdc)uiProfileIdc != PRO_EXTENDED && (EProfileIdc)uiProfileIdc != PRO_HIGH)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "SPS ID can not be supported!\n");
            return 0; // return false;
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //constraint_set0_flag
        bConstraintSetFlags[0] = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //constraint_set1_flag
        bConstraintSetFlags[1] = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //constraint_set2_flag
        bConstraintSetFlags[2] = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //constraint_set3_flag
        bConstraintSetFlags[3] = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //constraint_set4_flag
        bConstraintSetFlags[4] = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //constraint_set5_flag
        bConstraintSetFlags[5] = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 2, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; // reserved_zero_2bits, equal to 0
        if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 8, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; // level_idc
        uiLevelIdc = (uint8_t)uiCode;
        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_parameter_set_id
        if (uiCode >= MAX_SPS_COUNT)
        { // Modified to check invalid negative iSpsId, 12/1/2009
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, " iSpsId is out of range! \n");
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_SPS_ID_OVERFLOW);
        }
        iSpsId = (int32_t)uiCode;
        pSubsetSps = &sTempSubsetSps;
        pSps = &sTempSubsetSps.sSps;
        memset(pSubsetSps, 0, sizeof(SSubsetSps));
        // Use the level 5.2 for compatibility
        SLevelLimits* pSMaxLevelLimits = GetLevelLimits(52, false);
        SLevelLimits* pSLevelLimits = GetLevelLimits(uiLevelIdc, bConstraintSetFlags[3]);
        if (null == pSLevelLimits)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"ParseSps(): level_idx ({uiLevelIdc}).\n");
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_UNSUPPORTED_NON_BASELINE);
        }
        else pSps->pSLevelLimits = pSLevelLimits;
        // syntax elements in default
        pSps->uiChromaFormatIdc = 1;
        pSps->uiChromaArrayType = 1;

        pSps->uiProfileIdc = uiProfileIdc;
        pSps->uiLevelIdc = uiLevelIdc;
        pSps->iSpsId = iSpsId;

        if (PRO_SCALABLE_BASELINE == (EProfileIdc)uiProfileIdc || PRO_SCALABLE_HIGH == (EProfileIdc)uiProfileIdc ||
            PRO_HIGH == (EProfileIdc)uiProfileIdc || PRO_HIGH10 == (EProfileIdc)uiProfileIdc ||
            PRO_HIGH422 == (EProfileIdc)uiProfileIdc || PRO_HIGH444 == (EProfileIdc)uiProfileIdc ||
            PRO_CAVLC444 == (EProfileIdc)uiProfileIdc || 44 == uiProfileIdc)
        {

            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //chroma_format_idc
            pSps->uiChromaFormatIdc = (uint8_t)uiCode;
            //    if (pSps->uiChromaFormatIdc != 1) {
            //      WelsLog (& (pCtx->sLogCtx), WELS_LOG_WARNING, "ParseSps(): chroma_format_idc (%d) = 1 supported.",
            //               pSps->uiChromaFormatIdc);
            //      return GENERATE_ERROR_NO (ERR_LEVEL_PARAM_SETS, ERR_INFO_UNSUPPORTED_NON_BASELINE);
            //    }
            if (pSps->uiChromaFormatIdc > 1)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"ParseSps(): chroma_format_idc ({pSps->uiChromaFormatIdc}) <=1 supported.");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_UNSUPPORTED_NON_BASELINE);

            }// To support 4:0:0; 4:2:0
            pSps->uiChromaArrayType = pSps->uiChromaFormatIdc;
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //bit_depth_luma_minus8
            if (uiCode != 0)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"ParseSps(): bit_depth_luma ({8 + uiCode}) Only 8 bit supported.");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_UNSUPPORTED_NON_BASELINE);
            }
            pSps->uiBitDepthLuma = 8;

            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //bit_depth_chroma_minus8
            if (uiCode != 0)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"ParseSps(): bit_depth_chroma ({8 + uiCode}). Only 8 bit supported.");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_UNSUPPORTED_NON_BASELINE);
            }
            pSps->uiBitDepthChroma = 8;

            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //qpprime_y_zero_transform_bypass_flag
            pSps->bQpPrimeYZeroTransfBypassFlag = uiCode != 0;
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_scaling_matrix_present_flag
            pSps->bSeqScalingMatrixPresentFlag = uiCode != 0;

            if (pSps->bSeqScalingMatrixPresentFlag)
            {
                if ((uiRetTmp = (uint32_t)ParseScalingList(pSps, pBs, false, false, pSps->bSeqScalingListPresentFlag, &pSps->iScalingList4x4[0],
                                                    &pSps->iScalingList8x8[0])) != ERR_NONE) return (int32_t)uiRetTmp;
            }
        }
        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //log2_max_frame_num_minus4
        if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, SPS_LOG2_MAX_FRAME_NUM_MINUS4_MAX, "log2_max_frame_num_minus4"))
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_LOG2_MAX_FRAME_NUM_MINUS4);
        pSps->uiLog2MaxFrameNum = LOG2_MAX_FRAME_NUM_OFFSET + uiCode;
        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_order_cnt_type
        pSps->uiPocType = uiCode;

        if (0 == pSps->uiPocType)
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //log2_max_pic_order_cnt_lsb_minus4
            // log2_max_pic_order_cnt_lsb_minus4 should be in range 0 to 12, inclusive. (sec. 7.4.3)
            if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, SPS_LOG2_MAX_PIC_ORDER_CNT_LSB_MINUS4_MAX, "log2_max_pic_order_cnt_lsb_minus4"))
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_LOG2_MAX_PIC_ORDER_CNT_LSB_MINUS4);
            pSps->iLog2MaxPocLsb = (int32_t)(LOG2_MAX_PIC_ORDER_CNT_LSB_OFFSET + uiCode); // log2_max_pic_order_cnt_lsb_minus4

        }
        else if (1 == pSps->uiPocType)
        {
            int32_t i;
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //delta_pic_order_always_zero_flag
            pSps->bDeltaPicOrderAlwaysZeroFlag = uiCode != 0;
            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //offset_for_non_ref_pic
            pSps->iOffsetForNonRefPic = iCode;
            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //offset_for_top_to_bottom_field
            pSps->iOffsetForTopToBottomField = iCode;
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_ref_frames_in_pic_order_cnt_cycle
            if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, SPS_NUM_REF_FRAMES_IN_PIC_ORDER_CNT_CYCLE_MAX,
                                          "num_ref_frames_in_pic_order_cnt_cycle"))
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_NUM_REF_FRAME_IN_PIC_ORDER_CNT_CYCLE);
            pSps->iNumRefFramesInPocCycle = (int32_t)uiCode;
            for (i = 0; i < pSps->iNumRefFramesInPocCycle; i++)
            {
                if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //offset_for_ref_frame[ i ]
                pSps->iOffsetForRefFrame[i] = (int8_t)iCode;
            }
        }
        if (pSps->uiPocType > 2)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $" illegal pic_order_cnt_type: {pSps->uiPocType} ! ");
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_POC_TYPE);
        }

        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //max_num_ref_frames
        pSps->iNumRefFrames = (int32_t)uiCode;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //gaps_in_frame_num_value_allowed_flag
        pSps->bGapsInFrameNumValueAllowedFlag = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_width_in_mbs_minus1
        pSps->iMbWidth = PIC_WIDTH_IN_MBS_OFFSET + uiCode;
        if (pSps->iMbWidth > MAX_MB_SIZE || pSps->iMbWidth == 0)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"pic_width_in_mbs({(int32_t)pSps->iMbWidth}) invalid!");
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_MAX_MB_SIZE);
        }
        if (((uint64_t)pSps->iMbWidth * (uint64_t)pSps->iMbWidth) > (uint64_t)(8u * pSLevelLimits->uiMaxFS))
        {
            if (((uint64_t)pSps->iMbWidth * (uint64_t)pSps->iMbWidth) > (uint64_t)(8u * pSMaxLevelLimits->uiMaxFS))
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "the pic_width_in_mbs exceeds the level limits!");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_MAX_MB_SIZE);
            }
            else
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "the pic_width_in_mbs exceeds the level limits!");
            }
        }
        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_height_in_map_units_minus1
        pSps->iMbHeight = PIC_HEIGHT_IN_MAP_UNITS_OFFSET + uiCode;
        if (pSps->iMbHeight > MAX_MB_SIZE || pSps->iMbHeight == 0)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"pic_height_in_mbs({(int32_t)pSps->iMbHeight}) invalid!");
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_MAX_MB_SIZE);
        }
        if (((uint64_t)pSps->iMbHeight * (uint64_t)pSps->iMbHeight) > (uint64_t)(8u * pSLevelLimits->uiMaxFS))
        {
            if (((uint64_t)pSps->iMbHeight * (uint64_t)pSps->iMbHeight) > (uint64_t)(8u * pSMaxLevelLimits->uiMaxFS))
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "the pic_height_in_mbs exceeds the level limits!");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_MAX_MB_SIZE);
            }
            else
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "the pic_height_in_mbs exceeds the level limits!");
            }
        }
        uint64_t uiTmp64 = (uint64_t)pSps->iMbWidth * (uint64_t)pSps->iMbHeight;
        if (uiTmp64 > (uint64_t)pSLevelLimits->uiMaxFS)
        {
            if (uiTmp64 > (uint64_t)pSMaxLevelLimits->uiMaxFS)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "the total count of mb exceeds the level limits!");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_MAX_MB_SIZE);
            }
            else
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "the total count of mb exceeds the level limits!");
            }
        }
        pSps->uiTotalMbCount = (uint32_t)uiTmp64;
        if (WELS_CHECK_SE_UPPER_ERROR(pCtx, pSps->iNumRefFrames, SPS_MAX_NUM_REF_FRAMES_MAX, "max_num_ref_frames"))
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_MAX_NUM_REF_FRAMES);
        // here we check max_num_ref_frames
        uint32_t uiMaxDpbMbs = pSLevelLimits->uiMaxDPBMbs;
        uint32_t uiMaxDpbFrames = uiMaxDpbMbs / pSps->uiTotalMbCount;
        if (uiMaxDpbFrames > SPS_MAX_NUM_REF_FRAMES_MAX)
            uiMaxDpbFrames = SPS_MAX_NUM_REF_FRAMES_MAX;
        if ((uint32_t)pSps->iNumRefFrames > uiMaxDpbFrames)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, " max_num_ref_frames exceeds level limits!");
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //frame_mbs_only_flag
        pSps->bFrameMbsOnlyFlag = uiCode != 0;
        if (!pSps->bFrameMbsOnlyFlag)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"ParseSps(): frame_mbs_only_flag ({(pSps->bFrameMbsOnlyFlag ? 1 : 0)}) not supported.");
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_UNSUPPORTED_MBAFF);
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //direct_8x8_inference_flag
        pSps->bDirect8x8InferenceFlag = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //frame_cropping_flag
        pSps->bFrameCroppingFlag = uiCode != 0;
        if (pSps->bFrameCroppingFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //frame_crop_left_offset
            pSps->sFrameCrop.iLeftOffset = (int32_t)uiCode;
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //frame_crop_right_offset
            pSps->sFrameCrop.iRightOffset = (int32_t)uiCode;
            if ((pSps->sFrameCrop.iLeftOffset + pSps->sFrameCrop.iRightOffset) > ((int32_t)pSps->iMbWidth * 16 / 2))
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "frame_crop_left_offset + frame_crop_right_offset exceeds limits!");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_CROPPING_DATA);
            }
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //frame_crop_top_offset
            pSps->sFrameCrop.iTopOffset = (int32_t)uiCode;
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //frame_crop_bottom_offset
            pSps->sFrameCrop.iBottomOffset = (int32_t)uiCode;
            if ((pSps->sFrameCrop.iTopOffset + pSps->sFrameCrop.iBottomOffset) > ((int32_t)pSps->iMbHeight * 16 / 2))
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "frame_crop_top_offset + frame_crop_right_offset exceeds limits!");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_CROPPING_DATA);
            }
        }
        else
        {
            pSps->sFrameCrop.iLeftOffset = 0; // frame_crop_left_offset
            pSps->sFrameCrop.iRightOffset = 0; // frame_crop_right_offset
            pSps->sFrameCrop.iTopOffset = 0; // frame_crop_top_offset
            pSps->sFrameCrop.iBottomOffset = 0; // frame_crop_bottom_offset
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //vui_parameters_present_flag
        pSps->bVuiParamPresentFlag = uiCode != 0;
        if (pSps->bVuiParamPresentFlag)
        {
            int iRetVui = ParseVui(pCtx, pSps, pBsAux);
            if (iRetVui == GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_UNSUPPORTED_VUI_HRD))
            {
                if (kbUseSubsetFlag)
                { //Currently do no support VUI with HRD enable in subsetSPS
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "hrd parse in vui of subsetSPS is not supported!");
                    return iRetVui;
                }
            }
            else
            {
                if ((uiRetTmp = (uint32_t)iRetVui) != ERR_NONE) return (int32_t)uiRetTmp;
            }
        }

        if (pCtx->pParam->bParseOnly)
        {
            if (kSrcNalLen >= SPS_PPS_BS_SIZE - 4)
            { //sps bs exceeds!
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"sps payload size ({kSrcNalLen}) too large for parse only ({SPS_PPS_BS_SIZE - 4}), not supported!");
                pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_OUT_OF_MEMORY);
            }
            if (!kbUseSubsetFlag)
            { //SPS
                SSpsBsInfo* pSpsBs = &pCtx->sSpsBsInfo[iSpsId];
                pSpsBs->iSpsId = iSpsId;
                int32_t iTrailingZeroByte = 0;
                while (pSrcNal[kSrcNalLen - iTrailingZeroByte - 1] == 0x0) //remove final trailing 0 bytes
                    iTrailingZeroByte++;
                int32_t iActualLen = kSrcNalLen - iTrailingZeroByte;
                pSpsBs->uiSpsBsLen = (uint16_t)iActualLen;
                //unify start code as 0x0001
                int32_t iStartDeltaByte = 0; //0 for 0x0001, 1 for 0x001
                if (pSrcNal[0] == 0x0 && pSrcNal[1] == 0x0 && pSrcNal[2] == 0x1)
                { //if 0x001
                    pSpsBs->pSpsBsBuf[0] = 0x0; //add 0 to form 0x0001
                    iStartDeltaByte++;
                    pSpsBs->uiSpsBsLen++;
                }
                memcpy(pSpsBs->pSpsBsBuf + iStartDeltaByte, pSrcNal, iActualLen);
            }
            else
            { //subset SPS
                SSpsBsInfo* pSpsBs = &pCtx->sSubsetSpsBsInfo[iSpsId];
                pSpsBs->iSpsId = iSpsId;
                pSpsBs->pSpsBsBuf[0] = pSpsBs->pSpsBsBuf[1] = pSpsBs->pSpsBsBuf[2] = 0x00;
                pSpsBs->pSpsBsBuf[3] = 0x01;
                pSpsBs->pSpsBsBuf[4] = 0x67;

                //re-write subset SPS to SPS
                SBitStringAux sSubsetSpsBs;
                CMemoryAlign* pMa = pCtx->pMemAlign;

                uint8_t* pBsBuf = (uint8_t*)(pMa->WelsMallocz(SPS_PPS_BS_SIZE + 4,
                                  "Temp buffer for parse only usage.")); //to reserve 4 bytes for UVLC writing buffer
                if (null == pBsBuf)
                {
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "sps buffer alloc failed for parse only!");
                    pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
                    return pCtx->iErrorCode;
                }
                InitBits(&sSubsetSpsBs, pBsBuf, (int32_t)(pBs->pEndBuf - pBs->pStartBuf));
                BsWriteBits(&sSubsetSpsBs, 8, 77); //profile_idc, forced to Main profile
                BsWriteOneBit(&sSubsetSpsBs, pSps->bConstraintSet0Flag ? 1u : 0u); // constraint_set0_flag
                BsWriteOneBit(&sSubsetSpsBs, pSps->bConstraintSet1Flag ? 1u : 0u); // constraint_set1_flag
                BsWriteOneBit(&sSubsetSpsBs, pSps->bConstraintSet2Flag ? 1u : 0u); // constraint_set2_flag
                BsWriteOneBit(&sSubsetSpsBs, pSps->bConstraintSet3Flag ? 1u : 0u); // constraint_set3_flag
                BsWriteBits(&sSubsetSpsBs, 4, 0); //constraint_set4_flag, constraint_set5_flag, reserved_zero_2bits
                BsWriteBits(&sSubsetSpsBs, 8, pSps->uiLevelIdc); //level_idc
                BsWriteUE(&sSubsetSpsBs, (uint32_t)pSps->iSpsId); //sps_id
                BsWriteUE(&sSubsetSpsBs, pSps->uiLog2MaxFrameNum - 4); //log2_max_frame_num_minus4
                BsWriteUE(&sSubsetSpsBs, pSps->uiPocType); //pic_order_cnt_type
                if (pSps->uiPocType == 0)
                {
                    BsWriteUE(&sSubsetSpsBs, (uint32_t)(pSps->iLog2MaxPocLsb - 4)); //log2_max_pic_order_cnt_lsb_minus4
                }
                else if (pSps->uiPocType == 1)
                {
                    BsWriteOneBit(&sSubsetSpsBs, pSps->bDeltaPicOrderAlwaysZeroFlag ? 1u : 0u); //delta_pic_order_always_zero_flag
                    BsWriteSE(&sSubsetSpsBs, pSps->iOffsetForNonRefPic); //offset_for_no_ref_pic
                    BsWriteSE(&sSubsetSpsBs, pSps->iOffsetForTopToBottomField); //offset_for_top_to_bottom_field
                    BsWriteUE(&sSubsetSpsBs, (uint32_t)pSps->iNumRefFramesInPocCycle); //num_ref_frames_in_pic_order_cnt_cycle
                    for (int32_t i = 0; i < pSps->iNumRefFramesInPocCycle; ++i)
                    {
                        BsWriteSE(&sSubsetSpsBs, pSps->iOffsetForRefFrame[i]); //offset_for_ref_frame[i]
                    }
                }
                BsWriteUE(&sSubsetSpsBs, (uint32_t)pSps->iNumRefFrames); //max_num_ref_frames
                BsWriteOneBit(&sSubsetSpsBs, pSps->bGapsInFrameNumValueAllowedFlag ? 1u : 0u); //gaps_in_frame_num_value_allowed_flag
                BsWriteUE(&sSubsetSpsBs, pSps->iMbWidth - 1); //pic_width_in_mbs_minus1
                BsWriteUE(&sSubsetSpsBs, pSps->iMbHeight - 1); //pic_height_in_map_units_minus1
                BsWriteOneBit(&sSubsetSpsBs, pSps->bFrameMbsOnlyFlag ? 1u : 0u); //frame_mbs_only_flag
                if (!pSps->bFrameMbsOnlyFlag)
                {
                    BsWriteOneBit(&sSubsetSpsBs, pSps->bMbaffFlag ? 1u : 0u); //mb_adaptive_frame_field_flag
                }
                BsWriteOneBit(&sSubsetSpsBs, pSps->bDirect8x8InferenceFlag ? 1u : 0u); //direct_8x8_inference_flag
                BsWriteOneBit(&sSubsetSpsBs, pSps->bFrameCroppingFlag ? 1u : 0u); //frame_cropping_flag
                if (pSps->bFrameCroppingFlag)
                {
                    BsWriteUE(&sSubsetSpsBs, (uint32_t)pSps->sFrameCrop.iLeftOffset); //frame_crop_left_offset
                    BsWriteUE(&sSubsetSpsBs, (uint32_t)pSps->sFrameCrop.iRightOffset); //frame_crop_right_offset
                    BsWriteUE(&sSubsetSpsBs, (uint32_t)pSps->sFrameCrop.iTopOffset); //frame_crop_top_offset
                    BsWriteUE(&sSubsetSpsBs, (uint32_t)pSps->sFrameCrop.iBottomOffset); //frame_crop_bottom_offset
                }
                BsWriteOneBit(&sSubsetSpsBs, 0); //vui_parameters_present_flag
                BsRbspTrailingBits(&sSubsetSpsBs); //finished, rbsp trailing bit
                int32_t iRbspSize = (int32_t)(sSubsetSpsBs.pCurBuf - sSubsetSpsBs.pStartBuf);
                RBSP2EBSP(pSpsBs->pSpsBsBuf + 5, sSubsetSpsBs.pStartBuf, iRbspSize);
                pSpsBs->uiSpsBsLen = (uint16_t)(sSubsetSpsBs.pCurBuf - sSubsetSpsBs.pStartBuf + 5);
                if (pBsBuf != null)
                {
                    pMa->WelsFree(pBsBuf, "pBsBuf for parse only usage");
                }
            }
        }
        // Check if SPS SVC extension applicated
        if (kbUseSubsetFlag && (PRO_SCALABLE_BASELINE == (EProfileIdc)uiProfileIdc || PRO_SCALABLE_HIGH == (EProfileIdc)uiProfileIdc))
        {
            if ((iRet = DecodeSpsSvcExt(pCtx, pSubsetSps, pBs)) != ERR_NONE)
            {
                return iRet;
            }

            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //svc_vui_parameters_present_flag
            pSubsetSps->bSvcVuiParamPresentFlag = uiCode != 0;
            if (pSubsetSps->bSvcVuiParamPresentFlag)
            {
            }
        }


        if (PRO_SCALABLE_BASELINE == (EProfileIdc)uiProfileIdc || PRO_SCALABLE_HIGH == (EProfileIdc)uiProfileIdc)
            pCtx->sSpsPpsCtx.bAvcBasedFlag = false;

        *pPicWidth = (int32_t)(pSps->iMbWidth << 4);
        *pPicHeight = (int32_t)(pSps->iMbHeight << 4);
        SSps* pTmpSps = null;
        if (kbUseSubsetFlag)
        {
            pTmpSps = &pCtx->sSpsPpsCtx.sSubsetSpsBuffer[iSpsId].sSps;
        }
        else
        {
            pTmpSps = &pCtx->sSpsPpsCtx.sSpsBuffer[iSpsId];
        }
        if (CheckSpsActive(pCtx, pTmpSps, kbUseSubsetFlag))
        {
            // we are overwriting the active sps, copy a temp buffer
            if (kbUseSubsetFlag)
            {
                if (memcmp(&pCtx->sSpsPpsCtx.sSubsetSpsBuffer[iSpsId], pSubsetSps, sizeof(SSubsetSps)) != 0)
                {
                    if (pCtx->pAccessUnitList->uiAvailUnitsNum > 0)
                    {
                        memcpy(&pCtx->sSpsPpsCtx.sSubsetSpsBuffer[MAX_SPS_COUNT], pSubsetSps, sizeof(SSubsetSps));
                        pCtx->bAuReadyFlag = true;
                        pCtx->pAccessUnitList->uiEndPos = pCtx->pAccessUnitList->uiAvailUnitsNum - 1;
                        pCtx->sSpsPpsCtx.iOverwriteFlags |= OVERWRITE_SUBSETSPS;
                    }
                    else if ((pCtx->pSps != null) && (pCtx->pSps->iSpsId == pSubsetSps->sSps.iSpsId))
                    {
                        memcpy(&pCtx->sSpsPpsCtx.sSubsetSpsBuffer[MAX_SPS_COUNT], pSubsetSps, sizeof(SSubsetSps));
                        pCtx->sSpsPpsCtx.iOverwriteFlags |= OVERWRITE_SUBSETSPS;
                    }
                    else
                    {
                        memcpy(&pCtx->sSpsPpsCtx.sSubsetSpsBuffer[iSpsId], pSubsetSps, sizeof(SSubsetSps));
                    }
                }
            }
            else
            {
                if (memcmp(&pCtx->sSpsPpsCtx.sSpsBuffer[iSpsId], pSps, sizeof(SSps)) != 0)
                {
                    if (pCtx->pAccessUnitList->uiAvailUnitsNum > 0)
                    {
                        memcpy(&pCtx->sSpsPpsCtx.sSpsBuffer[MAX_SPS_COUNT], pSps, sizeof(SSps));
                        pCtx->sSpsPpsCtx.iOverwriteFlags |= OVERWRITE_SPS;
                        pCtx->bAuReadyFlag = true;
                        pCtx->pAccessUnitList->uiEndPos = pCtx->pAccessUnitList->uiAvailUnitsNum - 1;
                    }
                    else if ((pCtx->pSps != null) && (pCtx->pSps->iSpsId == pSps->iSpsId))
                    {
                        memcpy(&pCtx->sSpsPpsCtx.sSpsBuffer[MAX_SPS_COUNT], pSps, sizeof(SSps));
                        pCtx->sSpsPpsCtx.iOverwriteFlags |= OVERWRITE_SPS;
                    }
                    else
                    {
                        memcpy(&pCtx->sSpsPpsCtx.sSpsBuffer[iSpsId], pSps, sizeof(SSps));
                    }
                }
            }
        }
        // Not overwrite active sps, just copy to final place
        else if (kbUseSubsetFlag)
        {
            memcpy(&pCtx->sSpsPpsCtx.sSubsetSpsBuffer[iSpsId], pSubsetSps, sizeof(SSubsetSps));
            pCtx->sSpsPpsCtx.bSubspsAvailFlags[iSpsId] = true;
            pCtx->sSpsPpsCtx.bSubspsExistAheadFlag = true;
        }
        else
        {
            memcpy(&pCtx->sSpsPpsCtx.sSpsBuffer[iSpsId], pSps, sizeof(SSps));
            pCtx->sSpsPpsCtx.bSpsAvailFlags[iSpsId] = true;
            pCtx->sSpsPpsCtx.bSpsExistAheadFlag = true;
        }
        return ERR_NONE;
    }

    /*!
     *************************************************************************************
     * \brief   to parse Picture Parameter Set (PPS)
     *
     * \param   pCtx        Decoder context
     * \param   pPpsList    pps list
     * \param   pBsAux      bitstream reader auxiliary
     *
     * \return  0 - successed
     *          1 - failed
     *
     * \note    Call it in case eNalUnitType is PPS.
     *************************************************************************************
     */
    public static int32_t ParsePps(SWelsDecoderContext* pCtx, SPps* pPpsList, SBitStringAux* pBsAux, uint8_t* pSrcNal,
                                   int32_t kSrcNalLen)
    {
        uint32_t uiRetTmp;

        SPps* pPps = null;
        SPps sTempPps;
        uint32_t uiPpsId = 0;
        uint32_t iTmp;
        uint32_t uiCode;
        int32_t iCode;

        if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_parameter_set_id
        uiPpsId = uiCode;
        if (uiPpsId >= MAX_PPS_COUNT)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_PPS_ID_OVERFLOW);
        }
        pPps = &sTempPps;
        memset(pPps, 0, sizeof(SPps));

        pPps->iPpsId = (int32_t)uiPpsId;
        if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //seq_parameter_set_id
        pPps->iSpsId = (int32_t)uiCode;

        if (pPps->iSpsId >= MAX_SPS_COUNT)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_SPS_ID_OVERFLOW);
        }

        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //entropy_coding_mode_flag
        pPps->bEntropyCodingModeFlag = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //bottom_field_pic_order_in_frame_present_flag
        pPps->bPicOrderPresentFlag = uiCode != 0;

        if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_slice_groups_minus1
        pPps->uiNumSliceGroups = NUM_SLICE_GROUPS_OFFSET + uiCode;

        if (pPps->uiNumSliceGroups > MAX_SLICEGROUP_IDS)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_SLICEGROUP);
        }

        if (pPps->uiNumSliceGroups > 1)
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //slice_group_map_type
            pPps->uiSliceGroupMapType = uiCode;
            if (pPps->uiSliceGroupMapType > 1)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"ParsePps(): slice_group_map_type ({pPps->uiSliceGroupMapType}): support only 0,1.");
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_UNSUPPORTED_FMOTYPE);
            }

            switch (pPps->uiSliceGroupMapType)
            {
                case 0:
                    for (iTmp = 0; iTmp < pPps->uiNumSliceGroups; iTmp++)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //run_length_minus1[ iGroup ]
                        if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, MAX_MB_SIZE - 1, "run_length_minus1"))
                            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_SLICEGROUP);
                        pPps->uiRunLength[iTmp] = RUN_LENGTH_OFFSET + uiCode;
                    }
                    break;
                default:
                    break;
            }
        }

        if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_ref_idx_l0_default_active_minus1
        pPps->uiNumRefIdxL0Active = NUM_REF_IDX_L0_DEFAULT_ACTIVE_OFFSET + uiCode;
        if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_ref_idx_l1_default_active_minus1
        pPps->uiNumRefIdxL1Active = NUM_REF_IDX_L1_DEFAULT_ACTIVE_OFFSET + uiCode;

        if (pPps->uiNumRefIdxL0Active > MAX_REF_PIC_COUNT ||
            pPps->uiNumRefIdxL1Active > MAX_REF_PIC_COUNT)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_REF_COUNT_OVERFLOW);
        }

        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //weighted_pred_flag
        pPps->bWeightedPredFlag = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 2, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //weighted_bipred_idc
        pPps->uiWeightedBipredIdc = (uint8_t)uiCode;
        // weighted_bipred_idc > 0 NOT supported now, but no impact when we ignore it

        if ((uiRetTmp = (uint32_t)BsGetSe(pBsAux, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_init_qp_minus26
        pPps->iPicInitQp = PIC_INIT_QP_OFFSET + iCode;
        if (WELS_CHECK_SE_BOTH_ERROR(pCtx, pPps->iPicInitQp, PPS_PIC_INIT_QP_QS_MIN, PPS_PIC_INIT_QP_QS_MAX, "pic_init_qp_minus26 + 26"))
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_PIC_INIT_QP);
        if ((uiRetTmp = (uint32_t)BsGetSe(pBsAux, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_init_qs_minus26
        pPps->iPicInitQs = PIC_INIT_QS_OFFSET + iCode;
        if (WELS_CHECK_SE_BOTH_ERROR(pCtx, pPps->iPicInitQs, PPS_PIC_INIT_QP_QS_MIN, PPS_PIC_INIT_QP_QS_MAX, "pic_init_qs_minus26 + 26"))
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_PIC_INIT_QS);
        if ((uiRetTmp = (uint32_t)BsGetSe(pBsAux, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //chroma_qp_index_offset,cb
        pPps->iChromaQpIndexOffset[0] = iCode;
        if (WELS_CHECK_SE_BOTH_ERROR(pCtx, pPps->iChromaQpIndexOffset[0], PPS_CHROMA_QP_INDEX_OFFSET_MIN, PPS_CHROMA_QP_INDEX_OFFSET_MAX,
                                     "chroma_qp_index_offset"))
            return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_CHROMA_QP_INDEX_OFFSET);
        pPps->iChromaQpIndexOffset[1] = pPps->iChromaQpIndexOffset[0];//init cr qp offset
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //deblocking_filter_control_present_flag
        pPps->bDeblockingFilterControlPresentFlag = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //constrained_intra_pred_flag
        pPps->bConstainedIntraPredFlag = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //redundant_pic_cnt_present_flag
        pPps->bRedundantPicCntPresentFlag = uiCode != 0;

        if (CheckMoreRBSPData(pBsAux))
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //transform_8x8_mode_flag
            pPps->bTransform8x8ModeFlag = uiCode != 0;
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_scaling_matrix_present_flag
            pPps->bPicScalingMatrixPresentFlag = uiCode != 0;
            if (pPps->bPicScalingMatrixPresentFlag)
            {
                if (pCtx->sSpsPpsCtx.bSpsAvailFlags[pPps->iSpsId])
                {
                    if ((uiRetTmp = (uint32_t)ParseScalingList(&pCtx->sSpsPpsCtx.sSpsBuffer[pPps->iSpsId], pBsAux, true, pPps->bTransform8x8ModeFlag,
                                                        pPps->bPicScalingListPresentFlag, &pPps->iScalingList4x4[0], &pPps->iScalingList8x8[0])) != ERR_NONE) return (int32_t)uiRetTmp;
                }
                else
                {
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                            $"ParsePps(): sps_id ({pPps->iSpsId}) does not exist for scaling_list. This PPS ({pPps->iPpsId}) is marked as invalid.");
                    return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_SPS_ID);
                }
            }
            if ((uiRetTmp = (uint32_t)BsGetSe(pBsAux, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //second_chroma_qp_index_offset
            pPps->iChromaQpIndexOffset[1] = iCode;
            if (WELS_CHECK_SE_BOTH_ERROR(pCtx, pPps->iChromaQpIndexOffset[1], PPS_CHROMA_QP_INDEX_OFFSET_MIN,
                                         PPS_CHROMA_QP_INDEX_OFFSET_MAX, "chroma_qp_index_offset"))
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_INVALID_CHROMA_QP_INDEX_OFFSET);
        }

        if (pCtx->pPps != null && pCtx->pPps->iPpsId == pPps->iPpsId)
        {
            if (memcmp(pCtx->pPps, pPps, sizeof(SPps)) != 0)
            {
                memcpy(&pCtx->sSpsPpsCtx.sPpsBuffer[MAX_PPS_COUNT], pPps, sizeof(SPps));
                pCtx->sSpsPpsCtx.iOverwriteFlags |= OVERWRITE_PPS;
                if (pCtx->pAccessUnitList->uiAvailUnitsNum > 0)
                {
                    pCtx->bAuReadyFlag = true;
                    pCtx->pAccessUnitList->uiEndPos = pCtx->pAccessUnitList->uiAvailUnitsNum - 1;
                }
            }
        }
        else
        {
            memcpy(&pCtx->sSpsPpsCtx.sPpsBuffer[(int)uiPpsId], pPps, sizeof(SPps));
            pCtx->sSpsPpsCtx.bPpsAvailFlags[uiPpsId] = true;
        }
        if (pCtx->pParam->bParseOnly)
        {
            if (kSrcNalLen >= SPS_PPS_BS_SIZE - 4)
            { //pps bs exceeds
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"pps payload size ({kSrcNalLen}) too large for parse only ({SPS_PPS_BS_SIZE - 4}), not supported!");
                pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                return GENERATE_ERROR_NO(ERR_LEVEL_PARAM_SETS, ERR_INFO_OUT_OF_MEMORY);
            }
            SPpsBsInfo* pPpsBs = &pCtx->sPpsBsInfo[(int)uiPpsId];
            pPpsBs->iPpsId = (int32_t)uiPpsId;
            int32_t iTrailingZeroByte = 0;
            while (pSrcNal[kSrcNalLen - iTrailingZeroByte - 1] == 0x0) //remove final trailing 0 bytes
                iTrailingZeroByte++;
            int32_t iActualLen = kSrcNalLen - iTrailingZeroByte;
            pPpsBs->uiPpsBsLen = (uint16_t)iActualLen;
            //unify start code as 0x0001
            int32_t iStartDeltaByte = 0; //0 for 0x0001, 1 for 0x001
            if (pSrcNal[0] == 0x0 && pSrcNal[1] == 0x0 && pSrcNal[2] == 0x1)
            { //if 0x001
                pPpsBs->pPpsBsBuf[0] = 0x0; //add 0 to form 0x0001
                iStartDeltaByte++;
                pPpsBs->uiPpsBsLen++;
            }
            memcpy(pPpsBs->pPpsBsBuf + iStartDeltaByte, pSrcNal, iActualLen);
        }
        return ERR_NONE;
    }

    public const int VUI_MAX_CHROMA_LOG_TYPE_TOP_BOTTOM_FIELD_MAX = 5;
    public const int VUI_NUM_UNITS_IN_TICK_MIN = 1;
    public const int VUI_TIME_SCALE_MIN = 1;
    public const int VUI_MAX_BYTES_PER_PIC_DENOM_MAX = 16;
    public const int VUI_MAX_BITS_PER_MB_DENOM_MAX = 16;
    public const int VUI_LOG2_MAX_MV_LENGTH_HOR_MAX = 16;
    public const int VUI_LOG2_MAX_MV_LENGTH_VER_MAX = 16;
    public const int VUI_MAX_DEC_FRAME_BUFFERING_MAX = 16;

    /*!
    *************************************************************************************
    * \brief   to parse Video Usability Information (VUI) parameter of the SPS
    *
    * \param   pCtx        Decoder context
    * \param   pSps        the sps which current Vui parameter belongs to
    * \param   pBsAux      bitstream reader auxiliary
    *
    * \return  0 - successed
    *          1 - failed
    *
    * \note    Call it in case the flag "vui_parameters_present_flag" in sps is true.
    *************************************************************************************
    */
    public static int32_t ParseVui(SWelsDecoderContext* pCtx, SSps* pSps, SBitStringAux* pBsAux)
    {
        uint32_t uiRetTmp;
        uint32_t uiCode;
        SVui* pVui = &pSps->sVui;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //aspect_ratio_info_present_flag
        pVui->bAspectRatioInfoPresentFlag = uiCode != 0;
        if (pSps->sVui.bAspectRatioInfoPresentFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 8, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //aspect_ratio_idc
            pVui->uiAspectRatioIdc = uiCode;
            if (pVui->uiAspectRatioIdc < 17)
            {
                pVui->uiSarWidth = g_ksVuiSampleAspectRatio[pVui->uiAspectRatioIdc].uiWidth;
                pVui->uiSarHeight = g_ksVuiSampleAspectRatio[pVui->uiAspectRatioIdc].uiHeight;
            }
            else if (pVui->uiAspectRatioIdc == EXTENDED_SAR)
            {
                if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 16, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //sar_width
                pVui->uiSarWidth = uiCode;
                if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 16, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //sar_height
                pVui->uiSarHeight = uiCode;
            }
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //overscan_info_present_flag
        pVui->bOverscanInfoPresentFlag = uiCode != 0;
        if (pVui->bOverscanInfoPresentFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //overscan_appropriate_flag
            pVui->bOverscanAppropriateFlag = uiCode != 0;
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //video_signal_type_present_flag
        pVui->bVideoSignalTypePresentFlag = uiCode != 0;
        if (pVui->bVideoSignalTypePresentFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 3, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //video_format
            pVui->uiVideoFormat = (uint8_t)uiCode;
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //video_full_range_flag
            pVui->bVideoFullRangeFlag = uiCode != 0;
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //colour_description_present_flag
            pVui->bColourDescripPresentFlag = uiCode != 0;
            if (pVui->bColourDescripPresentFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 8, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //colour_primaries
                pVui->uiColourPrimaries = (uint8_t)uiCode;
                if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 8, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //transfer_characteristics
                pVui->uiTransferCharacteristics = (uint8_t)uiCode;
                if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 8, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //matrix_coefficients
                pVui->uiMatrixCoeffs = (uint8_t)uiCode;
            }
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //chroma_loc_info_present_flag
        pVui->bChromaLocInfoPresentFlag = uiCode != 0;
        if (pVui->bChromaLocInfoPresentFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //chroma_sample_loc_type_top_field
            pVui->uiChromaSampleLocTypeTopField = uiCode;
            WELS_CHECK_SE_UPPER_WARNING(pCtx, pVui->uiChromaSampleLocTypeTopField, VUI_MAX_CHROMA_LOG_TYPE_TOP_BOTTOM_FIELD_MAX,
                                        "chroma_sample_loc_type_top_field");
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //chroma_sample_loc_type_bottom_field
            pVui->uiChromaSampleLocTypeBottomField = uiCode;
            WELS_CHECK_SE_UPPER_WARNING(pCtx, pVui->uiChromaSampleLocTypeBottomField, VUI_MAX_CHROMA_LOG_TYPE_TOP_BOTTOM_FIELD_MAX,
                                        "chroma_sample_loc_type_bottom_field");
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //timing_info_present_flag
        pVui->bTimingInfoPresentFlag = uiCode != 0;
        if (pVui->bTimingInfoPresentFlag)
        {
            uint32_t uiTmp = 0;
            if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 16, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_units_in_tick
            uiTmp = (uiCode << 16);
            if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 16, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_units_in_tick
            uiTmp |= uiCode;
            pVui->uiNumUnitsInTick = uiTmp;
            WELS_CHECK_SE_LOWER_WARNING(pCtx, pVui->uiNumUnitsInTick, VUI_NUM_UNITS_IN_TICK_MIN, "num_units_in_tick");
            if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 16, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //time_scale
            uiTmp = (uiCode << 16);
            if ((uiRetTmp = (uint32_t)BsGetBits(pBsAux, 16, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //time_scale
            uiTmp |= uiCode;
            pVui->uiTimeScale = uiTmp;
            WELS_CHECK_SE_LOWER_WARNING(pCtx, pVui->uiNumUnitsInTick, VUI_TIME_SCALE_MIN, "time_scale");
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //fixed_frame_rate_flag
            pVui->bFixedFrameRateFlag = uiCode != 0;
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //nal_hrd_parameters_present_flag
        pVui->bNalHrdParamPresentFlag = uiCode != 0;
        if (pVui->bNalHrdParamPresentFlag)
        { //Add HRD parse. the values are not being used though.
            // _PARSE_NALHRD_VCLHRD_PARAMS_
            int32_t cpb_cnt_minus1 = (int32_t)BsGetUe(pBsAux, &uiCode);
            /*bit_rate_scale = */
            BsGetBits(pBsAux, 4, &uiCode);
            /*cpb_size_scale = */
            BsGetBits(pBsAux, 4, &uiCode);
            for (int32_t i = 0; i <= cpb_cnt_minus1; i++)
            {
                /*bit_rate_value_minus1[i] = */
                BsGetUe(pBsAux, &uiCode);
                /*cpb_size_value_minus1[i] = */
                BsGetUe(pBsAux, &uiCode);
                /*cbr_flag[i] = */
                BsGetOneBit(pBsAux, &uiCode);
            }
            /*initial_cpb_removal_delay_length_minus1 = */
            BsGetBits(pBsAux, 5, &uiCode);
            /*cpb_removal_delay_length_minus1 = */
            BsGetBits(pBsAux, 5, &uiCode);
            /*dpb_output_delay_length_minus1 = */
            BsGetBits(pBsAux, 5, &uiCode);
            /*time_offset_length = */
            BsGetBits(pBsAux, 5, &uiCode);
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //vcl_hrd_parameters_present_flag
        pVui->bVclHrdParamPresentFlag = uiCode != 0;
        if (pVui->bVclHrdParamPresentFlag)
        {//Add HRD parse. the values are not being used though.
            // _PARSE_NALHRD_VCLHRD_PARAMS_
            int32_t cpb_cnt_minus1 = (int32_t)BsGetUe(pBsAux, &uiCode);
            /*bit_rate_scale = */
            BsGetBits(pBsAux, 4, &uiCode);
            /*cpb_size_scale = */
            BsGetBits(pBsAux, 4, &uiCode);
            for (int32_t i = 0; i <= cpb_cnt_minus1; i++)
            {
                /*bit_rate_value_minus1[i] = */
                BsGetUe(pBsAux, &uiCode);
                /*cpb_size_value_minus1[i] = */
                BsGetUe(pBsAux, &uiCode);
                /*cbr_flag[i] = */
                BsGetOneBit(pBsAux, &uiCode);
            }
            /*initial_cpb_removal_delay_length_minus1 = */
            BsGetBits(pBsAux, 5, &uiCode);
            /*cpb_removal_delay_length_minus1 = */
            BsGetBits(pBsAux, 5, &uiCode);
            /*dpb_output_delay_length_minus1 = */
            BsGetBits(pBsAux, 5, &uiCode);
            /*time_offset_length = */
            BsGetBits(pBsAux, 5, &uiCode);
        }
        // _PARSE_NALHRD_VCLHRD_PARAMS_
        if (pVui->bNalHrdParamPresentFlag | pVui->bVclHrdParamPresentFlag)
        {
            /*low_delay_hrd_flag = */
            BsGetOneBit(pBsAux, &uiCode);
        }
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_struct_present_flag
        pVui->bPicStructPresentFlag = uiCode != 0;
        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //bitstream_restriction_flag
        pVui->bBitstreamRestrictionFlag = uiCode != 0;
        if (pVui->bBitstreamRestrictionFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //motion_vectors_over_pic_boundaries_flag
            pVui->bMotionVectorsOverPicBoundariesFlag = uiCode != 0;
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //max_bytes_per_pic_denom
            pVui->uiMaxBytesPerPicDenom = uiCode;
            WELS_CHECK_SE_UPPER_WARNING(pCtx, pVui->uiMaxBytesPerPicDenom, VUI_MAX_BYTES_PER_PIC_DENOM_MAX,
                                        "max_bytes_per_pic_denom");
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //max_bits_per_mb_denom
            pVui->uiMaxBitsPerMbDenom = uiCode;
            WELS_CHECK_SE_UPPER_WARNING(pCtx, pVui->uiMaxBitsPerMbDenom, VUI_MAX_BITS_PER_MB_DENOM_MAX,
                                        "max_bits_per_mb_denom");
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //log2_max_mv_length_horizontal
            pVui->uiLog2MaxMvLengthHorizontal = uiCode;
            WELS_CHECK_SE_UPPER_WARNING(pCtx, pVui->uiLog2MaxMvLengthHorizontal, VUI_LOG2_MAX_MV_LENGTH_HOR_MAX,
                                        "log2_max_mv_length_horizontal");
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //log2_max_mv_length_vertical
            pVui->uiLog2MaxMvLengthVertical = uiCode;
            WELS_CHECK_SE_UPPER_WARNING(pCtx, pVui->uiLog2MaxMvLengthVertical, VUI_LOG2_MAX_MV_LENGTH_VER_MAX,
                                        "log2_max_mv_length_vertical");
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //max_num_reorder_frames
            pVui->uiMaxNumReorderFrames = uiCode;
            WELS_CHECK_SE_UPPER_WARNING(pCtx, pVui->uiMaxNumReorderFrames, VUI_MAX_DEC_FRAME_BUFFERING_MAX,
                                        "max_num_reorder_frames");
            if ((uiRetTmp = (uint32_t)BsGetUe(pBsAux, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //max_dec_frame_buffering
            pVui->uiMaxDecFrameBuffering = uiCode;
            WELS_CHECK_SE_UPPER_WARNING(pCtx, pVui->uiMaxDecFrameBuffering, VUI_MAX_DEC_FRAME_BUFFERING_MAX,
                                        "max_num_reorder_frames");
        }
        return ERR_NONE;
    }
    /*!
     *************************************************************************************
     * \brief   to parse SEI message payload
     *
     * \param   pSei        sei message to be parsed output
     * \param   pBsAux      bitstream reader auxiliary
     *
     * \return  0 - successed
     *          1 - failed
     *
     * \note    Call it in case eNalUnitType is NAL_UNIT_SEI.
     *************************************************************************************
     */
    public static int32_t ParseSei(void* pSei, SBitStringAux* pBsAux)
    { // reserved Sei_Msg type


        return ERR_NONE;
    }
    /*
     *************************************************************************************
     * \brief   to parse scalinglist message payload
     *
     * \param   pps sps scaling list matrix      message to be parsed output
     * \param   pBsAux      bitstream reader auxiliary
     *
     * \return  0 - successed
     *          1 - failed
     *
     * \note    Call it in case scaling list matrix present at sps or pps level
     *************************************************************************************
     */
    public static int32_t SetScalingListValue(uint8_t* pScalingList, int iScalingListNum, bool* bUseDefaultScalingMatrixFlag,
                                              SBitStringAux* pBsAux)
    { // reserved Sei_Msg type
        uint32_t uiRetTmp;
        int iLastScale = 8;
        int iNextScale = 8;
        int iDeltaScale;
        int32_t iCode;
        int32_t iIdx;
        for (int j = 0; j < iScalingListNum; j++)
        {
            if (iNextScale != 0)
            {
                if ((uiRetTmp = (uint32_t)BsGetSe(pBsAux, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                if (WELS_CHECK_SE_BOTH_ERROR_NOLOG(iCode, SCALING_LIST_DELTA_SCALE_MIN, SCALING_LIST_DELTA_SCALE_MAX))
                    return ERR_SCALING_LIST_DELTA_SCALE; // "DeltaScale"
                iDeltaScale = iCode;
                iNextScale = (iLastScale + iDeltaScale + 256) % 256;
                *bUseDefaultScalingMatrixFlag = (j == 0 && iNextScale == 0);
                if (*bUseDefaultScalingMatrixFlag)
                    break;
            }
            iIdx = iScalingListNum == 16 ? g_kuiZigzagScan[j] : g_kuiZigzagScan8x8[j];
            pScalingList[iIdx] = (uint8_t)((iNextScale == 0) ? iLastScale : iNextScale);
            iLastScale = pScalingList[iIdx];
        }


        return ERR_NONE;
    }

    public static int32_t ParseScalingList(SSps* pSps, SBitStringAux* pBs, bool bPPS, bool kbTrans8x8ModeFlag,
                                           bool* pScalingListPresentFlag, InlineArray16<uint8_t>* iScalingList4x4, InlineArray64<uint8_t>* iScalingList8x8)
    {
        uint32_t uiRetTmp;
        uint32_t uiScalingListNum;
        uint32_t uiCode;

        bool bUseDefaultScalingMatrixFlag4x4 = false;
        bool bUseDefaultScalingMatrixFlag8x8 = false;
        bool bInit = false;
        uint8_t** defaultScaling = stackalloc uint8_t*[4];

        if (!bPPS)
        { //sps scaling_list
            uiScalingListNum = (pSps->uiChromaFormatIdc != 3) ? 8u : 12u;
        }
        else
        { //pps scaling_list
            uiScalingListNum = (uint32_t)(6 + (kbTrans8x8ModeFlag ? 1 : 0) * ((pSps->uiChromaFormatIdc != 3) ? 2 : 6));
            bInit = pSps->bSeqScalingMatrixPresentFlag;
        }

        //Init default_scaling_list value for sps or pps
        defaultScaling[0] = bInit ? (uint8_t*)&pSps->iScalingList4x4[0] : (uint8_t*)&g_kuiDequantScaling4x4Default[0];
        defaultScaling[1] = bInit ? (uint8_t*)&pSps->iScalingList4x4[3] : (uint8_t*)&g_kuiDequantScaling4x4Default[1];
        defaultScaling[2] = bInit ? (uint8_t*)&pSps->iScalingList8x8[0] : (uint8_t*)&g_kuiDequantScaling8x8Default[0];
        defaultScaling[3] = bInit ? (uint8_t*)&pSps->iScalingList8x8[1] : (uint8_t*)&g_kuiDequantScaling8x8Default[1];

        for (uint i = 0; i < uiScalingListNum; i++)
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
            pScalingListPresentFlag[i] = uiCode != 0;
            if (uiCode != 0)
            {
                if (i < 6)
                {//4x4 scaling list
                    if ((uiRetTmp = (uint32_t)SetScalingListValue((uint8_t*)&iScalingList4x4[i], 16, &bUseDefaultScalingMatrixFlag4x4, pBs)) != ERR_NONE) return (int32_t)uiRetTmp;
                    if (bUseDefaultScalingMatrixFlag4x4)
                    {
                        bUseDefaultScalingMatrixFlag4x4 = false;
                        memcpy(&iScalingList4x4[i], &g_kuiDequantScaling4x4Default[i / 3], sizeof(uint8_t) * 16);
                    }


                }
                else
                {
                    if ((uiRetTmp = (uint32_t)SetScalingListValue((uint8_t*)&iScalingList8x8[i - 6], 64, &bUseDefaultScalingMatrixFlag8x8, pBs)) != ERR_NONE) return (int32_t)uiRetTmp;

                    if (bUseDefaultScalingMatrixFlag8x8)
                    {
                        bUseDefaultScalingMatrixFlag8x8 = false;
                        memcpy(&iScalingList8x8[i - 6], &g_kuiDequantScaling8x8Default[(i - 6) & 1], sizeof(uint8_t) * 64);
                    }
                }

            }
            else
            {
                if (i < 6)
                {
                    if ((i != 0) && (i != 3))
                        memcpy(&iScalingList4x4[i], &iScalingList4x4[i - 1], sizeof(uint8_t) * 16);
                    else
                        memcpy(&iScalingList4x4[i], defaultScaling[i / 3], sizeof(uint8_t) * 16);

                }
                else
                {
                    if ((i == 6) || (i == 7))
                        memcpy(&iScalingList8x8[i - 6], defaultScaling[(i & 1) + 2], sizeof(uint8_t) * 64);
                    else
                        memcpy(&iScalingList8x8[i - 6], &iScalingList8x8[i - 8], sizeof(uint8_t) * 64);

                }
            }
        }
        return ERR_NONE;

    }

    /*!
     *************************************************************************************
     * \brief   reset fmo list due to got Sps now
     *
     * \param   pCtx    decoder context
     *
     * \return  count number of fmo context units are reset
     *************************************************************************************
     */
    public static int32_t ResetFmoList(SWelsDecoderContext* pCtx)
    {
        int32_t iCountNum = 0;
        if (null != pCtx)
        {
            // Fixed memory leak due to PPS_ID might not be continuous sometimes, 1/5/2010
            UninitFmoList(&pCtx->sFmoList[0], MAX_PPS_COUNT, pCtx->iActiveFmoNum, pCtx->pMemAlign);
            iCountNum = pCtx->iActiveFmoNum;
            pCtx->iActiveFmoNum = 0;
        }
        return iCountNum;
    }
}
