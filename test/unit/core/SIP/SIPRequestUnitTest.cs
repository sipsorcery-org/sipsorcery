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
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPRequestUnitTest
    {
        private static string m_CRLF = SIPConstants.CRLF;

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPRequestUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ParseXtenINVITEUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
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
                m_CRLF +
                "v=0" + m_CRLF +
                "o=aaronxten 423137371 423137414 IN IP4 192.168.1.2" + m_CRLF +
                "s=X-PRO" + m_CRLF +
                "c=IN IP4 192.168.1.2" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 8004 RTP/AVP 0 8 3 97 101" + m_CRLF +
                "a=rtpmap:0 pcmu/8000" + m_CRLF +
                "a=rtpmap:8 pcma/8000" + m_CRLF +
                "a=rtpmap:3 gsm/8000" + m_CRLF +
                "a=rtpmap:97 speex/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-15" + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            if (inviteReq.Body != null)
            {
                logger.LogDebug("Body Length = " + inviteReq.Body.Length + ".");
            }
            logger.LogDebug("Body:\r\n" + inviteReq.Body + ".");

            Assert.True(inviteReq.Method == SIPMethodsEnum.INVITE, "The SIP request method was not parsed correctly.");
            //Assert.True(inviteReq.SIPMajorVersion == 2, "The SIP Major version was not parsed correctly.");
            //Assert.True(inviteReq.SIPMinorVersion == 0, "The SIP Minor version was not parsed correctly.");
            Assert.True(inviteReq.URI.User == "303", "The SIP request URI Name was not parsed correctly.");
            Assert.True(inviteReq.URI.Host == "sip.blueface.ie", "The SIP request URI Host was not parsed correctly.");
            Assert.True(inviteReq.Body != null && inviteReq.Body.Length == 271, "The SIP content body was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void MalformedContactHeaderUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
            "REGISTER sip:sip.sipsorcery.com SIP/2.0" + m_CRLF +
            "CSeq: 1142 REGISTER" + m_CRLF +
            "Via: SIP/2.0/UDP 24.183.120.253:5060;branch=z9hG4bKeab06dc6-4b03-1910-9cbd-0ceee68cfdc5;rport" + m_CRLF +
            "User-Agent: Ekiga/3.2.7" + m_CRLF +
            "Authorization: Digest username='twolmsted', realm='sipsorcery.com', nonce='14116380271465720944', uri='sip:sip.sipsorcery.com', algorithm=MD5, response='e9e190a05c482e0c287829ecf6d42207'" + m_CRLF +
            "From: <sip:twolmsted@sip.sipsorcery.com>;tag=0021d3c4-4a03-1910-9c86-0ceee68cfdc5" + m_CRLF +
            "Call-ID: 0021d3c4-4a03-1910-9c84-0ceee68cfdc5@two-PC" + m_CRLF +
            "To: <sip:twolmsted@sip.sipsorcery.com>" + m_CRLF +
            "Contact: <sip:twolmsted@24.183.120.253, sip:5060>" + m_CRLF +
            "Allow: INVITE,ACK,OPTIONS,BYE,CANCEL,SUBSCRIBE,NOTIFY,REFER,MESSAGE,INFO,PING" + m_CRLF +
            "Expires: 3600" + m_CRLF +
            "Content-Length: 0" + m_CRLF +
            "Max-Forwards: 70" + m_CRLF + m_CRLF;

            Assert.Throws<SIPValidationException>(() => SIPRequest.ParseSIPRequest(sipMsg));
        }

        [Fact]
        public void ParseAsteriskACKUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "ACK sip:303@213.168.225.133 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bK3667AD800F014BD685C2E0A8B2AB9D0F" + m_CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=396492091" + m_CRLF +
                "To: <sip:303@bluesipd>;tag=as022cbb0e" + m_CRLF +
                "Contact: <sip:bluesipd@192.168.1.2:5065>" + m_CRLF +
                "Route: <sip:213.168.225.135;lr>" + m_CRLF +
                "Call-ID: EDA17D42-034E-438B-8467-18DF1E4678A7@192.168.1.2" + m_CRLF +
                "CSeq: 39639 ACK" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest ackReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            Assert.True(ackReq.Method == SIPMethodsEnum.ACK, "The SIP request method was not parsed correctly.");
            //Assert.True(ackReq.SIPMajorVersion == 2, "The SIP Major version was not parsed correctly.");
            //Assert.True(ackReq.SIPMinorVersion == 0, "The SIP Minor version was not parsed correctly.");
            Assert.True(ackReq.URI.User == "303", "The SIP request URI was not parsed correctly.");
            Assert.True(ackReq.URI.Host == "213.168.225.133", "The SIP request URI Host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseCiscoACKUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "ACK sip:303@213.168.225.133:5061 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 89.100.92.186:5060;branch=z9hG4bK1254dba9" + m_CRLF +
                "From: dev <sip:aaron@azaclauson.dyndns.org>" + m_CRLF +
                "To: <sip:303@azaclauson.dyndns.org>;tag=as108bd3ae" + m_CRLF +
                "Call-ID: 0013c339-acec0041-61c7c61e-3eb0b7c0@89.100.92.186" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Date: Mon, 07 Aug 2006 14:57:40 GMT" + m_CRLF +
                "CSeq: 102 ACK" + m_CRLF +
                "User-Agent: Cisco-CP7960G/8.0" + m_CRLF +
                "Route: <sip:89.100.92.186:6060;lr>" + m_CRLF +
                "Proxy-Authorization: Digest username=\"aaron\",realm=\"sip.blueface.ie\",uri=\"sip:303@azaclauson.dyndns.org\",response=\"638c8fb6186fe865e80f6232cc417c3f\",nonce=\"44f121a2\",algorithm=md5" + m_CRLF +
                "Remote-Party-ID: \"dev\" <sip:aaron@89.100.92.186>;party=calling;id-type=subscriber;privacy=off;screen=yes" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest ackReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            Assert.True(ackReq.Method == SIPMethodsEnum.ACK, "The SIP request method was not parsed correctly.");
            //Assert.True(ackReq.SIPMajorVersion == 2, "The SIP Major version was not parsed correctly.");
            //Assert.True(ackReq.SIPMinorVersion == 0, "The SIP Minor version was not parsed correctly.");
            Assert.True(ackReq.URI.User == "303", "The SIP request URI was not parsed correctly.");
            Assert.True(ackReq.URI.Host == "213.168.225.133:5061", "The SIP request URI Host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseXtenByeUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "BYE sip:303@213.168.225.133 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bK7F023DE3FF8941008DE7DCE71A20CB78" + m_CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=396492091" + m_CRLF +
                "To: <sip:303@bluesipd>;tag=as022cbb0e" + m_CRLF +
                "Contact: <sip:bluesipd@192.168.1.2:5065>" + m_CRLF +
                "Route: <sip:213.168.225.135;lr>" + m_CRLF +
                "Call-ID: EDA17D42-034E-438B-8467-18DF1E4678A7@192.168.1.2" + m_CRLF +
                "CSeq: 39640 BYE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest byeReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            Assert.True(byeReq.Method == SIPMethodsEnum.BYE, "The SIP request method was not parsed correctly.");
            //Assert.True(byeReq.SIPMajorVersion == 2, "The SIP Major version was not parsed correctly.");
            //Assert.True(byeReq.SIPMinorVersion == 0, "The SIP Minor version was not parsed correctly.");
            Assert.True(byeReq.URI.User == "303", "The SIP request URI name was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseAsteriskBYEUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "BYE sip:bluesipd@192.168.1.2:5065 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK74ab714b;rport" + m_CRLF +
                "Route: <sip:bluesipd@192.168.1.2:5065>" + m_CRLF +
                "From: <sip:303@bluesipd>;tag=as6a65fae3" + m_CRLF +
                "To: bluesipd <sip:bluesipd@bluesipd:5065>;tag=1898247079" + m_CRLF +
                "Contact: <sip:303@213.168.225.133>" + m_CRLF +
                "Call-ID: 80B34165-8C89-4623-B862-40AFB1884071@192.168.1.2" + m_CRLF +
                "CSeq: 102 BYE" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest byeReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            Assert.True(byeReq.Method == SIPMethodsEnum.BYE, "The SIP request method was not parsed correctly.");
            //Assert.True(byeReq.SIPMajorVersion == 2, "The SIP Major version was not parsed correctly.");
            //Assert.True(byeReq.SIPMinorVersion == 0, "The SIP Minor version was not parsed correctly.");
            Assert.True(byeReq.URI.User == "bluesipd", "The SIP request URI Name was not parsed correctly.");
            Assert.True(byeReq.URI.Host == "192.168.1.2:5065", "The SIP request URI Host was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void TopRouteUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "BYE sip:bluesipd@192.168.1.2:5065 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK483ca249;rport" + m_CRLF +
                "Route: <sip:220.240.255.198:64300;lr>,<sip:bluesipd@192.168.1.2:5065>" + m_CRLF +
                "From: <sip:303@bluesipd>;tag=as7a10c774" + m_CRLF +
                "To: bluesipd <sip:bluesipd@bluesipd:5065>;tag=2561975990" + m_CRLF +
                "Contact: <sip:303@213.168.225.133>" + m_CRLF +
                "Call-ID: D9D08936-5455-476C-A5A2-A069D4B8DBFF@192.168.1.2" + m_CRLF +
                "CSeq: 102 BYE" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest byeReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            SIPRoute topRoute = byeReq.Header.Routes.PopRoute();
            Assert.True(topRoute.Host == "220.240.255.198:64300", "The top route was not parsed correctly, top route IP address was " + topRoute.Host + ".");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void SubscribeRequestUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SUBSCRIBE sip:0123456@127.0.0.1 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.10:15796" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "From: \"user@sip.domain.com\" <sip:user@sipdomain.com>;tag=a6cf9fe4cdee4e1cad88240403b95669;epid=1bb41c1f89" + m_CRLF +
                "To: <sip:0123456@sip.blueface.ie>;tag=as211b359e" + m_CRLF +
                "Call-ID: abebae2060d747c3ba11a0d0cde9ab0b" + m_CRLF +
                "CSeq: 81 SUBSCRIBE" + m_CRLF +
                "Contact: <sip:192.168.1.10:15796>" + m_CRLF +
                "User-Agent: RTC/1.3" + m_CRLF +
                "Event: presence" + m_CRLF +
                "Accept: application/xpidf+xml, text/xml+msrtc.pidf, application/pidf+xml" + m_CRLF +
                "Supported: com.microsoft.autoextend" + m_CRLF +
                "Supported: ms-benotify" + m_CRLF +
                "Proxy-Require: ms-benotify" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest subscribeReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(subscribeReq.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void SpaceInNamesRequestUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:Blue Face SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 127.0.0.1:1720;branch=z9hG4bKlgnUQcaywCOaPcXR" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "User-Agent: PA168S" + m_CRLF +
                "From: \"user\" <sip:user@Blue Face>;tag=81swjAV7dHG1yjd5" + m_CRLF +
                "To: \"user\" <sip:user@Blue Face>" + m_CRLF +
                "Call-ID: DHZVs1HFuMoTQ6LO@82.114.95.1" + m_CRLF +
                "CSeq: 15754 REGISTER" + m_CRLF +
                "Contact: <sip:user@127.0.0.1:1720>" + m_CRLF +
                "Expires: 30" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest registerReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(registerReq.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Error on this request is a non-numeric port on the Via header.
        /// </summary>
        [Fact]
        public void DodgyAastraRequestUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 83.70.206.42:5060port;branch=z9hG4bK77c61058c08e3a6737e4c76e6241cc3f" + m_CRLF +
                "To: <sip:100001@sip.blueface.ie:5060>" + m_CRLF +
                "From: <sip:100001@sip.blueface.ie:5060>;tag=AI5A09C508-2F0401CDFF625DD3" + m_CRLF +
                "Call-ID: AI5A09C4D6-3122395B17A0C101@192.168.14.250" + m_CRLF +
                "CSeq: 25015 REGISTER" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Expires: 3000" + m_CRLF +
                "Contact: <sip:100001@83.70.206.42:5060\n" +
                "Allow: ACK,BYE,CANCEL,INVITE,NOTIFY,OPTIONS,REFER" + m_CRLF +
                "User-Agent: Aastra Intelligate" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.Throws<SIPValidationException>(() => SIPRequest.ParseSIPRequest(sipMessageBuffer));
        }

        [Fact]
        public void NetgearInviteRequestUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:12345@sip.domain.com:5060;TCID-0 SIP/2.0" + m_CRLF +
                "From: UNAVAILABLE<sip:user@sip.domain.com:5060>;tag=c0a83dfe-13c4-26bf01-975a21d0-2d8a" + m_CRLF +
                "To: <sip:1234@sipdomain.com:5060>" + m_CRLF +
                "Call-ID: 94b6e3f8-c0a83dfe-13c4-26bf01-975a21ce-52c@sip.domain.com" + m_CRLF +
                "CSeq: 1 INVITE" + m_CRLF +
                "Via: SIP/2.0/UDP 86.9.84.23:5060;branch=z9hG4bK-26bf01-975a21d0-1ffb" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "User-Agent: TA612V-V1.2_54" + m_CRLF +
                "Supported: timer,replaces" + m_CRLF +
                "Contact: <sip:user@88.8.88.88:5060>" + m_CRLF +
                "Content-Type: application/SDP" + m_CRLF +
                "Content-Length: 386" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=b0000 613 888 IN IP4 88.8.88.88" + m_CRLF +
                "s=SIP Call" + m_CRLF +
                "c=IN IP4 88.8.88.88" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 10000 RTP/AVP 0 101 18 100 101 2 103 8" + m_CRLF +
                "a=fmtp:101 0-15" + m_CRLF +
                "a=fmtp:18 annexb=no" + m_CRLF +
                "a=sendrecv" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=rtpmap:18 G729/8000" + m_CRLF +
                "a=rtpmap:100 G726-16/8000" + m_CRLF +
                "a=rtpmap:101 G726-24/8000" + m_CRLF +
                "a=rtpmap:2 G726-32/8000" + m_CRLF +
                "a=rtpmap:103 G726-40/8000" + m_CRLF +
                "a=rtpmap:8 PCMA/8000";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(inviteReq.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void RTCRegisterRequestUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.10:15796" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "From: <sip:user@sip.blueface.ie>;tag=1a52c38c46e3439c8b4fe8a58f5ae834;epid=1bb41c1f89" + m_CRLF +
                "To: <sip:user@sip.blueface.ie>" + m_CRLF +
                "Call-ID: aeb2c6c905a84610a54de60bb6ef476c" + m_CRLF +
                "CSeq: 417 REGISTER" + m_CRLF +
                "Contact: <sip:192.168.1.10:15796>;methods=\"INVITE, MESSAGE, INFO, SUBSCRIBE, OPTIONS, BYE, CANCEL, NOTIFY, ACK, REFER, BENOTIFY\"" + m_CRLF +
                "User-Agent: RTC/1.3.5470 (Messenger 5.1.0680)" + m_CRLF +
                "Event: registration" + m_CRLF +
                "Allow-Events: presence" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest registerReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void CiscoRegisterRequest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:194.213.29.11 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 86.9.88.23:5060;branch=z9hG4bK15dbeda2" + m_CRLF +
                "From: <sip:sip2@194.213.29.11;user=phone>" + m_CRLF +
                "To: <sip:sip2@194.213.29.11;user=phone>" + m_CRLF +
                "Call-ID: 0013c339-acec0005-7488d654-42a83bd0@192.168.1.100" + m_CRLF +
                "Date: Sat, 22 Apr 2006 00:47:31 GMT" + m_CRLF +
                "CSeq: 10389 REGISTER" + m_CRLF +
                "User-Agent: CSCO/7" + m_CRLF +
                "Contact: <sip:sip2@86.9.88.23:5060;user=phone>" + m_CRLF +
                "Content-Length: 0" + m_CRLF +
                "Expires: 3600" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest registerReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);
        }

        [Fact]
        public void AuthenticatedRegisterRequestUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 86.9.88.23:10060;branch=z9hG4bK1DFDB76492E74691A3DF68AC672DAA7C" + m_CRLF +
                "From: Aaron <sip:aaronxten@blueface.ie>;tag=2090971807" + m_CRLF +
                "To: Aaron <sip:aaronxten@blueface.ie>" + m_CRLF +
                "Call-ID: 19CBFF5EB6CB4668A29BEF0C3DC49910@blueface.ie" + m_CRLF +
                "CSeq: 24291 REGISTER" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Contact: \"Aaron\" <sip:aaronxten@86.9.88.23:10060>" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                "Expires: 1800" + m_CRLF +
                "Authorization: Digest realm=\"sip.blueface.ie\",nonce=\"1694683214\",username=\"aaronxten\",response=\"85f2089ac958e69ce4d74f5ae72a9a5f\",uri=\"sip:blueface.ie\"" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest registerReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(registerReq.ToString());

            SIPAuthenticationHeader authHeader = registerReq.Header.AuthenticationHeader;

            Assert.True(authHeader != null, "The Authorization header was not correctly extracted from the SIP Register Request.");
            Assert.True(authHeader.SIPDigest.Nonce == "1694683214", "The Authorization header nonce was not correctly extracted from the SIP Register Request, header nonce = " + authHeader.SIPDigest.Nonce + ".");
            Assert.True(authHeader.SIPDigest.Realm == "sip.blueface.ie", "The Authorization header realm was not correctly extracted from the SIP Register Request.");
            Assert.True(authHeader.SIPDigest.Username == "aaronxten", "The Authorization username nonce was not correctly extracted from the SIP Register Request, header username = " + authHeader.SIPDigest.Username + ".");
            Assert.True(authHeader.SIPDigest.URI == "sip:blueface.ie", "The Authorization header URI was not correctly extracted from the SIP Register Request.");
            Assert.True(authHeader.SIPDigest.Response == "85f2089ac958e69ce4d74f5ae72a9a5f", "The Authorization header response was not correctly extracted from the SIP Register Request.");
        }

        [Fact]
        public void MicrosoftMessengerRegisterRequestUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:aaronmsn SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.31:16879" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "From: <sip:aaronmsn>;tag=27359cb6dcdb4b8e9570dd9fc4b09c14;epid=5649ab5588" + m_CRLF +
                "To: <sip:aaronmsn>" + m_CRLF +
                "Call-ID: 19b7a4c8c6d647b19afe031df5e91332@192.168.1.31" + m_CRLF +
                "CSeq: 1 REGISTER" + m_CRLF +
                "Contact: <sip:192.168.1.31:16879>;methods=\"INVITE, MESSAGE, INFO, SUBSCRIBE, OPTIONS, BYE, CANCEL, NOTIFY, ACK, REFER\"" + m_CRLF +
                "User-Agent: RTC/1.2.4949" + m_CRLF +
                "Event: registration" + m_CRLF +
                "Allow-Events: presence" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest registerReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(registerReq.ToString());
        }

        [Fact]
        public void CreateBranchIdUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            string branchId = CallProperties.CreateBranchId();

            logger.LogDebug("branchid=" + branchId);

            int length = branchId.Length - SIPConstants.SIP_BRANCH_MAGICCOOKIE.Length;
            logger.LogDebug("length=" + length);

            Assert.True(branchId != null, "The branchid was not created correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        /*[Test]
        public void LoopDetectUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
               
            string sipMsg = 
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            string branchId = inviteReq.CreateBranchId();
				
            SIPViaHeader requestVia = new SIPViaHeader("192.168.1.2", 5065, branchId);
                
            inviteReq.Header.Via.PushViaHeader(requestVia);
				
            Assert.True(inviteReq.IsLoop("192.168.1.2", 5065, branchId), "The SIP request was not correctly detected as a loop.");

            logger.LogDebug("-----------------------------------------");
        }*/

        [Fact]
        public void LooseRouteForProxyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "Route: <sip:82.195.148.216:5062;lr>,<sip:89.100.92.186:45270;lr>" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            MockSIPChannel mockSIPChannel = new MockSIPChannel(IPSocket.ParseSocketString("82.195.148.216:5062"));
            SIPTransport mockSIPTransport = new SIPTransport(true);
            mockSIPTransport.AddSIPChannel(mockSIPChannel);

            mockSIPTransport.PreProcessRouteInfo(inviteReq);

            Assert.True(inviteReq.URI.ToString() == "sip:303@sip.blueface.ie", "The request URI was incorrectly modified.");
            Assert.True(inviteReq.Header.Routes.TopRoute.ToString() == "<sip:89.100.92.186:45270;lr>", "The request route information was not correctly preprocessed.");
            Assert.True(inviteReq.Header.Routes.Length == 1, "The route set was an incorrect length.");
        }

        [Fact]
        public void LooseRouteForProxyMultipleContactsUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "Route: <sip:82.195.148.216:5062;lr>,<sip:89.100.92.186:45270;lr>" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            SIPTransport mockSIPTransport = new SIPTransport(true);
            mockSIPTransport.AddSIPChannel(new MockSIPChannel(IPSocket.ParseSocketString("82.195.148.216:5061")));
            mockSIPTransport.AddSIPChannel(new MockSIPChannel(IPSocket.ParseSocketString("82.195.148.216:5062")));

            mockSIPTransport.PreProcessRouteInfo(inviteReq);

            Assert.True(inviteReq.URI.ToString() == "sip:303@sip.blueface.ie", "The request URI was incorrectly modified.");
            Assert.True(inviteReq.Header.Routes.TopRoute.ToString() == "<sip:89.100.92.186:45270;lr>", "The request route information was not correctly preprocessed.");
            Assert.True(inviteReq.Header.Routes.Length == 1, "The route set was an incorrect length.");
        }

        [Fact]
        public void LooseRouteNotForProxyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "Route: <sip:82.195.148.216:5062;lr>,<sip:89.100.92.186:45270;lr>" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            SIPTransport mockSIPTransport = new SIPTransport(true);
            mockSIPTransport.AddSIPChannel(new MockSIPChannel(IPSocket.ParseSocketString("10.10.10.10:5060")));

            mockSIPTransport.PreProcessRouteInfo(inviteReq);

            Assert.True(inviteReq.URI.ToString() == "sip:303@sip.blueface.ie", "The request URI was incorrectly modified.");
            Assert.True(inviteReq.Header.Routes.TopRoute.ToString() == "<sip:82.195.148.216:5062;lr>", "The request route information was not correctly preprocessed.");
        }

        [Fact]
        public void StrictRoutePriorToProxyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:82.195.148.216:5062;lr SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "Route: <sip:89.100.92.186:45270;lr>,<sip:303@sip.blueface.ie>" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            SIPTransport mockSIPTransport = new SIPTransport(true);
            mockSIPTransport.AddSIPChannel(new MockSIPChannel(IPSocket.ParseSocketString("82.195.148.216:5062")));

            mockSIPTransport.PreProcessRouteInfo(inviteReq);

            logger.LogDebug(inviteReq.ToString());
            logger.LogDebug("Next Route=" + inviteReq.Header.Routes.TopRoute.ToString());
            logger.LogDebug("Request URI=" + inviteReq.URI.ToString());

            Assert.True(inviteReq.Header.Routes.TopRoute.ToString() == "<sip:89.100.92.186:45270;lr>", "Top route was not correct.");
            Assert.True(inviteReq.URI.ToString() == "sip:303@sip.blueface.ie", "The request URI was incorrectly adjusted.");
            Assert.True(inviteReq.Header.Routes.Length == 1, "The route set was not correct.");
        }

        [Fact]
        public void StrictRouteAfterProxyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "Route: <sip:82.195.148.216:5062;lr>,<sip:10.10.10.10>,<sip:89.100.92.186:45270;lr>" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            SIPTransport mockSIPTransport = new SIPTransport(true);
            mockSIPTransport.AddSIPChannel(new MockSIPChannel(IPSocket.ParseSocketString("82.195.148.216:5062")));

            mockSIPTransport.PreProcessRouteInfo(inviteReq);

            logger.LogDebug("Next Route=" + inviteReq.Header.Routes.TopRoute.ToString());
            logger.LogDebug("Request URI=" + inviteReq.URI.ToString());

            Assert.True(inviteReq.Header.Routes.TopRoute.ToString() == "<sip:89.100.92.186:45270;lr>", "Top route was not correctly recognised.");
            Assert.True(inviteReq.Header.Routes.BottomRoute.ToString() == "<sip:303@sip.blueface.ie>", "Bottom route was not correctly placed.");
            Assert.True(inviteReq.URI.ToString() == "sip:10.10.10.10", "The request URI was not correctly adjusted.");
            Assert.True(inviteReq.Header.Routes.Length == 2, "The route set was not correct.");
        }

        [Fact(Skip = "This SIP stack will not put hostnames into a Route header in order to avoid unnecessary DNS lookups.")]
        //[Ignore()]
        public void LooseRouteForProxyHostnameUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "Route: <sip:sip.blueface.ie;lr>,<sip:89.100.92.186:45270;lr>" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: X-PRO release 1103v" + m_CRLF +
                m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            SIPTransport mockSIPTransport = new SIPTransport(true);
            mockSIPTransport.AddSIPChannel(new MockSIPChannel(IPSocket.ParseSocketString("194.213.29.100:5060")));

            mockSIPTransport.PreProcessRouteInfo(inviteReq);

            Assert.True(inviteReq.URI.ToString() == "sip:303@sip.blueface.ie", "The request URI was incorrectly modified.");
            Assert.True(inviteReq.Header.Routes.TopRoute.ToString() == "<sip:89.100.92.186:45270;lr>", "The request route information was not correctly preprocessed.");
            Assert.True(inviteReq.Header.Routes.Length == 1, "The route set was an incorrect length.");
        }

        [Fact]
        public void SpuriousStartCharsInResponseUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // This is an example of a malformed response received in the wild. It matches the bnf format for a request,
            // if the format of the SIP URI is not taken into account.
            string sipMsg =
                "16394SIP/2.0 200 OK" + m_CRLF +
                "To: <sip:user@83.70.216.94:5056>;tag=56314300b3ccd13fi0" + m_CRLF +
                "From: <sip:natkeepalive@194.213.29.52:5064>;tag=7816855980" + m_CRLF +
                "Call-ID: 1652975648@194.213.29.52" + m_CRLF +
                "CSeq: 685 OPTIONS" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK46427189218ce28213adfb77e9df73b8ba6f6f0b" + m_CRLF +
                "Via: SIP/2.0/UDP 194.213.29.52:5064;branch=z9hG4bK1531800555" + m_CRLF +
                "Server: Linksys/PAP2-3.1.3(LS)" + m_CRLF +
                "Content-Length: 5" + m_CRLF +
                "Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, REFER" + m_CRLF +
                "Supported: x-sipura" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.Throws<SIPValidationException>(() => SIPRequest.ParseSIPRequest(sipMessageBuffer));

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void RegisterZeroExpiryUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:213.200.94.181 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.32:10254;branch=z9hG4bK-d87543-eb7c9f44883c5955-1--d87543-;rport;received=89.100.104.191" + m_CRLF +
                "To: aaronxten <sip:aaronxten@213.200.94.181>" + m_CRLF +
                "From: aaronxten <sip:aaronxten@213.200.94.181>;tag=774d2561" + m_CRLF +
                "Call-ID: MTBhNGZjZmQ2OTc3MWU5MTZjNWUxMDYxOTk1MjdmYzk." + m_CRLF +
                "CSeq: 2 REGISTER" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.32:10254;rinstance=6d2bbd8014ca7a76>;expires=0" + m_CRLF +
                "Max-Forwards: 69" + m_CRLF +
                "User-Agent: X-Lite release 1006e stamp 34025" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY, MESSAGE, SUBSCRIBE, INFO" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest registerReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(registerReq.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AvayaInviteUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:194.213.29.100:5060 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 10.1.1.241;branch=z9hG4bK94fc63626" + m_CRLF +
                "To: UNKNOWN <sip:8219000@sip.194.213.29.100>" + m_CRLF +
                "From: 'Joe Bloggs' <sip:ei9gz@blueface.ie>;tag=cc16d34c122e5fe" + m_CRLF +
                "Call-ID: 61d0b3a80f5727a9be56ac739e8b0581@blueface.ie" + m_CRLF +
                "CSeq: 2009546910 INVITE" + m_CRLF +
                "Contact: 'Val Gavin' <sip:ei9gz@10.1.1.241>" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Route: <sip:8219522@sip.194.213.29.100>" + m_CRLF +    // Strict Route header (this header is actually a fault but it ends up being a strict route).
                "User-Agent: NeuralX MxSF/v3.2.6.26" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 318" + m_CRLF +
                "P-Asserted-Identity: 'Joe Bloggs' <sip:9@blueface.ie>" + m_CRLF +
                "Allow: INVITE" + m_CRLF +
                "Allow: CANCEL" + m_CRLF +
                "Allow: OPTIONS" + m_CRLF +
                "Allow: BYE" + m_CRLF +
                "Allow: REFER" + m_CRLF +
                "Allow: INFO" + m_CRLF +
                "Allow: UPDATE" + m_CRLF +
                "Supported: replaces" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=xxxxx 1174909600 1174909601 IN IP4 10.1.1.241" + m_CRLF +
                "s=-" + m_CRLF +
                "c=IN IP4 10.1.1.241" + m_CRLF +
                "t=0 0" + m_CRLF +
                "a=sendrecv" + m_CRLF +
                "m=audio 20026 RTP/AVP 8 0 18 101" + m_CRLF +
                "a=rtpmap:8 PCMA/8000" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:18 G729/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:18 annexb=no" + m_CRLF +
                "a=fmtp:101 0-15" + m_CRLF +
                "a=ptime:20" + m_CRLF +
                "a=rtcp:20027 IN IP4 10.1.1.241";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            SIPTransport mockSIPTransport = new SIPTransport(true);
            mockSIPTransport.AddSIPChannel(new MockSIPChannel(IPSocket.ParseSocketString("194.213.29.100:5060")));

            mockSIPTransport.PreProcessRouteInfo(inviteReq);

            logger.LogDebug(inviteReq.ToString());

            Assert.True(inviteReq.URI.ToString() == "sip:8219522@sip.194.213.29.100", "The request URI was not updated to the strict route.");
            Assert.True(inviteReq.Header.Routes.TopRoute.URI.ToString() == "sip:194.213.29.100:5060", "The route set was not correctly updated.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void LocalphoneInviteUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:shebeen@sip.mysipswitch.com;switchtag=134308 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 77.75.25.45;branch=z9hG4bK048.7b51ac95.0" + m_CRLF +
                "Via: SIP/2.0/UDP 213.166.9.4:5060;branch=76c5c145958c18a58a4b8f83c82476d8;rport=5060" + m_CRLF +
                "To: \"02031296073\" <sip:02031296073@213.166.9.4>" + m_CRLF +
                "From: \"Anonymous\" <sip:213.166.9.4:5060>;tag=3433217327-893254" + m_CRLF +
                "Call-ID: 334573-3433217327-893210@interface-e1000g0" + m_CRLF +
                "CSeq: 1 INVITE" + m_CRLF +
                "Contact: <sip:213.166.9.4:5060>" + m_CRLF +
                "Max-Forwards: 12" + m_CRLF +
                "Record-Route: <sip:77.75.25.45;lr=on;ftag=3433217327-893254>" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 162" + m_CRLF +
                "Session-Expires: 3600;Refresher=uac" + m_CRLF +
                "Supported: timer" + m_CRLF +
                "P-Asserted-Identity:<sip:unknown@213.166.9.4>" + m_CRLF +
                "Privacy: none" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=NexTone-MSW 1234 0 IN IP4 213.166.9.6" + m_CRLF +
                "s=sip call" + m_CRLF +
                "c=IN IP4 213.166.9.6" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 55694 RTP/AVP 0 8 18" + m_CRLF +
                "a=rtpmap:18 G729/8000" + m_CRLF +
                "a=fmtp:18 annexb=yes";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(inviteReq.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void MultipleRouteHeadersUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "BYE sip:bluesipd@192.168.1.2:5065 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK483ca249;rport" + m_CRLF +
                "Route: <sip:220.240.255.198:64300;lr>,<sip:bluesipd@192.168.1.2:5065>" + m_CRLF +
                "Route: <sip:21.10.21.2;lr>,<sip:bluesipd@90.91.82.12>,<sip:bluesipd@90.91.82.12>" + m_CRLF +
                "Route: <sip:2.3.22.2;lr>,<sip:bluesipd@90.91.82.12>" + m_CRLF +
                "From: <sip:303@bluesipd>;tag=as7a10c774" + m_CRLF +
                "To: bluesipd <sip:bluesipd@bluesipd:5065>;tag=2561975990" + m_CRLF +
                "Contact: <sip:303@213.168.225.133>" + m_CRLF +
                "Call-ID: D9D08936-5455-476C-A5A2-A069D4B8DBFF@192.168.1.2" + m_CRLF +
                "CSeq: 102 BYE" + m_CRLF +
                "User-Agent: asterisk" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest byeReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            Assert.True(byeReq.Header.Routes.Length == 7, "The wrong number of Route headers were parsed.");
            SIPRoute nextRoute = byeReq.Header.Routes.PopRoute();
            Assert.True(nextRoute.Host == "220.240.255.198:64300", "The first route was incorrect.");
            nextRoute = byeReq.Header.Routes.PopRoute();
            Assert.True(nextRoute.Host == "192.168.1.2:5065", "The second route was incorrect.");
            nextRoute = byeReq.Header.Routes.PopRoute();
            Assert.True(nextRoute.Host == "21.10.21.2", "The third route was incorrect.");
            nextRoute = byeReq.Header.Routes.PopRoute();
            Assert.True(nextRoute.Host == "90.91.82.12", "The fourth route was incorrect.");
            nextRoute = byeReq.Header.Routes.PopRoute();
            Assert.True(nextRoute.Host == "90.91.82.12", "The fifth route was incorrect.");
            nextRoute = byeReq.Header.Routes.PopRoute();
            Assert.True(nextRoute.Host == "2.3.22.2", "The sixth route was incorrect.");
            nextRoute = byeReq.Header.Routes.PopRoute();
            Assert.True(nextRoute.Host == "90.91.82.12", "The seventh route was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void SinologicInvalidInviteUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "INVITE sip:0447507533@69.59.142.213 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP checksecuritytester.sinologic.net:5060;branch=z9hG4bK34323636;rport" + m_CRLF +
                "From: \"0447507533\" <sip:0447507533@checksecuritytester.sinologic.net>;tag=as55c3de87" + m_CRLF +
                "To: <sip:0447507533@69.59.142.213>" + m_CRLF +
                "Contact: <sip:0447507533@checksecuritytester.sinologic.net>" + m_CRLF +
                "Call-ID: 5c4df8b003fe7b900fa3cfaf7f0e4d21@69.59.142.213" + m_CRLF +
                "CSeq: 102 INVITE" + m_CRLF +
                "User-Agent: SIP Security Tester" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Date: Fri, 1 Jul 2011 12:54:11 GMT" + m_CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, SUBSCRIBE, NOTIFY, INFO" + m_CRLF +
                "Supported: replaces" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 232" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(inviteReq.ToString());
        }

        [Fact]
        public void ParseACKWithDomainNameInViaTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "ACK sip:67.222.131.147 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 1234.s1.minisipserver.com;branch=z9hG4bK-MSS-508bd56b768a2403;rport" + m_CRLF +
                "To: \"300\" <sip:300@sipsorcery.com>;tag=XPGJDZQUZD" + m_CRLF +
                "From: \"user\" <sip:aaronminisip@sipsorcery.com>;tag=c76ba46e" + m_CRLF +
                "Call-ID: A0D069D5C4FF31D9A627957CMSS33C800AB." + m_CRLF +
                "CSeq: 2 ACK" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "User-Agent: miniSIPServer Cloud" + m_CRLF +
                "Proxy-Authorization: Digest username=\"user\",realm=\"sipsorcery.com\",nonce=\"13046264131868153844\",uri=\"sip:300@sipsorcery.com\",response=\"7b3b69c82a8ca80e5d6c58d4be652a79\",opaque=\"\",algorithm=MD5" + m_CRLF +
                "Content-Length: 0" + m_CRLF + m_CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPRequest ackReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            logger.LogDebug(ackReq.ToString());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a SIP request is serialised to a string correctly.
        /// </summary>
        [Fact]
        public void ToStringSerialisationTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = new SIPURI("dummy", "dummy", null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp);
            SIPRequest registerRequest = SIPRequest.GetRequest(SIPMethodsEnum.REGISTER, uri);
            registerRequest.Header.Vias.TopViaHeader.Branch = "z9hG4bKb4313133e5fe42da87034c2b22ac2aab";
            registerRequest.Header.From.FromTag = "OLBDXPNBTJ";
            registerRequest.Header.CallId = "2b79ac74010c494aa1eaaacb9819d77d";

            logger.LogDebug(registerRequest.ToString());

            string expectedSerialisation = "REGISTER sip:dummy@dummy SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 0.0.0.0;branch=z9hG4bKb4313133e5fe42da87034c2b22ac2aab;rport" + m_CRLF +
"To: <sip:dummy@dummy>" + m_CRLF +
"From: <sip:0.0.0.0:0>;tag=OLBDXPNBTJ" + m_CRLF +
"Call-ID: 2b79ac74010c494aa1eaaacb9819d77d" + m_CRLF +
"CSeq: 1 REGISTER" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE" + m_CRLF +
"Content-Length: 0" + m_CRLF +
"" + m_CRLF +
"";
            Assert.Equal(expectedSerialisation, registerRequest.ToString());
        }

        /// <summary>
        /// Tests that a copied SIP request is serialised to a string correctly.
        /// </summary>
        [Fact]
        public void CopyToStringSerialisationTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI uri = new SIPURI("dummy", "dummy", null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp);
            SIPRequest registerRequest = SIPRequest.GetRequest(SIPMethodsEnum.REGISTER, uri);
            registerRequest.Header.Vias.TopViaHeader.Branch = "z9hG4bKb4313133e5fe42da87034c2b22ac2aab";
            registerRequest.Header.From.FromTag = "OLBDXPNBTJ";
            registerRequest.Header.CallId = "2b79ac74010c494aa1eaaacb9819d77d";

            SIPRequest copy = registerRequest.Copy();

            logger.LogDebug(copy.ToString());

            string expectedSerialisation = "REGISTER sip:dummy@dummy SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 0.0.0.0;branch=z9hG4bKb4313133e5fe42da87034c2b22ac2aab;rport" + m_CRLF +
"To: <sip:dummy@dummy>" + m_CRLF +
"From: <sip:0.0.0.0:0>;tag=OLBDXPNBTJ" + m_CRLF +
"Call-ID: 2b79ac74010c494aa1eaaacb9819d77d" + m_CRLF +
"CSeq: 1 REGISTER" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE" + m_CRLF +
"Content-Length: 0" + m_CRLF +
"" + m_CRLF +
"";
            Assert.Equal(expectedSerialisation, copy.ToString());
        }

        /// <summary>
        /// Tests that a parsed SIP request is serialised to a string correctly.
        /// </summary>
        [Fact]
        public void ParsedToStringSerialisationTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipRequestStr = "REGISTER sip:dummy@dummy SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 0.0.0.0;branch=z9hG4bKb4313133e5fe42da87034c2b22ac2aab;rport" + m_CRLF +
