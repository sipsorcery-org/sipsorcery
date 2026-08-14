using System.Diagnostics;
using System.Drawing;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class Roundtrip_BitmapBenchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _rtBgrWriter = new();
#endif
    private byte[]? _bgr;
    BenchmarkParams _params;

    // Define the width/height pairs to test
    record struct BenchmarkParams(int Width, int Height, int Stride);
    public IEnumerable<(int Width, int Height)> DimensionPairs()
    {
        yield return (640, 480);
        yield return (720, 405);
        yield return (719, 404);
        yield return (719, 405);
    }

    [ParamsSource(nameof(DimensionPairs))]
    public (int Width, int Height) Dimensions { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        using var bmp = new Bitmap($"img/testpattern_{Dimensions.Width}x{Dimensions.Height}.bmp");

        _bgr = Utils.BitmapToBuffer(bmp, out var stride);

        _params = new BenchmarkParams(Dimensions.Width, Dimensions.Height, stride);
    }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_rtBgrWriter is not null);
        _i420Writer.Clear();
        _rtBgrWriter.Clear();
#endif
    }

    [Benchmark]
    public int RoundtripBgr24ToI420Array()
    {
        Debug.Assert(_bgr is not null);
        var i420 = PixelConverter.BGRtoI420(_bgr, Dimensions.Width, Dimensions.Height, _params.Stride);
        var rtBgr = PixelConverter.I420toBGR(i420, Dimensions.Width, Dimensions.Height, out _);
        return i420.Length + rtBgr.Length;
    }

    [Benchmark]
    public int RoundtripBgr24ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_bgr is not null);
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_rtBgrWriter is not null);
        PixelConverter.BGRtoI420(_i420Writer, _bgr.AsSpan(), Dimensions.Width, Dimensions.Height, _params.Stride);
        PixelConverter.I420toBGR(_rtBgrWriter, _i420Writer.WrittenSpan, Dimensions.Width, Dimensions.Height, out _);
        return _i420Writer.WrittenCount + _rtBgrWriter.WrittenCount;
#endif
    }
}
