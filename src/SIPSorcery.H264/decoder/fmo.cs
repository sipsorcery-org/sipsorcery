//-----------------------------------------------------------------------------
// Filename: fmo.cs
//
// Description: Port of codec/decoder/core/inc/fmo.h and src/fmo.cpp: Flexible
// Macroblock Ordering.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// So far only the SFmo type and UninitFmoList, which the parameter set parser
// needs (ResetFmoList). The rest of fmo.cpp comes with slice decoding.
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
}
