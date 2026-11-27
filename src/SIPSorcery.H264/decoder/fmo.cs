//-----------------------------------------------------------------------------
// Filename: fmo.cs
//
// Description: Port of codec/decoder/core/inc/fmo.h and src/fmo.cpp: Flexible
// Macroblock Ordering.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// All of fmo.cpp. FMO itself (more than one slice group) is outside the
// Constrained Baseline target, but FmoParamUpdate runs for every slice and
// FmoNextMb is the slice loop's next-MB step. MB_XY_T is int32_t.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D2: the rest of fmo.cpp.
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
 * \file    fmo.c
 *
 * \brief   Flexible Macroblock Ordering implementation
 *
 * \date    2/4/2009 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Decoder;

/*!
 * \brief   Wels Flexible Macroblock Ordering (FMO)
 */
/// <summary>TagFmo (PFmo is SFmo*).</summary>
internal unsafe struct SFmo
{
    public uint8_t* pMbAllocMap;
    public int32_t iCountMbNum;
    public int32_t iSliceGroupCount;
    public int32_t iSliceGroupType;
    public bool bActiveFlag;
    public fixed byte uiReserved[3];          // reserved padding bytes
}

internal static unsafe partial class WelsDec
{
    /*!
     * \brief   Generate MB allocated map for interleaved slice group (TYPE 0)
     *
     * \param   pFmo    fmo context
     * \param   pPps    pps context
     *
     * \return  0 - successful; none 0 - failed
     */
    private static int32_t FmoGenerateMbAllocMapType0(SFmo* pFmo, SPps* pPps)
    {
        uint32_t uiNumSliceGroups = 0;
        int32_t iMbNum = 0;
        int32_t i = 0;

        if (null == pFmo || null == pPps)
            return ERR_INFO_INVALID_PARAM;
        uiNumSliceGroups = pPps->uiNumSliceGroups;
        iMbNum = pFmo->iCountMbNum;
        if (null == pFmo->pMbAllocMap || iMbNum <= 0
            || uiNumSliceGroups > MAX_SLICEGROUP_IDS)
            return ERR_INFO_INVALID_PARAM;

        do
        {
            uint8_t uiGroup = 0;
            do
            {
                uint32_t kuiRunIdx = pPps->uiRunLength[uiGroup];
                // Reject zero or oversized run lengths before advancing i. A single run
                // cannot legitimately exceed the total MB count; this also prevents any
                // integer wrap of the map index with attacker-influenced values.
                if (0 == kuiRunIdx || kuiRunIdx > (uint32_t)iMbNum)
                    return ERR_INFO_INVALID_PARAM;
                int32_t j = 0;
                do
                {
                    pFmo->pMbAllocMap[i + j] = uiGroup;
                    ++j;
                } while (j < (int32_t)kuiRunIdx && i + j < iMbNum);
                i += (int32_t)kuiRunIdx;
                ++uiGroup;
            } while (uiGroup < uiNumSliceGroups && i < iMbNum);
        } while (i < iMbNum);

        return ERR_NONE; // well here
    }

    /*!
     * \brief   Generate MB allocated map for dispersed slice group (TYPE 1)
     *
     * \param   pFmo        fmo context
     * \param   pPps        pps context
     * \param   iMbWidth    MB width
     *
     * \return  0 - successful; none 0 - failed
     */
    private static int32_t FmoGenerateMbAllocMapType1(SFmo* pFmo, SPps* pPps, int32_t kiMbWidth)
    {
        uint32_t uiNumSliceGroups = 0;
        int32_t iMbNum = 0;
        int32_t i = 0;
        if (null == pFmo || null == pPps)
            return ERR_INFO_INVALID_PARAM;
        uiNumSliceGroups = pPps->uiNumSliceGroups;
        iMbNum = pFmo->iCountMbNum;
        if (null == pFmo->pMbAllocMap || iMbNum <= 0 || kiMbWidth == 0
            || uiNumSliceGroups > MAX_SLICEGROUP_IDS)
            return ERR_INFO_INVALID_PARAM;

        do
        {
            // C does this in uint32_t (the int32_t terms meet uiNumSliceGroups).
            pFmo->pMbAllocMap[i] = (uint8_t)(((uint32_t)(i % kiMbWidth) + (((uint32_t)(i / kiMbWidth) * uiNumSliceGroups) >> 1)) % uiNumSliceGroups);
            ++i;
        } while (i < iMbNum);

        return ERR_NONE; // well here
    }

