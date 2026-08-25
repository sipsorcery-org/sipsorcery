using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks.Benchmarks;

public class ConvertNv12ToI420Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
#endif

    public IEnumerable<BenchmarkParams> GetImages()
    {
        yield return CreateBenchmarkParams("ref-nv12.yuv", 640, 480);

        static BenchmarkParams CreateBenchmarkParams(string image, int width, int height)
        {
            var bytes = Utils.LoadFromFile(image);
            return new BenchmarkParams { Width = width, Height = height, Stride = -1, Bytes = bytes };
        }
    }

    [ParamsSource(nameof(GetImages))]
    public BenchmarkParams Image { get; set; }

    [Benchmark]
    public int ConvertNV12ToI420Array()
    {
        var i420 = PixelConverter.NV12toI420(Image.Bytes, Image.Width, Image.Height);
        return i420.Length;
    }

    [Benchmark]
    public int ConvertNV12ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_i420Writer is not null);
        _i420Writer.Clear();
        var i420BytesWritten = PixelConverter.NV12toI420(_i420Writer, Image.Bytes.AsSpan(), Image.Width, Image.Height);
        return i420BytesWritten;
#endif
    }
}
