//-----------------------------------------------------------------------------
// Filename: WelsFrameWork.cs
//
// Description: Port of codec/processing/src/common/WelsFrameWork.h,
// WelsFrameWork.cpp and the C++ half of WelsFrameWorkEx.cpp (E7): the video
// processor (CVpFrameWork), which checks the pixel maps and dispatches Init,
// Uninit, Process, Get and Set to the method its type names, and
// WelsCreateVpInterface / WelsDestroyVpInterface. CVpFrameWork is a native
// struct that holds its camera content methods by value in place of the
// upstream m_pStgChain of IStrategy pointers, so the virtual dispatch is a
// switch on the method. The IStrategy defaults (Init, Uninit, Flush, Get,
// Set and SpecialFeature return RET_SUCCESS) are kept for the methods that do
// not override them. The colour space method is not supported upstream
// either (a null strategy), so Process gives RET_NOTSUPPORTED. The screen
// content methods (scene change detection for screen, complexity analysis
// for screen, scroll detection) come with Phase 4 and image rotation is not
// ported, so dispatching to them throws NotSupportedException. The C
// interface (IWelsVPc) is not ported. The single threaded port has no use
// for m_mutes.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E7).
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
 * \file         :  WelsFrameWork.h
 *
 * \brief        :  framework of wels video processor class
 *
 * \date         :  2011/01/04
 *
 * \description  :
 *
 *************************************************************************************
 */

using System.Runtime.InteropServices;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    public const int32_t WELSVP_MAJOR_VERSION = 1;
    public const int32_t WELSVP_MINOR_VERSION = 1;
    public const int32_t WELSVP_VERSION = (WELSVP_MAJOR_VERSION << 8) + WELSVP_MINOR_VERSION;
    // The C++ build's value (the C build's is 0x0001 + ...).
    public const int32_t WELSVP_INTERFACE_VERION = 0x8000 + (WELSVP_VERSION & 0x7fff);

    public const int32_t MAX_STRATEGY_NUM = (int32_t)EMethods.METHOD_MASK - 1;

    /* interface API implement */

    public static EResult WelsCreateVpInterface(CVpFrameWork** ppCtx, int iVersion)
    {
        if ((iVersion & 0x8000) != 0)
            return CreateSpecificVpInterface(ppCtx);
        else if ((iVersion & 0x7fff) != 0)
            throw new NotSupportedException("The C interface of the video processor (IWelsVPc) is not ported.");
        else
            return EResult.RET_INVALIDPARAM;
    }

    public static EResult WelsDestroyVpInterface(CVpFrameWork* pCtx, int iVersion)
    {
        if ((iVersion & 0x8000) != 0)
            return DestroySpecificVpInterface(pCtx);
        else if ((iVersion & 0x7fff) != 0)
            throw new NotSupportedException("The C interface of the video processor (IWelsVPc) is not ported.");
        else
            return EResult.RET_INVALIDPARAM;
    }

    ///////////////////////////////////////////////////////////////////////

    public static EResult CreateSpecificVpInterface(CVpFrameWork** ppCtx)
    {
        EResult eReturn = EResult.RET_FAILED;

        CVpFrameWork* pFr = (CVpFrameWork*)NativeMemory.AllocZeroed((nuint)sizeof(CVpFrameWork));
        if (pFr != null)
        {
            pFr->CVpFrameWork_ctor(1, ref eReturn);
            *ppCtx = pFr;
            eReturn = EResult.RET_SUCCESS;
        }

        return eReturn;
    }

    public static EResult DestroySpecificVpInterface(CVpFrameWork* pCtx)
    {
        // delete pCtx
        if (pCtx != null)
        {
            pCtx->CVpFrameWork_dtor();
            NativeMemory.Free(pCtx);
        }

        return EResult.RET_SUCCESS;
    }
}

