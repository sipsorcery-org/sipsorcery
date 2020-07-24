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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var candidate = RTCIceCandidate.Parse("1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.host, candidate.type);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);

            logger.LogDebug(candidate.ToString());
        }


        /// <summary>
        /// Tests that parsing a server reflexive candidate works correctly.
        /// </summary>
        [Fact]
        public void ParseSvrRflxCandidateUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var candidate = RTCIceCandidate.Parse("842163049 1 udp 1677729535 8.8.8.8 12767 typ srflx raddr 0.0.0.0 rport 0 generation 0 network-cost 999");

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.srflx, candidate.type);
            Assert.Equal(RTCIceProtocol.udp, candidate.protocol);

            logger.LogDebug(candidate.ToString());
        }

        /// <summary>
        /// Tests that the foundation value is the same for equivalent candidates.
        /// </summary>
        [Fact]
        public void EquivalentCandidateFoundationUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCIceCandidateInit initA = new RTCIceCandidateInit { usernameFragment = "abcd" };
            var candidateA = new RTCIceCandidate(initA);
            candidateA.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host, null, 0);

            RTCIceCandidateInit initB = new RTCIceCandidateInit { usernameFragment = "efgh" };
            var candidateB = new RTCIceCandidate(initB);
            candidateB.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host, null, 0);

            Assert.NotNull(candidateA);
            Assert.NotNull(candidateB);
            Assert.Equal(candidateA.foundation, candidateB.foundation);

            logger.LogDebug(candidateA.ToString());
        }

        /// <summary>
        /// Tests that the foundation value is different for non equivalent candidates.
        /// </summary>
        [Fact]
        public void NonEquivalentCandidateFoundationUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCIceCandidateInit initA = new RTCIceCandidateInit { usernameFragment = "abcd" };
            var candidateA = new RTCIceCandidate(initA);
            candidateA.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 1024, RTCIceCandidateType.host, null, 0);

            RTCIceCandidateInit initB = new RTCIceCandidateInit { usernameFragment = "efgh" };
            var candidateB = new RTCIceCandidate(initB);
            candidateB.SetAddressProperties(RTCIceProtocol.udp, IPAddress.IPv6Loopback, 1024, RTCIceCandidateType.host, null, 0);

            Assert.NotNull(candidateA);
            Assert.NotNull(candidateB);
            Assert.NotEqual(candidateA.foundation, candidateB.foundation);

            logger.LogDebug(candidateA.ToString());
            logger.LogDebug(candidateB.ToString());
        }
    }
}
