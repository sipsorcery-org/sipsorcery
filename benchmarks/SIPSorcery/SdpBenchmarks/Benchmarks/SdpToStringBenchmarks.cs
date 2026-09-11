using System.Diagnostics;
using BenchmarkDotNet.Attributes;

namespace SdpBenchmarks.Benchmarks;

public class SdpToStringBenchmarks
{
#if !LibVersion
    System.Text.StringBuilder? _builder = new();
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
    public string? SdpWriteString()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_builder is not null);
        _builder.Clear();
        Scenario.Sdp.WriteString(_builder);
        return _builder.ToString();
#endif
    }
}
