using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPURIUnitTest
    {
        [TestMethod]
        public void SampleTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
            Assert.IsTrue(true, "True was false.");
        }

        [TestMethod]
        public void ParseHostOnlyURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:sip.domain.com");

            Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseHostAndUserURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com");

            Assert.IsTrue(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseWithParamURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com;param=1234");

            Assert.IsTrue(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.IsTrue(sipURI.Parameters.Get("PARAM") == "1234", "The SIP URI Parameter was not parsed correctly.");
            Assert.IsTrue(sipURI.ToString() == "sip:user@sip.domain.com;param=1234", "The SIP URI was not correctly to string'ed.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseWithParamAndPortURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:1234@sip.domain.com:5060;TCID-0");

            Console.WriteLine("URI Name = " + sipURI.User);
            Console.WriteLine("URI Host = " + sipURI.Host);

            Assert.IsTrue(sipURI.User == "1234", "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "sip.domain.com:5060", "The SIP URI Host was not parsed correctly.");
            Assert.IsTrue(sipURI.Parameters.Has("TCID-0"), "The SIP URI Parameter was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseWithHeaderURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com?header=1234");

            Assert.IsTrue(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.IsTrue(sipURI.Headers.Get("header") == "1234", "The SIP URI Header was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void SpaceInHostNameURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:Blue Face");

            Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "Blue Face", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ContactAsteriskURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("*");

            Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "*", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void AreEqualNoParamsURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");

            Assert.AreEqual(sipURI1, sipURI2, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void AreEqualIPAddressNoParamsURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@192.168.1.101");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@192.168.1.101");

            Assert.AreEqual(sipURI1, sipURI2, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void AreEqualWithParamsURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1");

            Assert.AreEqual(sipURI1, sipURI2, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void NotEqualWithParamsURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value2");

            Assert.AreNotEqual(sipURI1, sipURI2, "The SIP URIs were incorrectly equated as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void AreEqualWithHeadersURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2?header1=value1&header2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1?header2=value2&header1=value1");

            Assert.AreEqual(sipURI1, sipURI2, "The SIP URIs were not correctly identified as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void NotEqualWithHeadersURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2?header1=value2&header2=value2");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1?header2=value2&header1=value1");

            Assert.AreNotEqual(sipURI1, sipURI2, "The SIP URIs were not correctly identified as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void GetHashCodeEqualityURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");

            Assert.AreEqual(sipURI1.GetHashCode(), sipURI2.GetHashCode(), "The SIP URIs did not have equal hash codes.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void GetHashCodeNotEqualURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value2");

            Assert.AreNotEqual(sipURI1.GetHashCode(), sipURI2.GetHashCode(), "The SIP URIs did not have equal hash codes.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void GetHashCodeDiffParamOrderURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1");
            SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");

            Assert.AreEqual(sipURI1.GetHashCode(), sipURI2.GetHashCode(), "The SIP URIs did not have equal hash codes.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void AreEqualNullURIsUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;
            SIPURI sipURI2 = null;

            Assert.IsTrue(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void NotEqualOneNullURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");
            SIPURI sipURI2 = null;

            Assert.IsFalse(sipURI1 == sipURI2, "The SIP URIs were incorrectly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void AreEqualNullEqualsOverloadUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;

            Assert.IsTrue(sipURI1 == null, "The SIP URIs were not correctly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void AreEqualNullNotEqualsOverloadUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI1 = null;

            Assert.IsFalse(sipURI1 != null, "The SIP URIs were incorrectly found as equal.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        [ExpectedException(typeof(SIPValidationException))]
        public void UnknownSchemeUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("tel:1234565");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParamsInUserPortionURITest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:C=on;t=DLPAN@10.0.0.1:5060;lr");

            Assert.IsTrue("C=on;t=DLPAN" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.IsTrue("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void SwitchTagParameterUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:joebloggs@sip.mysipswitch.com;switchtag=119651");

            Assert.IsTrue("joebloggs" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.IsTrue("sip.mysipswitch.com" == sipURI.Host, "SIP host portion parsed incorrectly.");
            Assert.IsTrue("119651" == sipURI.Parameters.Get("switchtag"), "switchtag parameter parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void LongUserUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:EhZgKgLM9CwGqYDAECqDpL5MNrM_sKN5NurN5q_pssAk4oxhjKEMT4@10.0.0.1:5060");

            Assert.IsTrue("EhZgKgLM9CwGqYDAECqDpL5MNrM_sKN5NurN5q_pssAk4oxhjKEMT4" == sipURI.User, "SIP user portion parsed incorrectly.");
            Assert.IsTrue("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParsePartialURINoSchemeUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip.domain.com");

            Assert.IsTrue(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
            Assert.IsTrue(sipURI.Protocol == SIPProtocolsEnum.udp, "The SIP URI protocol was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParsePartialURISIPSSchemeUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sips:sip.domain.com:1234");

            Assert.IsTrue(sipURI.Scheme == SIPSchemesEnum.sips, "The SIP URI scheme was not parsed correctly.");
            Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "sip.domain.com:1234", "The SIP URI Host was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParsePartialURIWithUserUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip:joe.bloggs@sip.domain.com:1234;transport=tcp");

            Assert.IsTrue(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
            Assert.IsTrue(sipURI.User == "joe.bloggs", "The SIP URI User was not parsed correctly.");
            Assert.IsTrue(sipURI.Host == "sip.domain.com:1234", "The SIP URI Host was not parsed correctly.");
            Assert.IsTrue(sipURI.Protocol == SIPProtocolsEnum.tcp, "The SIP URI protocol was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        /// <summary>
        /// Got a URI like this from Zoiper.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(SIPValidationException))]
        public void ParseHoHostUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURI("sip:;transport=UDP");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void UDPProtocolToStringTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = new SIPURI(SIPSchemesEnum.sip, SIPEndPoint.ParseSIPEndPoint("127.0.0.1"));
            Console.WriteLine(sipURI.ToString());
            Assert.IsTrue(sipURI.ToString() == "sip:127.0.0.1:5060", "The SIP URI was not ToString'ed correctly.");
            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseUDPProtocolToStringTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("127.0.0.1");
            Console.WriteLine(sipURI.ToString());
            Assert.IsTrue(sipURI.ToString() == "sip:127.0.0.1", "The SIP URI was not ToString'ed correctly.");
            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseBigURIUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("TRUNKa1d2ce524d44cd54f39ac78bcdba85c7@65.98.14.50:5069");
            Console.WriteLine(sipURI.ToString());
            Assert.IsTrue(sipURI.ToString() == "sip:TRUNKa1d2ce524d44cd54f39ac78bcdba85c7@65.98.14.50:5069", "The SIP URI was not ToString'ed correctly.");
            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        [ExpectedException(typeof(SIPValidationException))]
        public void ParseMalformedContactUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip:twolmsted@24.183.120.253, sip:5060");
            Console.WriteLine(sipURI.ToString());
            Console.WriteLine("-----------------------------------------");
        }
    }
}
