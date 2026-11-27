//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Benchmarks for SIPSorcery.H264, the C# port of OpenH264.
//
//   H264Benchmarks [BenchmarkDotNet options]
//       Runs the BenchmarkDotNet benchmarks, for example --filter *Encode*.
//   H264Benchmarks run encode|decode <width>x<height> [seconds] [--c]
//       Encodes or decodes flat out for a while (10 s by default) and reports
//       the frame rate and what was allocated once warmed up. Without
//       BenchmarkDotNet's process juggling, for a profiler (see the README).
//       --c turns the SIMD kernels off.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (Phase 5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Diagnostics;
using BenchmarkDotNet.Running;
using H264Benchmarks;
using SIPSorcery.H264;

if (args.Length > 0 && args[0] == "run")
{
    return Run(args);
}

BenchmarkSwitcher.FromAssembly(typeof(EncodeBenchmarks).Assembly).Run(args);
return 0;

static int Run(string[] args)
{
    bool cKernels = args.Contains("--c");
    args = args.Where(a => a != "--c").ToArray();
    if (args.Length < 3 || (args[1] != "encode" && args[1] != "decode"))
    {
        Console.Error.WriteLine("Usage: H264Benchmarks run encode|decode <width>x<height> [seconds] [--c]");
        return 1;
    }
    if (cKernels)
    {
        WelsCommon.WelsCpuFeatureMask = 0;
    }
    bool encode = args[1] == "encode";
    var (width, height) = Resolutions.Parse(args[2]);
    double seconds = args.Length > 3 ? double.Parse(args[3]) : 10;

    Console.WriteLine($"Generating {EncodeBenchmarks.FRAMES} {width}x{height} frames ...");
    byte[][] frames = TestFrames.Generate(width, height, EncodeBenchmarks.FRAMES);

    using var encoder = new BenchEncoder(width, height);
    using var decoder = new BenchDecoder();
    List<byte[]>? clip = encode ? null : Clips.Encode(width, height, frames);
    long index = 0;
    long bytes = 0;

    unsafe long Step()
    {
        if (encode)
        {
            return encoder.Encode(frames[TestFrames.PingPong(index++, frames.Length)]);
        }
        byte[] au = clip![(int)(index++ % clip.Count)];
        return decoder.Decode(au, out _, out _, out _) != null ? au.Length : 0;
    }

    // A second's worth of warm-up, for tiered compilation and for the codec's buffers.
    var warmup = Stopwatch.StartNew();
    while (warmup.Elapsed.TotalSeconds < 1)
    {
        Step();
    }

    long startFrames = index;
    long startAllocated = GC.GetAllocatedBytesForCurrentThread();
    int startGen0 = GC.CollectionCount(0);
    int startSkipped = encoder.Skipped;
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < seconds)
    {
        bytes += Step();
    }
    sw.Stop();
    long done = index - startFrames;
    long allocated = GC.GetAllocatedBytesForCurrentThread() - startAllocated;

    string kernels = cKernels ? "C kernels" : "SIMD kernels where there are some";
    Console.WriteLine($"{(encode ? "Encoded" : "Decoded")} {done} {width}x{height} frames in {sw.Elapsed.TotalSeconds:0.00} s ({kernels}): " +
        $"{done / sw.Elapsed.TotalSeconds:0.0} frames a second, {sw.Elapsed.TotalMilliseconds / done:0.00} ms a frame.");
    if (encode)
    {
        Console.WriteLine($"{encoder.Skipped - startSkipped} skipped by the rate control, " +
            $"{bytes * 8 / 1000.0 / (done / (double)BenchEncoder.FRAME_RATE):0} kbps at {BenchEncoder.FRAME_RATE} fps.");
    }
    Console.WriteLine($"Allocated {allocated} bytes ({allocated / (double)done:0.0} a frame), {GC.CollectionCount(0) - startGen0} gen0 collections.");
    return 0;
}
