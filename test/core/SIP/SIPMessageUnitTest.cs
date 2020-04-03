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
    public class SIPMessageBufferUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPMessageBufferUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static string CRLF = SIPConstants.CRLF;

        [Fact]
        public void ParseResponseUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 100 Trying" + CRLF +
                "Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKD+ta2mJ+C+VV/L50aPO1lFJnrag=" + CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64193;branch=z9hG4bKB86FC8D2431F49E9862D1EE439C78AD8" + CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=3272744142" + CRLF +
                "To: <sip:303@bluesipd>" + CRLF +
                "Call-ID: FE63F90D-4339-4AD0-9D44-59F44A1935E7@192.168.1.2" + CRLF +
                "CSeq: 45560 INVITE" + CRLF +
                "User-Agent: asterisk" + CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + CRLF +
                "Contact: <sip:303@213.168.225.133>" + CRLF +
                "Content-Length: 0" + CRLF + CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);

            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseResponseWithBodyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + CRLF +
                "Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKT36BdhXPlT5cqPFQQr81yMmZ37U=" + CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64216;branch=z9hG4bK7D8B6549580844AEA104BD4A837049DD" + CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=630217013" + CRLF +
                "To: <sip:303@bluesipd>;tag=as46f418e9" + CRLF +
                "Call-ID: 9AA41C8F-D691-49F3-B346-2538B5FD962F@192.168.1.2" + CRLF +
                "CSeq: 27481 INVITE" + CRLF +
                "User-Agent: asterisk" + CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + CRLF +
                "Contact: <sip:303@213.168.225.133>" + CRLF +
                "Content-Type: application/sdp" + CRLF +
                "Content-Length: 352" + CRLF +
                CRLF +
                "v=0" + CRLF +
                "o=root 24710 24712 IN IP4 213.168.225.133" + CRLF +
                "s=session" + CRLF +
                "c=IN IP4 213.168.225.133" + CRLF +
                "t=0 0" + CRLF +
                "m=audio 18656 RTP/AVP 0 8 18 3 97 111 101" + CRLF +
                "a=rtpmap:0 PCMU/8000" + CRLF +
                "a=rtpmap:8 PCMA/8000" + CRLF +
                "a=rtpmap:18 G729/8000" + CRLF +
                "a=rtpmap:3 GSM/8000" + CRLF +
                "a=rtpmap:97 iLBC/8000" + CRLF +
                "a=rtpmap:111 G726-32/8000" + CRLF +
                "a=rtpmap:101 telephone-event/8000" + CRLF +
                "a=fmtp:101 0-16" + CRLF +
                "a=silenceSupp:off - - - -" + CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);

            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseResponseNoEndDoubleCRLFUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 100 Trying" + CRLF +
                "Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKD+ta2mJ+C+VV/L50aPO1lFJnrag=" + CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64193;branch=z9hG4bKB86FC8D2431F49E9862D1EE439C78AD8" + CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=3272744142" + CRLF +
                "To: <sip:303@bluesipd>" + CRLF +
                "Call-ID: FE63F90D-4339-4AD0-9D44-59F44A1935E7@192.168.1.2" + CRLF +
                "CSeq: 45560 INVITE" + CRLF +
                "User-Agent: asterisk" + CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + CRLF +
                "Contact: <sip:303@213.168.225.133>" + CRLF +
                "Content-Length: 0" + CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);

            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseCiscoOptionsResponseUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK7ae332e73550dbdf2f159061651e7ed5bb88ac52, SIP/2.0/UDP 194.213.29.52:5064;branch=z9hG4bK1121681627" + CRLF +
                "From: <sip:natkeepalive@194.213.29.52:5064>;tag=8341482660" + CRLF +
                "To: <sip:user@1.2.3.4:5060>;tag=000e38e46c60ef28651381fe-201e6ab1" + CRLF +
                "Call-ID: 1125158248@194.213.29.52" + CRLF +
                "Date: Wed, 29 Nov 2006 22:31:58 GMT" + CRLF +
                "CSeq: 148 OPTIONS" + CRLF +
                "Server: CSCO/7" + CRLF +
                "Content-Type: application/sdp" + CRLF +
                "Allow: OPTIONS,INVITE,BYE,CANCEL,REGISTER,ACK,NOTIFY,REFER" + CRLF +
                "Content-Length: 193" + CRLF +
                CRLF +
                "v=0" + CRLF +
                "o=Cisco-SIPUA (null) (null) IN IP4 87.198.196.121" + CRLF +
                "s=SIP Call" + CRLF +
                "c=IN IP4 87.198.196.121" + CRLF +
                "t=0 0" + CRLF +
                "m=audio 1 RTP/AVP 18 0 8" + CRLF +
                "a=rtpmap:18 G729/8000" + CRLF +
                "a=rtpmap:0 PCMU/8000" + CRLF +
                "a=rtpmap:8 PCMA/8000" + CRLF +
                CRLF;

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse sipResponse = SIPResponse.ParseSIPResponse(sipMessageBuffer);

            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            Assert.True(sipResponse.Header.Vias.Length == 2, "The SIP reponse did not end up with the right number of Via headers.");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that the Content-Length is correctly parsed.
        /// </summary>
        [Fact]
        public void ContentLengthParseFromSingleRequestTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string notifyRequest =
