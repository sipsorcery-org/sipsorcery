//-----------------------------------------------------------------------------
// Filename: vp8codec_keyframe_interval_unittest.cs
//
// Description: Unit tests for the KeyframeIntervalFrames property and
// keyframe-vs-inter cadence state machine on VP8Codec, added in PR 1
// of the P-frame foundation series. The actual inter encoding path
// is not implemented yet, so these tests exercise only the counter
// behaviour; correctness of the eventual inter bitstream is tested
// in PRs 3-5.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 27 Apr 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class vp8codec_keyframe_interval_unittest
    {
        // ---------- KeyframeIntervalFrames property ----------

        [Fact]
        public void KeyframeIntervalFrames_DefaultIs30()
        {
            var codec = new VP8Codec();
            Assert.Equal(30, codec.KeyframeIntervalFrames);
        }

        [Fact]
        public void KeyframeIntervalFrames_SetTo1Allowed()
        {
            var codec = new VP8Codec();
            codec.KeyframeIntervalFrames = 1;
            Assert.Equal(1, codec.KeyframeIntervalFrames);
        }

        [Fact]
        public void KeyframeIntervalFrames_SetToZeroThrows()
        {
            var codec = new VP8Codec();
            Assert.Throws<ArgumentOutOfRangeException>(() => codec.KeyframeIntervalFrames = 0);
        }

        [Fact]
        public void KeyframeIntervalFrames_SetToNegativeThrows()
        {
            var codec = new VP8Codec();
            Assert.Throws<ArgumentOutOfRangeException>(() => codec.KeyframeIntervalFrames = -1);
        }

        // ---------- Frame-counter cadence ----------

        [Fact]
        public void FramesSinceLastKeyframe_IsZeroBeforeFirstEncode()
        {
            var codec = new VP8Codec();
            Assert.Equal(0, codec.FramesSinceLastKeyframe);
        }

        [Fact]
        public void FramesSinceLastKeyframe_IsOneAfterFirstEncode()
        {
            var codec = new VP8Codec();
            byte[] yuv = MakeFlatI420(16, 16, y: 128, u: 128, v: 128);
            codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            // First encode is always a keyframe (no reference yet) -> counter = 1.
            Assert.Equal(1, codec.FramesSinceLastKeyframe);
        }

        [Fact]
        public void FramesSinceLastKeyframe_IncrementsOnInterFrames()
        {
            var codec = new VP8Codec { KeyframeIntervalFrames = 30 };
            byte[] yuv = MakeFlatI420(16, 16, y: 128, u: 128, v: 128);

            // Encode 5 frames. With interval=30, frames 2..5 should be inter
            // (and the counter should go 1, 2, 3, 4, 5 across them).
            for (int i = 1; i <= 5; i++)
            {
                codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
                Assert.Equal(i, codec.FramesSinceLastKeyframe);
            }
        }

        [Fact]
        public void FramesSinceLastKeyframe_ResetsAfterIntervalKeyframe()
        {
            var codec = new VP8Codec { KeyframeIntervalFrames = 3 };
            byte[] yuv = MakeFlatI420(16, 16, y: 128, u: 128, v: 128);

            // Frame 1: keyframe (first frame). counter -> 1.
            codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            Assert.Equal(1, codec.FramesSinceLastKeyframe);

            // Frame 2: inter. counter -> 2.
            codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            Assert.Equal(2, codec.FramesSinceLastKeyframe);

            // Frame 3: inter (counter == interval == 3 means we've sent
            // 2 inter frames since last key, this 3rd would be the next
            // keyframe). counter -> 3.
            codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            Assert.Equal(3, codec.FramesSinceLastKeyframe);

            // Frame 4: counter has reached interval, this is the next
            // keyframe. counter resets to 1.
            codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            Assert.Equal(1, codec.FramesSinceLastKeyframe);

            // Frame 5: inter again. counter -> 2.
            codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            Assert.Equal(2, codec.FramesSinceLastKeyframe);
        }

        [Fact]
        public void ForceKeyFrame_ResetsCounter()
        {
            var codec = new VP8Codec { KeyframeIntervalFrames = 30 };
            byte[] yuv = MakeFlatI420(16, 16, y: 128, u: 128, v: 128);

            // Run a few frames into the cadence.
            for (int i = 0; i < 5; i++)
            {
                codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            }
            Assert.Equal(5, codec.FramesSinceLastKeyframe);

            // ForceKeyFrame -> next encode is a keyframe -> counter = 1.
            codec.ForceKeyFrame();
            codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
            Assert.Equal(1, codec.FramesSinceLastKeyframe);
        }

        [Fact]
        public void KeyframeIntervalFrames_OneEveryFrameIsKeyframe()
        {
            var codec = new VP8Codec { KeyframeIntervalFrames = 1 };
            byte[] yuv = MakeFlatI420(16, 16, y: 128, u: 128, v: 128);

            // With interval = 1, every encode is a keyframe; counter
            // resets to 1 each time.
            for (int i = 0; i < 5; i++)
            {
                codec.EncodeVideo(16, 16, yuv, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);
                Assert.Equal(1, codec.FramesSinceLastKeyframe);
            }
        }

        // ---------- helpers ----------

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
    }
}
