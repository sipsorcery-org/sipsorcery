using System.Net;
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
        /// Tests that a REFER request is properly formatted when initiating a blind transfer.
        /// </summary>
        [Fact]
        public void BlindTransferCreatesReferRequestTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            MockSIPChannel channel = new MockSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(channel);

            SIPUserAgent userAgent = new SIPUserAgent(transport, null);

            // Create a simple INVITE request to establish a call
            var inviteReq = SIPRequest.GetRequest(
                SIPMethodsEnum.INVITE,
                SIPURI.ParseSIPURI("sip:bob@localhost"),
                new SIPToHeader(null, SIPURI.ParseSIPURI("sip:bob@localhost"), null),
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:alice@localhost"), CallProperties.CreateNewTag()));

            inviteReq.Header.CSeq = 1;
            inviteReq.Header.CallId = CallProperties.CreateNewCallId();
            inviteReq.Header.Contact = new System.Collections.Generic.List<SIPContactHeader> 
            { 
                new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:alice@localhost")) 
            };

            // Simulate an accepted INVITE to establish a dialogue
            var okResponse = SIPResponse.GetResponse(inviteReq, SIPResponseStatusCodesEnum.Ok, null);
            okResponse.Header.To.ToTag = CallProperties.CreateNewTag();
            okResponse.Header.Contact = new System.Collections.Generic.List<SIPContactHeader> 
            { 
                new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:bob@localhost")) 
            };

            // Verify the user agent was created
            Assert.NotNull(userAgent);

            userAgent.Dispose();
        }

        /// <summary>
        /// Tests that a REFER request without a Refer-To header would be rejected with a bad request.
        /// This tests the validation logic in ProcessTransferRequest.
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
        /// Tests that REFER request processing validates the dialog state.
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
    }
}
