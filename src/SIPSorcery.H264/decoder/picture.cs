//-----------------------------------------------------------------------------
// Filename: picture.cs
//
// Description: Port of codec/decoder/core/inc/picture.h: the decoder's
// reconstructed / reference picture.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// C's pointers to fixed-size rows (int8_t (*pNzc)[24], int16_t (*pMv[2])[16][2])
// are pointers to inline-array rows, so pNzc[iMbXy][i] indexes as in C.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
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

namespace SIPSorcery.H264.Decoder;

/*
*  Reconstructed Picture definition
*  It is used to express reference picture, also consequent reconstruction picture for output
*/
/// <summary>SPicture (PPicture is SPicture*).</summary>
internal unsafe struct SPicture
{
    /************************************payload data*********************************/
    public PtrArray4<uint8_t> pBuffer;             // pointer to the first allocated byte, basical offset of buffer, dimension:
    public PtrArray4<uint8_t> pData;               // pointer to picture planes respectively
    public fixed int iLinesize[4];// linesize of picture planes respectively used currently
    public int32_t iPlanes;                        // How many planes are introduced due to color space format?
    // picture information

    /*******************************from EC mv copy****************************/
    public bool bIdrFlag;

    /*******************************from other standard syntax****************************/
    /*from sps*/
    public int32_t iWidthInPixel;  // picture width in pixel
    public int32_t iHeightInPixel;// picture height in pixel
    /*from slice header*/
    public int32_t iFramePoc;              // frame POC

    /*******************************sef_definition for misc use****************************/
    public bool bUsedAsRef;                                                     //for ref pic management
    public bool bIsLongRef;     // long term reference frame flag       //for ref pic management
    public int8_t iRefCount;
    public delegate*<SPicture*, void> pSetUnRef;

    public bool bIsComplete;    // indicate whether current picture is complete, not from EC
    /*******************************for future use****************************/
    public uint8_t uiTemporalId;
    public uint8_t uiSpatialId;
    public uint8_t uiQualityId;

    public int32_t iFrameNum;              // frame number                 //for ref pic management
    public int32_t iFrameWrapNum;          // frame wrap number            //for ref pic management
    public int32_t iLongTermFrameIdx;                                      //id for long term ref pic
    public uint32_t uiLongTermPicNum;       //long_term_pic_num

    public int32_t iSpsId; //against mosaic caused by cross-IDR interval reference.
    public int32_t iPpsId;
    public ulong uiTimeStamp;
    public uint32_t uiDecodingTimeStamp; //represent relative decoding time stamps
    public int32_t iPicBuffIdx;
    public EWelsSliceType eSliceType;
    public bool bIsUngroupedMultiSlice; //multi-slice picture with each each slice group contains one slice.
    public bool bNewSeqBegin;
    public int32_t iMbEcedNum;
    public int32_t iMbEcedPropNum;
    public int32_t iMbNum;

    public bool* pMbCorrectlyDecodedFlag;
    public InlineArray24<int8_t>* pNzc;
    public uint32_t* pMbType; // mb type used for direct mode
    public PtrArray2<InlineArray16<InlineArray2<int16_t>>> pMv; // [LIST_A] -> [MB_BLOCK4x4_NUM][MV_A], used for direct mode
    public PtrArray2<InlineArray16<int8_t>> pRefIndex; // [LIST_A] -> [MB_BLOCK4x4_NUM], used for direct mode
    public InlineArray2<PtrArray17<SPicture>> pRefPic;  //ref pictures used for direct mode
    public SWelsDecEvent* pReadyEvent;  //MB line ready event
}// "Picture" declaration is comflict with Mac system
