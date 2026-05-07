using System;
using BenchmarkDotNet.Attributes;
using Vpx.Net;

namespace SIPSorcery.VP8.Benchmarks;

/// <summary>Micro-benchmark for token packing (partition 1 coefficient entropy).</summary>
[SimpleJob]
public unsafe class PackTokensMicroBenchmarks
{
    private TOKENEXTRA[] _tokens = null!;
    private byte[] _outBuf = null!;
    private BOOL_CODER _bc;

    [GlobalSetup]
    public void Setup()
    {
        tokenize.EnsureCoefProbCache(default_coef_probs_c.default_coef_probs);

        // Dense coefficient pattern -> non-trivial token count per block.
        short[] q =
        {
            8, -4, 2, -1,
            3, 0, 5, -2,
            -1, 7, 0, 3,
            2, -3, 1, 0,
        };

        const int totalTokens = 384;
        _tokens = new TOKENEXTRA[totalTokens];
        var blockBuf = new TokenStreamBuffer();
        int w = 0;
        while (w < totalTokens)
        {
            blockBuf.Clear();
            _ = tokenize.vp8_tokenize_block(
                q, firstCoeffIndex: 0, eob: 16,
                blockType: 3, initialContext: 0,
                default_coef_probs_c.default_coef_probs, blockBuf);
            ReadOnlySpan<TOKENEXTRA> span = blockBuf.AsSpan();
            for (int i = 0; i < span.Length && w < totalTokens; i++)
                _tokens[w++] = span[i];
        }

        _outBuf = new byte[8192];
    }

    [Benchmark]
    public uint PackTokens_FixedStream()
    {
        fixed (byte* p = _outBuf)
        {
            boolhuff.vp8_start_encode(ref _bc, p, p + _outBuf.Length);
            bitstream.vp8_pack_tokens(ref _bc, _tokens.AsSpan());
            boolhuff.vp8_stop_encode(ref _bc);
        }

        return _bc.pos;
    }
}
