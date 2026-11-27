//-----------------------------------------------------------------------------
// Filename: utils.cs
//
// Description: Port of:
//  - codec/common/inc/utils.h
//  - codec/common/src/utils.cpp
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The one deliberate change: upstream's WelsLog is printf-style (format string
// + va_list, formatted later in the trace sink). Here call sites pass a C#
// interpolated string, captured by WelsLogMessage, which skips the formatting
// entirely when the level is filtered out. So
//   WelsLog (pLogCtx, WELS_LOG_WARNING, "sps_id (%d) out of range", iSpsId);
// is ported as
//   WelsLog(pLogCtx, WELS_LOG_WARNING, $"sps_id ({iSpsId}) out of range");
// SLogContext.pfLog therefore receives the finished message instead of a
// format and va_list.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
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
 *
 * \file    utils.c
 *
 * \brief   common tool/function utilization
 *
 * \date    03/10/2009 Created
 *
 *************************************************************************************
 */

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264;

/// <summary>
/// TagLogContext. pfLog is PWelsLogCallbackFunc with the va_list replaced by the
/// already formatted message (see the file header).
/// </summary>
internal unsafe struct SLogContext
{
    public delegate*<void*, int, string, void> pfLog;
    public void* pLogCtx;
    public void* pCodecInstance;

    /// <summary>
    /// Port addition: asks the sink (pLogCtx) whether a level would be emitted, so
    /// WelsLog can skip formatting. Null means "always format".
    /// </summary>
    public delegate*<void*, int, bool> pfIsEnabled;
}

/// <summary>
/// Collects a WelsLog interpolated message, formatting it only if the log context
/// would emit it at that level.
/// </summary>
[InterpolatedStringHandler]
internal unsafe ref struct WelsLogMessage
{
    private DefaultInterpolatedStringHandler _builder;
    public readonly bool Enabled;

    public WelsLogMessage(int literalLength, int formattedCount, SLogContext* logCtx, int iLevel, out bool shouldAppend)
    {
        Enabled = WelsCommon.WelsLogEnabled(logCtx, iLevel);
        shouldAppend = Enabled;
        _builder = Enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
    }

    public void AppendLiteral(string value) => _builder.AppendLiteral(value);
    public void AppendFormatted<T>(T value) => _builder.AppendFormatted(value);
    public void AppendFormatted<T>(T value, string format) => _builder.AppendFormatted(value, format);
    public void AppendFormatted<T>(T value, int alignment) => _builder.AppendFormatted(value, alignment);
    public void AppendFormatted<T>(T value, int alignment, string format) => _builder.AppendFormatted(value, alignment, format);
    public void AppendFormatted(ReadOnlySpan<char> value) => _builder.AppendFormatted(value);
    public void AppendFormatted(string value) => _builder.AppendFormatted(value);

    public string ToStringAndClear() => Enabled ? _builder.ToStringAndClear() : string.Empty;
}

internal static unsafe partial class WelsCommon
{
    public const int MAX_LOG_SIZE = 1024;
    public const int MAX_MBS_PER_FRAME = 36864; //in accordance with max level support in Rec

    /// <summary>
    /// Port addition: whether a WelsLog at this level would reach the sink, so the
    /// trace level filter upstream applies after formatting is applied before it.
    /// </summary>
    public static bool WelsLogEnabled(SLogContext* logCtx, int iLevel)
    {
        if (logCtx == null || logCtx->pfLog == null)
        {
            return false;
        }
        return logCtx->pfIsEnabled == null || logCtx->pfIsEnabled(logCtx->pLogCtx, iLevel);
    }

    public static void WelsLog(SLogContext* logCtx, int iLevel,
        [InterpolatedStringHandlerArgument(nameof(logCtx), nameof(iLevel))] ref WelsLogMessage kpFmt)
    {
        if (kpFmt.Enabled)
        {
            WelsLog(logCtx, iLevel, kpFmt.ToStringAndClear());
        }
    }

    public static void WelsLog(SLogContext* logCtx, int iLevel, string kpFmt)
    {
        if (!WelsLogEnabled(logCtx, iLevel))
        {
            return;
        }

        string pTraceTag;
        switch (iLevel)
        {
            case WELS_LOG_ERROR:
                pTraceTag = $"[OpenH264] this = 0x{(nint)logCtx->pCodecInstance:X}, Error:";
                break;
            case WELS_LOG_WARNING:
                pTraceTag = $"[OpenH264] this = 0x{(nint)logCtx->pCodecInstance:X}, Warning:";
                break;
            case WELS_LOG_INFO:
                pTraceTag = $"[OpenH264] this = 0x{(nint)logCtx->pCodecInstance:X}, Info:";
                break;
            case WELS_LOG_DEBUG:
                pTraceTag = $"[OpenH264] this = 0x{(nint)logCtx->pCodecInstance:X}, Debug:";
                break;
            default:
                pTraceTag = $"[OpenH264] this = 0x{(nint)logCtx->pCodecInstance:X}, Detail:";
                break;
        }
        string message = pTraceTag + kpFmt;
        if (message.Length >= MAX_LOG_SIZE)
        {
            message = message.Substring(0, MAX_LOG_SIZE - 1);   // WelsVsnprintf truncation
        }
        logCtx->pfLog(logCtx->pLogCtx, iLevel, message);
    }

    /// CONST_FACTOR_PSNR / CALC_PSNR
    private const double CONST_FACTOR_PSNR = 10.0 / 2.302585092994046; // (10.0 / log(10.0))
    private static float CALC_PSNR(int w, int h, long s) => (float)(CONST_FACTOR_PSNR * Math.Log(65025.0 * w * h / s));

    /*!
     *************************************************************************************
     * \brief   PSNR calculation utilization in Wels
     *
     * \param   pTarPic     target picture to be calculated in Picture pData format
     * \param   iTarStride  stride of target picture pData pBuffer
     * \param   pRefPic     base referencing picture samples
     * \param   iRefStride  stride of reference picture pData pBuffer
     * \param   iWidth      picture iWidth in pixel
     * \param   iHeight     picture iHeight in pixel
     *
     * \return  actual PSNR result;
     *
     * \note    N/A
     *************************************************************************************
     */
    public static float WelsCalcPsnr(void* kpTarPic,
                                     int kiTarStride,
                                     void* kpRefPic,
                                     int kiRefStride,
                                     int kiWidth,
                                     int kiHeight)
    {
        long iSqe = 0;
        int x, y;
        byte* pTar = (byte*)kpTarPic;
        byte* pRef = (byte*)kpRefPic;

        if (null == pTar || null == pRef)
            return (-1.0f);

        for (y = 0; y < kiHeight; ++y)
        { // OPTable !!
            for (x = 0; x < kiWidth; ++x)
            {
                int kiT = pTar[y * kiTarStride + x] - pRef[y * kiRefStride + x];
                iSqe += kiT * kiT;
            }
        }
        if (0 == iSqe)
        {
            return (99.99f);
        }
        return CALC_PSNR(kiWidth, kiHeight, iSqe);
    }
}
