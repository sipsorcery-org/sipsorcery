//-----------------------------------------------------------------------------
// Filename: mb_encoder_unittest.cs
//
// Description: Unit tests for the per-macroblock encode pipeline
// (mb_encoder.EncodeMacroblockDcPred). Covers the trivial uniform-input
// case (every transformed block must collapse to a single EOB token, and
// reconstruction must equal the prediction), and a non-trivial case
// (random-but-deterministic source pixels) where reconstruction must
// match the source within the quantizer's tolerance — which is the right
// invariant for an intra encoder, since a perfect round-trip is only
// possible for fully-zero residuals.
//
// These tests don't compare against a libvpx C reference for the produced
// token streams: that would require running the full libvpx encoder,
// which is too heavy for a unit test. Instead they verify the
// orchestration's two top-level invariants — flat input -> EOB-only
// output, and reconstruction equals source within quant noise — with the
// underlying primitives (DCT, quantize, tokenize, idct) already
// individually bit-exact-verified against libvpx in earlier PRs.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public class mb_encoder_unittest
    {
        // ---------- uniform input: trivial outputs ----------

        /// <summary>
        /// Uniform-grey input with no neighbour context: prediction = 128
        /// (default), residual = 0 everywhere, every transformed block
        /// quantizes to all zeros, every block tokenizes to a single EOB.
        /// Reconstruction must equal the prediction (128).
        /// </summary>
        [Fact]
        public void EncodeMacroblock_UniformGrey_NoNeighbours_ProducesEobOnlyAndReconstructsToPred()
        {
            byte[] srcY = FlatBytes(256, 128);
            byte[] srcU = FlatBytes(64, 128);
            byte[] srcV = FlatBytes(64, 128);

            var fq = quantizer_init.BuildForQIndex(32);
            var r = mb_encoder.EncodeMacroblockDcPred(
                srcY, srcU, srcV,
                aboveY: null, leftY: null,
                aboveU: null, leftU: null,
                aboveV: null, leftV: null,
                fq: fq);

            // Each of the 25 blocks must be exactly one EOB token.
            Assert.Single(r.Y2Block);
            Assert.Equal((byte)entropy.DCT_EOB_TOKEN, r.Y2Block[0].Token);
            for (int i = 0; i < 16; i++)
            {
                Assert.Single(r.YBlocks[i]);
                Assert.Equal((byte)entropy.DCT_EOB_TOKEN, r.YBlocks[i][0].Token);
            }
            for (int i = 0; i < 4; i++)
            {
                Assert.Single(r.UBlocks[i]);
                Assert.Equal((byte)entropy.DCT_EOB_TOKEN, r.UBlocks[i][0].Token);
                Assert.Single(r.VBlocks[i]);
                Assert.Equal((byte)entropy.DCT_EOB_TOKEN, r.VBlocks[i][0].Token);
            }

            // Reconstruction equals the prediction.
            for (int i = 0; i < 256; i++) Assert.Equal((byte)128, r.ReconY[i]);
            for (int i = 0; i < 64; i++)
            {
                Assert.Equal((byte)128, r.ReconU[i]);
                Assert.Equal((byte)128, r.ReconV[i]);
            }
        }

        /// <summary>
        /// Uniform-grey input WITH non-default neighbour context — DC_PRED
        /// computes from the actual neighbours, not 128. Residual is still
        /// zero (input == DC of input == prediction), reconstruction must
        /// match the source.
        /// </summary>
        [Fact]
        public void EncodeMacroblock_UniformContext_PredictionMatchesNeighbours()
        {
            byte[] srcY = FlatBytes(256, 200);
            byte[] srcU = FlatBytes(64, 100);
            byte[] srcV = FlatBytes(64, 50);
            byte[] aboveY = FlatBytes(16, 200), leftY = FlatBytes(16, 200);
            byte[] aboveU = FlatBytes(8, 100),  leftU = FlatBytes(8, 100);
            byte[] aboveV = FlatBytes(8, 50),   leftV = FlatBytes(8, 50);

            var fq = quantizer_init.BuildForQIndex(32);
            var r = mb_encoder.EncodeMacroblockDcPred(
                srcY, srcU, srcV,
                aboveY, leftY, aboveU, leftU, aboveV, leftV,
                fq);

            // All blocks still EOB-only because residual is zero.
            Assert.Single(r.Y2Block);
            Assert.Equal((byte)entropy.DCT_EOB_TOKEN, r.Y2Block[0].Token);

            // Reconstruction matches the source.
            for (int i = 0; i < 256; i++) Assert.Equal((byte)200, r.ReconY[i]);
            for (int i = 0; i < 64; i++)
            {
                Assert.Equal((byte)100, r.ReconU[i]);
                Assert.Equal((byte)50, r.ReconV[i]);
            }
        }

        // ---------- non-trivial input: round-trip within quantizer tolerance ----------

        /// <summary>
        /// Deterministic pseudo-random source pixels with no neighbour
        /// context. The encoder produces some non-EOB tokens for at least
        /// some blocks (otherwise the test data is too smooth), and
        /// reconstruction matches the source within a tolerance bounded by
        /// the quantizer step.
        ///
        /// At Q=32 the Y AC dequantizer is 36 (see quantizer_init_unittest);
        /// after the DCT's 8x scaling and the rounding constants, a
        /// per-pixel reconstruction error of up to ~16 is expected for
        /// arbitrary input. We assert <= 32 to give comfortable headroom
        /// against the worst-case rounding combinations.
        /// </summary>
        [Fact]
        public void EncodeMacroblock_RandomInput_ReconstructionWithinQuantTolerance()
        {
            byte[] srcY = DeterministicBytes(256, seed: 1);
            byte[] srcU = DeterministicBytes(64, seed: 2);
            byte[] srcV = DeterministicBytes(64, seed: 3);

            var fq = quantizer_init.BuildForQIndex(32);
            var r = mb_encoder.EncodeMacroblockDcPred(
                srcY, srcU, srcV,
                aboveY: null, leftY: null,
                aboveU: null, leftU: null,
                aboveV: null, leftV: null,
                fq: fq);

            // At least some block carries non-EOB tokens (otherwise the
            // input was effectively flat and this test isn't exercising
            // anything).
            int nonTrivialBlocks = 0;
            if (r.Y2Block.Count > 1) nonTrivialBlocks++;
            for (int i = 0; i < 16; i++) if (r.YBlocks[i].Count > 1) nonTrivialBlocks++;
            for (int i = 0; i < 4; i++)
            {
                if (r.UBlocks[i].Count > 1) nonTrivialBlocks++;
                if (r.VBlocks[i].Count > 1) nonTrivialBlocks++;
            }
            Assert.True(nonTrivialBlocks > 0, "test data was too flat — all blocks were EOB-only");

            // Reconstruction error per pixel must be bounded.
            const int kTol = 32;
            for (int i = 0; i < 256; i++)
            {
                int diff = r.ReconY[i] - srcY[i];
                Assert.True(diff >= -kTol && diff <= kTol,
                    "Y[" + i + "] recon err " + diff + " exceeds tolerance " + kTol);
            }
            for (int i = 0; i < 64; i++)
            {
                int dU = r.ReconU[i] - srcU[i];
                int dV = r.ReconV[i] - srcV[i];
                Assert.True(dU >= -kTol && dU <= kTol, "U[" + i + "] recon err " + dU);
                Assert.True(dV >= -kTol && dV <= kTol, "V[" + i + "] recon err " + dV);
            }
        }

        // ---------- helpers ----------

        private static byte[] FlatBytes(int n, byte v)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = v;
            return b;
        }

        // Same xorshift seed everywhere for reproducibility — these tests
        // must produce the same input on every run regardless of CLR or OS.
        private static byte[] DeterministicBytes(int n, int seed)
        {
            var b = new byte[n];
            uint x = (uint)(0x9E3779B9 ^ seed);
            for (int i = 0; i < n; i++)
            {
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                b[i] = (byte)(x & 0xFF);
            }
            return b;
        }
    }
}