"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1" + CRLF +
"To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968" + CRLF +
"From: <sip:127.0.0.1:5003>;tag=1555449860" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 4 NOTIFY" + CRLF +
"Content-Length: 2393" + CRLF +
"Contact: <sip:127.0.0.1:5003>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Event: dialog" + CRLF + CRLF;

            byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

            int contentLength = SIPMessageBuffer.GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

            logger.LogDebug("Content-Length " + contentLength + ".");

            Assert.True(contentLength == 2393, "The content length was parsed incorrectly.");
        }

        /// <summary>
        /// Tests that the Content-Length is correctly parsed when there is extra spacing in the header.
        /// </summary>
        [Fact]
        public void ContentLengthParseFromSingleRequestExtraSpacingTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string notifyRequest =
"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1" + CRLF +
"To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968" + CRLF +
"From: <sip:127.0.0.1:5003>;tag=1555449860" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 4 NOTIFY" + CRLF +
"Content-Length      :   2393  " + CRLF +
"Contact: <sip:127.0.0.1:5003>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Event: dialog" + CRLF + CRLF;

            byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

            int contentLength = SIPMessageBuffer.GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

            logger.LogDebug("Content-Length " + contentLength + ".");

            Assert.True(contentLength == 2393, "The content length was parsed incorrectly.");
        }

        /// <summary>
        /// Tests that the Content-Length is correctly parsed when a compact header form is used.
        /// </summary>
        [Fact]
        public void ContentLengthCompactParseFromSingleRequestTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string notifyRequest =
"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1" + CRLF +
"To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968" + CRLF +
"From: <sip:127.0.0.1:5003>;tag=1555449860" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 4 NOTIFY" + CRLF +
"l: 2393" + CRLF +
"Contact: <sip:127.0.0.1:5003>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Event: dialog" + CRLF + CRLF;

            byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

            int contentLength = SIPMessageBuffer.GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

            logger.LogDebug("Content-Length " + contentLength + ".");

            Assert.True(contentLength == 2393, "The content length was parsed incorrectly.");
        }

        /// <summary>
        /// Tests that the Content-Length is correctly parsed when a compact header form is used and there is extra
        /// spacing in the header.
        /// </summary>
        [Fact]
        public void ContentLengthCompactParseFromSingleRequestExtraSpacingTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string notifyRequest =
"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1" + CRLF +
"To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968" + CRLF +
"From: <sip:127.0.0.1:5003>;tag=1555449860" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 4 NOTIFY" + CRLF +
"l   :       2393" + CRLF +
"Contact: <sip:127.0.0.1:5003>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Event: dialog" + CRLF + CRLF;

            byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

            int contentLength = SIPMessageBuffer.GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

            Assert.True(contentLength == 2393, "The content length was parsed incorrectly.");
        }

        /// <summary>
        /// Tests that a SIP request received with no Content-Length header is interpreted as having no body.
        /// </summary>
        [Fact]
        public void ParseReceiveNoContentLengthHeaderRequestTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string notifyRequest =
"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1" + CRLF +
"To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968" + CRLF +
"From: <sip:127.0.0.1:5003>;tag=1555449860" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 4 NOTIFY" + CRLF +
"Contact: <sip:127.0.0.1:5003>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Event: dialog" + CRLF + CRLF;

            byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);
            byte[] parsedNotifyBytes = SIPMessageBuffer.ParseSIPMessageFromStream(notifyRequestBytes, 0, notifyRequestBytes.Length, out _);

            Assert.True(notifyRequestBytes.Length == parsedNotifyBytes.Length, "The length of the parsed byte array was incorrect.");
        }

        /// <summary>
        /// Tests that a transmission containing a SIP request is correctly extracted.
        /// </summary>
        [Fact]
        public void ParseReceiveSingleRequestTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string notifyRequest =
