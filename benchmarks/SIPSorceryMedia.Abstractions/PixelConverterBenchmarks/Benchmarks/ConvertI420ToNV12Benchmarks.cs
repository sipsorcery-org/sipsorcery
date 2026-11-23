using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks.Benchmarks;

public class ConvertI420ToNV12Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _nv12Writer = new();
#endif

    public IEnumerable<BenchmarkParams> GetImages()
    {
        yield return CreateBenchmarkParams("ref-i420.yuv", 640, 480);

        static BenchmarkParams CreateBenchmarkParams(string image, int width, int height)
        {
            var bytes = Utils.LoadFromFile(image);
            return new BenchmarkParams { Width = width, Height = height, Stride = -1, Bytes = bytes };
        }
    }

    [ParamsSource(nameof(GetImages))]
    public BenchmarkParams Image { get; set; }

    [Benchmark]
    public int ConvertI420ToNV12Array()
    {
        var nv12 = PixelConverter.I420toNV12(Image.Bytes, Image.Width, Image.Height);
        return nv12.Length;
    }

    [Benchmark]
    public int ConvertI420ToNV12BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_nv12Writer is not null);
        _nv12Writer.Clear();
        var nv12BytesWritten = PixelConverter.I420toNV12(_nv12Writer, Image.Bytes.AsSpan(), Image.Width, Image.Height);
        return nv12BytesWritten;
#endif
    }
}
