//-----------------------------------------------------------------------------
// Filename: SIPUserAgentSingleCallUnitTest.cs
//
// Description: Unit tests for the SIPUserAgent guards that prevent a second call
// from being placed or answered while a call is already established. Without the
// guards the established call's dialog, media session and cancellation source get
// silently replaced leaving the original call orphaned, i.e. no BYE is ever sent
// for it.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Aug 2026  Aaron Clauson   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPUserAgentSingleCallUnitTest
    {
        private readonly ILogger logger;

        public SIPUserAgentSingleCallUnitTest(ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Creates a minimal SIP INVITE request with an SDP body so that SIPUserAgent.Answer
        /// can process it successfully and establish a dialog.
        /// </summary>
        private static SIPRequest CreateInviteRequest(string callId, IPEndPoint channelEndPoint)
        {
            var uri = SIPURI.ParseSIPURI($"sip:user@{channelEndPoint}");
            var toHeader = new SIPToHeader(null, uri, null);
            var fromHeader = new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:caller@127.0.0.1"), CallProperties.CreateNewTag());

            var request = new SIPRequest(SIPMethodsEnum.INVITE, uri);
            var header = new SIPHeader(fromHeader, toHeader, 1, callId);
            request.Header = header;
            header.CSeqMethod = SIPMethodsEnum.INVITE;
            header.Vias.PushViaHeader(new SIPViaHeader(channelEndPoint, CallProperties.CreateBranchId()));
            header.Contact = new List<SIPContactHeader> { new SIPContactHeader(null, uri) };
            header.ContentType = SIPSorcery.Net.SDP.SDP_MIME_CONTENTTYPE;
            header.MaxForwards = 70;

            request.Body =
                $"v=0\r\no=- 0 0 IN IP4 {channelEndPoint.Address}\r\ns=-\r\nc=IN IP4 {channelEndPoint.Address}\r\nt=0 0\r\nm=audio 49170 RTP/AVP 0\r\na=rtpmap:0 PCMU/8000\r\n";

            return request;
        }

        /// <summary>
        /// Injects an INVITE into the agent's transport and returns the request the agent's
        /// incoming call handler was given.
        /// </summary>
        private static async Task<SIPRequest> InjectInviteAsync(
            SIPUserAgent agent,
            RecordingMockSIPChannel channel,
            IPEndPoint channelEndPoint,
            string callId)
        {
            var incomingCallReceived = new TaskCompletionSource<SIPRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<SIPUserAgent, SIPRequest> handler = (ua, req) => incomingCallReceived.TrySetResult(req);
            agent.OnIncomingCall += handler;

            try
            {
                var inviteRequest = CreateInviteRequest(callId, channelEndPoint);
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, Encoding.UTF8.GetBytes(inviteRequest.ToString()));

                await Task.WhenAny(incomingCallReceived.Task, Task.Delay(5000));
                Assert.True(incomingCallReceived.Task.IsCompleted, "OnIncomingCall did not fire within timeout.");

                return incomingCallReceived.Task.Result;
            }
            finally
            {
                agent.OnIncomingCall -= handler;
            }
        }

        /// <summary>
        /// Establishes a dialog on the agent by injecting an INVITE and answering it.
        /// </summary>
        private static async Task<(string callId, MockMediaSession mediaSession)> EstablishDialogAsync(
            SIPUserAgent agent,
            RecordingMockSIPChannel channel,
            IPEndPoint channelEndPoint)
        {
            string callId = CallProperties.CreateNewCallId();
            var request = await InjectInviteAsync(agent, channel, channelEndPoint, callId);

            var uas = agent.AcceptCall(request);
            var mediaSession = new MockMediaSession();

            Assert.True(await agent.Answer(uas, mediaSession), "Failed to answer call and establish dialog.");
            Assert.NotNull(agent.Dialogue);

            return (callId, mediaSession);
        }

        /// <summary>
        /// Verifies that a second incoming call, one that is not an attended transfer replacing the
        /// established call, is rejected with a Busy Here response and that the established call's
        /// dialog and media session are left intact.
        /// </summary>
        [Fact]
        public async Task AnswerSecondCallRejectedWhenAlreadyOnCall()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6061);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                var (callId, mediaSession) = await EstablishDialogAsync(agent, channel, channelEndPoint);

                await Task.Delay(500);
                while (channel.AllSentMessages.TryTake(out _)) { }

                // A second, unrelated, incoming call. Note the transport level handler will not pass this
                // to the application when a dialog is up so it's injected directly the way an application
                // with its own transport handler would supply it.
                var secondInvite = CreateInviteRequest(CallProperties.CreateNewCallId(), channelEndPoint);
                var secondUas = agent.AcceptCall(secondInvite);
                var secondMediaSession = new MockMediaSession();

                bool answered = await agent.Answer(secondUas, secondMediaSession);

                Assert.False(answered, "The second call should not have been answered.");
                Assert.Equal(callId, agent.Dialogue.CallId);
                Assert.Same(mediaSession, agent.MediaSession);
                Assert.False(mediaSession.IsClosed, "The established call's media session should not have been closed.");
                Assert.True(agent.IsCallActive, "The established call should still be active.");

                await Task.Delay(500);

                // Note the established call's 200 OK gets retransmitted, the mock channel never ACKs it,
                // so only responses that could belong to the second call are checked for.
                var sentMessages = channel.AllSentMessages.ToList();
                Assert.Contains(sentMessages, x => x.StartsWith("SIP/2.0 486"));
                Assert.DoesNotContain(sentMessages, x => x.StartsWith("SIP/2.0 180"));
                Assert.DoesNotContain(sentMessages, x => x.Contains(secondInvite.Header.CallId) && x.StartsWith("SIP/2.0 200"));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that an attempt to place a new outgoing call while a call is established fails
        /// and leaves the established call intact.
        /// </summary>
        [Fact]
        public async Task PlaceCallFailsWhenAlreadyOnCall()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6062);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                var (callId, mediaSession) = await EstablishDialogAsync(agent, channel, channelEndPoint);

                string failureReason = null;
                agent.ClientCallFailed += (uac, error, resp) => failureReason = error;

                bool callResult = await agent.Call("sip:127.0.0.1:6063", null, null, new MockMediaSession());

                Assert.False(callResult, "The second call should not have been placed.");
                Assert.NotNull(failureReason);
                Assert.Equal(callId, agent.Dialogue.CallId);
                Assert.Same(mediaSession, agent.MediaSession);
                Assert.True(agent.IsCallActive, "The established call should still be active.");
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Builds an in-dialog REFER request for the agent's established dialog, i.e. one that will get
        /// matched to the dialog and dispatched to the transfer request handling.
        /// </summary>
        /// <param name="isAttended">If true the Refer-To will carry a Replaces parameter, which is what
        /// makes it an attended transfer. Blind transfers do not include one.</param>
        private static SIPRequest CreateReferRequest(SIPDialogue dialogue, IPEndPoint channelEndPoint, bool isAttended)
        {
            var uri = SIPURI.ParseSIPURI($"sip:user@{channelEndPoint}");
            var toHeader = new SIPToHeader(null, uri, dialogue.LocalTag);
            var fromHeader = new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:caller@127.0.0.1"), dialogue.RemoteTag);

            var request = new SIPRequest(SIPMethodsEnum.REFER, uri);
            var header = new SIPHeader(fromHeader, toHeader, dialogue.RemoteCSeq + 1, dialogue.CallId);
            request.Header = header;
            header.CSeqMethod = SIPMethodsEnum.REFER;
            header.Vias.PushViaHeader(new SIPViaHeader(channelEndPoint, CallProperties.CreateBranchId()));
            header.Contact = new List<SIPContactHeader> { new SIPContactHeader(null, uri) };
            header.ReferTo = CreateReferToField(isAttended).ToString();
            header.MaxForwards = 70;

            return request;
        }

        /// <summary>
        /// Builds the Refer-To user field for a transfer destination. Built the same way
        /// SIPUserAgent.GetReferRequest does so the escaping matches what a real transferor sends.
        /// </summary>
        private static SIPUserField CreateReferToField(bool isAttended)
        {
            var referToUri = SIPURI.ParseSIPURI("sip:transfer-target@127.0.0.1:6099");

            if (isAttended)
            {
                var replacesHeaders = new SIPParameters();
                replacesHeaders.Set(SIPHeaderAncillary.SIP_REFER_REPLACES,
                    SIPEscape.SIPURIParameterEscape($"{CallProperties.CreateNewCallId()};to-tag={CallProperties.CreateNewTag()};from-tag={CallProperties.CreateNewTag()}"));
                referToUri.Headers = replacesHeaders;
            }

            return new SIPUserField(null, referToUri, null);
        }

        /// <summary>
        /// Injects a REFER request for the agent's dialog and returns the messages that were sent in
        /// response to it.
        /// </summary>
        private static async Task<List<string>> InjectReferAsync(
            SIPUserAgent agent,
            RecordingMockSIPChannel channel,
            IPEndPoint channelEndPoint,
            bool isAttended = false)
        {
            while (channel.AllSentMessages.TryTake(out _)) { }

            var referRequest = CreateReferRequest(agent.Dialogue, channelEndPoint, isAttended);
            var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
            var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

            channel.FireMessageReceived(localEP, remoteEP, Encoding.UTF8.GetBytes(referRequest.ToString()));

            await Task.Delay(1000);

            return channel.AllSentMessages.ToList();
        }

        /// <summary>
        /// Verifies a transfer request is rejected when no OnTransferRequested handler is set and the
        /// dialog is using the transfer mode it gets by default.
        /// </summary>
        [Fact]
        public async Task TransferRejectedWhenNoHandlerAndDefaultTransferMode()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6064);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);
                Assert.Equal(SIPDialogueTransferModesEnum.Default, agent.Dialogue.TransferMode);

                var responses = await InjectReferAsync(agent, channel, channelEndPoint);

                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 603"));
                Assert.DoesNotContain(responses, x => x.StartsWith("SIP/2.0 202"));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies a transfer request is rejected when no OnTransferRequested handler is set and the
        /// dialog's transfer mode explicitly blocks transfers.
        /// </summary>
        [Fact]
        public async Task TransferRejectedWhenNoHandlerAndTransferModeNotAllowed()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6065);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);
                agent.Dialogue.TransferMode = SIPDialogueTransferModesEnum.NotAllowed;

                var responses = await InjectReferAsync(agent, channel, channelEndPoint);

                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 603"));
                Assert.DoesNotContain(responses, x => x.StartsWith("SIP/2.0 202"));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies a transfer request is accepted when no handler is set but the dialog's transfer mode
        /// explicitly permits transfers.
        /// </summary>
        [Fact]
        public async Task TransferAcceptedWhenNoHandlerAndTransferModePermits()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6066);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);
                agent.Dialogue.TransferMode = SIPDialogueTransferModesEnum.Allowed;

                var responses = await InjectReferAsync(agent, channel, channelEndPoint);

                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 202"));
                Assert.DoesNotContain(responses, x => x.StartsWith("SIP/2.0 603"));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies the legacy BlindPlaceCall transfer mode only permits blind transfers. An attended
        /// transfer, i.e. one with a Replaces parameter on the Refer-To, must still be rejected under
        /// that mode, it is what separates it from Allowed.
        /// </summary>
        [Theory]
        [InlineData(false, "SIP/2.0 202", 6069)]
        [InlineData(true, "SIP/2.0 603", 6070)]
        public async Task LegacyBlindPlaceCallModeOnlyPermitsBlindTransfers(
            bool isAttended,
            string expectedStatusLine,
            int port)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, port);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);
