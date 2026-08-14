using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SIPSorceryMedia.Abstractions;

namespace PixelConverterBenchmarks;

public class ConvertOddDimensionNV12ToI420Banchmarks
{
#if !LibVersion
    CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter<byte>? _i420Writer = new();
#endif
    private byte[]? _oddNv12;
    private const int OddWidth = 5;
    private const int OddHeight = 3;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _oddNv12 = Utils.CreateOddDimensionBuffer(OddWidth, OddHeight);
    }

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
        Debug.Assert(_oddNv12 is not null);
        var i420 = PixelConverter.NV12toI420(_oddNv12, OddWidth, OddHeight);
        return i420.Length;
    }

    [Benchmark]
    public int ConvertOddDimensionNV12ToI420BufferWriter()
    {
#if LibVersion
        return 0;
#else
        Debug.Assert(_i420Writer is not null);
        Debug.Assert(_oddNv12 is not null);
        PixelConverter.NV12toI420(_i420Writer, _oddNv12.AsSpan(), OddWidth, OddHeight);
        return _i420Writer.WrittenCount;
#endif
    }
}
