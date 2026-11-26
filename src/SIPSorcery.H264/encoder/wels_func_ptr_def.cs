//-----------------------------------------------------------------------------
// Filename: wels_func_ptr_def.cs
//
// Description: Port of codec/encoder/core/inc/wels_func_ptr_def.h, part 2
// (E2): the encoder's function pointer list. So far it holds the kernel
// slots that WelsInitEncodingFuncs, WelsInitReconstructionFuncs,
// WelsInitSampleSadFunc and WelsInitIntraPredFuncs fill (E2), the CAVLC
// writer's slots that InitCoeffFunc fills (E3), pParametersetStrategy (E1),
// the intra mode decision, rate control and memory-zero slots (E4), and the
// deblocking slots with DeblockingFunc (E4b). The other upstream fields
// (expand picture, MC, inter mode decision, motion search, the RC SAD
// accumulator) arrive with the modules that install them; comments mark where
// they go.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The typedefs are global aliases, as in the decoder. Upstream's encoder
// PGetIntraPredFunc takes (pPred, pRef, kiStride), where the decoder's alias
// of the same name takes (pPred, kiStride), so the encoder's is
// PGetIntraPredFuncEnc here. PCopyFunc has the same signature on both sides
// and is the decoder's alias.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (E1).
// 26 Nov 2026  Claude          Kernel typedefs, SSampleDealingFunc and the E2 slots.
// 26 Nov 2026  Claude          The CAVLC writer's typedefs and slots (E3).
// 26 Nov 2026  Claude          Intra mode decision, rate control and memory-zero slots (E4).
// 26 Nov 2026  Claude          DeblockingFunc and the deblocking slots (E4b).
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

// typedef void (*PDctFunc) (int16_t* pDct, uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2);
global using unsafe PDctFunc = delegate*<short*, byte*, int, byte*, int, void>;
// typedef void (*PIDctFunc) (uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, int16_t* pRes);
global using unsafe PIDctFunc = delegate*<byte*, int, byte*, int, short*, void>;
// typedef void (*PDeQuantizationFunc) (int16_t* pRes, const uint16_t* kpQpTable);
global using unsafe PDeQuantizationFunc = delegate*<short*, ushort*, void>;
// typedef void (*PDeQuantizationHadamardFunc) (int16_t* pRes, const uint16_t kuiMF);
global using unsafe PDeQuantizationHadamardFunc = delegate*<short*, ushort, void>;
// typedef int32_t (*PGetNoneZeroCountFunc) (int16_t* pLevel);
global using unsafe PGetNoneZeroCountFunc = delegate*<short*, int>;
// typedef void (*PScanFunc) (int16_t* pLevel, int16_t* pDct);
global using unsafe PScanFunc = delegate*<short*, short*, void>;
// typedef int32_t (*PCalculateSingleCtrFunc) (int16_t* pDct);
global using unsafe PCalculateSingleCtrFunc = delegate*<short*, int>;
// typedef void (*PTransformHadamard4x4Func) (int16_t* pLumaDc, int16_t* pDct);
global using unsafe PTransformHadamard4x4Func = delegate*<short*, short*, void>;
// typedef void (*PQuantizationFunc) (int16_t* pDct, const int16_t* pFF, const int16_t* pMF);
global using unsafe PQuantizationFunc = delegate*<short*, short*, short*, void>;
// typedef void (*PQuantizationMaxFunc) (int16_t* pDct, const int16_t* pFF, const int16_t* pMF, int16_t* pMax);
global using unsafe PQuantizationMaxFunc = delegate*<short*, short*, short*, short*, void>;
// typedef void (*PQuantizationDcFunc) (int16_t* pDct, int16_t iFF,  int16_t iMF);
global using unsafe PQuantizationDcFunc = delegate*<short*, short, short, void>;
// typedef int32_t (*PQuantizationSkipFunc) (int16_t* pDct, int16_t iFF,  int16_t iMF);
global using unsafe PQuantizationSkipFunc = delegate*<short*, short, short, int>;
// typedef int32_t (*PQuantizationHadamardFunc) (int16_t* pRes, const int16_t kiFF, int16_t iMF, int16_t* pDct, int16_t* pBlock);
global using unsafe PQuantizationHadamardFunc = delegate*<short*, short, short, short*, short*, int>;
// The four deblocking kernel typedefs (PLumaDeblockingLT4Func .. PChromaDeblockingEQ4Func) are the decoder's aliases.
// typedef void (*PDeblockingBSCalc) (SWelsFuncPtrList* pFunc, SMB* pCurMb, uint8_t uiBS[2][4][4], Mb_Type uiCurMbType,
//                                    int32_t iMbStride, int32_t iLeftFlag, int32_t iTopFlag);
global using unsafe PDeblockingBSCalc = delegate*<SIPSorcery.H264.Encoder.SWelsFuncPtrList*, SIPSorcery.H264.Encoder.SMB*, byte*, uint, int,
                                                  int, int, void>;
