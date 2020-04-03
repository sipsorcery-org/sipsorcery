//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPEndPointTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPEndPointTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void AllFieldsParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sips:10.0.0.100:5060;lr;transport=tcp;";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.tls, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

            Assert.True(true, "True was false.");
        }

        [Fact]
        public void HostOnlyParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "10.0.0.100";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");
        }

        [Fact]
        public void HostAndSchemeParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sip:10.0.0.100";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

            Assert.True(true, "True was false.");
        }

        [Fact]
        public void HostAndPortParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "10.0.0.100:5065";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5065, "The SIPEndPoint port was incorrectly parsed.");
        }

        [Fact]
        public void HostAndTransportParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "10.0.0.100;transport=tcp";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.tcp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

            Assert.True(true, "True was false.");
        }

        [Fact]
        public void SchemeHostPortParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sips:10.0.0.100:5063";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.tls, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5063, "The SIPEndPoint port was incorrectly parsed.");
        }

        [Fact]
        public void SchemeHostTransportParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sip:10.0.0.100:5063;lr;tag=123;transport=tcp;tag2=abcd";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.tcp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5063, "The SIPEndPoint port was incorrectly parsed.");
        }

        [Fact]
        public void EqualityTestNoPostHostTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100");
            SIPEndPoint sipEP2 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100:5060");

            Assert.True(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
        }

        [Fact]
        public void EqualityTestTLSHostTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("sips:10.0.0.100");
            SIPEndPoint sipEP2 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100:5061;transport=tls");

            Assert.True(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
        }

        [Fact]
        public void EqualityTestRouteTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("sip:10.0.0.100;lr");
            SIPEndPoint sipEP2 = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Parse("10.0.0.100"), 5060));
            Assert.True(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
        }

        /// <summary>
        /// Tests that a SIP end point with an IPv6 loopback address gets represented as a string correctly.
        /// </summary>
        [Fact]
        public void IPv6LoopbackToStringTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint sipEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.IPv6Loopback, 0));

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.ToString() == "udp:[::1]:5060", "The SIP end point string representation was not correct.");
        }

        /// <summary>
        /// Tests that a SIP end point with an IPv6 loopback address gets parsed correctly.
        /// </summary>
        [Fact]
        public void IPv6LoopbackAndSchemeParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "udp:[::1]";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point with an IPv6 loopback address and port gets parsed correctly.
        /// </summary>
        [Fact]
        public void IPv6LoopbackAndPortParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "tcp:[::1]:6060";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.tcp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6, "The SIPEndPoint IP address family was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 6060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point with an IPv6 loopback address and scheme gets parsed correctly.
        /// </summary>
        [Fact]
        public void IPv6LoopbackWithScehemeParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sip:[::1]:6060";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6, "The SIPEndPoint IP address family was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 6060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point for a web socket with an IPv6 loopback address and port gets parsed correctly.
        /// </summary>
        [Fact]
        public void WebSocketLoopbackAndPortParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "ws:[::1]:6060";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.ws, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6, "The SIPEndPoint IP address family was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 6060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point for a secure web socket gets parsed correctly.
        /// </summary>
        [Fact]
        public void SecureWebSocketLoopbackAndPortParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "wss:[fe80::54a9:d238:b2ee:ceb]:7060";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.wss, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "fe80::54a9:d238:b2ee:ceb", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6, "The SIPEndPoint IP address family was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 7060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point an IPV6 address and a connection id gets parsed correctly.
        /// </summary>
        [Fact]
        public void IPv6WithConnectionIDParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "udp:[::1];xid=1234567";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");
            Assert.True(sipEndPoint.ConnectionID == "1234567", "The SIPEndPoint connection ID was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point an IPV6 address, a connection id and a channel id gets parsed correctly.
        /// </summary>
        [Fact]
        public void IPv6WithConnectionAndChannelIDParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "udp:[::1];cid=123;xid=1234567";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.True(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.True(sipEndPoint.Address.ToString() == "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.True(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");
            Assert.True(sipEndPoint.ChannelID == "123", "The SIPEndPoint channel ID was incorrectly parsed.");
            Assert.True(sipEndPoint.ConnectionID == "1234567", "The SIPEndPoint connection ID was incorrectly parsed.");
        }
    }
}
