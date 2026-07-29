//-----------------------------------------------------------------------------
// Filename: RTCPSenderReportUnitTest.cs
//
// Description: Unit tests for the RTCPSenderReport class.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPSenderResportUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPSenderResportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a RTCPSenderReport payload can be correctly serialised and 
        /// deserialised.
        /// </summary>
        [Fact]
        public void RoundtripRTCPSenderResportUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            uint ssrc = 23;
            ulong ntpTs = 1;
            uint rtpTs = 2;
            uint packetCount = 3;
            uint octetCount = 4;

            uint rrSsrc = 5;
            byte fractionLost = 6;
            var packetsLost = 7;
            uint highestSeqNum = 8;
            uint jitter = 9;
            uint lastSRTimestamp = 10;
            uint delaySinceLastSR = 11;

            ReceptionReportSample rr = new ReceptionReportSample(rrSsrc, fractionLost, packetsLost, highestSeqNum, jitter, lastSRTimestamp, delaySinceLastSR);

            var sr = new RTCPSenderReport(ssrc, ntpTs, rtpTs, packetCount, octetCount, new List<ReceptionReportSample> { rr });
            var buffer = sr.GetBytes();

            RTCPSenderReport parsedSR = new RTCPSenderReport(buffer);

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
        }

        /// <summary>
        /// Tests parsing a Sender Report from a byte array works correctly.
        /// </summary>
        [Fact]
        public void ParseSenderReportUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = TypeExtensions.ParseHexStr("80C8000641446122E1D2B0EA004B650C0000D556000001310003BA5A");

            RTCPSenderReport sr = new RTCPSenderReport(buffer);

            Assert.NotNull(sr);
            Assert.Equal(1095000354U, sr.SSRC);
        }

        /// <summary>
        /// Tests that a sender report whose header claims more reception reports than the packet
        /// actually contains is parsed tolerantly: the complete reports are returned and the
        /// missing ones are skipped without an exception. RTCP packets arrive from the network so
        /// a truncated or corrupted report count must not be able to throw.
        /// </summary>
        [Fact]
        public void ParseSenderReportWithOverstatedReportCountUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var rr = new ReceptionReportSample(5, 6, 7, 8, 9, 10, 11);
            var sr = new RTCPSenderReport(23, 1, 2, 3, 4, new List<ReceptionReportSample> { rr });
            var buffer = sr.GetBytes();

            // Patch the reception report count (low 5 bits of the first byte) to claim 3 reports
            // when only 1 is present in the packet.
            buffer[0] = (byte)((buffer[0] & 0xE0) | 0x03);

            var parsed = new RTCPSenderReport(buffer);

            Assert.Equal(23U, parsed.SSRC);
            Assert.Single(parsed.ReceptionReports);
            Assert.Equal(5U, parsed.ReceptionReports.First().SSRC);
        }
    }
}
