//-----------------------------------------------------------------------------
// Filename: memmgr_nal_unit.cs
//
// Description: Port of codec/decoder/core/inc/memmgr_nal_unit.h and
// src/memmgr_nal_unit.cpp: the access unit's NAL unit list memory.
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
 *     Copyright (c)  2008-2013, Cisco Systems
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
 *  memmgr_nal_unit.c
 *
 *  Abstract
 *      memory manager utils for NAL Unit list available
 *
 *  History
 *      07/10/2008 Created
 *
 *****************************************************************************/

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    public static int32_t MemInitNalList(SAccessUnit** ppAu, uint32_t kuiSize, CMemoryAlign* pMa)
    {
        uint32_t uiIdx = 0;
        uint8_t* pBase = null, pPtr = null;
        uint32_t kuiSizeAu = (uint32_t)sizeof(SAccessUnit);
        uint32_t kuiSizeNalUnitPtr = kuiSize * (uint32_t)sizeof(SNalUnit*);
        uint32_t kuiSizeNalUnit = (uint32_t)sizeof(SNalUnit);
        uint32_t kuiCountSize = (kuiSizeAu + kuiSizeNalUnitPtr + kuiSize * kuiSizeNalUnit) * sizeof(uint8_t);

        if (kuiSize == 0)
            return ERR_INFO_INVALID_PARAM;

        if (*ppAu != null)
        {
            MemFreeNalList(ppAu, pMa);
        }

        pBase = (uint8_t*)pMa->WelsMallocz(kuiCountSize, "Access Unit");
        if (pBase == null)
            return ERR_INFO_OUT_OF_MEMORY;
        pPtr = pBase;
        *ppAu = (SAccessUnit*)pPtr;
        pPtr += kuiSizeAu;
        (*ppAu)->pNalUnitsList = (SNalUnit**)pPtr;
        pPtr += kuiSizeNalUnitPtr;
        do
        {
            (*ppAu)->pNalUnitsList[uiIdx] = (SNalUnit*)pPtr;
            pPtr += kuiSizeNalUnit;
            ++uiIdx;
        } while (uiIdx < kuiSize);

        (*ppAu)->uiCountUnitsNum = kuiSize;
        (*ppAu)->uiAvailUnitsNum = 0;
        (*ppAu)->uiActualUnitsNum = 0;
        (*ppAu)->uiStartPos = 0;
        (*ppAu)->uiEndPos = 0;
        (*ppAu)->bCompletedAuFlag = false;

        return ERR_NONE;
    }

    public static int32_t MemFreeNalList(SAccessUnit** ppAu, CMemoryAlign* pMa)
    {
        if (ppAu != null)
        {
            SAccessUnit* pAu = *ppAu;
            if (pAu != null)
            {
                pMa->WelsFree(pAu, "Access Unit");
                *ppAu = null;
            }
        }
        return ERR_NONE;
    }


    public static int32_t ExpandNalUnitList(SAccessUnit** ppAu, int32_t kiOrgSize, int32_t kiExpSize, CMemoryAlign* pMa)
    {
        if (kiExpSize <= kiOrgSize)
            return ERR_INFO_INVALID_PARAM;
        else
        {
            SAccessUnit* pTmp = null;
            int32_t iIdx = 0;
            int32_t iRet = ERR_NONE;
            if ((iRet = MemInitNalList(&pTmp, (uint32_t)kiExpSize, pMa)) != ERR_NONE) // request new list with expanding
                return iRet;

            do
            {
                memcpy(pTmp->pNalUnitsList[iIdx], (*ppAu)->pNalUnitsList[iIdx], sizeof(SNalUnit)); //confirmed_safe_unsafe_usage
                ++iIdx;
            } while (iIdx < kiOrgSize);

            pTmp->uiCountUnitsNum = (uint32_t)kiExpSize;
            pTmp->uiAvailUnitsNum = (*ppAu)->uiAvailUnitsNum;
            pTmp->uiActualUnitsNum = (*ppAu)->uiActualUnitsNum;
            pTmp->uiEndPos = (*ppAu)->uiEndPos;
            pTmp->bCompletedAuFlag = (*ppAu)->bCompletedAuFlag;

            MemFreeNalList(ppAu, pMa); // free old list
            *ppAu = pTmp;
            return ERR_NONE;
        }
    }

    /*
     *  MemGetNextNal
     *  Get next NAL Unit for using.
     *  Need expand NAL Unit list if exceeding count number of available NAL Units withing an Access Unit
     */
    public static SNalUnit* MemGetNextNal(SAccessUnit** ppAu, CMemoryAlign* pMa)
    {
        SAccessUnit* pAu = *ppAu;
        SNalUnit* pNu = null;

        if (pAu->uiAvailUnitsNum >= pAu->uiCountUnitsNum)
        { // need expand list
            uint32_t kuiExpandingSize = pAu->uiCountUnitsNum + (MAX_NAL_UNIT_NUM_IN_AU >> 1);
            if (ExpandNalUnitList(ppAu, (int32_t)pAu->uiCountUnitsNum, (int32_t)kuiExpandingSize, pMa) != 0)
                return null; // out of memory
            pAu = *ppAu;
        }

        pNu = pAu->pNalUnitsList[pAu->uiAvailUnitsNum++]; // ready for next nal position

        memset(pNu, 0, sizeof(SNalUnit)); // Please do not remove this for cache intend!!

        return pNu;
    }
}
