//-----------------------------------------------------------------------------
// Filename: encoder_context.cs
//
// Description: Port of codec/encoder/core/inc/encoder_context.h, part 1
// (E1): the sWelsEncCtx fields that parameter set generation and writing
// use. The remaining fields arrive with the code that reads them, in the
// upstream order. The port never copies or memsets the context as a whole,
// so its layout only needs to hold these fields, not mirror the C one.
// OpenH264 commit 78783f19 (v2.7.0-1).
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

namespace SIPSorcery.H264.Encoder;

/*
 *  Encoder Context
 */
/// <summary>TagWelsEncCtx (partial, see the file header).</summary>
internal unsafe struct sWelsEncCtx
{
    public SLogContext sLogCtx;
    // Input
    public SWelsSvcCodingParam* pSvcParam;      // SVC parameter, WelsSVCParamConfig in svc_param_settings.h

    public SWelsFuncPtrList* pFuncList;

    public bool bCurFrameMarkedAsSceneLtr;

    // Cur pSlice type
    public uint8_t uiDependencyId;              // Idc of dependecy layer to be coded
    public uint8_t uiTemporalId;                // Idc of temporal layer to be coded

    public SWelsSPS* pSpsArray;                 // MAX_SPS_COUNT by standard compatible
    public SWelsSPS* pSps;
    public SWelsPPS* pPPSArray;                 // MAX_PPS_COUNT by standard compatible
    public SWelsPPS* pPps;
    /* SVC only */
    public SSubsetSps* pSubsetArray;            // MAX_SPS_COUNT by standard compatible
    public SSubsetSps* pSubsetSps;
    public int32_t iSpsNum;                     // number of pSps used
    public int32_t iSubsetSpsNum;               // number of pSps used
    public int32_t iPpsNum;                     // number of pPps used

    // Output
    public SWelsEncoderOutput* pOut;            // for NAL raw pData (need allocating memory for sNalList internal)
    public uint8_t* pFrameBs;                   // restoring bitstream pBuffer of all NALs in a frame
    public int32_t iFrameBsSize;                // count size of frame bs in bytes allocated
    public int32_t iPosBsBuffer;                // current writing position of frame bs pBuffer

    public int32_t iMaxSliceCount;              // maximal count number of slices for all layers observation
    public int16_t iActiveThreadsNum;           // number of threads active so far

    /*
     * DQ layer idc map for svc encoding, might be a better scheme than that of design before,
     * can aware idc of referencing layer and that idc of successive layer to be coded
     */
    /* SVC only */
    public SDqIdc* pDqIdcMap;                   // overall DQ map of full scalability in specific frame (All full D/T/Q layers involved)

    public SParaSetOffset sPSOVector;
    public SParaSetOffset* pPSOVector;
    public CMemoryAlign* pMemAlign;

    public int32_t iEncoderError;
    public bool bDeliveryFlag;

    public int64_t uiLastTimestamp;
}
