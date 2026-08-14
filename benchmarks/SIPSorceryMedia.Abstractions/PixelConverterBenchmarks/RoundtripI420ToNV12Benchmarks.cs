using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class RoundtripI420ToNV12Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte> _nv12Writer = new();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte> _i420Writer = new();
#endif
    private byte[] _i420;
    private const int Width = 640;
    private const int Height = 480;
    private const int OddWidth = 5;
    private const int OddHeight = 3;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _i420 = File.ReadAllBytes("img/ref-i420.yuv");
    }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_nv12Writer is not null);
        Debug.Assert(_i420Writer is not null);
        _nv12Writer.Clear();
        _i420Writer.Clear();
#endif
    }

    [Benchmark]
    public int RoundtripI420ToNV12Array()
    {
        Debug.Assert(_i420 is not null);
        var nv12 = PixelConverter.I420toNV12(_i420, Width, Height);
        var roundtripI420 = PixelConverter.NV12toI420(nv12, Width, Height);
        return nv12.Length + roundtripI420.Length;
    }

    [Benchmark]
    public int RoundtripI420ToNV12BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_i420 is not null);
        Debug.Assert(_nv12Writer is not null);
        Debug.Assert(_i420Writer is not null);
        PixelConverter.I420toNV12(_nv12Writer, _i420.AsSpan(), Width, Height);
        PixelConverter.NV12toI420(_i420Writer, _nv12Writer.WrittenSpan, Width, Height);
        return _nv12Writer.WrittenCount + _i420Writer.WrittenCount;
#endif
    }
}
