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

using System;
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

            bool parseResult = RTCIceCandidateInit.TryParse(candidate.toJSON(), out var init);

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
        /// A TCP host candidate parses with the protocol and candidate type identified.
        /// </summary>
        [Fact]
        public void Parse_TcpHostCandidate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("4 1 tcp 2105458943 10.0.1.16 9 typ host tcpType active generation 0");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceProtocol.tcp, candidate.protocol);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal(RTCIceTcpCandidateType.active, candidate.tcpType);
            Assert.Equal("active", candidate.relatedAddress);
            Assert.Equal("10.0.1.16", candidate.address);
            Assert.Equal(9, candidate.port);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Parse_NullOrEmptyCandidate_ThrowsArgumentNullException(string candidateLine)
        {
            Assert.Throws<ArgumentNullException>(() => RTCIceCandidate.Parse(candidateLine));
        }

        [Fact]
        public void Parse_CandidatePrefixAndRelatedFields_ParsesEveryField()
        {
            var candidate = RTCIceCandidate.Parse(
                "candidate:relay-foundation 2 udp 123456 203.0.113.10 3478 typ relay raddr 192.0.2.10 rport 5000 generation 0");

            Assert.Equal("relay-foundation", candidate.foundation);
            Assert.Equal(RTCIceComponent.rtcp, candidate.component);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);
            Assert.Equal(123456u, candidate.priority);
            Assert.Equal("203.0.113.10", candidate.address);
            Assert.Equal(3478, candidate.port);
            Assert.Equal(RTCIceCandidateType.relay, candidate.type);
            Assert.Equal("192.0.2.10", candidate.relatedAddress);
            Assert.Equal(5000, candidate.relatedPort);
        }

        [Fact]
        public void Parse_InvalidEnumAndPriorityFields_LeavesDefaultValues()
        {
            var candidate = RTCIceCandidate.Parse("foundation invalid invalid invalid 192.0.2.1 5000 typ invalid");

            Assert.Equal(RTCIceComponent.rtp, candidate.component);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);
            Assert.Equal(0u, candidate.priority);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
        }

        [Fact]
        public void Parse_RelatedAddressWithoutRelatedPort_LeavesPortAtDefault()
        {
            var candidate = RTCIceCandidate.Parse(
                "foundation 1 udp 100 203.0.113.10 3478 typ srflx raddr 192.0.2.10 generation 0");

            Assert.Equal("192.0.2.10", candidate.relatedAddress);
            Assert.Equal(0, candidate.relatedPort);
        }

        [Fact]
        public void Parse_TcpCandidateWithoutTcpType_LeavesTcpTypeAtDefault()
        {
            var candidate = RTCIceCandidate.Parse("foundation 1 tcp 100 192.0.2.1 9 typ host");

            Assert.Equal(RTCIceProtocol.tcp, candidate.protocol);
            Assert.Equal(RTCIceTcpCandidateType.active, candidate.tcpType);
        }

        [Fact]
        public void Parse_TcpRelayCandidate_ParsesRelatedFields()
        {
            const string candidateLine =
                "foundation 1 tcp 100 203.0.113.10 3478 typ relay tcptype passive raddr 192.0.2.10 rport 5000 generation 0";

            var candidate = RTCIceCandidate.Parse(candidateLine);

            Assert.Equal("192.0.2.10", candidate.relatedAddress);
            Assert.Equal(5000, candidate.relatedPort);
        }

        [Theory]
        [InlineData(RTCIceCandidateType.host, RTCIceProtocol.udp, RTCIceTcpCandidateType.active, null, 0,
            "foundation 1 udp 100 192.0.2.1 5000 typ host generation 0")]
        [InlineData(RTCIceCandidateType.prflx, RTCIceProtocol.tcp, RTCIceTcpCandidateType.so, null, 0,
            "foundation 1 tcp 100 192.0.2.1 5000 typ prflx tcptype so generation 0")]
        [InlineData(RTCIceCandidateType.srflx, RTCIceProtocol.udp, RTCIceTcpCandidateType.active, "198.51.100.1", 6000,
            "foundation 1 udp 100 192.0.2.1 5000 typ srflx raddr 198.51.100.1 rport 6000 generation 0")]
        [InlineData(RTCIceCandidateType.relay, RTCIceProtocol.tcp, RTCIceTcpCandidateType.passive, "198.51.100.1", 6000,
            "foundation 1 tcp 100 192.0.2.1 5000 typ relay tcptype passive raddr 198.51.100.1 rport 6000 generation 0")]
        [InlineData(RTCIceCandidateType.relay, RTCIceProtocol.udp, RTCIceTcpCandidateType.active, null, 0,
            "foundation 1 udp 100 192.0.2.1 5000 typ relay raddr 0.0.0.0 rport 0 generation 0")]
        public void ToString_AllCandidateForms_ReturnsExpectedSdp(
            RTCIceCandidateType type,
            RTCIceProtocol protocol,
            RTCIceTcpCandidateType tcpType,
            string relatedAddress,
            ushort relatedPort,
            string expected)
        {
            var candidate = new RTCIceCandidate(new RTCIceCandidateInit())
            {
                foundation = "foundation",
                component = RTCIceComponent.rtp,
                protocol = protocol,
                priority = 100,
                address = "192.0.2.1",
                port = 5000,
                type = type,
                tcpType = tcpType,
                relatedAddress = relatedAddress,
                relatedPort = relatedPort
            };

            Assert.Equal(expected, candidate.ToString());
            Assert.Equal(expected, candidate.candidate);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void CandidateInitTryParse_NullOrWhiteSpace_ReturnsFalse(string json)
        {
            Assert.False(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Null(init);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("not-json")]
        public void CandidateInitTryParse_NullOrInvalidJsonValue_ReturnsFalse(string json)
        {
            Assert.False(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Null(init);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"sdpMid\":\"audio\"}")]
        [InlineData("{\"candidate\":\"candidate:foundation 1 udp 100 192.0.2.1 5000 typ host\"}")]
        public void CandidateInitTryParse_MissingRequiredField_ReturnsFalse(string json)
        {
            Assert.False(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.NotNull(init);
        }

        [Fact]
        public void CandidateInitTryParse_AllFields_ReturnsPopulatedObject()
        {
            const string json = "{\"candidate\":\"candidate:foundation 1 udp 100 192.0.2.1 5000 typ host\",\"sdpMid\":\"audio\",\"sdpMLineIndex\":2,\"usernameFragment\":\"ufrag\"}";

            Assert.True(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Equal("candidate:foundation 1 udp 100 192.0.2.1 5000 typ host", init.candidate);
            Assert.Equal("audio", init.sdpMid);
            Assert.Equal(2, init.sdpMLineIndex);
            Assert.Equal("ufrag", init.usernameFragment);
        }

        [Fact]
        public void Candidate_FromJson_ParsesAllFields()
        {
            const string json = "{\"candidate\":\"candidate:foundation 1 udp 100 192.0.2.1 5000 typ host\",\"sdpMid\":\"audio\",\"sdpMLineIndex\":2,\"usernameFragment\":\"ufrag\"}";

            Assert.True(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.NotNull(init);
            Assert.Equal("candidate:foundation 1 udp 100 192.0.2.1 5000 typ host", init.candidate);
            Assert.Equal("audio", init.sdpMid);
            Assert.Equal(2, init.sdpMLineIndex);
            Assert.Equal("ufrag", init.usernameFragment);
        }

        [Fact]
        public void Candidate_ToJson_SerializesAllFields()
        {
            var init = new RTCIceCandidateInit
            {
                candidate = "candidate:foundation 1 udp 100 192.0.2.1 5000 typ host",
                sdpMid = "audio",
                sdpMLineIndex = 2,
                usernameFragment = "ufrag"
            };

            var json = init.toJSON();

            Assert.Equal(
                "{\"candidate\":\"candidate:foundation 1 udp 100 192.0.2.1 5000 typ host\",\"sdpMid\":\"audio\",\"sdpMLineIndex\":2,\"usernameFragment\":\"ufrag\"}",
                json);
        }

        /// <summary>
        /// Tests that two identical host candidates are equal using IEquatable implementation.
        /// </summary>
        [Fact]
        public void HostCandidatesEqualityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");
            var candidate2 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");

            Assert.True(candidate1.Equals(candidate2));
            Assert.True(candidate2.Equals(candidate1));
            Assert.Equal(candidate1.GetHashCode(), candidate2.GetHashCode());

            // Verify that equality matches ToString equality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests that two different host candidates are not equal.
        /// </summary>
        [Fact]
        public void HostCandidatesInequalityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");
            var candidate2 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.51 61680 typ host generation 0");

            Assert.False(candidate1.Equals(candidate2));
            Assert.False(candidate2.Equals(candidate1));

            // Verify that inequality matches ToString inequality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests that two identical srflx candidates are equal.
        /// </summary>
        [Fact]
        public void SrflxCandidatesEqualityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 0.0.0.0 rport 0 generation 0");
            var candidate2 = RTCIceCandidate.Parse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 0.0.0.0 rport 0 generation 0");

            Assert.True(candidate1.Equals(candidate2));
            Assert.True(candidate2.Equals(candidate1));
            Assert.Equal(candidate1.GetHashCode(), candidate2.GetHashCode());

            // Verify that equality matches ToString equality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests that two different srflx candidates are not equal.
        /// </summary>
        [Fact]
        public void SrflxCandidatesInequalityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 192.168.1.1 rport 5000 generation 0");
            var candidate2 = RTCIceCandidate.Parse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 192.168.1.2 rport 5000 generation 0");

            Assert.False(candidate1.Equals(candidate2));

            // Verify that inequality matches ToString inequality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests that two identical TCP candidates are equal.
        /// </summary>
        [Fact]
        public void TcpCandidatesEqualityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse("4 1 TCP 2105458943 10.0.1.16 9 typ host tcptype active generation 0");
            var candidate2 = RTCIceCandidate.Parse("4 1 TCP 2105458943 10.0.1.16 9 typ host tcptype active generation 0");

            Assert.True(candidate1.Equals(candidate2));
            Assert.True(candidate2.Equals(candidate1));
            Assert.Equal(candidate1.GetHashCode(), candidate2.GetHashCode());

            // Verify that equality matches ToString equality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests equality with null reference.
        /// </summary>
        [Fact]
        public void CandidateEqualityWithNullUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");

            Assert.False(candidate.Equals(null));
            Assert.False(candidate.Equals((IRTCIceCandidate)null));
        }

        /// <summary>
        /// Tests equality with object of different type.
        /// </summary>
        [Fact]
        public void CandidateEqualityWithDifferentTypeUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");

            Assert.False(candidate.Equals("not a candidate"));
            Assert.False(candidate.Equals(42));
        }

        /// <summary>
        /// Tests that two candidates created via constructor are equal when they have the same properties.
        /// </summary>
        [Fact]
        public void ConstructedCandidatesEqualityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Parse("192.168.1.100"), 5000, RTCIceCandidateType.host);
            var candidate2 = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Parse("192.168.1.100"), 5000, RTCIceCandidateType.host);

            Assert.True(candidate1.Equals(candidate2));
            Assert.Equal(candidate1.GetHashCode(), candidate2.GetHashCode());

            // Verify that equality matches ToString equality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests that candidates with different ports are not equal.
        /// </summary>
        [Fact]
        public void CandidatesDifferentPortInequalityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Parse("192.168.1.100"), 5000, RTCIceCandidateType.host);
            var candidate2 = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Parse("192.168.1.100"), 5001, RTCIceCandidateType.host);

            Assert.False(candidate1.Equals(candidate2));

            // Verify that inequality matches ToString inequality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests that candidates with different protocols are not equal.
        /// </summary>
        [Fact]
        public void CandidatesDifferentProtocolInequalityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Parse("192.168.1.100"), 5000, RTCIceCandidateType.host);
            var candidate2 = new RTCIceCandidate(RTCIceProtocol.tcp, IPAddress.Parse("192.168.1.100"), 5000, RTCIceCandidateType.host);

            Assert.False(candidate1.Equals(candidate2));

            // Verify that inequality matches ToString inequality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests that candidates with different types are not equal.
        /// </summary>
        [Fact]
        public void CandidatesDifferentTypeInequalityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Parse("192.168.1.100"), 5000, RTCIceCandidateType.host);
            var candidate2 = new RTCIceCandidate(RTCIceProtocol.udp, IPAddress.Parse("192.168.1.100"), 5000, RTCIceCandidateType.srflx);

            Assert.False(candidate1.Equals(candidate2));

            // Verify that inequality matches ToString inequality
            Assert.Equal(candidate1.ToString() == candidate2.ToString(), candidate1.Equals(candidate2));
        }

        /// <summary>
        /// Tests self equality (reflexive property).
        /// </summary>
        [Fact]
        public void CandidateSelfEqualityUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");

            Assert.True(candidate.Equals(candidate));
            Assert.Equal(candidate.GetHashCode(), candidate.GetHashCode());
        }

        /// <summary>
        /// Tests that equality is consistent with ToString comparison across various candidate types.
        /// </summary>
        [Theory]
        [InlineData("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0")]
        [InlineData("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 192.168.1.1 rport 5000 generation 0")]
        [InlineData("4 1 TCP 2105458943 10.0.1.16 9 typ host tcptype active generation 0")]
        [InlineData("1 1 udp 100 10.0.0.1 5000 typ relay raddr 192.168.1.1 rport 5000 generation 0")]
        public void CandidateEqualityConsistentWithToStringUnitTest(string candidateString)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse(candidateString);
            var candidate2 = RTCIceCandidate.Parse(candidateString);

            // The primary assertion: equality matches ToString equality
            bool toStringEqual = candidate1.ToString() == candidate2.ToString();
            bool equalsEqual = candidate1.Equals(candidate2);

            Assert.Equal(toStringEqual, equalsEqual);
            Assert.True(equalsEqual, $"Two candidates parsed from identical strings should be equal");
        }

        /// <summary>
        /// Tests the == operator with equal candidates.
        /// </summary>
        [Fact]
        public void OperatorEqualityWithEqualCandidatesUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");
            var candidate2 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");

            Assert.True(candidate1 == candidate2);
            Assert.False(candidate1 != candidate2);
        }

        /// <summary>
        /// Tests the != operator with different candidates.
        /// </summary>
        [Fact]
        public void OperatorInequalityWithDifferentCandidatesUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");
            var candidate2 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.51 61680 typ host generation 0");

            Assert.True(candidate1 != candidate2);
            Assert.False(candidate1 == candidate2);
        }

        /// <summary>
        /// Tests the == operator with null references.
        /// </summary>
        [Fact]
        public void OperatorEqualityWithNullUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");
            RTCIceCandidate nullCandidate = null;

            Assert.False(candidate == null);
            Assert.False(null == candidate);
            Assert.True(nullCandidate == null);
            Assert.True(null == nullCandidate);
        }

        /// <summary>
        /// Tests the != operator with null references.
        /// </summary>
        [Fact]
        public void OperatorInequalityWithNullUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");
            RTCIceCandidate nullCandidate = null;

            Assert.True(candidate != null);
            Assert.True(null != candidate);
            Assert.False(nullCandidate != null);
            Assert.False(null != nullCandidate);
        }

        /// <summary>
        /// Tests that == operator matches Equals method and ToString equality.
        /// </summary>
        [Fact]
        public void OperatorEqualityConsistentWithEqualsAndToStringUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var candidate1 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");
            var candidate2 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");
            var candidate3 = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.51 61680 typ host generation 0");

            // Equal candidates
            Assert.Equal(candidate1 == candidate2, candidate1.Equals(candidate2));
            Assert.Equal(candidate1 == candidate2, candidate1.ToString() == candidate2.ToString());

            // Different candidates
            Assert.Equal(candidate1 == candidate3, candidate1.Equals(candidate3));
            Assert.Equal(candidate1 == candidate3, candidate1.ToString() == candidate3.ToString());
        }
    }
}
