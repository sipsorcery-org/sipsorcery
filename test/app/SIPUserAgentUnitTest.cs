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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SIPSorcery.UnitTests;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPUserAgentUnitTest
    {
        /// <summary>
        /// Tests that the Blind Transfer function doesn't do anything unexpected. The transfer
        /// request should return false since the Accepted response never arrives.
        /// </summary>
        [Fact]
        public async void BlindTransferUnitTest()
        {
            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0)));

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = @"INVITE sip:192.168.11.50:5060 SIP/2.0
Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3691
To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ
From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938
Call-ID: 17324d6df8744d978008c8997bfd208d
CSeq: 3532 INVITE
Contact: <sip:aaron@192.168.11.50:60163;ob>
Max-Forwards: 70
User-Agent: MicroSIP/3.19.22
Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS
Supported: replaces, 100rel, timer, norefersub
Content-Length: 343
Content-Type: application/sdp
Session-Expires: 1800
Min-SE: 90

v=0
o=- 3785527268 3785527269 IN IP4 192.168.11.50
s=pjmedia
t=0 0
m=audio 4032 RTP/AVP 0 101
c=IN IP4 192.168.11.50
a=rtpmap:0 PCMU/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-16
a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, null, uasTx);
            userAgent.Answer(mockUas);

            CancellationTokenSource cts = new CancellationTokenSource();
            bool result = await userAgent.BlindTransfer(SIPURI.ParseSIPURIRelaxed("127.0.0.1"), TimeSpan.FromSeconds(2), cts.Token);

            Assert.False(result);
        }

        /// <summary>
        /// Tests that the Blind Transfer function can be cancelled properly.
        /// </summary>
        [Fact]
        public void BlindTransferCancelUnitTest()
        {
            SIPTransport transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0)));

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = @"INVITE sip:192.168.11.50:5060 SIP/2.0
Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3691
To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ
From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938
Call-ID: 17324d6df8744d978008c8997bfd208d
CSeq: 3532 INVITE
Contact: <sip:aaron@192.168.11.50:60163;ob>
Max-Forwards: 70
User-Agent: MicroSIP/3.19.22
Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS
Supported: replaces, 100rel, timer, norefersub
Content-Length: 343
Content-Type: application/sdp
Session-Expires: 1800
Min-SE: 90

v=0
o=- 3785527268 3785527269 IN IP4 192.168.11.50
s=pjmedia
t=0 0
m=audio 4032 RTP/AVP 0 101
c=IN IP4 192.168.11.50
a=rtpmap:0 PCMU/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-16
a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, null, uasTx);
            userAgent.Answer(mockUas);

            CancellationTokenSource cts = new CancellationTokenSource();
            var blindTransferTask = userAgent.BlindTransfer(SIPURI.ParseSIPURIRelaxed("127.0.0.1"), TimeSpan.FromSeconds(2), cts.Token);

            cts.Cancel();

            Assert.ThrowsAnyAsync<TaskCanceledException>(async () => { bool result = await blindTransferTask; });
        }

        /// <summary>
        /// Tests that the answering and hanging up a mock call work as expected.
        /// </summary>
        [Fact]
        public void HangupUserAgentUnitTest()
        {
            // NOTE: For this test to work the transport layer must be instantiated with the queue incoming
            // flag set to false. That has the effect of getting the mockChannel.FireMessageReceived to flow
            // all the way through to the SIPUSerAgent on the same thread.
            SIPTransport transport = new SIPTransport(MockSIPDNSManager.Resolve, new SIPTransactionEngine(), false);
            MockSIPChannel mockChannel = new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(mockChannel);

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = @"INVITE sip:192.168.11.50:5060 SIP/2.0
Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3691
To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ
From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938
Call-ID: 17324d6df8744d978008c8997bfd208d
CSeq: 3532 INVITE
Contact: <sip:aaron@192.168.11.50:60163;ob>
Max-Forwards: 70
User-Agent: MicroSIP/3.19.22
Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS
Supported: replaces, 100rel, timer, norefersub
Content-Length: 343
Content-Type: application/sdp
Session-Expires: 1800
Min-SE: 90

v=0
o=- 3785527268 3785527269 IN IP4 192.168.11.50
s=pjmedia
t=0 0
m=audio 4032 RTP/AVP 0 101
c=IN IP4 192.168.11.50
a=rtpmap:0 PCMU/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-16
a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, null, null, SIPCallDirection.In, null, null, null, uasTx);
            userAgent.Answer(mockUas);

            // Incremented Cseq and modified Via header from original request. Means the request is the same dialog but different tx.
            string inviteReqStr2 = @"BYE sip:192.168.11.50:5060 SIP/2.0
Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3700
To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ
From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938
Call-ID: 17324d6df8744d978008c8997bfd208d
CSeq: 3533 BYE
Contact: <sip:aaron@192.168.11.50:60163;ob>
Max-Forwards: 70
User-Agent: MicroSIP/3.19.22
Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, INFO, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS
Supported: replaces, 100rel, timer, norefersub
";

            mockChannel.FireMessageReceived(dummySipEndPoint, dummySipEndPoint, Encoding.UTF8.GetBytes(inviteReqStr2));
        }
    }
}
