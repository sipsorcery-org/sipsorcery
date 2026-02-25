//-----------------------------------------------------------------------------
// Filename: STUNClientUnitTest.cs
//
// Description: Unit tests for the STUNClient class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 07 Jul 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class STUNClientUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public STUNClientUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that the STUN client can get it's public IP address from a known STUN server.
        /// </summary>
        [Fact()]
        public void GetPublicIPStunClientTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var publicIP = STUNClient.GetPublicIPAddress("stun.cloudflare.com");

            logger.LogDebug("Public IP address {publicIP}.", publicIP);
        }
    }
}
