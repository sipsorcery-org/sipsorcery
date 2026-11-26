//-----------------------------------------------------------------------------
// Filename: picture.cs
//
// Description: Port of codec/encoder/core/inc/picture.h: the encoder's
// reconstructed picture and the screen content block feature storage it
// points to. The encoder's SPicture is its own struct, separate from the
// decoder's.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
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

internal static partial class WelsEnc
{
    public const int LIST_SIZE = 0x10000;    //(256*256)
}

/// <summary>TagScreenBlockFeatureStorage. Should be stored with RefPic, one for each frame.</summary>
internal unsafe struct SScreenBlockFeatureStorage
{
    //Input
    public uint16_t* pFeatureOfBlockPointer;    // Pointer to pFeatureOfBlock
    public int32_t iIs16x16;                    //Feature block size
    public uint8_t uiFeatureStrategyIndex;      // index of hash strategy

    //Modify
    public uint32_t* pTimesOfFeatureValue;      // times of every value in Feature
    public uint16_t** pLocationOfFeature;       // uint16_t *pLocationOfFeature[LIST_SIZE], pLocationOfFeature[i] saves all the location(x,y) whose Feature = i;
    public uint16_t* pLocationPointer;          // buffer of position array
    public int32_t iActualListSize;             // actual list size
    public fixed uint uiSadCostThreshold[WelsEnc.BLOCK_SIZE_ALL];
    public bool bRefBlockFeatureCalculated;     // flag of whether pre-process is done
    public uint16_t** pFeatureValuePointerList; //uint16_t* pFeatureValuePointerList[WELS_MAX (LIST_SIZE_SUM_16x16, LIST_SIZE_MSE_16x16)]
}

/*
 *  Reconstructed Picture definition
 *  It is used to express reference picture, also consequent reconstruction picture for output
 */
/// <summary>TagPicture (encoder).</summary>
internal unsafe struct SPicture
{
    /************************************payload pData*********************************/
    public uint8_t* pBuffer;                    // pointer to the first allocated byte, basical offset of pBuffer, dimension:
    public PtrArray3<uint8_t> pData;            // pointer to picture planes respectively
    public fixed int iLineSize[3];              // iLineSize of picture planes respectively

    // picture information
    /*******************************from other standard syntax****************************/
    /*from pSps*/
    public int32_t iWidthInPixel;               // picture width in pixel
    public int32_t iHeightInPixel;              // picture height in pixel
    public int32_t iPictureType;                // got from sSliceHeader(): eSliceType
    public int32_t iFramePoc;                   // frame POC

    public float fFrameRate;                    // MOVE
    public int32_t iFrameNum;                   // frame number      //for pRef pic management

    public uint32_t* uiRefMbType;               // for iMbWidth*iMbHeight
    public uint8_t* pRefMbQp;                   // for iMbWidth*iMbHeight

    public int32_t* pMbSkipSad;                 //for iMbWidth*iMbHeight

    public SMVUnitXY* sMvList;

    /*******************************sef_definition for misc use****************************/
    public int32_t iMarkFrameNum;
    public int32_t iLongTermPicNum;

    public bool bUsedAsRef;                     //for pRef pic management
    public bool bIsLongRef;                     // long term reference frame flag  //for pRef pic management
    public bool bIsSceneLTR;                    //long term reference & large scene change
    public uint8_t uiRecieveConfirmed;
    public uint8_t uiTemporalId;
    public uint8_t uiSpatialId;
    public int32_t iFrameAverageQp;

    /*******************************for screen reference frames****************************/
    public SScreenBlockFeatureStorage* pScreenBlockFeatureStorage;

    /*
     *    set picture as unreferenced
     */
    public void SetUnref()
    {
        iFramePoc = -1;
        iFrameNum = -1;
        // uiTemporalId = uiSpatialId = iLongTermPicNum = -1: the two uint8_t fields get 0xff.
        iLongTermPicNum = -1;
        uiSpatialId = unchecked((uint8_t)(-1));
        uiTemporalId = uiSpatialId;
        bIsLongRef = false;
        uiRecieveConfirmed = WelsEnc.RECIEVE_FAILED;
        iMarkFrameNum = -1;
        bUsedAsRef = false;

        if (null != pScreenBlockFeatureStorage)
        {
            pScreenBlockFeatureStorage->bRefBlockFeatureCalculated = false;
        }
    }
}