// typedef void (*PDeblockingFilterSlice) (SDqLayer* pCurDq, SWelsFuncPtrList* pFunc, SSlice* pSlice);
global using unsafe PDeblockingFilterSlice = delegate*<SIPSorcery.H264.Encoder.SDqLayer*, SIPSorcery.H264.Encoder.SWelsFuncPtrList*,
                                                       SIPSorcery.H264.Encoder.SSlice*, void>;
// typedef  void (*PSetNoneZeroCountZeroFunc) (int8_t* pNonZeroCount);
global using unsafe PSetNoneZeroCountZeroFunc = delegate*<sbyte*, void>;
// typedef int32_t (*PSampleSadSatdCostFunc) (uint8_t*, int32_t, uint8_t*, int32_t);
global using unsafe PSampleSadSatdCostFunc = delegate*<byte*, int, byte*, int, int>;
// typedef void (*PSample4SadCostFunc) (uint8_t*, int32_t, uint8_t*, int32_t, int32_t*);
global using unsafe PSample4SadCostFunc = delegate*<byte*, int, byte*, int, int*, void>;
// typedef int32_t (*PIntraPred4x4Combined3Func) (uint8_t*, int32_t, uint8_t*, int32_t, uint8_t*, int32_t*, int32_t, int32_t, int32_t);
global using unsafe PIntraPred4x4Combined3Func = delegate*<byte*, int, byte*, int, byte*, int*, int, int, int, int>;
// typedef int32_t (*PIntraPred16x16Combined3Func) (uint8_t*, int32_t, uint8_t*, int32_t, int32_t*, int32_t, uint8_t*);
global using unsafe PIntraPred16x16Combined3Func = delegate*<byte*, int, byte*, int, int*, int, byte*, int>;
// typedef int32_t (*PIntraPred8x8Combined3Func) (uint8_t*, int32_t, uint8_t*, int32_t, int32_t*, int32_t, uint8_t*, uint8_t*, uint8_t*);
global using unsafe PIntraPred8x8Combined3Func = delegate*<byte*, int, byte*, int, int*, int, byte*, byte*, byte*, int>;
// typedef void (*PGetIntraPredFunc) (uint8_t* pPrediction, uint8_t* pRef, const int32_t kiStride);
// (renamed: the decoder's PGetIntraPredFunc takes (pPred, kiStride); see the file header)
global using unsafe PGetIntraPredFuncEnc = delegate*<byte*, byte*, int, void>;
// typedef int32_t (*PGetVarianceFromIntraVaaFunc) (uint8_t* pSampelY, const int32_t kiStride);
global using unsafe PGetVarianceFromIntraVaaFunc = delegate*<byte*, int, int>;
// typedef uint8_t (*PGetMbSignFromInterVaaFunc) (int32_t* pSad8x8);
global using unsafe PGetMbSignFromInterVaaFunc = delegate*<int*, byte>;
// typedef void (*PUpdateMbMvFunc) (SMVUnitXY* pMvUnit, const SMVUnitXY ksMv);
global using unsafe PUpdateMbMvFunc = delegate*<SIPSorcery.H264.Encoder.SMVUnitXY*, SIPSorcery.H264.Encoder.SMVUnitXY, void>;
// typedef  int32_t (*PCavlcParamCalFunc) (int16_t* pCoff, uint8_t* pRun, int16_t* pLevel, int32_t* pTotalCoeffs,
//                                         int32_t iEndIdx);
global using unsafe PCavlcParamCalFunc = delegate*<short*, byte*, short*, int*, int, int>;
// typedef int32_t (*PWelsSpatialWriteMbSyn) (sWelsEncCtx* pCtx, SSlice* pSlice, SMB* pCurMb);
global using unsafe PWelsSpatialWriteMbSyn = delegate*<SIPSorcery.H264.Encoder.sWelsEncCtx*, SIPSorcery.H264.Encoder.SSlice*,
                                                       SIPSorcery.H264.Encoder.SMB*, int>;
