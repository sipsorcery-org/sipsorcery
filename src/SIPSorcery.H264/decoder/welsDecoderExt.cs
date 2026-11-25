//-----------------------------------------------------------------------------
// Filename: welsDecoderExt.cs
//
// Description: Port of codec/decoder/plus/inc/welsDecoderExt.h and
// plus/src/welsDecoderExt.cpp: CWelsDecoder, the ISVCDecoder implementation
// behind the public decoder API (Initialize, DecodeFrame2, DecodeFrameNoDelay,
// FlushFrame, SetOption/GetOption), plus WelsCreateDecoder,
// WelsDestroyDecoder and WelsGetDecoderCapability.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// CWelsDecoder is a plain struct in native memory, like welsCodecTrace: its
// address is handed to the decoder context (pLastDecPicInfo, pVlcTable,
// pPictInfoList, ...) and to the trace as the codec instance, so it must not
// move. New/Delete stand for upstream's new/delete and run the constructor and
// destructor; the methods are called through the CWelsDecoder*.
//
// Not ported, by design (see the plan):
//  - Threaded decoding. m_iThreadCount stays 0 and m_iCtxCount 1, so every
//    m_iThreadCount >= 1 and GetThreadCount (pCtx) >= 1 branch, the thread
//    procs, Open/CloseDecoderThreads, ThreadResetDecoder,
//    ThreadDecodeFrameInternal, ParseAccessUnit and the mutex/event/semaphore
//    members are left out; each spot says so. DECODER_OPTION_NUM_OF_THREADS is
//    accepted and ignored with a warning.
//  - Parse-only mode (bParseOnly). Initialize rejects it with cmInitParaError,
//    so DecodeParser stops at its "bParseOnly should be true" guard.
//  - OUTPUT_BIT_STREAM, which upstream builds with off.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D6).
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
 * \file    welsDecoderExt.cpp
 *
 * \date    03/12/2009 Created
 *
 *************************************************************************************
 */

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SIPSorcery.H264.Decoder.WelsDec;

namespace SIPSorcery.H264.Decoder;

internal unsafe struct CWelsDecoder
{
    /// <summary>version.h's VERSION_NUMBER when GENERATED_VERSION_HEADER is not defined, as upstream builds it.</summary>
    private const string VERSION_NUMBER = "openh264 default: 1.4";

    private welsCodecTrace* m_pWelsTrace;
    private uint32_t m_uiDecodeTimeStamp;
    private bool m_bIsBaseline;
    private int32_t m_iCpuCount;
    private int32_t m_iThreadCount;
    private int32_t m_iCtxCount;
    private SPicBuff* m_pPicBuff;
    // Upstream sets these three and never reads them (m_DecCtxActiveCount only the threaded decoder reads).
#pragma warning disable CS0414
    private bool m_bParamSetsLostFlag;
    private bool m_bFreezeOutput;
    private int32_t m_DecCtxActiveCount;
#pragma warning restore CS0414
    private SWelsDecoderThreadCTX* m_pDecThrCtx;
    private SWelsDecoderThreadCTX* m_pLastDecThrCtx;
    private int32_t m_iLastBufferedIdx;
    // m_csDecoder, m_sBufferingEvent, m_sReleaseBufferEvent and m_sIsBusy: threads only, not ported.
    private InlineArray16<SPictInfo> m_sPictInfoList;
    private SPictReoderingStatus m_sReoderingStatus;
    // m_pDecThrCtxActive[WELS_DEC_MAX_NUM_CPU]: threads only, not ported.
    private SVlcTable m_sVlcTable;
    private SWelsLastDecPicInfo m_sLastDecPicInfo;
    private SDecoderStatistics m_sDecoderStatistics;// For real time debugging
    private int32_t m_iStreamSeqNum;

    /// <summary>this, for the addresses upstream takes of members. Valid because the object lives in native memory.</summary>
    private CWelsDecoder* This => (CWelsDecoder*)Unsafe.AsPointer(ref this);

    private SPictInfo* PictInfoList => (SPictInfo*)&This->m_sPictInfoList;

    //////////////////////////////////////////////////////////////////////
    // Construction/Destruction
    //////////////////////////////////////////////////////////////////////

    /// <summary>new CWelsDecoder(): allocates the object in native memory and runs the constructor.</summary>
    public static CWelsDecoder* New()
    {
        CWelsDecoder* p = (CWelsDecoder*)NativeMemory.AllocZeroed((nuint)sizeof(CWelsDecoder));
        p->Construct();
        return p;
    }

    /// <summary>delete pDecoder: runs the destructor and frees the object.</summary>
    public static void Delete(ref CWelsDecoder* p)
    {
        if (p != null)
        {
            p->Destruct();
            NativeMemory.Free(p);
            p = null;
        }
    }

    /***************************************************************************
    *   Description:
    *       class CWelsDecoder constructor function, do initialization  and
    *       alloc memory required
    *
    *   Input parameters: none
    *
    *   return: none
    ***************************************************************************/
    private void Construct()
    {
        m_pWelsTrace = null;
        m_uiDecodeTimeStamp = 0;
        m_bIsBaseline = false;
        m_iCpuCount = 1;
        m_iThreadCount = 0;
        m_iCtxCount = 1;
        m_pPicBuff = null;
        m_bParamSetsLostFlag = false;
        m_bFreezeOutput = false;
        m_DecCtxActiveCount = 0;
        m_pDecThrCtx = null;
        m_pLastDecThrCtx = null;
        m_iLastBufferedIdx = 0;
        m_iStreamSeqNum = 0;

        m_sReoderingStatus = default;
        m_sReoderingStatus.iMinPOC = IMinInt32;
        for (int32_t i = 0; i < 16; ++i)
        {
            m_sPictInfoList[i] = default;
            m_sPictInfoList[i].iPOC = IMinInt32;
        }

        m_pWelsTrace = welsCodecTrace.New();
        if (m_pWelsTrace != null)
        {
            m_pWelsTrace->SetCodecInstance(This);
            m_pWelsTrace->SetTraceLevel(WELS_LOG_ERROR);

            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, "CWelsDecoder::CWelsDecoder() entry");
        }

        ResetReorderingPictureBuffers(&This->m_sReoderingStatus, PictInfoList, true);

        // m_iCpuCount = GetCPUCount(): the CPU count only bounds DECODER_OPTION_NUM_OF_THREADS,
        // and threaded decoding is not ported, so it stays 1.
        if (m_iCpuCount > WELS_DEC_MAX_NUM_CPU)
        {
            m_iCpuCount = WELS_DEC_MAX_NUM_CPU;
        }

