using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks.Benchmarks;

public class ConvertOddDimensionNV12ToI420Banchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
#endif

    public IEnumerable<BenchmarkParams> GetImages()
    {
        yield return new BenchmarkParams { Width = -1, Height = -1, Stride = -1, Bytes = Utils.CreateOddDimensionBuffer(5, 3) };
    }

    [ParamsSource(nameof(GetImages))]
    public BenchmarkParams Image { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
#if !LibVersion
        Debug.Assert(_i420Writer is not null);
        _i420Writer.Clear();
#endif
    }

    [Benchmark]
    public int ConvertOddDimensionNV12ToI420Array()
    {
        var i420 = PixelConverter.NV12toI420(Image.Bytes, Image.Width, Image.Height);
        return i420.Length;
    }

    [Benchmark]
    public int ConvertOddDimensionNV12ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_i420Writer is not null);
        var i420BytesWritten = PixelConverter.NV12toI420(_i420Writer, Image.Bytes.AsSpan(), Image.Width, Image.Height);
        return i420BytesWritten;
#endif
    }
}
