using BenchmarkDotNet.Attributes;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace IceBenchmarks.Benchmarks;

public class RTCSessionDescriptionInitHttpContentBenchmarks
{
    private RTCSessionDescriptionInit? _candidateInit = null;

    public IEnumerable<BenchmarkInput> Inputs()
    {
        yield return new("""UDP host""", """{"type":"answer","candidate":"candidate:1 1 udp 2130706431 192.0.2.10 5000 typ host generation 0","sdpMid":"0","sdpMLineIndex":0,"usernameFragment":"ufrag"}""");
        yield return new("""UDP server reflexive""", """{"type":"answer","candidate":"candidate:2 1 udp 1677734910 203.0.113.1 50000 typ srflx raddr 192.168.1.10 rport 8998 generation 0","sdpMid":"audio","sdpMLineIndex":1,"usernameFragment":"ufrag"}""");
        yield return new("""TCP relay""", """{"type":"answer","candidate":"candidate:3 1 tcp 1518280447 203.0.113.20 443 typ relay tcptype passive raddr 192.168.1.10 rport 5000 generation 0","sdpMid":"video","sdpMLineIndex":2,"usernameFragment":"ufrag"}""");
    }

    [ParamsSource(nameof(Inputs))]
    public required BenchmarkInput Input { get; set; }

    [GlobalSetup]
    public void GlobalSetup() => RTCSessionDescriptionInit.TryParse(Input.Value, out _candidateInit);

    [Benchmark]
    public Stream HttpContent_String()
    {
        using var content = new StringContent(_candidateInit.toJSON());
        var stream = content.ReadAsStream();
        stream.CopyTo(Stream.Null);
        return stream;
    }

    [Benchmark]
    public Stream HttpContent_Json()
    {
#if LibVersion
        return Stream.Null;
#else
        using var content = global::System.Net.Http.Json.JsonContent.Create(inputValue: _candidateInit, mediaType: null, options: SipSorceryJsonSerializerContext.Default.Options);
        var stream = content.ReadAsStream();
        stream.CopyTo(Stream.Null);
        return stream;
#endif
    }
}
