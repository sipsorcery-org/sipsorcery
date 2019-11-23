//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
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
    public class IPSocketUnitTest
    {
        [Fact]
        public void ParsePortFromSocketTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int port = IPSocket.ParsePortFromSocket("localhost:5060");
            Console.WriteLine("port=" + port);
            Assert.True(port == 5060, "The port was not parsed correctly.");
        }

        [Fact]
        public void ParseHostFromSocketTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string host = IPSocket.ParseHostFromSocket("localhost:5060");
            Console.WriteLine("host=" + host);
            Assert.True(host == "localhost", "The host was not parsed correctly.");
        }

        [Fact]
        public void Test172IPRangeIsPrivate()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.False(IPSocket.IsPrivateAddress("172.15.1.1"), "Public IP address was mistakenly identified as private.");
            Assert.True(IPSocket.IsPrivateAddress("172.16.1.1"), "Private IP address was not correctly identified.");

            Console.WriteLine("-----------------------------------------");
        }
    }
}
