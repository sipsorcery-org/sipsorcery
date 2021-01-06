//-----------------------------------------------------------------------------
// Filename: NetServicesUnitTest.cs
//
// Description: Unit tests for the NetServices class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Nov 2019  Aaron Clauson   Created.
// 14 Dec 2020  Aaron Clauson   Migrated some regularly failing tests (on macos)
//                              from unit tests to integration tests.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.IntegrationTests
{
    [Trait("Category", "integration")]
    public class NetServicesUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public NetServicesUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that the a local address is returned for an Internet IPv6 destination.
        /// </summary>
        //[Fact(Skip = "Only works if machine running the test has a public IPv6 address assigned.")]
        // TODO: This test sporadically fails on appveyor macos jobs. Try and determine if it's due
        // to the vm having a different network configuration, IPv6 set up etc.
        [Fact]
        [Trait("Category", "IPv6")]
        public void GetLocalForInternetIPv6AdressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (Socket.OSSupportsIPv6)
            {
                if (NetServices.LocalIPAddresses.Any(x => x.AddressFamily == AddressFamily.InterNetworkV6 &&
                    !x.IsIPv6LinkLocal && !x.IsIPv6SiteLocal && !x.IsIPv6Teredo && !IPAddress.IsLoopback(x)))
                {
                    var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("2606:db00:0:62b::2"));
                    Assert.NotNull(localAddress);

                    logger.LogDebug($"Local address {localAddress}.");
                }
                else
                {
                    logger.LogDebug("Test skipped as no public IPv6 address available.");
                }
            }
            else
            {
                logger.LogDebug("Test skipped as OS does not support IPv6.");
            }
        }
    }
}
