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

using System.Net;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPTransactionUnitTest
    {
        protected static readonly string m_CRLF = SIPConstants.CRLF;
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPTransactionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void CreateTransactionUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipRequestStr =
                "INVITE sip:023434211@213.200.94.182;switchtag=902888 SIP/2.0" + m_CRLF +
                "Record-Route: <sip:2.3.4.5;ftag=9307C640-33C;lr=on>" + m_CRLF +
                "Via: SIP/2.0/UDP  5.6.7.2:5060" + m_CRLF +
                "Via: SIP/2.0/UDP 1.2.3.4;branch=z9hG4bKa7ac.2bfad091.0" + m_CRLF +
                "From: \"unknown\" <sip:00.000.00.0>;tag=9307C640-33C" + m_CRLF +
                "To: <sip:0113001211@82.209.165.194>" + m_CRLF +
                "Date: Thu, 21 Feb 2008 01:46:30 GMT" + m_CRLF +
                "Call-ID: A8706191-DF5511DC-B886ED7B-395C3F7E" + m_CRLF +
                "Supported: timer,100rel" + m_CRLF +
                "Min-SE:  1800" + m_CRLF +
                "Cisco-Guid: 2825897321-3746894300-3095653755-962346878" + m_CRLF +
                "User-Agent: Cisco-SIPGateway/IOS-12.x" + m_CRLF +
                "Allow: INVITE, OPTIONS, BYE, CANCEL, ACK, PRACK, COMET, REFER, SUBSCRIBE, NOTIFY, INFO" + m_CRLF +
                "CSeq: 101 INVITE" + m_CRLF +
                "Max-Forwards: 5" + m_CRLF +
                "Timestamp: 1203558390" + m_CRLF +
                "Contact: <sip:1.2.3.4:5060>" + m_CRLF +
                "Expires: 180" + m_CRLF +
                "Allow-Events: telephone-event" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "Content-Length: 370" + m_CRLF +
                 m_CRLF +
                "v=0" + m_CRLF +
                "o=CiscoSystemsSIP-GW-UserAgent 9312 7567 IN IP4 00.00.00.0" + m_CRLF +
                "s=SIP Call" + m_CRLF +
                "c=IN IP4 00.000.00.0" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 16434 RTP/AVP 8 0 4 18 3 101" + m_CRLF +
                "c=IN IP4 00.000.00.0" + m_CRLF +
                "a=rtpmap:8 PCMA/8000" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:4 G723/8000" + m_CRLF +
                "a=fmtp:4 annexa=no" + m_CRLF +
                "a=rtpmap:18 G729/8000" + m_CRLF +
                "a=fmtp:18 annexb=no" + m_CRLF +
                "a=rtpmap:3 GSM/8000" + m_CRLF +
                "a=rtpmap:101 telepho";

            SIPRequest request = SIPRequest.ParseSIPRequest(sipRequestStr);
            SIPTransport sipTransport = new SIPTransport();
            SIPTransactionEngine transactionEngine = sipTransport.m_transactionEngine;
            SIPEndPoint dummySIPEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 1234));
            SIPTransaction transaction = new UACInviteTransaction(sipTransport, request, null);

            Assert.True(transaction.TransactionRequest.URI.ToString() == "sip:023434211@213.200.94.182;switchtag=902888", "Transaction request URI was incorrect.");
        }
    }
}
