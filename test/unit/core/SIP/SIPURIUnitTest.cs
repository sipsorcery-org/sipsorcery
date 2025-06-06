﻿//-----------------------------------------------------------------------------
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
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPURIUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPURIUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void SampleTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            Assert.True(true, "True was false.");
        }

        [Fact]
        public void ParseHostOnlyURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:sip.domain.com");

            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseHostAndUserURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com");

            Assert.True(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseWithParamURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com;param=1234");

            Assert.True(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Parameters.Get("PARAM") == "1234", "The SIP URI Parameter was not parsed correctly.");
            Assert.True(sipURI.ToString() == "sip:user@sip.domain.com;param=1234", "The SIP URI was not correctly to string'ed.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseWithParamAndPortURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:1234@sip.domain.com:5060;TCID-0");

            logger.LogDebug("URI Name = {User}", sipURI.User);
            logger.LogDebug("URI Host = {Host}", sipURI.Host);

            Assert.True(sipURI.User == "1234", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com:5060", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Parameters.Has("TCID-0"), "The SIP URI Parameter was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseWithHeaderURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com?header=1234");

            Assert.True(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Headers.Get("header") == "1234", "The SIP URI Header was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void SpaceInHostNameURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:Blue Face");

            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "Blue Face", "The SIP URI Host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ContactAsteriskURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("*");

            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "*", "The SIP URI Host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AreEqualNoParamsURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AreEqualIPAddressNoParamsURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@192.168.1.101");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@192.168.1.101");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AreEqualWithParamsURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            logger.LogDebug("-----------------------------------------");
        }


        [Fact]
        public void NotEqualWithParamsURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value2");

            Assert.NotEqual(sipURI1, sipURI2);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AreEqualWithHeadersURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2?header1=value1&header2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1?header2=value2&header1=value1");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly identified as equal.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void NotEqualWithHeadersURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2?header1=value2&header2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1?header2=value2&header1=value1");

            logger.LogDebug("sipURI1: {sipURI1}", sipURI1);
            logger.LogDebug("sipURI2: {sipURI2}", sipURI2);

            Assert.NotEqual(sipURI1, sipURI2);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void UriWithParameterEqualityURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");

            Assert.True(sipURI1 == sipURI2, "The SIP URIs did not have equal hash codes.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void UriWithDifferentParamsEqualURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value2");

            Assert.NotEqual(sipURI1, sipURI2);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void UriWithSameParamsInDifferentOrderURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");

            Assert.Equal(sipURI1, sipURI2);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AreEqualNullURIsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;
            SIPURI sipURI2 = null;

            Assert.True(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void NotEqualOneNullURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");
            SIPURI sipURI2 = null;

            Assert.False(sipURI1 == sipURI2, "The SIP URIs were incorrectly found as equal.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AreEqualNullEqualsOverloadUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;

            Assert.True(sipURI1 == null, "The SIP URIs were not correctly found as equal.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AreEqualNullNotEqualsOverloadUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;

            Assert.False(sipURI1 != null, "The SIP URIs were incorrectly found as equal.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void UnknownSchemeUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURI("mailto:1234565"));

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownSchemesUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            foreach (var value in System.Enum.GetValues(typeof(SIPSchemesEnum)))
            {
                Assert.True(SIPURI.ParseSIPURI(value.ToString() + ":1234565").Scheme == (SIPSchemesEnum)value);
            }

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParamsInUserPortionURIWithUserPhoneTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:C=on;t=DLPAN@10.0.0.1:5060;lr;user=phone");

            Assert.True(sipURI.UserParameters.Get("C") == "on", "SIP user portion parsed incorrectly. Param C wrong.");
            Assert.True(sipURI.UserParameters.Get("t") == "DLPAN", "SIP user portion parsed incorrectly. Param t wrong");
            Assert.True(sipURI.UserWithoutParameters == "", "SIP user portion parsed incorrectly. UserNoParams wrong");
            Assert.True(sipURI.User == "C=on;t=DLPAN", "SIP user portion parsed incorrectly. User wrong");
            Assert.True("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void OneParamInUserPortionURIWithUserPhoneTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:C=on@10.0.0.1:5060;lr;user=phone");

            Assert.True(sipURI.UserParameters.Get("C") == "on", "SIP user portion parsed incorrectly. Param C wrong.");
            Assert.True(sipURI.UserWithoutParameters == "", "SIP user portion parsed incorrectly. UserNoParams wrong");
            Assert.True(sipURI.User == "C=on", "SIP user portion parsed incorrectly. User wrong");
            Assert.True("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParamsInUserPortionURIPhoneNumWithParamsTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:+41999999999;cpc=ordinary@10.0.0.1:5060;transport=udp;user=phone");

            Assert.True(sipURI.UserParameters.Get("cpc") == "ordinary", "SIP user portion parsed incorrectly. Param cpc wrong.");
            Assert.True(sipURI.UserWithoutParameters == "+41999999999", "SIP user portion parsed incorrectly. UserNoParams wrong.");
            Assert.True(sipURI.User == "+41999999999;cpc=ordinary", "SIP user portion parsed incorrectly. User wrong.");
            Assert.True("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParamsInUserPortionURITest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:C=on;t=DLPAN@10.0.0.1:5060;lr");

            Assert.True("C=on;t=DLPAN" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.True("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void SwitchTagParameterUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:joebloggs@sip.mysipswitch.com;switchtag=119651");

            Assert.True("joebloggs" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.True("sip.mysipswitch.com" == sipURI.Host, "SIP host portion parsed incorrectly.");
            Assert.True("119651" == sipURI.Parameters.Get("switchtag"), "switchtag parameter parsed incorrectly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void LongUserUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:EhZgKgLM9CwGqYDAECqDpL5MNrM_sKN5NurN5q_pssAk4oxhjKEMT4@10.0.0.1:5060");

            Assert.True("EhZgKgLM9CwGqYDAECqDpL5MNrM_sKN5NurN5q_pssAk4oxhjKEMT4" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.True("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParsePartialURINoSchemeUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip.domain.com");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Protocol == SIPProtocolsEnum.udp, "The SIP URI protocol was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParsePartialURISIPSSchemeUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sips:sip.domain.com:1234");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sips, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com:1234", "The SIP URI Host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParsePartialURIWithUserUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip:joe.bloggs@sip.domain.com:1234;transport=tcp");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.User == "joe.bloggs", "The SIP URI User was not parsed correctly.");
            Assert.True(sipURI.Host == "sip.domain.com:1234", "The SIP URI Host was not parsed correctly.");
            Assert.True(sipURI.Protocol == SIPProtocolsEnum.tcp, "The SIP URI protocol was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Got a URI like this from Zoiper.
        /// </summary>
        [Fact]
        public void ParseHoHostUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURI("sip:;transport=UDP"));

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void UDPProtocolToStringTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = new SIPURI(SIPSchemesEnum.sip, SIPEndPoint.ParseSIPEndPoint("127.0.0.1"));
            logger.LogDebug("{sipURI}", sipURI.ToString());
            Assert.True(sipURI.ToString() == "sip:127.0.0.1:5060", "The SIP URI was not ToString'ed correctly.");
            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseUDPProtocolToStringTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("127.0.0.1");
            logger.LogDebug("{sipURI}", sipURI.ToString());
            Assert.True(sipURI.ToString() == "sip:127.0.0.1", "The SIP URI was not ToString'ed correctly.");
            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseBigURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("TRUNKa1d2ce524d44cd54f39ac78bcdba85c7@65.98.14.50:5069");
            logger.LogDebug("{sipURI}", sipURI.ToString());
            Assert.True(sipURI.ToString() == "sip:TRUNKa1d2ce524d44cd54f39ac78bcdba85c7@65.98.14.50:5069", "The SIP URI was not ToString'ed correctly.");
            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseMalformedContactUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURIRelaxed("sip:twolmsted@24.183.120.253, sip:5060"));

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void NoPortIPv4CanonicalAddressToStringTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:127.0.0.1");
            logger.LogDebug("SIP URI {sipURI}", sipURI);
            logger.LogDebug("Canonical address {CanonicalAddress}", sipURI.CanonicalAddress);

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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:[::1]");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.Host == "[::1]", "The SIP URI host was not parsed correctly.");
            Assert.True(sipURI.ToSIPEndPoint() == new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.IPv6Loopback, 5060, null, null), "The SIP URI end point details were not parsed correctly.");

            logger.LogDebug("SIP URI {sipURI}", sipURI);

            //rj2: should throw exception
            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURI("sip:user1@2a00:1450:4005:800::2004"));//ipv6 host without mandatory brackets
            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURI("sip:user1@:::ffff:127.0.0.1"));//ipv6 with mapped ipv4 localhost
            //rj2: should/does not throw exception
            sipURI = SIPURI.ParseSIPURI("sip:[::ffff:127.0.0.1]");
            Assert.True(sipURI.Host == "[::ffff:127.0.0.1]", "The SIP URI host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with an IPv6 address and an explicit port is correctly parsed.
        /// </summary>
        [Fact]
        public void ParseIPv6WithExplicitPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:[::1]:6060");

            Assert.True(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.True(sipURI.Host == "[::1]:6060", "The SIP URI host was not parsed correctly.");
            Assert.True(sipURI.ToSIPEndPoint() == new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.IPv6Loopback, 6060, null, null), "The SIP URI end point details were not parsed correctly.");

            logger.LogDebug("SIP URI {sipURI}", sipURI.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that SIP URIs with an IPv6 address with default ports generate the same canonical addresses.
        /// </summary>
        [Fact]
        public void IPv6UriPortToNoPortCanonicalAddressUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURINoPort = SIPURI.ParseSIPURI("sip:[::1]");
            SIPURI sipURIWIthPort = SIPURI.ParseSIPURI("sip:[::1]:5060");
            logger.LogDebug("SIP URI {sipURI}", sipURINoPort.ToString());
            logger.LogDebug("Canonical address {CanonicalAddress}", sipURIWIthPort.CanonicalAddress);

            Assert.Equal(sipURINoPort.CanonicalAddress, sipURIWIthPort.CanonicalAddress);

            Assert.True(sipURINoPort.ToString() == "sip:[::1]", "The SIP URI was not ToString'ed correctly.");
            Assert.True(sipURIWIthPort.CanonicalAddress == "sip:[::1]:5060", "The SIP URI canonical address was not correct.");

            //rj2: more test cases
            sipURINoPort = SIPURI.ParseSIPURI("sip:[2a00:1450:4005:800::2004]");
            sipURIWIthPort = SIPURI.ParseSIPURI("sip:[2a00:1450:4005:800::2004]:5060");
            logger.LogDebug("SIP URI {sipURI}", sipURINoPort.ToString());
            logger.LogDebug("Canonical address {CanonicalAddress}", sipURIWIthPort.CanonicalAddress);

            Assert.Equal(sipURINoPort.CanonicalAddress, sipURIWIthPort.CanonicalAddress);

            Assert.True(sipURINoPort.ToString() == "sip:[2a00:1450:4005:800::2004]", "The SIP URI was not ToString'ed correctly.");
            Assert.True(sipURIWIthPort.CanonicalAddress == "sip:[2a00:1450:4005:800::2004]:5060", "The SIP URI canonical address was not correct.");


            sipURINoPort = SIPURI.ParseSIPURI("sip:user1@[2a00:1450:4005:800::2004]");
            sipURIWIthPort = SIPURI.ParseSIPURI("sip:user1@[2a00:1450:4005:800::2004]:5060");
            logger.LogDebug("SIP URI {sipURI}", sipURINoPort.ToString());
            logger.LogDebug("Canonical address {CanonicalAddress}", sipURIWIthPort.CanonicalAddress);

            Assert.Equal(sipURINoPort.CanonicalAddress, sipURIWIthPort.CanonicalAddress);

            Assert.True(sipURINoPort.ToString() == "sip:user1@[2a00:1450:4005:800::2004]", "The SIP URI was not ToString'ed correctly.");
            Assert.True(sipURIWIthPort.CanonicalAddress == "sip:user1@[2a00:1450:4005:800::2004]:5060", "The SIP URI canonical address was not correct.");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that the SIP URI constructor that takes an IP address works correctly for IPv6.
        /// </summary>
        [Fact]
        public void UriConstructorWithIPv6AddressUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI ipv6Uri = new SIPURI(SIPSchemesEnum.sip, IPAddress.IPv6Loopback, 6060);

            Assert.Equal("sip:[::1]:6060", ipv6Uri.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that the invalid SIP URIs with IPv6 addresses missing enclosing '[' and ']' throw an exception.
        /// </summary>
        [Fact]
        public void InvalidIPv6UriThrowUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI ipv6Uri = new SIPURI(SIPSchemesEnum.sip, IPAddress.IPv6Loopback, 6060);

            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURI("sip:user1@2a00:1450:4005:800::2004"));
            Assert.Throws<SIPValidationException>(() => SIPURI.ParseSIPURI("sip:user1@:::ffff:127.0.0.1"));

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with an IPv4 address mapped to an IPv6 address is parsed correctly.
        /// </summary>
        [Fact]
        public void ParseIPv4MappedAddressUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI ipv6Uri = new SIPURI(SIPSchemesEnum.sip, IPAddress.IPv6Loopback, 6060);

            var uri = SIPURI.ParseSIPURI("sip:[::ffff:127.0.0.1]");

            Assert.Equal("[::ffff:127.0.0.1]", uri.Host);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI supplied in a REFER request Refer-To header can be parsed.
        /// </summary>
        [Fact]
        public void ParseReplacesHeaderUriUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI referToUri = SIPURI.ParseSIPURI("sip:1@127.0.0.1?Replaces=84929ZTg0Zjk1Y2UyM2Q1OWJjYWNlZmYyYTI0Njg1YjgwMzI%3Bto-tag%3D8787f9cc94bb4bb19c089af17e5a94f7%3Bfrom-tag%3Dc2b89404");

            Assert.NotNull(referToUri);
            Assert.Equal("sip:1@127.0.0.1", referToUri.ToParameterlessString());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a private IPv4 address gets mangled correctly.
        /// </summary>
        [Fact]
        public void MangleUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@192.168.0.50:5060?Replaces=xyz");
            SIPURI mangled = SIPURI.Mangle(uri, IPSocket.Parse("67.222.131.147:5090"));

            logger.LogDebug("Mangled URI {mangled}.", mangled);

            Assert.NotNull(mangled);
            Assert.Equal("sip:user@67.222.131.147:5090?Replaces=xyz", mangled.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a private IPv4 address and no port gets mangled correctly.
        /// </summary>
        [Fact]
        public void MangleNoPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@192.168.0.50?Replaces=xyz");
            SIPURI mangled = SIPURI.Mangle(uri, IPSocket.Parse("67.222.131.147:5090"));

            logger.LogDebug("Mangled URI {mangled}.", mangled);

            Assert.NotNull(mangled);
            Assert.Equal("sip:user@67.222.131.147:5090?Replaces=xyz", mangled.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a private IPv4 address and that was recived on an IPv6
        /// end point gets mangled correctly.
        /// </summary>
        [Fact]
        public void MangleReceiveOnIPv6UnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@192.168.0.50:5060?Replaces=xyz");
            SIPURI mangled = SIPURI.Mangle(uri, IPSocket.Parse("[2001:730:3ec2::10]:5090"));

            logger.LogDebug("Mangled URI {mangled}.", mangled);

            Assert.NotNull(mangled);
            Assert.Equal("sip:user@[2001:730:3ec2::10]:5090?Replaces=xyz", mangled.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a does not get mangled when the received on IP address
        /// is the same private IP address as the URI host.
        /// </summary>
        [Fact]
        public void NoMangleSameAddressUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@192.168.0.50:5060?Replaces=xyz");
            SIPURI mangled = SIPURI.Mangle(uri, IPSocket.Parse("192.168.0.50:5060"));

            Assert.Null(mangled);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a public IPv4 address does not get mangled.
        /// </summary>
        [Fact]
        public void NoManglePublicIPv4UnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@67.222.131.149:5060?Replaces=xyz");
            SIPURI mangled = SIPURI.Mangle(uri, IPSocket.Parse("67.222.131.147:5060"));

            Assert.Null(mangled);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a hostname does not get mangled.
        /// </summary>
        [Fact]
        public void NoMangleHostnameUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@sipsorcery.com:5060?Replaces=xyz");
            SIPURI mangled = SIPURI.Mangle(uri, IPSocket.Parse("67.222.131.147:5060"));

            Assert.Null(mangled);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with an IPv6 host does not get mangled.
        /// </summary>
        [Fact]
        public void NoMangleIPv6UnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@[2001:730:3ec2::10]:5060?Replaces=xyz");
            SIPURI mangled = SIPURI.Mangle(uri, IPSocket.Parse("67.222.131.147:5060"));

            Assert.Null(mangled);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a default UDP port is correctly recognised.
        /// </summary>
        [Fact]
        public void DefaultUdpPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@[2001:730:3ec2::10]:5060?Replaces=xyz");

            Assert.Equal(SIPProtocolsEnum.udp, uri.Protocol);
            Assert.True(uri.IsDefaultPort());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a the port not set is correctly recognised as using the
        /// default port.
        /// </summary>
        [Fact]
        public void DefaultUdpPortWhenNotSetUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@[2001:730:3ec2::10]?Replaces=xyz");

            Assert.Equal(SIPProtocolsEnum.udp, uri.Protocol);
            Assert.True(uri.IsDefaultPort());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a non-default UDP port is correctly recognised.
        /// </summary>
        [Fact]
        public void NonDefaultUdpPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@[2001:730:3ec2::10]:5080?Replaces=xyz");

            Assert.Equal(SIPProtocolsEnum.udp, uri.Protocol);
            Assert.False(uri.IsDefaultPort());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a default TCP port is correctly recognised.
        /// </summary>
        [Fact]
        public void DefaultTcpPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@[2001:730:3ec2::10]:5060;transport=tcp?Replaces=xyz");

            Assert.Equal(SIPProtocolsEnum.tcp, uri.Protocol);
            Assert.True(uri.IsDefaultPort());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a default TLS port is correctly recognised.
        /// </summary>
        [Fact]
        public void DefaultTlsPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sips:user@[2001:730:3ec2::10]:5061?Replaces=xyz");

            Assert.Equal(SIPProtocolsEnum.tls, uri.Protocol);
            Assert.True(uri.IsDefaultPort());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a default Web Socket port is correctly recognised.
        /// </summary>
        [Fact]
        public void DefaultWebSocketPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@[2001:730:3ec2::10]:80;transport=ws?Replaces=xyz");

            Assert.Equal(SIPProtocolsEnum.ws, uri.Protocol);
            Assert.True(uri.IsDefaultPort());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a default Secure Web Socket port is correctly recognised.
        /// </summary>
        [Fact]
        public void DefaultSecureWebSocketPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = SIPURI.ParseSIPURI("sip:user@[2001:730:3ec2::10]:443;transport=wss?Replaces=xyz");

            Assert.Equal(SIPProtocolsEnum.wss, uri.Protocol);
            Assert.True(uri.IsDefaultPort());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP URI with a tel: scheme is correctly recognised.
        /// </summary>
        [Fact]
        public void ParseTelSchemeURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var telStr = "tel:+1-(201) 555 0123;phone-context=example.com";
            SIPURI sipURI = SIPURI.ParseSIPURI(telStr);

            Assert.Null(sipURI.User);
            Assert.Equal("+1-(201) 555 0123", sipURI.Host);
            Assert.Equal("tel:+1-(201) 555 0123", sipURI.CanonicalAddress);
            Assert.True(sipURI.Parameters.Has("phone-context"), "The tel: URI parameters were not parsed correctly.");
            Assert.Equal("example.com", sipURI.Parameters.Get("phone-context"));
            Assert.Equal(telStr, sipURI.ToString());
            Assert.Equal("+1-(201) 555 0123", sipURI.ToAOR());

            logger.LogDebug("-----------------------------------------");
        }
    }
}
