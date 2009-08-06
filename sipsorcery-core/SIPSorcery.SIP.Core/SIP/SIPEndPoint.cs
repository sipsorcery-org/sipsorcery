using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    public class SIPEndPoint
    {
        private static ILog logger = AppState.logger;

        private static string m_transportParameterKey = SIPHeaderAncillary.SIP_HEADERANC_TRANSPORT;
        private static int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static int m_defaultSIPTLSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;

        public SIPProtocolsEnum SIPProtocol = SIPProtocolsEnum.udp;
        public IPEndPoint SocketEndPoint;

        private SIPEndPoint() { }

        public SIPEndPoint(IPEndPoint endPoint)
        {
            SocketEndPoint = endPoint;
        }

        public SIPEndPoint(SIPURI sipURI)
        {
            SIPProtocol = sipURI.Protocol;
            SocketEndPoint = IPSocket.ParseSocketString(sipURI.Host);
        }

        public SIPEndPoint(SIPProtocolsEnum protocol, IPEndPoint endPoint)
        {
            SIPProtocol = protocol;
            SocketEndPoint = endPoint;
        }

        public static SIPEndPoint ParseSIPEndPoint(string sipEndPointStr)
        {
            try {
                if (sipEndPointStr.IsNullOrBlank()) {
                    return null;
                }

                if(sipEndPointStr.StartsWith("udp") || sipEndPointStr.StartsWith("tcp") || sipEndPointStr.StartsWith("tls")){
                    return ParseSerialisedSIPEndPoint(sipEndPointStr);
                }

                string ipAddress = null;
                int port = 0;
                SIPProtocolsEnum protocol = SIPProtocolsEnum.udp;

                if (sipEndPointStr.StartsWith("sip:")) {
                    sipEndPointStr = sipEndPointStr.Substring(4);
                }
                else if (sipEndPointStr.StartsWith("sips:")) {
                    sipEndPointStr = sipEndPointStr.Substring(5);
                    protocol = SIPProtocolsEnum.tls;
                }

                int colonIndex = sipEndPointStr.IndexOf(':');
                int semiColonIndex = sipEndPointStr.IndexOf(';');
                if (colonIndex == -1 && semiColonIndex == -1) {
                    ipAddress = sipEndPointStr;
                }
                else if (colonIndex != -1 && semiColonIndex == -1) {
                    ipAddress = sipEndPointStr.Substring(0, colonIndex);
                    port = Convert.ToInt32(sipEndPointStr.Substring(colonIndex + 1));
                }
                else {
                    if (colonIndex != -1 && colonIndex < semiColonIndex) {
                        ipAddress = sipEndPointStr.Substring(0, colonIndex);
                        port = Convert.ToInt32(sipEndPointStr.Substring(colonIndex + 1, semiColonIndex - colonIndex - 1));
                    }
                    else {
                        ipAddress = sipEndPointStr.Substring(0, semiColonIndex);
                    }

                    if (protocol != SIPProtocolsEnum.tls) {
                        sipEndPointStr = sipEndPointStr.Substring(semiColonIndex + 1);
                        int transportIndex = sipEndPointStr.ToLower().IndexOf(m_transportParameterKey + "=");
                        if (transportIndex != -1) {
                            sipEndPointStr = sipEndPointStr.Substring(transportIndex + 10);
                            semiColonIndex = sipEndPointStr.IndexOf(';');
                            if (semiColonIndex != -1) {
                                protocol = SIPProtocolsType.GetProtocolType(sipEndPointStr.Substring(0, semiColonIndex));
                            }
                            else {
                                protocol = SIPProtocolsType.GetProtocolType(sipEndPointStr);
                            }
                        }
                    }
                }

                if(port == 0 ) {
                    port = (protocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort;
                }

                return new SIPEndPoint(protocol, new IPEndPoint(IPAddress.Parse(ipAddress), port));
            }
            catch (Exception excp) {
                logger.Error("Exception ParseSIPEndPoint. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Reverses ToString().
        /// </summary>
        /// <param name="serialisedSIPEndPoint">The serialised SIP end point MUST be in the form protocol:socket and protocol must
        /// be exactly 3 characters. Valid examples are udp:10.0.0.1:5060, invalid example is 10.0.0.1:5060.</param>
        private static SIPEndPoint ParseSerialisedSIPEndPoint(string serialisedSIPEndPoint) {
            return new SIPEndPoint(SIPProtocolsType.GetProtocolType(serialisedSIPEndPoint.Substring(0, 3)), IPSocket.ParseSocketString(serialisedSIPEndPoint.Substring(4)));
        }

        public override string ToString()
        {
            return SIPProtocol + ":" + SocketEndPoint;
        }

        public static bool AreEqual(SIPEndPoint endPoint1, SIPEndPoint endPoint2)
        {
            return endPoint1 == endPoint2;
        }

        public override bool Equals(object obj)
        {
            return AreEqual(this, (SIPEndPoint)obj);
        }

        public static bool operator ==(SIPEndPoint endPoint1, SIPEndPoint endPoint2) {
            if ((object)endPoint1 == null && (object)endPoint2 == null) {
                return true;
            }
            else if ((object)endPoint1 == null || (object)endPoint2 == null) {
                return false;
            }
            else if (endPoint1.ToString() != endPoint2.ToString()) {
                return false;
            }

            return true;
        }

        public static bool operator !=(SIPEndPoint endPoint1, SIPEndPoint endPoint2)
        {
            return !(endPoint1 == endPoint2);
        }

        #region Unit testing.

		#if UNITTEST

        [TestFixture]
        public class SIPEndPointUnitTest {
            [TestFixtureSetUp]
            public void Init() { }

            [TestFixtureTearDown]
            public void Dispose() { }

            [Test]
            public void SampleTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void AllFieldsParseTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipEndPointStr = "sips:10.0.0.100:5060;lr;transport=tcp;";
                SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

                Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

                Assert.IsTrue(sipEndPoint.SIPProtocol == SIPProtocolsEnum.tls, "The SIPEndPoint protocol was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void HostOnlyParseTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipEndPointStr = "10.0.0.100";
                SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

                Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

                Assert.IsTrue(sipEndPoint.SIPProtocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void HostAndSchemeParseTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipEndPointStr = "sip:10.0.0.100";
                SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

                Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

                Assert.IsTrue(sipEndPoint.SIPProtocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void HostAndPortParseTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipEndPointStr = "10.0.0.100:5065";
                SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

                Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

                Assert.IsTrue(sipEndPoint.SIPProtocol == SIPProtocolsEnum.udp, "The SIPEndPoint protocol was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Port == 5065, "The SIPEndPoint port was incorrectly parsed.");

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void HostAndTransportParseTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipEndPointStr = "10.0.0.100;transport=tcp";
                SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

                Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

                Assert.IsTrue(sipEndPoint.SIPProtocol == SIPProtocolsEnum.tcp, "The SIPEndPoint protocol was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Port == 5060, "The SIPEndPoint port was incorrectly parsed.");

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void SchemeHostPortParseTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipEndPointStr = "sips:10.0.0.100:5063";
                SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

                Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

                Assert.IsTrue(sipEndPoint.SIPProtocol == SIPProtocolsEnum.tls, "The SIPEndPoint protocol was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Port == 5063, "The SIPEndPoint port was incorrectly parsed.");

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void SchemeHostTransportParseTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipEndPointStr = "sip:10.0.0.100:5063;lr;tag=123;transport=tcp;tag2=abcd";
                SIPEndPoint sipEndPoint = SIPEndPoint.ParseSIPEndPoint(sipEndPointStr);

                Console.WriteLine("SIPEndPoint=" + sipEndPoint.ToString() + ".");

                Assert.IsTrue(sipEndPoint.SIPProtocol == SIPProtocolsEnum.tcp, "The SIPEndPoint protocol was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Address.ToString() == "10.0.0.100", "The SIPEndPoint IP address was incorrectly parsed.");
                Assert.IsTrue(sipEndPoint.SocketEndPoint.Port == 5063, "The SIPEndPoint port was incorrectly parsed.");

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void EqualityTestNoPostHostTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100");
                SIPEndPoint sipEP2 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100:5060");

                Assert.IsTrue(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
            }

            [Test]
            public void EqualityTestTLSHostTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("sips:10.0.0.100");
                SIPEndPoint sipEP2 = SIPEndPoint.ParseSIPEndPoint("10.0.0.100:5061;transport=tls");

                Assert.IsTrue(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
            }

            [Test]
            public void EqualityTestRouteTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPEndPoint sipEP1 = SIPEndPoint.ParseSIPEndPoint("sip:10.0.0.100;lr");
                SIPEndPoint sipEP2 = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Parse("10.0.0.100"), 5060));
                Assert.IsTrue(sipEP1 == sipEP2, "The SIP end points should have been detected as equal.");
            }
        }

        #endif
        
        #endregion
    }
}
