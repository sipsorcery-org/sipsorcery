//-----------------------------------------------------------------------------
// Filename: param_svc.cs
//
// Description: Port of codec/encoder/core/inc/param_svc.h: the encoder's
// internal coding parameters (SWelsSvcCodingParam), their defaults and the
// transcoding from the public SEncParamBase/SEncParamExt.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// SWelsSvcCodingParam derives from SEncParamExt in C++. C# structs cannot
// inherit, so the SEncParamExt fields are repeated first, in the same order
// and with the same types, and the derived fields follow. That keeps the
// C++ casts working: an SWelsSvcCodingParam* is usable as an SEncParamExt*
// (see Base() and the layout test in the unit tests). ENABLE_FRAME_DUMP is
// not defined upstream, so SSpatialLayerInternal has no sRecFileName.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (E1).
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

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Encoder;

/*
 *  Dependency Layer Parameter
 */
/// <summary>TagDLayerParam.</summary>
internal unsafe struct SSpatialLayerInternal
{
    public int32_t iActualWidth;            // input source picture actual width
    public int32_t iActualHeight;           // input source picture actual height
    public int32_t iTemporalResolution;
    public int32_t iDecompositionStages;
    public fixed uint8_t uiCodingIdx2TemporalId[(1 << WelsEnc.MAX_TEMPORAL_LEVEL) + 1];

    public int8_t iHighestTemporalId;
    public float fInputFrameRate;           // input frame rate
    public float fOutputFrameRate;          // output frame rate
    public uint16_t uiIdrPicId;             // IDR picture id: [0, 65535], this one is used for LTR
    public int32_t iCodingIndex;
    public int32_t iFrameIndex;             // count how many frames elapsed during coding context currently
    public bool bEncCurFrmAsIdrFlag;
    public int32_t iFrameNum;               // current frame number coding
    public int32_t iPOC;                    // frame iPOC
}

/// <summary>The anonymous struct of TagWelsSvcCodingParam::SUsedPicRect.</summary>
internal struct SUsedPicRectT
{
    public int32_t iLeft;
    public int32_t iTop;
    public int32_t iWidth;
    public int32_t iHeight;
}

/*
 *  Cisco OpenH264 Encoder Parameter Configuration
 */
/// <summary>TagWelsSvcCodingParam: SEncParamExt.</summary>
internal unsafe struct SWelsSvcCodingParam
{
    // ---- SEncParamExt (base class), same order and types as api/codec_app_def.cs ----
    public EUsageType iUsageType;
    public int iPicWidth;
    public int iPicHeight;
    public int iTargetBitrate;
    public RC_MODES iRCMode;
    public float fMaxFrameRate;
    public int iTemporalLayerNum;
    public int iSpatialLayerNum;
    public InlineArray4<SSpatialLayerConfig> sSpatialLayers;
    public ECOMPLEXITY_MODE iComplexityMode;
    public uint uiIntraPeriod;
    public int iNumRefFrame;
    public EParameterSetStrategy eSpsPpsIdStrategy;
    public bool bPrefixNalAddingCtrl;
    public bool bEnableSSEI;
    public bool bSimulcastAVC;
    public int iPaddingFlag;
    public int iEntropyCodingModeFlag;
    public bool bEnableFrameSkip;
    public int iMaxBitrate;
    public int iMaxQp;
    public int iMinQp;
    public uint uiMaxNalSize;
    public bool bEnableLongTermReference;
    public int iLTRRefNum;
    public uint iLtrMarkPeriod;
    public ushort iMultipleThreadIdc;
    public bool bUseLoadBalancing;
    public int iLoopFilterDisableIdc;
    public int iLoopFilterAlphaC0Offset;
    public int iLoopFilterBetaOffset;
    public bool bEnableDenoise;
    public bool bEnableBackgroundDetection;
    public bool bEnableAdaptiveQuant;
    public bool bEnableFrameCroppingFlag;
    public bool bEnableSceneChangeDetect;
    public bool bIsLosslessLink;
    public bool bFixRCOverShoot;
    public int iIdrBitrateRatio;
    public bool bPsnrY;
    public bool bPsnrU;
    public bool bPsnrV;
    // ---- end of SEncParamExt ----

