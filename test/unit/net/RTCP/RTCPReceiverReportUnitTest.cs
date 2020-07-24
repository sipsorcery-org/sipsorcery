//-----------------------------------------------------------------------------
// Filename: RTCPReceiverReportUnitTest.cs
//
// Description: Unit tests for the RTCPReceiverReport class.

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
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPReceiverReportUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPReceiverReportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a RTCPSenderReport payload can be correctly serialised and 
        /// deserialised.
        /// </summary>
        [Fact]
        public void RoundtripRTCPReceiverResportUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 1;

            uint rrSsrc = 5;
            byte fractionLost = 6;
            int packetsLost = 7;
            uint highestSeqNum = 8;
            uint jitter = 9;
            uint lastSRTimestamp = 10;
            uint delaySinceLastSR = 11;

            var rr = new ReceptionReportSample(rrSsrc, fractionLost, packetsLost, highestSeqNum, jitter, lastSRTimestamp, delaySinceLastSR);

            var receiverReport = new RTCPReceiverReport(ssrc, new List<ReceptionReportSample> { rr });
            byte[] buffer = receiverReport.GetBytes();

            RTCPReceiverReport parsedRR = new RTCPReceiverReport(buffer);

            Assert.Equal(ssrc, parsedRR.SSRC);

            Assert.Equal(rrSsrc, parsedRR.ReceptionReports.First().SSRC);
            Assert.Equal(fractionLost, parsedRR.ReceptionReports.First().FractionLost);
            Assert.Equal(packetsLost, parsedRR.ReceptionReports.First().PacketsLost);
            Assert.Equal(highestSeqNum, parsedRR.ReceptionReports.First().ExtendedHighestSequenceNumber);
            Assert.Equal(jitter, parsedRR.ReceptionReports.First().Jitter);
            Assert.Equal(lastSRTimestamp, parsedRR.ReceptionReports.First().LastSenderReportTimestamp);
            Assert.Equal(delaySinceLastSR, parsedRR.ReceptionReports.First().DelaySinceLastSenderReport);
        }

        /// <summary>
        /// Tests parsing a receiver report with 0 reception reports.
        /// </summary>
        [Fact]
        public void ParseEmtpyReceiverReportUnitTest()
        {
            var rrBytes = new byte[] { 0x80, 0xc9, 0x00, 0x01, 0x03, 0x86, 0x4a, 0xb9 };

            var receiverReport = new RTCPReceiverReport(rrBytes);

            Assert.True(receiverReport.ReceptionReports.Count == 0);
            Assert.Equal((uint)59132601, receiverReport.SSRC);
        }

        /// <summary>
        /// Tests parsing a Receiver Report from a byte array works correctly.
        /// </summary>
        [Fact]
        public void ParseReceiverReportUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var buffer = TypeExtensions.ParseHexStr("81C9000700000001679915EA000000000000212E000004B40000000000000000");

            RTCPReceiverReport rr = new RTCPReceiverReport(buffer);

            Assert.NotNull(rr);
            Assert.Equal(1738085866U, rr.ReceptionReports.First().SSRC);
        }

        /// <summary>
        /// Tests parsing a Receiver Report received from the Chrome browser as part of a WebRTC session works correctly.
        /// </summary>
        [Fact]
        public void ParseReceiverReportChromeUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var buffer = TypeExtensions.ParseHexStr("81C90007FA17FA1709CF4FFA000000000000496C00000021000000000000000080000003315A25AFFAF8545434C7");

            RTCPReceiverReport rr = new RTCPReceiverReport(buffer);

            Assert.NotNull(rr);
            Assert.Equal(4195875351U, rr.SSRC);
        }
    }
}
