using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class ConvertOddDimensionI420ToNV12Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _nv12Writer = new();
#endif
    private byte[]? _oddI420;
    private const int OddWidth = 5;
    private const int OddHeight = 3;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var i420 = File.ReadAllBytes("img/ref-i420.yuv");
        _oddI420 = Utils.CreateOddDimensionBuffer(OddWidth, OddHeight);
    }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_nv12Writer is not null);
        _nv12Writer.Clear();
#endif
    }

    [Benchmark]
    public int ConvertOddDimensionI420ToNV12Array()
    {
        Debug.Assert(_oddI420 is not null);
        var nv12 = PixelConverter.I420toNV12(_oddI420, OddWidth, OddHeight);
        return nv12.Length;
    }

    [Benchmark]
    public int ConvertOddDimensionI420ToNV12BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_nv12Writer is not null);
        Debug.Assert(_oddI420 is not null);
        PixelConverter.I420toNV12(_nv12Writer, _oddI420.AsSpan(), OddWidth, OddHeight);
        return _nv12Writer.WrittenCount;
#endif
    }
}