    public InlineArray4<SSpatialLayerInternal> sDependencyLayers; // [MAX_DEPENDENCY_LAYER]

    /* General */
    public uint32_t uiGopSize;              // GOP size (at maximal frame rate: 16)
    public SUsedPicRectT SUsedPicRect;      // the rect in input picture that encoder actually used

    public sbyte* pCurPath; // record current lib path such as:/pData/pData/com.wels.enc/lib/

    public bool bDeblockingParallelFlag;    // deblocking filter parallelization control flag
    public int32_t iBitsVaryPercentage;

    public int8_t iDecompStages;            // GOP size dependency
    public int32_t iMaxNumRefFrame;

    // Port addition: the SEncParamExt base subobject (C++ upcast).
    [UnscopedRef]
    public ref SEncParamExt Base() => ref Unsafe.As<SWelsSvcCodingParam, SEncParamExt>(ref this);

    // TagWelsSvcCodingParam() { FillDefault(); } -- C# structs are zeroed instead; callers that
    // construct one call FillDefault() as the C++ constructor does.

    public static void FillDefault(ref SEncParamExt param)
    {
        param = default; // memset (&param, 0, sizeof (param));
        param.uiIntraPeriod = 0;                    // intra period (multiple of GOP size as desired)
        param.iNumRefFrame = AUTO_REF_PIC_COUNT;// number of reference frame used

        param.iPicWidth = 0;    //   actual input picture width
        param.iPicHeight = 0;    //   actual input picture height

        param.fMaxFrameRate = WelsEnc.MAX_FRAME_RATE;       // maximal frame rate [Hz / fps]

        param.iComplexityMode = LOW_COMPLEXITY;
        param.iTargetBitrate = UNSPECIFIED_BIT_RATE; // overall target bitrate introduced in RC module
        param.iMaxBitrate = UNSPECIFIED_BIT_RATE;
        param.iMultipleThreadIdc = 1;
        param.bUseLoadBalancing = true;

        param.iLTRRefNum = 0;
        param.iLtrMarkPeriod = 30;   //the min distance of two int32_t references

        param.bEnableSSEI = false;
        param.bSimulcastAVC = false;
        param.bEnableFrameCroppingFlag = true; // enable frame cropping flag: true alwayse in application
        // false: Streaming Video Sharing; true: Video Conferencing Meeting;

        /* Deblocking loop filter */
        param.iLoopFilterDisableIdc = 0;    // 0: on, 1: off, 2: on except for slice boundaries
        param.iLoopFilterAlphaC0Offset = 0;    // AlphaOffset: valid range [-6, 6], default 0
        param.iLoopFilterBetaOffset = 0;    // BetaOffset:  valid range [-6, 6], default 0

        /* Rate Control */
        param.iRCMode = RC_QUALITY_MODE;
        param.iPaddingFlag = 0;
        param.iEntropyCodingModeFlag = 0;
        param.bEnableDenoise = false;        // denoise control
        param.bEnableSceneChangeDetect = true;         // scene change detection control
        param.bEnableBackgroundDetection = true;         // background detection control
        param.bEnableAdaptiveQuant = true;         // adaptive quantization control
        param.bEnableFrameSkip = true;         // frame skipping
        param.bEnableLongTermReference = false;        // long term reference control
        param.eSpsPpsIdStrategy = INCREASING_ID;// pSps pPps id addition control
        param.bPrefixNalAddingCtrl = false;        // prefix NAL adding control
        param.iSpatialLayerNum = 1;            // number of dependency(Spatial/CGS) layers used to be encoded
        param.iTemporalLayerNum = 1;            // number of temporal layer specified

        param.iMaxQp = WelsEnc.QP_MAX_VALUE;
        param.iMinQp = WelsEnc.QP_MIN_VALUE;
        param.iUsageType = CAMERA_VIDEO_REAL_TIME;
        param.uiMaxNalSize = 0;
        param.bIsLosslessLink = false;
        param.bFixRCOverShoot = true;
        param.iIdrBitrateRatio = WelsEnc.IDR_BITRATE_RATIO * 100;
        param.bPsnrY = false;
        param.bPsnrU = false;
        param.bPsnrV = false;
        for (int32_t iLayer = 0; iLayer < MAX_SPATIAL_LAYER_NUM; iLayer++)
        {
            ref SSpatialLayerConfig layer = ref param.sSpatialLayers[iLayer];
            layer.uiProfileIdc = PRO_UNKNOWN;
            layer.uiLevelIdc = LEVEL_UNKNOWN;
            layer.iDLayerQp = WelsEnc.SVC_QUALITY_BASE_QP;
            layer.fFrameRate = param.fMaxFrameRate;

            layer.iMaxSpatialBitrate = UNSPECIFIED_BIT_RATE;

            layer.sSliceArgument.uiSliceMode = SM_SINGLE_SLICE;
            layer.sSliceArgument.uiSliceNum = 0; //AUTO, using number of CPU cores
            layer.sSliceArgument.uiSliceSizeConstraint = 1500;

            layer.bAspectRatioPresent = false; // do not write any of the following information to the header
            layer.eAspectRatio = ASP_UNSPECIFIED;
            layer.sAspectRatioExtWidth = 0;
            layer.sAspectRatioExtHeight = 0;

            const int32_t kiLesserSliceNum = ((WelsEnc.MAX_SLICES_NUM < MAX_SLICES_NUM_TMP) ? WelsEnc.MAX_SLICES_NUM : MAX_SLICES_NUM_TMP);
            for (int32_t idx = 0; idx < kiLesserSliceNum; idx++)
                layer.sSliceArgument.uiSliceMbNum[idx] = 0; //default, using one row a slice if uiSliceMode is SM_RASTER_MODE

            // See codec_app_def.h for more info about members bVideoSignalTypePresent through uiColorMatrix.  The default values
            // used below preserve the previous behavior; i.e., no additional information will be written to the output file.
            layer.bVideoSignalTypePresent = false;          // do not write any of the following information to the header
            layer.uiVideoFormat = (byte)VF_UNDEF;           // undefined
            layer.bFullRange = false;                       // analog video data range [16, 235]
            layer.bColorDescriptionPresent = false;         // do not write any of the following three items to the header
            layer.uiColorPrimaries = (byte)CP_UNDEF;        // undefined
            layer.uiTransferCharacteristics = (byte)TRC_UNDEF; // undefined
            layer.uiColorMatrix = (byte)CM_UNDEF;           // undefined
        }
    }

