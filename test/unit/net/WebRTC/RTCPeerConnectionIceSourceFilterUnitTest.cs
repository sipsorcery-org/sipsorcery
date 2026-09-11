//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionIceSourceFilterUnitTest.cs
//
// Description: Regression tests for the ICE source-address filter applied
// to non-STUN packets received on an RTCPeerConnection
// (issue #1559 -- DTLS handshake DoS via off-path packet injection).
//
// The filter forwards a non-STUN packet only if its source matches one of
// the KNOWN remote ICE candidates: the candidates advertised in the remote
// SDP plus peer-reflexive candidates discovered via authenticated STUN.
// This blocks an off-path attacker who guesses the local port (issue #1559)
// while still accepting media that legitimately arrives from a valid-but-not-
// yet-nominated pair or an asymmetric path during ICE negotiation
// (issue #1731). The check lives on RtpIceChannel.IsKnownRemoteEndPoint.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 29 Apr 2026  Claude          Created (selected-pair filter, issue #1559).
// 15 Jun 2026  Claude          Switched to any-known-remote-candidate (issue #1731).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionIceSourceFilterUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPeerConnectionIceSourceFilterUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Before any remote candidate is known there is nothing to match against, so the filter
        /// must reject every non-STUN packet (the conservative pre-negotiation behaviour).
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_NoRemoteCandidates_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            var anyEP = new IPEndPoint(IPAddress.Parse("198.51.100.10"), 50000);

            Assert.False(channel.IsKnownRemoteEndPoint(anyEP),
                "with no known remote candidates the filter must reject all sources");
        }

        /// <summary>
        /// A packet whose source matches a known remote candidate must be accepted.
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_MatchingKnownCandidate_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            var remoteEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820);
            AddRemoteCandidate(channel, remoteEP, RTCIceCandidateType.host);

            Assert.True(channel.IsKnownRemoteEndPoint(remoteEP),
                "a source matching a known remote candidate must pass the filter");
        }

        /// <summary>
        /// The load-bearing case for issue #1731: media legitimately arrives from a valid candidate
        /// that is NOT the nominated pair (e.g. before nomination completes, an asymmetric path, or a
        /// second track). The previous selected-pair-only filter dropped these; they must now pass.
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_KnownButNotNominatedCandidate_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            var nominatedEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820);
            var otherKnownEP = new IPEndPoint(IPAddress.Parse("203.0.113.6"), 51821);
            AddRemoteCandidate(channel, nominatedEP, RTCIceCandidateType.host);
            AddRemoteCandidate(channel, otherKnownEP, RTCIceCandidateType.srflx);
            SetNominatedRemoteEndpoint(channel, nominatedEP);

            Assert.True(channel.IsKnownRemoteEndPoint(otherKnownEP),
                "a known but not-nominated remote candidate must pass the filter (issue #1731)");
        }

        /// <summary>
        /// Peer-reflexive candidates discovered via authenticated STUN are added to the remote
        /// candidate set, so media arriving from one must be accepted.
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_PeerReflexiveCandidate_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            var prflxEP = new IPEndPoint(IPAddress.Parse("203.0.113.20"), 40000);
            AddRemoteCandidate(channel, prflxEP, RTCIceCandidateType.prflx);

            Assert.True(channel.IsKnownRemoteEndPoint(prflxEP),
                "a peer-reflexive remote candidate must pass the filter");
        }

        /// <summary>
        /// A source that is not any known candidate must be rejected. This is the load-bearing case
        /// for issue #1559 -- an off-path attacker on a different IP cannot inject DTLS packets.
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_UnknownAddress_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            var knownEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820);
            var attackerEP = new IPEndPoint(IPAddress.Parse("198.51.100.99"), 51820);
            AddRemoteCandidate(channel, knownEP, RTCIceCandidateType.host);

            Assert.False(channel.IsKnownRemoteEndPoint(attackerEP),
                "an off-path source address must be rejected (issue #1559)");
        }

        /// <summary>
        /// Right IP but wrong port must be rejected -- an attacker who spoofed the peer's IP but is
        /// using a different source port.
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_UnknownPort_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            var knownEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820);
            var wrongPortEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51821);
            AddRemoteCandidate(channel, knownEP, RTCIceCandidateType.host);

            Assert.False(channel.IsKnownRemoteEndPoint(wrongPortEP),
                "a source on a known address but a wrong port must be rejected (issue #1559)");
        }

        /// <summary>
        /// A null remote endpoint is treated as "no source", which is rejected.
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_NullRemoteEndpoint_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            AddRemoteCandidate(channel, new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820), RTCIceCandidateType.host);

            Assert.False(channel.IsKnownRemoteEndPoint(null));
        }

        /// <summary>
        /// IPv4-mapped IPv6 source (::ffff:x.x.x.x) matching a known IPv4 candidate must pass -- the
        /// typical dual-stack socket scenario (issue #1603).
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_IPv4MappedIPv6SourceMatchesIPv4Candidate_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            AddRemoteCandidate(channel, new IPEndPoint(IPAddress.Parse("192.168.1.123"), 57256), RTCIceCandidateType.host);
            var mappedEP = new IPEndPoint(IPAddress.Parse("::ffff:192.168.1.123"), 57256);

            Assert.True(channel.IsKnownRemoteEndPoint(mappedEP),
                "IPv4-mapped IPv6 source matching an IPv4 candidate must pass the filter (issue #1603)");
        }

        /// <summary>
        /// The inverse: a regular IPv4 source matching a known IPv4-mapped IPv6 candidate must pass.
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_IPv4SourceMatchesIPv4MappedIPv6Candidate_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            AddRemoteCandidate(channel, new IPEndPoint(IPAddress.Parse("::ffff:192.168.1.123"), 57256), RTCIceCandidateType.host);
            var regularEP = new IPEndPoint(IPAddress.Parse("192.168.1.123"), 57256);

            Assert.True(channel.IsKnownRemoteEndPoint(regularEP),
                "IPv4 source matching an IPv4-mapped IPv6 candidate must pass the filter (issue #1603)");
        }

        /// <summary>
        /// Different underlying IPv4 addresses, even when one is IPv4-mapped, must not match.
        /// </summary>
        [Fact]
        public void IsKnownRemoteEndPoint_DifferentIPv4AddressesOneMapped_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var channel = GetIceChannel(pc);
            AddRemoteCandidate(channel, new IPEndPoint(IPAddress.Parse("192.168.1.100"), 57256), RTCIceCandidateType.host);
            var mappedEP = new IPEndPoint(IPAddress.Parse("::ffff:192.168.1.200"), 57256);

            Assert.False(channel.IsKnownRemoteEndPoint(mappedEP),
                "different underlying IPv4 addresses must be rejected");
        }

        // ---------- helpers ----------

        /// <summary>
        /// Reaches into the RTCPeerConnection's private RtpIceChannel instance.
        /// </summary>
        private static RtpIceChannel GetIceChannel(RTCPeerConnection pc)
        {
            var field = typeof(RTCPeerConnection).GetField(
                "_rtpIceChannel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            var channel = field.GetValue(pc) as RtpIceChannel;
            Assert.NotNull(channel);
            return channel;
        }

        /// <summary>
        /// Adds a remote candidate (with its DestinationEndPoint set) to the channel's known remote
        /// candidate set, mirroring what the SDP / peer-reflexive paths do.
        /// </summary>
        private static void AddRemoteCandidate(RtpIceChannel channel, IPEndPoint remoteEP, RTCIceCandidateType type)
        {
            var candidate = new RTCIceCandidate(
                RTCIceProtocol.udp,
                remoteEP.Address,
                (ushort)remoteEP.Port,
                type);
            candidate.SetDestinationEndPoint(remoteEP);
            channel._remoteCandidates.Add(candidate);
            // Mirror the production add path, which refreshes the copy-on-write snapshot the filter reads.
            channel.RefreshRemoteCandidatesSnapshot();
        }

        /// <summary>
        /// Sets a fake NominatedEntry so a test can prove the filter accepts known-but-not-nominated
        /// sources (the nominated pair must NOT be the gate for inbound media).
        /// </summary>
        private static void SetNominatedRemoteEndpoint(RtpIceChannel channel, IPEndPoint remoteEP)
        {
            var remoteCandidate = new RTCIceCandidate(
                RTCIceProtocol.udp,
                remoteEP.Address,
                (ushort)remoteEP.Port,
                RTCIceCandidateType.host);
            remoteCandidate.SetDestinationEndPoint(remoteEP);

            var localCandidate = new RTCIceCandidate(
                RTCIceProtocol.udp,
                IPAddress.Loopback,
                (ushort)1234,
                RTCIceCandidateType.host);

            var entry = new ChecklistEntry(localCandidate, remoteCandidate, isLocalController: true);

            var nominatedProp = typeof(RtpIceChannel).GetProperty(
                "NominatedEntry",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(nominatedProp);
            nominatedProp.SetValue(channel, entry);
        }
    }
}
