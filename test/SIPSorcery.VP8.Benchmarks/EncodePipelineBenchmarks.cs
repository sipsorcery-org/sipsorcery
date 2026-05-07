using BenchmarkDotNet.Attributes;
using Vpx.Net;

namespace SIPSorcery.VP8.Benchmarks;

/// <summary>
/// E2E-ish pipeline timings (Release, warmed BDN). Example host: Apple M1 Max, .NET 10.0.3 —
/// <see cref="Keyframe_Optimized_1280x720"/> mean ~78 ms (DefaultJob); 640×480 <see cref="Keyframe_Optimized"/> mean ~26 ms.
/// Run <c>dotnet run -c Release --project test/SIPSorcery.VP8.Benchmarks -- -f *Keyframe_Optimized* -j short</c> on your machine.
/// <c>Program --dump-profiler</c> prints wall vs scoped-sum for 640×480 and 1280×720 with <see cref="EncodeProfiler"/> enabled (slower; for bucket attribution).
/// </summary>
[SimpleJob]
[MemoryDiagnoser]
public class EncodePipelineBenchmarks
{
    private byte[] _i420640 = null!;
    private byte[] _i4201280 = null!;
    /// <summary>Second frame bytes for P-frame encode (after keyframe seeds LAST_FRAME).</summary>
    private byte[] _i420Inter = null!;
    private FrameEncoderBuffers _bufLegacy = null!;
    private FrameEncoderBuffers _bufOpt = null!;
    private FrameEncoderBuffers _bufMp4 = null!;
    private FrameEncoderBuffers _bufMp4_1280 = null!;
    private IVp8FrameEncodePipeline _optMp4 = null!;

    [GlobalSetup]
    public void Setup()
    {
        FillRandomI420(640, 480, seed: 42, out _i420640);
        FillRandomI420(1280, 720, seed: 44, out _i4201280);
        // Inter frame: small per-pixel perturbation of the keyframe so the
        // residual is realistic rather than pathologically random (which
        // overflows the boolhuff partition buffer in the encoder).
        _i420Inter = new byte[_i420640.Length];
        var rng = new Random(43);
        for (int i = 0; i < _i420640.Length; i++)
        {
            int delta = rng.Next(-8, 9);
            int v = _i420640[i] + delta;
            if (v < 0) v = 0; else if (v > 255) v = 255;
            _i420Inter[i] = (byte)v;
        }
        _bufLegacy = new FrameEncoderBuffers();
        _bufOpt = new FrameEncoderBuffers();
        _bufMp4 = new FrameEncoderBuffers();
        _bufMp4_1280 = new FrameEncoderBuffers();
        // Optimized pipeline configured for 4 token partitions (log2 = 2).
        _optMp4 = Vp8FrameEncodePipelineFactory.Create(Vp8EncodePipelineKind.Optimized, log2NumTokenPartitions: 2);
    }

    private static void FillRandomI420(int w, int h, int seed, out byte[] i420)
    {
        int y = w * h, uv = y / 4;
        i420 = new byte[y + 2 * uv];
        new Random(seed).NextBytes(i420);
    }

    [Benchmark(Baseline = true)]
    public int Keyframe_Legacy() =>
        LegacyVp8FrameEncodePipeline.Instance.EncodeKeyframeContiguousI420Pooled(
            _i420640, 640, 480, 32, _bufLegacy).Count;

    [Benchmark]
    public int Keyframe_Optimized() =>
        OptimizedVp8FrameEncodePipeline.Instance.EncodeKeyframeContiguousI420Pooled(
            _i420640, 640, 480, 32, _bufOpt).Count;

    [Benchmark]
    public int Keyframe_Legacy_1280x720() =>
        LegacyVp8FrameEncodePipeline.Instance.EncodeKeyframeContiguousI420Pooled(
            _i4201280, 1280, 720, 32, _bufLegacy).Count;

    [Benchmark]
    public int Keyframe_Optimized_1280x720() =>
        OptimizedVp8FrameEncodePipeline.Instance.EncodeKeyframeContiguousI420Pooled(
            _i4201280, 1280, 720, 32, _bufOpt).Count;

    /// <summary>Restores LAST_FRAME from <see cref="_i420640"/> before each inter iteration.
    /// Uses the matching pipeline so each pipeline's reconstruction state is consistent
    /// across the I-frame seed and the P-frame measurement (previous setup primed both
    /// pipelines on every iteration, doubling the measured cost).</summary>
    [IterationSetup(Target = nameof(Inter_Legacy))]
    public void InterLegacyIterationSetup()
    {
        _ = LegacyVp8FrameEncodePipeline.Instance.EncodeKeyframeContiguousI420Pooled(
            _i420640, 640, 480, 32, _bufLegacy);
    }

    [IterationSetup(Target = nameof(Inter_Optimized))]
    public void InterOptimizedIterationSetup()
    {
        _ = OptimizedVp8FrameEncodePipeline.Instance.EncodeKeyframeContiguousI420Pooled(
            _i420640, 640, 480, 32, _bufOpt);
    }

    [Benchmark]
    public int Inter_Legacy() =>
        LegacyVp8FrameEncodePipeline.Instance.EncodeInterFrameContiguousI420Pooled(
            _i420Inter, 640, 480, 32, _bufLegacy).Count;

    [Benchmark]
    public int Inter_Optimized() =>
        OptimizedVp8FrameEncodePipeline.Instance.EncodeInterFrameContiguousI420Pooled(
            _i420Inter, 640, 480, 32, _bufOpt).Count;

    // ----- Multi-token-partition (log2N=2 -> 4 partitions packed in parallel) -----

    [Benchmark]
    public int Keyframe_Optimized_4Part() =>
        _optMp4.EncodeKeyframeContiguousI420Pooled(_i420640, 640, 480, 32, _bufMp4).Count;

    [Benchmark]
    public int Keyframe_Optimized_4Part_1280x720() =>
        _optMp4.EncodeKeyframeContiguousI420Pooled(_i4201280, 1280, 720, 32, _bufMp4_1280).Count;

    [IterationSetup(Target = nameof(Inter_Optimized_4Part))]
    public void InterOpt4PartIterationSetup()
    {
        _ = _optMp4.EncodeKeyframeContiguousI420Pooled(_i420640, 640, 480, 32, _bufMp4);
    }

    [Benchmark]
    public int Inter_Optimized_4Part() =>
        _optMp4.EncodeInterFrameContiguousI420Pooled(_i420Inter, 640, 480, 32, _bufMp4).Count;

    /// <summary>
    /// One optimized keyframe with <see cref="EncodeProfiler"/> enabled; returns the text report
    /// (for local phase attribution — timing includes encode plus report formatting).
    /// </summary>
    [Benchmark]
    public string Keyframe_Optimized_EncodeProfilerReport()
    {
        EncodeProfiler.Reset();
        EncodeProfiler.Enabled = true;
        try
        {
            _ = OptimizedVp8FrameEncodePipeline.Instance.EncodeKeyframeContiguousI420Pooled(
                _i420640, 640, 480, 32, _bufOpt);
            return EncodeProfiler.GetReport();
        }
        finally
        {
            EncodeProfiler.Enabled = false;
        }
    }
}
