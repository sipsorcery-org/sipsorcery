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
    }
}
