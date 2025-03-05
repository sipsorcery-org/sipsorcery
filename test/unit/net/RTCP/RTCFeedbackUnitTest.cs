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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint senderSsrc = 33;
            uint mediaSsrc = 44;

            RTCPFeedback rtcpPli = new RTCPFeedback(senderSsrc, mediaSsrc, PSFBFeedbackTypesEnum.PLI);
            byte[] buffer = rtcpPli.GetBytes();

            logger.LogDebug("Serialised PLI feedback report: {Buffer}", BufferUtils.HexStr(buffer));

            RTCPFeedback parsedPli = new RTCPFeedback(buffer);

            Assert.Equal(RTCPReportTypesEnum.PSFB, parsedPli.Header.PacketType);
            Assert.Equal(PSFBFeedbackTypesEnum.PLI, parsedPli.Header.PayloadFeedbackMessageType);
            Assert.Equal(senderSsrc, parsedPli.SenderSSRC);
            Assert.Equal(mediaSsrc, parsedPli.MediaSSRC);
            Assert.Equal(2, parsedPli.Header.Length);
        }

        /// <summary>
        /// Tests that an RTCPFeedback for REMB payload can
        /// be correctly serialised and deserialised.
        /// </summary>
        [Fact]
        public void RoundtripREMBUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint senderSsrc = 33;
            uint mediaSsrc = 44;

            RTCPFeedback rtcpREMB = new RTCPFeedback(senderSsrc, mediaSsrc, PSFBFeedbackTypesEnum.AFB)
            {
                SENDER_PAYLOAD_SIZE = 8 + 12, // 8 bytes from (SenderSSRC + MediaSSRC) + extra 12 bytes from REMB Definition
                UniqueID = "REMB",
                NumSsrcs = 1,
                BitrateExp = 4,
                BitrateMantissa = 222242u,
                FeedbackSSRC = 0x4a8eec30
            };
            byte[] buffer = rtcpREMB.GetBytes();

            logger.LogDebug("Serialised REMB: {Buffer}", BufferUtils.HexStr(buffer));

            RTCPFeedback parsedREMB = new RTCPFeedback(buffer);

            Assert.Equal(RTCPReportTypesEnum.PSFB, parsedREMB.Header.PacketType);
            Assert.Equal(PSFBFeedbackTypesEnum.AFB, parsedREMB.Header.PayloadFeedbackMessageType);
            Assert.Equal(senderSsrc, parsedREMB.SenderSSRC);
            Assert.Equal(mediaSsrc, parsedREMB.MediaSSRC);
            Assert.Equal(rtcpREMB.UniqueID, parsedREMB.UniqueID);
            Assert.Equal(rtcpREMB.NumSsrcs, parsedREMB.NumSsrcs);
            Assert.Equal(rtcpREMB.BitrateExp, parsedREMB.BitrateExp);
            Assert.Equal(rtcpREMB.BitrateMantissa, parsedREMB.BitrateMantissa);
            Assert.Equal(rtcpREMB.FeedbackSSRC, parsedREMB.FeedbackSSRC);
        }
    }
}
