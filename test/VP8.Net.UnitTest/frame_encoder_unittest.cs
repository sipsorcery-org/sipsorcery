//-----------------------------------------------------------------------------
// Filename: frame_encoder_unittest.cs
//
// Description: End-to-end round-trip tests for the VP8 keyframe encoder:
// encode a synthetic I420 source frame, feed the resulting bitstream
// through the existing VP8 decoder primitives (bypassing the BGR
// conversion in DecodeVideo), and verify the decoded I420 planes match
// the source within the quantizer's tolerance.
//
// This is the "moment of truth" test for the foundation encoder series.
// Every primitive below this test (boolean coder, DCT, Walsh, quantize,
// tokenizer, pack_tokens, quantizer table builder, per-MB pipeline,
// frame header writer) has been individually bit-exact verified against
// libvpx in earlier PRs.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 25 Apr 2026  Claude          Created.
// 26 Apr 2026  Claude          Add checkerboard tests that exercise
//                              cross-MB entropy-context propagation
//                              (would fail with maxErr ~97 on master
//                              before the frame-scope context fix).
// 26 Apr 2026  Claude          Add skip-MB tests covering uniform
//                              (all-skip), mixed-content (partial-skip),
//                              and a bitstream-size sanity check that
//                              an all-skip frame is meaningfully smaller
//                              than its non-skip equivalent would be.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class frame_encoder_unittest
    {
        // ---------- Frame structure sanity ----------

        [Fact]
        public void EncodeKeyframe_Minimal16x16_HasValidKeyframeTagAndStartCode()
        {
            byte[] yuv = MakeFlatI420(16, 16, y: 128, u: 128, v: 128);
            var (y, u, v) = SplitI420(yuv, 16, 16);

            byte[] frame = frame_encoder.EncodeKeyframe(y, u, v, 16, 16, qIndex: 32);

            Assert.True(frame.Length >= 10);
            Assert.Equal(0, frame[0] & 0x1);                   // keyframe flag
            Assert.Equal(0x9D, frame[3]);
            Assert.Equal(0x01, frame[4]);
            Assert.Equal(0x2A, frame[5]);
            Assert.Equal(16, (frame[6] | (frame[7] << 8)) & 0x3FFF);
            Assert.Equal(16, (frame[8] | (frame[9] << 8)) & 0x3FFF);
        }

        // ---------- Round-trip through the decoder, in I420 space ----------

        [Fact]
        public void EncodeAndDecode_UniformGrey16x16_RoundTripsExactly()
        {
            const int W = 16, H = 16;
            byte[] yuv = MakeFlatI420(W, H, y: 128, u: 128, v: 128);
            byte[] decoded = EncodeAndDecodeI420(yuv, W, H, qIndex: 32);
            AssertExact(yuv, decoded);
        }

        [Fact]
        public void EncodeAndDecode_UniformColour16x16_DecodesWithinTolerance()
        {
            const int W = 16, H = 16;
            byte[] yuv = MakeFlatI420(W, H, y: 200, u: 100, v: 50);
            byte[] decoded = EncodeAndDecodeI420(yuv, W, H, qIndex: 32);
            AssertWithin(yuv, decoded, tol: 16);
        }

        [Fact]
        public void EncodeAndDecode_32x32UniformGrey_FourMacroblocksRoundTripExactly()
        {
            const int W = 32, H = 32;
            byte[] yuv = MakeFlatI420(W, H, y: 128, u: 128, v: 128);
            byte[] decoded = EncodeAndDecodeI420(yuv, W, H, qIndex: 32);
            AssertExact(yuv, decoded);
        }

        // ---------- Cross-MB entropy-context propagation ----------
        //
        // These tests exercise non-uniform content across multiple MBs
        // in the same row and across multiple MB rows. A regression
        // in either direction (omitting the frame-scope above-context,
        // or omitting the row-scope left-context) shows up here as
        // visibly garbled output past the first MB while the first MB
        // alone decodes correctly.
        //
        // Discriminator: on master before the fix, the 32x16 case
        // produced maxErr=97 / MAE=25.77 and the 32x32 case produced
        // maxErr=97 / MAE=37.97; the fix brings both to maxErr<=4 /
        // MAE<=1.53. The asserted bound of 16 is well below the
        // bug-present floor and well above the bug-fixed ceiling.

        [Fact]
        public void EncodeAndDecode_Checkerboard32x16_CrossMbContextRoundTrips()
        {
            // 32x16 = 2 MBs side by side; same row.
            const int W = 32, H = 16;
            byte[] yuv = MakeCheckerboardI420(W, H, dark: 50, light: 200);

            byte[] decoded = EncodeAndDecodeI420(yuv, W, H, qIndex: 32);

            AssertWithin(yuv, decoded, tol: 16);
        }

        [Fact]
        public void EncodeAndDecode_Checkerboard32x32_CrossMbRowAndColumnRoundTrips()
        {
            // 32x32 = 2x2 MB grid; exercises both cross-row above
            // context and cross-column left context.
            const int W = 32, H = 32;
            byte[] yuv = MakeCheckerboardI420(W, H, dark: 50, light: 200);

            byte[] decoded = EncodeAndDecodeI420(yuv, W, H, qIndex: 32);

            AssertWithin(yuv, decoded, tol: 16);
        }

        // ---------- Per-MB skip flag (mb_no_skip_coeff = 1) ----------
        //
        // The encoder enables libvpx's per-MB skip optimisation: the
        // keyframe header writes mb_no_skip_coeff = 1 and an 8-bit
        // prob_skip_false; for each MB whose 25 transformed blocks are
        // all EOB-only, a single skip-flag bit is written and the
        // entire token stream for that MB is suppressed in partition 1.
        //
        // The two existing uniform-grey round-trip tests already
        // exercise the all-skip path implicitly. These tests cover the
        // mixed (some-skip / some-non-skip) path and the bitstream-size
        // sanity check.

        [Fact]
        public void EncodeAndDecode_MixedContent64x16_SomeMbsSkipSomeDont()
        {
            // 64x16 = 4 MBs in a row.  The leftmost two MBs are flat
            // grey (residual zero -> every block tokenizes to a single
            // EOB -> skip flag = 1), the rightmost two are checkerboard
            // (busy residual -> every block emits coefficient tokens ->
            // skip flag = 0).  A correct encoder/decoder pair has to
            // handle the mix: skip-flag bits emitted for every MB, but
            // tokens written only for the right half.
            const int W = 64, H = 16;
            int ySize = W * H, cSize = (W / 2) * (H / 2);
            byte[] yuv = new byte[ySize + 2 * cSize];

            // Y: flat 128 on the left half, checkerboard on the right.
            for (int row = 0; row < H; row++)
            {
                for (int col = 0; col < W; col++)
                {
                    byte y;
                    if (col < 32)
                    {
                        y = 128;
                    }
                    else
                    {
                        y = ((((row >> 1) ^ (col >> 1)) & 1) != 0) ? (byte)50 : (byte)200;
                    }
                    yuv[row * W + col] = y;
                }
            }
            // UV: flat 128 across the frame.
            for (int i = 0; i < cSize; i++) yuv[ySize + i] = 128;
            for (int i = 0; i < cSize; i++) yuv[ySize + cSize + i] = 128;

            byte[] decoded = EncodeAndDecodeI420(yuv, W, H, qIndex: 32);

            // The flat half must round-trip exactly (skipped MBs decode
            // to pure DC_PRED reconstruction = the prediction value).
            for (int row = 0; row < H; row++)
                for (int col = 0; col < 32; col++)
                    Assert.Equal(yuv[row * W + col], decoded[row * W + col]);

            // The checkerboard half goes through the full encode/decode
            // pipeline; bound by the same tol the cross-MB tests use.
            for (int row = 0; row < H; row++)
                for (int col = 32; col < W; col++)
                {
                    int d = decoded[row * W + col] - yuv[row * W + col];
                    if (d < 0) d = -d;
                    Assert.True(d <= 16, $"Y[{row},{col}] err {d} > 16 (src={yuv[row*W+col]} dec={decoded[row*W+col]})");
                }

            // UV planes are uniform 128 (skippable); must round-trip
            // exactly.
            for (int i = 0; i < 2 * cSize; i++)
                Assert.Equal(yuv[ySize + i], decoded[ySize + i]);
        }

        [Fact]
        public void EncodeKeyframe_AllSkipFrame_IsMaterallySmallerThanNonSkip()
        {
            // An all-skip frame writes only skip-flag bits + mode-tree
            // bits in partition 0 and ZERO bytes in partition 1 (the
            // bool coder's flush still emits a few trailing bytes for
            // alignment, but no per-block coefficient tokens). A
            // non-trivial frame at the same dimensions writes the full
            // token stream for every MB. The size delta is the proof
            // that mb_no_skip_coeff = 1 is doing real work.
            const int W = 32, H = 32;

            byte[] uniform = MakeFlatI420(W, H, y: 128, u: 128, v: 128);
            byte[] busy = MakeCheckerboardI420(W, H, dark: 50, light: 200);

            var (uy, uu, uv) = SplitI420(uniform, W, H);
            var (by, bu, bv) = SplitI420(busy, W, H);

            byte[] uniformFrame = frame_encoder.EncodeKeyframe(uy, uu, uv, W, H, qIndex: 32);
            byte[] busyFrame    = frame_encoder.EncodeKeyframe(by, bu, bv, W, H, qIndex: 32);

            // The all-skip frame must be smaller than the non-skip
            // one. Empirically the difference is order-of-magnitude;
            // require at least 2x to avoid being brittle to encoder
            // tuning that doesn't change the underlying mechanism.
            Assert.True(uniformFrame.Length * 2 < busyFrame.Length,
                $"all-skip frame ({uniformFrame.Length} bytes) should be <50% the size of a busy frame ({busyFrame.Length} bytes)");
        }

        // ---------- helpers ----------

        // Encode an I420 source via the public VP8Codec, then decode using
        // the same low-level decoder calls that DecodeVideo uses internally
        // but stop BEFORE the I420->BGR conversion so we can compare
        // pixels in the colour space the codec actually operates in.
        private static byte[] EncodeAndDecodeI420(byte[] yuv, int width, int height, int qIndex)
        {
            var codec = new VP8Codec();
            byte[] frame = codec.EncodeVideo(width, height, yuv, SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.I420, SIPSorceryMedia.Abstractions.VideoCodecsEnum.VP8);
            Assert.NotNull(frame);
            Assert.True(frame.Length > 10);

            // Set up the decoder.
            var ctx = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            var cfg = new vpx_codec_dec_cfg_t { threads = 1 };
            var initRes = vpx_decoder.vpx_codec_dec_init(ctx, algo, cfg, 0);
            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, initRes);

            fixed (byte* pFrame = frame)
            {
                var decRes = vpx_decoder.vpx_codec_decode(ctx, pFrame, (uint)frame.Length, IntPtr.Zero, 0);
                Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, decRes);
            }

            IntPtr iter = IntPtr.Zero;
            var img = vpx_decoder.vpx_codec_get_frame(ctx, iter);
            Assert.NotNull(img);
            Assert.Equal((uint)width,  img.d_w);
            Assert.Equal((uint)height, img.d_h);

            int sz = width * height;
            int csz = (width / 2) * (height / 2);
            byte[] outYuv = new byte[sz + 2 * csz];

            for (int row = 0; row < height; row++)
            {
                Marshal.Copy((IntPtr)(img.planes[0] + row * img.stride[0]), outYuv, row * width, width);
                if (row < height / 2)
                {
                    Marshal.Copy((IntPtr)(img.planes[1] + row * img.stride[1]), outYuv, sz + row * (width / 2), width / 2);
                    Marshal.Copy((IntPtr)(img.planes[2] + row * img.stride[2]), outYuv, sz + csz + row * (width / 2), width / 2);
                }
            }
            return outYuv;
        }

        private static byte[] MakeFlatI420(int width, int height, byte y, byte u, byte v)
        {
            int ySize = width * height;
            int cSize = (width / 2) * (height / 2);
            var b = new byte[ySize + 2 * cSize];
            for (int i = 0; i < ySize; i++) b[i] = y;
            for (int i = 0; i < cSize; i++) b[ySize + i] = u;
            for (int i = 0; i < cSize; i++) b[ySize + cSize + i] = v;
            return b;
        }

        // Y has a 2x2-pixel checkerboard pattern (dark / light); UV are
        // flat 128 to keep the test focused on the luma path. The 2x2
        // tile size means most 4x4 transformed blocks contain a busy
        // mix of high-frequency components, so quantization at Q=32
        // produces non-zero coefficient tokens in essentially every
        // block - which is the configuration where the cross-MB
        // entropy-context bug manifests.
        private static byte[] MakeCheckerboardI420(int width, int height, byte dark, byte light)
        {
            int ySize = width * height;
            int cSize = (width / 2) * (height / 2);
            var b = new byte[ySize + 2 * cSize];
            for (int row = 0; row < height; row++)
                for (int col = 0; col < width; col++)
                    b[row * width + col] = ((((row >> 1) ^ (col >> 1)) & 1) != 0) ? dark : light;
            for (int i = 0; i < cSize; i++) b[ySize + i] = 128;
            for (int i = 0; i < cSize; i++) b[ySize + cSize + i] = 128;
            return b;
        }

        private static (byte[] Y, byte[] U, byte[] V) SplitI420(byte[] yuv, int w, int h)
        {
            int ySize = w * h, cSize = (w / 2) * (h / 2);
            var y = new byte[ySize]; var u = new byte[cSize]; var v = new byte[cSize];
            Buffer.BlockCopy(yuv, 0, y, 0, ySize);
            Buffer.BlockCopy(yuv, ySize, u, 0, cSize);
            Buffer.BlockCopy(yuv, ySize + cSize, v, 0, cSize);
            return (y, u, v);
        }

        private static void AssertExact(byte[] expected, byte[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i] == actual[i],
                    "Pixel " + i + " differs: src=" + expected[i] + " decoded=" + actual[i]);
            }
        }

        private static void AssertWithin(byte[] expected, byte[] actual, int tol)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                int d = actual[i] - expected[i];
                Assert.True(d >= -tol && d <= tol,
                    "Pixel " + i + " err " + d + " exceeds tolerance " + tol +
                    " (src=" + expected[i] + " decoded=" + actual[i] + ")");
            }
        }
    }
}
