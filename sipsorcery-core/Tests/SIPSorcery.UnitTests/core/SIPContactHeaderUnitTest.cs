using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPContactHeaderUnitTest
    {
        public SIPContactHeaderUnitTest()
        { }

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
        public void ParseContactHeaderDomainForUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:sip.domain.com@sip.domain.com>";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:sip.domain.com@sip.domain.com", "The Contact header URI was not parsed correctly.");
        }

        [TestMethod]
        [ExpectedException(typeof(SIPValidationException))]
        public void ParseBadAastraContactHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:10001@127.0.0.1:5060\n";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI);

            //Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            //Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:10001@127.0.0.1:5060", "The Contact header URI was not parsed correctly.");
        }

        [TestMethod]
        public void ParseNoAngleQuotesContactHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "sip:10001@127.0.0.1:5060";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI);

            Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:10001@127.0.0.1:5060", "The Contact header URI was not parsed correctly.");
        }

        [TestMethod]
        public void ParseCiscoContactHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:user@127.0.0.1:5060;user=phone;transport=udp>;+sip.instance=\"<urn:uuid:00000000-0000-0000-0000-0006d74b0e72>\";+u.sip!model.ccm.cisco.com=\"7\"";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

            Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060;user=phone;transport=udp", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.IsTrue(sipContactHeaderList[0].ContactParameters.ToString() == ";+sip.instance=\"<urn:uuid:00000000-0000-0000-0000-0006d74b0e72>\";+u.sip!model.ccm.cisco.com=\"7\"", "The Contact header Parameters were not parsed correctly.");
        }

        [TestMethod]
        [ExpectedException(typeof(SIPValidationException))]
        public void ParseNoLineBreakContactHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:10001@127.0.0.1:5060\nAllow: OPTIONS";

            Console.WriteLine("Contact Header = " + testContactHeader + ".");

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());

            //Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            //Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:10001@127.0.0.1:5060", "The Contact header URI was not parsed correctly.");
        }

        [TestMethod]
        public void ParseContactWithParamHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:user@127.0.0.1:5060;ftag=1233>";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

            Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060;ftag=1233", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.Parameters.Get("ftag") == "1233", "The Contact header ftag URI parameter was not parsed correctly.");
        }

        [TestMethod]
        public void ParseExpiresContactHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:user@127.0.0.1:5060>; expires=60";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());
            Console.WriteLine("Contact = " + sipContactHeaderList[0].ToString());

            Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.IsTrue(sipContactHeaderList[0].Expires == 60, "The Contact header Expires parameter was not parsed correctly.");
        }

        [TestMethod]
        public void ParseZeroExpiresContactHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:user@127.0.0.1:5060>; expires=0";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());
            Console.WriteLine("Contact = " + sipContactHeaderList[0].ToString());

            Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.IsTrue(sipContactHeaderList[0].Expires == 0, "The Contact header Expires parameter was not parsed correctly.");
        }

        [TestMethod]
        public void MultipleContactsHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "\"Mr. Watson\" <sip:watson@worcester.bell-telephone.com>;q=0.7; expires=3600, \"Mr. Watson\" <sip:watson@bell-telephone.com> ;q=0.1";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header Count=" + sipContactHeaderList.Count + ".");
            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

            Assert.IsTrue(sipContactHeaderList[0].ContactName == "Mr. Watson", "The Contact header name was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:watson@worcester.bell-telephone.com", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.IsTrue(sipContactHeaderList[0].Expires == 3600, "The Contact header Expires parameter was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].Q == "0.7", "The Contact header Q parameter was not parsed correctly.");
        }

        [TestMethod]
        public void MultipleContactsWithURIParamsHeaderUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "\"Mr. Watson\" <sip:watson@worcester.bell-telephone.com;ftag=1232>;q=0.7; expires=3600, \"Mr. Watson\" <sip:watson@bell-telephone.com?nonsense=yes> ;q=0.1";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Console.WriteLine("Contact Header Count=" + sipContactHeaderList.Count + ".");
            Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

            Assert.IsTrue(sipContactHeaderList[0].ContactName == "Mr. Watson", "The Contact header name was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:watson@worcester.bell-telephone.com;ftag=1232", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.IsTrue(sipContactHeaderList[0].Expires == 3600, "The Contact header Expires parameter was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].Q == "0.7", "The Contact header Q parameter was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[0].ContactURI.Parameters.Get("ftag") == "1232", "The Contact header URI ftag parameter was not parsed correctly.");
            Assert.IsTrue(sipContactHeaderList[1].ContactURI.Headers.Get("nonsense") == "yes", "The Contact header URI nonsense header was not parsed correctly.");
        }

        [TestMethod]
        public void SimpleAreEqualUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));

            Assert.IsTrue(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }

        [TestMethod]
        public void SimpleNotEqualUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.2:5060"));

            Assert.IsFalse(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }

        [TestMethod]
        public void WithParametersAreEqualUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;param1=value1"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;param1=value1"));

            Assert.IsTrue(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }

        [TestMethod]
        public void WithExpiresParametersAreEqualUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060> ;expires=0; param1=value1"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;expires=50;param1=value1"));

            Assert.IsTrue(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }

        [TestMethod]
        public void WithDifferentNamesAreEqualUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("\"Joe Bloggs\" <sip:user@127.0.0.1:5060> ;expires=0; param1=value1"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("\"Jane Doe\" <sip:user@127.0.0.1:5060>;expires=50;param1=value1"));

            Assert.IsTrue(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }
    }
}
