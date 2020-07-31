//-----------------------------------------------------------------------------
// Filename: RTCPSDesReportUnitTest.cs
//
// Description: Unit tests for the RTCPSDesReport class.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPSDesReportUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPSDesReportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a RTCP SDES report payload can be correctly serialised and 
        /// deserialised.
        /// </summary>
        [Fact]
        public void RoundtripRTCPSDesReportUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 8;
            string cname = "abc";

            RTCPSDesReport sdesReport = new RTCPSDesReport(ssrc, cname);
            byte[] buffer = sdesReport.GetBytes();

            RTCPSDesReport parsedReport = new RTCPSDesReport(buffer);

            Assert.Equal(0x00, buffer[buffer.Length - 1]); // Items must be terminated with 0x00.
            Assert.Equal(ssrc, parsedReport.SSRC);
            Assert.Equal(cname, parsedReport.CNAME);
        }

        /// <summary>
        /// Tests that a RTCP SDES report payload can be correctly serialised and 
        /// deserialised when the data values do not line up with a 4 byte boundary.
        /// </summary>
        [Fact]
        public void RoundtripRTCPSDesReportNotOnBoundaryUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 8;
            string cname = "ab1234";

            RTCPSDesReport sdesReport = new RTCPSDesReport(ssrc, cname);
            byte[] buffer = sdesReport.GetBytes();

            RTCPSDesReport parsedReport = new RTCPSDesReport(buffer);

            Assert.Equal(0x00, buffer[buffer.Length - 1]); // Items must be terminated with 0x00.
            Assert.Equal(ssrc, parsedReport.SSRC);
            Assert.Equal(cname, parsedReport.CNAME);
        }

        /// <summary>
        /// Tests that a RTCP SDES report payload can be correctly serialised and 
        /// deserialised when the data values line up with a 4 byte boundary.
        /// </summary>
        [Fact]
        public void RoundtripRTCPSDesReportOnBoundaryUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 8;
            string cname = "ab123"; // 5 bytes + 1 byte for the item null termination.

            RTCPSDesReport sdesReport = new RTCPSDesReport(ssrc, cname);
            byte[] buffer = sdesReport.GetBytes();

            RTCPSDesReport parsedReport = new RTCPSDesReport(buffer);

            Assert.Equal(0x00, buffer[buffer.Length - 1]); // Items must be terminated with 0x00.
            Assert.Equal(ssrc, parsedReport.SSRC);
            Assert.Equal(cname, parsedReport.CNAME);
        }
    }
}