    public void FillDefault()
    {
        FillDefault(ref Base());
        uiGopSize = 1;                      // GOP size (at maximal frame rate: 16)
        iMaxNumRefFrame = AUTO_REF_PIC_COUNT;
        SUsedPicRect.iLeft =
          SUsedPicRect.iTop =
            SUsedPicRect.iWidth =
              SUsedPicRect.iHeight = 0;     // the rect in input picture that encoder actually used

        pCurPath = null; // record current lib path such as:/pData/pData/com.wels.enc/lib/

        bDeblockingParallelFlag = false;// deblocking filter parallelization control flag

        iDecompStages = 0;    // GOP size dependency, unknown here and be revised later
        iBitsVaryPercentage = 10;
    }

    public int32_t ParamBaseTranscode(in SEncParamBase pCodingParam)
    {
        fMaxFrameRate = WELS_CLIP3(pCodingParam.fMaxFrameRate, WelsEnc.MIN_FRAME_RATE, WelsEnc.MAX_FRAME_RATE);
        iTargetBitrate = pCodingParam.iTargetBitrate;
        iUsageType = pCodingParam.iUsageType;
        iPicWidth = pCodingParam.iPicWidth;
        iPicHeight = pCodingParam.iPicHeight;

        SUsedPicRect.iLeft = 0;
        SUsedPicRect.iTop = 0;
        SUsedPicRect.iWidth = ((iPicWidth >> 1) * (1 << 1));
        SUsedPicRect.iHeight = ((iPicHeight >> 1) * (1 << 1));

        iRCMode = pCodingParam.iRCMode;    // rc mode

        int8_t iIdxSpatial = 0;
        EProfileIdc uiProfileIdc = PRO_UNKNOWN;
        if (iEntropyCodingModeFlag != 0)
            uiProfileIdc = PRO_MAIN;
        fixed (SSpatialLayerInternal* pDlp0 = &sDependencyLayers[0])
        fixed (SSpatialLayerConfig* sSpatialLayers = &this.sSpatialLayers[0])
        {
            SSpatialLayerInternal* pDlp = pDlp0;

            while (iIdxSpatial < iSpatialLayerNum)
            {
                // upstream writes the profile, level and QP of layer 0 (sSpatialLayers->) on every pass
                sSpatialLayers->uiProfileIdc = uiProfileIdc;
                sSpatialLayers->uiLevelIdc = LEVEL_UNKNOWN;
                sSpatialLayers[iIdxSpatial].fFrameRate = WELS_CLIP3(pCodingParam.fMaxFrameRate,
                    WelsEnc.MIN_FRAME_RATE, WelsEnc.MAX_FRAME_RATE);
                pDlp->fInputFrameRate =
                  pDlp->fOutputFrameRate = WELS_CLIP3(sSpatialLayers[iIdxSpatial].fFrameRate, WelsEnc.MIN_FRAME_RATE,
                                                       WelsEnc.MAX_FRAME_RATE);
                pDlp->iActualWidth = sSpatialLayers[iIdxSpatial].iVideoWidth = iPicWidth;
                pDlp->iActualHeight = sSpatialLayers[iIdxSpatial].iVideoHeight = iPicHeight;

                sSpatialLayers->iSpatialBitrate =
                  sSpatialLayers[iIdxSpatial].iSpatialBitrate = pCodingParam.iTargetBitrate; // target bitrate for current spatial layer

                sSpatialLayers->iMaxSpatialBitrate = UNSPECIFIED_BIT_RATE;
                sSpatialLayers->iDLayerQp = WelsEnc.SVC_QUALITY_BASE_QP;

                uiProfileIdc = (!bSimulcastAVC) ? PRO_SCALABLE_BASELINE : uiProfileIdc;
                ++pDlp;
                ++iIdxSpatial;
            }
        }
        SetActualPicResolution();

        return 0;
    }

