//-----------------------------------------------------------------------------
// Filename: SIPUserAgentUnitTest.cs
//
// Description: Unit tests for the SIPUserAgent class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Dec 2019	Aaron Clauson   Created, Dublin, Ireland.
// 14 Dec 2020  Aaron Clauson   Moved from unit to integration tests (while not 
//              really integration tests the duration is long'ish for a unit test).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions.V1;
using Xunit;

namespace SIPSorcery.SIP.IntegrationTests
{
    [Trait("Category", "integration")]
    public class SIPUserAgentUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPUserAgentUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private string m_CRLF = SIPConstants.CRLF;

        /// <summary>
        /// Tests that the Blind Transfer function doesn't do anything unexpected. The transfer
        /// request should return false since the Accepted response never arrives.
        /// </summary>
        [Fact]
        public async Task BlindTransferUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0)));

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = "INVITE sip:192.168.11.50:5060 SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3691" + m_CRLF +
"To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ" + m_CRLF +
"From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938" + m_CRLF +
"Call-ID: 17324d6df8744d978008c8997bfd208d" + m_CRLF +
"CSeq: 3532 INVITE" + m_CRLF +
"Contact: <sip:aaron@192.168.11.50:60163;ob>" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"User-Agent: MicroSIP/3.19.22" + m_CRLF +
"Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS" + m_CRLF +
"Supported: replaces, 100rel, timer, norefersub" + m_CRLF +
"Content-Length: 343" + m_CRLF +
"Content-Type: application/sdp" + m_CRLF +
"Session-Expires: 1800" + m_CRLF +
"Min-SE: 90" + m_CRLF +
"" + m_CRLF +
"v=0" + m_CRLF +
"o=- 3785527268 3785527269 IN IP4 192.168.11.50" + m_CRLF +
"s=pjmedia" + m_CRLF +
"t=0 0" + m_CRLF +
"m=audio 4032 RTP/AVP 0 101" + m_CRLF +
"c=IN IP4 192.168.11.50" + m_CRLF +
"a=rtpmap:0 PCMU/8000" + m_CRLF +
"a=rtpmap:101 telephone-event/8000" + m_CRLF +
"a=fmtp:101 0-16" + m_CRLF +
"a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, uasTx);
            await userAgent.Answer(mockUas, CreateMediaSession());

            CancellationTokenSource cts = new CancellationTokenSource();
            bool result = await userAgent.BlindTransfer(SIPURI.ParseSIPURIRelaxed("127.0.0.1"), TimeSpan.FromSeconds(2), cts.Token);

            Assert.False(result);
        }

        /// <summary>
        /// Tests that the Blind Transfer function can be cancelled properly.
        /// </summary>
        [Fact]
        public async Task BlindTransferCancelUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0)));

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = "INVITE sip:192.168.11.50:5060 SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3691" + m_CRLF +
"To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ" + m_CRLF +
"From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938" + m_CRLF +
"Call-ID: 17324d6df8744d978008c8997bfd208d" + m_CRLF +
"CSeq: 3532 INVITE" + m_CRLF +
"Contact: <sip:aaron@192.168.11.50:60163;ob>" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"User-Agent: MicroSIP/3.19.22" + m_CRLF +
"Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS" + m_CRLF +
"Supported: replaces, 100rel, timer, norefersub" + m_CRLF +
"Content-Length: 343" + m_CRLF +
"Content-Type: application/sdp" + m_CRLF +
"Session-Expires: 1800" + m_CRLF +
"Min-SE: 90" + m_CRLF +
"" + m_CRLF +
"v=0" + m_CRLF +
"o=- 3785527268 3785527269 IN IP4 192.168.11.50" + m_CRLF +
"s=pjmedia" + m_CRLF +
"t=0 0" + m_CRLF +
"m=audio 4032 RTP/AVP 0 101" + m_CRLF +
"c=IN IP4 192.168.11.50" + m_CRLF +
"a=rtpmap:0 PCMU/8000" + m_CRLF +
"a=rtpmap:101 telephone-event/8000" + m_CRLF +
"a=fmtp:101 0-16" + m_CRLF +
"a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, uasTx);
            await userAgent.Answer(mockUas, CreateMediaSession());

            CancellationTokenSource cts = new CancellationTokenSource();
            var blindTransferTask = userAgent.BlindTransfer(SIPURI.ParseSIPURIRelaxed("127.0.0.1"), TimeSpan.FromSeconds(2), cts.Token);

            cts.Cancel();

            Assert.False(await blindTransferTask);

            //await Assert.ThrowsAnyAsync<TaskCanceledException>(async () => { bool result = ; });
        }

        /// <summary>
        /// Tests that the answering and hanging up a mock call work as expected.
        /// </summary>
        [Fact]
        public async Task HangupUserAgentUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            MockSIPChannel mockChannel = new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(mockChannel);

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = "INVITE sip:192.168.11.50:5060 SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3691" + m_CRLF +
"To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ" + m_CRLF +
"From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938" + m_CRLF +
"Call-ID: 17324d6df8744d978008c8997bfd208d" + m_CRLF +
"CSeq: 3532 INVITE" + m_CRLF +
"Contact: <sip:aaron@192.168.11.50:60163;ob>" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"User-Agent: MicroSIP/3.19.22" + m_CRLF +
"Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS" + m_CRLF +
"Supported: replaces, 100rel, timer, norefersub" + m_CRLF +
"Content-Length: 343" + m_CRLF +
"Content-Type: application/sdp" + m_CRLF +
"Session-Expires: 1800" + m_CRLF +
"Min-SE: 90" + m_CRLF +
"" + m_CRLF +
"v=0" + m_CRLF +
"o=- 3785527268 3785527269 IN IP4 192.168.11.50" + m_CRLF +
"s=pjmedia" + m_CRLF +
"t=0 0" + m_CRLF +
"m=audio 4032 RTP/AVP 0 101" + m_CRLF +
"c=IN IP4 192.168.11.50" + m_CRLF +
"a=rtpmap:0 PCMU/8000" + m_CRLF +
"a=rtpmap:101 telephone-event/8000" + m_CRLF +
"a=fmtp:101 0-16" + m_CRLF +
"a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, uasTx);
            await userAgent.Answer(mockUas, CreateMediaSession());

            // Incremented Cseq and modified Via header from original request. Means the request is the same dialog but different tx.
            string inviteReqStr2 = "BYE sip:192.168.11.50:5060 SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3700" + m_CRLF +
