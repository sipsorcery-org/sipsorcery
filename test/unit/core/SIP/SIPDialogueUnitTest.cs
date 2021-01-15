//-----------------------------------------------------------------------------
// Filename: SIPDialogueUnitTest.cs
//
// Description: Unit tests for the SIPDialogue class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 15 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPDialogueUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPDialogueUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Checks that a SIP dialgoue is correctly create from a UAS transaction.
        /// </summary>
        [Fact]
        public async Task CreateDialogueFromUasTxUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string inviteReqStr = 
@"INVITE sip:dummy@127.0.0.1:12014 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Contact: <sip:127.0.0.1:1234>
Max-Forwards: 70
User-Agent: unittest
Content-Length: 5
Content-Type: application/sdp

dummy";
            var sipReqBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummyEP, dummyEP);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipReqBuffer);

            string okRespStr = 
@"SIP/2.0 200 OK
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Content-Length: 5
Content-Type: application/sdp

dummy";

            var sipRespBuffer = SIPMessageBuffer.ParseSIPMessage(okRespStr, dummyEP, dummyEP);
            SIPResponse okResponse = SIPResponse.ParseSIPResponse(sipRespBuffer);

            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));
            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            await uasTx.GotResponse(dummyEP, dummyEP, okResponse);

            var dialogue = new SIPDialogue(uasTx);

            Assert.NotNull(dialogue);
            Assert.Equal(SIPURI.ParseSIPURI("sip:127.0.0.1:1234"), dialogue.RemoteTarget);
            Assert.Equal(dummyEP, dialogue.RemoteSIPEndPoint);

            logger.LogDebug("---------------------------------------------------");
        }

        /// <summary>
        /// Checks that the remote target for a SIP dialgoue is correctly set for a UAS transaction
        /// when the ProxyReceived header is set.
        /// </summary>
        [Fact]
        public async Task CheckRemoteSocketProxyReceivedUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string inviteReqStr =
@"INVITE sip:dummy@127.0.0.1:12014 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Contact: <sip:127.0.0.1:1234>
Max-Forwards: 70
User-Agent: unittest
Content-Length: 5
Content-Type: application/sdp
Proxy-ReceivedFrom: udp:192.168.0.50:5080

dummy";
            var sipReqBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummyEP, dummyEP);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipReqBuffer);

            string okRespStr =
@"SIP/2.0 200 OK
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Content-Length: 5
Content-Type: application/sdp

dummy";

            var sipRespBuffer = SIPMessageBuffer.ParseSIPMessage(okRespStr, dummyEP, dummyEP);
            SIPResponse okResponse = SIPResponse.ParseSIPResponse(sipRespBuffer);

            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));
            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            await uasTx.GotResponse(dummyEP, dummyEP, okResponse);

            var dialogue = new SIPDialogue(uasTx);

            Assert.NotNull(dialogue);
            Assert.Equal(SIPURI.ParseSIPURI("sip:127.0.0.1:1234"), dialogue.RemoteTarget);
            Assert.Equal(SIPEndPoint.ParseSIPEndPoint("udp:192.168.0.50:5080"), dialogue.RemoteSIPEndPoint);

            logger.LogDebug("---------------------------------------------------");
        }

        /// <summary>
        /// Checks that a SIP dialgoue is correctly create from a UAC transaction.
        /// </summary>
        [Fact]
        public async Task CreateDialogueFromUacTxUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string inviteReqStr =
@"INVITE sip:dummy@127.0.0.1:12014 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Contact: <sip:127.0.0.1:1234>
Max-Forwards: 70
User-Agent: unittest
Content-Length: 5
Content-Type: application/sdp

dummy";
            var sipReqBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummyEP, dummyEP);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipReqBuffer);

            string okRespStr =
@"SIP/2.0 200 OK
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
Contact: <sip:127.0.0.1:1234>
CSeq: 1 INVITE
Content-Length: 5
Content-Type: application/sdp

dummy";

            var sipRespBuffer = SIPMessageBuffer.ParseSIPMessage(okRespStr, dummyEP, dummyEP);
            SIPResponse okResponse = SIPResponse.ParseSIPResponse(sipRespBuffer);

            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));

            UACInviteTransaction uacTx = new UACInviteTransaction(transport, inviteReq, null);
            await uacTx.GotResponse(dummyEP, dummyEP, okResponse);

            var dialogue = new SIPDialogue(uacTx);

            Assert.NotNull(dialogue);
            Assert.Equal(SIPURI.ParseSIPURI("sip:127.0.0.1:1234"), dialogue.RemoteTarget);
            Assert.Equal(dummyEP, dialogue.RemoteSIPEndPoint);

            logger.LogDebug("---------------------------------------------------");
        }

        /// <summary>
        /// Checks that the remote SIP end point for a UAC SIP dialgoue is correctly set
        /// when the Proxy-ReceivedFrom header is set.
        /// </summary>
        [Fact]
        public async Task UacTxCheckRemoteSocketProxyReceivedUnitTestUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string inviteReqStr =
@"INVITE sip:dummy@127.0.0.1:12014 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Contact: <sip:127.0.0.1:1234>
Max-Forwards: 70
User-Agent: unittest
Content-Length: 5
Content-Type: application/sdp

dummy";
            var sipReqBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummyEP, dummyEP);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipReqBuffer);

            string okRespStr =
@"SIP/2.0 200 OK
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
Contact: <sip:127.0.0.1:1234>
CSeq: 1 INVITE
Content-Length: 5
Content-Type: application/sdp
Proxy-ReceivedFrom: udp:192.168.0.50:5080

dummy";

            var sipRespBuffer = SIPMessageBuffer.ParseSIPMessage(okRespStr, dummyEP, dummyEP);
            SIPResponse okResponse = SIPResponse.ParseSIPResponse(sipRespBuffer);

            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));

            UACInviteTransaction uacTx = new UACInviteTransaction(transport, inviteReq, null);
            await uacTx.GotResponse(dummyEP, dummyEP, okResponse);

            var dialogue = new SIPDialogue(uacTx);

            Assert.NotNull(dialogue);
            Assert.Equal(SIPURI.ParseSIPURI("sip:127.0.0.1:1234"), dialogue.RemoteTarget);
            Assert.Equal(SIPEndPoint.ParseSIPEndPoint("udp:192.168.0.50:5080"), dialogue.RemoteSIPEndPoint);

            logger.LogDebug("---------------------------------------------------");
        }
    }
}
