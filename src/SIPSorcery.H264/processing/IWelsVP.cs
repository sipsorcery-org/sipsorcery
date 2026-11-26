//-----------------------------------------------------------------------------
// Filename: IWelsVP.cs
//
// Description: Port of codec/processing/interface/IWelsVP.h, part 1 (E5): the
// VAA result and adaptive quantisation structs the encoder's SVAAFrameInfo
// embeds, and the scene change levels. The video processing interface and
// methods follow with the preprocessing in E7.
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
 *     Copyright (c)  2004-2013, Cisco Systems
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
 * \file    IWelsVP.h
 *
 * \brief   Interface of video pre-process plugins
 *
 * \date    11/02/2009 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264;

/// <summary>ESceneChangeIdc.</summary>
internal enum ESceneChangeIdc
{
    SIMILAR_SCENE,          //similar scene
    MEDIUM_CHANGED_SCENE,   //medium changed scene
    LARGE_CHANGED_SCENE     //large changed scene
}

/// <summary>EStaticBlockIdc.</summary>
internal enum EStaticBlockIdc
{
    NO_STATIC,          // motion block
    COLLOCATED_STATIC,  // collocated static block
    SCROLLED_STATIC,    // scrolled static block
    BLOCK_STATIC_IDC_ALL
}

/// <summary>SVAACalcResult.</summary>
internal unsafe struct SVAACalcResult
{
    public byte* pCurY;             // Y data of current frame
    public byte* pRefY;             // Y data of pRef frame for diff calc
    public int* pSad8x8;            // int (*pSad8x8)[4]: sad of 8x8, every 4 in the same 16x16 get together
    public int* pSsd16x16;          // sum of square difference of 16x16
    public int* pSum16x16;          // sum of 16x16
    public int* pSumOfSquare16x16;  // sum of square of 16x16
    public int* pSumOfDiff8x8;      // int (*pSumOfDiff8x8)[4]
    public byte* pMad8x8;           // unsigned char (*pMad8x8)[4]
    public int iFrameSad;           // sad of frame
}

/// <summary>SMotionTextureUnit.</summary>
internal struct SMotionTextureUnit
{
    public ushort uiMotionIndex;
    public ushort uiTextureIndex;
}

/// <summary>SAdaptiveQuantizationParam.</summary>
internal unsafe struct SAdaptiveQuantizationParam
{
    public int iAdaptiveQuantMode; // 0:quality mode, 1:bitrates mode
    public SVAACalcResult* pCalcResult;
    public SMotionTextureUnit* pMotionTextureUnit;

    public sbyte* pMotionTextureIndexToDeltaQp;
    public int iAverMotionTextureIndexToDeltaQp; // *AQ_STEP_INT_MULTIPLY
}

/// <summary>SComplexityAnalysisParam.</summary>
internal unsafe struct SComplexityAnalysisParam
{
    public int iComplexityAnalysisMode;
    public int iCalcBgd;
    public int iMbNumInGom;
    public long iFrameComplexity;
    public int* pGomComplexity;
    public int* pGomForegroundBlockNum;
    public sbyte* pBackgroundMbFlag;
    public uint* uiRefMbType;
    public SVAACalcResult* pCalcResult;
}
