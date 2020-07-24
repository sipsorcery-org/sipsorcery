//-----------------------------------------------------------------------------
// Filename: STUNUriUnitTest.cs
//
// Description: Unit tests for the STUNUri class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 08 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class STUNUriUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public STUNUriUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with no scheme and no port works correctly.
        /// </summary>
        [Fact]
        public void ParseNoSchemeNoPortTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            bool result = STUNUri.TryParse("stun.sipsorcery.com", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stun, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(STUNConstants.DEFAULT_STUN_PORT, stunUri.Port);
            Assert.False(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with no scheme works correctly.
        /// </summary>
        [Fact]
        public void ParseNoSchemeTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            bool result = STUNUri.TryParse("stun.sipsorcery.com:4478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stun, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(4478, stunUri.Port);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an explicit scheme and port works correctly.
        /// </summary>
        [Fact]
        public void ParseWithSchemeAndPortTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            bool result = STUNUri.TryParse("stuns:stun.sipsorcery.com:4478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stuns, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(4478, stunUri.Port);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an IPv4 host address works correctly.
        /// </summary>
        [Fact]
        public void ParseWithIPv4AddressTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            bool result = STUNUri.TryParse("stuns:192.168.0.100:4478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stuns, stunUri.Scheme);
            Assert.Equal("192.168.0.100", stunUri.Host);
            Assert.Equal(4478, stunUri.Port);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an IPv6 host address works correctly.
        /// </summary>
        [Fact]
        public void ParseWithIPv6AddressTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            bool result = STUNUri.TryParse("turn:[::1]:14478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turn, stunUri.Scheme);
            Assert.Equal("[::1]", stunUri.Host);
            Assert.Equal(14478, stunUri.Port);
            Assert.True(stunUri.ExplicitPort);
        }
    }
}