"To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ" + m_CRLF +
"From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938" + m_CRLF +
"Call-ID: 17324d6df8744d978008c8997bfd208d" + m_CRLF +
"CSeq: 3533 BYE" + m_CRLF +
"Contact: <sip:aaron@192.168.11.50:60163;ob>" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"User-Agent: MicroSIP/3.19.22" + m_CRLF +
"Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS" + m_CRLF +
"Supported: replaces, 100rel, timer, norefersub" + m_CRLF +
"";

            mockChannel.FireMessageReceived(dummySipEndPoint, dummySipEndPoint, Encoding.UTF8.GetBytes(inviteReqStr2));
        }

        /// <summary>
        /// Tests that an incoming call without an SDP body gets processed correctly.
        /// </summary>
        [Fact]
        public async Task IncomingCallNoSdpUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0)));

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = "INVITE sip:192.168.11.50:5060 SIP/2.0" + m_CRLF +
"Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3691" + m_CRLF +
"To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ" + m_CRLF +
"From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938" + m_CRLF +
"Call-ID: 17324d6df8744d978008c8997bfd208d" + m_CRLF +
"CSeq: 3532 INVITE" + m_CRLF +
"Contact: <sip:aaron@192.168.11.50:60163;ob>" + m_CRLF +
"Max-Forwards: 70" + m_CRLF +
"Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS" + m_CRLF +
"Supported: replaces, 100rel, timer, norefersub" + m_CRLF +
"Content-Length: 0" + m_CRLF +
"Content-Type: application/sdp" + m_CRLF +
"Session-Expires: 1800" + m_CRLF + m_CRLF;

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            var uas = userAgent.AcceptCall(inviteReq);
            await userAgent.Answer(uas, CreateMockVoIPMediaEndPoint());

            // The call attempt should timeout while waiting for the ACK request with the SDP answer.
            Assert.False(userAgent.IsCallActive);
        }

        /// <summary>
        /// Tests that an incoming call without an SDP body and that receives an ACK with an SDP answer
        /// gets processed correctly.
        /// </summary>
        [Fact]
        public async Task IncomingCallNoSdpWithACKUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0)));
            var dummySep = SIPEndPoint.ParseSIPEndPoint("udp:127.0.0.1:5060");

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = @"INVITE sip:1@127.0.0.1 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:51200;branch=z9hG4bKbeed9b0cde8d43cc8a2aae91526b6a1d;rport
To: <sip:1@127.0.0.1>
From: <sip:thisis@anonymous.invalid>;tag=GCLNRILCDU
Call-ID: 7265e19f53a146a1bacdf4f4f8ea70b2
CSeq: 1 INVITE
Contact: <sip:127.0.0.1:51200>
Max-Forwards: 70
User-Agent: www.sipsorcery.com
Content-Length: 0
Content-Type: application/sdp" + m_CRLF + m_CRLF;

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            var uas = userAgent.AcceptCall(inviteReq);
            var mediaSession = CreateMediaSession();

            _ = Task.Run(() =>
            {
                Task.Delay(2000).Wait();

                string ackReqStr = @"ACK sip:127.0.0.1:5060 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:51200;branch=z9hG4bK76dfb1480ea14f778bd24afed1c8ded0;rport
To: <sip:1@127.0.0.1>;tag=YWPNZPMLPB
From: <sip:thisis@anonymous.invalid>;tag=GCLNRILCDU
Call-ID: 7265e19f53a146a1bacdf4f4f8ea70b2
CSeq: 1 ACK
Max-Forwards: 70
Content-Length: 160

v=0
o=- 67424 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 16976 RTP/AVP 8 101
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-16
a=sendrecv" + m_CRLF + m_CRLF;


                uas.ClientTransaction.ACKReceived(dummySep, dummySep, SIPRequest.ParseSIPRequest(ackReqStr));
            });

            await userAgent.Answer(uas, mediaSession);

            Assert.True(userAgent.IsCallActive);
        }

        /// <summary>
        /// Tests that the SIPUserAgent can correctly answer an audio only call and set the remote description.
        /// </summary>
        [Fact]
        public async Task AnswerAudioOnlyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = @"INVITE sip:dummy@0.0.0.0 SIP/2.0
