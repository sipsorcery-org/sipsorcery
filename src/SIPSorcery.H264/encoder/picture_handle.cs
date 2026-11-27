//-----------------------------------------------------------------------------
// Filename: picture_handle.cs
//
// Description: Port of codec/encoder/core/src/picture_handle.cpp: allocate
// and free the encoder's pictures. The screen content feature storage
// (RequestScreenBlockFeatureStorage) comes with the screen content tools, so
// AllocPicture rejects a request for it.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
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

internal static unsafe partial class WelsEnc
{
    /*!
     * \brief   alloc picture pData with borders for each plane based width and height of picture
     * \param   cx              width of picture in pixels
     * \param   cy              height of picture in pixels
     * \param   need_data       need pData allocation
     * \pram    need_expand     need borders expanding
     * \return  successful if effective picture pointer returned, otherwise failed with NULL
     */
    public static SPicture* AllocPicture(CMemoryAlign* pMa, int32_t kiWidth, int32_t kiHeight,
                                         bool bNeedMbInfo, int32_t iNeedFeatureStorage)
    {
        SPicture* pPic = null;
        int32_t iPicWidth = 0;
        int32_t iPicHeight = 0;

        int32_t iPicChromaWidth = 0;
        int32_t iPicChromaHeight = 0;
        int32_t iLumaSize = 0;
        int32_t iChromaSize = 0;

        pPic = (SPicture*)pMa->WelsMallocz((uint32_t)sizeof(SPicture), "pPic");

        if (null == pPic)
        {
            return null;
        }

        iPicWidth = WELS_ALIGN(kiWidth, MB_WIDTH_LUMA) + (PADDING_LENGTH << 1);     // with width of horizon
        iPicHeight = WELS_ALIGN(kiHeight, MB_HEIGHT_LUMA) + (PADDING_LENGTH << 1);  // with height of vertical
        iPicChromaWidth = iPicWidth >> 1;
        iPicChromaHeight = iPicHeight >> 1;
        iPicWidth = WELS_ALIGN(iPicWidth, 32);  // 32(or 16 for chroma below) to match original imp. here instead of cache_line_size
        iPicChromaWidth = WELS_ALIGN(iPicChromaWidth, 16);
        iLumaSize = iPicWidth * iPicHeight;
        iChromaSize = iPicChromaWidth * iPicChromaHeight;

        pPic->pBuffer = (uint8_t*)pMa->WelsMalloc((uint32_t)(iLumaSize /* luma */
                        + (iChromaSize << 1)) /* Cb,Cr */
                        , "pPic->pBuffer");
        if (null == pPic->pBuffer)
        {
            FreePicture(pMa, &pPic);
            return null;
        }
        pPic->iLineSize[0] = iPicWidth;
        pPic->iLineSize[1] = pPic->iLineSize[2] = iPicChromaWidth;
        pPic->pData[0] = pPic->pBuffer + (1 + pPic->iLineSize[0]) * PADDING_LENGTH;
        pPic->pData[1] = pPic->pBuffer + iLumaSize + (((1 + pPic->iLineSize[1]) * PADDING_LENGTH) >> 1);
        pPic->pData[2] = pPic->pBuffer + iLumaSize + iChromaSize + (((1 + pPic->iLineSize[2]) * PADDING_LENGTH) >> 1);

        pPic->iWidthInPixel = kiWidth;
        pPic->iHeightInPixel = kiHeight;
        pPic->iFrameNum = -1;

        pPic->bIsLongRef = false;
        pPic->iLongTermPicNum = -1;
        pPic->uiRecieveConfirmed = 0;
        pPic->iMarkFrameNum = -1;

        if (bNeedMbInfo)
        {
            uint32_t kuiCountMbNum = (uint32_t)(((15 + kiWidth) >> 4) * ((15 + kiHeight) >> 4));

            pPic->uiRefMbType = (uint32_t*)pMa->WelsMallocz(kuiCountMbNum * sizeof(uint32_t), "pPic->uiRefMbType");
            if (null == pPic->uiRefMbType)
            {
                FreePicture(pMa, &pPic);
                return null;
            }

            pPic->pRefMbQp = (uint8_t*)pMa->WelsMallocz(kuiCountMbNum * sizeof(uint8_t), "pPic->pRefMbQp");
            if (null == pPic->pRefMbQp)
            {
                FreePicture(pMa, &pPic);
                return null;
            }

            pPic->sMvList = (SMVUnitXY*)pMa->WelsMallocz(kuiCountMbNum * (uint32_t)sizeof(SMVUnitXY), "pPic->sMvList");
            if (null == pPic->sMvList)
            {
                FreePicture(pMa, &pPic);
                return null;
            }

            pPic->pMbSkipSad = (int32_t*)pMa->WelsMallocz(kuiCountMbNum * sizeof(int32_t), "pPic->pMbSkipSad");
            if (null == pPic->pMbSkipSad)
            {
                FreePicture(pMa, &pPic);
                return null;
            }
        }

        if (iNeedFeatureStorage != 0)
        {
            // RequestScreenBlockFeatureStorage comes with the screen content tools.
            FreePicture(pMa, &pPic);
            throw new NotSupportedException("AllocPicture: screen content feature storage is not supported by this port.");
        }
        else
        {
            pPic->pScreenBlockFeatureStorage = null;
        }
        return pPic;
    }

    /*!
     * \brief   free picture pData planes
     * \param   pPic        picture pointer to be destoryed
     * \return  none
     */
    public static void FreePicture(CMemoryAlign* pMa, SPicture** ppPic)
    {
        if (null != ppPic && null != *ppPic)
        {
            SPicture* pPic = *ppPic;

            if (null != pPic->pBuffer)
            {
                pMa->WelsFree(pPic->pBuffer, "pPic->pBuffer");
                pPic->pBuffer = null;
            }
            pPic->pBuffer = null;
            pPic->pData[0] =
              pPic->pData[1] =
                pPic->pData[2] = null;
            pPic->iLineSize[0] =
              pPic->iLineSize[1] =
                pPic->iLineSize[2] = 0;

            pPic->iWidthInPixel = 0;
            pPic->iHeightInPixel = 0;
            pPic->iFrameNum = -1;

            pPic->bIsLongRef = false;
            pPic->uiRecieveConfirmed = 0;
            pPic->iLongTermPicNum = -1;
            pPic->iMarkFrameNum = -1;

            if (pPic->uiRefMbType != null)
            {
                pMa->WelsFree(pPic->uiRefMbType, "pPic->uiRefMbType");
                pPic->uiRefMbType = null;
            }
            if (pPic->pRefMbQp != null)
            {
                pMa->WelsFree(pPic->pRefMbQp, "pPic->pRefMbQp");
                pPic->pRefMbQp = null;
            }

            if (pPic->sMvList != null)
            {
                pMa->WelsFree(pPic->sMvList, "pPic->sMvList");
                pPic->sMvList = null;
            }
            if (pPic->pMbSkipSad != null)
            {
                pMa->WelsFree(pPic->pMbSkipSad, "pPic->pMbSkipSad");
                pPic->pMbSkipSad = null;
            }

            // pScreenBlockFeatureStorage is never allocated by this port (see AllocPicture).

            pMa->WelsFree(*ppPic, "pPic");
            *ppPic = null;
        }
    }
}
