using System;
using BenchmarkDotNet.Attributes;
using Vpx.Net;

namespace SIPSorcery.VP8.Benchmarks;

/// <summary>Micro-benchmark for forward 4×4 DCT in isolation (encoder hot path).</summary>
[SimpleJob]
public unsafe class FdctMicroBenchmarks
{
    private const int N = 4096;
    private short[] _block = null!;
    private short[] _out = null!;

    [GlobalSetup]
    public void Setup()
    {
        _block = new short[16];
        _out = new short[16];
        new Random(7).NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_block.AsSpan()));
    }

    /// <summary>Many contiguous 4×4 transforms (pitch 8 bytes) to amortize call overhead.</summary>
    [Benchmark]
    public int Fdct4x4_Contiguous_Calls()
    {
        int sum = 0;
        fixed (short* b = _block)
        fixed (short* o = _out)
        {
            for (int i = 0; i < N; i++)
            {
                dct.vp8_short_fdct4x4_c(b, o, pitch: 8);
                sum += o[0];
            }
        }

        return sum;
    }
}
