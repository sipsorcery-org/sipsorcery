//-----------------------------------------------------------------------------
// Filename: svc_mode_decision.cs
//
// Description: Port of codec/encoder/core/src/svc_mode_decision.cpp, part 1
// (E4): the background detection update that the I-slice loop calls
// (WelsMdUpdateBGDInfo and WelsMdUpdateBGDInfoNULL). The enhancement layer,
// background skip, screen content and mode merge decisions follow with P
// slices in E5.
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
    public static void WelsMdUpdateBGDInfo(SDqLayer* pCurLayer, SMB* pCurMb, bool bCollocatedPredFlag,
                                           int32_t iRefPictureType)
    {
        uint8_t* pTargetRefMbQpList = (pCurLayer->pDecPic->pRefMbQp);
        int32_t kiMbXY = pCurMb->iMbXY;

        if (pCurMb->uiCbp != 0 || (int32_t)I_SLICE == iRefPictureType || !bCollocatedPredFlag)
        {
            pTargetRefMbQpList[kiMbXY] = pCurMb->uiLumaQp;
        }
        else
        { //unchange, do not need to evaluation?
            uint8_t* pRefPicRefMbQpList = (pCurLayer->pRefPic->pRefMbQp);
            pTargetRefMbQpList[kiMbXY] = pRefPicRefMbQpList[kiMbXY];
        }

        if (pCurMb->uiMbType == MB_TYPE_BACKGROUND)
        {
            pCurMb->uiMbType = MB_TYPE_SKIP;
        }
    }

    public static void WelsMdUpdateBGDInfoNULL(SDqLayer* pCurLayer, SMB* pCurMb, bool bCollocatedPredFlag,
                                               int32_t iRefPictureType)
    {
        WelsMdUpdateBGDInfo(pCurLayer, pCurMb, bCollocatedPredFlag, iRefPictureType);
    }
}
