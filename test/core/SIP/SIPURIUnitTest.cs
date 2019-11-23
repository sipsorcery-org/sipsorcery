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
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPURIUnitTest
    {
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;
        private readonly ITestOutputHelper output;

        public SIPURIUnitTest(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void SampleTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
            Assert.True(true, "True was false.");
        }

        [Fact]
        public void ParseHostOnlyURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:sip.domain.com");

            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParseHostAndUserURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com");

            Assert.True(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParseWithParamURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com;param=1234");

            Assert.True(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Parameters.Get("PARAM") == "1234", "The SIP URI Parameter was not parsed correctly.");
            Assert.True(sipURI.ToString() == "sip:user@sip.domain.com;param=1234", "The SIP URI was not correctly to string'ed.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParseWithParamAndPortURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:1234@sip.domain.com:5060;TCID-0");

            Console.WriteLine("URI Name = " + sipURI.User);
            Console.WriteLine("URI Host = " + sipURI.Host);

            Assert.True(sipURI.User == "1234", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com:5060", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Parameters.Has("TCID-0"), "The SIP URI Parameter was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParseWithHeaderURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com?header=1234");

            Assert.True(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Headers.Get("header") == "1234", "The SIP URI Header was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void SpaceInHostNameURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:Blue Face");

            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "Blue Face", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ContactAsteriskURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("*");

            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "*", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void AreEqualNoParamsURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void AreEqualIPAddressNoParamsURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@192.168.1.101");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@192.168.1.101");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void AreEqualWithParamsURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void NotEqualWithParamsURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value2");

            Assert.NotEqual(sipURI1, sipURI2);

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void AreEqualWithHeadersURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2?header1=value1&header2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1?header2=value2&header1=value1");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly identified as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void NotEqualWithHeadersURIUnitTest()
        {
            output.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2?header1=value2&header2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1?header2=value2&header1=value1");

            output.WriteLine($"sipURI1: {sipURI1}");
            output.WriteLine($"sipURI2: {sipURI2}");

            Assert.NotEqual(sipURI1, sipURI2);

            output.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void UriWithParameterEqualityURIUnitTest()
        {
            output.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs did not have equal hash codes.");

            output.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void UriWithDifferentParamsEqualURIUnitTest()
        {
            output.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value2");

            Assert.NotEqual(sipURI1, sipURI2);

            output.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void UriWithSameParamsInDifferentOrderURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");

            Assert.Equal(sipURI1, sipURI2);

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void AreEqualNullURIsUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;
            SIPURI sipURI2 = null;

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void NotEqualOneNullURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");
            SIPURI sipURI2 = null;

            Assert.False(sipURI1 == sipURI2, "The SIP URIs were incorrectly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void AreEqualNullEqualsOverloadUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;

            Assert.True(sipURI1 == null, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void AreEqualNullNotEqualsOverloadUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;

            Assert.False(sipURI1 != null, "The SIP URIs were incorrectly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void UnknownSchemeUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.Throws< SIPValidationException>(() => SIPURI.ParseSIPURI("tel:1234565"));

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParamsInUserPortionURITest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:C=on;t=DLPAN@10.0.0.1:5060;lr");

            Assert.True("C=on;t=DLPAN" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.True("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void SwitchTagParameterUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:joebloggs@sip.mysipswitch.com;switchtag=119651");

            Assert.True("joebloggs" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.True("sip.mysipswitch.com" == sipURI.Host, "SIP host portion parsed incorrectly.");
            Assert.True("119651" == sipURI.Parameters.Get("switchtag"), "switchtag parameter parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void LongUserUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:EhZgKgLM9CwGqYDAECqDpL5MNrM_sKN5NurN5q_pssAk4oxhjKEMT4@10.0.0.1:5060");

            Assert.True("EhZgKgLM9CwGqYDAECqDpL5MNrM_sKN5NurN5q_pssAk4oxhjKEMT4" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.True("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParsePartialURINoSchemeUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip.domain.com");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Protocol == SIPProtocolsEnum.udp, "The SIP URI protocol was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParsePartialURISIPSSchemeUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sips:sip.domain.com:1234");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sips, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com:1234", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParsePartialURIWithUserUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip:joe.bloggs@sip.domain.com:1234;transport=tcp");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.User == "joe.bloggs", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com:1234", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Protocol == SIPProtocolsEnum.tcp, "The SIP URI protocol was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        /// <summary>
        /// Got a URI like this from Zoiper.
        /// </summary>
        [Fact]
        public void ParseHoHostUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURI("sip:;transport=UDP"));

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void UDPProtocolToStringTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = new SIPURI(SIPSchemesEnum.sip, SIPEndPoint.ParseSIPEndPoint("127.0.0.1"));
            Console.WriteLine(sipURI.ToString());
            Assert.True(sipURI.ToString() == "sip:127.0.0.1:5060", "The SIP URI was not ToString'ed correctly.");
            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParseUDPProtocolToStringTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("127.0.0.1");
            Console.WriteLine(sipURI.ToString());
            Assert.True(sipURI.ToString() == "sip:127.0.0.1", "The SIP URI was not ToString'ed correctly.");
            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParseBigURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("TRUNKa1d2ce524d44cd54f39ac78bcdba85c7@65.98.14.50:5069");
            Console.WriteLine(sipURI.ToString());
            Assert.True(sipURI.ToString() == "sip:TRUNKa1d2ce524d44cd54f39ac78bcdba85c7@65.98.14.50:5069", "The SIP URI was not ToString'ed correctly.");
            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void ParseMalformedContactUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURIRelaxed("sip:twolmsted@24.183.120.253, sip:5060"));
            
            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void NoPortIPv4CanonicalAddressToStringTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:127.0.0.1");
            logger.LogDebug($"SIP URI {sipURI.ToString()}");
            logger.LogDebug($"Canonical address {sipURI.CanonicalAddress}");

            Assert.True(sipURI.ToString() == "sip:127.0.0.1", "The SIP URI was not ToString'ed correctly.");
            Assert.True(sipURI.CanonicalAddress == "sip:127.0.0.1:5060", "The SIP URI canonical address was not correct.");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with an IPv6 address is correctly parsed.
        /// </summary>
        [Fact]
        public void ParseIPv6UnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:[::1]");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.Host == "[::1]", "The SIP URI host was not parsed correctly.");
            Assert.True(sipURI.ToSIPEndPoint() == new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.IPv6Loopback, 5060, null, null), "The SIP URI end point details were not parsed correctly.");

            logger.LogDebug($"SIP URI {sipURI.ToString()}");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with an IPv6 address and an explicit port is correctly parsed.
        /// </summary>
        [Fact]
        public void ParseIPv6WithExplicitPortUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:[::1]:6060");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.Host == "[::1]:6060", "The SIP URI host was not parsed correctly.");
            Assert.True(sipURI.ToSIPEndPoint() == new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.IPv6Loopback, 6060, null, null), "The SIP URI end point details were not parsed correctly.");

            logger.LogDebug($"SIP URI {sipURI.ToString()}");

            logger.LogDebug("-----------------------------------------");
        }
    }
}
