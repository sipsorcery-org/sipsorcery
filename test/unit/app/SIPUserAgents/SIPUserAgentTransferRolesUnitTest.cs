//-----------------------------------------------------------------------------
// Filename: SIPUserAgentTransferRolesUnitTest.cs
//
// Description: Unit tests for the SIPUserAgent transfer functions covering the three
// RFC5589 roles. The Transferor (sends the REFER), the Transferee (receives the REFER
// and calls the new destination) and the Transfer Target (receives an INVITE with a
// Replaces header that takes over its call).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30 Aug 2026  Aaron Clauson   Created.
// 17 Nov 2026  Claude Code - Opus 5.5   A transferee's call to the target turned down leaves the original call up.
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
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPUserAgentTransferRolesUnitTest
    {
        private readonly ILogger logger;

        public SIPUserAgentTransferRolesUnitTest(ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        private static readonly SIPEndPoint DummySIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

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
        /// Establishes a dialog on the agent by injecting an INVITE and answering it. This is the
        /// call the agent holds with the remote party for all the tests in this class.
        /// </summary>
        private static async Task<MockMediaSession> EstablishDialogAsync(
            SIPUserAgent agent,
            RecordingMockSIPChannel channel,
            IPEndPoint channelEndPoint)
        {
            var incomingCallReceived = new TaskCompletionSource<SIPRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<SIPUserAgent, SIPRequest> handler = (ua, req) => incomingCallReceived.TrySetResult(req);
            agent.OnIncomingCall += handler;

            try
            {
                var inviteRequest = CreateInviteRequest(CallProperties.CreateNewCallId(), channelEndPoint);
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);

                channel.FireMessageReceived(localEP, DummySIPEndPoint, Encoding.UTF8.GetBytes(inviteRequest.ToString()));

                await Task.WhenAny(incomingCallReceived.Task, Task.Delay(5000));
                Assert.True(incomingCallReceived.Task.IsCompleted, "OnIncomingCall did not fire within timeout.");

                var uas = agent.AcceptCall(incomingCallReceived.Task.Result);
                var mediaSession = new MockMediaSession();

                Assert.True(await agent.Answer(uas, mediaSession), "Failed to answer call and establish dialog.");
                Assert.NotNull(agent.Dialogue);

                return mediaSession;
            }
            finally
            {
                agent.OnIncomingCall -= handler;
            }
        }

        /// <summary>
        /// Builds a dialogue representing the consulted call, i.e. the call the Transferor has with the
        /// Transfer Target and whose details go into the REFER request's Replaces parameter.
        /// </summary>
        private static SIPDialogue CreateConsultedDialogue(SIPCallDirection direction)
        {
            return new SIPDialogue
            {
                CallId = CallProperties.CreateNewCallId(),
                LocalTag = CallProperties.CreateNewTag(),
                RemoteTag = CallProperties.CreateNewTag(),
                LocalUserField = new SIPUserField("Transferor", SIPURI.ParseSIPURI("sip:transferor@127.0.0.1:7001"), null),
                RemoteUserField = new SIPUserField("Target", SIPURI.ParseSIPURI("sip:target@127.0.0.1:7002"), null),
                RemoteTarget = SIPURI.ParseSIPURI("sip:target@127.0.0.1:7002"),
                Direction = direction,
                DialogueState = SIPDialogueStateEnum.Confirmed
            };
        }

        /// <summary>
        /// Runs an attended transfer against a consulted dialogue with the supplied direction and returns
        /// the REFER request the user agent sent. The transfer itself will time out, no Accepted response
        /// is ever supplied, which does not affect the request that was sent.
        /// </summary>
        private static async Task<SIPRequest> GetSentReferRequestAsync(
            SIPUserAgent agent,
            RecordingMockSIPChannel channel,
            SIPDialogue consultedDialogue)
        {
            while (channel.AllSentMessages.TryTake(out _)) { }

            var cts = new CancellationTokenSource();
            await agent.AttendedTransfer(consultedDialogue, TimeSpan.FromSeconds(1), cts.Token);

            return GetSentRefer(channel);
        }

        /// <summary>
        /// Returns the REFER request the user agent sent.
        /// </summary>
        private static SIPRequest GetSentRefer(RecordingMockSIPChannel channel)
        {
            // The REFER gets retransmitted while waiting for the Accepted response that never arrives so
            // only the first instance is of interest.
            string referStr = channel.AllSentMessages.FirstOrDefault(x => x.StartsWith("REFER "));
            Assert.False(referStr == null, "No REFER request was sent by the user agent.");

            return SIPRequest.ParseSIPRequest(SIPMessageBuffer.ParseSIPMessage(referStr, DummySIPEndPoint, DummySIPEndPoint));
        }

        /// <summary>
        /// Injects a REFER request and returns the messages the user agent sent in response.
        /// </summary>
        private static async Task<List<string>> InjectRequestAsync(
            RecordingMockSIPChannel channel,
            IPEndPoint channelEndPoint,
            SIPRequest request)
        {
            while (channel.AllSentMessages.TryTake(out _)) { }

            var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
            channel.FireMessageReceived(localEP, DummySIPEndPoint, Encoding.UTF8.GetBytes(request.ToString()));

            await Task.Delay(1000);

            return channel.AllSentMessages.ToList();
        }

        /// <summary>
        /// Builds an in-dialog REFER request for the agent's established dialog.
        /// </summary>
        private static SIPRequest CreateReferRequest(SIPDialogue dialogue, IPEndPoint channelEndPoint, string referTo)
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
            header.MaxForwards = 70;

            if (referTo != null)
            {
                header.ReferTo = referTo;
            }

            return request;
        }

        #region Transferor role.

        /// <summary>
        /// Verifies the REFER request generated for an attended transfer carries the consulted dialogue's
        /// details in the Refer-To header's Replaces parameter. The Replaces triple is expressed from the
        /// Transfer Target's point of view, its local tag is our remote tag and vice versa, and the
        /// SIPDialogue class already normalises local and remote so the result is the same no matter which
        /// way the consulted call was placed.
        /// </summary>
        [Theory]
        [InlineData(SIPCallDirection.Out, 6081)]
        [InlineData(SIPCallDirection.In, 6082)]
        public async Task AttendedTransferReferCarriesConsultedDialogueReplaces(SIPCallDirection direction, int port)
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

                var consulted = CreateConsultedDialogue(direction);
                var referRequest = await GetSentReferRequestAsync(agent, channel, consulted);

                var referTo = SIPUserField.ParseSIPUserField(referRequest.Header.ReferTo);
                Assert.Equal(consulted.RemoteTarget.ToParameterlessString(), referTo.URI.ToParameterlessString());

                Assert.True(referTo.URI.Headers.Has(SIPHeaderAncillary.SIP_REFER_REPLACES), "The Refer-To header did not have a Replaces parameter.");

                var replaces = SIPReplacesParameter.Parse(referTo.URI.Headers.Get(SIPHeaderAncillary.SIP_REFER_REPLACES));
                Assert.NotNull(replaces);
                Assert.Equal(consulted.CallId, replaces.CallID);
                Assert.Equal(consulted.RemoteTag, replaces.ToTag);
                Assert.Equal(consulted.LocalTag, replaces.FromTag);
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies the Referred-By header on an attended transfer REFER identifies the Transferor, i.e.
        /// this user agent, which is the party requesting the transfer. RFC3892 uses Referred-By to
        /// identify the referrer so it must be our identity on the dialogue carrying the REFER and never
        /// the consulted call party's, regardless of which way the consulted call was placed.
        /// </summary>
        [Theory]
        [InlineData(SIPCallDirection.Out, 6083)]
        [InlineData(SIPCallDirection.In, 6084)]
        public async Task AttendedTransferReferredByIdentifiesTransferor(SIPCallDirection direction, int port)
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

                string expectedReferredBy = agent.Dialogue.LocalUserField.URI.ToParameterlessString();

                var consulted = CreateConsultedDialogue(direction);
                var referRequest = await GetSentReferRequestAsync(agent, channel, consulted);

                Assert.False(string.IsNullOrWhiteSpace(referRequest.Header.ReferredBy), "The REFER request did not have a Referred-By header.");

                var referredBy = SIPUserField.ParseSIPUserField(referRequest.Header.ReferredBy);
                Assert.Equal(expectedReferredBy, referredBy.URI.ToParameterlessString());

                // The consulted call party must never end up identified as the referrer.
                Assert.NotEqual(consulted.RemoteUserField.URI.ToParameterlessString(), referredBy.URI.ToParameterlessString());
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies a blind transfer REFER also identifies the Transferor. An application deciding whether
        /// to accept a transfer is handed the Referred-By value, and blind transfers are the ones that
        /// carry the toll fraud risk, so the header should not be exclusive to attended transfers.
        /// </summary>
        [Fact]
        public async Task BlindTransferReferredByIdentifiesTransferor()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6090);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);

                string expectedReferredBy = agent.Dialogue.LocalUserField.URI.ToParameterlessString();

                while (channel.AllSentMessages.TryTake(out _)) { }

                var cts = new CancellationTokenSource();
                await agent.BlindTransfer(SIPURI.ParseSIPURI("sip:transfer-target@127.0.0.1:6099"), TimeSpan.FromSeconds(1), cts.Token);

                var referRequest = GetSentRefer(channel);

                Assert.False(string.IsNullOrWhiteSpace(referRequest.Header.ReferredBy), "The REFER request did not have a Referred-By header.");

                var referredBy = SIPUserField.ParseSIPUserField(referRequest.Header.ReferredBy);
                Assert.Equal(expectedReferredBy, referredBy.URI.ToParameterlessString());
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies an attended transfer is not attempted when no consulted dialogue is supplied.
        /// </summary>
        [Fact]
        public async Task AttendedTransferWithNullTargetFails()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6085);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);

                while (channel.AllSentMessages.TryTake(out _)) { }

                var cts = new CancellationTokenSource();
                bool result = await agent.AttendedTransfer(null, TimeSpan.FromSeconds(1), cts.Token);

                Assert.False(result, "The attended transfer should not have been attempted.");
                Assert.DoesNotContain(channel.AllSentMessages, x => x.StartsWith("REFER "));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies an attended transfer is not attempted when the user agent has no established call.
        /// </summary>
        [Fact]
        public async Task AttendedTransferWithNoEstablishedCallFails()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6086);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                var cts = new CancellationTokenSource();
                bool result = await agent.AttendedTransfer(CreateConsultedDialogue(SIPCallDirection.Out), TimeSpan.FromSeconds(1), cts.Token);

                Assert.False(result, "The attended transfer should not have been attempted.");
                Assert.DoesNotContain(channel.AllSentMessages, x => x.StartsWith("REFER "));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        #endregion

        #region Transferee role.

        /// <summary>
        /// Verifies a REFER request without a mandatory Refer-To header is rejected with a Bad Request
        /// response.
        /// </summary>
        [Fact]
        public async Task ReferWithoutReferToRejectedWithBadRequest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6087);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);

                // A handler that accepts is set to prove the rejection is due to the missing header and
                // not the transfer acceptance decision.
                agent.OnTransferRequested += (referTo, referredBy) => true;

                var referRequest = CreateReferRequest(agent.Dialogue, channelEndPoint, null);
                var responses = await InjectRequestAsync(channel, channelEndPoint, referRequest);

                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 400"));
                Assert.DoesNotContain(responses, x => x.StartsWith("SIP/2.0 202"));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies a REFER request received on a dialog that is not in the confirmed state is rejected
        /// with a Call Leg/Transaction Does Not Exist response.
        /// </summary>
        [Fact]
        public async Task ReferOnUnconfirmedDialogRejected()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6088);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);

                agent.OnTransferRequested += (referTo, referredBy) => true;

                var referRequest = CreateReferRequest(agent.Dialogue, channelEndPoint, "<sip:transfer-target@127.0.0.1:6099>");

                // Take the dialog out of the confirmed state after building the request so it still matches.
                agent.Dialogue.DialogueState = SIPDialogueStateEnum.Early;

                var responses = await InjectRequestAsync(channel, channelEndPoint, referRequest);

                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 481"));
                Assert.DoesNotContain(responses, x => x.StartsWith("SIP/2.0 202"));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Checks whether the agent has sent an INVITE to the transfer destination used by the tests below.
        /// </summary>
        private static bool SentInviteToTarget(RecordingMockSIPChannel channel) =>
            channel.AllSentMessages.Any(x => x.StartsWith("INVITE sip:transfer-target@127.0.0.1:6099"));

        /// <summary>
        /// Verifies that with a confirmation handler the REFER request is accepted straight away, and that the
        /// transfer destination is not called while the confirmation is pending or once it's declined.
        /// </summary>
        [Fact]
        public async Task TransferConfirmDeclinedNotCarriedOut()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6094);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string askedReferTo = null;

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);

                agent.OnTransferRequested += (referTo, referredBy) => true;
                agent.OnTransferConfirmAsync += (referTo, referredBy) =>
                {
                    askedReferTo = referTo.URI.ToString();
                    return decision.Task;
                };

                var referRequest = CreateReferRequest(agent.Dialogue, channelEndPoint, "<sip:transfer-target@127.0.0.1:6099>");
                var responses = await InjectRequestAsync(channel, channelEndPoint, referRequest);

                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 202"));
                Assert.Equal("sip:transfer-target@127.0.0.1:6099", askedReferTo);
                Assert.False(SentInviteToTarget(channel), "The transfer destination was called before the transfer was confirmed.");

                decision.SetResult(false);
                await Task.Delay(500);

                Assert.False(SentInviteToTarget(channel), "The transfer destination was called after the transfer was declined.");
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that a transfer confirmed by the confirmation handler goes ahead and calls the transfer destination.
        /// </summary>
        [Fact]
        public async Task TransferConfirmedCallsDestination()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6095);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);

                agent.OnTransferRequested += (referTo, referredBy) => true;
                agent.OnTransferConfirmAsync += async (referTo, referredBy) =>
                {
                    await Task.Delay(200);
                    return true;
                };

                var referRequest = CreateReferRequest(agent.Dialogue, channelEndPoint, "<sip:transfer-target@127.0.0.1:6099>");
                var responses = await InjectRequestAsync(channel, channelEndPoint, referRequest);

                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 202"));
                Assert.True(SentInviteToTarget(channel), "The transfer destination was not called once the transfer was confirmed.");
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that a transfer confirmed after its call has ended is dropped rather than carried out.
        /// </summary>
        [Fact]
        public async Task TransferConfirmedAfterCallEndedDropped()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6096);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);

                agent.OnTransferRequested += (referTo, referredBy) => true;
                agent.OnTransferConfirmAsync += (referTo, referredBy) => decision.Task;

                var referRequest = CreateReferRequest(agent.Dialogue, channelEndPoint, "<sip:transfer-target@127.0.0.1:6099>");
                var responses = await InjectRequestAsync(channel, channelEndPoint, referRequest);
                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 202"));

                agent.Dialogue.DialogueState = SIPDialogueStateEnum.Terminated;
                decision.SetResult(true);
                await Task.Delay(500);

                Assert.False(SentInviteToTarget(channel), "The transfer destination was called after the call had ended.");
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }


        /// <summary>
        /// Verifies that when the transfer destination turns the call down (a 488, say) the call being
        /// transferred goes on: it isn't reported hung up, its media stays open and its dialog stays, and
        /// it's taken off hold. (It used to be ended as if the remote party had hung up, without a BYE,
        /// and the response to the re-INVITE taking it off hold then hit a null dialog.)
        /// </summary>
        [Fact]
        public async Task TransferToTargetRefusedOriginalCallGoesOn()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6097);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);
            bool hungup = false;
            var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var mediaSession = await EstablishDialogAsync(agent, channel, channelEndPoint);
                string originalCallId = agent.Dialogue.CallId;

                agent.OnCallHungup += _ => hungup = true;
                agent.OnTransferRequested += (referTo, referredBy) => true;
                agent.OnTransferToTargetFailed += _ => failed.TrySetResult(true);

                var referRequest = CreateReferRequest(agent.Dialogue, channelEndPoint, "<sip:transfer-target@127.0.0.1:6099>");
                var responses = await InjectRequestAsync(channel, channelEndPoint, referRequest);
                Assert.Contains(responses, x => x.StartsWith("SIP/2.0 202"));

                string inviteStr = channel.AllSentMessages.FirstOrDefault(x => x.StartsWith("INVITE sip:transfer-target@127.0.0.1:6099"));
                Assert.False(inviteStr == null, "The transfer destination was not called.");
                var invite = SIPRequest.ParseSIPRequest(SIPMessageBuffer.ParseSIPMessage(inviteStr, DummySIPEndPoint, DummySIPEndPoint));

                var refused = SIPResponse.GetResponse(invite, SIPResponseStatusCodesEnum.NotAcceptableHere, null);
                refused.Header.To.ToTag = CallProperties.CreateNewTag();
                channel.FireMessageReceived(new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint), DummySIPEndPoint, Encoding.UTF8.GetBytes(refused.ToString()));

                await Task.WhenAny(failed.Task, Task.Delay(5000));
                Assert.True(failed.Task.IsCompleted, "OnTransferToTargetFailed did not fire.");
                await Task.Delay(500);

                Assert.False(hungup, "The original call was reported hung up.");
                Assert.NotNull(agent.Dialogue);
                Assert.Equal(originalCallId, agent.Dialogue.CallId);
                Assert.False(mediaSession.IsClosed, "The original call's media was closed.");
                // Put on hold as the transfer started, and taken off it again.
                Assert.True(channel.AllSentMessages.Count(x => x.StartsWith("INVITE ") && x.Contains($"Call-ID: {originalCallId}")) >= 2,
                    "The original call wasn't taken off hold.");
                Assert.DoesNotContain(channel.AllSentMessages, x => x.StartsWith("BYE "));
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        #endregion

        #region Transfer Target role.

        /// <summary>
        /// Verifies that an INVITE with a Replaces header matching the established call raises the
        /// attended transfer requested event so the application can observe its call being taken over.
        /// </summary>
        [Fact]
        public async Task ReplacesInviteRaisesAttendedTransferRequested()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6089);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                await EstablishDialogAsync(agent, channel, channelEndPoint);

                var transferRequested = new TaskCompletionSource<SIPRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
                agent.OnAttendedTransferRequested += (req) => transferRequested.TrySetResult(req);

                var replacesInvite = CreateInviteRequest(CallProperties.CreateNewCallId(), channelEndPoint);
                replacesInvite.Header.Replaces =
                    $"{agent.Dialogue.CallId};to-tag={CallProperties.CreateNewTag()};from-tag={CallProperties.CreateNewTag()}";

                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                channel.FireMessageReceived(localEP, DummySIPEndPoint, Encoding.UTF8.GetBytes(replacesInvite.ToString()));

                await Task.WhenAny(transferRequested.Task, Task.Delay(5000));

                Assert.True(transferRequested.Task.IsCompleted, "The attended transfer requested event was not raised.");

                var raisedRequest = await transferRequested.Task;
                Assert.Equal(replacesInvite.Header.CallId, raisedRequest.Header.CallId);
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        #endregion
    }
}
