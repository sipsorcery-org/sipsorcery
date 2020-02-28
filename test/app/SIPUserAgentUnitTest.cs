﻿//-----------------------------------------------------------------------------
// Filename: SIPUserAgentUnitTest.cs
//
// Description: Unit tests for the SIPUserAgent class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Dec 2019	Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using Xunit;
using SIPSorcery.UnitTests;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
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
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, null, uasTx);
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
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, null, uasTx);
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

            SIPTransport transport = new SIPTransport(false, MockSIPDNSManager.Resolve);
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
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, null, uasTx);
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

        private IMediaSession CreateMediaSession()
        {
            return new MockMediaSession();
        }
    }
}