    public readonly void GetBaseParams(SEncParamBase* pCodingParam)
    {
        pCodingParam->iUsageType = iUsageType;
        pCodingParam->iPicWidth = iPicWidth;
        pCodingParam->iPicHeight = iPicHeight;
        pCodingParam->iTargetBitrate = iTargetBitrate;
        pCodingParam->iRCMode = iRCMode;
        pCodingParam->fMaxFrameRate = fMaxFrameRate;
    }

    public int32_t ParamTranscode(in SEncParamExt pCodingParam)
    {
        float fParamMaxFrameRate = WELS_CLIP3(pCodingParam.fMaxFrameRate, WelsEnc.MIN_FRAME_RATE, WelsEnc.MAX_FRAME_RATE);
        iUsageType = pCodingParam.iUsageType;
        iPicWidth = pCodingParam.iPicWidth;
        iPicHeight = pCodingParam.iPicHeight;
        fMaxFrameRate = fParamMaxFrameRate;
        iComplexityMode = pCodingParam.iComplexityMode;

        SUsedPicRect.iLeft = 0;
        SUsedPicRect.iTop = 0;
        SUsedPicRect.iWidth = ((iPicWidth >> 1) << 1);
        SUsedPicRect.iHeight = ((iPicHeight >> 1) << 1);

        iMultipleThreadIdc = pCodingParam.iMultipleThreadIdc;
        bUseLoadBalancing = pCodingParam.bUseLoadBalancing;

        /* Deblocking loop filter */
        iLoopFilterDisableIdc = pCodingParam.iLoopFilterDisableIdc;      // 0: on, 1: off, 2: on except for slice boundaries,
        iLoopFilterAlphaC0Offset = pCodingParam.iLoopFilterAlphaC0Offset;   // AlphaOffset: valid range [-6, 6], default 0
        iLoopFilterBetaOffset = pCodingParam.iLoopFilterBetaOffset;      // BetaOffset:  valid range [-6, 6], default 0
        iEntropyCodingModeFlag = pCodingParam.iEntropyCodingModeFlag;
        bEnableFrameCroppingFlag = pCodingParam.bEnableFrameCroppingFlag;

        /* Rate Control */
        iRCMode = pCodingParam.iRCMode;    // rc mode
        bSimulcastAVC = pCodingParam.bSimulcastAVC;
        iPaddingFlag = pCodingParam.iPaddingFlag;

        iTargetBitrate = pCodingParam.iTargetBitrate;  // target bitrate
        iMaxBitrate = pCodingParam.iMaxBitrate;
        if ((iMaxBitrate != UNSPECIFIED_BIT_RATE) && (iMaxBitrate < iTargetBitrate))
        {
            iMaxBitrate = iTargetBitrate;
        }
        iMaxQp = pCodingParam.iMaxQp;
        iMinQp = pCodingParam.iMinQp;
        uiMaxNalSize = pCodingParam.uiMaxNalSize;
        /* Denoise Control */
        bEnableDenoise = pCodingParam.bEnableDenoise ? true : false;    // Denoise Control  // only support 0 or 1 now

        /* Scene change detection control */
        bEnableSceneChangeDetect = pCodingParam.bEnableSceneChangeDetect;

        /* Background detection Control */
        bEnableBackgroundDetection = pCodingParam.bEnableBackgroundDetection ? true : false;

        /* Adaptive quantization control */
        bEnableAdaptiveQuant = pCodingParam.bEnableAdaptiveQuant ? true : false;

        /* Frame skipping */
        bEnableFrameSkip = pCodingParam.bEnableFrameSkip ? true : false;

        /* Enable int32_t term reference */
        bEnableLongTermReference = pCodingParam.bEnableLongTermReference ? true : false;
        iLtrMarkPeriod = pCodingParam.iLtrMarkPeriod;
        bIsLosslessLink = pCodingParam.bIsLosslessLink;
        bFixRCOverShoot = pCodingParam.bFixRCOverShoot;
        iIdrBitrateRatio = pCodingParam.iIdrBitrateRatio;
        bPsnrY = pCodingParam.bPsnrY;
        bPsnrU = pCodingParam.bPsnrU;
        bPsnrV = pCodingParam.bPsnrV;
        if (iUsageType == SCREEN_CONTENT_REAL_TIME && !bIsLosslessLink && bEnableLongTermReference)
        {
            bEnableLongTermReference = false;
        }

        /* For ssei information */
        bEnableSSEI = pCodingParam.bEnableSSEI;
        bSimulcastAVC = pCodingParam.bSimulcastAVC;

        /* Layer definition */
        iSpatialLayerNum = (int8_t)WELS_CLIP3(pCodingParam.iSpatialLayerNum, 1,
                                                WelsEnc.MAX_DEPENDENCY_LAYER); // number of dependency(Spatial/CGS) layers used to be encoded
        iTemporalLayerNum = (int8_t)WELS_CLIP3(pCodingParam.iTemporalLayerNum, 1,
                              WelsEnc.MAX_TEMPORAL_LEVEL); // number of temporal layer specified

        uiGopSize = 1u << (iTemporalLayerNum - 1); // Override GOP size based temporal layer
        iDecompStages = (int8_t)(iTemporalLayerNum - 1);        // WELS_LOG2( uiGopSize );// GOP size dependency
        uiIntraPeriod = pCodingParam.uiIntraPeriod;// intra period (multiple of GOP size as desired)
        if (uiIntraPeriod == unchecked((uint32_t)(-1)))
            uiIntraPeriod = 0;
        else if ((uiIntraPeriod & (uiGopSize - 1)) != 0) // none multiple of GOP size
            uiIntraPeriod = ((uiIntraPeriod + uiGopSize - 1) / uiGopSize) * uiGopSize;

        if (((pCodingParam.iNumRefFrame != AUTO_REF_PIC_COUNT)
             && !((pCodingParam.iNumRefFrame > WelsEnc.MAX_REF_PIC_COUNT) || (pCodingParam.iNumRefFrame < WelsEnc.MIN_REF_PIC_COUNT)))
            || ((iNumRefFrame != AUTO_REF_PIC_COUNT) && (pCodingParam.iNumRefFrame == AUTO_REF_PIC_COUNT)))
        {
            iNumRefFrame = pCodingParam.iNumRefFrame;
        }
        if ((iNumRefFrame != AUTO_REF_PIC_COUNT) && (iNumRefFrame > iMaxNumRefFrame))
        {
            iMaxNumRefFrame = iNumRefFrame;
        }
        iLTRRefNum = (pCodingParam.bEnableLongTermReference ? pCodingParam.iLTRRefNum : 0);
        iLtrMarkPeriod = pCodingParam.iLtrMarkPeriod;

        bPrefixNalAddingCtrl = pCodingParam.bPrefixNalAddingCtrl;

        if ((CONSTANT_ID == pCodingParam.eSpsPpsIdStrategy)
            || (INCREASING_ID == pCodingParam.eSpsPpsIdStrategy)
            || (SPS_LISTING == pCodingParam.eSpsPpsIdStrategy)
            || (SPS_LISTING_AND_PPS_INCREASING == pCodingParam.eSpsPpsIdStrategy)
            || (SPS_PPS_LISTING == pCodingParam.eSpsPpsIdStrategy))
        {
            eSpsPpsIdStrategy =
              pCodingParam.eSpsPpsIdStrategy;//For SVC meeting application, to avoid mosaic issue caused by cross-IDR reference.
            //SHOULD enable this feature.
        }
        else
        {
            // keep the default value
        }

        fixed (SSpatialLayerInternal* pDlp0 = &sDependencyLayers[0])
        fixed (SSpatialLayerConfig* pSpatialLayer0 = &sSpatialLayers[0])
        {
            SSpatialLayerInternal* pDlp = pDlp0;
            SSpatialLayerConfig* pSpatialLayer = pSpatialLayer0;
            EProfileIdc uiProfileIdc = iEntropyCodingModeFlag != 0 ? PRO_HIGH : PRO_BASELINE;
            int8_t iIdxSpatial = 0;
            while (iIdxSpatial < iSpatialLayerNum)
            {
                ref readonly SSpatialLayerConfig src = ref pCodingParam.sSpatialLayers[iIdxSpatial];
                pSpatialLayer->uiProfileIdc = (src.uiProfileIdc == PRO_UNKNOWN) ? uiProfileIdc :
                                                src.uiProfileIdc;
                pSpatialLayer->uiLevelIdc = src.uiLevelIdc;

                float fLayerFrameRate = WELS_CLIP3(src.fFrameRate,
                                                    WelsEnc.MIN_FRAME_RATE, fParamMaxFrameRate);
                pDlp->fInputFrameRate = fParamMaxFrameRate;
                pSpatialLayer->fFrameRate =
                  pDlp->fOutputFrameRate = WELS_CLIP3(fLayerFrameRate, WelsEnc.MIN_FRAME_RATE, fParamMaxFrameRate);

                pSpatialLayer->iVideoWidth = WELS_CLIP3(src.iVideoWidth, 0,
                                             iPicWidth);  // frame width
                pSpatialLayer->iVideoHeight = WELS_CLIP3(src.iVideoHeight, 0,
                                              iPicHeight);// frame height

                pSpatialLayer->iSpatialBitrate =
                  src.iSpatialBitrate;       // target bitrate for current spatial layer
                pSpatialLayer->iMaxSpatialBitrate =
                  src.iMaxSpatialBitrate;

                if ((iSpatialLayerNum == 1) && (iIdxSpatial == 0))
                {
                    if (pSpatialLayer->iVideoWidth == 0)
                    {
                        pSpatialLayer->iVideoWidth = iPicWidth;
                    }
                    if (pSpatialLayer->iVideoHeight == 0)
                    {
                        pSpatialLayer->iVideoHeight = iPicHeight;
                    }
                    if (pSpatialLayer->iSpatialBitrate == 0)
                    {
                        pSpatialLayer->iSpatialBitrate = iTargetBitrate;
                    }
                    if (pSpatialLayer->iMaxSpatialBitrate == 0)
                    {
                        pSpatialLayer->iMaxSpatialBitrate = iMaxBitrate;
                    }
                }

                //multi slice
                pSpatialLayer->sSliceArgument = src.sSliceArgument;

                pSpatialLayer->iDLayerQp = src.iDLayerQp;

                // See codec_app_def.h and parameter_sets.h for more info about members bVideoSignalTypePresent through uiColorMatrix.
                pSpatialLayer->bVideoSignalTypePresent = src.bVideoSignalTypePresent;
                pSpatialLayer->uiVideoFormat = src.uiVideoFormat;
                pSpatialLayer->bFullRange = src.bFullRange;
                pSpatialLayer->bColorDescriptionPresent = src.bColorDescriptionPresent;
                pSpatialLayer->uiColorPrimaries = src.uiColorPrimaries;
                pSpatialLayer->uiTransferCharacteristics = src.uiTransferCharacteristics;
                pSpatialLayer->uiColorMatrix = src.uiColorMatrix;

                pSpatialLayer->bAspectRatioPresent = src.bAspectRatioPresent;
                pSpatialLayer->eAspectRatio = src.eAspectRatio;
                pSpatialLayer->sAspectRatioExtWidth = src.sAspectRatioExtWidth;
                pSpatialLayer->sAspectRatioExtHeight = src.sAspectRatioExtHeight;

                uiProfileIdc = (!bSimulcastAVC) ? PRO_SCALABLE_BASELINE : uiProfileIdc; //it is used in the D>0 layer if SVC is applied, so set to PRO_SCALABLE_BASELINE
                ++pDlp;
                ++pSpatialLayer;
                ++iIdxSpatial;
            }
        }

        SetActualPicResolution();

        return 0;
    }

