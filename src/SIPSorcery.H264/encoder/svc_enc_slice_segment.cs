//-----------------------------------------------------------------------------
// Filename: svc_enc_slice_segment.cs
//
// Description: Port of codec/encoder/core/inc/svc_enc_slice_segment.h and
// codec/encoder/core/src/svc_enc_slice_segment.cpp, part 1 (E1): the slice
// count constants and the slice argument checks that parameter validation
// uses. The slice context (SSliceCtx, InitSlicePEncCtx, the MB map
// functions) follows with the slice encoder in E4.
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

internal static unsafe partial class WelsEnc
{
    // NOTE:
    // if PREFIX_NALs are used in base layer(iDid=0, qid=0), MAX_SLICES_NUM will be half of MAX_NAL_UNITS_IN_LAYER in case ST or MT without PACKING_ONE_SLICE_PER_LAYER
    // in case MT and PACKING_ONE_SLICE_PER_LAYER, MAX_SLICES_NUM should not be exceeding MAX_LAYER_NUM_OF_FRAME
    // for AVC cases, maximal resolution we can support up to (?x1024) for SM_ROWMB_SLICE slice mode
    // fine solution for MAX_SLICES_NUM, need us use the variable instead of MACRO for any resolution combining any multiple-slice mode adaptive
    public const int SAVED_NALUNIT_NUM = (MAX_SPATIAL_LAYER_NUM * MAX_QUALITY_LAYER_NUM) + 1 + MAX_SPATIAL_LAYER_NUM; // SPS/PPS + SEI/SSEI + PADDING_NAL
    public const int MAX_SLICES_NUM = (MAX_NAL_UNITS_IN_LAYER - SAVED_NALUNIT_NUM) / 3;  // Also MAX_SLICES_NUM need constrained by implementation: uiSliceIdc allocated in SSliceCtx.pOverallMbMap need a byte range as expected
    public const int AVERSLICENUM_CONSTRAINT = MAX_SLICES_NUM;                        // used in sNalList initialization,

    public const int MIN_NUM_MB_PER_SLICE = 48;                                                      // (128/16 * 96/16), addressing the lowest resolution for multiple slicing is 128x96 above

    public const int DEFAULT_MAXPACKETSIZE_CONSTRAINT = 1200;          //in bytes
    //#define MINPACKETSIZE_CONSTRAINT                (1200)

    public const int AVER_MARGIN_BYTES = 100; //in bytes
    public static int32_t JUMPPACKETSIZE_CONSTRAINT(int32_t max_byte) => max_byte - AVER_MARGIN_BYTES; //in bytes
    public static bool JUMPPACKETSIZE_JUDGE(int32_t len, int32_t mb_idx, int32_t max_byte) => len > JUMPPACKETSIZE_CONSTRAINT(max_byte); //( (mb_idx+1)%40/*16slice for compare*/ == 0 )        //
    //cur_mb_idx is for early tests, can be omit in optimization

    /*!
     *  Check slices assignment settings on MST_INTERLEAVE type
     */

    //slice parameter check for SM_FIXEDSLCNUM_SLICE
    public static bool CheckFixedSliceNumMultiSliceSetting(int32_t kiMbNumInFrame, SSliceArgument* pSliceArg)
    {
        int32_t* pSlicesAssignList = (int32_t*)&(pSliceArg->uiSliceMbNum[0]);
        uint32_t kuiSliceNum = pSliceArg->uiSliceNum;
        uint32_t uiSliceIdx = 0;
        int32_t kiMbNumPerSlice = (int32_t)((uint32_t)kiMbNumInFrame / kuiSliceNum);
        int32_t iNumMbLeft = kiMbNumInFrame;

        if (null == pSlicesAssignList)
            return false;

        for (; uiSliceIdx + 1 < kuiSliceNum; ++uiSliceIdx)
        {
            pSlicesAssignList[uiSliceIdx] = kiMbNumPerSlice;
            iNumMbLeft -= kiMbNumPerSlice;
        }

        pSlicesAssignList[uiSliceIdx] = iNumMbLeft;

        if (iNumMbLeft <= 0 || kiMbNumPerSlice <= 0)
        {
            return false;
        }

        return true;
    }

    //slice parameter check for SM_ROWMB_SLICE
    public static bool CheckRowMbMultiSliceSetting(int32_t kiMbWidth, SSliceArgument* pSliceArg)
    {
        int32_t* pSlicesAssignList = (int32_t*)&(pSliceArg->uiSliceMbNum[0]);
        uint32_t kuiSliceNum = pSliceArg->uiSliceNum;
        uint32_t uiSliceIdx = 0;

        if (null == pSlicesAssignList)
            return false;

        while (uiSliceIdx < kuiSliceNum)
        {
            pSlicesAssignList[uiSliceIdx] = kiMbWidth;
            ++uiSliceIdx;
        }
        return true;
    }