"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1" + CRLF +
"To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968" + CRLF +
"From: <sip:127.0.0.1:5003>;tag=1555449860" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 4 NOTIFY" + CRLF +
"Contact: <sip:127.0.0.1:5003>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Content-Length: 2393" + CRLF +
"Event: dialog" + CRLF +
CRLF +
"<?xml version='1.0' encoding='utf-16'?>" + CRLF +
"<dialog-info xmlns:ss='sipsorcery:dialog-info' version='0' state='full' entity='sip:aaron@10.1.1.5' xmlns='urn:ietf:params:xml:ns:dialog-info'>" + CRLF +
"  <dialog id='6eab270b-b981-4734-bb6f-a4d33f77c331' call-id='c0a6182504cd4501afb8339f4218704e' local-tag='1047197926' remote-tag='56F30C5C-4B96DF700001F3A1-B67FABB0' direction='initiator'>" + CRLF +
"    <state>confirmed</state>" + CRLF +
"    <duration>1676</duration>" + CRLF +
"    <ss:bridgeid>1c47e24b-4c1b-4dae-af93-567f26a7c215</ss:bridgeid>" + CRLF +
"    <local>" + CRLF +
"      <identity>sip:switchboard@10.1.1.5</identity>" + CRLF +
"      <cseq>1</cseq>" + CRLF +
"      <ss:sdp>v=0" + CRLF +
"o=- 1268178554 1268178554 IN IP4 10.1.1.7" + CRLF +
"s=Polycom IP Phone" + CRLF +
"c=IN IP4 10.1.1.7" + CRLF +
"t=0 0" + CRLF +
"a=sendrecv" + CRLF +
"m=audio 2262 RTP/AVP 18 0 8 101" + CRLF +
"a=rtpmap:18 G729/8000" + CRLF +
"a=rtpmap:0 PCMU/8000" + CRLF +
"a=rtpmap:8 PCMA/8000" + CRLF +
"a=rtpmap:101 telephone-event/8000" + CRLF +
"</ss:sdp>" + CRLF +
"    </local>" + CRLF +
"    <remote>" + CRLF +
"      <identity>sip:music@iptel.org</identity>" + CRLF +
"      <target uri='sip:music@213.192.59.78:5080' />" + CRLF +
"      <cseq>1</cseq>" + CRLF +
"      <ss:sdp>v=0" + CRLF +
"o=sems 2134578198 1169488647 IN IP4 213.192.59.78" + CRLF +
"s=session" + CRLF +
"c=IN IP4 213.192.59.91" + CRLF +
"t=0 0" + CRLF +
"m=audio 27712 RTP/AVP 0 8 101" + CRLF +
"a=rtpmap:0 PCMU/8000" + CRLF +
"a=rtpmap:8 PCMA/8000" + CRLF +
"a=rtpmap:101 telephone-event/8000" + CRLF +
"a=fmtp:101 0-15" + CRLF +
"</ss:sdp>" + CRLF +
"    </remote>" + CRLF +
"  </dialog>" + CRLF +
"  <dialog id='b5f20497-c482-4b88-99d1-51b13f9d9167' call-id='4b20b31441064c599e63a3a1320322ae' local-tag='1468371802' remote-tag='1048320465' direction='recipient'>" + CRLF +
"    <state>confirmed</state>" + CRLF +
"    <duration>1676</duration>" + CRLF +
"    <ss:bridgeid>1c47e24b-4c1b-4dae-af93-567f26a7c215</ss:bridgeid>" + CRLF +
"    <local>" + CRLF +
"      <identity>sip:hold@10.1.1.5@10.1.1.5</identity>" + CRLF +
"      <cseq>2</cseq>" + CRLF +
"      <ss:sdp>v=0" + CRLF +
"o=sems 2134578198 1169488647 IN IP4 213.192.59.78" + CRLF +
"s=session" + CRLF +
"c=IN IP4 213.192.59.91" + CRLF +
"t=0 0" + CRLF +
"m=audio 27712 RTP/AVP 0 8 101" + CRLF +
"a=rtpmap:0 PCMU/8000" + CRLF +
"a=rtpmap:8 PCMA/8000" + CRLF +
"a=rtpmap:101 telephone-event/8000" + CRLF +
"a=fmtp:101 0-15" + CRLF +
"</ss:sdp>" + CRLF +
"    </local>" + CRLF +
"    <remote>" + CRLF +
"      <identity>sip:switchboard@10.1.1.5</identity>" + CRLF +
"      <target uri='sip:10.1.1.5:62442;transport=tcp' />" + CRLF +
"      <cseq>2</cseq>" + CRLF +
"      <ss:sdp>v=0" + CRLF +
"o=- 1268178554 1268178554 IN IP4 10.1.1.7" + CRLF +
"s=Polycom IP Phone" + CRLF +
"c=IN IP4 10.1.1.7" + CRLF +
"t=0 0" + CRLF +
"a=sendrecv" + CRLF +
"m=audio 2262 RTP/AVP 18 0 8 101" + CRLF +
"a=rtpmap:18 G729/8000" + CRLF +
"a=rtpmap:0 PCMU/8000" + CRLF +
"a=rtpmap:8 PCMA/8000" + CRLF +
"a=rtpmap:101 telephone-event/8000" + CRLF +
"</ss:sdp>" + CRLF +
"    </remote>" + CRLF +
"  </dialog>" + CRLF +
"</dialog-info>";

            byte[] notifyRequestBytes = Encoding.ASCII.GetBytes(notifyRequest);
            byte[] parsedNotifyBytes = SIPMessageBuffer.ParseSIPMessageFromStream(notifyRequestBytes, 0, notifyRequestBytes.Length, out _);

            Assert.True(notifyRequestBytes.Length == parsedNotifyBytes.Length, "The length of the parsed byte array was incorrect.");
        }

        /// <summary>
        /// Tests parsing a receive with multiple requests and responses.
        /// </summary>
        [Fact]
        public void ParseMultiRequestAndResponseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
            "SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" + CRLF +
