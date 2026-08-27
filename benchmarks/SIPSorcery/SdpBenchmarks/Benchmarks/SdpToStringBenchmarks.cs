using System.Diagnostics;
using BenchmarkDotNet.Attributes;

namespace SdpBenchmarks.Benchmarks;

public class SdpToStringBenchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<char>? _writer = new(4096);
#endif

    public IEnumerable<BenchmarkParams> GetScenarios() => BenchmarkParams.GetScenarios();

    [ParamsSource(nameof(GetScenarios))]
    public required BenchmarkParams Scenario { get; set; }

    [Benchmark]
    public string? SdpToString()
    {
        return Scenario.Sdp.ToString();
    }

    [Benchmark]
    public int SdpWriteString()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_writer is not null);
        _writer.Clear();
        Scenario.Sdp.WriteString(_writer);
        return _writer.WrittenCount;
#endif
    }
}