/// <summary>CVpFrameWork (see the file header).</summary>
internal unsafe struct CVpFrameWork
{
    // IStrategy* m_pStgChain[MAX_STRATEGY_NUM], by value: CreateStrategy (WelsStaticCast (EMethods, i + 1), uiCPUFlag).
    private CDenoiser m_cDenoiser;                                  // METHOD_DENOISE
    private CSceneChangeDetectionVideo m_cSceneChangeDetectionVideo; // METHOD_SCENE_CHANGE_DETECTION_VIDEO
    private CDownsampling m_cDownsampling;                          // METHOD_DOWNSAMPLE
    private CVAACalculation m_cVAACalculation;                      // METHOD_VAA_STATISTICS
    private CBackgroundDetection m_cBackgroundDetection;            // METHOD_BACKGROUND_DETECTION
    private CAdaptiveQuantization m_cAdaptiveQuantization;          // METHOD_ADAPTIVE_QUANT
    private CComplexityAnalysis m_cComplexityAnalysis;              // METHOD_COMPLEXITY_ANALYSIS

    /// <summary>CVpFrameWork::CVpFrameWork (uint32_t uiThreadsNum, EResult&amp; eReturn).</summary>
    public void CVpFrameWork_ctor(uint32_t uiThreadsNum, ref EResult eReturn)
    {
        // WelsCPUFeatureDetect (&iCoreNum): C-only build, no features.
        uint32_t uiCPUFlag = 0;

        m_cDenoiser = new CDenoiser((int32_t)uiCPUFlag);
        m_cSceneChangeDetectionVideo = new CSceneChangeDetectionVideo(EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO, (int32_t)uiCPUFlag);
        m_cDownsampling = new CDownsampling((int32_t)uiCPUFlag);
        m_cVAACalculation = new CVAACalculation((int32_t)uiCPUFlag);
        m_cBackgroundDetection = new CBackgroundDetection((int32_t)uiCPUFlag);
        m_cAdaptiveQuantization = new CAdaptiveQuantization((int32_t)uiCPUFlag);
        m_cComplexityAnalysis = new CComplexityAnalysis((int32_t)uiCPUFlag);

        // WelsMutexInit (&m_mutes): single threaded port.

        eReturn = EResult.RET_SUCCESS;
    }

    /// <summary>CVpFrameWork::~CVpFrameWork.</summary>
    public void CVpFrameWork_dtor()
    {
        for (int32_t i = 0; i < WelsVP.MAX_STRATEGY_NUM; i++)
        {
            EMethods eMethod = (EMethods)(i + 1);
            if (HasStrategy(eMethod))
            {
                Uninit((int32_t)eMethod);
            }
        }
        // delete m_pStgChain[i]: only these two own memory.
        m_cDownsampling.Destroy();
        m_cBackgroundDetection.Destroy();

        // WelsMutexDestroy (&m_mutes): single threaded port.
    }

    public EResult Init(int32_t iType, void* pCfg)
    {
        EResult eReturn = EResult.RET_SUCCESS;
        EMethods eMethod = WelsVP.WelsVpGetValidMethod(iType);

        Uninit(iType);

        // None of the methods overrides IStrategy::Init, which returns RET_SUCCESS.
        if (HasStrategy(eMethod))
            eReturn = EResult.RET_SUCCESS;

        return eReturn;
    }

    public EResult Uninit(int32_t iType)
    {
        EResult eReturn = EResult.RET_SUCCESS;
        EMethods eMethod = WelsVP.WelsVpGetValidMethod(iType);

        // None of the methods overrides IStrategy::Uninit, which returns RET_SUCCESS.
        if (HasStrategy(eMethod))
            eReturn = EResult.RET_SUCCESS;

        return eReturn;
    }

    public EResult Flush(int32_t iType)
    {
        EResult eReturn = EResult.RET_SUCCESS;

        return eReturn;
    }

