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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Sys.UnitTests
{
    [TestClass]
    public class NetServicesUnitTest
    {
        /// <summary>
        /// Tests that a local IPv4 interface is matched against a destination address on the same network.
        /// </summary>
        [TestMethod]
        public void GetLocalIPAddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddress(IPAddress.Parse("192.168.11.48"));
            Assert.IsNotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the loopback address is returned for a loopback destination.
        /// </summary>
        [TestMethod]
        public void GetLocalForLoopbackAddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddress(IPAddress.Loopback);
            Assert.AreEqual(IPAddress.Loopback, localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the a local address is returned for an Internet destination.
        /// </summary>
        [TestMethod]
        public void GetLocalForInternetAdressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddress(IPAddress.Parse("67.222.131.147"));
            Assert.IsNotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the IPv6 loopback address is returned for an IPv6 loopback destination.
        /// </summary>
        [TestMethod]
        [TestCategory("IPv6")]
        public void GetLocalForIPv6LoopbackAddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddress(IPAddress.IPv6Loopback);
            Assert.AreEqual(IPAddress.IPv6Loopback, localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that a local IPv6 interface is matched against a destination address on the same network.
        /// </summary>
        [TestMethod]
        //[TestCategory("IPv6")]
        public void GetLocalIPv6AddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddress(IPAddress.Parse("fe80::54a9:d238:b2ee:abc"));

            Assert.IsNotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the a local address is returned for an Internet IPv6 destination.
        /// </summary>
        [TestMethod]
        //[TestCategory("IPv6")]
        public void GetLocalForInternetIPv6AdressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddress(IPAddress.Parse("2606:db00:0:62b::2"));
            Assert.IsNotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }
    }
}