Via: SIP/2.0/UDP 0.0.0.0;branch=z9hG4bK57441c4980b94e1686a06ae080be2935;rport
To: <sip:dummy@0.0.0.0>
From: <sip:0.0.0.0:0>;tag=MYILIYPHQD
Call-ID: ddf0e5a9687b4745925438da9000445d
CSeq: 1 INVITE
Max-Forwards: 70
Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE
Content-Length: 0

v=0
o=- 1838015445 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 19762 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            var uas = userAgent.AcceptCall(inviteReq);
            var result = await userAgent.Answer(uas, CreateMediaSession());

            Assert.True(result);
        }

        /// <summary>
        /// Tests that the SIPUserAgent can correctly place an audio only call and sets the remote description.
        /// </summary>
        [Fact]
        public async Task PlaceCallUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport serverTransport = new SIPTransport();
            SIPUDPChannel udpChannel = new SIPUDPChannel(IPAddress.Loopback, 0);
            serverTransport.AddSIPChannel(udpChannel);

            // Set up two user agents: one to answer the test call and one to place it.
            SIPUserAgent userAgentServer = new SIPUserAgent(serverTransport, null);
            SIPUserAgent userAgentClient = new SIPUserAgent(new SIPTransport(), null);

            serverTransport.SIPTransportRequestReceived += async (lep, rep, req) =>
            {
                logger.LogDebug("Request received: " + req.StatusLine);

                var uas = userAgentServer.AcceptCall(req);
                var serverMediaEndPoint = CreateMockVoIPMediaEndPoint();
                var answerResult = await userAgentServer.Answer(uas, serverMediaEndPoint);

                logger.LogDebug($"Server agent answer result {answerResult}.");

                Assert.True(answerResult);
            };

            var dstUri = udpChannel.GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 0)));

            logger.LogDebug($"Attempting call to {dstUri.ToString()}.");

            var clientMediaEndPoint = CreateMockVoIPMediaEndPoint();
            var callResult = await userAgentClient.Call(dstUri.ToString(), null, null, clientMediaEndPoint);

            logger.LogDebug($"Client agent answer result {callResult }.");

            Assert.True(callResult);
            Assert.Equal(SIPDialogueStateEnum.Confirmed, userAgentClient.Dialogue.DialogueState);
            Assert.Equal(SIPDialogueStateEnum.Confirmed, userAgentServer.Dialogue.DialogueState);
        }

        /// <summary>
        /// Tests that the SIPUserAgent can correctly deal with a call failure due to a mismatched audio codec.
        /// </summary>
        [Fact]
        public async Task PlaceCallMismatchedCapabilitiesUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport serverTransport = new SIPTransport();
            SIPUDPChannel udpChannel = new SIPUDPChannel(IPAddress.Loopback, 0);
            serverTransport.AddSIPChannel(udpChannel);

            // Set up two user agents: one to answer the test call and one to place it.
            SIPUserAgent userAgentServer = new SIPUserAgent(serverTransport, null);
            SIPUserAgent userAgentClient = new SIPUserAgent(new SIPTransport(), null);

            serverTransport.SIPTransportRequestReceived += async (lep, rep, req) =>
            {
                logger.LogDebug("Request received: " + req.StatusLine);

                var uas = userAgentServer.AcceptCall(req);
                var serverAudioSession = CreateMockVoIPMediaEndPoint(format => format.Codec == AudioCodecsEnum.PCMU);

                var answerResult = await userAgentServer.Answer(uas, serverAudioSession).ConfigureAwait(false);

                logger.LogDebug($"Server agent answer result {answerResult}.");

                Assert.False(answerResult);
            };

            var dstUri = udpChannel.GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 0)));

            logger.LogDebug($"Attempting call to {dstUri.ToString()}.");

            var clientMediaEndPoint = CreateMockVoIPMediaEndPoint(format => format.Codec == AudioCodecsEnum.G722);
            var callResult = await userAgentClient.Call(dstUri.ToString(), null, null, clientMediaEndPoint);

            logger.LogDebug($"Client agent answer result {callResult }.");

            Assert.False(callResult);
        }

        /// <summary>
        /// Tests that the SIPUserAgent can correctly handle the condition where no local audio track has
        /// been added to the RTP session when an answer is attempted.
        /// </summary>
        [Fact]
        public async Task HandleMissingAudioTrackOnAnswerUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = @"INVITE sip:dummy@0.0.0.0 SIP/2.0
