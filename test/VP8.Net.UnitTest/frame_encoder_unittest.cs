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
