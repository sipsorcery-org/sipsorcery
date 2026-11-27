//-----------------------------------------------------------------------------
// Filename: welsCodecTrace_unittest.cs
//
// Description: Unit tests for the ports of codec/common/src/welsCodecTrace.cpp
// and the WelsLog part of codec/common/src/utils.cpp.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;

namespace SIPSorcery.H264.UnitTest;

public unsafe class welsCodecTrace_unittest
{
    private sealed class Sink
    {
        public readonly List<(int Level, string Message)> Messages = new();
    }

    private static void CaptureTrace(void* ctx, int level, string message)
    {
        var sink = (Sink)GCHandle.FromIntPtr((nint)ctx).Target!;
        sink.Messages.Add((level, message));
    }

    /// <summary>Counts how many times it is formatted, to prove filtered logs aren't.</summary>
    private sealed class FormatCounter
    {
        public int Count;
        public override string ToString()
        {
            Count++;
            return "formatted";
        }
    }

    private static (nint trace, GCHandle handle, Sink sink) CreateTrace(int level)
    {
        var sink = new Sink();
        var handle = GCHandle.Alloc(sink);
        welsCodecTrace* trace = welsCodecTrace.New();
        trace->SetTraceLevel(level);
        trace->SetTraceCallback(&CaptureTrace);
        trace->SetTraceCallbackContext((void*)GCHandle.ToIntPtr(handle));
        return ((nint)trace, handle, sink);
    }

    private static void Destroy(nint trace, GCHandle handle)
    {
        welsCodecTrace* p = (welsCodecTrace*)trace;
        welsCodecTrace.Delete(ref p);
        handle.Free();
    }

    [Fact]
    public void Construct_SetsUpstreamDefaults()
    {
        welsCodecTrace* trace = welsCodecTrace.New();
        Assert.Equal(codec_app_def.WELS_LOG_DEFAULT, trace->m_iTraceLevel);
        Assert.True(trace->m_fpTrace != null);
        Assert.True(trace->m_sLogCtx.pLogCtx == trace);
        welsCodecTrace.Delete(ref trace);
        Assert.True(trace == null);
    }

    [Fact]
    public void WelsLog_DeliversMessagesAtOrBelowTheTraceLevel()
    {
        var (trace, handle, sink) = CreateTrace(codec_app_def.WELS_LOG_WARNING);
        SLogContext* ctx = &((welsCodecTrace*)trace)->m_sLogCtx;
        int spsId = 33;

        WelsCommon.WelsLog(ctx, codec_app_def.WELS_LOG_ERROR, $"sps_id ({spsId}) out of range");
        WelsCommon.WelsLog(ctx, codec_app_def.WELS_LOG_WARNING, "plain warning");
        WelsCommon.WelsLog(ctx, codec_app_def.WELS_LOG_INFO, $"info {spsId}");

        Assert.Equal(2, sink.Messages.Count);
        Assert.Equal(codec_app_def.WELS_LOG_ERROR, sink.Messages[0].Level);
        Assert.StartsWith("[OpenH264] this = 0x", sink.Messages[0].Message);
        Assert.EndsWith(", Error:sps_id (33) out of range", sink.Messages[0].Message);
        Assert.EndsWith(", Warning:plain warning", sink.Messages[1].Message);

        Destroy(trace, handle);
    }

    [Fact]
    public void WelsLog_DoesNotFormatFilteredMessages()
    {
        var (trace, handle, sink) = CreateTrace(codec_app_def.WELS_LOG_ERROR);
        SLogContext* ctx = &((welsCodecTrace*)trace)->m_sLogCtx;
        var counter = new FormatCounter();

        WelsCommon.WelsLog(ctx, codec_app_def.WELS_LOG_DEBUG, $"value {counter}");
        Assert.Equal(0, counter.Count);
        Assert.Empty(sink.Messages);

        WelsCommon.WelsLog(ctx, codec_app_def.WELS_LOG_ERROR, $"value {counter}");
        Assert.Equal(1, counter.Count);
        Assert.Single(sink.Messages);

        Destroy(trace, handle);
    }

    [Fact]
    public void WelsLog_CopiedContextStillReachesTheTrace()
    {
        // The decoder copies the trace's SLogContext by value into its own context
        // (decoder.cpp: pCtx->sLogCtx = *pLogCtx); the copy must still work.
        var (trace, handle, sink) = CreateTrace(codec_app_def.WELS_LOG_INFO);
        SLogContext copy = ((welsCodecTrace*)trace)->m_sLogCtx;

        WelsCommon.WelsLog(&copy, codec_app_def.WELS_LOG_INFO, "from copy");

        Assert.Single(sink.Messages);
        Destroy(trace, handle);
    }

    [Fact]
    public void WelsLog_QuietLevelSuppressesEverything()
    {
        var (trace, handle, sink) = CreateTrace(codec_app_def.WELS_LOG_QUIET);
        SLogContext* ctx = &((welsCodecTrace*)trace)->m_sLogCtx;

        WelsCommon.WelsLog(ctx, codec_app_def.WELS_LOG_ERROR, "error");

        Assert.Empty(sink.Messages);
        Destroy(trace, handle);
    }
}
