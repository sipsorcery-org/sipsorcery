using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class RoundtripBgra32ToI420Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _bgrWriter = new();
#endif

    public IEnumerable<BenchmarkParams> GetImages()
    {
        yield return CreateBenchmarkParams("ref-bgra32.bmp");
        yield return CreateBenchmarkParams("ref-bgra32-1920x1080.bmp");

        static BenchmarkParams CreateBenchmarkParams(string image)
        {
            using var bmp = Utils.LoadBitmap(image);
            var rgba = Utils.BitmapToBuffer(bmp, out var stride);
            return new BenchmarkParams { Width = bmp.Width, Height = bmp.Height, Stride = stride, Bytes = rgba };
        }
    }

    [ParamsSource(nameof(GetImages))]
    public BenchmarkParams Image { get; set; }

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
    public int RoundtripBgra32ToI420Array()
    {
        var i420 = PixelConverter.RGBAtoI420(Image.Bytes, Image.Width, Image.Height, Image.Stride);
        var bgr = PixelConverter.I420toBGR(i420, Image.Width, Image.Height, out _);
        return i420.Length + bgr.Length;
    }

    [Benchmark]
    public int RoundtripBgra32ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_bgrWriter is not null);
        PixelConverter.RGBAtoI420(_i420Writer, Image.Bytes.AsSpan(), Image.Width, Image.Height, Image.Stride);
        PixelConverter.I420toBGR(_bgrWriter, _i420Writer.WrittenSpan, Image.Width, Image.Height, out _);
        return _i420Writer.WrittenCount + _bgrWriter.WrittenCount;
#endif
    }
}