"To: <sip:dummy@dummy>" + m_CRLF +
"From: <sip:0.0.0.0:0>;tag=OLBDXPNBTJ" + m_CRLF +
"Call-ID: 2b79ac74010c494aa1eaaacb9819d77d" + m_CRLF +
"CSeq: 1 REGISTER" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE" + m_CRLF +
"Content-Length: 0" + m_CRLF +
"" + m_CRLF +
"";
            SIPRequest registerRequest = SIPRequest.ParseSIPRequest(sipRequestStr);

            logger.LogDebug(registerRequest.ToString());

            string expectedSerialisation = "REGISTER sip:dummy@dummy SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 0.0.0.0;branch=z9hG4bKb4313133e5fe42da87034c2b22ac2aab;rport" + m_CRLF +
"To: <sip:dummy@dummy>" + m_CRLF +
"From: <sip:0.0.0.0:0>;tag=OLBDXPNBTJ" + m_CRLF +
"Call-ID: 2b79ac74010c494aa1eaaacb9819d77d" + m_CRLF +
"CSeq: 1 REGISTER" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE" + m_CRLF +
"Content-Length: 0" + m_CRLF +
"" + m_CRLF +
"";
            Assert.Equal(expectedSerialisation, registerRequest.ToString());
        }
    }
}
