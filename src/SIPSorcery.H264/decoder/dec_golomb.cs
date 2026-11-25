//-----------------------------------------------------------------------------
// Filename: dec_golomb.cs
//
// Description: Port of codec/decoder/core/inc/dec_golomb.h: the decoder's bit
// reader (BsGetBits) and Exp-Golomb decoding, plus the syntax element range
// check macros and offsets.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Macro translation:
//  - GET_WORD / NEED_BITS / DUMP_BITS work on the reader state through refs
//    and return false where the macro executes `return ERR_INFO_READ_OVERFLOW`
//    in its caller, so callers write
//    `if (!DUMP_BITS (...)) return ERR_INFO_READ_OVERFLOW;`.
//  - WELS_READ_VERIFY (x) is written inline at each call site, with a
//    function-local `uint32_t uiRetTmp`:
//    `if ((uiRetTmp = (uint32_t)x) != ERR_NONE) return (int32_t)uiRetTmp;`.
//  - WELS_CHECK_SE_*_ERROR / _WARNING log as the macros do and return true
//    when the value is out of range; the caller then does the macro's
//    `return ret_code`. Overloads for int32_t and uint32_t keep C's comparison
//    (a uint32_t syntax element compares unsigned).
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
 *     Copyright (c)  2009-2013, Cisco Systems
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
 * \file    golomb.h
 *
 * \brief   Exponential Golomb entropy coding/decoding routine
 *
 * \date    03/13/2009 Created
 *
 *************************************************************************************
 */

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GET_WORD(ref uint32_t iCurBits, ref uint8_t* pBufPtr, ref int32_t iLeftBits, intX_t iAllowedBytes, intX_t iReadBytes)
    {
        if (iReadBytes > iAllowedBytes + 1)
        {
            return false; // return ERR_INFO_READ_OVERFLOW;
        }
        uint32_t uiWord = 0;
        if (iReadBytes < iAllowedBytes)
        {
            uiWord = ((uint32_t)pBufPtr[0]) << 8;
        }
        if ((iReadBytes + 1) < iAllowedBytes)
        {
            uiWord |= pBufPtr[1];
        }
        iCurBits |= uiWord << (iLeftBits);
        iLeftBits -= 16;
        pBufPtr += 2;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool NEED_BITS(ref uint32_t iCurBits, ref uint8_t* pBufPtr, ref int32_t iLeftBits, intX_t iAllowedBytes, intX_t iReadBytes)
    {
        if (iLeftBits > 0)
        {
            return GET_WORD(ref iCurBits, ref pBufPtr, ref iLeftBits, iAllowedBytes, iReadBytes);
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t UBITS(uint32_t iCurBits, int32_t iNumBits) => (iCurBits >> (32 - (iNumBits)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DUMP_BITS(ref uint32_t iCurBits, ref uint8_t* pBufPtr, ref int32_t iLeftBits, int32_t iNumBits, intX_t iAllowedBytes, intX_t iReadBytes)
    {
        iCurBits <<= (iNumBits);
        iLeftBits += (iNumBits);
        return NEED_BITS(ref iCurBits, ref pBufPtr, ref iLeftBits, iAllowedBytes, iReadBytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t BsGetBits(SBitStringAux* pBs, int32_t iNumBits, uint32_t* pCode)
    {
        intX_t iRc = (intX_t)UBITS(pBs->uiCurBits, iNumBits);
        intX_t iAllowedBytes = (intX_t)(pBs->pEndBuf - pBs->pStartBuf); //actual stream bytes
        intX_t iReadBytes = (intX_t)(pBs->pCurBuf - pBs->pStartBuf);
        if (!DUMP_BITS(ref pBs->uiCurBits, ref pBs->pCurBuf, ref pBs->iLeftBits, iNumBits, iAllowedBytes, iReadBytes))
            return ERR_INFO_READ_OVERFLOW;
        *pCode = (uint32_t)iRc;
        return ERR_NONE;
    }

    /*
     *  Exponential Golomb codes decoding routines
     */

    public static readonly uint32_t* g_kuiPrefix8BitsTable = StaticTable.Of<uint32_t>(16, [
        0, 0, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3
    ]);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t GetPrefixBits(uint32_t uiValue)
    {
        uint32_t iNumBit = 0;

        if ((uiValue & 0xffff0000) != 0)
        {
            uiValue >>= 16;
            iNumBit += 16;
        }
        if ((uiValue & 0xff00) != 0)
        {
            uiValue >>= 8;
            iNumBit += 8;
        }

        if ((uiValue & 0xf0) != 0)
        {
            uiValue >>= 4;
            iNumBit += 4;
        }
        iNumBit += g_kuiPrefix8BitsTable[uiValue];

        return (32 - iNumBit);
    }

    /*
     *  Read one bit from bit stream followed
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t BsGetOneBit(SBitStringAux* pBs, uint32_t* pCode)
    {
        return (uint32_t)(BsGetBits(pBs, 1, pCode));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t GetLeadingZeroBits(uint32_t iCurBits)
    { //<=32 bits
        uint32_t uiValue;

        uiValue = UBITS(iCurBits, 8); //ShowBits( bs, 8 );
        if (uiValue != 0)
        {
            return g_kuiLeadingZeroTable[uiValue];
        }

        uiValue = UBITS(iCurBits, 16); //ShowBits( bs, 16 );
        if (uiValue != 0)
        {
            return (g_kuiLeadingZeroTable[uiValue] + 8);
        }

        uiValue = UBITS(iCurBits, 24); //ShowBits( bs, 24 );
        if (uiValue != 0)
        {
            return (g_kuiLeadingZeroTable[uiValue] + 16);
        }

        uiValue = iCurBits; //ShowBits( bs, 32 );
        if (uiValue != 0)
        {
            return (g_kuiLeadingZeroTable[uiValue] + 24);
        }
        //ASSERT(false);  // should not go here
        return -1;
    }

    public static uint32_t BsGetUe(SBitStringAux* pBs, uint32_t* pCode)
    {
        uint32_t iValue = 0;
        int32_t iLeadingZeroBits = GetLeadingZeroBits(pBs->uiCurBits);
        intX_t iAllowedBytes, iReadBytes;
        iAllowedBytes = (intX_t)(pBs->pEndBuf - pBs->pStartBuf); //actual stream bytes

        if (iLeadingZeroBits == -1)
        { //bistream error
            return ERR_INFO_READ_LEADING_ZERO;//-1
        }
        else if (iLeadingZeroBits >
                 16)
        { //rarely into this condition (even may be bitstream error), prevent from 16-bit reading overflow
          //using two-step reading instead of one time reading of >16 bits.
            iReadBytes = (intX_t)(pBs->pCurBuf - pBs->pStartBuf);
            if (!DUMP_BITS(ref pBs->uiCurBits, ref pBs->pCurBuf, ref pBs->iLeftBits, 16, iAllowedBytes, iReadBytes))
                return ERR_INFO_READ_OVERFLOW;
            iReadBytes = (intX_t)(pBs->pCurBuf - pBs->pStartBuf);
            if (!DUMP_BITS(ref pBs->uiCurBits, ref pBs->pCurBuf, ref pBs->iLeftBits, iLeadingZeroBits + 1 - 16, iAllowedBytes, iReadBytes))
                return ERR_INFO_READ_OVERFLOW;
        }
        else
        {
            iReadBytes = (intX_t)(pBs->pCurBuf - pBs->pStartBuf);
            if (!DUMP_BITS(ref pBs->uiCurBits, ref pBs->pCurBuf, ref pBs->iLeftBits, iLeadingZeroBits + 1, iAllowedBytes, iReadBytes))
                return ERR_INFO_READ_OVERFLOW;
        }
        if (iLeadingZeroBits != 0)
        {
            iValue = UBITS(pBs->uiCurBits, iLeadingZeroBits);
            iReadBytes = (intX_t)(pBs->pCurBuf - pBs->pStartBuf);
            if (!DUMP_BITS(ref pBs->uiCurBits, ref pBs->pCurBuf, ref pBs->iLeftBits, iLeadingZeroBits, iAllowedBytes, iReadBytes))
                return ERR_INFO_READ_OVERFLOW;
        }

        *pCode = ((1u << iLeadingZeroBits) - 1 + iValue);
        return ERR_NONE;
    }


    /*
     *  Read signed exp golomb codes
     */
    public static int32_t BsGetSe(SBitStringAux* pBs, int32_t* pCode)
    {
        uint32_t uiRetTmp;
        uint32_t uiCodeNum;

        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCodeNum)) != ERR_NONE) return (int32_t)uiRetTmp;

        if ((uiCodeNum & 0x01) != 0)
        {
            *pCode = (int32_t)((uiCodeNum + 1) >> 1);
        }
        else
        {
            *pCode = NEG_NUM((int32_t)(uiCodeNum >> 1));
        }
        return ERR_NONE;
    }

    /*
     * Get unsigned truncated exp golomb code.
     */
    public static int32_t BsGetTe0(SBitStringAux* pBs, int32_t iRange, uint32_t* pCode)
    {
        uint32_t uiRetTmp;
        if (iRange == 1)
        {
            *pCode = 0;
        }
        else if (iRange == 2)
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, pCode)) != ERR_NONE) return (int32_t)uiRetTmp;
            *pCode ^= 1;
        }
        else
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, pCode)) != ERR_NONE) return (int32_t)uiRetTmp;
        }
        return ERR_NONE;
    }

    /*
     *  Get number of trailing bits
     */
    public static int32_t BsGetTrailingBits(uint8_t* pBuf)
    {
        // TODO
        uint32_t uiValue = *pBuf;
        int32_t iRetNum = 0;

        do
        {
            if ((uiValue & 1) != 0)
                return iRetNum;
            uiValue >>= 1;
            ++iRetNum;
        } while (iRetNum < 9);

        return 0;
    }

    /*
     *      Check whether there is more rbsp data for processing
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CheckMoreRBSPData(SBitStringAux* pBsAux)
    {
        if ((pBsAux->iBits - ((pBsAux->pCurBuf - pBsAux->pStartBuf - 2) << 3) - pBsAux->iLeftBits) > 1)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    //define macros to check syntax elements
    public static bool WELS_CHECK_SE_BOTH_ERROR(SWelsDecoderContext* pCtx, int32_t val, int32_t lower_bound, int32_t upper_bound, string syntax_name)
    {
        if ((val < lower_bound) || (val > upper_bound))
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"invalid syntax {syntax_name} {val}");
            return true;
        }
        return false;
    }

    public static bool WELS_CHECK_SE_BOTH_ERROR(SWelsDecoderContext* pCtx, uint32_t val, uint32_t lower_bound, uint32_t upper_bound, string syntax_name)
    {
        if ((val < lower_bound) || (val > upper_bound))
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"invalid syntax {syntax_name} {(int32_t)val}");
            return true;
        }
        return false;
    }

    public static bool WELS_CHECK_SE_LOWER_ERROR(SWelsDecoderContext* pCtx, int32_t val, int32_t lower_bound, string syntax_name)
    {
        if (val < lower_bound)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"invalid syntax {syntax_name} {val}");
            return true;
        }
        return false;
    }

    public static bool WELS_CHECK_SE_UPPER_ERROR(SWelsDecoderContext* pCtx, int32_t val, int32_t upper_bound, string syntax_name)
    {
        if (val > upper_bound)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"invalid syntax {syntax_name} {val}");
            return true;
        }
        return false;
    }

    public static bool WELS_CHECK_SE_UPPER_ERROR(SWelsDecoderContext* pCtx, uint32_t val, uint32_t upper_bound, string syntax_name)
    {
        if (val > upper_bound)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"invalid syntax {syntax_name} {(int32_t)val}");
            return true;
        }
        return false;
    }

    public static bool WELS_CHECK_SE_BOTH_ERROR_NOLOG(int32_t val, int32_t lower_bound, int32_t upper_bound) =>
        (val < lower_bound) || (val > upper_bound);

    public static bool WELS_CHECK_SE_BOTH_ERROR_NOLOG(uint32_t val, uint32_t lower_bound, uint32_t upper_bound) =>
        (val < lower_bound) || (val > upper_bound);

    public static bool WELS_CHECK_SE_LOWER_ERROR_NOLOG(int32_t val, int32_t lower_bound) => val < lower_bound;

    public static bool WELS_CHECK_SE_UPPER_ERROR_NOLOG(int32_t val, int32_t upper_bound) => val > upper_bound;

    public static bool WELS_CHECK_SE_UPPER_ERROR_NOLOG(uint32_t val, uint32_t upper_bound) => val > upper_bound;

    public static void WELS_CHECK_SE_BOTH_WARNING(SWelsDecoderContext* pCtx, int32_t val, int32_t lower_bound, int32_t upper_bound, string syntax_name)
    {
        if ((val < lower_bound) || (val > upper_bound))
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"invalid syntax {syntax_name} {val}");
        }
    }

    public static void WELS_CHECK_SE_LOWER_WARNING(SWelsDecoderContext* pCtx, int32_t val, int32_t lower_bound, string syntax_name)
    {
        if (val < lower_bound)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"invalid syntax {syntax_name} {val}");
        }
    }

    public static void WELS_CHECK_SE_LOWER_WARNING(SWelsDecoderContext* pCtx, uint32_t val, uint32_t lower_bound, string syntax_name)
    {
        if (val < lower_bound)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"invalid syntax {syntax_name} {(int32_t)val}");
        }
    }

    public static void WELS_CHECK_SE_UPPER_WARNING(SWelsDecoderContext* pCtx, int32_t val, int32_t upper_bound, string syntax_name)
    {
        if (val > upper_bound)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"invalid syntax {syntax_name} {val}");
        }
    }

    public static void WELS_CHECK_SE_UPPER_WARNING(SWelsDecoderContext* pCtx, uint32_t val, uint32_t upper_bound, string syntax_name)
    {
        if (val > upper_bound)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"invalid syntax {syntax_name} {(int32_t)val}");
        }
    }

    // below define syntax element offset
    // for bit_depth_luma_minus8 and bit_depth_chroma_minus8
    public const int BIT_DEPTH_LUMA_OFFSET = 8;
    public const int BIT_DEPTH_CHROMA_OFFSET = 8;
    // for log2_max_frame_num_minus4
    public const int LOG2_MAX_FRAME_NUM_OFFSET = 4;
    // for log2_max_pic_order_cnt_lsb_minus4
    public const int LOG2_MAX_PIC_ORDER_CNT_LSB_OFFSET = 4;
    // for pic_width_in_mbs_minus1
    public const int PIC_WIDTH_IN_MBS_OFFSET = 1;
    // for pic_height_in_map_units_minus1
    public const int PIC_HEIGHT_IN_MAP_UNITS_OFFSET = 1;
    // for bit_depth_aux_minus8
    public const int BIT_DEPTH_AUX_OFFSET = 8;
    // for num_slice_groups_minus1
    public const int NUM_SLICE_GROUPS_OFFSET = 1;
    // for run_length_minus1
    public const int RUN_LENGTH_OFFSET = 1;
    // for slice_group_change_rate_minus1
    public const int SLICE_GROUP_CHANGE_RATE_OFFSET = 1;
    // for pic_size_in_map_units_minus1
    public const int PIC_SIZE_IN_MAP_UNITS_OFFSET = 1;
    // for num_ref_idx_l0_default_active_minus1 and num_ref_idx_l1_default_active_minus1
    public const int NUM_REF_IDX_L0_DEFAULT_ACTIVE_OFFSET = 1;
    public const int NUM_REF_IDX_L1_DEFAULT_ACTIVE_OFFSET = 1;
    // for pic_init_qp_minus26 and pic_init_qs_minus26
    public const int PIC_INIT_QP_OFFSET = 26;
    public const int PIC_INIT_QS_OFFSET = 26;
    // for num_ref_idx_l0_active_minus1 and num_ref_idx_l1_active_minus1
    public const int NUM_REF_IDX_L0_ACTIVE_OFFSET = 1;
    public const int NUM_REF_IDX_L1_ACTIVE_OFFSET = 1;

    // From Level 5.2
    public const int MAX_MB_SIZE = 36864;
    // for aspect_ratio_idc
    public const int EXTENDED_SAR = 255;
}
