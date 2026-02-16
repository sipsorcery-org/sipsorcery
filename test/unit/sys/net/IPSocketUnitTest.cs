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
using SIPSorcery.UnitTests;
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
        public void Test172IPRangeIsPrivate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            Assert.False(IPSocket.IsPrivateAddress("172.15.1.1"), "Public IP address was mistakenly identified as private.");
            Assert.True(IPSocket.IsPrivateAddress("172.16.1.1"), "Private IP address was not correctly identified.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseEndpointTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

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
