using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class ConvertKnownNV12ToBGRBenchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _bgrWriter = new();
#endif

    public IEnumerable<BenchmarkParams> GetImages()
    {
        yield return CreateBenchmarkParams("ref-nv12.yuv", 640, 480);

        static BenchmarkParams CreateBenchmarkParams(string image, int width, int height)
        {
            var bytes = Utils.LoadFromFile(image);
            return new BenchmarkParams { Width = width, Height = height, Stride = width * 3, Bytes = bytes };
        }
    }

    [ParamsSource(nameof(GetImages))]
    public BenchmarkParams Image { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_bgrWriter is not null);
        _bgrWriter.Clear();
#endif
    }

    [Benchmark]
    public int ConvertKnownNV12ToBGRArray()
    {
        var bgr = PixelConverter.NV12toBGR(Image.Bytes, Image.Width, Image.Height, Image.Stride);
        return bgr.Length;
    }

    [Benchmark]
    public int ConvertKnownNV12ToBGRBufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_bgrWriter is not null);
        PixelConverter.NV12toBGR(_bgrWriter, Image.Bytes, Image.Width, Image.Height, Image.Stride);
        return _bgrWriter.WrittenCount;
#endif
    }
}