// typedef void (*PStashMBStatus) (SDynamicSlicingStack* pDss, SSlice* pSlice, int32_t iMbSkipRun);
global using unsafe PStashMBStatus = delegate*<SIPSorcery.H264.Encoder.SDynamicSlicingStack*, SIPSorcery.H264.Encoder.SSlice*, int, void>;
// typedef int32_t (*PStashPopMBStatus) (SDynamicSlicingStack* pDss, SSlice* pSlice);
global using unsafe PStashPopMBStatus = delegate*<SIPSorcery.H264.Encoder.SDynamicSlicingStack*, SIPSorcery.H264.Encoder.SSlice*, int>;
// typedef int32_t (*PGetBsPosition)(SSlice *pSlice);
global using unsafe PGetBsPosition = delegate*<SIPSorcery.H264.Encoder.SSlice*, int>;
// typedef void (*PSetMemoryZero) (void* pDst, int32_t iSize);
global using unsafe PSetMemoryZero = delegate*<void*, int, void>;
// typedef int32_t (*PIntraFineMdFunc) (sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMB* pCurMb, SMbCache* pMbCache);
global using unsafe PIntraFineMdFunc = delegate*<SIPSorcery.H264.Encoder.sWelsEncCtx*, SIPSorcery.H264.Encoder.SWelsMD*,
                                                 SIPSorcery.H264.Encoder.SMB*, SIPSorcery.H264.Encoder.SMbCache*, int>;
// typedef void (*PMdBackgroundInfoUpdateFunc) (SDqLayer* pCurLayer,  SMB* pCurMb, const bool bFlag,
//                                              const int32_t kiRefPictureType);
global using unsafe PMdBackgroundInfoUpdateFunc = delegate*<SIPSorcery.H264.Encoder.SDqLayer*, SIPSorcery.H264.Encoder.SMB*, bool, int, void>;
// typedef void (*PInterFineMdFunc) (sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SSlice* slice, SMB* pCurMb, int32_t bestCost);
global using unsafe PInterFineMdFunc = delegate*<SIPSorcery.H264.Encoder.sWelsEncCtx*, SIPSorcery.H264.Encoder.SWelsMD*,
                                                 SIPSorcery.H264.Encoder.SSlice*, SIPSorcery.H264.Encoder.SMB*, int, void>;
// typedef bool (*PInterMdFirstIntraModeFunc) (sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMB* pCurMb, SMbCache* pMbCache);
global using unsafe PInterMdFirstIntraModeFunc = delegate*<SIPSorcery.H264.Encoder.sWelsEncCtx*, SIPSorcery.H264.Encoder.SWelsMD*,
                                                           SIPSorcery.H264.Encoder.SMB*, SIPSorcery.H264.Encoder.SMbCache*, bool>;
// typedef void (*PFillInterNeighborCacheFunc) (SMbCache* pMbCache, SMB* pCurMb, int32_t iMbWidth, int8_t* pVaaBgMbFlag);
global using unsafe PFillInterNeighborCacheFunc = delegate*<SIPSorcery.H264.Encoder.SMbCache*, SIPSorcery.H264.Encoder.SMB*, int,
                                                            sbyte*, void>;
