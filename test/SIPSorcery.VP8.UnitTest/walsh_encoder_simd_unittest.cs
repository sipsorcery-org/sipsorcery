//-----------------------------------------------------------------------------
// Filename: walsh_encoder_simd_unittest.cs
//
// Description: Bit-exactness fuzz tests for the encoder-side SIMD fast paths
// for forward/inverse 4x4 Walsh-Hadamard, vs the scalar references.
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
    public unsafe class walsh_encoder_simd_unittest
    {
        // Scalar reference for the inverse Walsh used by the encoder. Same shape as
        // mb_encoder.InverseWalsh4x4Into but unsafe-friendly.
        private static void InverseWalshScalarReference(short* input, short* output)
        {
            int* tmp = stackalloc int[16];
            int a1, b1, c1, d1, a2, b2, c2, d2;

            for (int i = 0; i < 4; i++)
            {
                a1 = input[i * 4 + 0] + input[i * 4 + 3];
                b1 = input[i * 4 + 1] + input[i * 4 + 2];
                c1 = input[i * 4 + 1] - input[i * 4 + 2];
                d1 = input[i * 4 + 0] - input[i * 4 + 3];

                tmp[i * 4 + 0] = a1 + b1;
                tmp[i * 4 + 1] = c1 + d1;
                tmp[i * 4 + 2] = a1 - b1;
                tmp[i * 4 + 3] = d1 - c1;
            }

            for (int i = 0; i < 4; i++)
            {
                a1 = tmp[0 * 4 + i] + tmp[3 * 4 + i];
                b1 = tmp[1 * 4 + i] + tmp[2 * 4 + i];
                c1 = tmp[1 * 4 + i] - tmp[2 * 4 + i];
                d1 = tmp[0 * 4 + i] - tmp[3 * 4 + i];

                a2 = a1 + b1;
                b2 = c1 + d1;
                c2 = a1 - b1;
                d2 = d1 - c1;

                output[0 * 4 + i] = (short)((a2 + 3) >> 3);
                output[1 * 4 + i] = (short)((b2 + 3) >> 3);
                output[2 * 4 + i] = (short)((c2 + 3) >> 3);
                output[3 * 4 + i] = (short)((d2 + 3) >> 3);
            }
        }

        // ----------------- Forward Walsh -----------------

        [Fact]
        public void Walsh4x4_ZeroInput_MatchesScalar()
        {
            short[] input = new short[16];
            short[] expected = new short[16];
            short[] actual = new short[16];

            fixed (short* inP = input)
            fixed (short* expP = expected)
            fixed (short* actP = actual)
            {
                dct.vp8_short_walsh4x4_c(inP, expP, pitch: 8);
                WalshEncoderSimd.Walsh4x4(inP, actP, pitch: 8);
            }

            for (int i = 0; i < 16; i++) Assert.Equal(expected[i], actual[i]);
        }

        [Fact]
        public void Walsh4x4_Fuzz_ManySeeds_MatchesScalar()
        {
            for (int seed = 0; seed < 64; seed++)
            {
                var rng = new Random(seed * 31 + 17);

                short[] input = new short[16];
                for (int i = 0; i < 16; i++)
                {
                    // Walsh forward sees DC coefficients of 4x4 residual blocks; range
                    // typically [-512, 512] after the 16x16-mode subtract+fdct pipeline,
                    // but we test a wider range to catch overflow.
                    input[i] = (short)rng.Next(-2048, 2049);
                }

                short[] expected = new short[16];
                short[] actual = new short[16];

                fixed (short* inP = input)
                fixed (short* expP = expected)
                fixed (short* actP = actual)
                {
                    dct.vp8_short_walsh4x4_c(inP, expP, pitch: 8);
                    WalshEncoderSimd.Walsh4x4(inP, actP, pitch: 8);
                }

                for (int i = 0; i < 16; i++)
                {
                    Assert.True(expected[i] == actual[i],
                        $"seed={seed} idx={i} expected={expected[i]} got={actual[i]} input[{i}]={input[i]}");
                }
            }
        }

        [Fact]
        public void Walsh4x4_StridedInput_MatchesScalar()
        {
            // Walsh is sometimes called with non-contiguous inputs (`pitch != 8`)
            // when the source coefficients live in a strided 16-coeff layout.
            // Validate both paths handle pitch=16 (8 shorts).
            for (int seed = 0; seed < 16; seed++)
            {
                var rng = new Random(seed * 7);
                short[] input = new short[32];
                for (int i = 0; i < 32; i++) input[i] = (short)rng.Next(-1024, 1025);

                short[] expected = new short[16];
                short[] actual = new short[16];

                fixed (short* inP = input)
                fixed (short* expP = expected)
                fixed (short* actP = actual)
                {
                    dct.vp8_short_walsh4x4_c(inP, expP, pitch: 16);
                    WalshEncoderSimd.Walsh4x4(inP, actP, pitch: 16);
                }

                for (int i = 0; i < 16; i++)
                {
                    Assert.True(expected[i] == actual[i],
                        $"seed={seed} idx={i} expected={expected[i]} got={actual[i]}");
                }
            }
        }

        // ----------------- Inverse Walsh -----------------

        [Fact]
        public void InverseWalsh4x4_ZeroInput_MatchesScalar()
        {
            short[] input = new short[16];
            short[] expected = new short[16];
            short[] actual = new short[16];

            fixed (short* inP = input)
            fixed (short* expP = expected)
            fixed (short* actP = actual)
            {
                InverseWalshScalarReference(inP, expP);
                WalshEncoderSimd.InverseWalsh4x4(inP, actP);
            }

            for (int i = 0; i < 16; i++) Assert.Equal(expected[i], actual[i]);
        }

        [Fact]
        public void InverseWalsh4x4_Fuzz_ManySeeds_MatchesScalar()
        {
            for (int seed = 0; seed < 64; seed++)
            {
                var rng = new Random(seed * 41 + 9);

                short[] input = new short[16];
                for (int i = 0; i < 16; i++) input[i] = (short)rng.Next(-2048, 2049);

                short[] expected = new short[16];
                short[] actual = new short[16];

                fixed (short* inP = input)
                fixed (short* expP = expected)
                fixed (short* actP = actual)
                {
                    InverseWalshScalarReference(inP, expP);
                    WalshEncoderSimd.InverseWalsh4x4(inP, actP);
                }

                for (int i = 0; i < 16; i++)
                {
                    Assert.True(expected[i] == actual[i],
                        $"seed={seed} idx={i} expected={expected[i]} got={actual[i]} input[{i}]={input[i]}");
                }
            }
        }

        [Fact]
        public void Walsh_Then_InverseWalsh_RoundTripsApproximately()
        {
            // The walsh+inv-walsh pair is dimensionally a 1/8-energy roundtrip due
            // to the *4 scale and >>3. So input * 4 = output after roundtrip (with
            // some rounding loss when nonzero). This test checks that the SIMD
            // path's roundtrip is identical to the scalar pair's roundtrip.
            for (int seed = 0; seed < 16; seed++)
            {
                var rng = new Random(seed);

                short[] input = new short[16];
                for (int i = 0; i < 16; i++) input[i] = (short)rng.Next(-128, 129);

                short[] fwdScalar = new short[16];
                short[] fwdSimd = new short[16];
                short[] roundScalar = new short[16];
                short[] roundSimd = new short[16];

                fixed (short* inP = input)
                fixed (short* fS = fwdScalar)
                fixed (short* fM = fwdSimd)
                fixed (short* rS = roundScalar)
                fixed (short* rM = roundSimd)
                {
                    dct.vp8_short_walsh4x4_c(inP, fS, pitch: 8);
                    WalshEncoderSimd.Walsh4x4(inP, fM, pitch: 8);

                    InverseWalshScalarReference(fS, rS);
                    WalshEncoderSimd.InverseWalsh4x4(fM, rM);
                }

                for (int i = 0; i < 16; i++)
                {
                    Assert.Equal(fwdScalar[i], fwdSimd[i]);
                    Assert.Equal(roundScalar[i], roundSimd[i]);
                }
            }
        }
    }
}