Via: SIP/2.0/UDP 0.0.0.0;branch=z9hG4bK57441c4980b94e1686a06ae080be2935;rport
To: <sip:dummy@0.0.0.0>
From: <sip:0.0.0.0:0>;tag=MYILIYPHQD
Call-ID: ddf0e5a9687b4745925438da9000445d
CSeq: 1 INVITE
Max-Forwards: 70
Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE
Content-Length: 0

v=0
o=- 1838015445 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 19762 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            var uas = userAgent.AcceptCall(inviteReq);
            RTPSession rtpSession = new RTPSession(false, false, false);
            var result = await userAgent.Answer(uas, rtpSession);

            Assert.False(result);

            rtpSession.Close("normal");
        }

        /// <summary>
        /// Tests that the SIPUserAgent can correctly handle the condition where the port number
        /// supplied in the remote SDP is invalid. Originally the behaviour was to reject the SDP
        /// with the invalid port number. That has now been changed to use the SDP "ignore" port
        /// number and wait for the port to be set by some other means.
        /// </summary>
        [Fact]
        public async Task HandleInvalidSdpPortOnAnswerUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = @"INVITE sip:dummy@0.0.0.0 SIP/2.0
Via: SIP/2.0/UDP 0.0.0.0;branch=z9hG4bK57441c4980b94e1686a06ae080be2935;rport
To: <sip:dummy@0.0.0.0>
From: <sip:0.0.0.0:0>;tag=MYILIYPHQD 
Call-ID: ddf0e5a9687b4745925438da9000445d
CSeq: 1 INVITE
Max-Forwards: 70
Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE
Content-Length: 0