// typedef bool (*PInterMdBackgroundDecisionFunc) (sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SSlice* slice, SMB* pCurMb,
//                                                 SMbCache* pMbCache, bool* pKeepPskip);
global using unsafe PInterMdBackgroundDecisionFunc = delegate*<SIPSorcery.H264.Encoder.sWelsEncCtx*, SIPSorcery.H264.Encoder.SWelsMD*,
                                                               SIPSorcery.H264.Encoder.SSlice*, SIPSorcery.H264.Encoder.SMB*,
                                                               SIPSorcery.H264.Encoder.SMbCache*, bool*, bool>;
// typedef bool (*PInterMdScrollingPSkipDecisionFunc) (sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SSlice* slice, SMB* pCurMb,
//                                                     SMbCache* pMbCache);
global using unsafe PInterMdScrollingPSkipDecisionFunc = delegate*<SIPSorcery.H264.Encoder.sWelsEncCtx*, SIPSorcery.H264.Encoder.SWelsMD*,
                                                                   SIPSorcery.H264.Encoder.SSlice*, SIPSorcery.H264.Encoder.SMB*,
                                                                   SIPSorcery.H264.Encoder.SMbCache*, bool>;
// typedef void (*PSetScrollingMv) (SVAAFrameInfo* pVaa, SWelsMD* pMd);
global using unsafe PSetScrollingMv = delegate*<SIPSorcery.H264.Encoder.SVAAFrameInfo*, SIPSorcery.H264.Encoder.SWelsMD*, void>;
// typedef void (*PInterMdFunc) (sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SSlice* slice, SMB* pCurMb, SMbCache* pMbCache);
global using unsafe PInterMdFunc = delegate*<SIPSorcery.H264.Encoder.sWelsEncCtx*, SIPSorcery.H264.Encoder.SWelsMD*,
                                             SIPSorcery.H264.Encoder.SSlice*, SIPSorcery.H264.Encoder.SMB*,
                                             SIPSorcery.H264.Encoder.SMbCache*, void>;
// typedef uint32_t (*PSampleSadHor8Func) (uint8_t*, int32_t, uint8_t*, int32_t, uint16_t*, int32_t*);
global using unsafe PSampleSadHor8Func = delegate*<byte*, int, byte*, int, ushort*, int*, uint>;
// typedef void (*PMotionSearchFunc) (SWelsFuncPtrList* pFuncList, SDqLayer* pCurDqLayer, SWelsME* pMe, SSlice* pSlice);
global using unsafe PMotionSearchFunc = delegate*<SIPSorcery.H264.Encoder.SWelsFuncPtrList*, SIPSorcery.H264.Encoder.SDqLayer*,
                                                  SIPSorcery.H264.Encoder.SWelsME*, SIPSorcery.H264.Encoder.SSlice*, void>;
// typedef void (*PSearchMethodFunc) (SWelsFuncPtrList* pFuncList, SWelsME* pMe, SSlice* pSlice, const int32_t kiEncStride,
//                                    const int32_t kiRefStride);
global using unsafe PSearchMethodFunc = delegate*<SIPSorcery.H264.Encoder.SWelsFuncPtrList*, SIPSorcery.H264.Encoder.SWelsME*,
                                                  SIPSorcery.H264.Encoder.SSlice*, int, int, void>;
// typedef void (*PCalculateSatdFunc) (PSampleSadSatdCostFunc pSatd, SWelsME* pMe, const int32_t kiEncStride,
//                                     const int32_t kiRefStride);
global using unsafe PCalculateSatdFunc = delegate*<delegate*<byte*, int, byte*, int, int>, SIPSorcery.H264.Encoder.SWelsME*, int, int, void>;
// typedef bool (*PCheckDirectionalMv) (PSampleSadSatdCostFunc pSad, SWelsME* pMe,
//                                      const SMVUnitXY ksMinMv, const SMVUnitXY ksMaxMv, const int32_t kiEncStride, const int32_t kiRefStride,
//                                      int32_t& iBestSadCost);
global using unsafe PCheckDirectionalMv = delegate*<delegate*<byte*, int, byte*, int, int>, SIPSorcery.H264.Encoder.SWelsME*,
                                                    SIPSorcery.H264.Encoder.SMVUnitXY, SIPSorcery.H264.Encoder.SMVUnitXY, int, int,
                                                    int*, bool>;
