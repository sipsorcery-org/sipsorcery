//-----------------------------------------------------------------------------
// Filename: SDPAudioVideoMediaFormatUnitTest.cs
//
// Description: Unit tests for the SDPAudioVideoMediaFormat class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Oct 2020  Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>
    /// Unit tests for Session Description Protocol (SDP) class.
    /// </summary>
    [Trait("Category", "unit")]
    public class SDPAudioVideoMediaFormatUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SDPAudioVideoMediaFormatUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a well known SDP audio format is correctly mapped to an audio format.
        /// </summary>
        [Fact]
        public void MapWellKnownAudioFormatUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPAudioVideoMediaFormat pcmu = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU);

            var audioFormat = pcmu.ToAudioFormat();

            Assert.Equal(SDPMediaTypesEnum.audio, pcmu.Kind);
            Assert.Equal(AudioCodecsEnum.PCMU, audioFormat.Codec);
            Assert.Equal(8000, audioFormat.ClockRate);
            Assert.Equal("PCMU/8000", pcmu.Rtpmap);
        }

        /// <summary>
        /// Tests that a dynamic AV1 video format is serialised to SDP and mapped back.
        /// </summary>
        [Fact]
        public void MapDynamicAv1VideoFormatUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var av1 = new VideoFormat(VideoCodecsEnum.AV1, 96);
            var sdpFormat = new SDPAudioVideoMediaFormat(av1);
            var roundTrip = sdpFormat.ToVideoFormat();

            Assert.Equal(SDPMediaTypesEnum.video, sdpFormat.Kind);
            Assert.Equal("AV1/90000", sdpFormat.Rtpmap);
            Assert.Equal(VideoCodecsEnum.AV1, roundTrip.Codec);
            Assert.Equal("AV1", roundTrip.FormatName);
            Assert.Equal(VideoFormat.DEFAULT_CLOCK_RATE, roundTrip.ClockRate);
        }
    }
}
