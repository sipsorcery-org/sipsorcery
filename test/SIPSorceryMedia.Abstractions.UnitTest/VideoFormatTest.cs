//-----------------------------------------------------------------------------
// Filename: VideoFormatTest.cs
//
// Description: Unit tests for the video format model.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 28 Mar 2026  OpenAI         Added AV1 coverage.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace SIPSorceryMedia.Abstractions.UnitTest
{
    public class VideoFormatTest
    {
        [Fact]
        public void DynamicAv1FormatMapsToAv1CodecUnitTest()
        {
            const int formatId = 96;
            const int clockRate = VideoFormat.DEFAULT_CLOCK_RATE;

            var videoFormat = new VideoFormat(formatId, "AV1", clockRate);

            Assert.Equal(VideoCodecsEnum.AV1, videoFormat.Codec);
            Assert.Equal(formatId, videoFormat.FormatID);
            Assert.Equal("AV1", videoFormat.FormatName);
            Assert.Equal(clockRate, videoFormat.ClockRate);
        }

        [Fact]
        public void Av1EnumCreatesRfcAlignedDynamicFormatUnitTest()
        {
            const int formatId = 96;

            var videoFormat = new VideoFormat(VideoCodecsEnum.AV1, formatId);

            Assert.Equal(VideoCodecsEnum.AV1, videoFormat.Codec);
            Assert.Equal(formatId, videoFormat.FormatID);
            Assert.Equal("AV1", videoFormat.FormatName);
            Assert.Equal(VideoFormat.DEFAULT_CLOCK_RATE, videoFormat.ClockRate);
        }
    }
}
