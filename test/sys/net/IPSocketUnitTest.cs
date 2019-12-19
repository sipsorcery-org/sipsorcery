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
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class IPSocketUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public IPSocketUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ParsePortFromSocketTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int port = IPSocket.ParsePortFromSocket("localhost:5060");
            logger.LogDebug("port=" + port);
            Assert.True(port == 5060, "The port was not parsed correctly.");
        }

        [Fact]
        public void ParseHostFromSocketTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string host = IPSocket.ParseHostFromSocket("localhost:5060");
            logger.LogDebug("host=" + host);
            Assert.True(host == "localhost", "The host was not parsed correctly.");
        }

        [Fact]
        public void Test172IPRangeIsPrivate()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.False(IPSocket.IsPrivateAddress("172.15.1.1"), "Public IP address was mistakenly identified as private.");
            Assert.True(IPSocket.IsPrivateAddress("172.16.1.1"), "Private IP address was not correctly identified.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string host = null;
            int port = 0;

            string endpoint = "localhost:5060";
            Assert.False(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Valid endpoint address");
            Assert.True(host == "localhost", "The host was not parsed correctly.");
            Assert.True(port == 5060, "The port was not parsed correctly.");
            Assert.False(IPSocket.IsIPAddress(host), $"'{endpoint}' Valid ip address");

            endpoint = "host.domain.tld:5060";
            Assert.False(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Valid endpoint address");
            Assert.True(host == "host.domain.tld", "The host was not parsed correctly.");
            Assert.True(port == 5060, "The port was not parsed correctly.");
            Assert.False(IPSocket.IsIPAddress(host), $"'{endpoint}' Valid ip address");

            endpoint = "127.0.0.1:5060";
            Assert.True(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Invalid endpoint address");
            Assert.True(host == "127.0.0.1", "The host was not parsed correctly.");
            Assert.True(port == 5060, "The port was not parsed correctly.");
            Assert.True(IPSocket.IsIPAddress(host), $"'{endpoint}' Invalid ip address");

            endpoint = "[::1]:5060";
            Assert.True(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Invalid endpoint address");
            Assert.True(host == "::1", "The host was not parsed correctly.");
            Assert.True(port == 5060, "The port was not parsed correctly.");
            Assert.True(IPSocket.IsIPAddress(host), $"'{endpoint}' Invalid ip address");

            endpoint = "[::ffff:127.0.0.1]:5060";
            Assert.True(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Invalid endpoint address");
            Assert.True(host == "::ffff:127.0.0.1", "The host was not parsed correctly.");
            Assert.True(port == 5060, "The port was not parsed correctly.");
            Assert.True(IPSocket.IsIPAddress(host), $"'{endpoint}' Invalid ip address");


            endpoint = "localhost";
            Assert.False(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Valid endpoint address");
            Assert.True(host == "localhost", "The host was not parsed correctly.");

            endpoint = "host.domain.tld";
            Assert.False(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Valid endpoint address");
            Assert.True(host == "host.domain.tld", "The host was not parsed correctly.");

            endpoint = "127.0.0.1";
            Assert.True(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Invalid endpoint address");
            Assert.True(host == "127.0.0.1", "The host was not parsed correctly.");
            Assert.True(IPSocket.IsIPAddress(host), $"'{endpoint}' Invalid ip address");

            endpoint = "[::1]";
            Assert.True(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Invalid endpoint address");
            Assert.True(host == "::1", "The host was not parsed correctly.");
            Assert.True(IPSocket.IsIPAddress(host), $"'{endpoint}' Invalid ip address");

            endpoint = "[::ffff:127.0.0.1]";
            Assert.True(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Invalid endpoint address");
            Assert.True(host == "::ffff:127.0.0.1", "The host was not parsed correctly.");
            Assert.True(IPSocket.IsIPAddress(host), $"'{endpoint}' Invalid ip address");

            endpoint = "::1";
            Assert.True(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Invalid endpoint address");
            Assert.True(host == "::1", "The host was not parsed correctly.");
            Assert.True(IPSocket.IsIPAddress(host), $"'{endpoint}' Invalid ip address");

            endpoint = "::ffff:127.0.0.1";
            Assert.True(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Invalid endpoint address");
            Assert.True(host == "::ffff:127.0.0.1", "The host was not parsed correctly.");
            Assert.True(IPSocket.IsIPAddress(host), $"'{endpoint}' Invalid ip address");

            endpoint = "::ffff:127.0.0..1";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint, out host, out port));

            endpoint = "::ffff:";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint, out host, out port));

            endpoint = "127.0.0..1";
            Assert.False(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Valid endpoint address");
            Assert.False(IPSocket.IsIPAddress(host), $"'{endpoint}' Valid ip address");

            endpoint = "127.0.0.";
            Assert.False(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Valid endpoint address");
            Assert.False(IPSocket.IsIPAddress(host), $"'{endpoint}' Valid ip address");

            endpoint = "328.0.0.1";
            Assert.False(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Valid endpoint address");
            Assert.False(IPSocket.IsIPAddress(host), $"'{endpoint}' Valid ip address");

            endpoint = "\0";
            Assert.False(IPSocket.Parse(endpoint, out host, out port), $"'{endpoint}' Valid endpoint address");
            Assert.False(IPSocket.IsIPAddress(host), $"'{endpoint}' Valid ip address");

        }

        [Fact]
        public void ParseEndpointTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            System.Net.IPEndPoint ep = null;

            string endpoint = "localhost:5060";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(System.Net.IPAddress.IsLoopback(ep.Address), "The host was not parsed correctly.");
            Assert.True(ep.Port == 5060, "The port was not parsed correctly.");

            endpoint = "host.domain.tld:5060";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint));

            endpoint = "127.0.0.1:5060";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(ep.Address.ToString() == "127.0.0.1", "The host was not parsed correctly.");
            Assert.True(ep.Port == 5060, "The port was not parsed correctly.");

            endpoint = "[::1]:5060";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(ep.Address.ToString() == "::1", "The host was not parsed correctly.");
            Assert.True(ep.Port == 5060, "The port was not parsed correctly.");

            endpoint = "[::ffff:127.0.0.1]:5060";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(ep.Address.ToString() == "::ffff:127.0.0.1", "The host was not parsed correctly.");
            Assert.True(ep.Port == 5060, "The port was not parsed correctly.");

            endpoint = "localhost";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(System.Net.IPAddress.IsLoopback(ep.Address), "The host was not parsed correctly.");

            endpoint = "host.domain.tld";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint));

            endpoint = "127.0.0.1";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(ep.Address.ToString() == "127.0.0.1", "The host was not parsed correctly.");

            endpoint = "[::1]";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(ep.Address.ToString() == "::1", "The host was not parsed correctly.");

            endpoint = "[::ffff:127.0.0.1]";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(ep.Address.ToString() == "::ffff:127.0.0.1", "The host was not parsed correctly.");

            endpoint = "::1";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(ep.Address.ToString() == "::1", "The host was not parsed correctly.");

            endpoint = "::ffff:127.0.0.1";
            ep = IPSocket.Parse(endpoint);
            Assert.NotNull(ep);
            Assert.True(ep.Address.ToString() == "::ffff:127.0.0.1", "The host was not parsed correctly.");

            endpoint = "::ffff:127.0.0..1";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint));

            endpoint = "::ffff:";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint));

            endpoint = "127.0.0..1";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint));

            endpoint = "127.0.0.";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint));

            endpoint = "328.0.0.1";
            Assert.Throws<FormatException>(() => IPSocket.Parse(endpoint));

            endpoint = "\0";
            Assert.Throws<ArgumentException>(() => IPSocket.Parse(endpoint));
        }
    }
}
