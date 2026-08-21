using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class Roundtrip_BitmapBenchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _rtBgrWriter = new();
#endif

    public IEnumerable<BenchmarkParams> GetImages()
    {
        yield return CreateBenchmarkParams(640, 480);
        yield return CreateBenchmarkParams(720, 405);
        yield return CreateBenchmarkParams(719, 404);
        yield return CreateBenchmarkParams(719, 405);

        static BenchmarkParams CreateBenchmarkParams(int width, int height)
        {
            var bmp = Utils.LoadBitmap($"testpattern_{width}x{height}.bmp");
            var bgr = Utils.BitmapToBuffer(bmp, out var stride);
            return new BenchmarkParams { Width = width, Height = height, Stride = stride, Bytes = bgr };
        }
    }

    [ParamsSource(nameof(GetImages))]
    public BenchmarkParams Image { get; set; }

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
        var i420 = PixelConverter.BGRtoI420(Image.Bytes, Image.Width, Image.Height, Image.Stride);
        var rtBgr = PixelConverter.I420toBGR(i420, Image.Width, Image.Height, out _);
        return i420.Length + rtBgr.Length;
    }

    [Benchmark]
    public int RoundtripBgr24ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_rtBgrWriter is not null);
        PixelConverter.BGRtoI420(_i420Writer, Image.Bytes.AsSpan(), Image.Width, Image.Height, Image.Stride);
        PixelConverter.I420toBGR(_rtBgrWriter, _i420Writer.WrittenSpan, Image.Width, Image.Height, out _);
        return _i420Writer.WrittenCount + _rtBgrWriter.WrittenCount;
#endif
    }
}
