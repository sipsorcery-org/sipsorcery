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
        [TestMethod]
        public void GetLocalIPAddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddress(IPAddress.Parse("192.168.11.48"));
            Assert.IsNotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }

        [TestMethod]
        public void GetLocalIPv6AddressUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddress(IPAddress.Parse("fe80::54a9:d238:b2ee:abc"));

            Assert.IsNotNull(localAddress);

            Console.WriteLine($"Local address {localAddress}.");
        }
    }
}
