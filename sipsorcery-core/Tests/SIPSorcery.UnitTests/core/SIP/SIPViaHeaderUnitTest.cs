using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPViaHeaderUnitTest
    {
        [TestMethod]
        public void ParseXTenViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            Console.WriteLine("Version = " + sipViaHeaders[0].Version + ".");
            Console.WriteLine("Transport = " + sipViaHeaders[0].Transport + ".");
            Console.WriteLine("Contact = " + sipViaHeaders[0].ContactAddress + ".");
            Console.WriteLine("received = " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
            Console.WriteLine("rport = " + sipViaHeaders[0].ReceivedFromPort + ".");
            Console.WriteLine("branch = " + sipViaHeaders[0].Branch + ".");
            Console.WriteLine("Parsed header = " + sipViaHeaders[0].ToString());

            Assert.IsTrue("SIP/2.0" == sipViaHeaders[0].Version, "The Via header Version was not correctly parsed, " + sipViaHeaders[0].Version + ".");
            Assert.IsTrue(SIPProtocolsEnum.udp == sipViaHeaders[0].Transport, "The Via header Transport was not correctly parsed, " + sipViaHeaders[0].Transport + ".");
            Assert.IsTrue("192.168.1.2:5065" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.IsTrue(null == sipViaHeaders[0].ReceivedFromIPAddress, "The Via header received field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
            Assert.IsTrue(0 == sipViaHeaders[0].ReceivedFromPort, "The Via header rport field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromPort + ".");
            Assert.IsTrue("z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeaders[0].Branch, "The Via header branch was not correctly parsed, " + sipViaHeaders[0].Branch + ".");

            //Assert.IsTrue("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseReceivedFromIPViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;received=88.99.88.99;rport=10060;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            Console.WriteLine("Version = " + sipViaHeaders[0].Version + ".");
            Console.WriteLine("Transport = " + sipViaHeaders[0].Transport + ".");
            Console.WriteLine("Contact = " + sipViaHeaders[0].ContactAddress + ".");
            Console.WriteLine("received = " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
            Console.WriteLine("rport = " + sipViaHeaders[0].ReceivedFromPort + ".");
            Console.WriteLine("branch = " + sipViaHeaders[0].Branch + ".");
            Console.WriteLine("Parsed header = " + sipViaHeaders[0].ToString());

            Assert.IsTrue("SIP/2.0" == sipViaHeaders[0].Version, "The Via header Version was not correctly parsed, " + sipViaHeaders[0].Version + ".");
            Assert.IsTrue(SIPProtocolsEnum.udp == sipViaHeaders[0].Transport, "The Via header Transport was not correctly parsed, " + sipViaHeaders[0].Transport + "."); Assert.IsTrue("192.168.1.2:5065" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.IsTrue("88.99.88.99" == sipViaHeaders[0].ReceivedFromIPAddress, "The Via header received field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
            Assert.IsTrue(10060 == sipViaHeaders[0].ReceivedFromPort, "The Via header rport field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromPort + ".");
            Assert.IsTrue("z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeaders[0].Branch, "The Via header branch was not correctly parsed, " + sipViaHeaders[0].Branch + ".");

            //Assert.IsTrue("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseNoPortViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noPortViaHeader = "SIP/2.0/UDP 192.168.1.1;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

            Console.WriteLine("Via Header Contact Address = " + sipViaHeaders[0].ContactAddress);
            Console.WriteLine("Via Header Received From Address = " + sipViaHeaders[0].ReceivedFromAddress);

            Assert.IsTrue(sipViaHeaders[0].Host == "192.168.1.1", "The Via header host was not parsed correctly");
            Assert.IsTrue("192.168.1.1" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseNoSemiColonViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

            Assert.IsTrue(sipViaHeaders.Length == 1, "The Via header list should have had a single entry.");
            Assert.IsTrue(sipViaHeaders[0].ContactAddress == "192.168.1.1:1234", "The Via header contact address was parsed incorrectly.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        [ExpectedException(typeof(SIPValidationException))]
        public void ParseNoContactViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noContactViaHeader = "SIP/2.0/UDP";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noContactViaHeader);

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseNoSemiButHasBranchColonViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

            Assert.IsTrue(sipViaHeaders[0].Host == "192.168.1.1", "The Via header host was not parsed correctly");
            Assert.IsTrue("192.168.1.1:1234" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.IsTrue(sipViaHeaders[0].Branch == "z9hG4bKFBB7EAC06934405182D13950BD51F001", "The Via header branch was not parsed correctly.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseNoBranchViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234;rport";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

            //Assert.IsNull(sipViaHeaders, "The Via header list should have been empty.");
            Assert.IsTrue(sipViaHeaders[0].ContactAddress == "192.168.1.1:1234", "The Via header contact was not correctly parsed.");
            Assert.IsNull(sipViaHeaders[0].Branch, "The Via branch should have been null.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        [ExpectedException(typeof(SIPValidationException))]
        public void ParseBadAastraViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234port;branch=213123";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void MaintainUnknownHeaderViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;received=88.99.88.99;unknown=12234;unknown2;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001;rport";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            Console.WriteLine("Via Header=" + sipViaHeaders[0].ToString() + ".");

            //Assert.IsTrue(Regex.Match(sipViaHeaders[0].ToString(), "rport").Success, "The Via header did not maintain the unknown rport parameter.");
            Assert.IsTrue(Regex.Match(sipViaHeaders[0].ToString(), "unknown=12234").Success, "The Via header did not maintain the unrecognised unknown parameter.");
            Assert.IsTrue(Regex.Match(sipViaHeaders[0].ToString(), "unknown2").Success, "The Via header did not maintain the unrecognised unknown2 parameter.");

            //Assert.IsTrue("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void GetIPEndPointViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            Assert.IsTrue(sipViaHeaders[0].ContactAddress == "192.168.1.2:5065", "Incorrect endpoint address for Via header.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void CreateNewViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPViaHeader viaHeader = new SIPViaHeader("192.168.1.2", 5063, "abcdefgh");

            Assert.IsTrue(viaHeader.Host == "192.168.1.2", "Incorrect Host for Via header.");
            Assert.IsTrue(viaHeader.Port == 5063, "Incorrect Port for Via header.");
            Assert.IsTrue(viaHeader.Branch == "abcdefgh", "Incorrect Branch for Via header.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseMultiViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noPortViaHeader = "SIP/2.0/UDP 192.168.1.1:5060;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001, SIP/2.0/UDP 192.168.0.1:5061;branch=z9hG4bKFBB7EAC06";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

            Assert.IsTrue(sipViaHeaders[0].Host == "192.168.1.1", "The first Via header host was not parsed correctly");
            Assert.IsTrue("192.168.1.1:5060" == sipViaHeaders[0].ContactAddress, "The first Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.IsTrue(sipViaHeaders[1].Host == "192.168.0.1", "The second Via header host was not parsed correctly");
            Assert.IsTrue("192.168.0.1:5061" == sipViaHeaders[1].ContactAddress, "The second Via header contact address was not correctly parsed, " + sipViaHeaders[1].ContactAddress + ".");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseMultiViaHeaderTest2()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noPortViaHeader = "SIP/2.0/UDP 194.213.29.100:5060;branch=z9hG4bK5feb18267ce40fb05969b4ba843681dbfc9ffcff, SIP/2.0/UDP 194.213.29.54:5061;branch=z9hG4bK52b6a8b7";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

            Assert.IsTrue(sipViaHeaders[0].Host == "194.213.29.100", "The first Via header host was not parsed correctly");
            Assert.IsTrue("194.213.29.100:5060" == sipViaHeaders[0].ContactAddress, "The first Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.IsTrue(sipViaHeaders[1].Host == "194.213.29.54", "The second Via header host was not parsed correctly");
            Assert.IsTrue("194.213.29.54:5061" == sipViaHeaders[1].ContactAddress, "The second Via header contact address was not correctly parsed, " + sipViaHeaders[1].ContactAddress + ".");

            Console.WriteLine("---------------------------------------------------");
        }
    }
}