CRLF +
"includesdp=trueSUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bK82b1f0704fc31f47b4c9e0bc383d3e0e41f2a60f;rport" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bK6d88a47e4b5c4bde9c45270ca64a1c53;rport" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport=62647;received=10.1.1.5" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" + CRLF +
"Proxy-ReceivedFrom: tcp:10.1.1.5:62647" + CRLF +
"Proxy-ReceivedOn: tcp:10.1.1.5:4506" + CRLF +
CRLF +
"includesdp=trueSIP/2.0 200 Ok" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKba4e75d7c55baef96457b36b7b570dae9a253dd8;rport=5060;received=127.0.0.1" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKc6f4c0fcd4684246abf539848017c0f0;rport" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bK17bbf15513b44e6aa88b605410148d2b;rport=62647;received=10.1.1.5" + CRLF +
"To: <sip:switchboard@10.1.1.5>;tag=2140367015" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1557768010" + CRLF +
"Call-ID: a65b4461-6929-4604-b498-256f6643e6ac" + CRLF +
"CSeq: 2 REGISTER" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>;expires=113" + CRLF +
"Date: Wed, 10 Mar 2010 00:21:14 GMT" + CRLF +
"Content-Length: 0" + CRLF +
"Server: www.sipsorcery.com" + CRLF + CRLF;

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

            byte[] request1Bytes = SIPMessageBuffer.ParseSIPMessageFromStream(testReceiveBytes, 0, testReceiveBytes.Length, out _);
            logger.LogDebug("Request1=" + UTF8Encoding.UTF8.GetString(request1Bytes));

            byte[] request2Bytes = SIPMessageBuffer.ParseSIPMessageFromStream(testReceiveBytes, request1Bytes.Length, testReceiveBytes.Length, out _);
            logger.LogDebug("Request2=" + UTF8Encoding.UTF8.GetString(request2Bytes));

            byte[] response1Bytes = SIPMessageBuffer.ParseSIPMessageFromStream(testReceiveBytes, request1Bytes.Length + request2Bytes.Length, testReceiveBytes.Length, out _);
            logger.LogDebug("Response1=" + UTF8Encoding.UTF8.GetString(response1Bytes));

            Assert.True(request1Bytes.Length + request2Bytes.Length + response1Bytes.Length == testReceiveBytes.Length, "The length of the parsed requests and responses was incorrect.");
        }

        /// <summary>
        /// Test that parsing a request with a single byte missing from its content is correctly handled.
        /// </summary>
        [Fact]
        public void ParseRequestOneByteMissingTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
            "SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" + CRLF +
"" + CRLF +
"includesdp=tru";

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);
            byte[] request1Bytes = SIPMessageBuffer.ParseSIPMessageFromStream(testReceiveBytes, 0, testReceiveBytes.Length, out _);

            Assert.True(request1Bytes == null, "The parsed bytes should have been empty.");
        }

        /// <summary>
        /// Test that parsing a request with a single byte missing from its content is correctly handled.
        /// </summary>
        [Fact]
        public void ParseRequestOneByteExtraTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
            "SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" + CRLF +
