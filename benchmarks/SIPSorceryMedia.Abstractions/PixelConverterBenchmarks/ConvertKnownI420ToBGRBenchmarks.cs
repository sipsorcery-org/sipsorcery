using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class ConvertKnownI420ToBGRBenchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _bgrWriter = new();
#endif
    private byte[]? _i420;
    private int _width = 640;
    private int _height = 480;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _i420 = File.ReadAllBytes("img/ref-i420.yuv");
    }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_bgrWriter is not null);
        _bgrWriter.Clear();
#endif
    }

    [Benchmark]
    public int RoundtripBgr24ToI420Array()
    {
        Debug.Assert(_i420 is not null);
        var bgr = PixelConverter.I420toBGR(_i420, _width, _height, out _);
        return bgr.Length;
    }

    [Benchmark]
    public int RoundtripBgr24ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_i420 is not null);
        Debug.Assert(_bgrWriter is not null);
        PixelConverter.I420toBGR(_bgrWriter, _i420, _width, _height, out _);
        return _bgrWriter.WrittenCount;
#endif
    }
}
