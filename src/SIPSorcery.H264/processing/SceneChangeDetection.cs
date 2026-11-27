//-----------------------------------------------------------------------------
// Filename: SceneChangeDetection.cs
//
// Description: Port of codec/processing/src/scenechangedetection/
// SceneChangeDetection.h and SceneChangeDetection.cpp, camera half (E7): the
// video scene change detector, which counts the 8x8 blocks whose SAD against
// the reference is above HIGH_MOTION_BLOCK_THRESHOLD, and the strategy that
// turns the count into a scene change level. The template
// CSceneChangeDetection<CSceneChangeDetectorVideo> is the native struct
// CSceneChangeDetectionVideo, which holds its detector by value as the
// template does. The screen content detector
// (CSceneChangeDetection<CSceneChangeDetectorScreen>) comes with Phase 4.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E7).
// 27 Nov 2026  Claude          The SIMD 8x8 SAD (P5.2).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2011-2013, Cisco Systems
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
 * \file         :  SceneChangeDetection.h
 *
 * \brief        :  scene change detection class of wels video processor class
 *
 * \date         :  2011/03/14
 *
 * \description  :  1. rewrite the package code of scene change detection class
 *
 *************************************************************************************
 */

// typedef int32_t (SadFunc) (uint8_t* pSrcY, int32_t iSrcStrideY, uint8_t* pRefY, int32_t iRefStrideY);
// typedef SadFunc* SadFuncPtr; (common.h)
global using unsafe SadFuncPtr = delegate*<byte*, int, byte*, int, int>;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    public const int32_t HIGH_MOTION_BLOCK_THRESHOLD = 320;
    public const float SCENE_CHANGE_MOTION_RATIO_LARGE_VIDEO = 0.85f;
    public const float SCENE_CHANGE_MOTION_RATIO_MEDIUM = 0.50f;
    public const float SCENE_CHANGE_MOTION_RATIO_LARGE_SCREEN = 0.80f;
}

/// <summary>SLocalParam.</summary>
internal unsafe struct SLocalParam
{
    public int32_t iWidth;
    public int32_t iHeight;
    public int32_t iBlock8x8Width;
    public int32_t iBlock8x8Height;
    public uint8_t* pRefY;
    public uint8_t* pCurY;
    public int32_t iRefStride;
    public int32_t iCurStride;
    public uint8_t* pStaticBlockIdc;
}

/// <summary>
/// CSceneChangeDetectorVideo. Upstream it holds a reference to its strategy's
/// SSceneChangeResult (m_sParam); here the strategy passes it to Detect by reference.
/// </summary>
internal unsafe struct CSceneChangeDetectorVideo
{
    private SadFuncPtr m_pfSad;
    private float m_fSceneChangeMotionRatioLarge;
    private float m_fSceneChangeMotionRatioMedium;

    public CSceneChangeDetectorVideo(int32_t iCpuFlag)
    {
        m_pfSad = &WelsSampleSad8x8_c;
        // In place of the X86_ASM and HAVE_NEON versions: the SIMD kernel (P5.2), which gives
        // the C kernel's results. The MMI and LASX versions have no counterpart.
        if (WelsCpuHasSimd128((uint32_t)iCpuFlag))
        {
            m_pfSad = &WelsSampleSad8x8_simd;
        }
        m_fSceneChangeMotionRatioLarge = WelsVP.SCENE_CHANGE_MOTION_RATIO_LARGE_VIDEO;
        m_fSceneChangeMotionRatioMedium = WelsVP.SCENE_CHANGE_MOTION_RATIO_MEDIUM;
    }

    /// <summary>void operator() (SLocalParam&amp; sLocalParam).</summary>
    public void Detect(ref SLocalParam sLocalParam, ref SSceneChangeResult m_sParam)
    {
        int32_t iRefRowStride = 0, iCurRowStride = 0;
        uint8_t* pRefY = sLocalParam.pRefY;
        uint8_t* pCurY = sLocalParam.pCurY;
        uint8_t* pRefTmp = null, pCurTmp = null;

        iRefRowStride = sLocalParam.iRefStride << 3;
        iCurRowStride = sLocalParam.iCurStride << 3;

        for (int32_t j = 0; j < sLocalParam.iBlock8x8Height; j++)
        {
            pRefTmp = pRefY;
            pCurTmp = pCurY;
            for (int32_t i = 0; i < sLocalParam.iBlock8x8Width; i++)
            {
                int32_t iSad = m_pfSad(pCurTmp, sLocalParam.iCurStride, pRefTmp, sLocalParam.iRefStride);
                m_sParam.iMotionBlockNum += iSad > WelsVP.HIGH_MOTION_BLOCK_THRESHOLD ? 1 : 0;
                pRefTmp += 8;
                pCurTmp += 8;
            }
            pRefY += iRefRowStride;
            pCurY += iCurRowStride;
        }
    }

