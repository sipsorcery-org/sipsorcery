//-----------------------------------------------------------------------------
// Filename: RTCPFeedbackUnitTest.cs
//
// Description: Unit tests for the RTCPFeedback class.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Jun 2020  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPFeedbackUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPFeedbackUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that an RTCPFeedback report for a Picture Loss Indication payload can 
        /// be correctly serialised and deserialised.
        /// </summary>
        [Fact]
        public void RoundtripPictureLossIndicationReportUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint senderSsrc = 33;
            uint mediaSsrc = 44;

            RTCPFeedback rtcpPli = new RTCPFeedback(senderSsrc, mediaSsrc, PSFBFeedbackTypesEnum.PLI);
            byte[] buffer = rtcpPli.GetBytes();

            logger.LogDebug($"Serialised PLI feedback report: {BufferUtils.HexStr(buffer)}.");

            RTCPFeedback parsedPli = new RTCPFeedback(buffer);

            Assert.Equal(RTCPReportTypesEnum.PSFB, parsedPli.Header.PacketType);
            Assert.Equal(PSFBFeedbackTypesEnum.PLI, parsedPli.Header.PayloadFeedbackMessageType);
            Assert.Equal(senderSsrc, parsedPli.SenderSSRC);
            Assert.Equal(mediaSsrc, parsedPli.MediaSSRC);
            Assert.Equal(2, parsedPli.Header.Length);
       }
    }
}
