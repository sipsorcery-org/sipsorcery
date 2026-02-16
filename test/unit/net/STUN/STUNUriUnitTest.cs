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
using SIPSorcery.UnitTests;
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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("stun.sipsorcery.com", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stun, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(STUNConstants.DEFAULT_STUN_PORT, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.udp, stunUri.Transport);
            Assert.False(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with no scheme works correctly.
        /// </summary>
        [Fact]
        public void ParseNoSchemeTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("stun.sipsorcery.com:4478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stun, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(4478, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.udp, stunUri.Transport);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an explicit scheme and port works correctly.
        /// </summary>
        [Fact]
        public void ParseWithStunsSchemeAndPortTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("stuns:stun.sipsorcery.com:4478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stuns, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(4478, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.tls, stunUri.Transport);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an IPv4 host address works correctly.
        /// </summary>
        [Fact]
        public void ParseWithStunsSchemeAndIPv4AddressTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("stuns:192.168.0.100:4478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stuns, stunUri.Scheme);
            Assert.Equal("192.168.0.100", stunUri.Host);
            Assert.Equal(4478, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.tls, stunUri.Transport);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an IPv6 host address works correctly.
        /// </summary>
        [Fact]
        public void ParseWithTurnAndIPv6AddressTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turn:[::1]:14478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turn, stunUri.Scheme);
            Assert.Equal("[::1]", stunUri.Host);
            Assert.Equal(14478, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.udp, stunUri.Transport);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an explicit scheme and port works correctly.
        /// </summary>
        [Fact]
        public void ParseWithTurnSchemeAndNoPortTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turn:stun.sipsorcery.com", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turn, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(3478, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.udp, stunUri.Transport);
            Assert.False(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an explicit scheme and port works correctly.
        /// </summary>
        [Fact]
        public void ParseWithTurnSchemeAndPortTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turn:stun.sipsorcery.com:4478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turn, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(4478, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.udp, stunUri.Transport);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an explicit scheme and port works correctly.
        /// </summary>
        [Fact]
        public void ParseWithTurnsSchemeAndNoPortTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turns:stun.sipsorcery.com", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turns, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(5349, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.tls, stunUri.Transport);
            Assert.False(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an explicit scheme and port works correctly.
        /// </summary>
        [Fact]
        public void ParseWithTurnsSchemeAndPortTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turns:stun.sipsorcery.com:4478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turns, stunUri.Scheme);
            Assert.Equal("stun.sipsorcery.com", stunUri.Host);
            Assert.Equal(4478, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.tls, stunUri.Transport);
            Assert.True(stunUri.ExplicitPort);
        }


        [Theory]

        // Defaults
        [InlineData("stun.sipsorcery.com", STUNSchemesEnum.stun, "stun.sipsorcery.com", 3478, STUNProtocolsEnum.udp, "stun:stun.sipsorcery.com")]
        [InlineData("stun:stun.sipsorcery.com", STUNSchemesEnum.stun, "stun.sipsorcery.com", 3478, STUNProtocolsEnum.udp, "stun:stun.sipsorcery.com")]
        [InlineData("stuns:stun.sipsorcery.com", STUNSchemesEnum.stuns, "stun.sipsorcery.com", 5349, STUNProtocolsEnum.tls, "stuns:stun.sipsorcery.com")]
        [InlineData("turn:stun.sipsorcery.com", STUNSchemesEnum.turn, "stun.sipsorcery.com", 3478, STUNProtocolsEnum.udp, "turn:stun.sipsorcery.com")]
        [InlineData("turns:stun.sipsorcery.com", STUNSchemesEnum.turns, "stun.sipsorcery.com", 5349, STUNProtocolsEnum.tls, "turns:stun.sipsorcery.com")]

        // Case normalization
        [InlineData("STUN:stun.sipsorcery.com?TRANSPORT=UDP", STUNSchemesEnum.stun, "stun.sipsorcery.com", 3478, STUNProtocolsEnum.udp, "stun:stun.sipsorcery.com")]
        [InlineData("TuRnS:Sub-Domain.stun.SIPSorcery.COM:15349?TrAnSpOrT=TcP", STUNSchemesEnum.turns, "sub-domain.stun.sipsorcery.com", 15349, STUNProtocolsEnum.tcp, "turns:sub-domain.stun.sipsorcery.com:15349?transport=tcp")]

        // IPv6 host parsing
        [InlineData("stun:[2001:db8::1]:13478", STUNSchemesEnum.stun, "[2001:db8::1]", 13478, STUNProtocolsEnum.udp, "stun:[2001:db8::1]:13478")]
        [InlineData("turns:[fe80::abcd]:15349?transport=tcp", STUNSchemesEnum.turns, "[fe80::abcd]", 15349, STUNProtocolsEnum.tcp, "turns:[fe80::abcd]:15349?transport=tcp")]

        // Query parameter handling
        //[InlineData("stun:stun.sipsorcery.com?transport=udp&foo=bar", STUNSchemesEnum.stun, "stun.sipsorcery.com", 3478, STUNProtocolsEnum.udp, "")]
        //[InlineData("turn:stun.sipsorcery.com:13478?transport=tcp&extra=value", STUNSchemesEnum.turn, "stun.sipsorcery.com", 13478, STUNProtocolsEnum.tcp, "")]

        // Whitespace and formatting
        [InlineData("  stun:stun.sipsorcery.com  ", STUNSchemesEnum.stun, "stun.sipsorcery.com", 3478, STUNProtocolsEnum.udp, "stun:stun.sipsorcery.com")]
        //[InlineData("stun:stun.sipsorcery.com/", STUNSchemesEnum.stun, "stun.sipsorcery.com", 3478, STUNProtocolsEnum.udp)]

        // Edge cases
        [InlineData("stun:stun.sipsorcery.com:1", STUNSchemesEnum.stun, "stun.sipsorcery.com", 1, STUNProtocolsEnum.udp, "stun:stun.sipsorcery.com:1")]
        [InlineData("stun:stun.sipsorcery.com:65535", STUNSchemesEnum.stun, "stun.sipsorcery.com", 65535, STUNProtocolsEnum.udp, "stun:stun.sipsorcery.com:65535")]
        [InlineData("turn:sub-domain.stun.sipsorcery.com:13478", STUNSchemesEnum.turn, "sub-domain.stun.sipsorcery.com", 13478, STUNProtocolsEnum.udp, "turn:sub-domain.stun.sipsorcery.com:13478")]

        public void ParseWithValidUri(
            string uri,
            STUNSchemesEnum expectedScheme,
            string expectedHost,
            int expectedPort,
            STUNProtocolsEnum expectedTransport,
            string expectedToString)
        {
            var result = STUNUri.TryParse(uri, out var parsed);

            Assert.True(result);
            Assert.NotNull(parsed);
            Assert.Equal(expectedScheme, parsed.Scheme);
            Assert.Equal(expectedHost, parsed.Host);
            Assert.Equal(expectedPort, parsed.Port);
            Assert.Equal(expectedTransport, parsed.Transport);
            Assert.Equal(expectedToString, parsed.ToString());
        }

        [Theory]
        [InlineData("stun:stun.sipsorcery.com:0")]
        [InlineData("stun:stun.sipsorcery.com:65536")]
        //[InlineData("stun:[2001:db8::1")]
        [InlineData("stun:stun.sipsorcery.com?transport=")]
        [InlineData("stun:stun.sipsorcery.com?=udp")]
        [InlineData("stun:exa mple.com")]
        [InlineData("stun:stun.sipsorcery.com:13478?transport=udp&transport=tcp")]
        [InlineData("stun:stun.sipsorcery.com/")]
        public void ParseWithInvalidUri(string uri)
        {
            var result = STUNUri.TryParse(uri, out var parsed);

            Assert.False(result);
            Assert.Null(parsed);
        }
    }
}
