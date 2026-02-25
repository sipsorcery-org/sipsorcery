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

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpChunkUnitTest
    {
        private readonly ILogger logger;

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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

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

        /// <summary>
        /// The gap ack block and duplicate TSN counts in a SACK chunk are supplied by the remote party and
        /// each allows up to 65535 entries. They must be validated against the length the chunk declared.
        /// The maximum counts drive the parse loops past the end of the receive buffer, and the resulting
        /// IndexOutOfRangeException is not one of the recoverable parse failures the SCTP receive loop
        /// expects, so it terminated the receive thread and with it the association and every data channel.
        /// The counts must be rejected as a recoverable parse failure instead.
        /// </summary>
        [Theory]
        [InlineData(0xFFFF, 0x0000)]   // gap ack blocks alone are enough.
        [InlineData(0x0000, 0xFFFF)]   // as are duplicate TSNs alone.
        [InlineData(0xFFFF, 0xFFFF)]
        [InlineData(0x0064, 0x0000)]   // a count small enough to stay in bounds must be rejected too - it
        [InlineData(0x0000, 0x0064)]   // silently parses stale bytes from earlier packets instead.
        public void ParseSACKChunkWithOversizedCountsIsRejected(int numGapAckBlocks, int numDuplicateTSNs)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            // A SACK chunk declaring only the fixed parameters (chunk length 16) but claiming it carries
            // gap ack blocks and/or duplicate TSNs, parsed from a buffer the size of the real receive
            // buffer so an unbounded read has somewhere to run before it leaves the array.
            var buffer = new byte[(int)SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW];

            buffer[0] = (byte)SctpChunkType.SACK;
            buffer[1] = 0x00;                                       // Chunk flags.
            NetConvert.ToBuffer((ushort)16, buffer, 2);             // Chunk length, fixed parameters only.
            NetConvert.ToBuffer(1000U, buffer, 4);                  // Cumulative TSN ack.
            NetConvert.ToBuffer(262144U, buffer, 8);                // ARwnd.
            NetConvert.ToBuffer((ushort)numGapAckBlocks, buffer, 12);
            NetConvert.ToBuffer((ushort)numDuplicateTSNs, buffer, 14);

            var ex = Assert.Throws<SipSorceryException>(() => SctpSackChunk.ParseChunk(buffer.AsSpan()));

            logger.LogDebug("Parse rejected with: {Message}", ex.Message);
        }

        /// <summary>
        /// A SACK chunk too short to even hold the fixed parameters must also be rejected. SctpPacket only
        /// checks a chunk is at least an SCTP chunk header long, so without this the fixed parameter reads
        /// run past the end of the chunk.
        /// </summary>
        [Fact]
        public void ParseSACKChunkShorterThanFixedParametersIsRejected()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = new byte[64];

            buffer[0] = (byte)SctpChunkType.SACK;
            buffer[1] = 0x00;
            NetConvert.ToBuffer((ushort)4, buffer, 2);              // Chunk header only.

            Assert.Throws<ApplicationException>(() => SctpSackChunk.ParseChunk(buffer.AsSpan()));
        }

        /// <summary>
        /// A SACK chunk whose counts match its declared length must still parse, including when it sits
        /// after the SCTP common header in a full packet.
        /// </summary>
        [Fact]
        public void ParseSACKChunkWithMatchingCountsSucceeds()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            // Chunk length 16 fixed + 2 gap ack blocks (8) + 1 duplicate TSN (4) = 28.
            var buffer = new byte[64];

            buffer[0] = (byte)SctpChunkType.SACK;
            buffer[1] = 0x00;
            NetConvert.ToBuffer((ushort)28, buffer, 2);
            NetConvert.ToBuffer(1000U, buffer, 4);
            NetConvert.ToBuffer(262144U, buffer, 8);
            NetConvert.ToBuffer((ushort)2, buffer, 12);             // Gap ack blocks.
            NetConvert.ToBuffer((ushort)1, buffer, 14);             // Duplicate TSNs.
            NetConvert.ToBuffer((ushort)3, buffer, 16);             // Gap block 1 start.
            NetConvert.ToBuffer((ushort)5, buffer, 18);             // Gap block 1 end.
            NetConvert.ToBuffer((ushort)8, buffer, 20);             // Gap block 2 start.
            NetConvert.ToBuffer((ushort)9, buffer, 22);             // Gap block 2 end.
            NetConvert.ToBuffer(1234U, buffer, 24);                 // Duplicate TSN.

            var sackChunk = SctpSackChunk.ParseChunk(buffer.AsSpan());

            Assert.Equal(1000U, sackChunk.CumulativeTsnAck);
            Assert.Equal(262144U, sackChunk.ARwnd);
            Assert.Equal(2, sackChunk.GapAckBlocks.Count);
            Assert.Equal(3, sackChunk.GapAckBlocks[0].Start);
            Assert.Equal(5, sackChunk.GapAckBlocks[0].End);
            Assert.Equal(8, sackChunk.GapAckBlocks[1].Start);
            Assert.Equal(9, sackChunk.GapAckBlocks[1].End);
            Assert.Equal(1234U, sackChunk.DuplicateTSN.Single());
        }
    }
}
