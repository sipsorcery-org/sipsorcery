//-----------------------------------------------------------------------------
// Filename: wels_func_ptr_def.cs
//
// Description: Port of codec/encoder/core/inc/wels_func_ptr_def.h, part 2
// (E2): the encoder's function pointer list. So far it holds the kernel
// slots that WelsInitEncodingFuncs, WelsInitReconstructionFuncs,
// WelsInitSampleSadFunc and WelsInitIntraPredFuncs fill (E2), and
// pParametersetStrategy (E1). The other upstream fields (expand picture, MC,
// mode decision, motion search, deblocking, rate control, CAVLC) arrive with
// the modules that install them; comments mark where they go.
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

/// <summary>TagWelsFuncPointerList (partial, see the file header).</summary>
internal unsafe struct SWelsFuncPtrList
{
    // SExpandPicFunc sExpandPicFunc; the mode decision callbacks (pfFillInterNeighborCache ..
    // pfSetScrollingMv) and SMcFunc sMcFuncs come with E4 and E5.

    public SSampleDealingFunc sSampleDealingFuncs;
#pragma warning disable CS0649 // written through the function-pointer array properties below
    private InlineArray7<nint> _pfGetLumaI16x16Pred;
    private InlineArray14<nint> _pfGetLumaI4x4Pred;
    private InlineArray7<nint> _pfGetChromaPred;
#pragma warning restore CS0649

    // pfSampleSadHor8 and the motion search pointers (pfMotionSearch .. pfUpdateFMESwitch)
    // come with E5.

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

    // DeblockingFunc pfDeblocking, pfSetNZCZero, SWelsRcFunc pfRc, pfAccumulateSadForRc,
    // the pfSetMemZero* pointers, pfCavlcParamCal, pfWelsSpatialWriteMbSyn, pfGetBsPosition,
    // pfStashMBStatus and pfStashPopMBStatus come with E3 to E6.

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
}
