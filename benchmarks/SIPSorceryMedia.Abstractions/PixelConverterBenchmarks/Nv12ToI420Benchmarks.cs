using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class Nv12ToI420Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
#endif
    private byte[]? _nv12;
    private const int Width = 640;
    private const int Height = 480;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _nv12 = File.ReadAllBytes("img/ref-nv12.yuv");
    }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_i420Writer is not null);
        _i420Writer.Clear();
#endif
    }

    [Benchmark]
    public int ConvertNV12ToI420Array()
    {
        Debug.Assert(_nv12 is not null);
        var i420 = PixelConverter.NV12toI420(_nv12, Width, Height);
        return i420.Length;
    }

    [Benchmark]
    public int ConvertNV12ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_nv12 is not null);
        Debug.Assert(_i420Writer is not null);
        PixelConverter.NV12toI420(_i420Writer, _nv12.AsSpan(), Width, Height);
        return _i420Writer.WrittenCount;
#endif
    }
}
