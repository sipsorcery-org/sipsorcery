using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks.Benchmarks;

public class RoundtripBgr24ToI420Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _bgr24Writer = new();
#endif

    public IEnumerable<BenchmarkParams> GetImages()
    {
        yield return CreateBenchmarkParams("ref-bgr24.bmp");

        static BenchmarkParams CreateBenchmarkParams(string image)
        {
            using var bmp = Utils.LoadBitmap(image);
            var bgr = Utils.BitmapToBuffer(bmp, out var stride);
            return new BenchmarkParams { Width = bmp.Width, Height = bmp.Height, Stride = stride, Bytes = bgr };
        }
    }

    [ParamsSource(nameof(GetImages))]
    public BenchmarkParams Image { get; set; }

    [Benchmark]
    public int RoundtripBgr24ToI420Array()
    {
        var i420 = PixelConverter.BGRtoI420(Image.Bytes, Image.Width, Image.Height, Image.Stride);
        var bgr24 = PixelConverter.I420toBGR(i420, Image.Width, Image.Height, out _);
        return i420.Length + bgr24.Length;
    }

    [Benchmark]
    public int RoundtripBgr24ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_bgr24Writer is not null);
        _i420Writer.Clear();
        _bgr24Writer.Clear();
        var i420BytesWritten = PixelConverter.BGRtoI420(_i420Writer, Image.Bytes.AsSpan(), Image.Width, Image.Height, Image.Stride);
        var rtBgrBytesWritten = PixelConverter.I420toBGR(_bgr24Writer, _i420Writer.WrittenSpan, Image.Width, Image.Height, out _);
        return i420BytesWritten + rtBgrBytesWritten;
#endif
    }
}
