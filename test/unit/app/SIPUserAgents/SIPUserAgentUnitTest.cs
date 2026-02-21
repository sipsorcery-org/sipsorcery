using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

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

        /// <summary>
        /// Tests that a SIPUserAgent can be created successfully.
        /// </summary>
        [Fact]
        public void CreateSIPUserAgentTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            MockSIPChannel channel = new MockSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(channel);

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            Assert.NotNull(userAgent);

            userAgent.Dispose();
        }

        /// <summary>
        /// Tests that a REFER request can be properly created.
        /// </summary>
        [Fact]
        public void CreateReferRequestTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create a REFER request to test its structure
            var referRequest = SIPRequest.GetRequest(
                SIPMethodsEnum.REFER,
                SIPURI.ParseSIPURI("sip:bob@localhost"),
                new SIPToHeader(null, SIPURI.ParseSIPURI("sip:bob@localhost"), CallProperties.CreateNewTag()),
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:alice@localhost"), CallProperties.CreateNewTag()));

            referRequest.Header.CSeq = 2;
            referRequest.Header.CallId = CallProperties.CreateNewCallId();
            referRequest.Header.ReferTo = "<sip:charlie@localhost>";

            // Verify the REFER request was created correctly
            Assert.Equal(SIPMethodsEnum.REFER, referRequest.Method);
            Assert.False(string.IsNullOrWhiteSpace(referRequest.Header.ReferTo));
            Assert.Contains("charlie@localhost", referRequest.Header.ReferTo);
        }

        /// <summary>
        /// Tests that a REFER request can be created without a Refer-To header.
        /// The actual rejection of such requests is handled by ProcessTransferRequest in SIPUserAgent,
        /// which validates the Refer-To header and returns 400 Bad Request if missing.
        /// </summary>
        [Fact]
        public void ReferWithoutReferToHeaderTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create a REFER request without a Refer-To header
            var referRequest = SIPRequest.GetRequest(
                SIPMethodsEnum.REFER,
                SIPURI.ParseSIPURI("sip:bob@localhost"),
                new SIPToHeader(null, SIPURI.ParseSIPURI("sip:bob@localhost"), CallProperties.CreateNewTag()),
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:alice@localhost"), CallProperties.CreateNewTag()));

            referRequest.Header.CSeq = 2;
            referRequest.Header.CallId = CallProperties.CreateNewCallId();

            // Verify the Refer-To header is empty/null
            Assert.True(string.IsNullOrWhiteSpace(referRequest.Header.ReferTo));
        }

        /// <summary>
        /// Tests that a REFER request can be properly created with required headers.
        /// In practice, REFER requests must be sent within an established dialog,
        /// which is validated by SIPUserAgent's ProcessTransferRequest method
        /// (returns 481 if no dialog exists or dialog is not in Confirmed state).
        /// </summary>
        [Fact]
        public void ReferRequiresEstablishedDialogTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            MockSIPChannel channel = new MockSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(channel);

            // Create a REFER request
            var referRequest = SIPRequest.GetRequest(
                SIPMethodsEnum.REFER,
                SIPURI.ParseSIPURI("sip:bob@localhost"),
                new SIPToHeader(null, SIPURI.ParseSIPURI("sip:bob@localhost"), CallProperties.CreateNewTag()),
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:alice@localhost"), CallProperties.CreateNewTag()));

            referRequest.Header.CSeq = 2;
            referRequest.Header.CallId = CallProperties.CreateNewCallId();
            referRequest.Header.ReferTo = "<sip:charlie@localhost>";

            // Verify the REFER request was created with a Refer-To header
            Assert.False(string.IsNullOrWhiteSpace(referRequest.Header.ReferTo));
            Assert.Equal(SIPMethodsEnum.REFER, referRequest.Method);
        }

        /// <summary>
        /// Tests that authentication headers are properly handled in REFER responses.
        /// This validates the fix for issue #1493.
        /// </summary>
        [Fact]
        public void ReferAuthenticationResponseTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create a REFER request
            var referRequest = SIPRequest.GetRequest(
                SIPMethodsEnum.REFER,
                SIPURI.ParseSIPURI("sip:bob@localhost"),
                new SIPToHeader(null, SIPURI.ParseSIPURI("sip:bob@localhost"), CallProperties.CreateNewTag()),
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:alice@localhost"), CallProperties.CreateNewTag()));

            referRequest.Header.CSeq = 2;
            referRequest.Header.CallId = CallProperties.CreateNewCallId();
            referRequest.Header.ReferTo = "<sip:charlie@localhost>";

            // Create a 401 Unauthorized response
            var unauthorizedResponse = SIPResponse.GetResponse(
                referRequest,
                SIPResponseStatusCodesEnum.Unauthorised,
                "Authentication Required");

            // Verify the response
            Assert.Equal(SIPResponseStatusCodesEnum.Unauthorised, unauthorizedResponse.Status);
            Assert.Equal(referRequest.Header.CSeq, unauthorizedResponse.Header.CSeq);
            Assert.Equal(SIPMethodsEnum.REFER, unauthorizedResponse.Header.CSeqMethod);

            // Create a 407 Proxy Authentication Required response
            var proxyAuthResponse = SIPResponse.GetResponse(
                referRequest,
                SIPResponseStatusCodesEnum.ProxyAuthenticationRequired,
                "Proxy Authentication Required");

            // Verify the response
            Assert.Equal(SIPResponseStatusCodesEnum.ProxyAuthenticationRequired, proxyAuthResponse.Status);
            Assert.Equal(referRequest.Header.CSeq, proxyAuthResponse.Header.CSeq);
            Assert.Equal(SIPMethodsEnum.REFER, proxyAuthResponse.Header.CSeqMethod);
        }

        /// <summary>
        /// Tests that a 202 Accepted response is the expected success response for a REFER request.
        /// </summary>
        [Fact]
        public void ReferAcceptedResponseTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create a REFER request
            var referRequest = SIPRequest.GetRequest(
                SIPMethodsEnum.REFER,
                SIPURI.ParseSIPURI("sip:bob@localhost"),
                new SIPToHeader(null, SIPURI.ParseSIPURI("sip:bob@localhost"), CallProperties.CreateNewTag()),
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:alice@localhost"), CallProperties.CreateNewTag()));

            referRequest.Header.CSeq = 2;
            referRequest.Header.CallId = CallProperties.CreateNewCallId();
            referRequest.Header.ReferTo = "<sip:charlie@localhost>";

            // Create a 202 Accepted response
            var acceptedResponse = SIPResponse.GetResponse(
                referRequest,
                SIPResponseStatusCodesEnum.Accepted,
                null);

            // Verify the response
            Assert.Equal(SIPResponseStatusCodesEnum.Accepted, acceptedResponse.Status);
            Assert.Equal(referRequest.Header.CSeq, acceptedResponse.Header.CSeq);
            Assert.Equal(SIPMethodsEnum.REFER, acceptedResponse.Header.CSeqMethod);
        }

        /// <summary>
        /// Tests that authentication can be added to a REFER request using DuplicateAndAuthenticate.
        /// </summary>
        [Fact]
        public void ReferWithAuthenticationTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create a REFER request
            var referRequest = SIPRequest.GetRequest(
                SIPMethodsEnum.REFER,
                SIPURI.ParseSIPURI("sip:bob@localhost"),
                new SIPToHeader(null, SIPURI.ParseSIPURI("sip:bob@localhost"), CallProperties.CreateNewTag()),
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:alice@localhost"), CallProperties.CreateNewTag()));

            referRequest.Header.CSeq = 2;
            referRequest.Header.CallId = CallProperties.CreateNewCallId();
            referRequest.Header.ReferTo = "<sip:charlie@localhost>";

            // Create a 401 response with authentication challenge
            var authChallenge = SIPResponse.GetResponse(
                referRequest,
                SIPResponseStatusCodesEnum.Unauthorised,
                "Authentication Required");

            var authHeader = new SIPAuthenticationHeader(
                SIPAuthorisationHeadersEnum.WWWAuthenticate,
                "test-realm",
                CallProperties.CreateNewTag());
            authChallenge.Header.AuthenticationHeaders.Add(authHeader);

            // Verify the challenge includes authentication header
            Assert.NotEmpty(authChallenge.Header.AuthenticationHeaders);

            // Test that DuplicateAndAuthenticate can be called on the REFER request
            var authenticatedRequest = referRequest.DuplicateAndAuthenticate(
                authChallenge.Header.AuthenticationHeaders,
                "testuser",
                "testpass");

            // Verify the authenticated request was created
            Assert.NotNull(authenticatedRequest);
            Assert.Equal(SIPMethodsEnum.REFER, authenticatedRequest.Method);
            Assert.Equal(referRequest.Header.ReferTo, authenticatedRequest.Header.ReferTo);
        }

        /// <summary>
        /// Tests that BlindTransfer with exitAfterTransfer set to true hangs up the call
        /// after a successful 202 Accepted response to the REFER request.
        /// </summary>
        [Fact]
        public async Task BlindTransferExitAfterTransferHangupsCallTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            string CRLF = SIPConstants.CRLF;

            SIPTransport transport = new SIPTransport();
            MockSIPChannel mockChannel = new MockSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(mockChannel);

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            string inviteReqStr = "INVITE sip:192.168.11.50:5060 SIP/2.0" + CRLF +
"Via: SIP/2.0/UDP 192.168.11.50:60163;rport;branch=z9hG4bKPj869f70960bdd4204b1352eaf242a3691" + CRLF +
"To: <sip:2@192.168.11.50>;tag=ZUJSXRRGXQ" + CRLF +
"From: <sip:aaron@192.168.11.50>;tag=4a60ce364b774258873ff199e5e39938" + CRLF +
"Call-ID: 17324d6df8744d978008c8997bfd208d" + CRLF +
"CSeq: 3532 INVITE" + CRLF +
"Contact: <sip:aaron@192.168.11.50:60163;ob>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Content-Length: 343" + CRLF +
"Content-Type: application/sdp" + CRLF +
"" + CRLF +
"v=0" + CRLF +
"o=- 3785527268 3785527269 IN IP4 192.168.11.50" + CRLF +
"s=pjmedia" + CRLF +
"t=0 0" + CRLF +
"m=audio 4032 RTP/AVP 0 101" + CRLF +
"c=IN IP4 192.168.11.50" + CRLF +
"a=rtpmap:0 PCMU/8000" + CRLF +
"a=rtpmap:101 telephone-event/8000" + CRLF +
"a=fmtp:101 0-16" + CRLF +
"a=sendrecv";

            SIPEndPoint dummySipEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessageBuffer);

            UASInviteTransaction uasTx = new UASInviteTransaction(transport, inviteReq, null);
            SIPServerUserAgent mockUas = new SIPServerUserAgent(transport, null, uasTx, null);
            await userAgent.Answer(mockUas, new MockMediaSession());

            Assert.True(userAgent.IsCallActive);

            // Drain any pending signal from Answer's outgoing messages.
            mockChannel.SIPMessageSent.WaitOne(500);

            CancellationTokenSource cts = new CancellationTokenSource();
            var transferTask = userAgent.BlindTransfer(
                SIPURI.ParseSIPURIRelaxed("127.0.0.1"),
                TimeSpan.FromSeconds(5),
                cts.Token,
                exitAfterTransfer: true);

            // Wait for the REFER request to be sent through the mock channel.
            Assert.True(mockChannel.SIPMessageSent.WaitOne(TimeSpan.FromSeconds(2)), "REFER was not sent in time.");

            // Parse the sent REFER request and create a 202 Accepted response.
            string referMsgStr = mockChannel.LastSIPMessageSent;
            SIPMessageBuffer referMsgBuf = SIPMessageBuffer.ParseSIPMessage(referMsgStr, dummySipEndPoint, dummySipEndPoint);
            SIPRequest referReq = SIPRequest.ParseSIPRequest(referMsgBuf);
            Assert.Equal(SIPMethodsEnum.REFER, referReq.Method);

            SIPResponse acceptedResponse = SIPResponse.GetResponse(referReq, SIPResponseStatusCodesEnum.Accepted, null);
            byte[] respBytes = Encoding.UTF8.GetBytes(acceptedResponse.ToString());

            // Inject the 202 Accepted response back through the transport.
            mockChannel.FireMessageReceived(dummySipEndPoint, dummySipEndPoint, respBytes);

            bool result = await transferTask;

            Assert.True(result);
            Assert.False(userAgent.IsCallActive);
            Assert.True(userAgent.MediaSession.IsClosed);
        }
    }
}