v=0
o=- 1838015445 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 79762 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            var uas = userAgent.AcceptCall(inviteReq);

            RTPSession rtpSession = new RTPSession(false, false, false);
            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
            rtpSession.addTrack(audioTrack);

            var result = await userAgent.Answer(uas, rtpSession);

            Assert.True(result);

            rtpSession.Close("normal");
        }

        /// <summary>
        /// Tests that the SIPUserAgent can correctly place a call.
        /// </summary>
        [Fact]
        public async Task CheckCanPlaceCallUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // This transport will act as the call receiver. It allows the test to provide a 
            // tailored response to an incoming call.
            SIPTransport calleeTransport = new SIPTransport();

            // This transport will be used by the SIPUserAgent being tested to place the call.
            SIPTransport callerTransport = new SIPTransport();
            RTPSession rtpSession = new RTPSession(false, false, false);

            try
            {
                calleeTransport.AddSIPChannel(new SIPUDPChannel(IPAddress.Loopback, 0));
                calleeTransport.SIPTransportRequestReceived += async (lep, rep, req) =>
                {
                    if (req.Method != SIPMethodsEnum.INVITE)
                    {
                        SIPResponse notAllowedResponse = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                        await calleeTransport.SendResponseAsync(notAllowedResponse);
                    }
                    else
                    {
                        UASInviteTransaction uasTransaction = new UASInviteTransaction(calleeTransport, req, null);
                        var uas = new SIPServerUserAgent(calleeTransport, null, null, null, SIPCallDirection.In, null, null, uasTransaction);
                        uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
                        uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

                        var answerSdp = @"
v=0
o=- 1838015445 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 19762 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv";
                        uas.Answer(SDP.SDP_MIME_CONTENTTYPE, answerSdp, null, SIPDialogueTransferModesEnum.NotAllowed);
                    }
                };

                SIPUserAgent userAgent = new SIPUserAgent(callerTransport, null);

                MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
                rtpSession.addTrack(audioTrack);

                SIPURI dstUri = new SIPURI(SIPSchemesEnum.sip, calleeTransport.GetSIPChannels().First().ListeningSIPEndPoint);
                var result = await userAgent.Call(dstUri.ToString(), null, null, rtpSession);
                Assert.True(result);
            }
            finally
            {
                rtpSession?.Close("normal");
                callerTransport?.Shutdown();
                calleeTransport?.Shutdown();
            }
        }

        /// <summary>
        /// Tests that the SIPUserAgent can correctly handle the condition where the port number
        /// supplied in the remote SDP is invalid when a call attempt is made. Originally the 
        /// behaviour was to reject the SDP with the invalid port number. That has now been changed 
        /// to use the SDP "ignore" port number and wait for the port to be set by some other means.
        /// </summary>
        [Fact]
        public async Task HandleInvalidSdpPortOnPlaceCallUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // This transport will act as the call receiver. It allows the test to provide a 
            // tailored response to an incoming call.
            SIPTransport calleeTransport = new SIPTransport();

            // This transport will be used by the SIPUserAgent being tested to place the call.
            SIPTransport callerTransport = new SIPTransport();
            RTPSession rtpSession = new RTPSession(false, false, false);

            try
            {
                calleeTransport.AddSIPChannel(new SIPUDPChannel(IPAddress.Loopback, 0));
                calleeTransport.SIPTransportRequestReceived += async (lep, rep, req) =>
                {
                    if (req.Method != SIPMethodsEnum.INVITE)
                    {
                        SIPResponse notAllowedResponse = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                        await calleeTransport.SendResponseAsync(notAllowedResponse);
                    }
                    else
                    {
                        UASInviteTransaction uasTransaction = new UASInviteTransaction(calleeTransport, req, null);
                        var uas = new SIPServerUserAgent(calleeTransport, null, null, null, SIPCallDirection.In, null, null, uasTransaction);
                        uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
                        uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

                        var answerSdp = @"
v=0
o=- 1838015445 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 79762 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv";
                        uas.Answer(SDP.SDP_MIME_CONTENTTYPE, answerSdp, null, SIPDialogueTransferModesEnum.NotAllowed);
                    }
                };

                SIPUserAgent userAgent = new SIPUserAgent(callerTransport, null);

                MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
                rtpSession.addTrack(audioTrack);

                SIPURI dstUri = new SIPURI(SIPSchemesEnum.sip, calleeTransport.GetSIPChannels().First().ListeningSIPEndPoint);
                var result = await userAgent.Call(dstUri.ToString(), null, null, rtpSession);
                Assert.True(result);
            }
            finally
            {
                rtpSession?.Close("normal");
                callerTransport?.Shutdown();
                calleeTransport?.Shutdown();
            }
        }

        /// <summary>
        /// Tests that the SIPUserAgent can correctly place be the recipient of an attended transfer
        /// REFER request.
        /// </summary>
        /// <remarks>
        /// This test requires 4 SIPUserAgent instances:
        ///  - User Agent A is the original caller and transferrer.
        ///  - User Agent B calls User Agent D to create the call leg that will be replaced by the transfer.
        ///    This call leg would represent the original caller putting one call leg on hold and then
        ///    calling and talking to the transfer destination before completing the transfer,
        ///  - User Agent C receives and answers call from A and then at a later point gets a REFER 
        ///    request, also from A, to initiate the transfer,
        ///  - User Agent D receives and answer call from A and then at a later point receives a second
        ///    call from B that replaces the call with A.
        /// </remarks>
        [Fact]
        public async Task AttendedTransfereeUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // User agents A and B can use the same transport as they don't auto-answer incoming calls.
            SIPTransport sipTransportCaller = new SIPTransport();
            sipTransportCaller.AddSIPChannel(new SIPUDPChannel(IPAddress.Loopback, 0));
            var userAgentA = new SIPUserAgent(sipTransportCaller, null);
            var userAgentB = new SIPUserAgent(sipTransportCaller, null);

            SIPTransport sipTransportC = new SIPTransport();
            sipTransportC.AddSIPChannel(new SIPUDPChannel(IPAddress.Loopback, 0));
            var userAgentC = new SIPUserAgent(sipTransportC, null);

            SIPTransport sipTransportD = new SIPTransport();
            sipTransportD.AddSIPChannel(new SIPUDPChannel(IPAddress.Loopback, 0));
            var userAgentD = new SIPUserAgent(sipTransportD, null);

            logger.LogDebug($"sip transport for UA's A and B listening on: {sipTransportCaller.GetSIPChannels()[0].ListeningSIPEndPoint}.");
            logger.LogDebug($"sip transport for UA C listening on: {sipTransportC.GetSIPChannels()[0].ListeningSIPEndPoint}.");
            logger.LogDebug($"sip transport for UA D listening on: {sipTransportD.GetSIPChannels()[0].ListeningSIPEndPoint}.");

            // Set up auto-answer for UA's C and D:
            foreach (var userAgent in new List<SIPUserAgent> { userAgentC, userAgentD })
            {
                userAgent.ServerCallCancelled += (uas) => logger.LogDebug("Incoming call cancelled by remote party.");
                userAgent.OnCallHungup += (dialog) => logger.LogDebug("Call hungup by remote party.");
                userAgent.OnIncomingCall += async (ua, req) =>
                {
                    var uas = ua.AcceptCall(req);
                    bool answerResult = await ua.Answer(uas, CreateMediaSession());
                    logger.LogDebug($"Answer incoming call result {answerResult}.");
                };
            }

            // Place the two calls from A to C and B to D.
            var dstUriC = sipTransportC.GetSIPChannels()[0].GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 0)));
            logger.LogDebug($"UA-A attempting call UA-C on {dstUriC}.");
            var callResultAtoC = await userAgentA.Call(dstUriC.ToString(), null, null, CreateMediaSession());
            logger.LogDebug($"Client agent answer result for A to C {callResultAtoC}.");

            Assert.True(callResultAtoC);

            var dstUriD = sipTransportD.GetSIPChannels()[0].GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 0)));
            logger.LogDebug($"UA-B attempting call UA-D on {dstUriD}.");
            var callResultBtoD = await userAgentB.Call(dstUriD.ToString(), null, null, CreateMediaSession());
            logger.LogDebug($"Client agent answer result for B to D {callResultBtoD}.");

            Assert.True(callResultBtoD);

            Assert.True(userAgentA.IsCallActive);
            Assert.True(userAgentB.IsCallActive);
            Assert.True(userAgentC.IsCallActive);
            Assert.True(userAgentD.IsCallActive);

            // Initiate attended transfer. A sends REFER request to C such that:
            // - A sends a REFER request to C,
            // - The REFER request Refer-To header tells C who to call and what to put in its INVITE request Replaces header,
            // - The INVITE from C to D tells D this new call from C replaces its call with B,
            // - When D answers it hangs up its call with B,
            // - When C gets the Ok response from C it hangs up its call with A.
            CancellationTokenSource cts = new CancellationTokenSource();
            bool transferResult = await userAgentA.AttendedTransfer(userAgentB.Dialogue, TimeSpan.FromSeconds(2), cts.Token);

            // This means the REFER request was accepted but the transfer still needs to be actioned.
            Assert.True(transferResult);

            // Give the transfer time to be processed.
            await Task.Delay(2000);

            Assert.False(userAgentA.IsCallActive);
            Assert.False(userAgentB.IsCallActive);
            Assert.True(userAgentC.IsCallActive);
            Assert.True(userAgentD.IsCallActive);

            sipTransportCaller.Shutdown();
            sipTransportC.Shutdown();
            sipTransportD.Shutdown();
        }

        private IMediaSession CreateMediaSession()
        {
            return new MockMediaSession();
        }

        private VoIPMediaSession CreateMockVoIPMediaEndPoint(Func<AudioFormat, bool> audioFormatFilter = null)
        {
            var audioSource = new AudioExtrasSource();
            audioSource.RestrictFormats(audioFormatFilter);

            MediaEndPoints mockEndPoints = new MediaEndPoints
            {
                AudioSource = audioSource
            };
            return new VoIPMediaSession(mockEndPoints);
        }
    }
}
