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
    public class SIPHeaderUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPHeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private const string m_CRLF = "\r\n";

        [Fact]
        public void ParseXTenHeadersTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug("Original SIP Headers:\n" + xtenInviteHeaders);

            string[] headersCollection = Regex.Split(xtenInviteHeaders, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            logger.LogDebug("Parsed SIP Headers:\n" + sipHeader.ToString());

            Assert.True("Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipHeader.Vias.TopViaHeader.ToString(), "The Via header was not parsed correctly," + sipHeader.Vias.TopViaHeader.ToString() + ".");
            Assert.True("SER Test X" == sipHeader.From.FromName, "The From Name value was not parsed correctly, " + sipHeader.From.FromName + ".");
            Assert.True("sip:aaronxten@sip.blueface.ie:5065" == sipHeader.From.FromURI.ToString(), "The From URI value was not parsed correctly, " + sipHeader.From.FromURI + ".");
            Assert.True("196468136" == sipHeader.From.FromTag, "The From tag value was not parsed correctly, " + sipHeader.From.FromTag + ".");
            Assert.True(null == sipHeader.To.ToName, "The To Name value was not parsed correctly, " + sipHeader.To.ToName + ".");
            Assert.True("sip:303@sip.blueface.ie" == sipHeader.To.ToURI.ToString(), "The To URI value was not parsed correctly, " + sipHeader.To.ToURI + ".");
            Assert.True(null == sipHeader.To.ToTag, "The To tag value was not parsed correctly, " + sipHeader.To.ToTag + ".");
            Assert.True("A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" == sipHeader.CallId, "The Call ID values was not parsed correctly, " + sipHeader.CallId + ".");
            Assert.True(49429 == sipHeader.CSeq, "The CSeq value was not parsed correctly, " + sipHeader.CSeq + ".");
            Assert.True(SIPMethodsEnum.INVITE == sipHeader.CSeqMethod, "The CSeq Method value was not parsed correctly, " + sipHeader.CSeqMethod + ".");
            Assert.True(70 == sipHeader.MaxForwards, "The MaxForwards value was not parsed correctly, " + sipHeader.MaxForwards + ".");
            Assert.True("X-PRO release 1103v" == sipHeader.UserAgent, "The UserAgent value was not parsed correctly, " + sipHeader.UserAgent + ".");
            Assert.True("application/sdp" == sipHeader.ContentType, "The ContentType value was not parsed correctly, " + sipHeader.ContentType + ".");
            Assert.True(271 == sipHeader.ContentLength, "The ContentLength value was not parsed correctly, " + sipHeader.ContentLength + ".");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseAsteriskRecordRouteHeadersTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
            logger.LogDebug("Original SIP Headers:\n" + xtenInviteHeaders);

            string[] headersCollection = Regex.Split(xtenInviteHeaders, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            logger.LogDebug("Parsed SIP Headers:\n" + sipHeader.ToString());

            SIPRoute topRoute = sipHeader.RecordRoutes.PopRoute();
            Assert.True(topRoute.Host == "213.168.225.133:5060", "The top record route was not parsed correctly.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseAMulitLineHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug("Original SIP Headers:\n" + mulitLineHeader);

            string[] headersCollection = SIPHeader.SplitHeaders(mulitLineHeader);
            foreach (string headerStr in headersCollection)
            {
                logger.LogDebug("Header => " + headerStr + ".");
            }

            Assert.True(headersCollection.Length == 12, "The headers were not split properly.");

            logger.LogDebug("");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            logger.LogDebug("Parsed SIP Headers:\n" + sipHeader.ToString());

            Assert.True(sipHeader.RecordRoutes.Length == 2, "An incorrect number of record route entries was extracted, number was " + sipHeader.RecordRoutes.Length + ".");

            SIPRoute topRoute = sipHeader.RecordRoutes.PopRoute();
            Assert.True(topRoute.Host == "213.168.225.133:5060", "The top record route was not parsed correctly.");

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseAuthenticationRequiredHeadersTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
            logger.LogDebug("Original SIP Headers:\n" + authReqdHeaders);

            string[] headersCollection = Regex.Split(authReqdHeaders, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            logger.LogDebug("Parsed SIP Headers:\n" + sipHeader.ToString());

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void ParseNoViaHeadersUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug("Original SIP Headers:\n" + noViaHeaders);

            string[] headersCollection = Regex.Split(noViaHeaders, "\r\n");

            Assert.Throws<SIPValidationException>(() => SIPHeader.ParseSIPHeaders(headersCollection));

            logger.LogDebug("---------------------------------------------------");
        }

        [Fact]
        public void LowerCaseExpiresUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug("Original SIP Headers:\n" + sipMsg);

            string[] headersCollection = Regex.Split(sipMsg, "\r\n");

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Assert.True(sipHeader.Expires == 60, "The expires values was parsed incorrectly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void HuaweiRegisterUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug("Original SIP Headers:\n" + sipMsg);

            string[] headersCollection = SIPHeader.SplitHeaders(sipMsg);

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            logger.LogDebug(sipHeader.ToString());
            logger.LogDebug(sipHeader.AuthenticationHeader.ToString());

            Assert.True(Regex.Match(sipHeader.AuthenticationHeader.ToString(), "nonce").Success, "The WWW-Authenticate header was not correctly parsed across multiple lines.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void MultipleContactHeadersUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug("Original SIP Headers:\n" + sipMsg);

            string[] headersCollection = SIPHeader.SplitHeaders(sipMsg);

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Assert.True(sipHeader.Contact.Count == 2, "The SIP header had the wrong number of Contacts.");
            Assert.True(sipHeader.Contact[0].ToString() == "\"Joe Bloggs\" <sip:joe@bloggs.com>;expires=0", "The first Contact header was not parsed correctly.");
            Assert.True(sipHeader.Contact[1].ToString() == "\"Jane Doe\" <sip:jane@doe.com>", "The second Contact header was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ExtractHeadersUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "From: <sip:10000579@200.170.136.196>;tag=0477183750" + m_CRLF +
                "To: <sip:10000579@200.170.136.196>;tag=414dedfe" + m_CRLF +
                "CSeq: 1 REGISTER" + m_CRLF +
                "Call-ID: 438676792abe47328fc557da2d84d0ee" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.102:7246;branch=z9hG4bK92460620adf84edab2341899a3453f79;received=124.168.235.200;rport=10552" + m_CRLF +
                "Server: Huawei SoftX3000 R006B03D" + m_CRLF +
                "Refer-To: Test Refer-To" + m_CRLF +
                "Authentication-Info: Test Authentication-Info" + m_CRLF +
                "WWW-Authenticate: Digest realm=\"huawei\"," + m_CRLF +
                " nonce=\"248e4b4457f252ae53c859bfe03c4f93\",domain=\"sip:huawei.com\"," + m_CRLF +
                " stale=false,algorithm=MD5" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            logger.LogDebug("Original SIP Headers:\n" + sipMsg);

            string[] headersCollection = SIPHeader.SplitHeaders(sipMsg);

            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Assert.Equal("Test Refer-To", sipHeader.ReferTo);
            Assert.Equal("Test Authentication-Info", sipHeader.AuthenticationInfo);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseFromHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "\"User\" <sip:user@domain.com>;tag=abcdef";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            logger.LogDebug("From header=" + sipFromHeader.ToString() + ".");

            Assert.True(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
            Assert.True(sipFromHeader.FromURI.ToString() == "sip:user@domain.com", "The From header URI was not parsed correctly.");
            Assert.True(sipFromHeader.FromTag == "abcdef", "The From header Tag was not parsed correctly.");
            Assert.True(sipFromHeader.ToString() == testFromHeader, "The From header ToString method did not produce the correct results.");
        }

        [Fact]
        public void ParseFromHeaderNoTagTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "User <sip:user@domain.com>";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.True(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
            Assert.True(sipFromHeader.FromURI.ToString() == "sip:user@domain.com", "The From header URI was not parsed correctly.");
            Assert.True(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseFromHeaderSocketDomainTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "User <sip:user@127.0.0.1:5090>";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.True(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
            Assert.True(sipFromHeader.FromURI.ToString() == "sip:user@127.0.0.1:5090", "The From header URI was not parsed correctly.");
            Assert.True(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseFromHeaderSocketDomainAndTagTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "User <sip:user@127.0.0.1:5090>;tag=abcdef";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.True(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
            Assert.True(sipFromHeader.FromURI.ToString() == "sip:user@127.0.0.1:5090", "The From header URI was not parsed correctly.");
            Assert.True(sipFromHeader.FromTag == "abcdef", "The From header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseFromHeaderNoNameTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "<sip:user@domaintest.com>";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.True(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
            Assert.True(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com", "The From header URI was not parsed correctly.");
            Assert.True(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseFromHeaderNoAngleBracketsTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "sip:user@domaintest.com";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.True(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
            Assert.True(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com", "The From header URI was not parsed correctly.");
            Assert.True(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseFromHeaderNoSpaceTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "UNAVAILABLE<sip:user@domaintest.com:5060>;tag=abcd";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.True(sipFromHeader.FromName == "UNAVAILABLE", "The From header name was not parsed correctly, name=" + sipFromHeader.FromName + ".");
            Assert.True(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com:5060", "The From header URI was not parsed correctly.");
            Assert.True(sipFromHeader.FromTag == "abcd", "The From header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseFromHeaderNoUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testFromHeader = "<sip:sip.domain.com>;tag=as6900b876";

            SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

            Assert.True(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
            Assert.True(sipFromHeader.FromURI.ToString() == "sip:sip.domain.com", "The From header URI was not parsed correctly.");
            Assert.True(sipFromHeader.FromTag == "as6900b876", "The From header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseToHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

            Assert.True(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
            Assert.True(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
            Assert.True(sipToHeader.ToTag == "abcdef", "The To header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseMSCToHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testToHeader = "sip:xxx@127.0.110.30;tag=AZHf2-ZMfDX0";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

            logger.LogDebug("To header: " + sipToHeader.ToString());

            Assert.True(sipToHeader.ToName == null, "The To header name was not parsed correctly.");
            Assert.True(sipToHeader.ToURI.ToString() == "sip:xxx@127.0.110.30", "The To header URI was not parsed correctly.");
            Assert.True(sipToHeader.ToTag == "AZHf2-ZMfDX0", "The To header Tag was not parsed correctly.");
        }

        [Fact]
        public void ToStringToHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

            Assert.True(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
            Assert.True(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
            Assert.True(sipToHeader.ToTag == "abcdef", "The To header Tag was not parsed correctly.");
            Assert.True(sipToHeader.ToString() == "\"User\" <sip:user@domain.com>;tag=abcdef", "The To header was not put ToString correctly.");
        }

        /// <summary>
        /// New requests should be received with no To header tag. It is up to the recevier to populate the To header tag.
        /// This test makes sure that changing the tag works correctly.
        /// </summary>
        [Fact]
        public void ChangeTagToHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

            string newTag = "123456";
            sipToHeader.ToTag = newTag;

            logger.LogDebug("To header with new tag: " + sipToHeader.ToString());

            Assert.True(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
            Assert.True(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
            Assert.True(sipToHeader.ToTag == newTag, "The To header Tag was not parsed correctly.");
            Assert.True(sipToHeader.ToString() == "\"User\" <sip:user@domain.com>;tag=123456", "The To header was not put ToString correctly.");
        }

        [Fact]
        public void ParseByeToHeader()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testHeader = "\"Joe Bloggs\" <sip:joe@sip.blueface.ie>;tag=0013c339acec34652d988c7e-4fddcdef";

            SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testHeader);

            logger.LogDebug("To header: " + sipToHeader.ToString());

            Assert.True(sipToHeader.ToName == "Joe Bloggs", "The To header name was not parsed correctly.");
            Assert.True(sipToHeader.ToURI.ToString() == "sip:joe@sip.blueface.ie", "The To header URI was not parsed correctly.");
            Assert.True(sipToHeader.ToTag == "0013c339acec34652d988c7e-4fddcdef", "The To header Tag was not parsed correctly.");
        }

        [Fact]
        public void ParseAuthHeaderUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthenticationHeader authHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthorization, "Digest realm=\"o-fone.com\",nonce=\"mv1keFTRX4yYVsHb/E+rviOflIurIw\",algorithm=MD5,qop=\"auth\",username=\"joe.bloggs\", response=\"1234\",uri=\"sip:o-fone.com\"");

            logger.LogDebug("SIP Auth Header=" + authHeader.ToString() + ".");

            Assert.True(authHeader.SIPDigest.Realm == "o-fone.com", "The SIP auth header realm was not parsed correctly.");
            Assert.True(authHeader.SIPDigest.Nonce == "mv1keFTRX4yYVsHb/E+rviOflIurIw", "The SIP auth header nonce was not parsed correctly.");
            Assert.True(authHeader.SIPDigest.URI == "sip:o-fone.com", "The SIP URI was not parsed correctly.");
            Assert.True(authHeader.SIPDigest.Username == "joe.bloggs", "The SIP username was not parsed correctly.");
            Assert.True(authHeader.SIPDigest.Response == "1234", "The SIP response was not parsed correctly.");
        }


        [Fact]
        public void MissingBracketsRouteTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute newRoute = new SIPRoute("sip:127.0.0.1:5060");

            logger.LogDebug(newRoute.ToString());

            Assert.True(newRoute.URI.ToString() == "sip:127.0.0.1:5060", "The Route header URI was not correctly parsed.");
        }

        [Fact]
        public void ParseRouteTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060;lr>");

            logger.LogDebug("SIP Route=" + route.ToString() + ".");

            Assert.True(route.Host == "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.True(route.ToString() == "<sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
            Assert.False(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
        }

        [Fact]
        public void SetLooseRouteTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060>");
            route.IsStrictRouter = false;

            logger.LogDebug("SIP Route=" + route.ToString() + ".");

            Assert.True(route.Host == "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.True(route.ToString() == "<sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
            Assert.False(route.IsStrictRouter, "Route was not correctly settable as a loose router.");
        }

        [Fact]
        public void RemoveLooseRouterTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060;lr>");
            route.IsStrictRouter = true;

            logger.LogDebug("SIP Route=" + route.ToString() + ".");

            Assert.True(route.Host == "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.True(route.ToString() == "<sip:127.0.0.1:5060>", "The SIP route string was not correct.");
            Assert.True(route.IsStrictRouter, "Route was not correctly settable as a strict router.");
        }

        [Fact]
        public void ParseRouteWithDisplayNameTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("12345656 <sip:127.0.0.1:5060;lr>");

            logger.LogDebug("SIP Route=" + route.ToString() + ".");
            logger.LogDebug("Route to SIPEndPoint=" + route.ToSIPEndPoint().ToString() + ".");

            Assert.True(route.Host == "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.True(route.ToString() == "\"12345656\" <sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
            Assert.False(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
            Assert.True(route.ToSIPEndPoint().ToString() == "udp:127.0.0.1:5060", "The SIP route did not produce the correct SIP End Point.");
        }

        [Fact]
        public void ParseRouteWithDoubleQuotedDisplayNameTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPRoute route = SIPRoute.ParseSIPRoute("\"Joe Bloggs\" <sip:127.0.0.1:5060;lr>");

            logger.LogDebug("SIP Route=" + route.ToString() + ".");

            Assert.True(route.Host == "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.True(route.ToString() == "\"Joe Bloggs\" <sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
            Assert.False(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
        }

        [Fact]
        public void ParseRouteWithUserPortionTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string routeStr = "<sip:0033820600000@127.0.0.1:5060;lr;transport=udp>";
            SIPRoute route = SIPRoute.ParseSIPRoute(routeStr);

            logger.LogDebug("SIP Route=" + route.ToString() + ".");
            logger.LogDebug("Route to SIPEndPoint=" + route.ToSIPEndPoint().ToString() + ".");

            Assert.True(route.Host == "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
            Assert.True(route.ToString() == routeStr, "The SIP route string was not correct.");
            Assert.False(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
            Assert.True(route.ToSIPEndPoint().ToString() == "udp:127.0.0.1:5060", "The SIP route did not produce the correct SIP End Point.");
        }

        [Fact]
        public void ParseSIPRouteSetTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string routeSetString = "<sip:127.0.0.1:5434;lr>,<sip:10.0.0.1>,<sip:192.168.0.1;ftag=12345;lr=on>";
            SIPRouteSet routeSet = SIPRouteSet.ParseSIPRouteSet(routeSetString);

            logger.LogDebug(routeSet.ToString());

            Assert.True(routeSet.Length == 3, "The parsed route set had an incorrect length.");
            Assert.True(routeSet.ToString() == routeSetString, "The parsed route set did not produce the same string as the original parsed value.");
            SIPRoute topRoute = routeSet.PopRoute();
            Assert.True(topRoute.Host == "127.0.0.1:5434", "The first route host was not parsed correctly.");
            Assert.False(topRoute.IsStrictRouter, "The first route host was not correctly recognised as a loose router.");
            topRoute = routeSet.PopRoute();
            Assert.True(topRoute.Host == "10.0.0.1", "The second route host was not parsed correctly.");
            Assert.True(topRoute.IsStrictRouter, "The second route host was not correctly recognised as a strict router.");
            topRoute = routeSet.PopRoute();
            Assert.True(topRoute.Host == "192.168.0.1", "The third route host was not parsed correctly.");
            Assert.False(topRoute.IsStrictRouter, "The third route host was not correctly recognised as a loose router.");
            Assert.True(topRoute.URI.Parameters.Get("ftag") == "12345", "The ftag parameter on the third route was not correctly parsed.");
        }

        [Fact]
        public void AdjustReceivedViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            SIPViaSet viaSet = new SIPViaSet();
            viaSet.PushViaHeader(sipViaHeaders[0]);

            viaSet.UpateTopViaHeader(SIPSorcery.Sys.IPSocket.ParseSocketString("88.88.88.88:1234"));

            Assert.True(viaSet.Length == 1, "Incorrect number of Via headers in set.");
            Assert.True(viaSet.TopViaHeader.Host == "192.168.1.2", "Top Via Host was incorrect.");
            Assert.True(viaSet.TopViaHeader.Port == 5065, "Top Via Port was incorrect.");
            Assert.True(viaSet.TopViaHeader.ContactAddress == "192.168.1.2:5065", "Top Via ContactAddress was incorrect.");
            Assert.True(viaSet.TopViaHeader.ReceivedFromIPAddress == "88.88.88.88", "Top Via received was incorrect.");
            Assert.True(viaSet.TopViaHeader.ReceivedFromPort == 1234, "Top Via rport was incorrect.");

            logger.LogDebug("---------------------------------------------------");
        }

        /// <summary>
        /// Tests that when the sent from socket is the same as the socket received from that the received and rport
        /// parameters are still updated.
        /// </summary>
        [Fact]
        public void AdjustReceivedCorrectAlreadyViaHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

            SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

            SIPViaSet viaSet = new SIPViaSet();
            viaSet.PushViaHeader(sipViaHeaders[0]);

            viaSet.UpateTopViaHeader(SIPSorcery.Sys.IPSocket.ParseSocketString("192.168.1.2:5065"));

            Assert.True(viaSet.Length == 1, "Incorrect number of Via headers in set.");
            Assert.True(viaSet.TopViaHeader.Host == "192.168.1.2", "Top Via Host was incorrect.");
            Assert.True(viaSet.TopViaHeader.Port == 5065, "Top Via Port was incorrect.");
            Assert.True(viaSet.TopViaHeader.ContactAddress == "192.168.1.2:5065", "Top Via ContactAddress was incorrect.");
            Assert.True(viaSet.TopViaHeader.ReceivedFromIPAddress == "192.168.1.2", "Top Via received was incorrect.");
            Assert.True(viaSet.TopViaHeader.ReceivedFromPort == 5065, "Top Via rport was incorrect.");

            logger.LogDebug("---------------------------------------------------");
        }

        /// <summary>
        /// Tests that the Require and Supported extensions get parsed correctly.
        /// </summary>
        [Fact]
        public void ParseRequireAndSupportedExtensionsTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string inviteHeaders =
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                "Content-Length: 271" + m_CRLF +
                "Require: abcd, 100rel, xyz" + m_CRLF +
                "Supported: 100rel, other" + m_CRLF;

            string[] headersCollection = Regex.Split(inviteHeaders, "\r\n");
            SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

            Assert.True(sipHeader.RequiredExtensions.Contains(SIPExtensions.Prack), "The required header extensions was missing Prack.");
            Assert.True(sipHeader.SupportedExtensions.Contains(SIPExtensions.Prack), "The supported header extensions was missing Prack.");
            Assert.True(sipHeader.UnknownRequireExtension != null, "The had unknown required header extension was not correctly set.");

            logger.LogDebug("---------------------------------------------------");
        }

        /// <summary>
        /// Tests that the RSeq header get parsed correctly.
        /// </summary>
        [Fact]
        public void ParseRSeqHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string ringResponse =
                "SIP/2.0 180 Ringing" + m_CRLF +
                "Via: SIP/2.0/UDP 0.0.0.0:6060;branch=z9hG4bK299925765f7c4defb20cef3fde520172;rport=6060;received=127.0.0.1" + m_CRLF +
                "To: <sip:127.0.0.1>" + m_CRLF +
                "From: <sip:thisis@anonymous.invalid>;tag=NEEBBCYYZR" + m_CRLF +
                "Call-ID: 9add71138b794dadbd709a2b8c0cfc89" + m_CRLF +
                "CSeq: 1 INVITE" + m_CRLF +
                "Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, REFER, REGISTER, SUBSCRIBE" + m_CRLF +
                "Supported: 100rel" + m_CRLF +
                "Content-Length: 0" + m_CRLF +
                "RSeq: 266163001" + m_CRLF + m_CRLF;

            var sipResponse = SIPResponse.ParseSIPResponse(ringResponse);

            Assert.True(266163001 == sipResponse.Header.RSeq, "The RSeq header was not parsed correctly.");

            logger.LogDebug("---------------------------------------------------");
        }

        /// <summary>
        /// Tests that the RAck header get parsed correctly.
        /// </summary>
        [Fact]
        public void ParseRAckHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string prackRequest =
                "PRACK sip:127.0.0.1 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 0.0.0.0:6060;branch=z9hG4bKed0553cb6e4b476990a34d7c98f58a14;rport" + m_CRLF +
                "To: <sip:127.0.0.1>" + m_CRLF +
                "From: <sip:thisis@anonymous.invalid>;tag=YPACUCOFBG" + m_CRLF +
                "Call-ID: c22e9dc218a1423695b1f5ef33020b84" + m_CRLF +
                "CSeq: 1 ACK" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Length: 0" + m_CRLF +
                "RAck: 423501656 1 INVITE" + m_CRLF + m_CRLF;

            var sipRequest = SIPRequest.ParseSIPRequest(prackRequest);

            Assert.True(423501656 == sipRequest.Header.RAckRSeq, "The RAck sequence header value was not parsed correctly.");
            Assert.True(1 == sipRequest.Header.RAckCSeq, "The RAck cseq header value was not parsed correctly.");
            Assert.True(SIPMethodsEnum.INVITE == sipRequest.Header.RAckCSeqMethod, "The RAck method header value was not parsed correctly.");

            logger.LogDebug("---------------------------------------------------");
        }


    }
}
