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
                SENDER_PAYLOAD_SIZE = 8 + 12+4+4, // 8 bytes from (SenderSSRC + MediaSSRC) + extra 12 bytes from REMB Definition +2x extra 8 bytes for SSRCs
                UniqueID = "REMB",
                NumSsrcs = 3,
                BitrateExp = 4,
                BitrateMantissa = 222242u,
                FeedbackSSRCs = new uint[]{0x4a8eec30,0x4a8eec44,0x4a8eec58}
            };
            byte[] buffer = rtcpREMB.GetBytes();

            logger.LogDebug("Serialised REMB: {Buffer}", BufferUtils.HexStr(buffer));

            RTCPFeedback parsedREMB = new RTCPFeedback(buffer);
            var parsedBuffer = parsedREMB.GetBytes();
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

        /// <summary>
        /// The bitmask of a generic NACK reports the packets after its packet ID, wrapping at the end of the
        /// sequence number space.
        /// </summary>
        [Fact]
        public void ParseGenericNackLostSequenceNumbersUnitTest()
        {
            var nack = new RTCPFeedback(33, 44, RTCPFeedbackTypesEnum.NACK, ushort.MaxValue - 1, 0b1000_0000_0000_0101);

            var parsed = new RTCPFeedback(nack.GetBytes());

            Assert.Equal(new ushort[] { ushort.MaxValue - 1, ushort.MaxValue, 1, 14 }, parsed.LostSequenceNumbers);
            Assert.Empty(new RTCPFeedback(new RTCPFeedback(33, 44, RTCPFeedbackTypesEnum.TMMBR, 1, 1).GetBytes()).LostSequenceNumbers);
        }

        /// <summary>
        /// RTCP padding after the entries of a generic NACK is not read as more entries.
        /// </summary>
        [Fact]
        public void ParsePaddedGenericNackUnitTest()
        {
            const byte PADDING_FLAG = 0x20;
            const byte PADDING_LENGTH = 4;
            byte[] padded = [.. new RTCPFeedback(33, 44, RTCPFeedbackTypesEnum.NACK, 7, 0).GetBytes(), 0, 0, 0, PADDING_LENGTH];
            padded[0] |= PADDING_FLAG;
            padded[3] += PADDING_LENGTH / 4;

            Assert.Equal(new ushort[] { 7 }, new RTCPFeedback(padded).LostSequenceNumbers);
        }
    }
}
