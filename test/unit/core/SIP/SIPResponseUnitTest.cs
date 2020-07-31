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

using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPResponseUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPResponseUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static string m_CRLF = SIPConstants.CRLF;

        [Fact]
        public void ParseAsteriskTRYINGUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 100 Trying" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKD+ta2mJ+C+VV/L50aPO1lFJnrag=" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64193;branch=z9hG4bKB86FC8D2431F49E9862D1EE439C78AD8" + m_CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=3272744142" + m_CRLF +
                "To: <sip:303@bluesipd>" + m_CRLF +
                "Call-ID: FE63F90D-4339-4AD0-9D44-59F44A1935E7@192.168.1.2" + m_CRLF +
                "CSeq: 45560 INVITE" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
                "Contact: <sip:303@213.168.225.133>" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse tryingResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            Assert.True(tryingResp.Status == SIPResponseStatusCodesEnum.Trying, "The SIP response status was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseAsteriskOKUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKT36BdhXPlT5cqPFQQr81yMmZ37U=" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64216;branch=z9hG4bK7D8B6549580844AEA104BD4A837049DD" + m_CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=630217013" + m_CRLF +
                "To: <sip:303@bluesipd>;tag=as46f418e9" + m_CRLF +
                "Call-ID: 9AA41C8F-D691-49F3-B346-2538B5FD962F@192.168.1.2" + m_CRLF +
                "CSeq: 27481 INVITE" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
                "Contact: <sip:303@213.168.225.133>" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 352" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=root 24710 24712 IN IP4 213.168.225.133" + m_CRLF +
                "s=session" + m_CRLF +
                "c=IN IP4 213.168.225.133" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 18656 RTP/AVP 0 8 18 3 97 111 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:8 PCMA/8000" + m_CRLF +
                "a=rtpmap:18 G729/8000" + m_CRLF +
                "a=rtpmap:3 GSM/8000" + m_CRLF +
                "a=rtpmap:97 iLBC/8000" + m_CRLF +
                "a=rtpmap:111 G726-32/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-16" + m_CRLF +
                "a=silenceSupp:off - - - -" + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse okResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            Assert.True(okResp.Status == SIPResponseStatusCodesEnum.Ok, "The SIP response status was not parsed correctly.");
            Assert.True(okResp.Body.Length == 352, "The SIP response body length was not correct.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseOptionsBodyResponse()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg = "SIP/2.0 200 OK" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK10a1fab0" + m_CRLF +
                "From: \"Unknown\" <sip:Unknown@213.168.225.133>;tag=as18338373" + m_CRLF +
                "To: <sip:unknown@194.46.240.216>;tag=OLg-20481" + m_CRLF +
                "Call-ID: 675be0e1060ec5785593b125441ff9ac@213.168.225.133" + m_CRLF +
                "CSeq: 102 OPTIONS" + m_CRLF +
                "content-type: application/sdp" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, INFO, REFER, NOTIFY" + m_CRLF +
                "Content-Length: 217" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=0 5972727 56415 IN IP4 0.0.0.0" + m_CRLF +
                "s=SIP Call" + m_CRLF +
                "c=IN IP4 0.0.0.0" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 0 RTP/AVP 18 0 8 4 2" + m_CRLF +
                "a=rtpmap:18 G729/8000" + m_CRLF +
                "a=rtpmap:0 pcmu/8000" + m_CRLF +
                "a=rtpmap:8 pcma/8000" + m_CRLF +
                "a=rtpmap:4 g723/8000" + m_CRLF +
                "a=rtpmap:2 g726/8000" + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse okResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            Assert.True(okResp.Status == SIPResponseStatusCodesEnum.Ok, "The SIP response status was not parsed correctly.");
            Assert.True(okResp.Body.Length == 217, "The SIP response body length was not correct.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseForbiddenResponse()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg = "SIP/2.0 403 Forbidden" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.1;branch=z9hG4bKbcb78f72d221beec" + m_CRLF +
                "From: <sip:sip.blueface.ie>;tag=9a4c86234adcc297" + m_CRLF +
                "To: <sip:sip.blueface.ie>;tag=as6900b876" + m_CRLF +
                "Call-ID: 5b7207d13137dfcc@192.168.1.1" + m_CRLF +
                "CSeq: 100 REGISTER" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
                "Contact: <sip:sip.blueface.ie@213.168.225.133>" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse forbiddenResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            Assert.True(forbiddenResp.Status == SIPResponseStatusCodesEnum.Forbidden, "The SIP response status was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseOptionsResponse()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
            "SIP/2.0 200 OK" + m_CRLF +
            "Via: SIP/2.0/UDP 194.213.29.11:5060;branch=z9hG4bK330f55c874" + m_CRLF +
            "From: Anonymous <sip:194.213.29.11:5060>;tag=6859154930" + m_CRLF +
            "To: <sip:sip2@86.9.88.23:10062>;tag=0013c339acec0fe007b80bbf-30071da3" + m_CRLF +
            "Call-ID: 2501749a99424950b141bc994e48702d@194.213.29.11" + m_CRLF +
            "Date: Mon, 01 May 2006 13:47:24 GMT" + m_CRLF +
            "CSeq: 915 OPTIONS" + m_CRLF +
            "Server: CSCO/7" + m_CRLF +
            "Content-Type: application/sdp" + m_CRLF +
            "Content-Length: 247" + m_CRLF +
            "Allow: OPTIONS,INVITE,BYE,CANCEL,REGISTER,ACK,NOTIFY,REFER" + m_CRLF +
            m_CRLF +
            "v=0" + m_CRLF +
            "o=Cisco-SIPUA (null) (null) IN IP4 192.168.1.100" + m_CRLF +
            "s=SIP Call" + m_CRLF +
            "c=IN IP4 192.168.1.100" + m_CRLF +
            "t=0 0" + m_CRLF +
            "m=audio 1 RTP/AVP 0 8 18 101" + m_CRLF +
            "a=rtpmap:0 PCMU/8000" + m_CRLF +
            "a=rtpmap:8 PCMA/8000" + m_CRLF +
            "a=rtpmap:18 G729/8000" + m_CRLF +
            "a=rtpmap:101 telephone-event/8000" + m_CRLF +
            "a=fmtp:101 0-15" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse optionsResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            Assert.True(optionsResp.Status == SIPResponseStatusCodesEnum.Ok, "The SIP response status was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseMissingCSeqOptionsResponse()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + m_CRLF +
                "To: <sip:user@1.2.3.4:5060>;tag=eba877fbb8dd284bi0" + m_CRLF +
                "From: <sip:213.168.225.133:5060>;tag=5880003940" + m_CRLF +
                "Call-ID: 1192348132@213.168.225.133" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK1702000048" + m_CRLF +
                "Server: Linksys/RT31P2-2.0.10(LIc)" + m_CRLF +
                "Content-Length: 0" + m_CRLF +
                "Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, REFER" + m_CRLF +
                "Supported: x-sipura" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse optionsResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            logger.LogDebug("CSeq=" + optionsResp.Header.CSeq + ".");
            logger.LogDebug("CSeq Method=" + optionsResp.Header.CSeqMethod + ".");

            Assert.True(optionsResp.Header.CSeq == -1, "Response CSeq was incorrect.");
            Assert.True(optionsResp.Header.CSeqMethod == SIPMethodsEnum.NONE, "Response CSeq method was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseMSCOkResponse()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + m_CRLF +
                "From: Blue Face<sip:3000@127.0.0.1>;tag=as5fd53de7" + m_CRLF +
                "To: sip:xxx@127.0.0.1;tag=MTHf2-ol1Yn0" + m_CRLF +
                "Call-ID: 3e7df9d805ac596f3f091510164115e2@212.159.110.30:5061" + m_CRLF +
                "CSeq: 102 INVITE" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bKG+WGOVwLyT6vOW9s" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5061;branch=z9hG4bK09db9c73" + m_CRLF +
                "Contact: +3535xxx<sip:xxx@127.0.0.1:5061>" + m_CRLF +
                "User-Agent: MSC/VC510  Build-Date Nov  7 2005" + m_CRLF +
                "Allow: INVITE,BYE,CANCEL,OPTIONS,PRACK,NOTIFY,UPDATE,REFER" + m_CRLF +
                "Supported: timer,replaces" + m_CRLF +
                "Record-Route: <sip:213.168.225.133:5060;lr>,<sip:213.168.225.133:5061;lr>" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 182" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=xxxxxxxxx 75160 1 IN IP4 127.127.127.30" + m_CRLF +
                "s=-" + m_CRLF +
                "c=IN IP4 127.127.127.30" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 8002 RTP/AVP 0 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=ptime:20";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse okResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            logger.LogDebug("To: " + okResp.Header.To.ToString());

            Assert.True(SIPResponseStatusCodesEnum.Ok == okResp.Status, "Response should have been ok.");
            Assert.True("127.0.0.1" == okResp.Header.To.ToURI.Host, "To URI host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseMultipleContactsResponse()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.32:64226;branch=z9hG4bK-d87543-ac7a6a75bc519655-1--d87543-;rport=64226;received=89.100.104.191" + m_CRLF +
                "To: \"253989\"<sip:253989@fwd.pulver.com>;tag=cb2000b247d89723001a836145f3b053.5b6c" + m_CRLF +
                "From: \"253989\"<sip:253989@fwd.pulver.com>;tag=9812dd2f" + m_CRLF +
                "Call-ID: ODllYWY1NDJiNGMwYmQ1MjVmZmViMmEyMDViMGM0Y2Y." + m_CRLF +
                "CSeq: 2 REGISTER" + m_CRLF +
                "Date: Fri, 17 Nov 2006 17:15:35 GMT" + m_CRLF +
                "Contact: <sip:303@sip.blueface.ie>;q=0.1;expires=3298, \"Joe Bloggs\"<sip:253989@89.100.104.191:64226;rinstance=5720c5fed8cbcd34>;q=0.1;expires=3600" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse okResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            logger.LogDebug("To: " + okResp.Header.To.ToString());

            Assert.True(SIPResponseStatusCodesEnum.Ok == okResp.Status, "Response should have been ok.");
            Assert.True(okResp.Header.Contact.Count == 2, "Response should have had two contacts.");
            Assert.True(okResp.Header.Contact[0].ContactURI.ToString() == "sip:303@sip.blueface.ie", "The contact URI for the first contact header was incorrect.");
            Assert.True(okResp.Header.Contact[0].Expires == 3298, "The expires value for the first contact header was incorrect.");
            Assert.True(okResp.Header.Contact[0].Q == "0.1", "The q value for the first contact header was incorrect.");
            Assert.True(okResp.Header.Contact[1].ContactName == "Joe Bloggs", "The contact name for the first contact header was incorrect.");
            Assert.True(okResp.Header.Contact[1].ContactURI.ToString() == "sip:253989@89.100.104.191:64226;rinstance=5720c5fed8cbcd34", "The contact URI for the first contact header was incorrect.");
            Assert.True(okResp.Header.Contact[1].Expires == 3600, "The expires value for the second contact header was incorrect.");
            Assert.True(okResp.Header.Contact[1].Q == "0.1", "The q value for the second contact header was incorrect.");
            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseMultiLineRecordRouteResponse()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + m_CRLF +
                "Via: SIP/2.0/UDP 10.0.0.100:5060;rport=61540;branch=z9hG4bK40661a8b4a2d4973ae75fa52f1940383" + m_CRLF +
                "From: <sip:xxxxx0@proxy.localphone.com>;tag=1014391101" + m_CRLF +
                "To: <sip:0015619092899@proxy.localphone.com>;tag=gj-2k5-490f768a-00005cf1-00002e1aR2f0f2383.b" + m_CRLF +
                "Call-ID: 1960514b216a465fb900e2966d30e9bb" + m_CRLF +
                "CSeq: 2 INVITE" + m_CRLF +
                "Record-Route: <sip:77.75.25.44:5060;lr=on>" + m_CRLF +
                "Record-Route: <sip:77.75.25.45:5060;lr=on;ftag=1014391101>" + m_CRLF +
                "Accept: application/sdp, application/isup, application/dtmf, application/dtmf-relay,  multipart/mixed" + m_CRLF +
                "Contact: <sip:15619092899@64.152.60.78:5060>" + m_CRLF +
                "Allow: INVITE,ACK,CANCEL,BYE,REGISTER,REFER,INFO,SUBSCRIBE,NOTIFY,PRACK,UPDATE,OPTIONS" + m_CRLF +
                "Supported: timer" + m_CRLF +
                "Session-Expires: 600;refresher=uas" + m_CRLF +
                "Content-Length:  232" + m_CRLF +
                "Content-Disposition: session; handling=required" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=Sonus_UAC 4125 3983 IN IP4 64.152.60.78" + m_CRLF +
                "s=SIP Media Capabilities" + m_CRLF +
                "c=IN IP4 64.152.60.164" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 19144 RTP/AVP 0 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-15" + m_CRLF +
                "a=sendrecv" + m_CRLF +
                "a=ptime:20" + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse okResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            Assert.True(okResp.Header.RecordRoutes.Length == 2, "The wrong number of Record-Route headers were present in the parsed response.");
            Assert.True(okResp.Header.RecordRoutes.PopRoute().ToString() == "<sip:77.75.25.44:5060;lr=on>", "The top Record-Route header was incorrect.");
            SIPRoute nextRoute = okResp.Header.RecordRoutes.PopRoute();
            Assert.True(nextRoute.ToString() == "<sip:77.75.25.45:5060;lr=on;ftag=1014391101>", "The second Record-Route header was incorrect, " + nextRoute.ToString() + ".");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseMultiLineViaResponse()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + m_CRLF +
                "Via: SIP/2.0/UDP 194.213.29.100:5060;branch=z9hG4bK5feb18267ce40fb05969b4ba843681dbfc9ffcff, SIP/2.0/UDP 127.0.0.1:5061;branch=z9hG4bK52b6a8b7" + m_CRLF +
                "Record-Route: <sip:194.213.29.100:5060;lr>" + m_CRLF +
                "From: Unknown <sip:Unknown@127.0.0.1:5061>;tag=as58cbdbd1" + m_CRLF +
                "To: <sip:designersink01@10.10.49.155:5060>;tag=1144090013" + m_CRLF +
                "Call-ID: 40741a72794b85ed197e1e020bf42bb9@127.0.0.1" + m_CRLF +
                "CSeq: 102 INVITE" + m_CRLF +
                "Contact: <sip:xxxxxxx1@10.10.49.155:5060>" + m_CRLF +
                "Server: Patton SN4634 3BIS 00A0BA04469B R5.3 2009-01-15 H323 SIP BRI M5T SIP Stack/4.0.28.28" + m_CRLF +
                "Supported: replaces" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 298" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=MxSIP 0 56 IN IP4 10.10.10.155" + m_CRLF +
                "s=SIP Call" + m_CRLF +
                "c=IN IP4 10.10.10.155" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 4974 RTP/AVP 0 18 8 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:18 G729/8000" + m_CRLF +
                "a=rtpmap:8 PCMA/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:18 annexb=no" + m_CRLF +
                "a=fmtp:101 0-16" + m_CRLF +
                "a=sendrecv" + m_CRLF +
                "m=video 0 RTP/AVP 31 34 103 99";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse okResp = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            Assert.True(okResp.Header.Vias.Length == 2, "The wrong number of Record-Route headers were present in the parsed response.");
            Assert.True(okResp.Header.Vias.TopViaHeader.ContactAddress == "194.213.29.100:5060", "The top via contact address was not correctly parsed.");

            logger.LogDebug("-----------------------------------------");
        }
    }
}
