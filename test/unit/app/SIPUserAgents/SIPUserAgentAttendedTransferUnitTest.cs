//-----------------------------------------------------------------------------
// Filename: SIPUserAgentAttendedTransferUnitTest.cs
//
// Description: Unit tests for SIPUserAgent attended transfer handling, verifying
// that when multiple agents share a SIPTransport, only the agent whose dialog
// matches the Replaces header acts on the INVITE. Non-matching agents must
// silently ignore the request (not respond with 400).
//
// Author(s):
// Contributors
//
// History:
// 16 Feb 2026  Contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.SIP.App.UnitTests
{
    /// <summary>
    /// A MockSIPChannel variant that records ALL sent messages, not just the last one.
    /// This is needed to deterministically detect whether a 400 was ever sent.
    /// </summary>
    internal class RecordingMockSIPChannel : SIPChannel
    {
        public ConcurrentBag<string> AllSentMessages { get; } = new ConcurrentBag<string>();
        public AutoResetEvent SIPMessageSent { get; }

        public RecordingMockSIPChannel(IPEndPoint channelEndPoint)
        {
            ListeningIPAddress = channelEndPoint.Address;
            Port = channelEndPoint.Port;
            SIPProtocol = SIPProtocolsEnum.udp;
            ID = Crypto.GetRandomInt(5).ToString();
            SIPMessageSent = new AutoResetEvent(false);
        }

        public override Task<SocketError> SendAsync(SIPEndPoint destinationEndPoint, byte[] buffer, bool canInitiateConnection, string connectionIDHint)
        {
            string message = Encoding.UTF8.GetString(buffer);
            AllSentMessages.Add(message);
            SIPMessageSent.Set();
            return Task.FromResult(SocketError.Success);
        }

        public override Task<SocketError> SendSecureAsync(SIPEndPoint destinationEndPoint, byte[] buffer, string serverCertificate, bool canInitiateConnection, string connectionIDHint)
        {
            throw new NotImplementedException();
        }

        public override void Close() { }
        public override void Dispose() { }
        public override bool HasConnection(string connectionID) => throw new NotImplementedException();
        public override bool HasConnection(SIPEndPoint remoteEndPoint) => throw new NotImplementedException();
        public override bool HasConnection(Uri serverUri) => throw new NotImplementedException();
        public override bool IsAddressFamilySupported(AddressFamily addresFamily) => true;
        public override bool IsProtocolSupported(SIPProtocolsEnum protocol) => true;

        public void FireMessageReceived(SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, byte[] sipMsgBuffer)
        {
            SIPMessageReceived.Invoke(this, localEndPoint, remoteEndPoint, sipMsgBuffer);
        }
    }

    [Trait("Category", "unit")]
    public class SIPUserAgentAttendedTransferUnitTest
    {
        private readonly ILogger logger;

        public SIPUserAgentAttendedTransferUnitTest(ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Creates a minimal SIP INVITE request with an SDP body so that SIPUserAgent.Answer
        /// can process it successfully and establish a dialog.
        /// </summary>
        private static SIPRequest CreateInviteRequest(string callId, string fromTag, string toTag,
            IPEndPoint channelEndPoint)
        {
            var uri = SIPURI.ParseSIPURI($"sip:user@{channelEndPoint}");
            var toHeader = new SIPToHeader(null, uri, toTag);
            var fromHeader = new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:caller@127.0.0.1"), fromTag);

            var request = new SIPRequest(SIPMethodsEnum.INVITE, uri);
            var header = new SIPHeader(fromHeader, toHeader, 1, callId);
            request.Header = header;
            header.CSeqMethod = SIPMethodsEnum.INVITE;
            header.Vias.PushViaHeader(new SIPViaHeader(channelEndPoint, CallProperties.CreateBranchId()));
            header.Contact = new List<SIPContactHeader>
            {
                new SIPContactHeader(null, uri)
            };
            header.ContentType = SIPSorcery.Net.SDP.SDP_MIME_CONTENTTYPE;
            header.MaxForwards = 70;

            // Minimal SDP body so Answer() can process the offer.
            string sdpBody =
                "v=0\r\n" +
                $"o=- 0 0 IN IP4 {channelEndPoint.Address}\r\n" +
                "s=-\r\n" +
                $"c=IN IP4 {channelEndPoint.Address}\r\n" +
                "t=0 0\r\n" +
                "m=audio 49170 RTP/AVP 0\r\n" +
                "a=rtpmap:0 PCMU/8000\r\n";

            request.Body = sdpBody;

            return request;
        }

        /// <summary>
        /// Simulates answering an incoming call by injecting an INVITE via the channel,
        /// waiting for OnIncomingCall, and then calling Answer to establish the dialog.
        /// Returns the Call-ID that was used.
        /// </summary>
        private async Task<string> EstablishDialogAsync(
            SIPUserAgent agent,
            RecordingMockSIPChannel channel,
            IPEndPoint channelEndPoint)
        {
            string callId = CallProperties.CreateNewCallId();
            string fromTag = CallProperties.CreateNewTag();
            string toTag = CallProperties.CreateNewTag();

            var incomingCallReceived = new TaskCompletionSource<SIPRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

            agent.OnIncomingCall += (ua, req) =>
            {
                incomingCallReceived.TrySetResult(req);
            };

            var inviteRequest = CreateInviteRequest(callId, fromTag, toTag, channelEndPoint);
            var rawBytes = Encoding.UTF8.GetBytes(inviteRequest.ToString());

            var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
            var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

            channel.FireMessageReceived(localEP, remoteEP, rawBytes);

            // Wait for the incoming call handler to fire.
            await Task.WhenAny(incomingCallReceived.Task, Task.Delay(5000));
            Assert.True(incomingCallReceived.Task.IsCompleted, "OnIncomingCall did not fire within timeout.");

            var request = incomingCallReceived.Task.Result;

            // Answer the call to establish the dialog.
            var uas = agent.AcceptCall(request);
            var mediaSession = new MockMediaSession();
            bool answered = await agent.Answer(uas, mediaSession);
            Assert.True(answered, "Failed to answer call and establish dialog.");
            Assert.NotNull(agent.Dialogue);

            return callId;
        }

        /// <summary>
        /// Verifies that a SIPUserAgent whose dialog does NOT match the Replaces Call-ID
        /// silently ignores the attended transfer INVITE — no 400 Bad Request is sent.
        /// Before the fix for #1459, the agent would call AcceptCall (sending 100 Trying
        /// and 180 Ringing) and then reject with 400 Bad Request when the Replaces
        /// Call-ID didn't match. This 400 races against the correct agent's acceptance
        /// when multiple agents share a SIPTransport.
        /// </summary>
        [Fact]
        public async Task NonMatchingAgentIgnoresReplacesInvite()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6060);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            // Create a single agent — this will be the non-matching agent.
            var agent = new SIPUserAgent(transport, null);

            try
            {
                string callId = await EstablishDialogAsync(agent, channel, channelEndPoint);
                logger.LogDebug("Agent dialog Call-ID: {CallId}", callId);

                // Allow time for all dialog establishment messages (100 Trying, 180 Ringing, 200 OK)
                // to be fully sent before clearing.
                await Task.Delay(500);

                // Clear any messages from the dialog establishment phase.
                while (channel.AllSentMessages.TryTake(out _)) { }
                channel.SIPMessageSent.Reset();

                // Inject an attended transfer INVITE with Replaces targeting a DIFFERENT Call-ID
                // that does NOT match this agent's dialog.
                string nonMatchingCallId = CallProperties.CreateNewCallId();
                Assert.NotEqual(callId, nonMatchingCallId);

                string transferCallId = CallProperties.CreateNewCallId();
                string replacesValue = $"{nonMatchingCallId};to-tag={CallProperties.CreateNewTag()};from-tag={CallProperties.CreateNewTag()}";

                var transferInvite = CreateInviteRequest(transferCallId, CallProperties.CreateNewTag(), null, channelEndPoint);
                transferInvite.Header.Replaces = replacesValue;

                var rawBytes = Encoding.UTF8.GetBytes(transferInvite.ToString());
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, rawBytes);

                // Give the agent time to process the request.
                // With the bug, it would send 100 Trying + 180 Ringing + 400 Bad Request.
                // With the fix, it should send nothing for this transfer Call-ID.
                await Task.Delay(1500);

                // Filter to only messages related to the transfer INVITE (by its Call-ID).
                // Other messages (e.g. 200 OK retransmissions for the established dialog) are
                // expected because the UAS transaction retransmits until ACK is received.
                var transferResponses = new List<string>();
                foreach (string msg in channel.AllSentMessages)
                {
                    if (msg.Contains(transferCallId))
                    {
                        logger.LogDebug("Response to transfer INVITE:\n{Message}", msg);
                        transferResponses.Add(msg);
                    }
                }

                // The non-matching agent must not send any response for the transfer INVITE.
                Assert.Empty(transferResponses);
            }
            finally
            {
                agent.Dispose();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that the agent whose dialog matches the Replaces Call-ID does process
        /// the attended transfer (sends provisional responses like 100 Trying).
        /// </summary>
        [Fact]
        public async Task MatchingAgentProcessesReplacesInvite()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6061);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                string callId = await EstablishDialogAsync(agent, channel, channelEndPoint);
                logger.LogDebug("Established dialog with Call-ID: {CallId}", callId);

                // Allow time for all dialog establishment messages to be fully sent before clearing.
                await Task.Delay(500);

                // Clear messages from dialog establishment.
                while (channel.AllSentMessages.TryTake(out _)) { }
                channel.SIPMessageSent.Reset();

                // Inject an attended transfer INVITE with Replaces targeting this agent's dialog.
                string transferCallId = CallProperties.CreateNewCallId();
                string replacesValue = $"{callId};to-tag={CallProperties.CreateNewTag()};from-tag={CallProperties.CreateNewTag()}";

                var transferInvite = CreateInviteRequest(transferCallId, CallProperties.CreateNewTag(), null, channelEndPoint);
                transferInvite.Header.Replaces = replacesValue;

                var rawBytes = Encoding.UTF8.GetBytes(transferInvite.ToString());
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, rawBytes);

                // The matching agent should send provisional responses (100 Trying, 180 Ringing)
                // from AcceptCall, which is called inside SIPTransportRequestReceived.
                bool messageSent = channel.SIPMessageSent.WaitOne(5000);
                Assert.True(messageSent, "Expected matching agent to send a SIP response for the Replaces INVITE.");

                // Allow time for all messages to be sent.
                await Task.Delay(500);

                // Verify that responses were sent and none is a 400 Bad Request.
                Assert.NotEmpty(channel.AllSentMessages);

                foreach (string msg in channel.AllSentMessages)
                {
                    logger.LogDebug("SIP message sent:\n{Message}", msg);
                    Assert.DoesNotContain("400 Bad Request", msg);
                }
            }
            finally
            {
                agent.Dispose();
                transport.Shutdown();
            }
        }
    }
}
