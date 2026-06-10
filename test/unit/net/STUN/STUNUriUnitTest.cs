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

using System.Net.Sockets;
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
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that parsing a STUN URI with an explicit scheme and port works correctly.
        /// </summary>
        [Fact]
        public void ParseWithSchemeAndPortTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turn:[::1]:14478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turn, stunUri.Scheme);
            Assert.Equal("[::1]", stunUri.Host);
            Assert.Equal(14478, stunUri.Port);
            Assert.True(stunUri.ExplicitPort);
        }

        /// <summary>
        /// Tests that a "turns" URI with no explicit transport defaults to TCP (TLS over TCP), per
        /// RFC 7065. Previously it incorrectly defaulted to UDP, which broke TLS/TCP TURN servers such
        /// as turns:turn.cloudflare.com:443.
        /// </summary>
        [Fact]
        public void ParseTurnsWithoutTransportDefaultsToTcpTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turns:turn.cloudflare.com:443", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turns, stunUri.Scheme);
            Assert.Equal("turn.cloudflare.com", stunUri.Host);
            Assert.Equal(443, stunUri.Port);
            Assert.Equal(STUNProtocolsEnum.tcp, stunUri.Transport);
            Assert.Equal(ProtocolType.Tcp, stunUri.Protocol);
        }

        /// <summary>
        /// Tests that a "stuns" URI with no explicit transport defaults to TCP (TLS over TCP), per RFC 7064.
        /// </summary>
        [Fact]
        public void ParseStunsWithoutTransportDefaultsToTcpTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("stuns:stun.sipsorcery.com:5349", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.stuns, stunUri.Scheme);
            Assert.Equal(STUNProtocolsEnum.tcp, stunUri.Transport);
            Assert.Equal(ProtocolType.Tcp, stunUri.Protocol);
        }

        /// <summary>
        /// Tests that a non-secure "turn" URI with no explicit transport still defaults to UDP.
        /// </summary>
        [Fact]
        public void ParseTurnWithoutTransportDefaultsToUdpTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turn:turn.cloudflare.com:3478", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turn, stunUri.Scheme);
            Assert.Equal(STUNProtocolsEnum.udp, stunUri.Transport);
            Assert.Equal(ProtocolType.Udp, stunUri.Protocol);
        }

        /// <summary>
        /// Tests that an explicit transport on a secure scheme is respected and overrides the
        /// scheme-based default (e.g. turns + transport=udp selects DTLS over UDP).
        /// </summary>
        [Fact]
        public void ParseTurnsExplicitUdpTransportOverridesSchemeDefaultTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turns:turn.cloudflare.com:5349?transport=udp", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turns, stunUri.Scheme);
            Assert.Equal(STUNProtocolsEnum.udp, stunUri.Transport);
            Assert.Equal(ProtocolType.Udp, stunUri.Protocol);
        }

        /// <summary>
        /// Tests that an explicit transport=tcp on a "turns" URI is respected (the known-working form).
        /// </summary>
        [Fact]
        public void ParseTurnsExplicitTcpTransportTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = STUNUri.TryParse("turns:turn.cloudflare.com:443?transport=tcp", out var stunUri);

            Assert.True(result);
            Assert.Equal(STUNSchemesEnum.turns, stunUri.Scheme);
            Assert.Equal(STUNProtocolsEnum.tcp, stunUri.Transport);
            Assert.Equal(ProtocolType.Tcp, stunUri.Protocol);
        }

        /// <summary>
        /// Tests that URIs differing only by host are NOT equal. The equality operator previously
        /// omitted the host comparison, which caused distinct ICE servers to be sporadically
        /// de-duplicated when their host strings happened to land in the same dictionary hash bucket
        /// (string hash codes are randomised per process, hence the intermittent failures).
        /// </summary>
        [Fact]
        public void EqualityComparesHostTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            Assert.True(STUNUri.TryParse("stun:1.2.3.4:3478", out var uri1));
            Assert.True(STUNUri.TryParse("stun:1.2.3.5:3478", out var uri2));

            Assert.False(uri1 == uri2);
            Assert.True(uri1 != uri2);
            Assert.False(uri1.Equals(uri2));
        }

        /// <summary>
        /// Tests that the host comparison is case-insensitive (RFC 4343) and that equal URIs
        /// produce identical hash codes, as required for use as dictionary keys.
        /// </summary>
        [Fact]
        public void EqualityHostCaseInsensitiveTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            Assert.True(STUNUri.TryParse("stun:STUN.sipsorcery.com:3478", out var uri1));
            Assert.True(STUNUri.TryParse("stun:stun.sipsorcery.com:3478", out var uri2));

            Assert.True(uri1 == uri2);
            Assert.Equal(uri1.GetHashCode(), uri2.GetHashCode());
        }

        /// <summary>
        /// Tests the object overload of Equals. The previous implementation called
        /// object.Equals(this, obj) which dispatched straight back into itself (stack overflow) and
        /// threw InvalidCastException for non STUNUri arguments.
        /// </summary>
        [Fact]
        public void EqualsObjectOverloadTestMethod()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            Assert.True(STUNUri.TryParse("stun:1.2.3.4:3478", out var uri1));
            Assert.True(STUNUri.TryParse("stun:1.2.3.4:3478", out var uri2));

            Assert.True(uri1.Equals((object)uri2));
            Assert.False(uri1.Equals("not a stun uri"));
            Assert.False(uri1.Equals(null));
        }
    }
}