        m_pDecThrCtx = (SWelsDecoderThreadCTX*)NativeMemory.AllocZeroed((nuint)(sizeof(SWelsDecoderThreadCTX) * m_iCtxCount));
        // m_pDecThrCtxActive[i] = NULL: threads only.
    }

    /***************************************************************************
    *   Description:
    *       class CWelsDecoder destructor function, destroy allocced memory
    *
    *   Input parameters: none
    *
    *   return: none
    ***************************************************************************/
    private void Destruct()
    {
        if (m_pWelsTrace != null)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, "CWelsDecoder::~CWelsDecoder()");
        }
        // CloseDecoderThreads(): threads only.
        UninitDecoder();

        if (m_pWelsTrace != null)
        {
            welsCodecTrace.Delete(ref m_pWelsTrace);
        }
        if (m_pDecThrCtx != null)
        {
            NativeMemory.Free(m_pDecThrCtx);
            m_pDecThrCtx = null;
        }
    }

    public long Initialize(SDecodingParam* pParam)
    {
        int iRet = ERR_NONE;
        if (m_pWelsTrace == null)
        {
            return (long)cmMallocMemeError;
        }

        if (pParam == null)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "CWelsDecoder::Initialize(), invalid input argument.");
            return (long)cmInitParaError;
        }

        // Port addition: parse-only mode is not ported (see the file header).
        if (pParam->bParseOnly)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "CWelsDecoder::Initialize(), parse-only mode is not supported by this port.");
            return (long)cmInitParaError;
        }

        // H.264 decoder initialization,including memory allocation,then open it ready to decode
        iRet = InitDecoder(pParam);
        if (iRet != 0)
            return iRet;

        return (long)cmResultSuccess;
    }

    public long Uninitialize()
    {
        UninitDecoder();

        return ERR_NONE;
    }

    private void UninitDecoder()
    {
        for (int32_t i = 0; i < m_iCtxCount; ++i)
        {
            if (m_pDecThrCtx[i].pCtx != null)
            {
                if (i > 0)
                {
                    WelsResetRefPicWithoutUnRef(m_pDecThrCtx[i].pCtx);
                }
                UninitDecoderCtx(ref m_pDecThrCtx[i].pCtx);
            }
        }
    }

    // OpenDecoderThreads() and CloseDecoderThreads(): threads only, not ported.

    private void UninitDecoderCtx(ref SWelsDecoderContext* pCtx)
    {
        if (pCtx != null)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                $"CWelsDecoder::UninitDecoderCtx(), openh264 codec version = {VERSION_NUMBER}.");

            WelsEndDecoder(pCtx);

            if (pCtx->pMemAlign != null)
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                    $"CWelsDecoder::UninitDecoder(), verify memory usage ({pCtx->pMemAlign->WelsGetMemoryUsage()} bytes) after free..");
                WELS_DELETE_CMemoryAlign(ref pCtx->pMemAlign);
            }

            if (null != pCtx)
            {
                WelsFree(pCtx, "m_pDecContext");

                pCtx = null;
            }
            if (m_iCtxCount <= 1) m_pDecThrCtx[0].pCtx = null;
        }
    }

    // the return value of this function is not suitable, it need report failure info to upper layer.
    private int32_t InitDecoder(SDecodingParam* pParam)
    {
        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
            $"CWelsDecoder::init_decoder(), openh264 codec version = {VERSION_NUMBER}, ParseOnly = {(pParam->bParseOnly ? 1 : 0)}");
        // m_iThreadCount >= 1 && bParseOnly, and OpenDecoderThreads(): threads only.
        //reset decoder context
        m_sDecoderStatistics = default;
        m_sLastDecPicInfo = default;
        m_sVlcTable = default;
        UninitDecoder();
        WelsDecoderLastDecPicInfoDefaults(&This->m_sLastDecPicInfo);
        for (int32_t i = 0; i < m_iCtxCount; ++i)
        {
            InitDecoderCtx(ref m_pDecThrCtx[i].pCtx, pParam);
            // m_iThreadCount >= 1: pCtx->pThreadCtx = &m_pDecThrCtx[i], threads only.
        }
        m_bParamSetsLostFlag = false;
        m_bFreezeOutput = false;
        return (int32_t)cmResultSuccess;
    }

    // the return value of this function is not suitable, it need report failure info to upper layer.
    private int32_t InitDecoderCtx(ref SWelsDecoderContext* pCtx, SDecodingParam* pParam)
    {
        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
            $"CWelsDecoder::init_decoder(), openh264 codec version = {VERSION_NUMBER}, ParseOnly = {(pParam->bParseOnly ? 1 : 0)}");

        //reset decoder context
        UninitDecoderCtx(ref pCtx);
        pCtx = (SWelsDecoderContext*)WelsMallocz((uint)sizeof(SWelsDecoderContext), "m_pDecContext");
        if (null == pCtx)
            return (int32_t)cmMallocMemeError;
        int32_t iCacheLineSize = 16;   // on chip cache line size in byte
        pCtx->pMemAlign = WELS_NEW_CMemoryAlign((uint)iCacheLineSize);
        if (null == pCtx->pMemAlign)
        {
            UninitDecoderCtx(ref pCtx);
            return (int32_t)cmMallocMemeError;
        }
        if (m_iCtxCount <= 1) m_pDecThrCtx[0].pCtx = pCtx;
        //fill in default value into context
        CWelsDecoder* self = This;
        pCtx->pLastDecPicInfo = &self->m_sLastDecPicInfo;
        pCtx->pDecoderStatistics = &self->m_sDecoderStatistics;
        pCtx->pVlcTable = &self->m_sVlcTable;
        pCtx->pPictInfoList = PictInfoList;
        pCtx->pPictReoderingStatus = &self->m_sReoderingStatus;
        pCtx->pCsDecoder = null; // &m_csDecoder: the mutex is threads only, not ported.
        pCtx->pStreamSeqNum = &self->m_iStreamSeqNum;
        WelsDecoderDefaults(pCtx, &m_pWelsTrace->m_sLogCtx);
        WelsDecoderSpsPpsDefaults(&pCtx->sSpsPpsCtx);
        //check param and update decoder context
        pCtx->pParam = (SDecodingParam*)pCtx->pMemAlign->WelsMallocz((uint)sizeof(SDecodingParam),
                       "SDecodingParam");
        if (null == pCtx->pParam)
        {
            UninitDecoderCtx(ref pCtx);
            return (int32_t)cmMallocMemeError;
        }
        int32_t iRet = DecoderConfigParam(pCtx, pParam);
        if (iRet != (int32_t)cmResultSuccess)
            return iRet;

        //init decoder
        if (WelsInitDecoder(pCtx, &m_pWelsTrace->m_sLogCtx) != 0)
        {
            UninitDecoderCtx(ref pCtx);
            return (int32_t)cmMallocMemeError;
        }
        pCtx->pPicBuff = null;
        return (int32_t)cmResultSuccess;
    }

    private int32_t ResetDecoder(ref SWelsDecoderContext* pCtx)
    {
        // TBC: need to be modified when context and trace point are null
        // m_iThreadCount >= 1: ThreadResetDecoder (pCtx), threads only.
        if (pCtx != null && m_pWelsTrace != null)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, $"ResetDecoder(), context error code is {pCtx->iErrorCode}");
            SDecodingParam sPrevParam = *pCtx->pParam;

            if (InitDecoderCtx(ref pCtx, &sPrevParam) != 0)
            {
                UninitDecoderCtx(ref pCtx);
                return (int32_t)cmInitParaError;
            }
        }
        else if (m_pWelsTrace != null)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "ResetDecoder() failed as decoder context null");
        }
        ResetReorderingPictureBuffers(&This->m_sReoderingStatus, PictInfoList, false);
        return ERR_INFO_UNINIT;
    }

    // ThreadResetDecoder: threads only, not ported.

    /*
     * Set Option
     */
    public long SetOption(DECODER_OPTION eOptID, void* pOption)
    {
        int iVal = 0;
        if (eOptID == DECODER_OPTION_NUM_OF_THREADS)
        {
            if (pOption != null)
            {
                int32_t threadCount = *((int32_t*)pOption);
                if (threadCount < 0) threadCount = 0;
                if (threadCount > m_iCpuCount)
                {
                    threadCount = m_iCpuCount;
                }
                if (threadCount > 3)
                {
                    threadCount = 3;
                }
                // Upstream switches to threaded decoding here when threadCount != m_iThreadCount;
                // threaded decoding is not ported, so the decoder stays single threaded.
                if (threadCount != m_iThreadCount && m_pWelsTrace != null)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_WARNING,
                        $"CWelsDecoder::SetOption():DECODER_OPTION_NUM_OF_THREADS = {threadCount} ignored, threaded decoding is not supported by this port.");
                }
            }
            return (long)cmResultSuccess;
        }
        for (int32_t i = 0; i < m_iCtxCount; ++i)
        {
            SWelsDecoderContext* pDecContext = m_pDecThrCtx[i].pCtx;
            if (pDecContext == null && eOptID != DECODER_OPTION_TRACE_LEVEL &&
                eOptID != DECODER_OPTION_TRACE_CALLBACK && eOptID != DECODER_OPTION_TRACE_CALLBACK_CONTEXT)
                return (long)dsInitialOptExpected;
            if (eOptID == DECODER_OPTION_END_OF_STREAM)
            { // Indicate bit-stream of the final frame to be decoded
                if (pOption == null)
                    return (long)cmInitParaError;

                iVal = *((int*)pOption); // boolean value for whether enabled End Of Stream flag

                if (pDecContext == null) return (long)dsInitialOptExpected;

                pDecContext->bEndOfStreamFlag = iVal != 0 ? true : false;
                // iVal && m_iThreadCount >= 1: SET_EVENT (&m_sReleaseBufferEvent), threads only.

                return (long)cmResultSuccess;
            }
            else if (eOptID == DECODER_OPTION_ERROR_CON_IDC)
            { // Indicate error concealment status
                if (pOption == null)
                    return (long)cmInitParaError;

                if (pDecContext == null) return (long)dsInitialOptExpected;

                iVal = *((int*)pOption); // int value for error concealment idc
                iVal = WELS_CLIP3(iVal, (int32_t)ERROR_CON_DISABLE, (int32_t)ERROR_CON_SLICE_MV_COPY_CROSS_IDR_FREEZE_RES_CHANGE);
                if ((pDecContext->pParam->bParseOnly) && (iVal != (int32_t)ERROR_CON_DISABLE))
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsDecoder::SetOption for ERROR_CON_IDC = {iVal} not allowd for parse only!.");
                    return (long)cmInitParaError;
                }

                pDecContext->pParam->eEcActiveIdc = (ERROR_CON_IDC)iVal;
                InitErrorCon(pDecContext);
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                    $"CWelsDecoder::SetOption for ERROR_CON_IDC = {iVal}.");

                return (long)cmResultSuccess;
            }
            else if (eOptID == DECODER_OPTION_TRACE_LEVEL)
            {
                if (m_pWelsTrace != null)
                {
                    uint32_t level = *((uint32_t*)pOption);
                    m_pWelsTrace->SetTraceLevel((int)level);
                }
                return (long)cmResultSuccess;
            }
            else if (eOptID == DECODER_OPTION_TRACE_CALLBACK)
            {
                if (m_pWelsTrace != null)
                {
                    delegate*<void*, int, string, void> callback = *((delegate*<void*, int, string, void>*)pOption);
                    m_pWelsTrace->SetTraceCallback(callback);
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsDecoder::SetOption():DECODER_OPTION_TRACE_CALLBACK callback = 0x{(nint)(void*)callback:X}.");
                }
                return (long)cmResultSuccess;
            }
            else if (eOptID == DECODER_OPTION_TRACE_CALLBACK_CONTEXT)
            {
                if (m_pWelsTrace != null)
                {
                    void* ctx = *((void**)pOption);
                    m_pWelsTrace->SetTraceCallbackContext(ctx);
                }
                return (long)cmResultSuccess;
            }
            else if (eOptID == DECODER_OPTION_GET_STATISTICS)
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_WARNING,
                    "CWelsDecoder::SetOption():DECODER_OPTION_GET_STATISTICS: this option is get-only!");
                return (long)cmInitParaError;
            }
            else if (eOptID == DECODER_OPTION_STATISTICS_LOG_INTERVAL)
            {
                if (pOption != null)
                {
                    if (pDecContext == null) return (long)dsInitialOptExpected;
                    pDecContext->pDecoderStatistics->iStatisticsLogInterval = (*((uint*)pOption));
                    return (long)cmResultSuccess;
                }
            }
            else if (eOptID == DECODER_OPTION_GET_SAR_INFO)
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_WARNING,
                    "CWelsDecoder::SetOption():DECODER_OPTION_GET_SAR_INFO: this option is get-only!");
                return (long)cmInitParaError;
            }
        }
        return (long)cmInitParaError;
    }

    /*
     *  Get Option
     */
    public long GetOption(DECODER_OPTION eOptID, void* pOption)
    {
        int iVal = 0;
        if (DECODER_OPTION_NUM_OF_THREADS == eOptID)
        {
            *((int*)pOption) = m_iThreadCount;
            return (long)cmResultSuccess;
        }
        SWelsDecoderContext* pDecContext = m_pDecThrCtx[0].pCtx;
        if (pDecContext == null)
            return (long)cmInitExpected;

        if (pOption == null)
            return (long)cmInitParaError;

        if (DECODER_OPTION_END_OF_STREAM == eOptID)
        {
            iVal = pDecContext->bEndOfStreamFlag ? 1 : 0;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        // LONG_TERM_REF (defined in decoder_context.h)
        else if (DECODER_OPTION_IDR_PIC_ID == eOptID)
        {
            iVal = pDecContext->uiCurIdrPicId;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_FRAME_NUM == eOptID)
        {
            iVal = pDecContext->iFrameNum;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_LTR_MARKING_FLAG == eOptID)
        {
            iVal = pDecContext->bCurAuContainLtrMarkSeFlag ? 1 : 0;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_LTR_MARKED_FRAME_NUM == eOptID)
        {
            iVal = pDecContext->iFrameNumOfAuMarkedLtr;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_VCL_NAL == eOptID)
        { //feedback whether or not have VCL NAL in current AU
            iVal = pDecContext->iFeedbackVclNalInAu;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_TEMPORAL_ID == eOptID)
        { //if have VCL NAL in current AU, then feedback the temporal ID
            iVal = pDecContext->iFeedbackTidInAu;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_IS_REF_PIC == eOptID)
        {
            iVal = pDecContext->iFeedbackNalRefIdc;
            if (iVal > 0)
                iVal = 1;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_ERROR_CON_IDC == eOptID)
        {
            iVal = (int)pDecContext->pParam->eEcActiveIdc;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_GET_STATISTICS == eOptID)
        { // get decoder statistics info for real time debugging
            SDecoderStatistics* pDecoderStatistics = (SDecoderStatistics*)pOption;

            *pDecoderStatistics = *pDecContext->pDecoderStatistics;

            if (pDecContext->pDecoderStatistics->uiDecodedFrameCount != 0)
            { //not original status
                pDecoderStatistics->fAverageFrameSpeedInMs = (float)(pDecContext->dDecTime) /
                    (pDecContext->pDecoderStatistics->uiDecodedFrameCount);
                pDecoderStatistics->fActualAverageFrameSpeedInMs = (float)(pDecContext->dDecTime) /
                    (pDecContext->pDecoderStatistics->uiDecodedFrameCount + pDecContext->pDecoderStatistics->uiFreezingIDRNum +
                     pDecContext->pDecoderStatistics->uiFreezingNonIDRNum);
            }
            return (long)cmResultSuccess;
        }
        else if (eOptID == DECODER_OPTION_STATISTICS_LOG_INTERVAL)
        {
            if (pOption != null)
            {
                iVal = (int)pDecContext->pDecoderStatistics->iStatisticsLogInterval;
                *((uint*)pOption) = (uint)iVal;
                return (long)cmResultSuccess;
            }
        }
        else if (DECODER_OPTION_GET_SAR_INFO == eOptID)
        { //get decoder SAR info in VUI
            SVuiSarInfo* pVuiSarInfo = (SVuiSarInfo*)pOption;
            *pVuiSarInfo = default;
            if (pDecContext->pSps == null)
            {
                return (long)cmInitExpected;
            }
            else
            {
                pVuiSarInfo->uiSarWidth = pDecContext->pSps->sVui.uiSarWidth;
                pVuiSarInfo->uiSarHeight = pDecContext->pSps->sVui.uiSarHeight;
                pVuiSarInfo->bOverscanAppropriateFlag = pDecContext->pSps->sVui.bOverscanAppropriateFlag;
                return (long)cmResultSuccess;
            }
        }
        else if (DECODER_OPTION_PROFILE == eOptID)
        {
            if (pDecContext->pSps == null)
            {
                return (long)cmInitExpected;
            }
            iVal = (int)pDecContext->pSps->uiProfileIdc;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_LEVEL == eOptID)
        {
            if (pDecContext->pSps == null)
            {
                return (long)cmInitExpected;
            }
            iVal = (int)pDecContext->pSps->uiLevelIdc;
            *((int*)pOption) = iVal;
            return (long)cmResultSuccess;
        }
        else if (DECODER_OPTION_NUM_OF_FRAMES_REMAINING_IN_BUFFER == eOptID)
        {
            // The wait on each active thread context (m_DecCtxActiveCount) is threads only.
            *((int*)pOption) = m_sReoderingStatus.iNumOfPicts;
            return (long)cmResultSuccess;
        }

        return (long)cmInitParaError;
    }

    public DECODING_STATE DecodeFrameNoDelay(uint8_t* kpSrc,
        int kiSrcLen,
        uint8_t** ppDst,
        SBufferInfo* pDstInfo)
    {
        int iRet = (int)dsErrorFree;
        // m_iThreadCount >= 1: ThreadDecodeFrameInternal and the buffered release, threads only.
        //SBufferInfo sTmpBufferInfo;
        //unsigned char* ppTmpDst[3] = {NULL, NULL, NULL};
        iRet = (int)DecodeFrame2(kpSrc, kiSrcLen, ppDst, pDstInfo);
        //memcpy (&sTmpBufferInfo, pDstInfo, sizeof (SBufferInfo));
        //ppTmpDst[0] = ppDst[0];
        //ppTmpDst[1] = ppDst[1];
        //ppTmpDst[2] = ppDst[2];
        iRet |= (int)DecodeFrame2(null, 0, ppDst, pDstInfo);
        //if ((pDstInfo->iBufferStatus == 0) && (sTmpBufferInfo.iBufferStatus == 1)) {
        //memcpy (pDstInfo, &sTmpBufferInfo, sizeof (SBufferInfo));
        //ppDst[0] = ppTmpDst[0];
        //ppDst[1] = ppTmpDst[1];
        //ppDst[2] = ppTmpDst[2];
        //}
        return (DECODING_STATE)iRet;
    }

    public DECODING_STATE DecodeFrame2WithCtx(SWelsDecoderContext* pDecContext, uint8_t* kpSrc,
        int kiSrcLen,
        uint8_t** ppDst,
        SBufferInfo* pDstInfo)
    {
        if (pDecContext == null || pDecContext->pParam == null)
        {
            if (m_pWelsTrace != null)
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "Call DecodeFrame2 without Initialize.\n");
            }
            return dsInitialOptExpected;
        }

        if (pDecContext->pParam->bParseOnly)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "bParseOnly should be false for this API calling! \n");
            pDecContext->iErrorCode |= (int32_t)dsInvalidArgument;
            return dsInvalidArgument;
        }
        if (CheckBsBuffer(pDecContext, kiSrcLen) != 0)
        {
            if (ResetDecoder(ref pDecContext) != 0)
            {
                if (pDstInfo != null) pDstInfo->iBufferStatus = 0;
                return dsOutOfMemory;
            }
            return dsErrorFree;
        }
        if (kiSrcLen > 0 && kpSrc != null)
        {
            // OUTPUT_BIT_STREAM: off upstream.
            pDecContext->bEndOfStreamFlag = false;
            if (GetThreadCount(pDecContext) <= 0)
            {
                pDecContext->uiDecodingTimeStamp = ++m_uiDecodeTimeStamp;
            }
        }
        else
        {
            //For application MODE, the error detection should be added for safe.
            //But for CONSOLE MODE, when decoding LAST AU, kiSrcLen==0 && kpSrc==NULL.
            pDecContext->bEndOfStreamFlag = true;
            pDecContext->bInstantDecFlag = true;
        }

        int64_t iStart, iEnd;
        iStart = WelsTime();

        if (GetThreadCount(pDecContext) <= 1)
        {
            ppDst[0] = ppDst[1] = ppDst[2] = null;
        }
        pDecContext->iErrorCode = (int32_t)dsErrorFree; //initialize at the starting of AU decoding.
        pDecContext->iFeedbackVclNalInAu = (int32_t)FEEDBACK_UNKNOWN_NAL; //initialize
        ulong uiInBsTimeStamp = pDstInfo->uiInBsTimeStamp;
        if (GetThreadCount(pDecContext) <= 1)
        {
            *pDstInfo = default;
        }
        pDstInfo->uiInBsTimeStamp = uiInBsTimeStamp;
        // LONG_TERM_REF
        pDecContext->bReferenceLostAtT0Flag = false; //initialize for LTR
        pDecContext->bCurAuContainLtrMarkSeFlag = false;
        pDecContext->iFrameNumOfAuMarkedLtr = 0;
        pDecContext->iFrameNum = -1; //initialize

        // GetThreadCount (pDecContext) >= 1: WAIT_EVENT (&m_sReleaseBufferEvent), threads only.

        pDecContext->iFeedbackTidInAu = -1; //initialize
        pDecContext->iFeedbackNalRefIdc = -1; //initialize
        if (pDstInfo != null)
        {
            pDstInfo->uiOutYuvTimeStamp = 0;
            pDecContext->uiTimeStamp = pDstInfo->uiInBsTimeStamp;
        }
        else
        {
            pDecContext->uiTimeStamp = 0;
        }
        WelsDecodeBs(pDecContext, kpSrc, kiSrcLen, ppDst,
                     pDstInfo, null); //iErrorCode has been modified in this function
        pDecContext->bInstantDecFlag = false; //reset no-delay flag
        if (pDecContext->iErrorCode != 0)
        {
            EWelsNalUnitType eNalType =
                NAL_UNIT_UNSPEC_0; //for NBR, IDR frames are expected to decode as followed if error decoding an IDR currently

            eNalType = pDecContext->sCurNalHead.eNalUnitType;
            if ((pDecContext->iErrorCode & (int32_t)dsOutOfMemory) != 0)
            {
                if (ResetDecoder(ref pDecContext) != 0)
                {
                    if (pDstInfo != null) pDstInfo->iBufferStatus = 0;
                    return dsOutOfMemory;
                }
                return dsErrorFree;
            }
            if ((pDecContext->iErrorCode & (int32_t)dsRefListNullPtrs) != 0)
            {
                if (ResetDecoder(ref pDecContext) != 0)
                {
                    if (pDstInfo != null) pDstInfo->iBufferStatus = 0;
                    return dsRefListNullPtrs;
                }
                return dsErrorFree;
            }
            //for AVC bitstream (excluding AVC with temporal scalability, including TP), as long as error occur, SHOULD notify upper layer key frame loss.
            if ((IS_PARAM_SETS_NALS(eNalType) || NAL_UNIT_CODED_SLICE_IDR == eNalType) ||
                (VIDEO_BITSTREAM_AVC == pDecContext->eVideoType))
            {
                if (pDecContext->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                {
                    // LONG_TERM_REF
                    pDecContext->bParamSetsLostFlag = true;
                }
            }

            if (pDecContext->bPrintFrameErrorTraceFlag)
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, $"decode failed, failure type:{pDecContext->iErrorCode} \n");
                pDecContext->bPrintFrameErrorTraceFlag = false;
            }
            else
            {
                pDecContext->iIgnoredErrorInfoPacketCount++;
                if (pDecContext->iIgnoredErrorInfoPacketCount == int32_t.MaxValue)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_WARNING, "continuous error reached INT_MAX! Restart as 0.");
                    pDecContext->iIgnoredErrorInfoPacketCount = 0;
                }
            }
            if ((pDecContext->pParam->eEcActiveIdc != ERROR_CON_DISABLE) && (pDstInfo->iBufferStatus == 1))
            {
                //TODO after dec status updated
                pDecContext->iErrorCode |= (int32_t)dsDataErrorConcealed;

                SDecoderStatistics* pStat = pDecContext->pDecoderStatistics;
                pStat->uiDecodedFrameCount++;
                if (pStat->uiDecodedFrameCount == 0)
                { //exceed max value of uint32_t
                    ResetDecStatNums(pStat);
                    pStat->uiDecodedFrameCount++;
                }
                int32_t iMbConcealedNum = pDecContext->iMbEcedNum + pDecContext->iMbEcedPropNum;
                // int32_t + uint32_t arithmetic is unsigned in C, hence the (uint32_t) casts.
                pStat->uiAvgEcRatio = pDecContext->iMbNum == 0 ?
                    (pStat->uiAvgEcRatio * pStat->uiEcFrameNum) : ((
                        pStat->uiAvgEcRatio * pStat->uiEcFrameNum) + (uint32_t)((
                            iMbConcealedNum * 100) / pDecContext->iMbNum));
                pStat->uiAvgEcPropRatio = pDecContext->iMbNum == 0 ?
                    (pStat->uiAvgEcPropRatio * pStat->uiEcFrameNum) : ((
                        pStat->uiAvgEcPropRatio * pStat->uiEcFrameNum) + (uint32_t)((
                            pDecContext->iMbEcedPropNum * 100) / pDecContext->iMbNum));
                pStat->uiEcFrameNum += (iMbConcealedNum == 0 ? 0u : 1u);
                pStat->uiAvgEcRatio = pStat->uiEcFrameNum == 0 ? 0 :
                    pStat->uiAvgEcRatio / pStat->uiEcFrameNum;
                pStat->uiAvgEcPropRatio = pStat->uiEcFrameNum == 0 ? 0 :
                    pStat->uiAvgEcPropRatio / pStat->uiEcFrameNum;
            }
            iEnd = WelsTime();
            pDecContext->dDecTime += (iEnd - iStart) / 1e3;

            OutputStatisticsLog(pDecContext->pDecoderStatistics);
            // GetThreadCount (pDecContext) >= 1: BufferingReadyPicture and SET_EVENT, threads only.
            ReorderPicturesInDisplay(pDecContext, ppDst, pDstInfo);

            return (DECODING_STATE)pDecContext->iErrorCode;
        }
        // else Error free, the current codec works well

        if (pDstInfo->iBufferStatus == 1)
        {
            pDecContext->pDecoderStatistics->uiDecodedFrameCount++;
            if (pDecContext->pDecoderStatistics->uiDecodedFrameCount == 0)
            { //exceed max value of uint32_t
                ResetDecStatNums(pDecContext->pDecoderStatistics);
                pDecContext->pDecoderStatistics->uiDecodedFrameCount++;
            }

            OutputStatisticsLog(pDecContext->pDecoderStatistics);
        }
        iEnd = WelsTime();
        pDecContext->dDecTime += (iEnd - iStart) / 1e3;

        // GetThreadCount (pDecContext) >= 1: BufferingReadyPicture and SET_EVENT, threads only.
        ReorderPicturesInDisplay(pDecContext, ppDst, pDstInfo);
        return dsErrorFree;
    }

    public DECODING_STATE DecodeFrame2(uint8_t* kpSrc,
        int kiSrcLen,
        uint8_t** ppDst,
        SBufferInfo* pDstInfo)
    {
        SWelsDecoderContext* pDecContext = m_pDecThrCtx[0].pCtx;
        return DecodeFrame2WithCtx(pDecContext, kpSrc, kiSrcLen, ppDst, pDstInfo);
    }

    public DECODING_STATE FlushFrame(uint8_t** ppDst,
        SBufferInfo* pDstInfo)
    {
        bool bEndOfStreamFlag = true;
        if (m_iThreadCount <= 1)
        {
            for (int32_t j = 0; j < m_iCtxCount; ++j)
            {
                if (!m_pDecThrCtx[j].pCtx->bEndOfStreamFlag)
                {
                    bEndOfStreamFlag = false;
                }
            }
        }
        // The m_csDecoder lock around the reads below is threads only.
        int32_t iNumOfPicts = m_sReoderingStatus.iNumOfPicts;
        bool bHasBSlice = m_sReoderingStatus.bHasBSlice;
        if (bEndOfStreamFlag && iNumOfPicts > 0)
        {
            if (!bHasBSlice)
            {
                ReleaseBufferedReadyPictureNoReorder(null, ppDst, pDstInfo);
            }
            else
            {
                ReleaseBufferedReadyPictureReorder(null, ppDst, pDstInfo, true);
            }
        }
        return dsErrorFree;
    }

    private void OutputStatisticsLog(SDecoderStatistics* sDecoderStatistics)
    {
        if ((sDecoderStatistics->uiDecodedFrameCount > 0) && (sDecoderStatistics->iStatisticsLogInterval > 0)
            && ((sDecoderStatistics->uiDecodedFrameCount % sDecoderStatistics->iStatisticsLogInterval) == 0))
        {
            // Upstream's format string is split with backslash line continuations, so it carries
            // runs of indentation spaces; they are left out here. %d of the unsigned fields
            // prints them as int.
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                $"DecoderStatistics: uiWidth={(int)sDecoderStatistics->uiWidth}, uiHeight={(int)sDecoderStatistics->uiHeight}, " +
                $"fAverageFrameSpeedInMs={sDecoderStatistics->fAverageFrameSpeedInMs:F1}, fActualAverageFrameSpeedInMs={sDecoderStatistics->fActualAverageFrameSpeedInMs:F1}, " +
                $"uiDecodedFrameCount={(int)sDecoderStatistics->uiDecodedFrameCount}, uiResolutionChangeTimes={(int)sDecoderStatistics->uiResolutionChangeTimes}, uiIDRCorrectNum={(int)sDecoderStatistics->uiIDRCorrectNum}, " +
                $"uiAvgEcRatio={(int)sDecoderStatistics->uiAvgEcRatio}, uiAvgEcPropRatio={(int)sDecoderStatistics->uiAvgEcPropRatio}, uiEcIDRNum={(int)sDecoderStatistics->uiEcIDRNum}, uiEcFrameNum={(int)sDecoderStatistics->uiEcFrameNum}, " +
                $"uiIDRLostNum={(int)sDecoderStatistics->uiIDRLostNum}, uiFreezingIDRNum={(int)sDecoderStatistics->uiFreezingIDRNum}, uiFreezingNonIDRNum={(int)sDecoderStatistics->uiFreezingNonIDRNum}, iAvgLumaQp={sDecoderStatistics->iAvgLumaQp}, " +
                $"iSpsReportErrorNum={sDecoderStatistics->iSpsReportErrorNum}, iSubSpsReportErrorNum={sDecoderStatistics->iSubSpsReportErrorNum}, iPpsReportErrorNum={sDecoderStatistics->iPpsReportErrorNum}, iSpsNoExistNalNum={sDecoderStatistics->iSpsNoExistNalNum}, iSubSpsNoExistNalNum={sDecoderStatistics->iSubSpsNoExistNalNum}, iPpsNoExistNalNum={sDecoderStatistics->iPpsNoExistNalNum}, " +
                $"uiProfile={(int)sDecoderStatistics->uiProfile}, uiLevel={(int)sDecoderStatistics->uiLevel}, " +
                $"iCurrentActiveSpsId={sDecoderStatistics->iCurrentActiveSpsId}, iCurrentActivePpsId={sDecoderStatistics->iCurrentActivePpsId},");
        }
    }

    private void BufferingReadyPicture(SWelsDecoderContext* pCtx, uint8_t** ppDst,
        SBufferInfo* pDstInfo)
    {
        if (pDstInfo->iBufferStatus == 0)
        {
            return;
        }
        // The m_csDecoder lock around the publication below is threads only.
        m_bIsBaseline = (int32_t)pCtx->pSps->uiProfileIdc == 66 || (int32_t)pCtx->pSps->uiProfileIdc == 83;
        if (!m_bIsBaseline)
        {
            if (pCtx->pSliceHeader->eSliceType == B_SLICE)
            {
                m_sReoderingStatus.bHasBSlice = true;
            }
        }
        for (int32_t i = 0; i < 16; ++i)
        {
            if (m_sPictInfoList[i].iPOC == IMinInt32)
            {
                m_sPictInfoList[i].sBufferInfo = *pDstInfo;
                m_sPictInfoList[i].iPOC = pCtx->pSliceHeader->iPicOrderCntLsb;
                m_sPictInfoList[i].iSeqNum = pCtx->iSeqNum;
                m_sPictInfoList[i].uiDecodingTimeStamp = pCtx->uiDecodingTimeStamp;
                if (pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb != null)
                {
                    SPicture* pPrevPic = (GetThreadCount(pCtx) > 1 && pCtx->pThreadCtx != null)
                                         ? ((SWelsDecoderThreadCTX*)pCtx->pThreadCtx)->pPreviousDecodedPictureInDpb
                                         : pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb;
                    if (pPrevPic != null)
                    {
                        m_sPictInfoList[i].iPicBuffIdx = pPrevPic->iPicBuffIdx;
                        if (GetThreadCount(pCtx) <= 1) ++pPrevPic->iRefCount;
                    }
                }
                m_iLastBufferedIdx = i;
                pDstInfo->iBufferStatus = 0;
                ++m_sReoderingStatus.iNumOfPicts;
                if (i > m_sReoderingStatus.iLargestBufferedPicIndex)
                {
                    m_sReoderingStatus.iLargestBufferedPicIndex = i;
                }
                break;
            }
        }
    }

    private void ReleaseBufferedReadyPictureReorder(SWelsDecoderContext* pCtx, uint8_t** ppDst,
        SBufferInfo* pDstInfo, bool isFlush = false)
    {
        // The m_csDecoder lock around the dequeue is threads only.
        SPicBuff* pPicBuff = pCtx != null ? pCtx->pPicBuff : m_pPicBuff;
        if (pCtx == null && m_iThreadCount <= 1)
        {
            pCtx = m_pDecThrCtx[0].pCtx;
        }
        if (m_sReoderingStatus.iNumOfPicts > 0)
        {
            m_sReoderingStatus.iMinPOC = IMinInt32;
            int32_t firstValidIdx = -1;
            for (int32_t i = 0; i <= m_sReoderingStatus.iLargestBufferedPicIndex; ++i)
            {
                if (m_sReoderingStatus.iMinPOC == IMinInt32 && m_sPictInfoList[i].iPOC > IMinInt32)
                {
                    m_sReoderingStatus.iMinPOC = m_sPictInfoList[i].iPOC;
                    m_sReoderingStatus.iMinSeqNum = m_sPictInfoList[i].iSeqNum;
                    m_sReoderingStatus.iPictInfoIndex = i;
                    firstValidIdx = i;
                    break;
                }
            }
            for (int32_t i = 0; i <= m_sReoderingStatus.iLargestBufferedPicIndex; ++i)
            {
                if (i == firstValidIdx) continue;
                if (m_sPictInfoList[i].iPOC > IMinInt32
                    && ((m_sPictInfoList[i].iSeqNum == m_sReoderingStatus.iMinSeqNum) ? (m_sPictInfoList[i].iPOC < m_sReoderingStatus.iMinPOC) : (m_sPictInfoList[i].iSeqNum - m_sReoderingStatus.iMinSeqNum < 0)))
                {
                    m_sReoderingStatus.iMinPOC = m_sPictInfoList[i].iPOC;
                    m_sReoderingStatus.iMinSeqNum = m_sPictInfoList[i].iSeqNum;
                    m_sReoderingStatus.iPictInfoIndex = i;
                }
            }
        }
        if (m_sReoderingStatus.iMinPOC > IMinInt32)
        {
            bool isReady = true;
            if (!isFlush)
            {
                int32_t iLastPOC = pCtx != null ? pCtx->pSliceHeader->iPicOrderCntLsb : m_sPictInfoList[m_iLastBufferedIdx].iPOC;
                int32_t iLastSeqNum = pCtx != null ? pCtx->iSeqNum : m_sPictInfoList[m_iLastBufferedIdx].iSeqNum;
                isReady = (m_sReoderingStatus.iLastWrittenPOC > IMinInt32
                           && m_sReoderingStatus.iMinPOC - m_sReoderingStatus.iLastWrittenPOC <= 1)
                          || m_sReoderingStatus.iMinPOC < iLastPOC
                          || m_sReoderingStatus.iMinSeqNum - iLastSeqNum < 0;
            }
            if (isReady)
            {
                m_sReoderingStatus.iLastWrittenPOC = m_sReoderingStatus.iMinPOC;
                m_sReoderingStatus.iLastWrittenSeqNum = m_sReoderingStatus.iMinSeqNum;
                *pDstInfo = m_sPictInfoList[m_sReoderingStatus.iPictInfoIndex].sBufferInfo;
                ppDst[0] = pDstInfo->pDst[0];
                ppDst[1] = pDstInfo->pDst[1];
                ppDst[2] = pDstInfo->pDst[2];
                m_sPictInfoList[m_sReoderingStatus.iPictInfoIndex].iPOC = IMinInt32;
                int32_t iPicBuffIdx = m_sPictInfoList[m_sReoderingStatus.iPictInfoIndex].iPicBuffIdx;
                if (pPicBuff != null)
                {
                    if (iPicBuffIdx >= 0 && iPicBuffIdx < pPicBuff->iCapacity)
                    {
                        SPicture* pPic = pPicBuff->ppPic[iPicBuffIdx];
                        --pPic->iRefCount;
                        if (pPic->iRefCount <= 0 && pPic->pSetUnRef != null)
                            pPic->pSetUnRef(pPic);
                    }
                }
                m_sReoderingStatus.iMinPOC = IMinInt32;
                --m_sReoderingStatus.iNumOfPicts;
            }
        }
    }

    //if there is no b-frame, no ordering based on values of POCs is necessary.
    //The function is added to force to avoid picture reordering because some h.264 streams do not follow H.264 POC specifications.
    private void ReleaseBufferedReadyPictureNoReorder(SWelsDecoderContext* pCtx, uint8_t** ppDst, SBufferInfo* pDstInfo)
    {
        // The m_csDecoder lock around the dequeue is threads only.
        int32_t firstValidIdx = -1;
        uint32_t uiDecodingTimeStamp = 0;
        for (int32_t i = 0; i <= m_sReoderingStatus.iLargestBufferedPicIndex; ++i)
        {
            if (m_sPictInfoList[i].iPOC != IMinInt32)
            {
                uiDecodingTimeStamp = m_sPictInfoList[i].uiDecodingTimeStamp;
                m_sReoderingStatus.iPictInfoIndex = i;
                firstValidIdx = i;
                break;
            }
        }
        for (int32_t i = 0; i <= m_sReoderingStatus.iLargestBufferedPicIndex; ++i)
        {
            if (i == firstValidIdx) continue;
            if (m_sPictInfoList[i].iPOC != IMinInt32 && m_sPictInfoList[i].uiDecodingTimeStamp < uiDecodingTimeStamp)
            {
                uiDecodingTimeStamp = m_sPictInfoList[i].uiDecodingTimeStamp;
                m_sReoderingStatus.iPictInfoIndex = i;
            }
        }
        if (uiDecodingTimeStamp > 0)
        {
            m_sReoderingStatus.iLastWrittenPOC = m_sPictInfoList[m_sReoderingStatus.iPictInfoIndex].iPOC;
            m_sReoderingStatus.iLastWrittenSeqNum = m_sPictInfoList[m_sReoderingStatus.iPictInfoIndex].iSeqNum;
            *pDstInfo = m_sPictInfoList[m_sReoderingStatus.iPictInfoIndex].sBufferInfo;
            ppDst[0] = pDstInfo->pDst[0];
            ppDst[1] = pDstInfo->pDst[1];
            ppDst[2] = pDstInfo->pDst[2];
            m_sPictInfoList[m_sReoderingStatus.iPictInfoIndex].iPOC = IMinInt32;
            if (pCtx != null || m_pPicBuff != null)
            {
                SPicBuff* pPicBuff = pCtx != null ? pCtx->pPicBuff : m_pPicBuff;
                int32_t iPicBuffIdx = m_sPictInfoList[m_sReoderingStatus.iPictInfoIndex].iPicBuffIdx;
                if (pPicBuff != null && iPicBuffIdx >= 0 && iPicBuffIdx < pPicBuff->iCapacity)
                {
                    SPicture* pPic = pPicBuff->ppPic[iPicBuffIdx];
                    --pPic->iRefCount;
                    if (pPic->iRefCount <= 0 && pPic->pSetUnRef != null)
                        pPic->pSetUnRef(pPic);
                }
            }
            --m_sReoderingStatus.iNumOfPicts;
        }
        return;
    }

    private DECODING_STATE ReorderPicturesInDisplay(SWelsDecoderContext* pDecContext, uint8_t** ppDst,
        SBufferInfo* pDstInfo)
    {
        DECODING_STATE iRet = dsErrorFree;
        if (pDecContext->pSps != null)
        {
            m_bIsBaseline = (int32_t)pDecContext->pSps->uiProfileIdc == 66 || (int32_t)pDecContext->pSps->uiProfileIdc == 83;
            if (!m_bIsBaseline)
            {
                if (pDstInfo->iBufferStatus == 1)
                {
                    if (pDecContext->pSliceHeader->eSliceType == B_SLICE &&
                        ((pDecContext->iSeqNum == m_sReoderingStatus.iLastWrittenSeqNum) ?
                            (pDecContext->pSliceHeader->iPicOrderCntLsb <= m_sReoderingStatus.iLastWrittenPOC + 2) :
                            (pDecContext->iSeqNum - m_sReoderingStatus.iLastWrittenSeqNum == 1 && pDecContext->pSliceHeader->iPicOrderCntLsb == 0)))
                    {
                        m_sReoderingStatus.iLastWrittenPOC = pDecContext->pSliceHeader->iPicOrderCntLsb;
                        m_sReoderingStatus.iLastWrittenSeqNum = pDecContext->iSeqNum;
                        //issue #3478, use b-slice type to determine correct picture order as the first priority as POC order is not as reliable as based on b-slice
                        ppDst[0] = pDstInfo->pDst[0];
                        ppDst[1] = pDstInfo->pDst[1];
                        ppDst[2] = pDstInfo->pDst[2];
                        return iRet;
                    }
                    BufferingReadyPicture(pDecContext, ppDst, pDstInfo);
                    if (!m_sReoderingStatus.bHasBSlice && m_sReoderingStatus.iNumOfPicts > 1)
                    {
                        ReleaseBufferedReadyPictureNoReorder(pDecContext, ppDst, pDstInfo);
                    }
                    else
                    {
                        ReleaseBufferedReadyPictureReorder(pDecContext, ppDst, pDstInfo);
                    }
                }
            }
        }
        return iRet;
    }

    public DECODING_STATE DecodeParser(uint8_t* kpSrc, int kiSrcLen, SParserBsInfo* pDstInfo)
    {
        SWelsDecoderContext* pDecContext = m_pDecThrCtx[0].pCtx;

        if (pDecContext == null || pDecContext->pParam == null)
        {
            if (m_pWelsTrace != null)
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "Call DecodeParser without Initialize.\n");
            }
            return dsInitialOptExpected;
        }

        if (!pDecContext->pParam->bParseOnly)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "bParseOnly should be true for this API calling! \n");
            pDecContext->iErrorCode |= (int32_t)dsInvalidArgument;
            return dsInvalidArgument;
        }

        // Unreachable: Initialize rejects bParseOnly, and the rest of DecodeParser is the parse-only path.
        throw new NotSupportedException("Parse-only decoding (DecodeParser) is not supported by this port.");
    }

    public DECODING_STATE DecodeFrame(uint8_t* kpSrc,
        int kiSrcLen,
        uint8_t** ppDst,
        int* pStride,
        ref int iWidth,
        ref int iHeight)
    {
        DECODING_STATE eDecState = dsErrorFree;
        SBufferInfo DstInfo;

        DstInfo = default;
        DstInfo.UsrData.sSystemBuffer.iStride[0] = pStride[0];
        DstInfo.UsrData.sSystemBuffer.iStride[1] = pStride[1];
        DstInfo.UsrData.sSystemBuffer.iWidth = iWidth;
        DstInfo.UsrData.sSystemBuffer.iHeight = iHeight;

        eDecState = DecodeFrame2(kpSrc, kiSrcLen, ppDst, &DstInfo);
        if (eDecState == dsErrorFree)
        {
            pStride[0] = DstInfo.UsrData.sSystemBuffer.iStride[0];
            pStride[1] = DstInfo.UsrData.sSystemBuffer.iStride[1];
            iWidth = DstInfo.UsrData.sSystemBuffer.iWidth;
            iHeight = DstInfo.UsrData.sSystemBuffer.iHeight;
        }

        return eDecState;
    }

    public DECODING_STATE DecodeFrameEx(uint8_t* kpSrc,
        int kiSrcLen,
        uint8_t* pDst,
        int iDstStride,
        ref int iDstLen,
        ref int iWidth,
        ref int iHeight,
        ref int iColorFormat)
    {
        DECODING_STATE state = dsErrorFree;

        return state;
    }

    // ParseAccessUnit and ThreadDecodeFrameInternal: threads only, not ported.
}

