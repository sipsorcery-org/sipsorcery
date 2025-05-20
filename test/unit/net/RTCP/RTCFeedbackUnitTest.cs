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

using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            uint senderSsrc = 33;
            uint mediaSsrc = 44;

            RTCPFeedback rtcpPli = new RTCPFeedback(senderSsrc, mediaSsrc, PSFBFeedbackTypesEnum.PLI);
            byte[] buffer = new byte[rtcpPli.GetByteCount()];
            rtcpPli.WriteBytes(buffer.AsSpan());

            logger.LogDebug("Serialised PLI feedback report: {Buffer}", buffer.HexStr());

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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

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
            byte[] buffer = new byte[rtcpREMB.GetByteCount()];
            rtcpREMB.WriteBytes(buffer.AsSpan());

            logger.LogDebug("Serialised REMB: {Buffer}", buffer.HexStr());

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

        /// <summary>
        /// Tests that an RTCPFeedback for REMB payload with multiple SSRCs can
        /// be correctly serialised and deserialised.
        /// </summary>
        [Fact]
        public void RoundtripREMBUnitTestMultipleSsrcs()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            uint senderSsrc = 33;
            uint mediaSsrc = 44;

            RTCPFeedback rtcpREMB = new RTCPFeedback(senderSsrc, mediaSsrc, PSFBFeedbackTypesEnum.AFB)
            {
                SENDER_PAYLOAD_SIZE = 8 + 12 + 4 + 4, // 8 bytes from (SenderSSRC + MediaSSRC) + extra 12 bytes from REMB Definition +2x extra 8 bytes for SSRCs
                UniqueID = "REMB",
                NumSsrcs = 3,
                BitrateExp = 4,
                BitrateMantissa = 222242u,
                FeedbackSSRCs = new uint[] { 0x4a8eec30, 0x4a8eec44, 0x4a8eec58 }
            };

            byte[] buffer = new byte[rtcpREMB.GetByteCount()]; rtcpREMB.WriteBytes(buffer);

            logger.LogDebug("Serialised REMB: {Buffer}", buffer.HexStr());

            RTCPFeedback parsedREMB = new RTCPFeedback(buffer);
            var parsedBuffer = new byte[parsedREMB.GetByteCount()]; parsedREMB.WriteBytes(parsedBuffer);
            Assert.Equal(parsedBuffer, buffer);
            Assert.Equal(RTCPReportTypesEnum.PSFB, parsedREMB.Header.PacketType);
            Assert.Equal(PSFBFeedbackTypesEnum.AFB, parsedREMB.Header.PayloadFeedbackMessageType);
            Assert.Equal(senderSsrc, parsedREMB.SenderSSRC);
            Assert.Equal(mediaSsrc, parsedREMB.MediaSSRC);
            Assert.Equal(rtcpREMB.UniqueID, parsedREMB.UniqueID);
            Assert.Equal(rtcpREMB.NumSsrcs, parsedREMB.NumSsrcs);
            Assert.Equal(rtcpREMB.BitrateExp, parsedREMB.BitrateExp);
            Assert.Equal(rtcpREMB.BitrateMantissa, parsedREMB.BitrateMantissa);
            Assert.Equal(rtcpREMB.FeedbackSSRC, parsedREMB.FeedbackSSRC);
            Assert.Equal(rtcpREMB.FeedbackSSRCs, parsedREMB.FeedbackSSRCs);
        }
    }
}