CRLF +
"includesdp=true!";

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);
            byte[] request1Bytes = SIPMessageBuffer.ParseSIPMessageFromStream(testReceiveBytes, 0, testReceiveBytes.Length, out _);

            Assert.True(request1Bytes.Length == testReceiveBytes.Length - 1, "The parsed bytes was an incorrect length.");
        }

        /// <summary>
        /// Test parsing a request where the array contains enough data but the end position of the valid data in the array is short.
        /// This will occur when using a fixed length buffer to receive data and the position of the received data is less than the length
        /// of the receive array.
        /// </summary>
        [Fact]
        public void ParseRequestBytesReadShortTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
            "SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" + CRLF +
"" + CRLF +
"include                                               ";

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);
            byte[] request1Bytes = SIPMessageBuffer.ParseSIPMessageFromStream(testReceiveBytes, 0, testReceiveBytes.Length - 100, out _);

            Assert.True(request1Bytes == null, "A request array should not have been returned.");
        }

        /// <summary>
        /// Test that parsing a request works when there are some leading bytes related to a NAT keep alive transmission.
        /// </summary>
        [Fact]
        public void ParseRequestWithLeadingNATKeepAliveBytesTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
            "    SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" + CRLF +
CRLF +
"includesdp=true";

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

            int skippedBytes = 0;
            byte[] request1Bytes = SIPMessageBuffer.ParseSIPMessageFromStream(testReceiveBytes, 0, testReceiveBytes.Length, out skippedBytes);

            logger.LogDebug(Encoding.UTF8.GetString(request1Bytes));

            Assert.True(request1Bytes != null, "The parsed bytes should have been populated.");
            Assert.True(skippedBytes == 4, "The number of skipped bytes was incorrect.");
        }

        /// <summary>
        /// Tests that processing a buffer with a SIP message and some preceding spurious characters skips the correct number of bytes.
        /// </summary>
        [Fact]
        public void TestProcessRecevieWithBytesToSkipTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
"            SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" + CRLF +
CRLF +
"includesdp=true";

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

            int bytesSkipped = 0;
            byte[] result = SIPMessageBuffer.ParseSIPMessageFromStream(testReceiveBytes, 0, testReceiveBytes.Length, out bytesSkipped);

            Assert.True(result != null, "The resultant array should not have been null.");
            Assert.True(bytesSkipped == 12, "The bytes skipped was incorrect.");
        }

        //rj2
        [Fact]
        public void IsPingUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] buffer = new byte[] { 0x0d, 0x0a };//"\r\n"
            Assert.True(SIPMessageBuffer.IsPing(buffer), "Buffer \\r\\n is not a Ping message.");

            buffer = new byte[] { 0x0d, 0x0a, 0x0d, 0x0a };//"\r\n\r\n"
            Assert.True(SIPMessageBuffer.IsPing(buffer), "Buffer \\r\\n\\r\\n is not a Ping message.");

            buffer = new byte[] { 0x6a, 0x61, 0x4b, 0x00 };//"jaK\0"
            Assert.True(SIPMessageBuffer.IsPing(buffer), "Buffer jaK\\0 is not a Ping message.");

            buffer = new byte[] { 0x70, 0x6e, 0x67 };//"png"
            Assert.True(SIPMessageBuffer.IsPing(buffer), "Buffer png is not a Ping message.");

            buffer = new byte[] { 0x00, 0x00, 0x00, 0x00 };//"\0\0\0\0"
            Assert.True(SIPMessageBuffer.IsPing(buffer), "Buffer \\0\\0\\0\\0 is not a Ping message.");

            string sipMsg =
                "SIP/2.0 100 Trying" + CRLF +
                "Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKD+ta2mJ+C+VV/L50aPO1lFJnrag=" + CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64193;branch=z9hG4bKB86FC8D2431F49E9862D1EE439C78AD8" + CRLF +
                "From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=3272744142" + CRLF +
                "To: <sip:303@bluesipd>" + CRLF +
                "Call-ID: FE63F90D-4339-4AD0-9D44-59F44A1935E7@192.168.1.2" + CRLF +
                "CSeq: 45560 INVITE" + CRLF +
                "User-Agent: asterisk" + CRLF +
                "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + CRLF +
                "Contact: <sip:303@213.168.225.133>" + CRLF +
                "Content-Length: 0" + CRLF + CRLF;

            Assert.False(SIPMessageBuffer.IsPing(Encoding.UTF8.GetBytes(sipMsg)), "The SIP message is a Ping.");

            logger.LogDebug("-----------------------------------------");
        }

    }
}
