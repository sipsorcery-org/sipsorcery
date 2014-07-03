using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using log4net;

namespace SIPSorcery.SIP.App.UnitTests
{
    [TestClass]
    [Ignore] // Only suitable to be run locally on developer machine.
    public class SIPDNSManagerUnitTest
    {
        public SIPDNSManagerUnitTest()
        {
            log4net.Config.BasicConfigurator.Configure();
        }

        private TestContext testContextInstance;

        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        [TestMethod]
        public void SampleTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
            Assert.IsTrue(true, "True was false.");
        }

        [TestMethod]
        public void IPAddresTargetTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("10.0.0.100");
            SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

            Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

            Assert.IsTrue(lookupSIPEndPoint.Protocol == SIPProtocolsEnum.udp, "The resolved protocol was not correct.");
            Assert.IsTrue(lookupSIPEndPoint.GetIPEndPoint().ToString() == "10.0.0.100:5060", "The resolved socket was not correct.");
        }

        [TestMethod]
        public void IPAddresAndSIPSTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("sips:10.0.0.100");
            SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

            Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

            Assert.IsTrue(lookupSIPEndPoint.Protocol == SIPProtocolsEnum.tls, "The resolved protocol was not correct.");
            Assert.IsTrue(lookupSIPEndPoint.GetIPEndPoint().ToString() == "10.0.0.100:5061", "The resolved socket was not correct.");
        }

        [TestMethod]
        public void HostWithExplicitPortTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string host = "sip.blueface.ie:5060";
            int attempts = 0;

            SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService(host);

            while (lookupResult.Pending && attempts < 5)
            {
                attempts++;
                Console.WriteLine("Lookup for " + host + " is pending, " + attempts + ".");
                Thread.Sleep(1000);

                lookupResult = SIPDNSManager.ResolveSIPService(host);
            }

            SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

            Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

            Assert.IsTrue(lookupSIPEndPoint.Protocol == SIPProtocolsEnum.udp, "The resolved protocol was not correct.");
            Assert.IsTrue(lookupSIPEndPoint.GetIPEndPoint().ToString() == "194.213.29.92:5060", "The resolved socket was not correct.");
        }

        [TestMethod]
        public void HostWithNoExplicitPortTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int attempts = 0;
            string host = "sip.blueface.ie";

            SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService(host);

            while (lookupResult.Pending && attempts < 5)
            {
                attempts++;
                Console.WriteLine("Lookup for " + host + " is pending, " + attempts + ".");
                Thread.Sleep(1000);

                lookupResult = SIPDNSManager.ResolveSIPService(host);
            }

            SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

            Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

            Assert.IsTrue(lookupSIPEndPoint.Protocol == SIPProtocolsEnum.udp, "The resolved protocol was not correct.");
            Assert.IsTrue(lookupSIPEndPoint.GetIPEndPoint().ToString() == "194.213.29.91:5060", "The resolved socket was not correct.");
        }

        [TestMethod]
        public void HostWithExplicitPortAndMultipleIPsTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int attempts = 0;
            string host = "callcentric.com:5060";

            SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService(host);

            while (lookupResult.Pending && attempts < 5)
            {
                attempts++;
                Console.WriteLine("Lookup for " + host + " is pending, " + attempts + ".");
                Thread.Sleep(1000);

                lookupResult = SIPDNSManager.ResolveSIPService(host);
            }

            SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

            Assert.IsTrue(lookupResult.EndPointResults.Count > 0, "The number of lookup results returned was incorrect.");
        }

        [TestMethod]
        public void HostWithNAPTRRecordTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            //int attempts = 0;
            string host = "columbia.edu";

            SIPDNSLookupResult lookupResult = new SIPDNSLookupResult(SIPURI.ParseSIPURIRelaxed(host));
            SIPDNSManager.DNSNAPTRRecordLookup(host, false, ref lookupResult);

            Assert.IsTrue(lookupResult.SIPNAPTRResults != null && lookupResult.SIPNAPTRResults.Count > 0, "The number of NAPTR results returned was incorrect.");
            //Assert.IsTrue(lookupResult.SIPSRVResults != null && lookupResult.SIPSRVResults.Count > 0, "The number of SRV results returned was incorrect.");
            //Assert.IsTrue(lookupResult.EndPointResults.Count > 0, "The number of lookup results returned was incorrect.");
        }

        [TestMethod]
        public void HostWithNoNAPTRAndSRVTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int attempts = 0;
            string host = "callcentric.com";

            SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService(host);

            while (lookupResult.Pending && attempts < 5)
            {
                attempts++;
                Console.WriteLine("Lookup for " + host + " is pending, " + attempts + ".");
                Thread.Sleep(1000);

                lookupResult = SIPDNSManager.ResolveSIPService(host);
            }

            Assert.IsTrue(lookupResult.EndPointResults.Count > 0, "The number of lookup results returned was incorrect.");
        }

        [TestMethod]
        public void TLSSRVTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string host = "snom.com";

            SIPDNSLookupResult lookupResult = new SIPDNSLookupResult(SIPURI.ParseSIPURIRelaxed(host));
            SIPDNSManager.DNSSRVRecordLookup(SIPSchemesEnum.sip, SIPProtocolsEnum.tls, host, false, ref lookupResult);

            Console.WriteLine("result=" + lookupResult.SIPSRVResults[0].Data + ".");

            Assert.IsTrue(lookupResult.SIPSRVResults != null && lookupResult.SIPSRVResults.Count > 0, "The number of SRV results returned was incorrect.");
            Assert.IsTrue(lookupResult.SIPSRVResults[0].SIPService == SIPServicesEnum.siptls, "The SIP Service returned for the lookup was incorrect.");
            Assert.IsTrue(lookupResult.SIPSRVResults[0].Data == "sip.snom.com.", "The target returned for the lookup was incorrect.");
        }

        [TestMethod]
        public void TestSRVPriorityRespected()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int attempts = 0;
            string host = "us.voxalot.com";

            SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService(host);

            while (lookupResult.Pending && attempts < 5)
            {
                attempts++;
                Console.WriteLine("Lookup for " + host + " is pending, " + attempts + ".");
                Thread.Sleep(1000);

                lookupResult = SIPDNSManager.ResolveSIPService(host);
            }

            SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

            Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

            //Assert.IsTrue(lookupSIPEndPoint.Protocol == SIPProtocolsEnum.udp, "The resolved protocol was not correct.");
            // Assert.IsTrue(lookupSIPEndPoint.GetIPEndPoint().ToString() == "194.213.29.100:5060", "The resolved socket was not correct.");
        }
    }
}
