//-----------------------------------------------------------------------------
// Encoded bitstream parity: Legacy vs Optimized encoder pipelines must match.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class encode_pipeline_parity_unittest
    {
        public static TheoryData<Vp8EncodePipelineKind, Vp8EncodePipelineKind> PipelinePairs => new TheoryData<Vp8EncodePipelineKind, Vp8EncodePipelineKind>
        {
            { Vp8EncodePipelineKind.Legacy, Vp8EncodePipelineKind.Optimized },
        };

        [Theory]
        [MemberData(nameof(PipelinePairs))]
        public void Keyframe_16x16_ContiguousI420_Matches(Vp8EncodePipelineKind a, Vp8EncodePipelineKind b)
        {
            var buf = MakeGradientI420(16, 16);
            var encA = Vp8FrameEncodePipelineFactory.Create(a);
            var encB = Vp8FrameEncodePipelineFactory.Create(b);
            var fb = new FrameEncoderBuffers();
            byte[] yuv = buf;
            byte[] outA = encA.EncodeKeyframeContiguousI420(yuv, 16, 16, 32, fb);
            fb.LastFrameValid = false;
            byte[] outB = encB.EncodeKeyframeContiguousI420(yuv, 16, 16, 32, fb);
            Assert.Equal(outA, outB);
        }

        [Theory]
        [MemberData(nameof(PipelinePairs))]
        public void Keyframe_640x480_TestPattern_File_Matches(Vp8EncodePipelineKind a, Vp8EncodePipelineKind b)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "testpattern.i420");
            if (!File.Exists(path))
            {
                return;
            }
            byte[] yuv = File.ReadAllBytes(path);
            const int w = 640, h = 480;
            Assert.Equal(w * h * 3 / 2, yuv.Length);
            var encA = Vp8FrameEncodePipelineFactory.Create(a);
            var encB = Vp8FrameEncodePipelineFactory.Create(b);
            var fbA = new FrameEncoderBuffers();
            var fbB = new FrameEncoderBuffers();
            byte[] outA = encA.EncodeKeyframeContiguousI420(yuv, w, h, 32, fbA);
            byte[] outB = encB.EncodeKeyframeContiguousI420(yuv, w, h, 32, fbB);
            Assert.Equal(outA, outB);
        }

        [Theory]
        [MemberData(nameof(PipelinePairs))]
        public void Inter_AfterKey_Matches(Vp8EncodePipelineKind a, Vp8EncodePipelineKind b)
        {
            var encA = Vp8FrameEncodePipelineFactory.Create(a);
            var encB = Vp8FrameEncodePipelineFactory.Create(b);
            var fbA = new FrameEncoderBuffers();
            var fbB = new FrameEncoderBuffers();
            byte[] yuv = MakeGradientI420(16, 16);
            byte[] keyA = encA.EncodeKeyframeContiguousI420(yuv, 16, 16, 48, fbA);
            byte[] keyB = encB.EncodeKeyframeContiguousI420(yuv, 16, 16, 48, fbB);
            Assert.Equal(keyA, keyB);
            for (int i = 0; i < yuv.Length; i++) yuv[i] = (byte)((yuv[i] + 17) & 0xff);
            byte[] interA = encA.EncodeInterFrameContiguousI420(yuv, 16, 16, 48, fbA);
            byte[] interB = encB.EncodeInterFrameContiguousI420(yuv, 16, 16, 48, fbB);
            Assert.Equal(interA, interB);
        }

        [Fact]
        public void VP8Codec_Default_IsOptimized()
        {
            using var c = new VP8Codec();
            var field = typeof(VP8Codec).GetField("_encodePipeline",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            var pipeline = field.GetValue(c);
            Assert.Same(OptimizedVp8FrameEncodePipeline.Instance, pipeline);
        }

        private static byte[] MakeGradientI420(int width, int height)
        {
            int ySize = width * height;
            int cSize = (width / 2) * (height / 2);
            var buf = new byte[ySize + 2 * cSize];
            int i = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                buf[i++] = (byte)((x + y * 3) & 0xff);
            for (int j = 0; j < cSize; j++) buf[i++] = (byte)(80 + (j & 31));
            for (int j = 0; j < cSize; j++) buf[i++] = (byte)(120 + (j & 31));
            return buf;
        }
    }
}