// typedef void (*PLineFullSearchFunc) (SWelsFuncPtrList* pFuncList, SWelsME* pMe, uint16_t* pMvdTable,
//                                      const int32_t kiEncStride, const int32_t kiRefStride,
//                                      const int16_t kiMinMv, const int16_t kiMaxMv, const bool bVerticalSearch);
global using unsafe PLineFullSearchFunc = delegate*<SIPSorcery.H264.Encoder.SWelsFuncPtrList*, SIPSorcery.H264.Encoder.SWelsME*,
                                                    ushort*, int, int, short, short, bool, void>;
// typedef void (*PUpdateFMESwitch) (SDqLayer* pCurLayer);
global using unsafe PUpdateFMESwitch = delegate*<SIPSorcery.H264.Encoder.SDqLayer*, void>;

namespace SIPSorcery.H264.Encoder;

using System.Runtime.CompilerServices;

internal static partial class WelsEnc
{
    // #define     MAX_BLOCK_TYPE BLOCK_SIZE_ALL
    public const int MAX_BLOCK_TYPE = BLOCK_SIZE_ALL;
}

/// <summary>TagSampleDealingFunc.</summary>
internal unsafe struct SSampleDealingFunc
{
#pragma warning disable CS0649 // written through the function-pointer array properties below
    private InlineArray7<nint> _pfSampleSad;
    private InlineArray7<nint> _pfSampleSatd;
    private InlineArray7<nint> _pfSample4Sad;
#pragma warning restore CS0649
    public PIntraPred4x4Combined3Func pfIntra4x4Combined3Satd;
    public PIntraPred16x16Combined3Func pfIntra16x16Combined3Satd;
    public PIntraPred16x16Combined3Func pfIntra16x16Combined3Sad;
    public PIntraPred8x8Combined3Func pfIntra8x8Combined3Satd;
    public PIntraPred8x8Combined3Func pfIntra8x8Combined3Sad;
    public PSampleSadSatdCostFunc* pfMdCost;
    public PSampleSadSatdCostFunc* pfMeCost;
    public PIntraPred16x16Combined3Func pfIntra16x16Combined3;
    public PIntraPred8x8Combined3Func pfIntra8x8Combined3;
    public PIntraPred4x4Combined3Func pfIntra4x4Combined3;

    /// <summary>PSampleSadSatdCostFunc pfSampleSad[MAX_BLOCK_TYPE].</summary>
    public PSampleSadSatdCostFunc* pfSampleSad
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PSampleSadSatdCostFunc*)Unsafe.AsPointer(ref _pfSampleSad);
    }

    /// <summary>PSampleSadSatdCostFunc pfSampleSatd[MAX_BLOCK_TYPE].</summary>
    public PSampleSadSatdCostFunc* pfSampleSatd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PSampleSadSatdCostFunc*)Unsafe.AsPointer(ref _pfSampleSatd);
    }

    /// <summary>PSample4SadCostFunc pfSample4Sad[MAX_BLOCK_TYPE].</summary>
    public PSample4SadCostFunc* pfSample4Sad
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PSample4SadCostFunc*)Unsafe.AsPointer(ref _pfSample4Sad);
    }
}

/// <summary>tagDeblockingFunc.</summary>
internal unsafe struct DeblockingFunc
{
    public PLumaDeblockingLT4Func pfLumaDeblockingLT4Ver;
    public PLumaDeblockingEQ4Func pfLumaDeblockingEQ4Ver;
    public PLumaDeblockingLT4Func pfLumaDeblockingLT4Hor;
    public PLumaDeblockingEQ4Func pfLumaDeblockingEQ4Hor;