    public EResult Process(int32_t iType, SPixMap* pSrcPixMap, SPixMap* pDstPixMap)
    {
        EResult eReturn = EResult.RET_NOTSUPPORTED;
        EMethods eMethod = WelsVP.WelsVpGetValidMethod(iType);
        SPixMap sSrcPic = default;
        SPixMap sDstPic = default;

        if (pSrcPixMap != null) sSrcPic = *pSrcPixMap;
        if (pDstPixMap != null) sDstPic = *pDstPixMap;
        if (!CheckValid(eMethod, ref sSrcPic, ref sDstPic))
            return EResult.RET_INVALIDPARAM;

        // pStrategy->Process (0, &sSrcPic, &sDstPic)
        switch (eMethod)
        {
            case EMethods.METHOD_COLORSPACE_CONVERT:
                break;  // null strategy: not supported upstream either
            case EMethods.METHOD_DENOISE:
                eReturn = m_cDenoiser.Process(0, &sSrcPic, &sDstPic);
                break;
            case EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO:
                eReturn = m_cSceneChangeDetectionVideo.Process(0, &sSrcPic, &sDstPic);
                break;
            case EMethods.METHOD_DOWNSAMPLE:
                eReturn = m_cDownsampling.Process(0, &sSrcPic, &sDstPic);
                break;
            case EMethods.METHOD_VAA_STATISTICS:
                eReturn = m_cVAACalculation.Process(0, &sSrcPic, &sDstPic);
                break;
            case EMethods.METHOD_BACKGROUND_DETECTION:
                eReturn = m_cBackgroundDetection.Process(0, &sSrcPic, &sDstPic);
                break;
            case EMethods.METHOD_ADAPTIVE_QUANT:
                eReturn = m_cAdaptiveQuantization.Process(0, &sSrcPic, &sDstPic);
                break;
            case EMethods.METHOD_COMPLEXITY_ANALYSIS:
                eReturn = m_cComplexityAnalysis.Process(0, &sSrcPic, &sDstPic);
                break;
            default:
                throw NotPorted(eMethod);
        }

        return eReturn;
    }

    public EResult Get(int32_t iType, void* pParam)
    {
        EResult eReturn = EResult.RET_SUCCESS;
        EMethods eMethod = WelsVP.WelsVpGetValidMethod(iType);

        if (pParam == null)
            return EResult.RET_INVALIDPARAM;

        // pStrategy->Get (0, pParam)
        switch (eMethod)
        {
            case EMethods.METHOD_COLORSPACE_CONVERT:
                break;  // null strategy
            case EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO:
                eReturn = m_cSceneChangeDetectionVideo.Get(0, pParam);
                break;
            case EMethods.METHOD_ADAPTIVE_QUANT:
                eReturn = m_cAdaptiveQuantization.Get(0, pParam);
                break;
            case EMethods.METHOD_COMPLEXITY_ANALYSIS:
                eReturn = m_cComplexityAnalysis.Get(0, pParam);
                break;
            case EMethods.METHOD_DENOISE:
            case EMethods.METHOD_DOWNSAMPLE:
            case EMethods.METHOD_VAA_STATISTICS:
            case EMethods.METHOD_BACKGROUND_DETECTION:
                eReturn = EResult.RET_SUCCESS;  // IStrategy::Get
                break;
            default:
                throw NotPorted(eMethod);
        }

        return eReturn;
    }

    public EResult Set(int32_t iType, void* pParam)
    {
        EResult eReturn = EResult.RET_SUCCESS;
        EMethods eMethod = WelsVP.WelsVpGetValidMethod(iType);

        if (pParam == null)
            return EResult.RET_INVALIDPARAM;

        // pStrategy->Set (0, pParam)
        switch (eMethod)
        {
            case EMethods.METHOD_COLORSPACE_CONVERT:
                break;  // null strategy
            case EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO:
                eReturn = m_cSceneChangeDetectionVideo.Set(0, pParam);
                break;
            case EMethods.METHOD_VAA_STATISTICS:
                eReturn = m_cVAACalculation.Set(0, pParam);
                break;
            case EMethods.METHOD_BACKGROUND_DETECTION:
                eReturn = m_cBackgroundDetection.Set(0, pParam);
                break;
            case EMethods.METHOD_ADAPTIVE_QUANT:
                eReturn = m_cAdaptiveQuantization.Set(0, pParam);
                break;
            case EMethods.METHOD_COMPLEXITY_ANALYSIS:
                eReturn = m_cComplexityAnalysis.Set(0, pParam);
                break;
            case EMethods.METHOD_DENOISE:
            case EMethods.METHOD_DOWNSAMPLE:
                eReturn = EResult.RET_SUCCESS;  // IStrategy::Set
                break;
            default:
                throw NotPorted(eMethod);
        }

        return eReturn;
    }

    public EResult SpecialFeature(int32_t iType, void* pIn, void* pOut)
    {
        EResult eReturn = EResult.RET_SUCCESS;

        return eReturn;
    }

    /// <summary>
    /// Port addition: whether m_pStgChain holds a strategy for the method. Only the colour
    /// space method is null upstream; the methods this port does not have count as present.
    /// </summary>
    private static bool HasStrategy(EMethods eMethod) => eMethod != EMethods.METHOD_COLORSPACE_CONVERT;

