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

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPViaHeaderUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPViaHeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ParseXTenViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            logger.LogDebug("Version = " + sipViaHeaders[0].Version + ".");
            logger.LogDebug("Transport = " + sipViaHeaders[0].Transport + ".");
            logger.LogDebug("Contact = " + sipViaHeaders[0].ContactAddress + ".");
            logger.LogDebug("received = " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
            logger.LogDebug("rport = " + sipViaHeaders[0].ReceivedFromPort + ".");
            logger.LogDebug("branch = " + sipViaHeaders[0].Branch + ".");
            logger.LogDebug("Parsed header = " + sipViaHeaders[0].ToString());

            Assert.True("SIP/2.0" == sipViaHeaders[0].Version, "The Via header Version was not correctly parsed, " + sipViaHeaders[0].Version + ".");
            Assert.True(SIPProtocolsEnum.udp == sipViaHeaders[0].Transport, "The Via header Transport was not correctly parsed, " + sipViaHeaders[0].Transport + ".");
            Assert.True("192.168.1.2:5065" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.True(null == sipViaHeaders[0].ReceivedFromIPAddress, "The Via header received field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
            Assert.True(0 == sipViaHeaders[0].ReceivedFromPort, "The Via header rport field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromPort + ".");
            Assert.True("z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeaders[0].Branch, "The Via header branch was not correctly parsed, " + sipViaHeaders[0].Branch + ".");

            //Assert.True("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseReceivedFromIPViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;received=88.99.88.99;rport=10060;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            logger.LogDebug("Version = " + sipViaHeaders[0].Version + ".");
            logger.LogDebug("Transport = " + sipViaHeaders[0].Transport + ".");
            logger.LogDebug("Contact = " + sipViaHeaders[0].ContactAddress + ".");
            logger.LogDebug("received = " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
            logger.LogDebug("rport = " + sipViaHeaders[0].ReceivedFromPort + ".");
            logger.LogDebug("branch = " + sipViaHeaders[0].Branch + ".");
            logger.LogDebug("Parsed header = " + sipViaHeaders[0].ToString());

            Assert.True("SIP/2.0" == sipViaHeaders[0].Version, "The Via header Version was not correctly parsed, " + sipViaHeaders[0].Version + ".");
            Assert.True(SIPProtocolsEnum.udp == sipViaHeaders[0].Transport, "The Via header Transport was not correctly parsed, " + sipViaHeaders[0].Transport + ".");
            Assert.True("192.168.1.2:5065" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.True("88.99.88.99" == sipViaHeaders[0].ReceivedFromIPAddress, "The Via header received field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
            Assert.True(10060 == sipViaHeaders[0].ReceivedFromPort, "The Via header rport field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromPort + ".");
            Assert.True("z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeaders[0].Branch, "The Via header branch was not correctly parsed, " + sipViaHeaders[0].Branch + ".");

            //Assert.True("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseNoPortViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noPortViaHeader = "SIP/2.0/UDP 192.168.1.1;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

            logger.LogDebug("Via Header Contact Address = " + sipViaHeaders[0].ContactAddress);
            logger.LogDebug("Via Header Received From Address = " + sipViaHeaders[0].ReceivedFromAddress);

            Assert.True(sipViaHeaders[0].Host == "192.168.1.1", "The Via header host was not parsed correctly");
            Assert.True("192.168.1.1" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseNoSemiColonViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

            Assert.True(sipViaHeaders.Length == 1, "The Via header list should have had a single entry.");
            Assert.True(sipViaHeaders[0].ContactAddress == "192.168.1.1:1234", "The Via header contact address was parsed incorrectly.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseNoContactViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noContactViaHeader = "SIP/2.0/UDP";

            Assert.Throws<SIPValidationException>(() => SIPViaHeader.ParseSIPViaHeader(noContactViaHeader));

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseNoSemiButHasBranchColonViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

            Assert.True(sipViaHeaders[0].Host == "192.168.1.1", "The Via header host was not parsed correctly");
            Assert.True("192.168.1.1:1234" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.True(sipViaHeaders[0].Branch == "z9hG4bKFBB7EAC06934405182D13950BD51F001", "The Via header branch was not parsed correctly.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseNoBranchViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234;rport";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

            //Assert.IsNull(sipViaHeaders, "The Via header list should have been empty.");
            Assert.True(sipViaHeaders[0].ContactAddress == "192.168.1.1:1234", "The Via header contact was not correctly parsed.");
            Assert.True(sipViaHeaders[0].Branch == null, "The Via branch should have been null.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseBadAastraViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234port;branch=213123";

            Assert.Throws<SIPValidationException>(() => SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader));

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void MaintainUnknownHeaderViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;received=88.99.88.99;unknown=12234;unknown2;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001;rport";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            logger.LogDebug("Via Header=" + sipViaHeaders[0].ToString() + ".");

            //Assert.True(Regex.Match(sipViaHeaders[0].ToString(), "rport").Success, "The Via header did not maintain the unknown rport parameter.");
            Assert.True(Regex.Match(sipViaHeaders[0].ToString(), "unknown=12234").Success, "The Via header did not maintain the unrecognised unknown parameter.");
            Assert.True(Regex.Match(sipViaHeaders[0].ToString(), "unknown2").Success, "The Via header did not maintain the unrecognised unknown2 parameter.");

            //Assert.True("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void GetIPEndPointViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            Assert.True(sipViaHeaders[0].ContactAddress == "192.168.1.2:5065", "Incorrect endpoint address for Via header.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void CreateNewViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPViaHeader viaHeader = new SIPViaHeader("192.168.1.2", 5063, "abcdefgh");

            Assert.True(viaHeader.Host == "192.168.1.2", "Incorrect Host for Via header.");
            Assert.True(viaHeader.Port == 5063, "Incorrect Port for Via header.");
            Assert.True(viaHeader.Branch == "abcdefgh", "Incorrect Branch for Via header.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseMultiViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noPortViaHeader = "SIP/2.0/UDP 192.168.1.1:5060;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001, SIP/2.0/UDP 192.168.0.1:5061;branch=z9hG4bKFBB7EAC06";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

            Assert.True(sipViaHeaders[0].Host == "192.168.1.1", "The first Via header host was not parsed correctly");
            Assert.True("192.168.1.1:5060" == sipViaHeaders[0].ContactAddress, "The first Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.True(sipViaHeaders[1].Host == "192.168.0.1", "The second Via header host was not parsed correctly");
            Assert.True("192.168.0.1:5061" == sipViaHeaders[1].ContactAddress, "The second Via header contact address was not correctly parsed, " + sipViaHeaders[1].ContactAddress + ".");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseMultiViaHeaderTest2()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noPortViaHeader = "SIP/2.0/UDP 194.213.29.100:5060;branch=z9hG4bK5feb18267ce40fb05969b4ba843681dbfc9ffcff, SIP/2.0/UDP 127.0.0.1:5061;branch=z9hG4bK52b6a8b7";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

            Assert.True(sipViaHeaders[0].Host == "194.213.29.100", "The first Via header host was not parsed correctly");
            Assert.True("194.213.29.100:5060" == sipViaHeaders[0].ContactAddress, "The first Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
            Assert.True(sipViaHeaders[1].Host == "127.0.0.1", "The second Via header host was not parsed correctly");
            Assert.True("127.0.0.1:5061" == sipViaHeaders[1].ContactAddress, "The second Via header contact address was not correctly parsed, " + sipViaHeaders[1].ContactAddress + ".");

            logger.LogDebug("---------------------------------------------------");
        }
    }
}