    public readonly float GetSceneChangeMotionRatioLarge() => m_fSceneChangeMotionRatioLarge;

    public readonly float GetSceneChangeMotionRatioMedium() => m_fSceneChangeMotionRatioMedium;
}

/// <summary>CSceneChangeDetection&lt;CSceneChangeDetectorVideo&gt; (see the file header).</summary>
internal unsafe struct CSceneChangeDetectionVideo
{
    public EMethods m_eMethod;      // IStrategy

    private SSceneChangeResult m_sSceneChangeParam;
    private SLocalParam m_sLocalParam;
    private CSceneChangeDetectorVideo m_cDetector;

    public CSceneChangeDetectionVideo(EMethods eMethod, int32_t iCpuFlag)
    {
        m_cDetector = new CSceneChangeDetectorVideo(iCpuFlag);
        m_eMethod = eMethod;
        m_sSceneChangeParam = default;
        m_sLocalParam = default;
    }

    public EResult Process(int32_t iType, SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        EResult eReturn = EResult.RET_INVALIDPARAM;

        m_sLocalParam.iWidth = pSrcPixMap->sRect.iRectWidth;
        m_sLocalParam.iHeight = pSrcPixMap->sRect.iRectHeight;
        m_sLocalParam.iBlock8x8Width = m_sLocalParam.iWidth >> 3;
        m_sLocalParam.iBlock8x8Height = m_sLocalParam.iHeight >> 3;
        m_sLocalParam.pRefY = pRefPixMap->pPixel[0];
        m_sLocalParam.pCurY = pSrcPixMap->pPixel[0];
        m_sLocalParam.iRefStride = pRefPixMap->iStride[0];
        m_sLocalParam.iCurStride = pSrcPixMap->iStride[0];
        m_sLocalParam.pStaticBlockIdc = m_sSceneChangeParam.pStaticBlockIdc;

        int32_t iBlock8x8Num = m_sLocalParam.iBlock8x8Width * m_sLocalParam.iBlock8x8Height;
        // float * int + 0.5f in float, then + PESN (a double) in double, as C evaluates it.
        int32_t iSceneChangeThresholdLarge = (int32_t)(
            (double)(m_cDetector.GetSceneChangeMotionRatioLarge() * iBlock8x8Num + 0.5f) + WelsVP.PESN);
        int32_t iSceneChangeThresholdMedium = (int32_t)(
            (double)(m_cDetector.GetSceneChangeMotionRatioMedium() * iBlock8x8Num + 0.5f) + WelsVP.PESN);

        m_sSceneChangeParam.iMotionBlockNum = 0;
        m_sSceneChangeParam.iFrameComplexity = 0;
        m_sSceneChangeParam.eSceneChangeIdc = SIMILAR_SCENE;

        m_cDetector.Detect(ref m_sLocalParam, ref m_sSceneChangeParam);

        if (m_sSceneChangeParam.iMotionBlockNum >= iSceneChangeThresholdLarge)
        {
            m_sSceneChangeParam.eSceneChangeIdc = LARGE_CHANGED_SCENE;
        }
        else if (m_sSceneChangeParam.iMotionBlockNum >= iSceneChangeThresholdMedium)
        {
            m_sSceneChangeParam.eSceneChangeIdc = MEDIUM_CHANGED_SCENE;
        }

        eReturn = EResult.RET_SUCCESS;

        return eReturn;
    }

    public readonly EResult Get(int32_t iType, void* pParam)
    {
        if (pParam == null)
        {
            return EResult.RET_INVALIDPARAM;
        }
        *(SSceneChangeResult*)pParam = m_sSceneChangeParam;
        return EResult.RET_SUCCESS;
    }

    public EResult Set(int32_t iType, void* pParam)
    {
        if (pParam == null)
        {
            return EResult.RET_INVALIDPARAM;
        }
        m_sSceneChangeParam = *(SSceneChangeResult*)pParam;
        return EResult.RET_SUCCESS;
    }
}
