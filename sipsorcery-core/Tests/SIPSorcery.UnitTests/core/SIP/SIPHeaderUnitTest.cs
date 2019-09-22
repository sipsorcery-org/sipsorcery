using System;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.UnitTests
{
    [TestClass]
    public class SIPHeaderUnitTest
    {
        [TestMethod]
        public void ParseFromHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "\"User\" <sip:user@domain.com>;tag=abcdef";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Console.WriteLine("From header=" + sipFromHeader.ToString() + ".");

            Assert.IsTrue(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domain.com", "The From header URI was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromTag == "abcdef", "The From header Tag was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.ToString() == testFromHeader, "The From header ToString method did not produce the correct results.");
        }

        [TestMethod]
        public void ParseFromHeaderNoTagTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "User <sip:user@domain.com>";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.IsTrue(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domain.com", "The From header URI was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseFromHeaderSocketDomainTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "User <sip:user@127.0.0.1:5090>";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.IsTrue(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@127.0.0.1:5090", "The From header URI was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseFromHeaderSocketDomainAndTagTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "User <sip:user@127.0.0.1:5090>;tag=abcdef";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.IsTrue(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@127.0.0.1:5090", "The From header URI was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromTag == "abcdef", "The From header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseFromHeaderNoNameTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "<sip:user@domaintest.com>";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.IsTrue(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com", "The From header URI was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseFromHeaderNoAngleBracketsTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "sip:user@domaintest.com";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.IsTrue(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com", "The From header URI was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseFromHeaderNoSpaceTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "UNAVAILABLE<sip:user@domaintest.com:5060>;tag=abcd";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.IsTrue(sipFromHeader.FromName == "UNAVAILABLE", "The From header name was not parsed correctly, name=" + sipFromHeader.FromName + ".");
            Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com:5060", "The From header URI was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromTag == "abcd", "The From header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseFromHeaderNoUserTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "<sip:sip.domain.com>;tag=as6900b876";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.IsTrue(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:sip.domain.com", "The From header URI was not parsed correctly.");
            Assert.IsTrue(sipFromHeader.FromTag == "as6900b876", "The From header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseToHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

            Assert.IsTrue(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToTag == "abcdef", "The To header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseMSCToHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testToHeader = "sip:xxx@127.0.110.30;tag=AZHf2-ZMfDX0";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

            Console.WriteLine("To header: " + sipToHeader.ToString());

            Assert.IsTrue(sipToHeader.ToName == null, "The To header name was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:xxx@127.0.110.30", "The To header URI was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToTag == "AZHf2-ZMfDX0", "The To header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ToStringToHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

            Assert.IsTrue(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToTag == "abcdef", "The To header Tag was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToString() == "\"User\" <sip:user@domain.com>;tag=abcdef", "The To header was not put ToString correctly.");
        }

        /// <summary>
        /// New requests should be received with no To header tag. It is up to the recevier to populate the To header tag.
        /// This test makes sure that changing the tag works correctly.
        /// </summary>
        [TestMethod]
        public void ChangeTagToHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

            string newTag = "123456";
            sipToHeader.ToTag = newTag;

            Console.WriteLine("To header with new tag: " + sipToHeader.ToString());

            Assert.IsTrue(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToTag == newTag, "The To header Tag was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToString() == "\"User\" <sip:user@domain.com>;tag=123456", "The To header was not put ToString correctly.");
        }

        [TestMethod]
        public void ParseByeToHeader()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testHeader = "\"Joe Bloggs\" <sip:joe@sip.blueface.ie>;tag=0013c339acec34652d988c7e-4fddcdef";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testHeader);

            Console.WriteLine("To header: " + sipToHeader.ToString());

            Assert.IsTrue(sipToHeader.ToName == "Joe Bloggs", "The To header name was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:joe@sip.blueface.ie", "The To header URI was not parsed correctly.");
            Assert.IsTrue(sipToHeader.ToTag == "0013c339acec34652d988c7e-4fddcdef", "The To header Tag was not parsed correctly.");
        }

        [TestMethod]
        public void ParseAuthHeaderUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthenticationHeader authHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthorization, "Digest realm=\"o-fone.com\",nonce=\"mv1keFTRX4yYVsHb/E+rviOflIurIw\",algorithm=MD5,qop=\"auth\",username=\"joe.bloggs\", response=\"1234\",uri=\"sip:o-fone.com\"");

            Console.WriteLine("SIP Auth Header=" + authHeader.ToString() + ".");

            Assert.AreEqual(authHeader.SIPDigest.Realm, "o-fone.com", "The SIP auth header realm was not parsed correctly.");
            Assert.AreEqual(authHeader.SIPDigest.Nonce, "mv1keFTRX4yYVsHb/E+rviOflIurIw", "The SIP auth header nonce was not parsed correctly.");
            Assert.AreEqual(authHeader.SIPDigest.URI, "sip:o-fone.com", "The SIP URI was not parsed correctly.");
            Assert.AreEqual(authHeader.SIPDigest.Username, "joe.bloggs", "The SIP username was not parsed correctly.");
            Assert.AreEqual(authHeader.SIPDigest.Response, "1234", "The SIP response was not parsed correctly.");
        }


        [TestMethod]
        public void MissingBracketsRouteTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute newRoute = new SIPRoute("sip:127.0.0.1:5060");

            Console.WriteLine(newRoute.ToString());

            Assert.IsTrue(newRoute.URI.ToString() == "sip:127.0.0.1:5060", "The Route header URI was not correctly parsed.");
        }

        [TestMethod]
        public void ParseRouteTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060;lr>");

            Console.WriteLine("SIP Route=" + route.ToString() + ".");

            Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.AreEqual(route.ToString(), "<sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
            Assert.IsFalse(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
        }

        [TestMethod]
        public void SetLooseRouteTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060>");
            route.IsStrictRouter = false;

            Console.WriteLine("SIP Route=" + route.ToString() + ".");

            Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.AreEqual(route.ToString(), "<sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
            Assert.IsFalse(route.IsStrictRouter, "Route was not correctly settable as a loose router.");
        }

        [TestMethod]
        public void RemoveLooseRouterTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060;lr>");
            route.IsStrictRouter = true;

            Console.WriteLine("SIP Route=" + route.ToString() + ".");

            Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.AreEqual(route.ToString(), "<sip:127.0.0.1:5060>", "The SIP route string was not correct.");
            Assert.IsTrue(route.IsStrictRouter, "Route was not correctly settable as a strict router.");
        }

        [TestMethod]
        public void ParseRouteWithDisplayNameTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("12345656 <sip:127.0.0.1:5060;lr>");

            Console.WriteLine("SIP Route=" + route.ToString() + ".");
            Console.WriteLine("Route to SIPEndPoint=" + route.ToSIPEndPoint().ToString() + ".");

            Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.AreEqual(route.ToString(), "\"12345656\" <sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
            Assert.IsFalse(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
            Assert.AreEqual(route.ToSIPEndPoint().ToString(), "udp:127.0.0.1:5060", "The SIP route did not produce the correct SIP End Point.");
        }

        [TestMethod]
        public void ParseRouteWithDoubleQuotedDisplayNameTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("\"Joe Bloggs\" <sip:127.0.0.1:5060;lr>");

            Console.WriteLine("SIP Route=" + route.ToString() + ".");

            Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.AreEqual(route.ToString(), "\"Joe Bloggs\" <sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
            Assert.IsFalse(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
        }

        [TestMethod]
        public void ParseRouteWithUserPortionTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string routeStr = "<sip:0033820600000@127.0.0.1:5060;lr;transport=udp>";
            SIPRoute route = SIPRoute.ParseSIPRoute(routeStr);

            Console.WriteLine("SIP Route=" + route.ToString() + ".");
            Console.WriteLine("Route to SIPEndPoint=" + route.ToSIPEndPoint().ToString() + ".");

            Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.AreEqual(route.ToString(), routeStr, "The SIP route string was not correct.");
            Assert.IsFalse(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
            Assert.AreEqual(route.ToSIPEndPoint().ToString(), "udp:127.0.0.1:5060", "The SIP route did not produce the correct SIP End Point.");
        }

        [TestMethod]
        public void ParseSIPRouteSetTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string routeSetString = "<sip:127.0.0.1:5434;lr>,<sip:10.0.0.1>,<sip:192.168.0.1;ftag=12345;lr=on>";
            SIPRouteSet routeSet = SIPRouteSet.ParseSIPRouteSet(routeSetString);

            Console.WriteLine(routeSet.ToString());

            Assert.IsTrue(routeSet.Length == 3, "The parsed route set had an incorrect length.");
            Assert.IsTrue(routeSet.ToString() == routeSetString, "The parsed route set did not produce the same string as the original parsed value.");
            SIPRoute topRoute = routeSet.PopRoute();
            Assert.IsTrue(topRoute.Host == "127.0.0.1:5434", "The first route host was not parsed correctly.");
            Assert.IsFalse(topRoute.IsStrictRouter, "The first route host was not correctly recognised as a loose router.");
            topRoute = routeSet.PopRoute();
            Assert.IsTrue(topRoute.Host == "10.0.0.1", "The second route host was not parsed correctly.");
            Assert.IsTrue(topRoute.IsStrictRouter, "The second route host was not correctly recognised as a strict router.");
            topRoute = routeSet.PopRoute();
            Assert.IsTrue(topRoute.Host == "192.168.0.1", "The third route host was not parsed correctly.");
            Assert.IsFalse(topRoute.IsStrictRouter, "The third route host was not correctly recognised as a loose router.");
            Assert.IsTrue(topRoute.URI.Parameters.Get("ftag") == "12345", "The ftag parameter on the third route was not correctly parsed.");
        }

        [TestMethod]
        public void AdjustReceivedViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            SIPViaSet viaSet = new SIPViaSet();
            viaSet.PushViaHeader(sipViaHeaders[0]);

            viaSet.UpateTopViaHeader(SIPSorcery.Sys.IPSocket.ParseSocketString("88.88.88.88:1234"));

            Assert.IsTrue(viaSet.Length == 1, "Incorrect number of Via headers in set.");
            Assert.IsTrue(viaSet.TopViaHeader.Host == "192.168.1.2", "Top Via Host was incorrect.");
            Assert.IsTrue(viaSet.TopViaHeader.Port == 5065, "Top Via Port was incorrect.");
            Assert.IsTrue(viaSet.TopViaHeader.ContactAddress == "192.168.1.2:5065", "Top Via ContactAddress was incorrect.");
            Assert.IsTrue(viaSet.TopViaHeader.ReceivedFromIPAddress == "88.88.88.88", "Top Via received was incorrect.");
            Assert.IsTrue(viaSet.TopViaHeader.ReceivedFromPort == 1234, "Top Via rport was incorrect.");

            Console.WriteLine("---------------------------------------------------");
        }

        /// <summary>
        /// Tests that when the sent from socket is the same as the socket received from that the received and rport
        /// parameters are still updated.
        /// </summary>
        [TestMethod]
        public void AdjustReceivedCorrectAlreadyViaHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            SIPViaSet viaSet = new SIPViaSet();
            viaSet.PushViaHeader(sipViaHeaders[0]);

            viaSet.UpateTopViaHeader(SIPSorcery.Sys.IPSocket.ParseSocketString("192.168.1.2:5065"));

            Assert.IsTrue(viaSet.Length == 1, "Incorrect number of Via headers in set.");
            Assert.IsTrue(viaSet.TopViaHeader.Host == "192.168.1.2", "Top Via Host was incorrect.");
            Assert.IsTrue(viaSet.TopViaHeader.Port == 5065, "Top Via Port was incorrect.");
            Assert.IsTrue(viaSet.TopViaHeader.ContactAddress == "192.168.1.2:5065", "Top Via ContactAddress was incorrect.");
            Assert.IsTrue(viaSet.TopViaHeader.ReceivedFromIPAddress == "192.168.1.2", "Top Via received was incorrect.");
            Assert.IsTrue(viaSet.TopViaHeader.ReceivedFromPort == 5065, "Top Via rport was incorrect.");

            Console.WriteLine("---------------------------------------------------");
        }
    }
}
