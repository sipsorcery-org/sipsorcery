using BenchmarkDotNet.Attributes;
using SIPSorcery.Net;

namespace IceBenchmarks.Benchmarks;

public class IceCandidateEqualityBenchmarks
{
#if !LibVersion
    private readonly System.Text.StringBuilder _builder = new();
#endif
    private RTCIceCandidate _candidate1 = null!;
    private RTCIceCandidate _candidate2 = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _candidate1 = RTCIceCandidate.Parse("candidate:3 1 tcp 1518280447 203.0.113.20 443 typ relay tcptype passive raddr 192.168.1.10 rport 5000 generation 0");
        _candidate2 = RTCIceCandidate.Parse("candidate:3 1 tcp 1518280447 203.0.113.20 443 typ relay tcptype passive raddr 192.168.1.10 rport 5000 generation 0");
    }

    [Benchmark]
    public bool Equals_ToString() => _candidate1.ToString() == _candidate2.ToString();

    [Benchmark]
    public bool Equals_Equatable()
    {
#if LibVersion
        return false;
#else
        return _candidate1 == _candidate2;
#endif
    }
}
