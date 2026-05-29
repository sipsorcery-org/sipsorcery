﻿//-----------------------------------------------------------------------------
// Filename: encode_inter_frame_unittest.cs
//
// Description: Round-trip tests for the VP8 inter (P) frame encoder
// added in PR 5 of the P-frame foundation series. Each test encodes a
// keyframe followed by one or more inter frames and decodes the
// sequence back through the existing C# VP8 decoder, verifying the
// decoded pixels are close to (or, in the all-zero-residual case,
// exactly equal to) the source pixels.
//
// The tests cover:
//   - A static-source sequence (key + 4 inter frames of the same
//     content) -- inter frames should be tiny because every MB has
//     zero residual.
//   - A moving content sequence (key + a couple of inter frames
//     where the source content changes between frames) -- inter
//     frames have non-zero residuals which must round-trip through
//     the encoder/decoder pipeline.
//   - VP8Codec end-to-end: KeyframeIntervalFrames=4 with a 6-frame
//     sequence; the codec emits keyframes at frames 1 and 5 and
//     inter frames in between, and every decoded frame matches its
//     source within a small per-pixel tolerance.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 28 Apr 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class encode_inter_frame_unittest
    {
        [Fact]
        public void EncodeInterFrame_StaticSourceRoundTripsExactly()
        {
            // Static-source case: when the inter source equals the
            // already-encoded reference, every MB's residual is zero,
            // so every quantized coefficient is zero, every MB is
            // marked skippable, and the decoded frame equals the
            // reconstructed reference (which itself equals the
            // keyframe-decoded output). Result: an exact pixel match
            // through the full encode->decode round trip.
            const int width = 32, height = 32;
            const int qIndex = 32;

            byte[] yuv = MakeFlatI420(width, height, 120, 128, 128);
            var (y, u, v) = SplitI420(yuv, width, height);

            var ctx = OpenDecoder();

            // Frame 1 = keyframe.
            byte[] kf = frame_encoder.EncodeKeyframe(y, u, v, width, height, qIndex);
            byte[] keyDecoded = DecodeOne(ctx, kf, width, height);

            // Frame 2..5 = inter frames over the same source. Decoded
            // outputs should equal the keyframe's decoded output.
            for (int i = 0; i < 4; i++)
            {
                byte[] interFrame = frame_encoder.EncodeInterFrame(y, u, v, width, height, qIndex);
                Assert.True(interFrame.Length > 0);
                // For a static (residual=0) source, the inter frame's
                // payload is dominated by the per-MB skip flags and
                // mode tree bits -- no token coefficients at all. It
                // must be no larger than the corresponding keyframe.
                Assert.True(interFrame.Length <= kf.Length + 4,
                    $"static inter frame ({interFrame.Length}) should not exceed keyframe size ({kf.Length})");

                byte[] decoded = DecodeOne(ctx, interFrame, width, height);
                AssertExact(keyDecoded, decoded);
            }
        }

        [Theory]
        [InlineData(4,  50.0)]
        [InlineData(16, 45.0)]
        [InlineData(32, 35.0)]
        public void EncodeInterFrame_ChangingSourceRoundTripsWithinTolerance(int qIndex, double minPsnrDb)
        {
            // Moving-content case: the inter source is a shifted
            // version of the keyframe source. The encoder produces a
            // non-zero residual; the decoder applies it on top of its
            // copy of LAST_FRAME and should reconstruct a frame close
            // to the inter source. Higher Q -> more quantization
            // noise -> lower PSNR threshold.
            const int width = 64, height = 32;

            byte[] keyYuv   = MakeGradientI420(width, height, frame: 0);
            byte[] interYuv = MakeGradientI420(width, height, frame: 1);

            var (yK, uK, vK) = SplitI420(keyYuv,   width, height);
            var (yI, uI, vI) = SplitI420(interYuv, width, height);

            var ctx = OpenDecoder();

            byte[] kf = frame_encoder.EncodeKeyframe(yK, uK, vK, width, height, qIndex);
            byte[] decKey = DecodeOne(ctx, kf, width, height);

            byte[] inter = frame_encoder.EncodeInterFrame(yI, uI, vI, width, height, qIndex);
            byte[] decInter = DecodeOne(ctx, inter, width, height);

            // Decoded inter frame closely matches the inter source.
            AssertWithinPSNR(interYuv, decInter, minPsnrDb);

            // Sanity: the decoded inter is closer to the inter source
            // than the keyframe reconstruction is. This is the load-
            // bearing inter-vs-intra check -- if the residual were
            // being dropped or misencoded, dec_inter would equal
            // dec_key and this assertion would fail.
            double psnrInterVsInterSrc = ComputePSNR(interYuv, decInter);
            double psnrKeyVsInterSrc   = ComputePSNR(interYuv, decKey);
            Assert.True(psnrInterVsInterSrc > psnrKeyVsInterSrc + 5.0,
                $"inter encoding should bring the decoded frame substantially closer to the inter source than the reference. psnrInter={psnrInterVsInterSrc.ToString("F2")} psnrKey={psnrKeyVsInterSrc.ToString("F2")}");
        }

        [Fact]
        public void VP8Codec_EmitsInterFramesBetweenKeyframes()
        {
            // VP8Codec.EncodeVideo with KeyframeIntervalFrames=4 should
            // emit keyframes at frames 1 and 5 of a 6-frame stream and
            // inter frames in between. Verify the cadence by parsing
            // the 3-byte frame tag of each emitted frame
            // (key_frame_flag is bit 0 of byte 0).
            const int width = 32, height = 32;
            const int qIndex = 32;

            var codec = new VP8Codec
            {
                BaseQIndex = qIndex,
                KeyframeIntervalFrames = 4,
            };

            byte[] yuv = MakeFlatI420(width, height, 100, 128, 128);

            int[] expectedFrameType = { 0, 1, 1, 1, 0, 1 };  // 0 = key, 1 = inter
            for (int frame = 0; frame < expectedFrameType.Length; frame++)
            {
                byte[] enc = codec.EncodeVideo(width, height, yuv,
                    SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.I420,
                    SIPSorceryMedia.Abstractions.VideoCodecsEnum.VP8);
                Assert.NotNull(enc);
                Assert.True(enc.Length > 3);
                int keyFrameFlag = enc[0] & 0x1;
                Assert.Equal(expectedFrameType[frame], keyFrameFlag);
            }
        }

        [Fact]
        public void VP8Codec_KeyframeAndInterRoundTripThroughDecoder()
        {
            // End-to-end: a 6-frame static-source sequence with
            // KeyframeIntervalFrames=4 should decode every frame
            // exactly to the same bytes (by construction the source
            // doesn't change between frames, so every inter frame is
            // all-skip and equal to the keyframe reconstruction).
            const int width = 32, height = 32;
            const int qIndex = 32;

            var codec = new VP8Codec
            {
                BaseQIndex = qIndex,
                KeyframeIntervalFrames = 4,
            };

            byte[] yuv = MakeFlatI420(width, height, 100, 128, 128);
            var ctx = OpenDecoder();

            byte[] firstDecoded = null;
            for (int frame = 0; frame < 6; frame++)
            {
                byte[] enc = codec.EncodeVideo(width, height, yuv,
                    SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.I420,
                    SIPSorceryMedia.Abstractions.VideoCodecsEnum.VP8);
                byte[] dec = DecodeOne(ctx, enc, width, height);
                if (firstDecoded == null) firstDecoded = dec;
                else AssertExact(firstDecoded, dec);
            }
        }


        [Fact]
        public void VP8Codec_KeyAndInterDispatchedAcrossThreads()
        {
            // Reproducer for the bug encountered in the
            // WebRTCGetStartedVP8Net example: VideoTestPatternSource
            // dispatches each frame onto a Timer callback, which the
            // .NET thread pool is free to run on different worker
            // threads each tick. With the previous ThreadStatic
            // FrameEncoderBuffers each thread had its own LastFrameY
            // reference; if the keyframe ran on thread A and the next
            // inter call ran on thread B, B's buffers had no valid
            // reference and EncodeInterFrame would throw.
            //
            // This test forces calls onto distinct threads via
            // Task.Factory.StartNew(LongRunning) and verifies the
            // codec encodes both a keyframe and a subsequent inter
            // frame without throwing. The single per-codec
            // FrameEncoderBuffers (guarded by the codec's _encoderLock)
            // is what makes this work.
            const int width = 32, height = 32;

            var codec = new VP8Codec
            {
                BaseQIndex = 32,
                KeyframeIntervalFrames = 4,
            };

            byte[] yuv = new byte[width * height + 2 * (width / 2) * (height / 2)];
            for (int i = 0; i < width * height; i++) yuv[i] = 100;
            for (int i = width * height; i < yuv.Length; i++) yuv[i] = 128;

            int keyframeThreadId = -1;
            int interFrameThreadId = -1;

            // Frame 1 -- keyframe, on a fresh long-running thread.
            byte[] kf = System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                keyframeThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                return codec.EncodeVideo(width, height, yuv,
                    SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.I420,
                    SIPSorceryMedia.Abstractions.VideoCodecsEnum.VP8);
            }, System.Threading.Tasks.TaskCreationOptions.LongRunning).Result;

            Assert.NotNull(kf);
            Assert.True(kf.Length > 3);
            Assert.Equal(0, kf[0] & 0x1);  // key_frame_flag = 0 for keyframe

            // Frame 2 -- inter, on a *different* long-running thread.
            // With the old ThreadStatic buffers this would throw
            // InvalidOperationException("EncodeInterFrame requires a
            // valid LAST_FRAME reference") because the new thread's
            // buffers are empty.
            byte[] inter = System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                interFrameThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                return codec.EncodeVideo(width, height, yuv,
                    SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.I420,
                    SIPSorceryMedia.Abstractions.VideoCodecsEnum.VP8);
            }, System.Threading.Tasks.TaskCreationOptions.LongRunning).Result;

            Assert.NotNull(inter);
            Assert.True(inter.Length > 3);
            Assert.Equal(1, inter[0] & 0x1);  // key_frame_flag = 1 for inter

            // Sanity: the two calls really did run on different threads
            // (otherwise the test reduces to the single-thread case
            // which we already cover).
            Assert.NotEqual(keyframeThreadId, interFrameThreadId);
        }

        // ---------- helpers ----------

        private static vpx_codec_ctx_t OpenDecoder()
        {
            var ctx = new vpx_codec_ctx_t();
            var algo = vp8_dx.vpx_codec_vp8_dx();
            var cfg = new vpx_codec_dec_cfg_t { threads = 1 };
            var initRes = vpx_decoder.vpx_codec_dec_init(ctx, algo, cfg, 0);
            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, initRes);
            return ctx;
        }

        private static byte[] DecodeOne(vpx_codec_ctx_t ctx, byte[] frame, int width, int height)
        {
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

        private static byte[] MakeGradientI420(int width, int height, int frame)
        {
            // A gentle horizontal gradient in Y, shifted by 4 pixels per
            // frame. UV are flat 128. The shift is small enough to
            // produce a non-trivial but bounded inter residual at any Q.
            int ySize = width * height;
            int cSize = (width / 2) * (height / 2);
            var b = new byte[ySize + 2 * cSize];
            int shift = (frame * 4) % width;
            for (int row = 0; row < height; row++)
                for (int col = 0; col < width; col++)
                {
                    int x = (col + shift) % width;
                    b[row * width + col] = (byte)(40 + (x * 180) / width);
                }
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
                    $"byte {i} differs: expected={expected[i]} actual={actual[i]}");
            }
        }

        private static double ComputePSNR(byte[] expected, byte[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            double sse = 0.0;
            for (int i = 0; i < expected.Length; i++)
            {
                int d = expected[i] - actual[i];
                sse += d * d;
            }
            double mse = sse / expected.Length;
            return (mse == 0.0) ? double.PositiveInfinity : 10.0 * Math.Log10(255.0 * 255.0 / mse);
        }

        private static void AssertWithinPSNR(byte[] expected, byte[] actual, double minPsnrDb)
        {
            double psnr = ComputePSNR(expected, actual);
            Assert.True(psnr >= minPsnrDb,
                $"PSNR {psnr.ToString("F2")} dB below required {minPsnrDb.ToString("F2")} dB");
        }
    }
}