    // assuming that the width/height ratio of all spatial layers are the same

    public void SetActualPicResolution()
    {
        int32_t iSpatialIdx = iSpatialLayerNum - 1;
        for (; iSpatialIdx >= 0; iSpatialIdx--)
        {
            ref SSpatialLayerInternal pDlayerInternal = ref sDependencyLayers[iSpatialIdx];
            ref SSpatialLayerConfig pDlayer = ref sSpatialLayers[iSpatialIdx];

            pDlayerInternal.iActualWidth = pDlayer.iVideoWidth;
            pDlayerInternal.iActualHeight = pDlayer.iVideoHeight;
            pDlayer.iVideoWidth = WELS_ALIGN(pDlayerInternal.iActualWidth, MB_WIDTH_LUMA);
            pDlayer.iVideoHeight = WELS_ALIGN(pDlayerInternal.iActualHeight, MB_HEIGHT_LUMA);
        }
    }

    /*!
    * \brief  determined key coding tables for temporal scalability, uiProfileIdc etc for each spatial layer settings
    * \param  SWelsSvcCodingParam, and carried with known GOP size, max, input and output frame rate of each spatial
    * \return NONE (should ensure valid parameter before this procedure)
    */
    public int32_t DetermineTemporalSettings()
    {
        int32_t iDecStages = WELS_LOG2(uiGopSize); // (int8_t)GetLogFactor(1.0f, 1.0f * pcfg->uiGopSize);  //log2(uiGopSize)
        uint8_t* pTemporalIdList = (uint8_t*)&WelsEnc.g_kuiTemporalIdListTable[iDecStages];
        int8_t i = 0;

        while (i < iSpatialLayerNum)
        {
            ref SSpatialLayerInternal pDlp = ref sDependencyLayers[i];
            uint32_t kuiLogFactorInOutRate = WelsEnc.GetLogFactor(pDlp.fOutputFrameRate, pDlp.fInputFrameRate);
            uint32_t kuiLogFactorMaxInRate = WelsEnc.GetLogFactor(pDlp.fInputFrameRate, fMaxFrameRate);
            if (uint.MaxValue == kuiLogFactorInOutRate || uint.MaxValue == kuiLogFactorMaxInRate)
            {
                return WelsEnc.ENC_RETURN_INVALIDINPUT;
            }
            int32_t iNotCodedMask = 0;
            int8_t iMaxTemporalId = 0;

            fixed (uint8_t* puiCodingIdx2TemporalId = pDlp.uiCodingIdx2TemporalId)
            {
                memset(puiCodingIdx2TemporalId, WelsEnc.INVALID_TEMPORAL_ID, (1 << WelsEnc.MAX_TEMPORAL_LEVEL) + 1);
                iNotCodedMask = (1 << (int)(kuiLogFactorInOutRate + kuiLogFactorMaxInRate)) - 1;
                for (uint32_t uiFrameIdx = 0; uiFrameIdx <= uiGopSize; ++uiFrameIdx)
                {
                    if (0 == (uiFrameIdx & (uint32_t)iNotCodedMask))
                    {
                        int8_t kiTemporalId = (int8_t)pTemporalIdList[uiFrameIdx];
                        puiCodingIdx2TemporalId[uiFrameIdx] = (uint8_t)kiTemporalId;
                        if (kiTemporalId > iMaxTemporalId)
                        {
                            iMaxTemporalId = kiTemporalId;
                        }
                    }
                }
            }

            pDlp.iHighestTemporalId = iMaxTemporalId;
            pDlp.iTemporalResolution = (int32_t)(kuiLogFactorMaxInRate + kuiLogFactorInOutRate);
            pDlp.iDecompositionStages = (int32_t)((uint32_t)iDecStages - kuiLogFactorMaxInRate - kuiLogFactorInOutRate);
            if (pDlp.iDecompositionStages < 0)
            {
                return WelsEnc.ENC_RETURN_INVALIDINPUT;
            }
            ++i;
        }
        iDecompStages = (int8_t)iDecStages;
        return WelsEnc.ENC_RETURN_SUCCESS;
    }
}

