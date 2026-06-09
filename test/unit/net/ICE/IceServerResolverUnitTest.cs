//-----------------------------------------------------------------------------
// Filename: IceServerResolverUnitTest.cs
//
// Description: Characterization tests for IceServerResolver. These pin the
// non-DNS parsing/initialisation behaviour of the resolver - URL splitting,
// transport-policy filtering, duplicate elimination, immediate binding of IP
// literal hosts and ICE server id assignment - ahead of the ICE refactor.
//
// The tests deliberately use IP literal hosts so that ServerEndPoint is bound
// synchronously and no background DNS lookup is scheduled, keeping them
// network-free and deterministic.
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

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class IceServerResolverUnitTest
    {
        private readonly Microsoft.Extensions.Logging.ILogger logger;

        public IceServerResolverUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static RTCIceServer Server(string urls, string username = null, string credential = null) =>
            new RTCIceServer { urls = urls, username = username, credential = credential };

        [Fact]
        public void InitialiseIceServers_NullList_ResolvesNothing()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var resolver = new IceServerResolver();
            resolver.InitialiseIceServers(null, RTCIceTransportPolicy.all);

            Assert.Empty(resolver.IceServers);
        }

        [Fact]
        public void InitialiseIceServers_EmptyList_ResolvesNothing()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var resolver = new IceServerResolver();
            resolver.InitialiseIceServers(new List<RTCIceServer>(), RTCIceTransportPolicy.all);

            Assert.Empty(resolver.IceServers);
        }

        /// <summary>
        /// An ICE server whose host is an IP literal is bound to its end point synchronously during
        /// initialisation (no DNS lookup is scheduled).
        /// </summary>
        [Fact]
        public void InitialiseIceServers_IpLiteralHost_BindsEndPointImmediately()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var resolver = new IceServerResolver();
            resolver.InitialiseIceServers(new List<RTCIceServer> { Server("stun:1.2.3.4:3478") }, RTCIceTransportPolicy.all);

            var server = Assert.Single(resolver.IceServers).Value;
            Assert.Equal(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478), server.ServerEndPoint);
            Assert.Null(server.DnsResolutionTask);          // no background DNS for an IP literal.
        }

        /// <summary>
        /// Under the "relay" transport policy STUN servers are filtered out and only TURN servers are kept,
        /// because relay-only sessions can only use relayed candidates.
        /// </summary>
        [Fact]
        public void InitialiseIceServers_RelayPolicy_FiltersOutStun()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var resolver = new IceServerResolver();
            resolver.InitialiseIceServers(new List<RTCIceServer>
            {
                Server("stun:1.2.3.4:3478"),
                Server("turn:1.2.3.5:3478", "user", "pass")
            }, RTCIceTransportPolicy.relay);

            var server = Assert.Single(resolver.IceServers).Value;
            Assert.Equal(STUNSchemesEnum.turn, server.Uri.Scheme);
        }

        /// <summary>
        /// A single RTCIceServer.urls value may contain multiple comma-separated URLs; each becomes its own
        /// resolved ICE server entry.
        /// </summary>
        [Fact]
        public void InitialiseIceServers_CommaSeparatedUrls_ProducesMultipleServers()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var resolver = new IceServerResolver();
            resolver.InitialiseIceServers(
                new List<RTCIceServer> { Server("stun:1.2.3.4:3478,turn:1.2.3.5:3478", "user", "pass") },
                RTCIceTransportPolicy.all);

            Assert.Equal(2, resolver.IceServers.Count);
        }

        /// <summary>
        /// The same URL appearing more than once is de-duplicated to a single ICE server entry.
        /// </summary>
        [Fact]
        public void InitialiseIceServers_DuplicateUrls_AreDeduplicated()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var resolver = new IceServerResolver();
            resolver.InitialiseIceServers(new List<RTCIceServer>
            {
                Server("stun:1.2.3.4:3478"),
                Server("stun:1.2.3.4:3478")
            }, RTCIceTransportPolicy.all);

            Assert.Single(resolver.IceServers);
        }

        /// <summary>
        /// A URL with no scheme prefix defaults to the STUN scheme (STUNUri.TryParse is lenient and treats the
        /// whole value as the host[:port]). With an IP literal host it still binds synchronously.
        /// </summary>
        [Fact]
        public void InitialiseIceServers_UrlWithoutScheme_DefaultsToStunAndBinds()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var resolver = new IceServerResolver();
            resolver.InitialiseIceServers(new List<RTCIceServer> { Server("1.2.3.4:3478") }, RTCIceTransportPolicy.all);

            var server = Assert.Single(resolver.IceServers).Value;
            Assert.Equal(STUNSchemesEnum.stun, server.Uri.Scheme);
            Assert.Equal("1.2.3.4", server.Uri.Host);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478), server.ServerEndPoint);
            Assert.Null(server.DnsResolutionTask);
        }

        /// <summary>
        /// ICE server ids are assigned sequentially starting at IceServer.MINIMUM_ICE_SERVER_ID. The id is the
        /// suffix of the STUN transaction id prefix used to correlate responses, so the assignment is pinned.
        /// </summary>
        [Fact]
        public void InitialiseIceServers_AssignsSequentialIds()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var resolver = new IceServerResolver();
            resolver.InitialiseIceServers(new List<RTCIceServer>
            {
                Server("stun:1.2.3.4:3478"),
                Server("stun:1.2.3.5:3478"),
                Server("stun:1.2.3.6:3478")
            }, RTCIceTransportPolicy.all);

            var ids = resolver.IceServers.Values.Select(s => s._id).OrderBy(x => x).ToArray();
            Assert.Equal(new[]
            {
                IceServer.MINIMUM_ICE_SERVER_ID,
                IceServer.MINIMUM_ICE_SERVER_ID + 1,
                IceServer.MINIMUM_ICE_SERVER_ID + 2
            }, ids);
        }
    }
}