    //slice parameter check for SM_RASTER_SLICE
    public static bool CheckRasterMultiSliceSetting(int32_t kiMbNumInFrame, SSliceArgument* pSliceArg)
    {
        int32_t* pSlicesAssignList = (int32_t*)&(pSliceArg->uiSliceMbNum[0]);
        int32_t iActualSliceCount = 0;

        //check mb_num setting
        uint32_t uiSliceIdx = 0;
        int32_t iCountMb = 0;

        if (null == pSlicesAssignList)
            return false;

        while ((uiSliceIdx < MAX_SLICES_NUM) && (0 < pSlicesAssignList[uiSliceIdx]))
        {
            iCountMb += pSlicesAssignList[uiSliceIdx];
            iActualSliceCount = (int32_t)uiSliceIdx + 1;

            if (iCountMb >= kiMbNumInFrame)
            {
                break;
            }

            ++uiSliceIdx;
        }
        //break condition above makes, after the while
        // here must have (iActualSliceCount <= MAX_SLICES_NUM)

        //correction if needed
        if (iCountMb == kiMbNumInFrame)
        {
            ;
        }
        else if (iCountMb > kiMbNumInFrame)
        {
            //need correction:
            //setting is more than iMbNumInFrame,
            //cut the last uiSliceMbNum; adjust iCountMb
            pSlicesAssignList[iActualSliceCount - 1] -= (iCountMb - kiMbNumInFrame);
            iCountMb = kiMbNumInFrame;
        }
        else if (iActualSliceCount < MAX_SLICES_NUM)
        {
            //where ( iCountMb < iMbNumInFrame )
            //can do correction:
            //  make the last uiSliceMbNum the left num
            pSlicesAssignList[iActualSliceCount] = kiMbNumInFrame - iCountMb;
            iActualSliceCount += 1;
        }
        else
        {
            //here ( iCountMb < iMbNumInFrame ) && ( iActualSliceCount == MAX_SLICES_NUM )
            //no more slice can be added
            return false;
        }

        pSliceArg->uiSliceNum = (uint32_t)iActualSliceCount;
        return true;
    }

    // GOM based RC related for uiSliceNum decision, only used at SM_FIXEDSLCNUM_SLICE
    public static bool GomValidCheckSliceNum(int32_t kiMbWidth, int32_t kiMbHeight, uint32_t* pSliceNum)
    {
        int32_t kiCountNumMb = kiMbWidth * kiMbHeight;
        uint32_t iSliceNum = *pSliceNum;
        int32_t iGomSize;

        //The default RC is Bit-rate mode[Yi], but need consider as below:
        // Tuned to use max of mode0 and mode1 due can not refresh on this from rc mode changed outside, 8/16/2011
        // NOTE: GOM_ROW_MODE0_?P is integer multipler of GOM_ROW_MODE1_?P, which predefined at rc.h there, so GOM_ROM take MODE0 as the initial
        if (kiMbWidth <= MB_WIDTH_THRESHOLD_90P)
            iGomSize = kiMbWidth * GOM_ROW_MODE0_90P;
        else if (kiMbWidth <= MB_WIDTH_THRESHOLD_180P)
            iGomSize = kiMbWidth * GOM_ROW_MODE0_180P;
        else if (kiMbWidth <= MB_WIDTH_THRESHOLD_360P)
            iGomSize = kiMbWidth * GOM_ROW_MODE0_360P;
        else
            iGomSize = kiMbWidth * GOM_ROW_MODE0_720P;

        while (true)
        {
            if (kiCountNumMb < iGomSize * (int32_t)iSliceNum)
            {
                --iSliceNum;
                iSliceNum = iSliceNum - (iSliceNum & 0x01); // verfiy even num for multiple slices case
                if (iSliceNum < 2) // for safe
                    break;
                continue;
            }
            break;
        }

        if (*pSliceNum != iSliceNum)
        {
            *pSliceNum = (0 != iSliceNum) ? iSliceNum : 1;
            return false;
        }
        return true;
    }

