//-----------------------------------------------------------------------------
// Filename: PortRangeUnitTest.cs
//
// Description: Unit tests for the PortRange class.
//
// Author(s):
// Tobias Stähli
// 
// History:
// 6 Dec 2021  Tobias Stähli   Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.UnitTests.sys.net
{
    [Trait("Category", "unit")]
    public class PortRangeUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public PortRangeUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests simple sequential port allocation
        /// </summary>
        [Fact]
        public void GetSequentialPortUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var portRange = new PortRange(6000, 6005);
            Assert.Equal(6000, portRange.GetNextPort());
            Assert.Equal(6002, portRange.GetNextPort());
            Assert.Equal(6004, portRange.GetNextPort());
            Assert.Equal(6000, portRange.GetNextPort());
            Assert.Equal(6002, portRange.GetNextPort());
        }

        /// <summary>
        /// Tests statistical distribution of shuffled port allocation
        /// </summary>
        [Fact]
        public void GetShuffledPortsEvenlyDistributedUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var portRange = new PortRange(6000, 6011, shuffle: true);
            var portCount = new Dictionary<int, int>();
            const int N = 50_000;
            const int startPort = 6000;
            const int endPort = 6011;
            const float atLeastXPercentPerPort = 0.9f;
            for (int i = 0; i < N; i++) { 
                var p = portRange.GetNextPort();
                if (!portCount.ContainsKey(p))
                {
                    portCount[p] = 1;
                }
                else
                {
                    portCount[p]++;
                }
            }
            var portAssignedCount = ((endPort + 1) - startPort) / 2;
            Assert.Equal(portCount.Count, portAssignedCount);
            for(int i = startPort; i<endPort; i += 2)
            {
                logger.LogTrace("shuffled PortRange Port {Port}: Actual Count:{ActualCount} Expected Count: {ExpectedCount} Acceptable Range: [{a}, {b}])", i, portCount[i], (N / portAssignedCount), (N / portAssignedCount) * atLeastXPercentPerPort, (N / portAssignedCount) * ((1.0 - atLeastXPercentPerPort) + 1));
                Assert.True(portCount.ContainsKey(i), $"Expected port {i} to be allocated at least once");
                Assert.True(portCount[i] > (N / portAssignedCount) * atLeastXPercentPerPort);
                Assert.True(portCount[i] < (N / portAssignedCount) * ((1.0 - atLeastXPercentPerPort) + 1));
            }            
        }
    }
}
