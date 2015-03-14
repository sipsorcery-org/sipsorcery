using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPHeaderUnitTest
    {
        private const string m_CRLF = "\r\n";

        [TestMethod]
        public void ParseXTenHeadersTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenInviteHeaders =
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                "Content-Length: 271" + m_CRLF;

            Console.WriteLine("Original SIP Headers:\n" + xtenInviteHeaders);

            string[] headersCollection = Regex.Split(xtenInviteHeaders, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Console.WriteLine("Parsed SIP Headers:\n" + sipHeader.ToString());

            Assert.IsTrue("Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipHeader.Vias.TopViaHeader.ToString(), "The Via header was not parsed correctly," + sipHeader.Vias.TopViaHeader.ToString() + ".");
            Assert.IsTrue("SER Test X" == sipHeader.From.FromName, "The From Name value was not parsed correctly, " + sipHeader.From.FromName + ".");
            Assert.IsTrue("sip:aaronxten@sip.blueface.ie:5065" == sipHeader.From.FromURI.ToString(), "The From URI value was not parsed correctly, " + sipHeader.From.FromURI + ".");
            Assert.IsTrue("196468136" == sipHeader.From.FromTag, "The From tag value was not parsed correctly, " + sipHeader.From.FromTag + ".");
            Assert.IsTrue(null == sipHeader.To.ToName, "The To Name value was not parsed correctly, " + sipHeader.To.ToName + ".");
            Assert.IsTrue("sip:303@sip.blueface.ie" == sipHeader.To.ToURI.ToString(), "The To URI value was not parsed correctly, " + sipHeader.To.ToURI + ".");
            Assert.IsTrue(null == sipHeader.To.ToTag, "The To tag value was not parsed correctly, " + sipHeader.To.ToTag + ".");
            Assert.IsTrue("A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" == sipHeader.CallId, "The Call ID values was not parsed correctly, " + sipHeader.CallId + ".");
            Assert.IsTrue(49429 == sipHeader.CSeq, "The CSeq value was not parsed correctly, " + sipHeader.CSeq + ".");
            Assert.IsTrue(SIPMethodsEnum.INVITE == sipHeader.CSeqMethod, "The CSeq Method value was not parsed correctly, " + sipHeader.CSeqMethod + ".");
            Assert.IsTrue(70 == sipHeader.MaxForwards, "The MaxForwards value was not parsed correctly, " + sipHeader.MaxForwards + ".");
            Assert.IsTrue("X-PRO release 1103v" == sipHeader.UserAgent, "The UserAgent value was not parsed correctly, " + sipHeader.UserAgent + ".");
            Assert.IsTrue("application/sdp" == sipHeader.ContentType, "The ContentType value was not parsed correctly, " + sipHeader.ContentType + ".");
            Assert.IsTrue(271 == sipHeader.ContentLength, "The ContentLength value was not parsed correctly, " + sipHeader.ContentLength + ".");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseAsteriskRecordRouteHeadersTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenInviteHeaders =
                "Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bK8Z4EIWBeY45fRGwC0qIeu/xpw3A=" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64091;branch=z9hG4bK4E0728C26A0640E7830D7C9179D08D67" + m_CRLF +
                "Record-Route: <sip:213.168.225.133:5060;lr>,<sip:220.240.255.198:64091;lr>" + m_CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=457825353" + m_CRLF +
                "To: <sip:303@bluesipd>;tag=as02a64a42" + m_CRLF +
                "Call-ID: 8A702FA2-18F0-4DFC-AED5-C1A883EADB84@192.168.1.2" + m_CRLF +
                "CSeq: 38002 INVITE" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
                "Contact: <sip:303@213.168.225.133>" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 350" + m_CRLF;
            Console.WriteLine("Original SIP Headers:\n" + xtenInviteHeaders);

            string[] headersCollection = Regex.Split(xtenInviteHeaders, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Console.WriteLine("Parsed SIP Headers:\n" + sipHeader.ToString());

            SIPRoute topRoute = sipHeader.RecordRoutes.PopRoute();
            Assert.IsTrue(topRoute.Host == "213.168.225.133:5060", "The top record route was not parsed correctly.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseAMulitLineHeaderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string mulitLineHeader =
                "Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bK8Z4EIWBeY45fRGwC0qIeu/xpw3A=" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64091;branch=z9hG4bK4E0728C26A0640E7830D7C9179D08D67" + m_CRLF +
                "Record-Route: <sip:213.168.225.133:5060;lr>," + m_CRLF +
                " <sip:220.240.255.198:64091;lr>" + m_CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=457825353" + m_CRLF +
                "To: <sip:303@bluesipd>;tag=as02a64a42" + m_CRLF +
                "Call-ID: 8A702FA2-18F0-4DFC-AED5-C1A883EADB84@192.168.1.2" + m_CRLF +
                "CSeq: 38002 INVITE" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
                "Contact: <sip:303@213.168.225.133>" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 350" + m_CRLF;

            Console.WriteLine("Original SIP Headers:\n" + mulitLineHeader);

            string[] headersCollection = SIPHeader.SplitHeaders(mulitLineHeader);
            foreach (string headerStr in headersCollection)
            {
                Console.WriteLine("Header => " + headerStr + ".");
            }

            Assert.IsTrue(headersCollection.Length == 12, "The headers were not split properly.");

            Console.WriteLine();

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Console.WriteLine("Parsed SIP Headers:\n" + sipHeader.ToString());

            Assert.IsTrue(sipHeader.RecordRoutes.Length == 2, "An incorrect number of record route entries was extracted, number was " + sipHeader.RecordRoutes.Length + ".");

            SIPRoute topRoute = sipHeader.RecordRoutes.PopRoute();
            Assert.IsTrue(topRoute.Host == "213.168.225.133:5060", "The top record route was not parsed correctly.");

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void ParseAuthenticationRequiredHeadersTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string authReqdHeaders =
                "SIP/2.0 407 Proxy Authentication Required" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5066;received=220.240.255.198:64066;branch=65cacee9-25b6-405c-8f82-e40427438af7" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>;tag=as67b6416e" + m_CRLF +
                "Contact: <sip:303@213.168.225.133>" + m_CRLF +
                "Call-ID: 5bcb927f-9571-47d0-a2a1-36226bcf7665@192.168.1.2" + m_CRLF +
                "CSeq: 908 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Proxy-Authenticate: Digest realm=\"asterisk\", nonce=\"15aeff81\"" + m_CRLF +
                "Record-Route: <sip:213.168.225.135:5060;lr>" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF + m_CRLF;
            Console.WriteLine("Original SIP Headers:\n" + authReqdHeaders);

            string[] headersCollection = Regex.Split(authReqdHeaders, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Console.WriteLine("Parsed SIP Headers:\n" + sipHeader.ToString());

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        [ExpectedException(typeof(SIPValidationException))]
        public void ParseNoViaHeadersUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string noViaHeaders =
                "SIP/2.0 407 Proxy Authentication Required" + m_CRLF +
                "From: dev <sip:aarondev@84.203.144.70>;tag=0013c339acec050c0635cf7b-48c41caf" + m_CRLF +
                "To: <sip:303@84.203.144.70>;tag=as019f14fe" + m_CRLF +
                "Call-ID: 0013c339-acec0011-7181eff5-7cfa0e24@89.100.92.186" + m_CRLF +
                "CSeq: 101 INVITE" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, SUBSCRIBE, NOTIFY" + m_CRLF +
                "Contact: <sip:303@213.168.225.133:5061>" + m_CRLF +
                "Proxy-Authenticate: Digest algorithm=MD5, realm=\"sip.blueface.ie\", nonce=\"789f00ab\"" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            Console.WriteLine("Original SIP Headers:\n" + noViaHeaders);

            string[] headersCollection = Regex.Split(noViaHeaders, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Console.WriteLine("---------------------------------------------------");
        }

        [TestMethod]
        public void LowerCaseExpiresUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "Via: SIP/2.0/UDP 192.168.1.32:10254;branch=z9hG4bK-d87543-eb7c9f44883c5955-1--d87543-;rport;received=89.100.104.191" + m_CRLF +
                "To: aaronxten <sip:aaronxten@213.200.94.181>" + m_CRLF +
                "From: aaronxten <sip:aaronxten@213.200.94.181>;tag=774d2561" + m_CRLF +
                "Call-ID: MTBhNGZjZmQ2OTc3MWU5MTZjNWUxMDYxOTk1MjdmYzk." + m_CRLF +
                "CSeq: 2 REGISTER" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.32:10254;rinstance=6d2bbd8014ca7a76>;expires=0" + m_CRLF +
                "Max-Forwards: 69" + m_CRLF +
                "expires: 60" + m_CRLF +
                "User-Agent: X-Lite release 1006e stamp 34025" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY, MESSAGE, SUBSCRIBE, INFO" + m_CRLF + m_CRLF;

            Console.WriteLine("Original SIP Headers:\n" + sipMsg);

            string[] headersCollection = Regex.Split(sipMsg, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Assert.IsTrue(sipHeader.Expires == 60, "The expires values was parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void HuaweiRegisterUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "From: <sip:10000579@200.170.136.196>;tag=0477183750" + m_CRLF +
                "To: <sip:10000579@200.170.136.196>;tag=414dedfe" + m_CRLF +
                "CSeq: 1 REGISTER" + m_CRLF +
                "Call-ID: 438676792abe47328fc557da2d84d0ee" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.102:7246;branch=z9hG4bK92460620adf84edab2341899a3453f79;received=124.168.235.200;rport=10552" + m_CRLF +
                "Server: Huawei SoftX3000 R006B03D" + m_CRLF +
                "WWW-Authenticate: Digest realm=\"huawei\"," + m_CRLF +
                " nonce=\"248e4b4457f252ae53c859bfe03c4f93\",domain=\"sip:huawei.com\"," + m_CRLF +
                " stale=false,algorithm=MD5" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            Console.WriteLine("Original SIP Headers:\n" + sipMsg);

            string[] headersCollection = SIPHeader.SplitHeaders(sipMsg);

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Console.WriteLine(sipHeader.ToString());
            Console.WriteLine(sipHeader.AuthenticationHeader.ToString());

            Assert.IsTrue(Regex.Match(sipHeader.AuthenticationHeader.ToString(), "nonce").Success, "The WWW-Authenticate header was not correctly parsed across multpiple lines.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void MultipleContactHeadersUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "From: <sip:10000579@200.170.136.196>;tag=0477183750" + m_CRLF +
                "To: <sip:10000579@200.170.136.196>;tag=414dedfe" + m_CRLF +
                "CSeq: 1 REGISTER" + m_CRLF +
                "Contact: \"Joe Bloggs\" <sip:joe@bloggs.com>;expires=0" + m_CRLF +
                "Call-ID: 438676792abe47328fc557da2d84d0ee" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.102:7246;branch=z9hG4bK92460620adf84edab2341899a3453f79;received=124.168.235.200;rport=10552" + m_CRLF +
                "Server: Huawei SoftX3000 R006B03D" + m_CRLF +
                "WWW-Authenticate: Digest realm=\"huawei\"," + m_CRLF +
                " nonce=\"248e4b4457f252ae53c859bfe03c4f93\",domain=\"sip:huawei.com\"," + m_CRLF +
                " stale=false,algorithm=MD5" + m_CRLF +
                "Contact: \"Jane Doe\" <sip:jane@doe.com>" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            Console.WriteLine("Original SIP Headers:\n" + sipMsg);

            string[] headersCollection = SIPHeader.SplitHeaders(sipMsg);

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Assert.IsTrue(sipHeader.Contact.Count == 2, "The SIP header had the wrong number of Contacts.");
            Assert.IsTrue(sipHeader.Contact[0].ToString() == "\"Joe Bloggs\" <sip:joe@bloggs.com>;expires=0", "The first Contact header was not parsed correctly.");
            Assert.IsTrue(sipHeader.Contact[1].ToString() == "\"Jane Doe\" <sip:jane@doe.com>", "The second Contact header was not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }
    }
}
