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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class NetServicesUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public NetServicesUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a local IPv4 interface is matched against a destination address on the same network.
        /// </summary>
        [Fact]
        public void GetLocalIPAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("192.168.11.48"));
            Assert.NotNull(localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the loopback address is returned for a loopback destination.
        /// </summary>
        [Fact]
        public void GetLocalForLoopbackAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Loopback);
            Assert.Equal(IPAddress.Loopback, localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the a local address is returned for an Internet destination.
        /// </summary>
        [Fact]
        public void GetLocalForInternetAdressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("67.222.131.147"));
            Assert.NotNull(localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the IPv6 loopback address is returned for an IPv6 loopback destination.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6")]
        public void GetLocalForIPv6LoopbackAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.IPv6Loopback);
            Assert.Equal(IPAddress.IPv6Loopback, localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that a local IPv6 interface is matched against a destination address on the same network.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6")]
        public void GetLocalIPv6AddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("fe80::54a9:d238:b2ee:abc"));

            Assert.NotNull(localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the a local address is returned for an Internet IPv6 destination.
        /// </summary>
        [Fact(Skip = "Only works if machine running the test has a public IPv6 address assigned.")]
        [Trait("Category", "IPv6")]
        //[Ignore] // Only works if machine running the test has a public IPv6 address assigned.
        public void GetLocalForInternetIPv6AdressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("2606:db00:0:62b::2"));
            Assert.NotNull(localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the list of local IP addresses is returned.
        /// </summary>
        [Fact]
        public void GetAllLocalIPAddressesUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddresses = NetServices.LocalIPAddresses;
            Assert.NotNull(localAddresses);

            foreach (var localAddress in localAddresses)
            {
                logger.LogDebug($"Local address {localAddress}.");
            }
        }

        /// <summary>
        /// Tests that the local address for accessing the Internet on this machine can be determined.
        /// </summary>
        [Fact]
        public void GetInternetAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localInternetAddresses = NetServices.InternetDefaultAddress;
            Assert.NotNull(localInternetAddresses);

            logger.LogDebug($"Local Internet address {localInternetAddresses}.");
        }
    }
}
