//-----------------------------------------------------------------------------
// Filename: STUNDnsUnitTest.cs
//
// Description: Unit tests for the STUNDns class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 09 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class STUNDnsUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public STUNDnsUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that looking up a localhost STUN URI works correctly.
        /// </summary>
        [Fact]
        public async void LookupLocalhostTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNUri.TryParse("localhost", out var stunUri);
            var result = await STUNDns.Resolve(stunUri);

            Assert.NotNull(result);
            Assert.Equal(IPAddress.Loopback, result.Address);

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");
        }

        /// <summary>
        /// Tests that looking up a localhost STUN URI with an IPv6 preference works correctly.
        /// </summary>
        [Fact]
        public async void LookupLocalhostIPv6TestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNUri.TryParse("localhost", out var stunUri);
            var result = await STUNDns.Resolve(stunUri, true);

            Assert.NotNull(result);

            if (Socket.OSSupportsIPv6)
            {

                Assert.Equal(IPAddress.IPv6Loopback, result.Address);
            }
            else
            {
                Assert.Equal(IPAddress.Loopback, result.Address);
            }

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");
        }

        /// <summary>
        /// Tests that looking up a STUN URI with a local network host works correctly.
        /// </summary>
        [Fact]
        public async void LookupPrivateNetworkHostTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string localHostname = Dns.GetHostName();

            logger.LogDebug($"Attempting DNS lookup for {localHostname}.");

            STUNUri.TryParse(localHostname, out var stunUri);
            var result = await STUNDns.Resolve(stunUri);

            Assert.NotNull(result);

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");
        }

        /// <summary>
        /// Tests that looking up a STUN URI with a local network host works correctly.
        /// </summary>
        [Fact]
        public async void LookupPrivateNetworkHostIPv6TestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string localHostname = Dns.GetHostName();

            STUNUri.TryParse(localHostname, out var stunUri);

            logger.LogDebug($"Attempting DNS lookup for {stunUri}.");

            var result = await STUNDns.Resolve(stunUri, true);

            Assert.NotNull(result);

            if (Socket.OSSupportsIPv6)
            {
                Assert.Equal(AddressFamily.InterNetworkV6, result.AddressFamily);
            }
            else
            {
                Assert.Equal(AddressFamily.InterNetwork, result.AddressFamily);
            }

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");
        }

        /// <summary>
        /// Tests that looking up a STUN URI with an explicit port works correctly.
        /// </summary>
        [Fact]
        public async void LookupHostWithExplicitPortTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNUri.TryParse("stun.sipsorcery.com:3478", out var stunUri);
            var result = await STUNDns.Resolve(stunUri);

            Assert.NotNull(result);

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");
        }

        /// <summary>
        /// Tests that looking up a STUN URI with an explicit port works correctly.
        /// </summary>
        [Fact]
        public async void LookupHostPreferIPv6TestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNUri.TryParse("www.google.com", out var stunUri);
            var result = await STUNDns.Resolve(stunUri, true);

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");

            Assert.NotNull(result);

            if (Socket.OSSupportsIPv6)
            {
                Assert.Equal(AddressFamily.InterNetworkV6, result.AddressFamily);
            }
            else
            {
                Assert.Equal(AddressFamily.InterNetwork, result.AddressFamily);
            }
        }

        /// <summary>
        /// Tests that looking up a STUN URI with a SRV record works correctly.
        /// </summary>
        [Fact]
        public async void LookupWithSRVTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNUri.TryParse("sipsorcery.com", out var stunUri);
            var result = await STUNDns.Resolve(stunUri);

            Assert.NotNull(result);

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");
        }

        /// <summary>
        /// Tests that looking up a STUN URI with a SRV record works correctly.
        /// </summary>
        [Fact]
        public async void LookupWithSRVTestPreferIPv6Method()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNUri.TryParse("sipsorcery.com", out var stunUri);
            var result = await STUNDns.Resolve(stunUri, true);

            Assert.NotNull(result);

            // No IPv6 DNS record available so should fallback to IPv4.
            Assert.Equal(AddressFamily.InterNetwork, result.AddressFamily);

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");
        }

        /// <summary>
        /// Tests that looking up a non-existent local network host returns null.
        /// </summary>
        [Fact]
        public async void LookupNonExistentHostTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNUri.TryParse("idontexist", out var stunUri);
            var result = await STUNDns.Resolve(stunUri, true);

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");

            Assert.Null(result);
        }

        /// <summary>
        /// Tests that looking up a non-existent canonical hostname returns null.
        /// </summary>
        [Fact]
        public async void LookupNonExistentCanoncialHostTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNUri.TryParse("somehost.fsdfergerw.com", out var stunUri);
            var result = await STUNDns.Resolve(stunUri, true);

            logger.LogDebug($"STUN DNS lookup for {stunUri} {result}.");

            Assert.Null(result);
        }
    }
}