    public PChromaDeblockingLT4Func pfChromaDeblockingLT4Ver;
    public PChromaDeblockingEQ4Func pfChromaDeblockingEQ4Ver;
    public PChromaDeblockingLT4Func pfChromaDeblockingLT4Hor;
    public PChromaDeblockingEQ4Func pfChromaDeblockingEQ4Hor;

    public PDeblockingBSCalc pfDeblockingBSCalc;

    public PDeblockingFilterSlice pfDeblockingFilterSlice;
}

/// <summary>TagWelsFuncPointerList (partial, see the file header).</summary>
internal unsafe struct SWelsFuncPtrList
{
    public SExpandPicFunc sExpandPicFunc;
    public PFillInterNeighborCacheFunc pfFillInterNeighborCache;

    public PGetVarianceFromIntraVaaFunc pfGetVarianceFromIntraVaa;
    public PGetMbSignFromInterVaaFunc pfGetMbSignFromInterVaa;
    public PUpdateMbMvFunc pfUpdateMbMv;
    public PInterMdFirstIntraModeFunc pfFirstIntraMode; //svc_encode_slice.c svc_mode_decision.c svc_base_layer_md.c
    public PIntraFineMdFunc pfIntraFineMd;  //svc_encode_slice.c svc_mode_decision.c svc_base_layer_md.c
    public PInterFineMdFunc pfInterFineMd;  //svc_encode_slice.c svc_base_layer_md.c
    public PInterMdFunc pfInterMd;

    public PInterMdBackgroundDecisionFunc pfInterMdBackgroundDecision;
    public PMdBackgroundInfoUpdateFunc pfMdBackgroundInfoUpdate;

    public PInterMdScrollingPSkipDecisionFunc pfSCDPSkipDecision;
    public PSetScrollingMv pfSetScrollingMv;

    public SMcFunc sMcFuncs;
    public SSampleDealingFunc sSampleDealingFuncs;
#pragma warning disable CS0649 // written through the function-pointer array properties below
    private InlineArray7<nint> _pfGetLumaI16x16Pred;
    private InlineArray14<nint> _pfGetLumaI4x4Pred;
    private InlineArray7<nint> _pfGetChromaPred;

    private InlineArray2<nint> _pfSampleSadHor8;     // 1: for 16x16 square; 0: for 8x8 square
    private InlineArray3<nint> _pfMotionSearch;      // [BLOCK_STATIC_IDC_ALL] svc_encode_slice.c svc_mode_decision.c svc_enhance_layer_md.c svc_base_layer_md.c
    private InlineArray7<nint> _pfSearchMethod;      // [BLOCK_SIZE_ALL]
#pragma warning restore CS0649
    public PCalculateSatdFunc pfCalculateSatd;
    public PCheckDirectionalMv pfCheckDirectionalMv;

    // The feature search pointers (pfInitializeHashforFeature, pfFillQpelLocationByFeatureValue,
    // pfCalculateBlockFeatureOfFrame[2] and pfCalculateSingleBlockFeature[2]) are for screen
    // content, which comes with Phase 4.
    public PLineFullSearchFunc pfVerticalFullSearch;
    public PLineFullSearchFunc pfHorizontalFullSearch;
    public PUpdateFMESwitch pfUpdateFMESwitch;

    public PCopyFunc pfCopy16x16Aligned;    //svc_encode_slice.c svc_mode_decision.c svc_base_layer_md.c
    public PCopyFunc pfCopy16x16NotAligned;  //md.c
    public PCopyFunc pfCopy8x8Aligned;    //svc_encode_slice.c svc_mode_decision.c svc_base_layer_md.c md.c
    public PCopyFunc pfCopy16x8NotAligned;  //for MeRefineFracPixel 16x8 based
    public PCopyFunc pfCopy8x16Aligned;    //for MeRefineFracPixel 8x16 based
    public PCopyFunc pfCopy4x4;    //not sure if aligned or not, need further tune
    public PCopyFunc pfCopy8x4;    //not sure if aligned or not, need further tune
    public PCopyFunc pfCopy4x8;    //not sure if aligned or not, need further tune
    public PDctFunc pfDctT4;
    public PDctFunc pfDctFourT4;

