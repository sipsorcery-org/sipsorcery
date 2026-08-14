using System.Diagnostics;
using System.Drawing;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class RoundtripBgra32ToI420Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _bgrWriter = new();
#endif
    private byte[] _rgba;
    private int _width;
    private int _height;
    private int _stride;

    [GlobalSetup]
    public void GlobalSetup()
    {
        using var bmp = new Bitmap("img/ref-bgra32.bmp");

        _rgba = Utils.BitmapToBuffer(bmp, out var stride);

        _width = bmp.Width;
        _height = bmp.Height;
        _stride = stride;
    }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_bgrWriter is not null);
        _i420Writer.Clear();
        _bgrWriter.Clear();
#endif
    }

    [Benchmark]
    public int RoundtripBgr24ToI420Array()
    {
        Debug.Assert(_rgba is not null);
        var i420 = PixelConverter.RGBAtoI420(_rgba, _width, _height, _stride);
        var bgr = PixelConverter.I420toBGR(i420, _width, _height, out _);
        return i420.Length + bgr.Length;
    }

    [Benchmark]
    public int RoundtripBgr24ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_rgba is not null);
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_bgrWriter is not null);
        PixelConverter.RGBAtoI420(_i420Writer, _rgba.AsSpan(), _width, _height, _stride);
        PixelConverter.I420toBGR(_bgrWriter, _i420Writer.WrittenSpan, _width, _height, out _);
        return _i420Writer.WrittenCount + _bgrWriter.WrittenCount;
#endif
    }
}
