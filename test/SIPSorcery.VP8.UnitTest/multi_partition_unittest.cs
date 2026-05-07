//-----------------------------------------------------------------------------
// Multi-token-partition encoder round-trips: encode keyframe + inter for
// log2_nbr_of_dct_partitions in {0, 1, 2, 3} (1 / 2 / 4 / 8 token
// partitions) and verify the libvpx decoder accepts the bitstream and
// recovers pixels within tolerance. Also extends pipeline parity to
// cover every partition count.
//
// Bit-exactness is preserved for log2N == 0 (the existing tests cover
// that). For log2N > 0 the encoder produces a different bitstream
// because tokens are split across partitions and a (N-1)*3-byte size
// table is inserted after partition 0; what we assert is decoder
// round-trip correctness, not bit-equality with the single-partition
// path.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class multi_partition_unittest
    {
        public static TheoryData<int> Log2PartitionCounts => new TheoryData<int>
        {
            0, 1, 2, 3,
        };

        public static TheoryData<Vp8EncodePipelineKind, int> PipelineAndPartitions => new TheoryData<Vp8EncodePipelineKind, int>
        {
            { Vp8EncodePipelineKind.Legacy,    0 },
            { Vp8EncodePipelineKind.Legacy,    1 },
            { Vp8EncodePipelineKind.Legacy,    2 },
            { Vp8EncodePipelineKind.Legacy,    3 },
            { Vp8EncodePipelineKind.Optimized, 0 },
            { Vp8EncodePipelineKind.Optimized, 1 },
            { Vp8EncodePipelineKind.Optimized, 2 },
            { Vp8EncodePipelineKind.Optimized, 3 },
        };

        // ---------- Keyframe round-trip across log2N ----------

        [Theory]
        [MemberData(nameof(PipelineAndPartitions))]
        public void EncodeKeyframe_Checkerboard128x128_RoundTripsForLog2N(Vp8EncodePipelineKind kind, int log2N)
        {
            // 128x128 = 8x8 MB grid. Wide enough for log2N=3 (8 partitions,
            // 1 row per partition) and tall enough that log2N=2 / 1 each
            // get multiple rows.
            const int W = 128, H = 128;
            byte[] yuv = MakeCheckerboardI420(W, H, dark: 50, light: 200);

            byte[] frame = EncodeKeyframe(kind, log2N, yuv, W, H, qIndex: 32);
            byte[] decoded = DecodeI420(frame, W, H);

            AssertWithin(yuv, decoded, tol: 16);
        }

        [Theory]
        [MemberData(nameof(Log2PartitionCounts))]
        public void EncodeKeyframe_FlatGrey16x16_RoundTripsExactly(int log2N)
        {
            // Smallest possible frame (1 MB row). Stresses the
            // empty-partition stitch path for log2N >= 1.
            const int W = 16, H = 16;
            byte[] yuv = MakeFlatI420(W, H, y: 128, u: 128, v: 128);

            byte[] frame = EncodeKeyframe(Vp8EncodePipelineKind.Optimized, log2N, yuv, W, H, qIndex: 32);
            byte[] decoded = DecodeI420(frame, W, H);

            AssertExact(yuv, decoded);
        }

        // ---------- Inter round-trip across log2N ----------

        [Theory]
        [MemberData(nameof(PipelineAndPartitions))]
        public void EncodeInter_Checkerboard128x128_AfterKeyRoundTripsForLog2N(Vp8EncodePipelineKind kind, int log2N)
        {
            const int W = 128, H = 128;
            byte[] yuv = MakeCheckerboardI420(W, H, dark: 50, light: 200);

            // Encode keyframe and inter through the same VP8Codec instance
            // so LAST_FRAME state is consistent.
            using var codec = new VP8Codec(kind, log2N);
            codec.BaseQIndex = 32;

            byte[] keyFrame = codec.EncodeVideo(W, H, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            Assert.NotNull(keyFrame);

            byte[] yuvInter = (byte[])yuv.Clone();
            for (int i = 0; i < yuvInter.Length; i++) yuvInter[i] = (byte)((yuvInter[i] + 13) & 0xff);

            byte[] interFrame = codec.EncodeVideo(W, H, yuvInter, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            Assert.NotNull(interFrame);

            // Decode both frames sequentially through one decoder and
            // compare the second decoded frame against the inter source.
            var ctx = new vpx_codec_ctx_t();
            var algo = vp8_dx.vpx_codec_vp8_dx();
            var cfg = new vpx_codec_dec_cfg_t { threads = 1 };
            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK,
                vpx_decoder.vpx_codec_dec_init(ctx, algo, cfg, 0));

            DecodeFrame(ctx, keyFrame);     // seeds LAST_FRAME
            byte[] decoded = DecodeFrame(ctx, interFrame, W, H);

            AssertWithin(yuvInter, decoded, tol: 24);
        }

        // ---------- Pipeline parity at each log2N ----------

        [Theory]
        [MemberData(nameof(Log2PartitionCounts))]
        public void Keyframe_LegacyVsOptimized_BitExactPerLog2N(int log2N)
        {
            const int W = 128, H = 128;
            byte[] yuv = MakeCheckerboardI420(W, H, dark: 50, light: 200);

            var encA = Vp8FrameEncodePipelineFactory.Create(Vp8EncodePipelineKind.Legacy, log2N);
            var encB = Vp8FrameEncodePipelineFactory.Create(Vp8EncodePipelineKind.Optimized, log2N);
            var fbA = new FrameEncoderBuffers();
            var fbB = new FrameEncoderBuffers();

            byte[] outA = encA.EncodeKeyframeContiguousI420(yuv, W, H, 32, fbA);
            byte[] outB = encB.EncodeKeyframeContiguousI420(yuv, W, H, 32, fbB);
            Assert.Equal(outA, outB);
        }

        [Theory]
        [MemberData(nameof(Log2PartitionCounts))]
        public void Inter_LegacyVsOptimized_BitExactPerLog2N(int log2N)
        {
            const int W = 128, H = 128;
            byte[] yuv = MakeCheckerboardI420(W, H, dark: 50, light: 200);

            var encA = Vp8FrameEncodePipelineFactory.Create(Vp8EncodePipelineKind.Legacy, log2N);
            var encB = Vp8FrameEncodePipelineFactory.Create(Vp8EncodePipelineKind.Optimized, log2N);
            var fbA = new FrameEncoderBuffers();
            var fbB = new FrameEncoderBuffers();

            byte[] keyA = encA.EncodeKeyframeContiguousI420(yuv, W, H, 32, fbA);
            byte[] keyB = encB.EncodeKeyframeContiguousI420(yuv, W, H, 32, fbB);
            Assert.Equal(keyA, keyB);

            byte[] yuvInter = (byte[])yuv.Clone();
            for (int i = 0; i < yuvInter.Length; i++) yuvInter[i] = (byte)((yuvInter[i] + 13) & 0xff);

            byte[] interA = encA.EncodeInterFrameContiguousI420(yuvInter, W, H, 32, fbA);
            byte[] interB = encB.EncodeInterFrameContiguousI420(yuvInter, W, H, 32, fbB);
            Assert.Equal(interA, interB);
        }

        // ---------- Header bit sanity ----------

        [Fact]
        public void EncodeKeyframe_Log2N3_HeaderEncodesEightPartitions()
        {
            // log2N=3 -> 2-bit field with value 3 in the header (bits read
            // by the decoder as log2_nbr_of_dct_partitions). Decoder must
            // then read 7 size table entries and 8 partitions. Our
            // round-trip test above verifies the decoder round-trips
            // correctly; this test just sanity-checks the encoded
            // bitstream is non-empty and not the same as log2N=0.
            const int W = 32, H = 32;
            byte[] yuv = MakeCheckerboardI420(W, H, dark: 50, light: 200);

            byte[] frame0 = EncodeKeyframe(Vp8EncodePipelineKind.Optimized, 0, yuv, W, H, qIndex: 32);
            byte[] frame3 = EncodeKeyframe(Vp8EncodePipelineKind.Optimized, 3, yuv, W, H, qIndex: 32);

            // log2N=3 inserts a (8-1)*3 = 21-byte size table; total
            // bitstream must be at least 21 bytes longer than the
            // single-partition equivalent (more in practice because the
            // extra bool-coder flush bytes per partition also add up).
            Assert.True(frame3.Length >= frame0.Length + 21,
                $"log2N=3 frame ({frame3.Length} bytes) should be at least 21 bytes larger than log2N=0 frame ({frame0.Length} bytes)");
        }

        // ---------- helpers ----------

        private static byte[] EncodeKeyframe(Vp8EncodePipelineKind kind, int log2N, byte[] i420, int w, int h, int qIndex)
        {
            var enc = Vp8FrameEncodePipelineFactory.Create(kind, log2N);
            var fb = new FrameEncoderBuffers();
            return enc.EncodeKeyframeContiguousI420(i420, w, h, qIndex, fb);
        }

        private static byte[] DecodeI420(byte[] frame, int width, int height)
        {
            var ctx = new vpx_codec_ctx_t();
            var algo = vp8_dx.vpx_codec_vp8_dx();
            var cfg = new vpx_codec_dec_cfg_t { threads = 1 };
            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK,
                vpx_decoder.vpx_codec_dec_init(ctx, algo, cfg, 0));
            return DecodeFrame(ctx, frame, width, height);
        }

        private static byte[] DecodeFrame(vpx_codec_ctx_t ctx, byte[] frame, int width = 0, int height = 0)
        {
            fixed (byte* pFrame = frame)
            {
                var decRes = vpx_decoder.vpx_codec_decode(ctx, pFrame, (uint)frame.Length, IntPtr.Zero, 0);
                Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, decRes);
            }

            IntPtr iter = IntPtr.Zero;
            var img = vpx_decoder.vpx_codec_get_frame(ctx, iter);
            Assert.NotNull(img);

            int w = width  != 0 ? width  : (int)img.d_w;
            int h = height != 0 ? height : (int)img.d_h;
            int sz = w * h, csz = (w / 2) * (h / 2);
            byte[] outYuv = new byte[sz + 2 * csz];

            for (int row = 0; row < h; row++)
            {
                Marshal.Copy((IntPtr)(img.planes[0] + row * img.stride[0]), outYuv, row * w, w);
                if (row < h / 2)
                {
                    Marshal.Copy((IntPtr)(img.planes[1] + row * img.stride[1]), outYuv, sz + row * (w / 2), w / 2);
                    Marshal.Copy((IntPtr)(img.planes[2] + row * img.stride[2]), outYuv, sz + csz + row * (w / 2), w / 2);
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

        private static void AssertExact(byte[] expected, byte[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
                Assert.True(expected[i] == actual[i],
                    "Pixel " + i + " differs: src=" + expected[i] + " decoded=" + actual[i]);
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
