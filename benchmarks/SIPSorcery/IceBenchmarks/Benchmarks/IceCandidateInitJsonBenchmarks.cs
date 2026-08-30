using BenchmarkDotNet.Attributes;
using SIPSorcery.Net;

namespace IceBenchmarks.Benchmarks;

public class IceCandidateInitJsonBenchmarks
{
    private RTCIceCandidateInit? _candidateInit = null;

    public IEnumerable<BenchmarkInput> Inputs()
    {
        yield return new("UDP host", "{\"candidate\":\"candidate:1 1 udp 2130706431 192.0.2.10 5000 typ host generation 0\",\"sdpMid\":\"0\",\"sdpMLineIndex\":0,\"usernameFragment\":\"ufrag\"}");
        yield return new("UDP server reflexive", "{\"candidate\":\"candidate:2 1 udp 1677734910 203.0.113.1 50000 typ srflx raddr 192.168.1.10 rport 8998 generation 0\",\"sdpMid\":\"audio\",\"sdpMLineIndex\":1,\"usernameFragment\":\"ufrag\"}");
        yield return new("TCP relay", "{\"candidate\":\"candidate:3 1 tcp 1518280447 203.0.113.20 443 typ relay tcptype passive raddr 192.168.1.10 rport 5000 generation 0\",\"sdpMid\":\"video\",\"sdpMLineIndex\":2,\"usernameFragment\":\"ufrag\"}");
    }

    [ParamsSource(nameof(Inputs))]
    public required BenchmarkInput Input { get; set; }

    [GlobalSetup]
    public void GlobalSetup() => RTCIceCandidateInit.TryParse(Input.Value, out _candidateInit);

    [Benchmark]
    public RTCIceCandidateInit? TryParse()
    {
        RTCIceCandidateInit.TryParse(Input.Value, out var candidateInit);
        return candidateInit;
    }

    [Benchmark]
    public string ToJson() => _candidateInit!.toJSON();
}
