//-----------------------------------------------------------------------------
// Filename: wels_preprocess.cs
//
// Description: Port of codec/encoder/core/inc/wels_preprocess.h, part 1 (E5):
// SVAAFrameInfo, the VAA state the mode decision and the rate control read.
// The screen content extension (SVAAFrameInfoExt) and CWelsPreProcess follow
// with the preprocessing in E7.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5).
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
 * \file    wels_preprocess.h
 *
 * \brief   interface of video pre-process plugins
 *
 * \date    20110331 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Encoder;

/// <summary>TagVAAFrameInfo.</summary>
internal unsafe struct SVAAFrameInfo
{
    public SVAACalcResult sVaaCalcInfo;
    public SAdaptiveQuantizationParam sAdaptiveQuantParam;
    public SComplexityAnalysisParam sComplexityAnalysisParam;

    public int32_t iPicWidth;          // maximal iWidth of picture in samples for svc coding
    public int32_t iPicHeight;         // maximal iHeight of picture in samples for svc coding
    public int32_t iPicStride;         //luma
    public int32_t iPicStrideUV;

    public uint8_t* pRefY; //pRef
    public uint8_t* pCurY; //cur
    public uint8_t* pRefU; //pRef
    public uint8_t* pCurU; //cur
    public uint8_t* pRefV; //pRef
    public uint8_t* pCurV; //cur

    public int8_t* pVaaBackgroundMbFlag;
    public uint8_t uiValidLongTermPicIdx;
    public uint8_t uiMarkLongTermPicIdx;

    public ESceneChangeIdc eSceneChangeIdc;
    public bool bSceneChangeFlag;
    public bool bIdrPeriodFlag;
}