#pragma warning disable CS0618 // Type or member is obsolete
                agent.Dialogue.TransferMode = SIPDialogueTransferModesEnum.BlindPlaceCall;
#pragma warning restore CS0618 // Type or member is obsolete

                var responses = await InjectReferAsync(agent, channel, channelEndPoint, isAttended);

                Assert.Contains(responses, x => x.StartsWith(expectedStatusLine));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies an OnTransferRequested handler takes precedence over the dialog's transfer mode, in
        /// both directions.
        /// </summary>
        [Theory]
        [InlineData(SIPDialogueTransferModesEnum.NotAllowed, true, "SIP/2.0 202", 6067)]
        [InlineData(SIPDialogueTransferModesEnum.Allowed, false, "SIP/2.0 603", 6068)]
        public async Task TransferHandlerTakesPrecedenceOverTransferMode(
            SIPDialogueTransferModesEnum transferMode,
            bool handlerResult,
            string expectedStatusLine,
            int port)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, port);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);
                agent.Dialogue.TransferMode = transferMode;

                bool handlerCalled = false;
                agent.OnTransferRequested += (referTo, referredBy) =>
                {
                    handlerCalled = true;
                    return handlerResult;
                };

                var responses = await InjectReferAsync(agent, channel, channelEndPoint);

                Assert.True(handlerCalled, "The transfer requested handler was not called.");
                Assert.Contains(responses, x => x.StartsWith(expectedStatusLine));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }
    }
}
