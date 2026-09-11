//-----------------------------------------------------------------------------
// Filename: IPAddressHelperUnitTest.cs
//
// Description: Characterization tests for IPAddressHelper, the IP-address
// classification helpers used by ICE candidate gathering/ordering (RFC 3484/6724
// style precedence and IPv6 category checks).
//
// Author(s):
// Aaron Clauson
//
// History:
// 09 Jun 2026	Aaron Clauson	Created.
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
    public class IPAddressHelperUnitTest
    {
        private readonly Microsoft.Extensions.Logging.ILogger logger;

        public IPAddressHelperUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Theory]
        [InlineData("192.168.1.1", 30u)]   // native IPv4.
        [InlineData("::1", 60u)]            // IPv6 loopback.
        [InlineData("2001:db8::1", 40u)]    // ordinary global IPv6.
        [InlineData("2002:c0a8:0101::1", 20u)] // 6to4.
        [InlineData("3ffe::1", 1u)]         // 6bone.
        [InlineData("fec0::1", 50u)]        // IPv6 site-local (ULA-style).
        public void IPAddressPrecedence_ReturnsExpected(string ip, uint expected)
        {
            logger.LogDebug("--> {MethodName} ({Ip})", TestHelper.GetCurrentMethodName(), ip);
            Assert.Equal(expected, IPAddressHelper.IPAddressPrecedence(IPAddress.Parse(ip)));
        }

        [Theory]
        [InlineData("2002:c0a8:0101::1", true)]
        [InlineData("2001:db8::1", false)]
        public void IPIs6To4_DetectsPrefix(string ip, bool expected)
        {
            logger.LogDebug("--> {MethodName} ({Ip})", TestHelper.GetCurrentMethodName(), ip);
            Assert.Equal(expected, IPAddressHelper.IPIs6To4(IPAddress.Parse(ip)));
        }

        [Theory]
        [InlineData("3ffe::1", true)]
        [InlineData("2001:db8::1", false)]
        public void IPIs6Bone_DetectsPrefix(string ip, bool expected)
        {
            logger.LogDebug("--> {MethodName} ({Ip})", TestHelper.GetCurrentMethodName(), ip);
            Assert.Equal(expected, IPAddressHelper.IPIs6Bone(IPAddress.Parse(ip)));
        }

        [Theory]
        [InlineData("fec0::1", true)]
        [InlineData("2001:db8::1", false)]
        public void IPIsSiteLocal_DetectsPrefix(string ip, bool expected)
        {
            logger.LogDebug("--> {MethodName} ({Ip})", TestHelper.GetCurrentMethodName(), ip);
            Assert.Equal(expected, IPAddressHelper.IPIsSiteLocal(IPAddress.Parse(ip)));
        }

        /// <summary>
        /// Pins the CURRENT (buggy) behaviour of IPIsV4Compatibility: it always returns false. The
        /// implementation passes a 1-byte kV4CompatibilityPrefix to IPIsHelper with a /96 (12 byte) length, so
        /// the helper's "tomatch.Length &lt; bytesToCompare" guard (1 &lt; 12) short-circuits to false for every
        /// address - even a genuine ::a.b.c.d compat address. This test documents the existing behaviour so a
        /// refactor that fixes it (e.g. supplying a full 12-byte prefix) deliberately updates this expectation.
        /// </summary>
        [Theory]
        [InlineData("::1.2.3.4", false)]  // genuine v4-compatible address - still false due to the prefix-length bug.
        [InlineData("2001:db8::1", false)]
        public void IPIsV4Compatibility_AlwaysFalse_CurrentBehaviour(string ip, bool expected)
        {
            logger.LogDebug("--> {MethodName} ({Ip})", TestHelper.GetCurrentMethodName(), ip);
            Assert.Equal(expected, IPAddressHelper.IPIsV4Compatibility(IPAddress.Parse(ip)));
        }

        /// <summary>
        /// Pins the CURRENT (buggy) behaviour of IPIsLinkLocalV4: it always returns false. The implementation
        /// calls BitConverter.ToInt64 on the 4-byte array returned by GetAddressBytes() for an IPv4 address,
        /// which throws (ToInt64 needs 8 bytes); the exception is swallowed and false returned. A genuine
        /// 169.254.x.x link-local address is therefore reported as non-link-local. Documented so a fix updates
        /// this expectation intentionally.
        /// </summary>
        [Theory]
        [InlineData("169.254.1.1", false)] // genuine link-local - still false due to the ToInt64 bug.
        [InlineData("192.168.1.1", false)]
        [InlineData("2001:db8::1", false)] // not IPv4 at all.
        public void IPIsLinkLocalV4_AlwaysFalse_CurrentBehaviour(string ip, bool expected)
        {
            logger.LogDebug("--> {MethodName} ({Ip})", TestHelper.GetCurrentMethodName(), ip);
            Assert.Equal(expected, IPAddressHelper.IPIsLinkLocalV4(IPAddress.Parse(ip)));
        }
    }
}