    /*!
     * \brief   Generate MB allocated map for various type of slice group cases (TYPE 0, .., 6)
     *
     * \param   pFmo        fmo context
     * \param   pPps        pps context
     * \param   kiMbWidth   MB width
     * \param   kiMbHeight  MB height
     *
     * \return  0 - successful; none 0 - failed
     */
    private static int32_t FmoGenerateSliceGroup(SFmo* pFmo, SPps* kpPps, int32_t kiMbWidth,
        int32_t kiMbHeight, CMemoryAlign* pMa)
    {
        int32_t iNumMb = 0;
        int32_t iErr = 0;
        bool bResolutionChanged = false;

        // the cases we would not like
        if (null == pFmo || null == kpPps)
            return ERR_INFO_INVALID_PARAM;

        iNumMb = kiMbWidth * kiMbHeight;

        if (0 == iNumMb)
            return ERR_INFO_INVALID_PARAM;

        pMa->WelsFree(pFmo->pMbAllocMap, "_fmo->pMbAllocMap");
        pFmo->pMbAllocMap = (uint8_t*)pMa->WelsMallocz((uint32_t)(iNumMb * sizeof(uint8_t)), "_fmo->pMbAllocMap");
        if (null == pFmo->pMbAllocMap) // out of memory
            return ERR_INFO_OUT_OF_MEMORY;

        pFmo->iCountMbNum = iNumMb;

        if (kpPps->uiNumSliceGroups < 2 && iNumMb > 0)
        { // only one slice group, exactly it is single slice based
            memset(pFmo->pMbAllocMap, 0, iNumMb * sizeof(int8_t));   // for safe

            pFmo->iSliceGroupCount = 1;

            return ERR_NONE;
        }

        if (bResolutionChanged || ((int32_t)kpPps->uiSliceGroupMapType != pFmo->iSliceGroupType)
            || ((int32_t)kpPps->uiNumSliceGroups != pFmo->iSliceGroupCount))
        {
            switch (kpPps->uiSliceGroupMapType)
            {
                case 0:
                    iErr = FmoGenerateMbAllocMapType0(pFmo, kpPps);
                    break;
                case 1:
                    iErr = FmoGenerateMbAllocMapType1(pFmo, kpPps, kiMbWidth);
                    break;
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                    // Reserve for others slice group type
                    iErr = 1;
                    break;
                default:
                    return ERR_INFO_UNSUPPORTED_FMOTYPE;
            }
        }

        if (0 == iErr)
        {      // well now
            pFmo->iSliceGroupCount = (int32_t)kpPps->uiNumSliceGroups;
            pFmo->iSliceGroupType = (int32_t)kpPps->uiSliceGroupMapType;
        }
        else
        {
            // Map generation failed (e.g. a rejected/oversized run length). The map was
            // allocated above but this FMO is not marked active, so UninitFmoList would
            // never free it. Release it here to avoid a leak on the rejection path.
            pMa->WelsFree(pFmo->pMbAllocMap, "_fmo->pMbAllocMap");
            pFmo->pMbAllocMap = null;
            pFmo->iCountMbNum = 0;
        }

        return iErr;
    }

    /*!
     * \brief   Initialize Wels Flexible Macroblock Ordering (FMO)
     *
     * \param   pFmo        Wels fmo to be initialized
     * \param   pPps        pps argument
     * \param   kiMbWidth   mb width
     * \param   kiMbHeight  mb height
     *
     * \return  0 - successful; none 0 - failed;
     */
    public static int32_t InitFmo(SFmo* pFmo, SPps* pPps, int32_t kiMbWidth, int32_t kiMbHeight, CMemoryAlign* pMa)
    {
        return FmoGenerateSliceGroup(pFmo, pPps, kiMbWidth, kiMbHeight, pMa);
    }


    /*!
     * \brief   Uninitialize Wels Flexible Macroblock Ordering (FMO) list
     *
     * \param   pFmo        Wels base fmo ptr to be uninitialized
     * \param   kiCnt       count number of PPS per list
     * \param   kiAvail     count available number of PPS in list
     *
     * \return  NONE
     */
    public static void UninitFmoList(SFmo* pFmo, int32_t kiCnt, int32_t kiAvail, CMemoryAlign* pMa)
    {
        SFmo* pIter = pFmo;
        int32_t i = 0;
        int32_t iFreeNodes = 0;

        if (null == pIter || kiAvail <= 0 || kiCnt < kiAvail)
            return;

        while (i < kiCnt)
        {
            if (pIter != null && pIter->bActiveFlag)
            {
                if (null != pIter->pMbAllocMap)
                {
                    pMa->WelsFree(pIter->pMbAllocMap, "pIter->pMbAllocMap");

                    pIter->pMbAllocMap = null;
                }
                pIter->iSliceGroupCount = 0;
                pIter->iSliceGroupType = -1;
                pIter->iCountMbNum = 0;
                pIter->bActiveFlag = false;
                ++iFreeNodes;
                if (iFreeNodes >= kiAvail)
                    break;
            }
            ++pIter;
            ++i;
        }
    }

