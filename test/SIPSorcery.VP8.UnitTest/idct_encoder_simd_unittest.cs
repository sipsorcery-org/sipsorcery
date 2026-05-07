//-----------------------------------------------------------------------------
// Filename: idct_encoder_simd_unittest.cs
//
// Description: Bit-exactness fuzz tests for the encoder-side SIMD fast path
// IdctEncoderSimd vs the scalar reference idctllm.vp8_short_idct4x4llm_c.
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
    public unsafe class idct_encoder_simd_unittest
    {
        // ----------------- helpers -----------------

        private static void ScalarReference(short[] input, byte[] pred, int predStride, byte[] dst, int dstStride)
        {
            fixed (short* inP = input)
            fixed (byte* predP = pred)
            fixed (byte* dstP = dst)
            {
                idctllm.vp8_short_idct4x4llm_c(inP, predP, predStride, dstP, dstStride);
            }
        }

        private static void RunSimdFlat(short[] input, byte pred, byte[] dst, int dstStride)
        {
            fixed (short* inP = input)
            fixed (byte* dstP = dst)
            {
                IdctEncoderSimd.Idct4x4AddFlat(inP, pred, dstP, dstStride);
            }
        }

        private static void RunSimdBlock(short[] input, byte[] pred, int predStride, byte[] dst, int dstStride)
        {
            fixed (short* inP = input)
            fixed (byte* predP = pred)
            fixed (byte* dstP = dst)
            {
                IdctEncoderSimd.Idct4x4AddBlock(inP, predP, predStride, dstP, dstStride);
            }
        }

        private static void Assert4x4Equal(byte[] a, int aStride, byte[] b, int bStride, int aOffsetX = 0, int aOffsetY = 0, int bOffsetX = 0, int bOffsetY = 0)
        {
            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                Assert.Equal(a[(aOffsetY + r) * aStride + aOffsetX + c], b[(bOffsetY + r) * bStride + bOffsetX + c]);
        }

        // ----------------- bit-exact zero coefficient -----------------

        [Fact]
        public void Idct4x4AddFlat_ZeroCoefs_MatchesScalar()
        {
            short[] coef = new short[16];
            byte[] predBuf = new byte[16];
            byte[] dstScalar = new byte[16];

            for (int p = 0; p < 256; p += 17)
            {
                Array.Fill(predBuf, (byte)p);
                ScalarReference(coef, predBuf, 4, dstScalar, 4);

                byte[] dstSimd = new byte[16];
                RunSimdFlat(coef, (byte)p, dstSimd, 4);
                Assert4x4Equal(dstScalar, 4, dstSimd, 4);
            }
        }

        [Fact]
        public void Idct4x4AddBlock_ZeroCoefs_MatchesScalar_PerPixelPred()
        {
            short[] coef = new short[16];
            var rng = new Random(0xC0FFEE);

            for (int trial = 0; trial < 32; trial++)
            {
                byte[] predBuf = new byte[16];
                rng.NextBytes(predBuf);

                byte[] dstScalar = new byte[16];
                ScalarReference(coef, predBuf, 4, dstScalar, 4);

                byte[] dstSimd = new byte[16];
                RunSimdBlock(coef, predBuf, 4, dstSimd, 4);
                Assert4x4Equal(dstScalar, 4, dstSimd, 4);
            }
        }

        // ----------------- bit-exact small fixed pattern -----------------

        [Fact]
        public void Idct4x4AddFlat_SmallFixedPattern_MatchesScalar()
        {
            // Single non-zero DC -> uniform shift after the 2-pass.
            short[] coef = new short[16];
            coef[0] = 32;

            byte pred = 100;
            byte[] predBuf = new byte[16];
            Array.Fill(predBuf, pred);

            byte[] dstScalar = new byte[16];
            ScalarReference(coef, predBuf, 4, dstScalar, 4);

            byte[] dstSimd = new byte[16];
            RunSimdFlat(coef, pred, dstSimd, 4);

            Assert4x4Equal(dstScalar, 4, dstSimd, 4);
        }

        // ----------------- fuzz: random coefs, flat pred -----------------

        [Fact]
        public void Idct4x4AddFlat_Fuzz_ManySeeds_MatchesScalar()
        {
            // Pick coef ranges that exercise the fixed-point multiplies and
            // saturation paths but stay in libvpx-realistic bands. Real
            // dequantized coefs are bounded by ~[-2048, 2047] in practice
            // (after multiplying quant-by-zigzag-coef), and idct adds a
            // factor of ~2.6 in the worst case.
            for (int seed = 0; seed < 64; seed++)
            {
                var rng = new Random(seed);

                short[] coef = new short[16];
                for (int i = 0; i < 16; i++)
                {
                    coef[i] = (short)rng.Next(-1024, 1025);
                }

                byte pred = (byte)rng.Next(0, 256);
                byte[] predBuf = new byte[16];
                Array.Fill(predBuf, pred);

                byte[] dstScalar = new byte[16];
                ScalarReference(coef, predBuf, 4, dstScalar, 4);

                byte[] dstSimd = new byte[16];
                RunSimdFlat(coef, pred, dstSimd, 4);

                for (int i = 0; i < 16; i++)
                {
                    Assert.True(dstScalar[i] == dstSimd[i],
                        $"seed={seed} idx={i} expected={dstScalar[i]} got={dstSimd[i]} pred={pred}");
                }
            }
        }

        // ----------------- fuzz: random coefs, per-pixel pred, strided -----------------

        [Fact]
        public void Idct4x4AddBlock_Fuzz_ManySeeds_MatchesScalar_StridedDst()
        {
            const int dstStride = 24;
            const int predStride = 32;

            for (int seed = 0; seed < 64; seed++)
            {
                var rng = new Random(seed * 17 + 1);

                short[] coef = new short[16];
                for (int i = 0; i < 16; i++) coef[i] = (short)rng.Next(-1024, 1025);

                byte[] predFull = new byte[predStride * 8];
                rng.NextBytes(predFull);
                byte[] dstScalarFull = new byte[dstStride * 8];
                byte[] dstSimdFull = new byte[dstStride * 8];

                int predOffsetX = 3;
                int predOffsetY = 2;
                int dstOffsetX = 5;
                int dstOffsetY = 1;

                fixed (short* inP = coef)
                fixed (byte* predP = predFull)
                fixed (byte* dstP = dstScalarFull)
                {
                    idctllm.vp8_short_idct4x4llm_c(inP,
                        predP + predOffsetY * predStride + predOffsetX, predStride,
                        dstP + dstOffsetY * dstStride + dstOffsetX, dstStride);
                }

                fixed (short* inP = coef)
                fixed (byte* predP = predFull)
                fixed (byte* dstP = dstSimdFull)
                {
                    IdctEncoderSimd.Idct4x4AddBlock(inP,
                        predP + predOffsetY * predStride + predOffsetX, predStride,
                        dstP + dstOffsetY * dstStride + dstOffsetX, dstStride);
                }

                for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    int idx = (dstOffsetY + r) * dstStride + dstOffsetX + c;
                    Assert.True(dstScalarFull[idx] == dstSimdFull[idx],
                        $"seed={seed} r={r} c={c} expected={dstScalarFull[idx]} got={dstSimdFull[idx]}");
                }
            }
        }

        // ----------------- saturation corners -----------------

        [Fact]
        public void Idct4x4AddFlat_LargePositiveCoefs_ClampsTo255()
        {
            short[] coef = new short[16];
            for (int i = 0; i < 16; i++) coef[i] = 1024;

            byte pred = 200;
            byte[] predBuf = new byte[16];
            Array.Fill(predBuf, pred);

            byte[] dstScalar = new byte[16];
            ScalarReference(coef, predBuf, 4, dstScalar, 4);

            byte[] dstSimd = new byte[16];
            RunSimdFlat(coef, pred, dstSimd, 4);

            Assert4x4Equal(dstScalar, 4, dstSimd, 4);
        }

        [Fact]
        public void Idct4x4AddFlat_LargeNegativeCoefs_ClampsToZero()
        {
            short[] coef = new short[16];
            for (int i = 0; i < 16; i++) coef[i] = -1024;

            byte pred = 50;
            byte[] predBuf = new byte[16];
            Array.Fill(predBuf, pred);

            byte[] dstScalar = new byte[16];
            ScalarReference(coef, predBuf, 4, dstScalar, 4);

            byte[] dstSimd = new byte[16];
            RunSimdFlat(coef, pred, dstSimd, 4);

            Assert4x4Equal(dstScalar, 4, dstSimd, 4);
        }
    }
}