    private static NotSupportedException NotPorted(EMethods eMethod) =>
        new NotSupportedException($"The video processing method {eMethod} is not ported (screen content comes with Phase 4).");

    public static bool CheckValid(EMethods eMethod, ref SPixMap pSrcPixMap, ref SPixMap pDstPixMap)
    {
        bool eReturn = false;

        if (eMethod == EMethods.METHOD_NULL)
            goto exit;

        if (eMethod != EMethods.METHOD_COLORSPACE_CONVERT)
        {
            if ((uint8_t*)pSrcPixMap.pPixel[0] != null)
            {
                if (pSrcPixMap.eFormat != EVideoFormat.VIDEO_FORMAT_I420 && pSrcPixMap.eFormat != EVideoFormat.VIDEO_FORMAT_YV12)
                    goto exit;
            }
            if ((uint8_t*)pSrcPixMap.pPixel[0] != null && (uint8_t*)pDstPixMap.pPixel[0] != null)
            {
                if (pDstPixMap.eFormat != pSrcPixMap.eFormat)
                    goto exit;
            }
        }

        if ((uint8_t*)pSrcPixMap.pPixel[0] != null)
        {
            int32_t kiSrcChromaWidth = pSrcPixMap.sRect.iRectWidth >> 1;
            if (pSrcPixMap.sRect.iRectWidth <= 0 || pSrcPixMap.sRect.iRectHeight <= 0
                || (int64_t)(pSrcPixMap.sRect.iRectWidth) * pSrcPixMap.sRect.iRectHeight > (WelsVP.MAX_MBS_PER_FRAME << 8))
                goto exit;
            if (pSrcPixMap.sRect.iRectTop >= pSrcPixMap.sRect.iRectHeight
                || pSrcPixMap.sRect.iRectLeft >= pSrcPixMap.sRect.iRectWidth || pSrcPixMap.sRect.iRectWidth > pSrcPixMap.iStride[0])
                goto exit;
            if (eMethod == EMethods.METHOD_DOWNSAMPLE
                && (pSrcPixMap.eFormat == EVideoFormat.VIDEO_FORMAT_I420 || pSrcPixMap.eFormat == EVideoFormat.VIDEO_FORMAT_YV12)
                && ((uint8_t*)pSrcPixMap.pPixel[1] == null || (uint8_t*)pSrcPixMap.pPixel[2] == null
                    || pSrcPixMap.iStride[1] <= 0 || pSrcPixMap.iStride[2] <= 0
                    || kiSrcChromaWidth > pSrcPixMap.iStride[1] || kiSrcChromaWidth > pSrcPixMap.iStride[2]))
                goto exit;
        }
        if ((uint8_t*)pDstPixMap.pPixel[0] != null)
        {
            int32_t kiDstChromaWidth = pDstPixMap.sRect.iRectWidth >> 1;
            if (pDstPixMap.sRect.iRectWidth <= 0 || pDstPixMap.sRect.iRectHeight <= 0
                || (int64_t)(pDstPixMap.sRect.iRectWidth) * pDstPixMap.sRect.iRectHeight > (WelsVP.MAX_MBS_PER_FRAME << 8))
                goto exit;
            if (pDstPixMap.sRect.iRectTop >= pDstPixMap.sRect.iRectHeight
                || pDstPixMap.sRect.iRectLeft >= pDstPixMap.sRect.iRectWidth || pDstPixMap.sRect.iRectWidth > pDstPixMap.iStride[0])
                goto exit;
            if (eMethod == EMethods.METHOD_DOWNSAMPLE
                && (pDstPixMap.eFormat == EVideoFormat.VIDEO_FORMAT_I420 || pDstPixMap.eFormat == EVideoFormat.VIDEO_FORMAT_YV12)
                && ((uint8_t*)pDstPixMap.pPixel[1] == null || (uint8_t*)pDstPixMap.pPixel[2] == null
                    || pDstPixMap.iStride[1] <= 0 || pDstPixMap.iStride[2] <= 0
                    || kiDstChromaWidth > pDstPixMap.iStride[1] || kiDstChromaWidth > pDstPixMap.iStride[2]))
                goto exit;
        }
        eReturn = true;

    exit:
        return eReturn;
    }
}
