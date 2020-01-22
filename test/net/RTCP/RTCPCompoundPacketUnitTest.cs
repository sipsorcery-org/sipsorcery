//-----------------------------------------------------------------------------
// Filename: RTCPCompoundPacketUnitTest.cs
//
// Description: Unit tests for the RTCPCompoundPacket class.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 30 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPCompoundPacketUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPCompoundPacketUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a RTCPCompoundPacket payload can be correctly serialised and 
        /// deserialised.
        /// </summary>
        [Fact]
        public void RoundtripRTCPCompoundPacketUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 23;
            ulong ntpTs = 1;
            uint rtpTs = 2;
            uint packetCount = 3;
            uint octetCount = 4;

            uint rrSsrc = 5;
            byte fractionLost = 6;
            int packetsLost = 7;
            uint highestSeqNum = 8;
            uint jitter = 9;
            uint lastSRTimestamp = 10;
            uint delaySinceLastSR = 11;

            string cname = "dummy";

            ReceptionReportSample rr = new ReceptionReportSample(rrSsrc, fractionLost, packetsLost, highestSeqNum, jitter, lastSRTimestamp, delaySinceLastSR);
            var sr = new RTCPSenderReport(ssrc, ntpTs, rtpTs, packetCount, octetCount, new List<ReceptionReportSample> { rr });
            RTCPSDesReport sdesReport = new RTCPSDesReport(ssrc, cname);

            RTCPCompoundPacket compoundPacket = new RTCPCompoundPacket(sr, sdesReport);

            byte[] buffer = compoundPacket.GetBytes();

            RTCPCompoundPacket parsedCP = new RTCPCompoundPacket(buffer);
            RTCPSenderReport parsedSR = parsedCP.SenderReport;

            Assert.Equal(ssrc, parsedSR.SSRC);
            Assert.Equal(ntpTs, parsedSR.NtpTimestamp);
            Assert.Equal(rtpTs, parsedSR.RtpTimestamp);
            Assert.Equal(packetCount, parsedSR.PacketCount);
            Assert.Equal(octetCount, parsedSR.OctetCount);
            Assert.True(parsedSR.ReceptionReports.Count == 1);

            Assert.Equal(rrSsrc, parsedSR.ReceptionReports.First().SSRC);
            Assert.Equal(fractionLost, parsedSR.ReceptionReports.First().FractionLost);
            Assert.Equal(packetsLost, parsedSR.ReceptionReports.First().PacketsLost);
            Assert.Equal(highestSeqNum, parsedSR.ReceptionReports.First().ExtendedHighestSequenceNumber);
            Assert.Equal(jitter, parsedSR.ReceptionReports.First().Jitter);
            Assert.Equal(lastSRTimestamp, parsedSR.ReceptionReports.First().LastSenderReportTimestamp);
            Assert.Equal(delaySinceLastSR, parsedSR.ReceptionReports.First().DelaySinceLastSenderReport);

            Assert.Equal(cname, parsedCP.SDesReport.CNAME);
        }

        [Fact]
        public void ParseChromeRtcpPacketUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var buffer = TypeExtensions.ParseHexStr("81C9000700000001384B9567000000000000D214000004C900000000000000008FCE0005000000010000000052454D42010A884A384B95678000000BF9CDAEFFBEF60160B98F");

            RTCPCompoundPacket cp = new RTCPCompoundPacket(buffer);

            Assert.NotNull(cp);
        }

        [Fact]
        public void ParseChromeRtcpPacket2UnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var buffer = TypeExtensions.ParseHexStr("81C90007FA17FA17761E74C8000000000000F19700000045000000000000000080000001FF6EBFCCFAFB3C6D6291");

            RTCPCompoundPacket cp = new RTCPCompoundPacket(buffer);

            Assert.NotNull(cp);
        }
    }
}
