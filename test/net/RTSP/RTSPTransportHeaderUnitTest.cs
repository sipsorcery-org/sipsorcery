//-----------------------------------------------------------------------------
// Filename: RTSPTransportHeaderUnitTest.cs
//
// Description: Unit tests for the RTSPTransportHeader class.
//
// Author(s):
// Aaron Clauson
//// History:
// 21 Jan 2014	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTSPTransportHeaderUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTSPTransportHeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a typical RTSP transport header can be correctly parsed.
        /// </summary>
        [Fact]
        public void RTSPTransportHeaderParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string header = "RTP/AVP;unicast;destination=192.168.33.170;source=192.168.33.103;client_port=61132-61133;server_port=6970-6971";

            var transportHeader = RTSPTransportHeader.Parse(header);

            Assert.Equal("RTP/AVP", transportHeader.TransportSpecifier);
            Assert.Equal("unicast", transportHeader.BroadcastType);
            Assert.Equal("192.168.33.170", transportHeader.Destination);
            Assert.Equal("192.168.33.103", transportHeader.Source);
            Assert.Equal("61132-61133", transportHeader.ClientRTPPortRange);
            Assert.Equal("6970-6971", transportHeader.ServerRTPPortRange);
        }

        /// <summary>
        /// Tests that a typical RTSP transport header can be formatted to a header string..
        /// </summary>
        [Fact]
        public void RTSPTransportHeaderToStringTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var transportHeader = new RTSPTransportHeader() { Destination = "192.168.33.170", Source = "192.168.33.103", ClientRTPPortRange = "61132-61133", ServerRTPPortRange = "6970-6971" };

            Assert.Equal("RTP/AVP/UDP", transportHeader.TransportSpecifier);
            Assert.Equal("unicast", transportHeader.BroadcastType);
            Assert.Equal("192.168.33.170", transportHeader.Destination);
            Assert.Equal("192.168.33.103", transportHeader.Source);
            Assert.Equal("61132-61133", transportHeader.ClientRTPPortRange);
            Assert.Equal("6970-6971", transportHeader.ServerRTPPortRange);

            Assert.Equal("RTP/AVP/UDP;unicast;destination=192.168.33.170;source=192.168.33.103;client_port=61132-61133;server_port=6970-6971", transportHeader.ToString());
        }
    }
}
