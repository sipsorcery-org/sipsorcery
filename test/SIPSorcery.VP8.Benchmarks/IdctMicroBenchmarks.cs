using System;
using BenchmarkDotNet.Attributes;
using Vpx.Net;

namespace SIPSorcery.VP8.Benchmarks;

/// <summary>Micro-benchmark for the encoder-side IDCT + add + clamp fast path
/// (<see cref="IdctEncoderSimd"/>) compared with the scalar reference
/// <see cref="idctllm.vp8_short_idct4x4llm_c"/>. The encoder calls the IDCT
/// 24 times per macroblock for reconstruction; this measures one block
/// many times to amortize call overhead.</summary>
[SimpleJob]
public unsafe class IdctMicroBenchmarks
{
    private const int N = 4096;
    private short[] _coef = null!;
    private byte[] _pred = null!;
    private byte[] _dst = null!;

    [GlobalSetup]
    public void Setup()
    {
        _coef = new short[16];
        _pred = new byte[16];
        _dst = new byte[16];
        var rng = new Random(11);
        for (int i = 0; i < 16; i++) _coef[i] = (short)rng.Next(-512, 513);
        rng.NextBytes(_pred);
    }

    [Benchmark(Baseline = true)]
    public int Idct4x4_Scalar_Reference_Calls()
    {
        int s = 0;
        fixed (short* cp = _coef)
        fixed (byte* pp = _pred)
        fixed (byte* dp = _dst)
        {
            for (int i = 0; i < N; i++)
            {
                idctllm.vp8_short_idct4x4llm_c(cp, pp, 4, dp, 4);
                s += dp[0];
            }
        }
        return s;
    }

    [Benchmark]
    public int Idct4x4_AddBlock_SimdEncoder_Calls()
    {
        int s = 0;
        fixed (short* cp = _coef)
        fixed (byte* pp = _pred)
        fixed (byte* dp = _dst)
        {
            for (int i = 0; i < N; i++)
            {
                IdctEncoderSimd.Idct4x4AddBlock(cp, pp, 4, dp, 4);
                s += dp[0];
            }
        }
        return s;
    }

    [Benchmark]
    public int Idct4x4_AddFlat_SimdEncoder_Calls()
    {
        int s = 0;
        fixed (short* cp = _coef)
        fixed (byte* dp = _dst)
        {
            for (int i = 0; i < N; i++)
            {
                IdctEncoderSimd.Idct4x4AddFlat(cp, _pred[0], dp, 4);
                s += dp[0];
            }
        }
        return s;
    }
}
