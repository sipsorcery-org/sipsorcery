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
        //[TestMethod]
        //[Ignore] // This is a machine and OS specific test.
        //public void GetLocalIPAddressUnitTest()
        //{
        //    Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    var localAddress = NetServices.GetLocalAddress(IPAddress.Parse("192.168.11.48"));
        //    Assert.IsNotNull(localAddress);

        //    Console.WriteLine($"Local address {localAddress}.");
        //}

        /// <summary>
        /// /// Tests that a local IPv6 interface is matched against a destination address on the same network.
        /// </summary>
        //[TestMethod]
        //[Ignore] // This is a machine and OS specific test.
        //public void GetLocalIPv6AddressUnitTest()
        //{
        //    Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    var localAddress = NetServices.GetLocalAddress(IPAddress.Parse("fe80::54a9:d238:b2ee:abc"));

        //    Assert.IsNotNull(localAddress);

        //    Console.WriteLine($"Local address {localAddress}.");
        //}
    }
}