/// <summary>TagExistingParasetList.</summary>
internal struct SExistingParasetList
{
    public InlineArray32<SWelsSPS> sSps;            // [MAX_SPS_COUNT]
    public InlineArray32<SSubsetSps> sSubsetSps;    // [MAX_SPS_COUNT]
    public InlineArray57<SWelsPPS> sPps;            // [MAX_PPS_COUNT]

    public uint32_t uiInUseSpsNum;
    public uint32_t uiInUseSubsetSpsNum;
    public uint32_t uiInUsePpsNum;
}

internal static unsafe partial class WelsEnc
{
    public const uint8_t INVALID_TEMPORAL_ID = 0xff;

    /*!
    * \brief    get Logarithms base 2 of (upper/base)
    * \param    base    based scaler
    * \param    upper   input upper value
    * \return   2 based scaling factor
    */
    public static uint32_t GetLogFactor(float @base, float upper)
    {
        double dLog2factor = Math.Log10(1.0 * upper / @base) / Math.Log10(2.0);
        const double dEpsilon = 0.0001;
        double dRound = Math.Floor(dLog2factor + 0.5);

        if (dLog2factor < dRound + dEpsilon && dRound < dLog2factor + dEpsilon)
        {
            return (uint32_t)(dRound);
        }
        return uint.MaxValue;
    }

    public static int32_t FreeCodingParam(SWelsSvcCodingParam** pParam, CMemoryAlign* pMa)
    {
        if (pParam == null || *pParam == null || pMa == null)
            return 1;
        pMa->WelsFree(*pParam, "SWelsSvcCodingParam");
        *pParam = null;
        return 0;
    }

    public static int32_t AllocCodingParam(SWelsSvcCodingParam** pParam, CMemoryAlign* pMa)
    {
        if (pParam == null || pMa == null)
            return 1;
        if (*pParam != null)
        {
            FreeCodingParam(pParam, pMa);
        }
        SWelsSvcCodingParam* pCodingParam = (SWelsSvcCodingParam*)pMa->WelsMallocz((uint32_t)sizeof(SWelsSvcCodingParam),
                                            "SWelsSvcCodingParam");
        if (null == pCodingParam)
            return 1;
        *pParam = pCodingParam;
        return 0;
    }
}
