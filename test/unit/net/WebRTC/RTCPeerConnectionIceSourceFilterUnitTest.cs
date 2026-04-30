//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionIceSourceFilterUnitTest.cs
//
// Description: Regression tests for the ICE source-address filter applied
// to non-STUN packets received on an RTCPeerConnection
// (issue #1559 -- DTLS handshake DoS via off-path packet injection).
//
// The filter mirrors what libwebrtc and pion both do at the ICE layer:
// after a candidate pair has been nominated, only forward non-STUN
// packets up the stack if their source address+port matches the selected
// pair's remote endpoint.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 29 Apr 2026  Claude          Created.
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
        /// Before any ICE candidate pair has been nominated there is no
        /// "selected" remote to compare against. The filter must reject
        /// every non-STUN packet -- DTLS isn't expected to start until
        /// after nomination, and this is the conservative behaviour
        /// libwebrtc and pion both apply.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_NoNominatedEntry_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var anyEP = new IPEndPoint(IPAddress.Parse("198.51.100.10"), 50000);

            Assert.False(pc.IsFromSelectedIceCandidate(anyEP),
                "with no NominatedEntry the filter must reject all sources");
        }

        /// <summary>
        /// After nomination, packets from the selected pair's remote
        /// endpoint must be accepted.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_MatchingSource_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            var remoteEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820);
            InjectNominatedRemoteEndpoint(pc, remoteEP);

            Assert.True(pc.IsFromSelectedIceCandidate(remoteEP),
                "matching source must pass the filter");
        }

        /// <summary>
        /// After nomination, packets from a different IP address than the
        /// nominated pair's remote endpoint must be rejected. This is the
        /// load-bearing case for issue #1559 -- an attacker on a different
        /// IP than the genuine peer cannot inject DTLS handshake packets.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_DifferentAddress_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var nominatedEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820);
            var attackerEP  = new IPEndPoint(IPAddress.Parse("198.51.100.99"), 51820);
            InjectNominatedRemoteEndpoint(pc, nominatedEP);

            Assert.False(pc.IsFromSelectedIceCandidate(attackerEP),
                "off-path source address must be rejected (issue #1559)");
        }

        /// <summary>
        /// After nomination, packets from the right IP but the wrong
        /// port must be rejected. The wrong-port case covers an attacker
        /// who has spoofed the genuine peer's IP but is using a different
        /// source port.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_DifferentPort_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var nominatedEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820);
            var wrongPortEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51821);
            InjectNominatedRemoteEndpoint(pc, nominatedEP);

            Assert.False(pc.IsFromSelectedIceCandidate(wrongPortEP),
                "wrong source port must be rejected (issue #1559)");
        }

        /// <summary>
        /// A null remote endpoint is treated as "no source", which is
        /// rejected.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_NullRemoteEndpoint_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var nominatedEP = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820);
            InjectNominatedRemoteEndpoint(pc, nominatedEP);

            Assert.False(pc.IsFromSelectedIceCandidate(null));
        }

        /// <summary>
        /// When the nominated endpoint is an IPv4 address and the received
        /// packet source is an IPv4-mapped IPv6 address (::ffff:x.x.x.x),
        /// the filter should still accept the packet. This is the typical
        /// scenario when using dual-stack sockets on Linux/Windows.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_IPv4MappedIPv6RemoteMatchesIPv4Nominated_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            // Nominated endpoint uses regular IPv4.
            var nominatedEP = new IPEndPoint(IPAddress.Parse("192.168.1.123"), 57256);
            // Incoming packet appears as IPv4-mapped IPv6 (as often happens on dual-stack sockets).
            var mappedEP = new IPEndPoint(IPAddress.Parse("::ffff:192.168.1.123"), 57256);
            InjectNominatedRemoteEndpoint(pc, nominatedEP);

            Assert.True(pc.IsFromSelectedIceCandidate(mappedEP),
                "IPv4-mapped IPv6 source matching nominated IPv4 must pass the filter");
        }

        /// <summary>
        /// When the nominated endpoint is an IPv4-mapped IPv6 address and the
        /// received packet source is a regular IPv4 address, the filter should
        /// still accept the packet. This is the inverse of the typical scenario.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_IPv4RemoteMatchesIPv4MappedIPv6Nominated_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            // Nominated endpoint uses IPv4-mapped IPv6.
            var nominatedEP = new IPEndPoint(IPAddress.Parse("::ffff:192.168.1.123"), 57256);
            // Incoming packet appears as regular IPv4.
            var regularEP = new IPEndPoint(IPAddress.Parse("192.168.1.123"), 57256);
            InjectNominatedRemoteEndpoint(pc, nominatedEP);

            Assert.True(pc.IsFromSelectedIceCandidate(regularEP),
                "IPv4 source matching nominated IPv4-mapped IPv6 must pass the filter");
        }

        /// <summary>
        /// When both endpoints are IPv4-mapped IPv6 addresses representing the
        /// same underlying IPv4 address, they should match.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_BothIPv4MappedIPv6Match_ReturnsTrue()
        {
            var pc = new RTCPeerConnection(null);
            var nominatedEP = new IPEndPoint(IPAddress.Parse("::ffff:10.0.0.1"), 12345);
            var remoteEP = new IPEndPoint(IPAddress.Parse("::ffff:10.0.0.1"), 12345);
            InjectNominatedRemoteEndpoint(pc, nominatedEP);

            Assert.True(pc.IsFromSelectedIceCandidate(remoteEP),
                "matching IPv4-mapped IPv6 addresses must pass the filter");
        }

        /// <summary>
        /// Different IPv4 addresses even when one is IPv4-mapped should not match.
        /// </summary>
        [Fact]
        public void IsFromSelectedIceCandidate_DifferentIPv4AddressesOneIsMapped_ReturnsFalse()
        {
            var pc = new RTCPeerConnection(null);
            var nominatedEP = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 57256);
            var mappedEP = new IPEndPoint(IPAddress.Parse("::ffff:192.168.1.200"), 57256);
            InjectNominatedRemoteEndpoint(pc, nominatedEP);

            Assert.False(pc.IsFromSelectedIceCandidate(mappedEP),
                "different underlying IPv4 addresses must be rejected");
        }

        // ---------- helpers ----------

        /// <summary>
        /// Reaches into the RTCPeerConnection's private RtpIceChannel
        /// instance and sets a fake NominatedEntry whose RemoteCandidate's
        /// DestinationEndPoint matches <paramref name="remoteEP"/>. Used
        /// by the tests to put the connection into a "post-nomination"
        /// state without running a real ICE handshake.
        /// </summary>
        private static void InjectNominatedRemoteEndpoint(RTCPeerConnection pc, IPEndPoint remoteEP)
        {
            var iceChannelField = typeof(RTCPeerConnection).GetField(
                "_rtpIceChannel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(iceChannelField);

            var iceChannel = iceChannelField.GetValue(pc);
            Assert.NotNull(iceChannel);

            // Build a fake remote candidate whose DestinationEndPoint matches
            // the supplied remote EP.
            var remoteCandidate = new RTCIceCandidate(
                RTCIceProtocol.udp,
                remoteEP.Address,
                (ushort)remoteEP.Port,
                RTCIceCandidateType.host);
            remoteCandidate.SetDestinationEndPoint(remoteEP);

            // Use any plausible local candidate -- only the remote side is
            // compared by the filter.
            var localCandidate = new RTCIceCandidate(
                RTCIceProtocol.udp,
                IPAddress.Loopback,
                (ushort)1234,
                RTCIceCandidateType.host);

            var entry = new ChecklistEntry(localCandidate, remoteCandidate, isLocalController: true);

            // NominatedEntry has a private setter -- assign via reflection.
            var nominatedProp = typeof(RtpIceChannel).GetProperty(
                "NominatedEntry",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(nominatedProp);
            nominatedProp.SetValue(iceChannel, entry);
        }
    }
}
