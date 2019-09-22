using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPEndPointTest
    {
        public SIPEndPointTest()
        { }

        [TestMethod]
        public void SampleTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
            Assert.IsTrue(true, "True was false.");
        }

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

            Assert.IsTrue(true, "True was false.");
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

            Assert.IsTrue(true, "True was false.");
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

            Assert.IsTrue(true, "True was false.");
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

            Assert.IsTrue(true, "True was false.");
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
    }
}
