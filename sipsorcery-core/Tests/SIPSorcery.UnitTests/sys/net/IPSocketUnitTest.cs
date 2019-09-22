using System;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Sys.UnitTests
{
    [TestClass]
    public class IPSocketUnitTest
    {
        [TestMethod]
        public void ParsePortFromSocketTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int port = IPSocket.ParsePortFromSocket("localhost:5060");
            Console.WriteLine("port=" + port);
            Assert.IsTrue(port == 5060, "The port was not parsed correctly.");
        }

        [TestMethod]
        public void ParseHostFromSocketTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string host = IPSocket.ParseHostFromSocket("localhost:5060");
            Console.WriteLine("host=" + host);
            Assert.IsTrue(host == "localhost", "The host was not parsed correctly.");
        }

        [TestMethod]
        public void Test172IPRangeIsPrivate()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.IsFalse(IPSocket.IsPrivateAddress("172.15.1.1"), "Public IP address was mistakenly identified as private.");
            Assert.IsTrue(IPSocket.IsPrivateAddress("172.16.1.1"), "Private IP address was not correctly identified.");

            Console.WriteLine("-----------------------------------------");
        }
    }
}
