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
        /// Tests that TryParse successfully parses a valid host candidate.
        /// </summary>
        [Fact]
        public void TryParse_ValidHostCandidate_ReturnsTrue()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0", out var candidate);

            Assert.True(result);
            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);
            Assert.Equal("192.168.11.50", candidate.address);
            Assert.Equal((ushort)61680, candidate.port);
        }

        /// <summary>
        /// Tests that TryParse successfully parses a valid server reflexive candidate.
        /// </summary>
        [Fact]
        public void TryParse_ValidSrflxCandidate_ReturnsTrue()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 192.168.1.100 rport 54321 generation 0", out var candidate);

            Assert.True(result);
            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.srflx, candidate.type);
            Assert.Equal("8.8.8.8", candidate.address);
            Assert.Equal((ushort)12767, candidate.port);
            Assert.Equal("192.168.1.100", candidate.relatedAddress);
            Assert.Equal((ushort)54321, candidate.relatedPort);
        }

        /// <summary>
        /// Tests that TryParse successfully parses a valid TCP candidate.
        /// </summary>
        [Fact]
        public void TryParse_ValidTcpCandidate_ReturnsTrue()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("1390596646 1 tcp 1880747346 192.168.11.50 9 typ host tcptype active generation 0", out var candidate);

            Assert.True(result);
            Assert.NotNull(candidate);
            Assert.Equal(RTCIceProtocol.tcp, candidate.protocol);
            Assert.Equal(RTCIceTcpCandidateType.active, candidate.tcpType);
        }

        /// <summary>
        /// Tests that TryParse successfully parses a candidate with candidate: prefix.
        /// </summary>
        [Fact]
        public void TryParse_CandidateWithPrefix_ReturnsTrue()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("candidate:1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0", out var candidate);

            Assert.True(result);
            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
        }

        /// <summary>
        /// Tests that TryParse successfully parses an IPv6 candidate.
        /// </summary>
        [Fact]
        public void TryParse_IPv6Candidate_ReturnsTrue()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("1390596646 1 udp 1880747346 [::1] 61680 typ host generation 0", out var candidate);

            Assert.True(result);
            Assert.NotNull(candidate);
            Assert.Equal(IPAddress.IPv6Loopback, IPAddress.Parse(candidate.address));
        }

        /// <summary>
        /// Tests that TryParse returns false for an empty candidate string.
        /// </summary>
        [Fact]
        public void TryParse_EmptyString_ReturnsFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("", out var candidate);

            Assert.False(result);
            Assert.Null(candidate);
        }

        /// <summary>
        /// Tests that TryParse returns false for a whitespace-only string.
        /// </summary>
        [Fact]
        public void TryParse_WhitespaceOnlyString_ReturnsFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("   ", out var candidate);

            Assert.False(result);
            Assert.Null(candidate);
        }

        /// <summary>
        /// Tests that TryParse returns false for a candidate line with too few fields.
        /// </summary>
        [Fact]
        public void TryParse_TooFewFields_ReturnsFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("1390596646 1 udp 1880747346", out var candidate);

            Assert.False(result);
            Assert.Null(candidate);
        }

        /// <summary>
        /// Tests that TryParse returns false for an invalid port number.
        /// </summary>
        [Fact]
        public void TryParse_InvalidPort_ReturnsFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("1390596646 1 udp 1880747346 192.168.11.50 notaport typ host generation 0", out var candidate);

            Assert.False(result);
            Assert.Null(candidate);
        }

        /// <summary>
        /// Tests that TryParse returns false for an invalid related port number.
        /// </summary>
        [Fact]
        public void TryParse_InvalidRelatedPort_ReturnsFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 192.168.1.100 rport notaport generation 0", out var candidate);

            Assert.False(result);
            Assert.Null(candidate);
        }

        /// <summary>
        /// Tests that TryParse handles candidates with extra attributes.
        /// </summary>
        [Fact]
        public void TryParse_CandidateWithExtraAttributes_ReturnsTrue()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = RTCIceCandidate.TryParse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 192.168.1.100 rport 54321 generation 0 network-cost 999", out var candidate);

            Assert.True(result);
            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.srflx, candidate.type);
        }

        /// <summary>
        /// Tests that Parse throws FormatException when TryParse would return false.
        /// </summary>
        [Fact]
        public void Parse_InvalidCandidate_ThrowsFormatException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var ex = Assert.Throws<FormatException>(() => RTCIceCandidate.Parse("invalid candidate"));

            Assert.Equal("The ICE candidate line was not in the correct format.", ex.Message);
        }

        /// <summary>
        /// Tests that Parse and TryParse produce equivalent results for valid input.
        /// </summary>
        [Fact]
        public void Parse_And_TryParse_ProduceEquivalentResults()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string candidateString = "842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 192.168.1.100 rport 54321 generation 0";

            var parsedCandidate = RTCIceCandidate.Parse(candidateString);
            bool tryParseResult = RTCIceCandidate.TryParse(candidateString, out var tryParsedCandidate);

            Assert.True(tryParseResult);
            Assert.NotNull(tryParsedCandidate);
            Assert.Equal(parsedCandidate, tryParsedCandidate);
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
    }
}
