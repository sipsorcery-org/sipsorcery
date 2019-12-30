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

            var sr = new RTCPReceiverReport(ssrc, new List<ReceptionReportSample> { rr });
            byte[] buffer = sr.GetBytes();

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
    }
}
