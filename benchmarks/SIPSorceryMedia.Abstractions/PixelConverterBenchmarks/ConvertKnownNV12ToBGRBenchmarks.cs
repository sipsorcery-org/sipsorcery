using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class ConvertKnownNV12ToBGRBenchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _bgrWriter = new();
#endif
    private byte[]? _nv12;
    private int _width = 640;
    private int _height = 480;
    private int _stride;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _nv12 = File.ReadAllBytes("img/ref-nv12.yuv");
        _stride = _width * 3;
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
        Debug.Assert(_nv12 is not null);
        var bgr = PixelConverter.NV12toBGR(_nv12, _width, _height, _stride);
        return bgr.Length;
    }

    [Benchmark]
    public int RoundtripBgr24ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_nv12 is not null);
        Debug.Assert(_bgrWriter is not null);
        PixelConverter.NV12toBGR(_bgrWriter, _nv12, _width, _height, _stride);
        return _bgrWriter.WrittenCount;
#endif
    }
}
