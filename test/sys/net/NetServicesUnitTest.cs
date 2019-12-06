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
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class NetServicesUnitTest
    {
        /// <summary>
        /// Tests that a local IPv4 interface is matched against a destination address on the same network.
        /// </summary>
        [Fact]
        public void GetLocalIPAddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("192.168.11.48"));
            Assert.NotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the loopback address is returned for a loopback destination.
        /// </summary>
        [Fact]
        public void GetLocalForLoopbackAddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Loopback);
            Assert.Equal(IPAddress.Loopback, localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the a local address is returned for an Internet destination.
        /// </summary>
        [Fact]
        public void GetLocalForInternetAdressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("67.222.131.147"));
            Assert.NotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the IPv6 loopback address is returned for an IPv6 loopback destination.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6")]
        public void GetLocalForIPv6LoopbackAddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.IPv6Loopback);
            Assert.Equal(IPAddress.IPv6Loopback, localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that a local IPv6 interface is matched against a destination address on the same network.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6")]
        public void GetLocalIPv6AddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("fe80::54a9:d238:b2ee:abc"));

            Assert.NotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the a local address is returned for an Internet IPv6 destination.
        /// </summary>
        [Fact(Skip = "Only works if machine running the test has a public IPv6 address assigned.")]
        [Trait("Category", "IPv6")]
        //[Ignore] // Only works if machine running the test has a public IPv6 address assigned.
        public void GetLocalForInternetIPv6AdressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("2606:db00:0:62b::2"));
            Assert.NotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the list of local IP addresses is returned.
        /// </summary>
        [Fact]
        public void GetAllLocalIPAddressesUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddresses = NetServices.GetAllLocalIPAddresses();
            Assert.NotNull(localAddresses);

            foreach (var localAddress in localAddresses)
            {
                Console.WriteLine($"Local address {localAddress}.");
            }
        }

        /// <summary>
        /// Tests that the local address for accessing the Internet on this machine can be determined.
        /// </summary>
        [Fact]
        public void GetInternetAddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localInternetAddresses = NetServices.GetLocalAddressForInternet();
            Assert.NotNull(localInternetAddresses);

            Console.WriteLine($"Local Internet address {localInternetAddresses}.");
        }
    }
}