internal static unsafe partial class WelsDec
{
    /*
    *       WelsGetDecoderCapability
    *       @return: DecCapability information
    */
    public static int WelsGetDecoderCapability(SDecoderCapability* pDecCapability)
    {
        *pDecCapability = default;
        pDecCapability->iProfileIdc = 66; //Baseline
        pDecCapability->iProfileIop = 0xE0; //11100000b
        pDecCapability->iLevelIdc = 32; //level_idc = 3.2
        pDecCapability->iMaxMbps = 216000; //from level_idc = 3.2
        pDecCapability->iMaxFs = 5120; //from level_idc = 3.2
        pDecCapability->iMaxCpb = 20000; //from level_idc = 3.2
        pDecCapability->iMaxDpb = 20480; //from level_idc = 3.2
        pDecCapability->iMaxBr = 20000; //from level_idc = 3.2
        pDecCapability->bRedPicCap = false; //not support redundant pic

        return ERR_NONE;
    }

    /*
    *   WelsCreateDecoder
    *   @return:    success in return 0, otherwise failed.
    */
    public static long WelsCreateDecoder(CWelsDecoder** ppDecoder)
    {
        if (null == ppDecoder)
        {
            return ERR_INVALID_PARAMETERS;
        }

        *ppDecoder = CWelsDecoder.New();

        if (null == *ppDecoder)
        {
            return ERR_MALLOC_FAILED;
        }

        return ERR_NONE;
    }

    /*
    *   WelsDestroyDecoder
    */
    public static void WelsDestroyDecoder(CWelsDecoder* pDecoder)
    {
        if (null != pDecoder)
        {
            CWelsDecoder.Delete(ref pDecoder);
        }
    }
}
