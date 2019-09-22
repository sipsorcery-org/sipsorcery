using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.UnitTests
{
    [TestClass]
    public class SIPMessageUnitTest
    {
        private static string m_CRLF = SIPConstants.CRLF;

        [TestMethod]
        public void ParseResponseUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);

            Assert.IsTrue(sipMessage != null, "The SIP message not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseResponseWithBodyUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);

            Assert.IsTrue(sipMessage != null, "The SIP message not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseResponseNoEndDoubleCRLFUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

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
                "Content-Length: 0" + m_CRLF;

            SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);

            Assert.IsTrue(sipMessage != null, "The SIP message not parsed correctly.");

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void ParseCiscoOptionsResponseUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "SIP/2.0 200 OK" + m_CRLF +
                "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK7ae332e73550dbdf2f159061651e7ed5bb88ac52, SIP/2.0/UDP 194.213.29.52:5064;branch=z9hG4bK1121681627" + m_CRLF +
                "From: <sip:natkeepalive@194.213.29.52:5064>;tag=8341482660" + m_CRLF +
                "To: <sip:user@1.2.3.4:5060>;tag=000e38e46c60ef28651381fe-201e6ab1" + m_CRLF +
                "Call-ID: 1125158248@194.213.29.52" + m_CRLF +
                "Date: Wed, 29 Nov 2006 22:31:58 GMT" + m_CRLF +
                "CSeq: 148 OPTIONS" + m_CRLF +
                "Server: CSCO/7" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Allow: OPTIONS,INVITE,BYE,CANCEL,REGISTER,ACK,NOTIFY,REFER" + m_CRLF +
                "Content-Length: 193" + m_CRLF +
                m_CRLF +
                "v=0" + m_CRLF +
                "o=Cisco-SIPUA (null) (null) IN IP4 87.198.196.121" + m_CRLF +
                "s=SIP Call" + m_CRLF +
                "c=IN IP4 87.198.196.121" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 1 RTP/AVP 18 0 8" + m_CRLF +
                "a=rtpmap:18 G729/8000" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:8 PCMA/8000" + m_CRLF +
                m_CRLF;

            SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            SIPResponse sipResponse = SIPResponse.ParseSIPResponse(sipMessage);

            Assert.IsTrue(sipMessage != null, "The SIP message not parsed correctly.");
            Assert.IsTrue(sipResponse.Header.Vias.Length == 2, "The SIP reponse did not end up with the right number of Via headers.");

            Console.WriteLine("-----------------------------------------");
        }
    }
}
