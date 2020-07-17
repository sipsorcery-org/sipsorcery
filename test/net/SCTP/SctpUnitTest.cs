//-----------------------------------------------------------------------------
// Filename: SctpUnitUnitTest.cs
//
// Description: Unit tests for the SCTP classes.
//
// History:
// 17 Jul 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class SctpUnitUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SctpUnitUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a Data Channel Open packet can be correctly serialised and parsed.
        /// </summary>
        [Fact]
        public void RoundTripDataChannelOpenPacketUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var dataChanOpen = new DCOpen("label");
            byte[] pkt = dataChanOpen.getBytes();

            logger.LogDebug(pkt.HexStr());

            var rndTripPkt = new DCOpen(new ByteBuffer(pkt));

            Assert.NotNull(rndTripPkt);
            Assert.Equal(dataChanOpen.getLabel(), rndTripPkt.getLabel());
        }
    }
}
