using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class ConvertKnownI420ToBGRBenchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _bgrWriter = new();
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

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_bgrWriter is not null);
        _bgrWriter.Clear();
#endif
    }

    [Benchmark]
    public int ConvertKnownI420ToBGRArray()
    {
        var bgr = PixelConverter.I420toBGR(Image.Bytes, Image.Width, Image.Height, out _);
        return bgr.Length;
    }

    [Benchmark]
    public int ConvertKnownI420ToBGRBufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_bgrWriter is not null);
        PixelConverter.I420toBGR(_bgrWriter, Image.Bytes, Image.Width, Image.Height, out _);
        return _bgrWriter.WrittenCount;
#endif
    }
}
