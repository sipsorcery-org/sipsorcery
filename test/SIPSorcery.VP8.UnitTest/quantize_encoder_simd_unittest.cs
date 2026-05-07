//-----------------------------------------------------------------------------
// Filename: quantize_encoder_simd_unittest.cs
//
// Description: Bit-exactness fuzz tests comparing the SIMD quantizer
// (QuantizeEncoderSimd.RegularQuantizeB) against the scalar reference
// (quantize.vp8_regular_quantize_b_arrays).
//
// Author(s):
// Claude Opus 4.7 (commissioned by Aaron Clauson).
//
// License: BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class quantize_encoder_simd_unittest
    {
        // libvpx-style quant table for dequant=8, moderate quality.
        private static short[] Repeat16(short v)
        {
            var a = new short[16];
            for (int i = 0; i < 16; i++) a[i] = v;
            return a;
        }

        private static int RunScalar(short[] coeff, short[] zbin, short[] zboost, short[] round, short[] quant,
            short[] qshift, short[] dequant, short zbinExtra, out short[] q, out short[] dq)
        {
            q = new short[16];
            dq = new short[16];
            fixed (short* cP = coeff)
            fixed (short* zbP = zbin)
            fixed (short* zbsP = zboost)
            fixed (short* rP = round)
            fixed (short* qvP = quant)
            fixed (short* qsP = qshift)
            fixed (short* dqvP = dequant)
            fixed (short* qP = q)
            fixed (short* dqP = dq)
            {
                return quantize.vp8_regular_quantize_b_arrays(cP, zbP, zbsP, rP, qvP, qsP, dqvP, qP, dqP, zbinExtra);
            }
        }

        private static int RunSimd(short[] coeff, short[] zbin, short[] zboost, short[] round, short[] quant,
            short[] qshift, short[] dequant, short zbinExtra, out short[] q, out short[] dq)
        {
            q = new short[16];
            dq = new short[16];
            fixed (short* cP = coeff)
            fixed (short* zbP = zbin)
            fixed (short* zbsP = zboost)
            fixed (short* rP = round)
            fixed (short* qvP = quant)
            fixed (short* qsP = qshift)
            fixed (short* dqvP = dequant)
            fixed (short* qP = q)
            fixed (short* dqP = dq)
            {
                return QuantizeEncoderSimd.RegularQuantizeB(cP, zbP, zbsP, rP, qvP, qsP, dqvP, qP, dqP, zbinExtra);
            }
        }

        [Fact]
        public void Quantize_ZeroInput_MatchesScalar()
        {
            short[] coeff = new short[16];
            int eobS = RunScalar(coeff, Repeat16(4), Repeat16(0), Repeat16(4), Repeat16(1),
                Repeat16(8192), Repeat16(8), 0, out var qS, out var dqS);
            int eobM = RunSimd(coeff, Repeat16(4), Repeat16(0), Repeat16(4), Repeat16(1),
                Repeat16(8192), Repeat16(8), 0, out var qM, out var dqM);

            Assert.Equal(eobS, eobM);
            Assert.Equal(qS, qM);
            Assert.Equal(dqS, dqM);
        }

        [Fact]
        public void Quantize_Fuzz_FlatTables_MatchesScalar()
        {
            short[] zbin = Repeat16(4);
            short[] zboost = Repeat16(0);
            short[] round = Repeat16(4);
            short[] quant = Repeat16(1);
            short[] qshift = Repeat16(8192);
            short[] dequant = Repeat16(8);

            for (int seed = 0; seed < 64; seed++)
            {
                var rng = new Random(seed * 37 + 5);
                short[] coeff = new short[16];
                for (int i = 0; i < 16; i++) coeff[i] = (short)rng.Next(-2048, 2049);

                int eobS = RunScalar(coeff, zbin, zboost, round, quant, qshift, dequant, 0, out var qS, out var dqS);
                int eobM = RunSimd(coeff, zbin, zboost, round, quant, qshift, dequant, 0, out var qM, out var dqM);

                Assert.True(eobS == eobM, $"seed={seed} eobS={eobS} eobM={eobM}");
                for (int i = 0; i < 16; i++)
                {
                    Assert.True(qS[i] == qM[i], $"seed={seed} i={i} qS={qS[i]} qM={qM[i]}");
                    Assert.True(dqS[i] == dqM[i], $"seed={seed} i={i} dqS={dqS[i]} dqM={dqM[i]}");
                }
            }
        }

        [Fact]
        public void Quantize_Fuzz_NonUniformTables_MatchesScalar()
        {
            // Use realistic-looking nonuniform tables drawn from libvpx Y plane
            // for a moderate Q.
            short[] zbin = { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 };
            short[] zboost = { 0, 0, 8, 8, 5, 4, 4, 5, 6, 7, 8, 12, 14, 18, 20, 24 };
            short[] round = { 8, 8, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 };
            short[] quant = { 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
            short[] qshift = { 8192, 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096 };
            short[] dequant = { 8, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16 };

            for (int seed = 0; seed < 64; seed++)
            {
                var rng = new Random(seed * 41 + 11);
                short[] coeff = new short[16];
                for (int i = 0; i < 16; i++) coeff[i] = (short)rng.Next(-2048, 2049);
                short zbinExtra = (short)rng.Next(0, 16);

                int eobS = RunScalar(coeff, zbin, zboost, round, quant, qshift, dequant, zbinExtra, out var qS, out var dqS);
                int eobM = RunSimd(coeff, zbin, zboost, round, quant, qshift, dequant, zbinExtra, out var qM, out var dqM);

                Assert.True(eobS == eobM, $"seed={seed} eobS={eobS} eobM={eobM}");
                for (int i = 0; i < 16; i++)
                {
                    Assert.True(qS[i] == qM[i], $"seed={seed} i={i} qS={qS[i]} qM={qM[i]}");
                    Assert.True(dqS[i] == dqM[i], $"seed={seed} i={i} dqS={dqS[i]} dqM={dqM[i]}");
                }
            }
        }

        [Fact]
        public void Quantize_Fuzz_LargeCoefRange_MatchesScalar()
        {
            // Real-world dq coefficients can range up to ~8000 — exercise that band.
            short[] zbin = Repeat16(150);
            short[] zboost = { 0, 0, 8, 8, 5, 4, 4, 5, 6, 7, 8, 12, 14, 18, 20, 24 };
            short[] round = Repeat16(80);
            short[] quant = Repeat16(20000);
            short[] qshift = Repeat16(2);
            short[] dequant = Repeat16(20);

            for (int seed = 0; seed < 32; seed++)
            {
                var rng = new Random(seed * 53 + 7);
                short[] coeff = new short[16];
                for (int i = 0; i < 16; i++) coeff[i] = (short)rng.Next(-8000, 8001);

                int eobS = RunScalar(coeff, zbin, zboost, round, quant, qshift, dequant, 0, out var qS, out var dqS);
                int eobM = RunSimd(coeff, zbin, zboost, round, quant, qshift, dequant, 0, out var qM, out var dqM);

                Assert.Equal(eobS, eobM);
                Assert.Equal(qS, qM);
                Assert.Equal(dqS, dqM);
            }
        }
    }
}
