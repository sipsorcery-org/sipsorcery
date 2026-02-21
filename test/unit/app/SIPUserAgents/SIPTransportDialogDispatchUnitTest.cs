//-----------------------------------------------------------------------------
// Filename: SIPTransportDialogDispatchUnitTest.cs
//
// Description: Unit tests for SIPTransport dialog dispatch — verifying that
// Replaces INVITEs are dispatched to the correct owner and 481 responses
// are sent for non-matching Replaces headers (RFC 3891).
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
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.SIP.App.UnitTests
{
    /// <summary>
    /// A recording mock channel that captures all sent messages.
    /// </summary>
    internal class DispatchRecordingChannel : SIPChannel
    {
        public ConcurrentBag<string> AllSentMessages { get; } = new ConcurrentBag<string>();
        public AutoResetEvent SIPMessageSent { get; }

        public DispatchRecordingChannel(IPEndPoint channelEndPoint)
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

        public override Task<SocketError> SendSecureAsync(SIPEndPoint destinationEndPoint, byte[] buffer, string serverCertificate, bool canInitiateConnection, string connectionIDHint) => throw new NotImplementedException();
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

    /// <summary>
    /// A test ISIPDialogOwner that records dispatched requests.
    /// </summary>
    internal class TestDialogOwner : ISIPDialogOwner
    {
        public string DialogCallID { get; set; }
        public string DialogLocalTag { get; set; }
        public string DialogRemoteTag { get; set; }

        public ConcurrentQueue<SIPRequest> ReceivedRequests { get; } = new ConcurrentQueue<SIPRequest>();
        public ManualResetEvent RequestReceived { get; } = new ManualResetEvent(false);

        public Task OnDialogRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            ReceivedRequests.Enqueue(sipRequest);
            RequestReceived.Set();
            return Task.CompletedTask;
        }
    }

    [Trait("Category", "DialogDispatch")]
    public class SIPTransportDialogDispatchUnitTest
    {
        private readonly ILogger logger;

        public SIPTransportDialogDispatchUnitTest(ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

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

        private static SIPRequest CreateByeRequest(string callId, string fromTag, string toTag,
            IPEndPoint channelEndPoint)
        {
            var uri = SIPURI.ParseSIPURI($"sip:user@{channelEndPoint}");
            var toHeader = new SIPToHeader(null, uri, toTag);
            var fromHeader = new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:caller@127.0.0.1"), fromTag);

            var request = new SIPRequest(SIPMethodsEnum.BYE, uri);
            var header = new SIPHeader(fromHeader, toHeader, 2, callId);
            request.Header = header;
            header.CSeqMethod = SIPMethodsEnum.BYE;
            header.Vias.PushViaHeader(new SIPViaHeader(channelEndPoint, CallProperties.CreateBranchId()));
            header.MaxForwards = 70;

            return request;
        }

        /// <summary>
        /// Verifies that a Replaces INVITE is dispatched to the owner whose
        /// Call-ID and tags match.
        /// </summary>
        [Fact]
        public void ReplacesInviteDispatchedToMatchingOwner()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 7060);
            var channel = new DispatchRecordingChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            string existingCallId = "existing-call-123";
            string localTag = "local-tag-1";
            string remoteTag = "remote-tag-1";

            var owner = new TestDialogOwner
            {
                DialogCallID = existingCallId,
                DialogLocalTag = localTag,
                DialogRemoteTag = remoteTag
            };
            transport.RegisterDialogOwner(existingCallId, owner);

            try
            {
                // Create a Replaces INVITE targeting the existing dialog.
                string transferCallId = CallProperties.CreateNewCallId();
                string replacesValue = $"{existingCallId};to-tag={localTag};from-tag={remoteTag}";

                var invite = CreateInviteRequest(transferCallId, CallProperties.CreateNewTag(), null, channelEndPoint);
                invite.Header.Replaces = replacesValue;

                var rawBytes = Encoding.UTF8.GetBytes(invite.ToString());
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, rawBytes);

                // Wait for the owner to receive the request.
                bool received = owner.RequestReceived.WaitOne(3000);
                Assert.True(received, "Owner should have received the Replaces INVITE.");
                Assert.Single(owner.ReceivedRequests);

                owner.ReceivedRequests.TryDequeue(out var dispatchedReq);
                Assert.Equal(SIPMethodsEnum.INVITE, dispatchedReq.Method);
                Assert.Equal(transferCallId, dispatchedReq.Header.CallId);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that a Replaces INVITE with no matching dialog owner gets a 481 response.
        /// </summary>
        [Fact]
        public void ReplacesInviteNoMatch481()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 7061);
            var channel = new DispatchRecordingChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            try
            {
                // No dialog owners registered — Replaces should result in 481.
                string transferCallId = CallProperties.CreateNewCallId();
                string replacesValue = $"nonexistent-call;to-tag=abc;from-tag=def";

                var invite = CreateInviteRequest(transferCallId, CallProperties.CreateNewTag(), null, channelEndPoint);
                invite.Header.Replaces = replacesValue;

                var rawBytes = Encoding.UTF8.GetBytes(invite.ToString());
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, rawBytes);

                // Wait for the 481 response.
                bool sent = channel.SIPMessageSent.WaitOne(3000);
                Assert.True(sent, "Transport should have sent a 481 response.");

                bool found481 = false;
                foreach (string msg in channel.AllSentMessages)
                {
                    if (msg.Contains("481"))
                    {
                        found481 = true;
                        break;
                    }
                }
                Assert.True(found481, "Expected 481 response for Replaces INVITE with no matching dialog.");
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that a Replaces INVITE with matching Call-ID but wrong tags gets a 481 response.
        /// </summary>
        [Fact]
        public void ReplacesInviteTagMismatch481()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 7062);
            var channel = new DispatchRecordingChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            string existingCallId = "existing-call-456";
            var owner = new TestDialogOwner
            {
                DialogCallID = existingCallId,
                DialogLocalTag = "correct-local",
                DialogRemoteTag = "correct-remote"
            };
            transport.RegisterDialogOwner(existingCallId, owner);

            try
            {
                // Replaces header with matching Call-ID but wrong tags.
                string transferCallId = CallProperties.CreateNewCallId();
                string replacesValue = $"{existingCallId};to-tag=wrong-local;from-tag=wrong-remote";

                var invite = CreateInviteRequest(transferCallId, CallProperties.CreateNewTag(), null, channelEndPoint);
                invite.Header.Replaces = replacesValue;

                var rawBytes = Encoding.UTF8.GetBytes(invite.ToString());
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, rawBytes);

                bool sent = channel.SIPMessageSent.WaitOne(3000);
                Assert.True(sent, "Transport should have sent a 481 response for tag mismatch.");

                bool found481 = false;
                foreach (string msg in channel.AllSentMessages)
                {
                    if (msg.Contains("481"))
                    {
                        found481 = true;
                        break;
                    }
                }
                Assert.True(found481, "Expected 481 response for Replaces INVITE with tag mismatch.");

                // Owner should NOT have received the request.
                Assert.Empty(owner.ReceivedRequests);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that an in-dialog BYE request is dispatched directly to the registered owner.
        /// </summary>
        [Fact]
        public void InDialogByeDispatchedToOwner()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 7063);
            var channel = new DispatchRecordingChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            string callId = "dialog-call-789";
            string localTag = "local-x";
            string remoteTag = "remote-y";

            var owner = new TestDialogOwner
            {
                DialogCallID = callId,
                DialogLocalTag = localTag,
                DialogRemoteTag = remoteTag
            };
            transport.RegisterDialogOwner(callId, owner);

            try
            {
                // Create an in-dialog BYE (both from-tag and to-tag present).
                var bye = CreateByeRequest(callId, remoteTag, localTag, channelEndPoint);

                var rawBytes = Encoding.UTF8.GetBytes(bye.ToString());
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, rawBytes);

                bool received = owner.RequestReceived.WaitOne(3000);
                Assert.True(received, "Owner should have received the in-dialog BYE.");

                owner.ReceivedRequests.TryDequeue(out var dispatchedReq);
                Assert.Equal(SIPMethodsEnum.BYE, dispatchedReq.Method);
                Assert.Equal(callId, dispatchedReq.Header.CallId);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that an INVITE with more than one Replaces header gets a 400 Bad Request
        /// per RFC 3891 Section 3.
        /// </summary>
        [Fact]
        public void MultipleReplacesHeaders400()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 7066);
            var channel = new DispatchRecordingChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            try
            {
                string transferCallId = CallProperties.CreateNewCallId();

                // Build a raw SIP message with two Replaces headers.
                string replacesValue1 = $"call-a;to-tag=t1;from-tag=f1";
                string replacesValue2 = $"call-b;to-tag=t2;from-tag=f2";

                var invite = CreateInviteRequest(transferCallId, CallProperties.CreateNewTag(), null, channelEndPoint);

                // Inject the raw message with duplicate Replaces headers by manipulating the serialized form.
                string raw = invite.ToString();
                string replacesLine = $"Replaces: {replacesValue1}\r\nReplaces: {replacesValue2}\r\n";
                raw = raw.Replace("Content-Type:", replacesLine + "Content-Type:");

                var rawBytes = Encoding.UTF8.GetBytes(raw);
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, rawBytes);

                bool sent = channel.SIPMessageSent.WaitOne(3000);
                Assert.True(sent, "Transport should have sent a 400 response for multiple Replaces headers.");

                bool found400 = false;
                foreach (string msg in channel.AllSentMessages)
                {
                    if (msg.Contains("400"))
                    {
                        found400 = true;
                        break;
                    }
                }
                Assert.True(found400, "Expected 400 Bad Request per RFC 3891 when multiple Replaces headers present.");
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Verifies that a new INVITE (no dialog, no Replaces) still broadcasts via the
        /// SIPTransportRequestReceived event.
        /// </summary>
        [Fact]
        public void NewInviteStillBroadcasts()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 7064);
            var channel = new DispatchRecordingChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var broadcastReceived = new ManualResetEvent(false);
            SIPRequest broadcastRequest = null;

            transport.SIPTransportRequestReceived += (localEP, remoteEP, req) =>
            {
                broadcastRequest = req;
                broadcastReceived.Set();
                return Task.CompletedTask;
            };

            try
            {
                // Create a new INVITE with no Replaces and a Call-ID not in the registry.
                var invite = CreateInviteRequest(
                    CallProperties.CreateNewCallId(),
                    CallProperties.CreateNewTag(),
                    null,
                    channelEndPoint);

                var rawBytes = Encoding.UTF8.GetBytes(invite.ToString());
                var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint);
                var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));

                channel.FireMessageReceived(localEP, remoteEP, rawBytes);

                bool received = broadcastReceived.WaitOne(3000);
                Assert.True(received, "New INVITE should have been broadcast via SIPTransportRequestReceived.");
                Assert.Equal(SIPMethodsEnum.INVITE, broadcastRequest.Method);
            }
            finally
            {
                transport.Shutdown();
            }
        }
    }
}
