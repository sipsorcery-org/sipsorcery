using System.Diagnostics;
using System.Drawing;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class RoundtripBgr24ToI420Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _bgr24Writer = new();
#endif
    private byte[]? _bgr;
    private int _width;
    private int _height;
    private int _stride;

    [GlobalSetup]
    public void GlobalSetup()
    {
        using var bmp = new Bitmap("img/ref-bgr24.bmp");

        _bgr = Utils.BitmapToBuffer(bmp, out var stride);

        _width = bmp.Width;
        _height = bmp.Height;
        _stride = stride;
    }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_bgr24Writer is not null);
        _i420Writer.Clear();
        _bgr24Writer.Clear();
#endif
    }

    [Benchmark]
    public int RoundtripBgr24ToI420Array()
    {
        Debug.Assert(_bgr is not null);
        var i420 = PixelConverter.BGRtoI420(_bgr, _width, _height, _stride);
        var bgr24 = PixelConverter.I420toBGR(i420, _width, _height, out _);
        return i420.Length + bgr24.Length;
    }

    [Benchmark]
    public int RoundtripBgr24ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_bgr is not null);
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_bgr24Writer is not null);
        PixelConverter.BGRtoI420(_i420Writer, _bgr.AsSpan(), _width, _height, _stride);
        PixelConverter.I420toBGR(_bgr24Writer, _i420Writer.WrittenSpan, _width, _height, out _);
        return _i420Writer.WrittenCount + _bgr24Writer.WrittenCount;
#endif
    }
}
