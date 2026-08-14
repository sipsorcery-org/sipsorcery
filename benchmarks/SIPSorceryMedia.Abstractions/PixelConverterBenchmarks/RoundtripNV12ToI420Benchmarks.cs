using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class RoundtripNV12ToI420Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte> _nv12Writer = new CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte> _i420Writer = new CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>();
#endif
    private byte[] _nv12;
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
        Debug.Assert(_nv12Writer is not null);
        Debug.Assert(_i420Writer is not null);
        _nv12Writer.Clear();
        _i420Writer.Clear();
#endif
    }

    [Benchmark]
    public int RoundtripNV12ToI420Array()
    {
        Debug.Assert(_nv12 is not null);
        var i420 = PixelConverter.NV12toI420(_nv12, Width, Height);
        var roundtripNv12 = PixelConverter.I420toNV12(i420, Width, Height);
        return i420.Length + roundtripNv12.Length;
    }

    [Benchmark]
    public int RoundtripNV12ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_nv12 is not null);
        Debug.Assert(_nv12Writer is not null);
        Debug.Assert(_i420Writer is not null);
        PixelConverter.NV12toI420(_i420Writer, _nv12.AsSpan(), Width, Height);
        PixelConverter.I420toNV12(_nv12Writer, _i420Writer.WrittenSpan, Width, Height);
        return _i420Writer.WrittenCount + _nv12Writer.WrittenCount;
#endif
    }
}
