using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks.Benchmarks;

public class RoundtripI420ToNV12Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte> _nv12Writer = new();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte> _i420Writer = new();
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
        Debug.Assert(_nv12Writer is not null);
        Debug.Assert(_i420Writer is not null);
        _nv12Writer.Clear();
        _i420Writer.Clear();
#endif
    }

    [Benchmark]
    public int RoundtripI420ToNV12Array()
    {
        var nv12 = PixelConverter.I420toNV12(Image.Bytes, Image.Width, Image.Height);
        var roundtripI420 = PixelConverter.NV12toI420(nv12, Image.Width, Image.Height);
        return nv12.Length + roundtripI420.Length;
    }

    [Benchmark]
    public int RoundtripI420ToNV12BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_nv12Writer is not null);
        Debug.Assert(_i420Writer is not null);
        var nv12bytesWritten = PixelConverter.I420toNV12(_nv12Writer, Image.Bytes.AsSpan(), Image.Width, Image.Height);
        var rtI420BytesWritten = PixelConverter.NV12toI420(_i420Writer, _nv12Writer.WrittenSpan, Image.Width, Image.Height);
        return nv12bytesWritten + rtI420BytesWritten;
#endif
    }
}
