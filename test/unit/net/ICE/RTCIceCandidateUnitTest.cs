//-----------------------------------------------------------------------------
// Filename: RTCIceCandidateUnitTest.cs
//
// Description: Unit tests for the RTCIceCandidate class.
//
// History:
// 17 Mar 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCIceCandidateUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCIceCandidateUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that parsing a host candidate works correctly.
        /// </summary>
        [Fact]
        public void ParseHostCandidateUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);

            logger.LogDebug("Candidate: {Candidate}", candidate.ToString());
        }

        /// <summary>
        /// Tests that parsing an IPv6 host candidate works correctly.
        /// </summary>
        [Fact]
        public void Parse_IPv6_Host_Candidate_UnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 [::1] 61680 typ host generation 0");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);
            Assert.Equal(IPAddress.IPv6Loopback, IPAddress.Parse(candidate.address));

            logger.LogDebug("Candidate: {Candidate}", candidate.ToString());
        }

        /// <summary>
        /// Tests that parsing an IPv6 host candidate works correctly.
        /// </summary>
        [Fact]
        public void Parse_IPv6_Host_NoBrackets_Candidate_UnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 ::1 61680 typ host generation 0");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);
            Assert.Equal(IPAddress.IPv6Loopback, IPAddress.Parse(candidate.address));

            logger.LogDebug("Candidate: {Candidate}", candidate.ToString());
        }

        /// <summary>
        /// Tests that parsing a server reflexive candidate works correctly.
        /// </summary>
        [Fact]
        public void ParseSvrRflxCandidateUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 0.0.0.0 rport 0 generation 0 network-cost 999");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.srflx, candidate.type);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);

            logger.LogDebug("Candidate: {Candidate}", candidate.ToString());
        }

        /// <summary>
        /// Tests that the foundation value is the same for equivalent candidates.
        /// </summary>
        [Fact]
        public void EquivalentCandidateFoundationUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTCIceCandidateInit initA = new RTCIceCandidateInit { usernameFragment = "abcd" };
            var candidateA = new RTCIceCandidate(initA);
            candidateA.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host, null, 0);

            RTCIceCandidateInit initB = new RTCIceCandidateInit { usernameFragment = "efgh" };
            var candidateB = new RTCIceCandidate(initB);
            candidateB.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host, null, 0);

            Assert.NotNull(candidateA);
            Assert.NotNull(candidateB);
            Assert.Equal(candidateA.foundation, candidateB.foundation);

            logger.LogDebug("CandidateA: {CandidateA}", candidateA.ToString());
        }

        /// <summary>
        /// Tests that the foundation value is different for non equivalent candidates.
        /// </summary>
        [Fact]
        public void NonEquivalentCandidateFoundationUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTCIceCandidateInit initA = new RTCIceCandidateInit { usernameFragment = "abcd" };
            var candidateA = new RTCIceCandidate(initA);
            candidateA.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host, null, 0);

            RTCIceCandidateInit initB = new RTCIceCandidateInit { usernameFragment = "efgh" };
            var candidateB = new RTCIceCandidate(initB);
            candidateB.SetAddressProperties(RTCIceProtocol.udp, IPAddress.IPv6Loopback, 1024, RTCIceCandidateType.host, null, 0);

            Assert.NotNull(candidateA);
            Assert.NotNull(candidateB);
            Assert.NotEqual(candidateA.foundation, candidateB.foundation);

            logger.LogDebug("CandidateA: {CandidateA}", candidateA.ToString());
            logger.LogDebug("CandidateB: {CandidateB}", candidateB.ToString());
        }

        /// <summary>
        /// Tests that serialising to JSON a candidate works correctly.
        /// </summary>
        [Fact]
        public void ToJsonUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);

            logger.LogDebug("Candidate JSON: {CandidateJson}", candidate.toJSON());

            var parseResult = RTCIceCandidateInit.TryParse(candidate.toJSON(), out var init);

            Assert.True(parseResult);

            Assert.Equal(0, init.sdpMLineIndex);
            Assert.Equal("0", init.sdpMid);

            var initCandidate = RTCIceCandidate.Parse(init.candidate);

            Assert.Equal(RTCIceCandidateType.host, initCandidate.type);
            Assert.Equal(RTCIceProtocol.udp, initCandidate.protocol);
        }

        /// <summary>
        /// Pins the exact RFC 5245 priority value computed for a host UDP IPv4 candidate. The formula is
        /// (typePreference &lt;&lt; 24) | (localPreference &lt;&lt; 8) | (256 - component) where for a host (126)
        /// UDP (relay preference 2) native-IPv4 (precedence 30) rtp (component 1) candidate this is:
        ///   (126 &lt;&lt; 24) | (((0 &lt;&lt; 8 | 30) + 2) &lt;&lt; 8) | (256 - 1) = 2113937663.
        /// </summary>
        [Fact]
        public void Priority_HostUdpIPv4_MatchesRfc5245Formula()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var candidate = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host);

            Assert.Equal(2113937663u, candidate.priority);
        }

        /// <summary>
        /// A host candidate has a higher priority than a server-reflexive candidate for the same address,
        /// because the type preference (126 vs 100) dominates the most significant byte of the priority.
        /// </summary>
        [Fact]
        public void Priority_HostHigherThanServerReflexive()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var host = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host);

            var srflx = new RTCIceCandidate(new RTCIceCandidateInit());
            srflx.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.srflx, null, 0);

            Assert.True(host.priority > srflx.priority,
                $"Expected host priority {host.priority} > srflx priority {srflx.priority}.");
        }

        /// <summary>
        /// A UDP candidate has a higher priority than the equivalent TCP candidate because the relay
        /// preference (UDP 2 vs TCP 1) feeds into the local preference component of the priority.
        /// </summary>
        [Fact]
        public void Priority_UdpHigherThanTcp()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var udp = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host);
            var tcp = new RTCIceCandidate(RTCIceProtocol.tcp, IPAddress.Loopback, 1024, RTCIceCandidateType.host);

            Assert.True(udp.priority > tcp.priority,
                $"Expected udp priority {udp.priority} > tcp priority {tcp.priority}.");
        }

        /// <summary>
        /// A server-reflexive candidate round-trips through ToString()/Parse() preserving the related
        /// address and port (raddr/rport) that distinguish a reflexive candidate's base.
        /// </summary>
        [Fact]
        public void ServerReflexive_SdpRoundTrip_PreservesRelatedAddress()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var srflx = new RTCIceCandidate(new RTCIceCandidateInit());
            srflx.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Parse("8.8.8.8"), 12767,
                RTCIceCandidateType.srflx, IPAddress.Parse("192.168.1.50"), 5060);

            var roundTripped = RTCIceCandidate.Parse(srflx.ToString());

            Assert.Equal(RTCIceCandidateType.srflx, roundTripped.type);
            Assert.Equal(RTCIceProtocol.udp, roundTripped.protocol);
            Assert.Equal("8.8.8.8", roundTripped.address);
            Assert.Equal(12767, roundTripped.port);
            Assert.Equal("192.168.1.50", roundTripped.relatedAddress);
            Assert.Equal(5060, roundTripped.relatedPort);
        }

        /// <summary>
        /// An mDNS (.local) host candidate parses with the hostname preserved in the address field rather
        /// than being rejected or resolved. This pins the behaviour relied on for mDNS candidate privacy.
        /// </summary>
        [Fact]
        public void Parse_MdnsHostCandidate_PreservesHostname()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse(
                "1390596646 1 udp 1880747346 f47ac10b-58cc-4372-a567-0e02b2c3d479.local 61680 typ host generation 0");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal("f47ac10b-58cc-4372-a567-0e02b2c3d479.local", candidate.address);
            Assert.False(IPAddress.TryParse(candidate.address, out _));
        }

        /// <summary>
        /// A TCP host candidate parses with the protocol and type identified. (Transport keyword is matched
        /// case-sensitively as lower-case "tcp", matching the form produced by ToString().)
        /// </summary>
        [Fact]
        public void Parse_TcpHostCandidate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("4 1 tcp 2105458943 10.0.1.16 9 typ host tcptype active generation 0");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceProtocol.tcp, candidate.protocol);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal("10.0.1.16", candidate.address);
            Assert.Equal(9, candidate.port);
        }
    }
}