    // GOM based RC related for uiSliceMbNum decision, only used at SM_FIXEDSLCNUM_SLICE
    public static bool GomValidCheckSliceMbNum(int32_t kiMbWidth, int32_t kiMbHeight, SSliceArgument* pSliceArg)
    {
        uint32_t* pSlicesAssignList = &(pSliceArg->uiSliceMbNum[0]);
        uint32_t kuiSliceNum = pSliceArg->uiSliceNum;
        int32_t kiMbNumInFrame = kiMbWidth * kiMbHeight;
        int32_t kiMbNumPerSlice = (int32_t)((uint32_t)kiMbNumInFrame / kuiSliceNum);
        int32_t iNumMbLeft = kiMbNumInFrame;

        int32_t iMinimalMbNum = kiMbWidth;    // in theory we need only 1 SMB, here let it as one SMB row required
        int32_t iMaximalMbNum = 0;    // dynamically assign later
        int32_t iGomSize;

        uint32_t uiSliceIdx = 0; // for test

        // The default RC is Bit-rate mode [Yi], but need consider as below:
        // Tuned to use max of mode0 and mode1 due can not refresh on this from rc mode changed outside, 8/16/2011
        // NOTE: GOM_ROW_MODE0_?P is integer multipler of GOM_ROW_MODE1_?P, which predefined at rc.h there, so GOM_ROM take MODE0 as the initial
        if (kiMbWidth <= MB_WIDTH_THRESHOLD_90P)
            iGomSize = kiMbWidth * GOM_ROW_MODE0_90P;
        else if (kiMbWidth <= MB_WIDTH_THRESHOLD_180P)
            iGomSize = kiMbWidth * GOM_ROW_MODE0_180P;
        else if (kiMbWidth <= MB_WIDTH_THRESHOLD_360P)
            iGomSize = kiMbWidth * GOM_ROW_MODE0_360P;
        else
            iGomSize = kiMbWidth * GOM_ROW_MODE0_720P;
        // GOM boundary aligned
        int32_t iNumMbAssigning = WELS_DIV_ROUND(INT_MULTIPLY * kiMbNumPerSlice, iGomSize * INT_MULTIPLY) * iGomSize;
        int32_t iCurNumMbAssigning = 0;

        iMinimalMbNum = iGomSize;
        // Ensure that the minimum macroblock requirement across all slices does not
        // exceed total frame capacity, preventing negative calculations for remaining
        // macroblocks.
        if (iMinimalMbNum * (int32_t)kuiSliceNum > kiMbNumInFrame)
        {
            return false;
        }
        while (uiSliceIdx + 1 < kuiSliceNum)
        {
            // C evaluates this in unsigned arithmetic; the wrap gives the same int32_t.
            iMaximalMbNum = (int32_t)((uint32_t)iNumMbLeft - (kuiSliceNum - uiSliceIdx - 1) * (uint32_t)iMinimalMbNum); // get maximal num_mb in left parts
            // make sure one GOM at least in each slice for safe
            if (iNumMbAssigning < iMinimalMbNum)
                iCurNumMbAssigning = iMinimalMbNum;
            else if (iNumMbAssigning > iMaximalMbNum)
                iCurNumMbAssigning = (iMaximalMbNum / iGomSize) * iGomSize;
            else
                iCurNumMbAssigning = iNumMbAssigning;

            if (iCurNumMbAssigning <= 0)
            {
                return false;
            }

            iNumMbLeft -= iCurNumMbAssigning;
            if (iNumMbLeft <= 0)
            {
                return false;
            }

            pSlicesAssignList[uiSliceIdx] = (uint32_t)iCurNumMbAssigning;
            ++uiSliceIdx;
        }
        pSlicesAssignList[uiSliceIdx] = (uint32_t)iNumMbLeft;
        if (iNumMbLeft < iMinimalMbNum)
        {
            return false;
        }

        return true;
    }

    /*!
     *  Get slice count for multiple slice segment
     *
     */
    public static int32_t GetInitialSliceNum(SSliceArgument* pSliceArgument)
    {
        if (null == pSliceArgument)
            return -1;

        switch (pSliceArgument->uiSliceMode)
        {
            case SM_SINGLE_SLICE:
            case SM_FIXEDSLCNUM_SLICE:
            case SM_RASTER_SLICE:
                {
                    return (int32_t)pSliceArgument->uiSliceNum;
                }
            case SM_SIZELIMITED_SLICE:
                {
                    return AVERSLICENUM_CONSTRAINT;//at the beginning of dynamic slicing, set the uiSliceNum to be 1
                }
            case SM_RESERVED:
            default:
                {
                    return -1;
                }
        }
    }

    //checking valid para
    public static int32_t DynamicMaxSliceNumConstraint(uint32_t uiMaximumNum, int32_t iConsumedNum, uint32_t iDulplicateTimes)
    {
        return (int32_t)((uiMaximumNum - (uint32_t)iConsumedNum - 1) / iDulplicateTimes);
    }
}
