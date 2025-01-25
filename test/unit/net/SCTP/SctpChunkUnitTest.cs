//-----------------------------------------------------------------------------
// Filename: SctpChunkUnitTest.cs
//
// Description: Unit tests for the SctpChunk class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpChunkUnitTest
    {
        private ILogger logger = null;

        public SctpChunkUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a HEARTBEAT chunk can be round tripped correctly.
        /// </summary>
        [Fact]
        public void RoundtripHeartBeatChunk()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SctpChunk heartbeatChunk = new SctpChunk(SctpChunkType.HEARTBEAT)
            {
                ChunkFlags = 0,
                ChunkValue = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }
            };

            byte[] buffer = new byte[heartbeatChunk.GetChunkLength(true)];

            heartbeatChunk.WriteTo(buffer, 0);

            var rndTripChunk = SctpChunk.Parse(buffer, 0);

            Assert.Equal(SctpChunkType.HEARTBEAT, rndTripChunk.KnownType);
            Assert.Equal(0, rndTripChunk.ChunkFlags);
            Assert.Equal("0102030405", rndTripChunk.ChunkValue.HexStr());
        }

        /// <summary>
        /// Tests that a SACK chunk can be parsed correctly.
        /// </summary>
        [Fact]
        public void ParseSACKChunk()
        {
            var sackBuffer = BufferUtils.ParseHexStr("13881388E48092946AB2050003000014D19244F60002000000000001A7498379");

            var sackPkt = SctpPacket.Parse(sackBuffer, 0, sackBuffer.Length);

            Assert.NotNull(sackPkt);
            Assert.Single(sackPkt.Chunks);
            Assert.Equal(2806612857U, (sackPkt.Chunks.Single() as SctpSackChunk).DuplicateTSN.Single());
        }
    }
}
