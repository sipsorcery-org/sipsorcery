using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks.Benchmarks;

public class RoundtripNV12ToI420Benchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte> _nv12Writer = new CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>();
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte> _i420Writer = new CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>();
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
    public int RoundtripNV12ToI420Array()
    {
        var i420 = PixelConverter.NV12toI420(Image.Bytes, Image.Width, Image.Height);
        var roundtripNv12 = PixelConverter.I420toNV12(i420, Image.Width, Image.Height);
        return i420.Length + roundtripNv12.Length;
    }

    [Benchmark]
    public int RoundtripNV12ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_nv12Writer is not null);
        Debug.Assert(_i420Writer is not null);
        var i420BytesWritten = PixelConverter.NV12toI420(_i420Writer, Image.Bytes.AsSpan(), Image.Width, Image.Height);
        var rtNv12BytesWritten = PixelConverter.I420toNV12(_nv12Writer, _i420Writer.WrittenSpan, Image.Width, Image.Height);
        return i420BytesWritten + rtNv12BytesWritten;
#endif
    }
}
