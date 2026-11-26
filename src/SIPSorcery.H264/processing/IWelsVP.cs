//-----------------------------------------------------------------------------
// Filename: IWelsVP.cs
//
// Description: Port of codec/processing/interface/IWelsVP.h, part 1: the VAA
// result and adaptive quantisation structs the encoder's SVAAFrameInfo
// embeds, and the scene change levels (E5), and the result codes, the pixel
// map, the method ids and the VAA statistics parameters (E5b). The video
// processing interface and the other methods' parameters follow with the
// preprocessing in E7.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5).
// 26 Nov 2026  Claude          EResult, EVideoFormat, SRect, SPixMap, EMethods and SVAACalcParam (E5b).
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

/// <summary>EResult.</summary>
internal enum EResult
{
    RET_SUCCESS = 0,
    RET_FAILED = -1,
    RET_INVALIDPARAM = -2,
    RET_OUTOFMEMORY = -3,
    RET_NOTSUPPORTED = -4,
    RET_UNEXPECTED = -5,
    RET_NEEDREINIT = -6
}

/// <summary>EVideoFormat.</summary>
internal enum EVideoFormat : uint
{
    VIDEO_FORMAT_NULL = 0,   /* invalid format   */
    /*rgb color formats*/
    VIDEO_FORMAT_RGB = 1,   /* rgb 24bits       */
    VIDEO_FORMAT_RGBA = 2,   /* rgba             */
    VIDEO_FORMAT_RGB555 = 3,   /* rgb555           */
    VIDEO_FORMAT_RGB565 = 4,   /* rgb565           */
    VIDEO_FORMAT_BGR = 5,   /* bgr 24bits       */
    VIDEO_FORMAT_BGRA = 6,   /* bgr 32bits       */
    VIDEO_FORMAT_ABGR = 7,   /* abgr             */
    VIDEO_FORMAT_ARGB = 8,   /* argb             */

    /*yuv color formats*/
    VIDEO_FORMAT_YUY2 = 20,   /* yuy2             */
    VIDEO_FORMAT_YVYU = 21,   /* yvyu             */
    VIDEO_FORMAT_UYVY = 22,   /* uyvy             */
    VIDEO_FORMAT_I420 = 23,   /* yuv 4:2:0 planar */
    VIDEO_FORMAT_YV12 = 24,   /* yuv 4:2:0 planar */
    VIDEO_FORMAT_INTERNAL = 25,   /* Only Used for SVC decoder testbed */
    VIDEO_FORMAT_NV12 = 26,   /* y planar + uv packed */
    VIDEO_FORMAT_I422 = 27,   /* yuv 4:2:2 planar */
    VIDEO_FORMAT_I444 = 28,   /* yuv 4:4:4 planar */
    VIDEO_FORMAT_YUYV = 20,   /* yuv 4:2:2 packed */

    VIDEO_FORMAT_RGB24 = 1,
    VIDEO_FORMAT_RGB32 = 2,
    VIDEO_FORMAT_RGB24_INV = 5,
    VIDEO_FORMAT_RGB32_INV = 6,
    VIDEO_FORMAT_RGB555_INV = 7,
    VIDEO_FORMAT_RGB565_INV = 8,
    VIDEO_FORMAT_YUV2 = 21,
    VIDEO_FORMAT_420 = 23,

    VIDEO_FORMAT_VFlip = 0x80000000
}

/// <summary>EPixMapBufferProperty.</summary>
internal enum EPixMapBufferProperty
{
    BUFFER_HOSTMEM = 0,
    BUFFER_SURFACE
}

/// <summary>SRect.</summary>
internal struct SRect
{
    public int iRectTop;
    public int iRectLeft;
    public int iRectWidth;
    public int iRectHeight;
}

/// <summary>SPixMap.</summary>
internal unsafe struct SPixMap
{
    public PtrArray3<uint8_t> pPixel;   // void* pPixel[3]
    public int iSizeInBits;
    public fixed int iStride[3];
    public SRect sRect;
    public EVideoFormat eFormat;
    public EPixMapBufferProperty eProperty;//not use? to remove? but how about the size of SPixMap?
}

/// <summary>EMethods.</summary>
internal enum EMethods
{
    METHOD_NULL = 0,
    METHOD_COLORSPACE_CONVERT,//not support yet
    METHOD_DENOISE,
    METHOD_SCENE_CHANGE_DETECTION_VIDEO,
    METHOD_SCENE_CHANGE_DETECTION_SCREEN,
    METHOD_DOWNSAMPLE,
    METHOD_VAA_STATISTICS,
    METHOD_BACKGROUND_DETECTION,
    METHOD_ADAPTIVE_QUANT,
    METHOD_COMPLEXITY_ANALYSIS,
    METHOD_COMPLEXITY_ANALYSIS_SCREEN,
    METHOD_IMAGE_ROTATE,
    METHOD_SCROLL_DETECTION,
    METHOD_MASK
}

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

/// <summary>SVAACalcParam.</summary>
internal unsafe struct SVAACalcParam
{
    public int iCalcVar;
    public int iCalcBgd;
    public int iCalcSsd;
    public int iReserved;
    public SVAACalcResult* pCalcResult;
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
