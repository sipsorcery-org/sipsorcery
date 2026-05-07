using System;
using System.Diagnostics;
using BenchmarkDotNet.Running;
using Vpx.Net;

namespace SIPSorcery.VP8.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--dump-profiler", StringComparison.Ordinal))
        {
            DumpProfilerSample();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }

    /// <summary>One optimized keyframe per resolution with <see cref="EncodeProfiler"/> enabled; prints wall vs scoped sum + phase breakdown.</summary>
    private static void DumpProfilerSample()
    {
        RunDumpProfilerRow(640, 480, seed: 42);
        RunDumpProfilerRow(1280, 720, seed: 43);
    }

    private static void RunDumpProfilerRow(int w, int h, int seed)
    {
        int y = w * h, uv = y / 4;
        var i420 = new byte[y + 2 * uv];
        new Random(seed).NextBytes(i420);
        var buffers = new FrameEncoderBuffers();

        EncodeProfiler.Reset();
        EncodeProfiler.Enabled = true;
        try
        {
            var sw = Stopwatch.StartNew();
            _ = OptimizedVp8FrameEncodePipeline.Instance.EncodeKeyframeContiguousI420Pooled(i420, w, h, 32, buffers);
            sw.Stop();
            Console.WriteLine($"--- {w}×{h} keyframe q=32 ---");
            Console.WriteLine(EncodeProfiler.FormatWallClockCompare(sw.Elapsed.TotalMilliseconds));
            Console.WriteLine(EncodeProfiler.GetReport());
        }
        finally
        {
            EncodeProfiler.Enabled = false;
        }
    }
}
