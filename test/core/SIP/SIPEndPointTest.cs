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

using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.UnitTests
{
    [TestClass]
    public class SIPEndPointTest
    {
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        [TestMethod]
        public void AllFieldsParseTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sips:10.0.0.100:5060;lr;transport=tcp;";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.tls, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

            Assert.IsTrue(true, "True was false.");
        }

        [TestMethod]
        public void HostOnlyParseTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "10.0.0.100";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");
        }

        [TestMethod]
        public void HostAndSchemeParseTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sip:10.0.0.100";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

            Assert.IsTrue(true, "True was false.");
        }

        [TestMethod]
        public void HostAndPortParseTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "10.0.0.100:5065";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5065, "The SIPEndPoint port was incorrectly parsed.");
        }

        [TestMethod]
        public void HostAndTransportParseTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "10.0.0.100;transport=tcp";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.tcp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

            Assert.IsTrue(true, "True was false.");
        }

        [TestMethod]
        public void SchemeHostPortParseTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sips:10.0.0.100:5063";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.tls, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5063, "The SIPEndPoint port was incorrectly parsed.");
        }

        [TestMethod]
        public void SchemeHostTransportParseTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sip:10.0.0.100:5063;lr;tag=123;transport=tcp;tag2=abcd";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.tcp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5063, "The SIPEndPoint port was incorrectly parsed.");
        }

        [TestMethod]
        public void EqualityTestNoPostHostTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100");
            SIPEndPoint sipEP2 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100:5060");

            Assert.IsTrue(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
        }

        [TestMethod]
        public void EqualityTestTLSHostTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("sips:10.0.0.100");
            SIPEndPoint sipEP2 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100:5061;transport=tls");

            Assert.IsTrue(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
        }

        [TestMethod]
        public void EqualityTestRouteTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("sip:10.0.0.100;lr");
            SIPEndPoint sipEP2 = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Parse("10.0.0.100"), 5060));
            Assert.IsTrue(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
        }

        /// <summary>
        /// Tests that a SIP end point with an IPv6 loopback address gets represented as astring correctly.
        /// </summary>
        [TestMethod]
        public void IPv6LoopbackToStringTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint sipEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.IPv6Loopback, 0));

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.AreEqual(sipEndPoint.ToString(), "udp:[::1]:5060", "The SIP end point string representation was not correct.");
        }

        /// <summary>
        /// Tests that a SIP end point with an IPv6 loopback address gets parsed correctly.
        /// </summary>
        [TestMethod]
        public void IPv6LoopbackAndSchemeParseTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "udp:[::1]";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point with an IPv6 loopback address and port gets parsed correctly.
        /// </summary>
        [TestMethod]
        public void IPv6LoopbackAndPortParseTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "tcp:[::1]:6060";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.AreEqual(sipEndPoint.Protocol, SIPProtocolsEnum.tcp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Address.ToString(), "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Address.AddressFamily, System.Net.Sockets.AddressFamily.InterNetworkV6, "The SIPEndPoint IP address family was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Port, 6060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point with an IPv6 loopback address and scheme gets parsed correctly.
        /// </summary>
        [TestMethod]
        public void IPv6LoopbackWithScehemeParseTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "sip:[::1]:6060";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.AreEqual(sipEndPoint.Protocol, SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Address.ToString(), "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Address.AddressFamily, System.Net.Sockets.AddressFamily.InterNetworkV6, "The SIPEndPoint IP address family was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Port, 6060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point for a web socket with an IPv6 loopback address and port gets parsed correctly.
        /// </summary>
        [TestMethod]
        public void WebSocketLoopbackAndPortParseTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "ws:[::1]:6060";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.AreEqual(sipEndPoint.Protocol, SIPProtocolsEnum.ws, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Address.ToString(), "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Address.AddressFamily, System.Net.Sockets.AddressFamily.InterNetworkV6, "The SIPEndPoint IP address family was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Port, 6060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point for a secure web socket gets parsed correctly.
        /// </summary>
        [TestMethod]
        public void SecureWebSocketLoopbackAndPortParseTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "wss:[fe80::54a9:d238:b2ee:ceb]:7060";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.AreEqual(sipEndPoint.Protocol, SIPProtocolsEnum.wss, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Address.ToString(), "fe80::54a9:d238:b2ee:ceb", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Address.AddressFamily, System.Net.Sockets.AddressFamily.InterNetworkV6, "The SIPEndPoint IP address family was incorrectly parsed.");
            Assert.AreEqual(sipEndPoint.Port, 7060, "The SIPEndPoint port was incorrectly parsed.");
        }

        /// <summary>
        /// Tests that a SIP end point an IPV6 address and a connection id gets parsed correctly.
        /// </summary>
        [TestMethod]
        public void IPv6WithConnectionIDParseTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipEndPointStr = "udp:[::1];connid=abcd1234";
            SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

            logger.LogDebug("SIPEndPoint=" + sipEndPoint.ToString() + ".");

            Assert.IsTrue(sipEndPoint.Protocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Address.ToString() == "::1", "The SIPEndPoint IP address was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");
            Assert.IsTrue(sipEndPoint.ConnectionID == "abcd1234", "The SIPEndPoint connection ID was incorrectly parsed.");
        }
    }
}
