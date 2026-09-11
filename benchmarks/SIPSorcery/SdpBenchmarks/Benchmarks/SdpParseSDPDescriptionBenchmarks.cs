using BenchmarkDotNet.Attributes;
using SIPSorcery.Net;

namespace SdpBenchmarks.Benchmarks;

public class SdpParseSDPDescriptionBenchmarks
{
    public IEnumerable<BenchmarkParams> GetScenarios() => BenchmarkParams.GetScenarios();

    [ParamsSource(nameof(GetScenarios))]
    public required BenchmarkParams Scenario { get; set; }

    [Benchmark]
    public SDP? SdpParseSDPDescription()
    {
        var sdp = SDP.ParseSDPDescription(Scenario.SdpText);
        return sdp;
    }
}
