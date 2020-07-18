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
using SIPSorcery.Net.Sctp;
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

            var dataChanOpen = new DataChannelOpen("label");
            byte[] pkt = dataChanOpen.getBytes();

            logger.LogDebug(pkt.HexStr());

            var rndTripPkt = new DataChannelOpen(new ByteBuffer(pkt));

            Assert.NotNull(rndTripPkt);
            Assert.Equal(dataChanOpen.getLabel(), rndTripPkt.getLabel());
        }

        /// <summary>
        /// Tests that a Data Channel Open chunk can be correctly serialised and parsed.
        /// </summary>
        [Fact]
        public void RoundTripDataChannelOpenChunkUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DataChunk dcopen = DataChunk.mkDataChannelOpen("123");
            int chunkLength = dcopen.getChunkLength();

            Assert.Equal(32, chunkLength);

            byte[] buf = new byte[chunkLength];
            ByteBuffer byteBuf = new ByteBuffer(buf);
            dcopen.write(byteBuf);

            logger.LogDebug(byteBuf.Data.HexStr());

            var rndTripChunk = new DataChunk(Chunk.CType.DATA, 0, byteBuf.Data.Length, byteBuf);
            var dataChannelOpenChunk = rndTripChunk.getDCEP();

            Assert.NotNull(rndTripChunk);
            Assert.NotNull(dataChannelOpenChunk);
        }
    }
}