    /*!
     * \brief   detect parameter sets are changed or not
     *
     * \param   pFmo                fmo context
     * \param   kiCountNumMb        (iMbWidth * iMbHeight) in Sps
     * \param   iSliceGroupType     slice group type if fmo is exactly enabled
     * \param   iSliceGroupCount    slice group count if fmo is exactly enabled
     *
     * \return  true - changed or not initialized yet; false - not change at all
     */
    public static bool FmoParamSetsChanged(SFmo* pFmo, int32_t kiCountNumMb, int32_t kiSliceGroupType,
                                           int32_t kiSliceGroupCount)
    {
        if (null == pFmo)
            return false;

        return ((!pFmo->bActiveFlag)
                || (kiCountNumMb != pFmo->iCountMbNum)
                || (kiSliceGroupType != pFmo->iSliceGroupType)
                || (kiSliceGroupCount != pFmo->iSliceGroupCount));
    }

    /*!
     * \brief   update/insert FMO parameter unit
     *
     * \param   _fmo    FMO context
     * \param   _sps    PSps
     * \param   _pps    PPps
     * \param   pActiveFmoNum   int32_t* [in/out]
     *
     * \return  true - update/insert successfully; false - failed;
     */
    public static int32_t FmoParamUpdate(SFmo* pFmo, SSps* pSps, SPps* pPps, int32_t* pActiveFmoNum, CMemoryAlign* pMa)
    {
        uint32_t kuiMbWidth = pSps->iMbWidth;
        uint32_t kuiMbHeight = pSps->iMbHeight;
        int32_t iRet = ERR_NONE;
        if (FmoParamSetsChanged(pFmo, (int32_t)(kuiMbWidth * kuiMbHeight), (int32_t)pPps->uiSliceGroupMapType, (int32_t)pPps->uiNumSliceGroups))
        {
            iRet = InitFmo(pFmo, pPps, (int32_t)kuiMbWidth, (int32_t)kuiMbHeight, pMa);
            if (iRet != 0)
                return iRet;

            if (!pFmo->bActiveFlag && *pActiveFmoNum < MAX_PPS_COUNT)
            {
                ++(*pActiveFmoNum);
                pFmo->bActiveFlag = true;
            }
        }
        return iRet;
    }

    /*!
     * \brief   Convert kMbXy to slice group idc correspondingly
     *
     * \param   pFmo        Wels fmo context
     * \param   kMbXy       kMbXy to be converted
     *
     * \return  slice group idc - successful; -1 - failed;
     */
    public static int32_t FmoMbToSliceGroup(SFmo* pFmo, int32_t kiMbXy)
    {
        int32_t kiMbNum = pFmo->iCountMbNum;
        uint8_t* kpMbMap = pFmo->pMbAllocMap;

        if (kiMbXy < 0 || kiMbXy >= kiMbNum || kpMbMap == null)
            return -1;

        return kpMbMap[kiMbXy];
    }

    /*!
     * \brief   Get successive mb to be processed with given current kMbXy
     *
     * \param   pFmo            Wels fmo context
     * \param   kMbXy           current kMbXy
     *
     * \return  iNextMb - successful; -1 - failed;
     */
    public static int32_t FmoNextMb(SFmo* pFmo, int32_t kiMbXy)
    {
        int32_t kiTotalMb = pFmo->iCountMbNum;
        uint8_t* kpMbMap = pFmo->pMbAllocMap;
        int32_t iNextMb = kiMbXy;
        uint8_t kuiSliceGroupIdc = (uint8_t)FmoMbToSliceGroup(pFmo, kiMbXy);

        if (kuiSliceGroupIdc == unchecked((uint8_t)(-1)))
            return -1;

        do
        {
            ++iNextMb;
            if (iNextMb >= kiTotalMb)
            {
                iNextMb = -1;
                break;
            }
            if (kpMbMap[iNextMb] == kuiSliceGroupIdc)
            {
                break;
            }
        } while (true);

        // -1: No further MB in this slice (could be end of picture)
        return iNextMb;
    }
}