    public PCalculateSingleCtrFunc pfCalculateSingleCtr4x4;
    public PScanFunc pfScan4x4;    //DC/AC
    public PScanFunc pfScan4x4Ac;

    public PQuantizationFunc pfQuantization4x4;
    public PQuantizationFunc pfQuantizationFour4x4;
    public PQuantizationDcFunc pfQuantizationDc4x4;
    public PQuantizationMaxFunc pfQuantizationFour4x4Max;
    public PQuantizationHadamardFunc pfQuantizationHadamard2x2;
    public PQuantizationSkipFunc pfQuantizationHadamard2x2Skip;

    public PTransformHadamard4x4Func pfTransformHadamard4x4Dc;

    public PGetNoneZeroCountFunc pfGetNoneZeroCount;

    public PDeQuantizationFunc pfDequantization4x4;
    public PDeQuantizationFunc pfDequantizationFour4x4;
    public PDeQuantizationHadamardFunc pfDequantizationIHadamard4x4;
    public PIDctFunc pfIDctFourT4;
    public PIDctFunc pfIDctT4;
    public PIDctFunc pfIDctI16x16Dc;

    /* For Deblocking */
    public DeblockingFunc pfDeblocking;
    public PSetNoneZeroCountZeroFunc pfSetNZCZero;

    public SWelsRcFunc pfRc;
    // PAccumulateSadFunc pfAccumulateSadForRc comes with E6.
    public PSetMemoryZero pfSetMemZeroSize8;            // for size is times to 8
    public PSetMemoryZero pfSetMemZeroSize64Aligned16;  // for size is times of 64, and address is align to 16
    public PSetMemoryZero pfSetMemZeroSize64;           // for size is times of 64, and don't know address is align to 16 or not

    public PCavlcParamCalFunc pfCavlcParamCal;
    public PWelsSpatialWriteMbSyn pfWelsSpatialWriteMbSyn;
    public PGetBsPosition pfGetBsPosition;
    public PStashMBStatus pfStashMBStatus;
    public PStashPopMBStatus pfStashPopMBStatus;

    public IWelsParametersetStrategy* pParametersetStrategy;

    /// <summary>PGetIntraPredFunc pfGetLumaI16x16Pred[I16_PRED_DC_A].</summary>
    public PGetIntraPredFuncEnc* pfGetLumaI16x16Pred
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PGetIntraPredFuncEnc*)Unsafe.AsPointer(ref _pfGetLumaI16x16Pred);
    }

    /// <summary>PGetIntraPredFunc pfGetLumaI4x4Pred[I4_PRED_A].</summary>
    public PGetIntraPredFuncEnc* pfGetLumaI4x4Pred
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PGetIntraPredFuncEnc*)Unsafe.AsPointer(ref _pfGetLumaI4x4Pred);
    }

    /// <summary>PGetIntraPredFunc pfGetChromaPred[C_PRED_A].</summary>
    public PGetIntraPredFuncEnc* pfGetChromaPred
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PGetIntraPredFuncEnc*)Unsafe.AsPointer(ref _pfGetChromaPred);
    }

    /// <summary>PSampleSadHor8Func pfSampleSadHor8[2].</summary>
    public PSampleSadHor8Func* pfSampleSadHor8
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PSampleSadHor8Func*)Unsafe.AsPointer(ref _pfSampleSadHor8);
    }

    /// <summary>PMotionSearchFunc pfMotionSearch[BLOCK_STATIC_IDC_ALL].</summary>
    public PMotionSearchFunc* pfMotionSearch
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PMotionSearchFunc*)Unsafe.AsPointer(ref _pfMotionSearch);
    }

    /// <summary>PSearchMethodFunc pfSearchMethod[BLOCK_SIZE_ALL].</summary>
    public PSearchMethodFunc* pfSearchMethod
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PSearchMethodFunc*)Unsafe.AsPointer(ref _pfSearchMethod);
    }
}
