//-----------------------------------------------------------------------------
// Filename: RtpIceChannelIPv6RouteUnitTest.cs
//
// Description: Unit tests for not pairing remote IPv6 candidates the host has
// no route to. The route lookup is replaced through the internal HasRouteToIPv6
// seam so the tests do not depend on the machine's network configuration.
//
// Author(s):
// Claude Code
//
// History:
// 23 Oct 2026	Claude Code	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RtpIceChannelIPv6RouteUnitTest
    {
        private const string GLOBAL_IPV6_ADDRESS = "2001:db8::1";
        private const string IPV4_ADDRESS = "192.168.1.50";

        public RtpIceChannelIPv6RouteUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static RTCIceCandidate Remote(string address, ushort port = 5000)
        {
            return new RTCIceCandidate(new RTCIceCandidateInit())
            {
                address = address,
                port = port,
                protocol = RTCIceProtocol.udp,
                component = RTCIceComponent.rtp,
                sdpMLineIndex = 0
            };
        }

        private static List<string> ChecklistAddresses(RtpIceChannel channel)
        {
            lock (channel._checklist)
            {
                return channel._checklist.Select(x => x.RemoteCandidate.address).ToList();
            }
        }

        /// <summary>
        /// The default channel binds to the IPv6 wildcard in dual mode, which is what pairs IPv6
        /// remote candidates. Hosts where IPv6 isn't available can't exercise the rule.
        /// </summary>
        private static bool CanPairIPv6(RtpIceChannel channel) => Socket.OSSupportsIPv6 && channel.IsDualMode;

        [Fact]
        public async Task UnroutableIPv6RemoteCandidateIsNotPaired()
        {
            using var channel = new RtpIceChannel();
            if (!CanPairIPv6(channel))
            {
                return;
            }

            var lookedUp = new List<IPAddress>();
            channel.HasRouteToIPv6 = destination =>
            {
                lookedUp.Add(destination);
                return false;
            };

            await channel.UpdateChecklist(channel._localChecklistCandidate, Remote(GLOBAL_IPV6_ADDRESS));
            await channel.UpdateChecklist(channel._localChecklistCandidate, Remote(IPV4_ADDRESS));

            Assert.Equal(new[] { IPV4_ADDRESS }, ChecklistAddresses(channel));
            Assert.Equal(new[] { IPAddress.Parse(GLOBAL_IPV6_ADDRESS) }, lookedUp);
        }

        [Fact]
        public async Task RoutableIPv6RemoteCandidateIsPaired()
        {
            using var channel = new RtpIceChannel();
            if (!CanPairIPv6(channel))
            {
                return;
            }

            channel.HasRouteToIPv6 = _ => true;

            await channel.UpdateChecklist(channel._localChecklistCandidate, Remote(GLOBAL_IPV6_ADDRESS));

            Assert.Equal(new[] { GLOBAL_IPV6_ADDRESS }, ChecklistAddresses(channel));
        }

        /// <summary>
        /// The route lookup is only for IPv6, IPv4 candidates are paired as before.
        /// </summary>
        [Fact]
        public async Task IPv4RemoteCandidateDoesNotUseTheIPv6RouteLookup()
        {
            using var channel = new RtpIceChannel();
            channel.HasRouteToIPv6 = destination =>
            {
                Assert.Fail($"Unexpected IPv6 route lookup for {destination}.");
                return false;
            };

            await channel.UpdateChecklist(channel._localChecklistCandidate, Remote(IPV4_ADDRESS));

            Assert.Equal(new[] { IPV4_ADDRESS }, ChecklistAddresses(channel));
        }
    }
}
