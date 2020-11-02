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
using SIPSorceryMedia.Abstractions.V1;
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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPAudioVideoMediaFormat pcmu = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU);

            var audioFormat = pcmu.ToAudioFormat();

            Assert.Equal(SDPMediaTypesEnum.audio, pcmu.Kind);
            Assert.Equal(AudioCodecsEnum.PCMU, audioFormat.Codec);
            Assert.Equal(8000, audioFormat.ClockRate);
            Assert.Equal("PCMU/8000", pcmu.Rtpmap);
        }
    }
}
