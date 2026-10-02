//-----------------------------------------------------------------------------
// Filename: UACInviteTransactionUnitTest.cs
//
// Description: Unit tests for the final responses an INVITE client transaction
// receives after it has completed.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 Oct 2026  Aaron Clauson   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class UACInviteTransactionUnitTest
    {
        private static readonly SIPEndPoint OutboundProxy = SIPEndPoint.ParseSIPEndPoint("udp:192.0.2.10:5060");
        private static readonly SIPEndPoint FarEnd = SIPEndPoint.ParseSIPEndPoint("udp:198.51.100.20:5060");

        /// <summary>
        /// Records each message the transport sends and where it was sent.
        /// </summary>
        private class DestinationRecordingChannel : SIPChannel
        {
            public ConcurrentQueue<(SIPEndPoint Destination, string Message)> Sent { get; } = new ConcurrentQueue<(SIPEndPoint, string)>();

            public DestinationRecordingChannel()
            {
                ListeningIPAddress = IPAddress.Loopback;
                Port = 5060;
                SIPProtocol = SIPProtocolsEnum.udp;
                ID = Crypto.GetRandomInt(5).ToString();
            }

            public override Task<SocketError> SendAsync(SIPEndPoint destinationEndPoint, byte[] buffer, bool canInitiateConnection, string connectionIDHint)
            {
                Sent.Enqueue((destinationEndPoint, Encoding.UTF8.GetString(buffer)));
                return Task.FromResult(SocketError.Success);
            }

            public override Task<SocketError> SendSecureAsync(SIPEndPoint destinationEndPoint, byte[] buffer, string serverCertificate, bool canInitiateConnection, string connectionIDHint)
                => throw new NotImplementedException();

            public override void Close() { }
            public override void Dispose() { }
            public override bool HasConnection(string connectionID) => false;
            public override bool HasConnection(SIPEndPoint remoteEndPoint) => false;
            public override bool HasConnection(Uri serverUri) => false;
            public override bool IsAddressFamilySupported(AddressFamily addresFamily) => true;
            public override bool IsProtocolSupported(SIPProtocolsEnum protocol) => protocol == SIPProtocolsEnum.udp;
        }

        private static SIPRequest GetInvite()
        {
            var uri = SIPURI.ParseSIPURI("sip:music@198.51.100.20");
            var invite = new SIPRequest(SIPMethodsEnum.INVITE, uri);
            invite.Header = new SIPHeader(
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:aaron@sipsorcery.com"), "fromtag"),
                new SIPToHeader(null, uri, null),
                1,
                CallProperties.CreateNewCallId());
            invite.Header.CSeqMethod = SIPMethodsEnum.INVITE;
            invite.Header.Vias.PushViaHeader(new SIPViaHeader(new IPEndPoint(IPAddress.Loopback, 5060), CallProperties.CreateBranchId()));
            return invite;
        }

        private static SIPResponse GetResponse(SIPRequest invite, SIPResponseStatusCodesEnum status, string toTag)
        {
            var response = SIPResponse.GetResponse(invite, status, null);

            // Its own To header: GetResponse shares the request's, and tagging that would retag every response
            // built from the same request.
            response.Header.To = new SIPToHeader(null, invite.URI, toTag);
            response.Header.Contact = new System.Collections.Generic.List<SIPContactHeader>
            {
                new SIPContactHeader(null, SIPURI.ParseSIPURI($"sip:{toTag}@{FarEnd.GetIPEndPoint()}"))
            };
            return response;
        }

        private static (SIPTransport Transport, DestinationRecordingChannel Channel, UACInviteTransaction Transaction, SIPRequest Invite) Create(SIPEndPoint outboundProxy)
        {
            var transport = new SIPTransport();
            var channel = new DestinationRecordingChannel();
            transport.AddSIPChannel(channel);

            var invite = GetInvite();
            var transaction = new UACInviteTransaction(transport, invite, outboundProxy);
            return (transport, channel, transaction, invite);
        }

        private static SIPRequest[] SentAcks(DestinationRecordingChannel channel)
            => channel.Sent.Select(s => SIPRequest.ParseSIPRequest(s.Message)).Where(r => r.Method == SIPMethodsEnum.ACK).ToArray();

        /// <summary>
        /// A late retransmission of the INVITE reached a server after it had answered, and the server, no longer
        /// holding the transaction, rejected it as a merged request. The 482 is acknowledged with an ACK for the
        /// 482 itself - its To tag and the INVITE's branch - not with a repeat of the ACK for the 200, which left
        /// the server retransmitting its 482 until it timed out.
        /// </summary>
        [Fact]
        public async Task ADifferentNonSuccessFinalResponseAfterA2xxIsAcknowledgedWithItsOwnAck()
        {
            var (transport, channel, transaction, invite) = Create(OutboundProxy);

            try
            {
                await transaction.GotResponse(transport.GetSIPChannels().First().ListeningSIPEndPoint, FarEnd, GetResponse(invite, SIPResponseStatusCodesEnum.Ok, "answered"));
                Assert.Equal(SIPTransactionStatesEnum.Confirmed, transaction.TransactionState);
                Assert.Single(SentAcks(channel));

                await transaction.GotResponse(transport.GetSIPChannels().First().ListeningSIPEndPoint, FarEnd, GetResponse(invite, SIPResponseStatusCodesEnum.LoopDetected, "merged"));

                var acks = SentAcks(channel);
                Assert.Equal(2, acks.Length);

                var mergedAck = acks[1];
                Assert.Equal("merged", mergedAck.Header.To.ToTag);
                Assert.Equal(invite.Header.Vias.TopViaHeader.Branch, mergedAck.Header.Vias.TopViaHeader.Branch);
                Assert.Equal(invite.URI.ToString(), mergedAck.URI.ToString());
                Assert.Equal(1, mergedAck.Header.CSeq);

                Assert.Equal("answered", transaction.AckRequest.Header.To.ToTag, StringComparer.Ordinal);
                Assert.Equal(0, transaction.AckRetransmits);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// A retransmission of the 200 that completed the transaction still has the same ACK repeated.
        /// </summary>
        [Fact]
        public async Task ARetransmitted2xxRepeatsTheStoredAck()
        {
            var (transport, channel, transaction, invite) = Create(OutboundProxy);

            try
            {
                var ok = GetResponse(invite, SIPResponseStatusCodesEnum.Ok, "answered");
                await transaction.GotResponse(transport.GetSIPChannels().First().ListeningSIPEndPoint, FarEnd, ok);
                await transaction.GotResponse(transport.GetSIPChannels().First().ListeningSIPEndPoint, FarEnd, GetResponse(invite, SIPResponseStatusCodesEnum.Ok, "answered"));

                var acks = SentAcks(channel);
                Assert.Equal(2, acks.Length);
                Assert.All(acks, a => Assert.Equal("answered", a.Header.To.ToTag));
                Assert.Equal(acks[0].Header.Vias.TopViaHeader.Branch, acks[1].Header.Vias.TopViaHeader.Branch);
                Assert.Equal(1, transaction.AckRetransmits);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// A repeated ACK goes where the first one went: through the outbound proxy, not straight to the far end's
        /// Contact.
        /// </summary>
        [Fact]
        public async Task ARepeatedAckIsSentThroughTheOutboundProxy()
        {
            var (transport, channel, transaction, invite) = Create(OutboundProxy);

            try
            {
                await transaction.GotResponse(transport.GetSIPChannels().First().ListeningSIPEndPoint, FarEnd, GetResponse(invite, SIPResponseStatusCodesEnum.Ok, "answered"));
                await transaction.GotResponse(transport.GetSIPChannels().First().ListeningSIPEndPoint, FarEnd, GetResponse(invite, SIPResponseStatusCodesEnum.Ok, "answered"));

                var destinations = channel.Sent.Select(s => s.Destination.GetIPEndPoint()).ToArray();
                Assert.Equal(2, destinations.Length);
                Assert.All(destinations, d => Assert.Equal(OutboundProxy.GetIPEndPoint(), d));
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// A 2xx from another fork, with a different To tag, cannot be acknowledged by the first 2xx's ACK and is
        /// left to the transaction user rather than answered with it.
        /// </summary>
        [Fact]
        public async Task A2xxFromAnotherForkIsNotSentTheFirstForksAck()
        {
            var (transport, channel, transaction, invite) = Create(OutboundProxy);

            try
            {
                await transaction.GotResponse(transport.GetSIPChannels().First().ListeningSIPEndPoint, FarEnd, GetResponse(invite, SIPResponseStatusCodesEnum.Ok, "first"));
                await transaction.GotResponse(transport.GetSIPChannels().First().ListeningSIPEndPoint, FarEnd, GetResponse(invite, SIPResponseStatusCodesEnum.Ok, "second"));

                Assert.Single(SentAcks(channel));
                Assert.Equal(0, transaction.AckRetransmits);
            }
            finally
            {
                transport.Shutdown();
            }
        }
    }
}
