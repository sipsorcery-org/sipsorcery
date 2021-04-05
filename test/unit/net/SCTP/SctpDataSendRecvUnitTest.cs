//-----------------------------------------------------------------------------
// Filename: SctpDataSendRecvUnitTest.cs
//
// Description: Unit tests to check the SctpDataSender and SctpDataReceiver
// classes work together.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Apr 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpDataSendRecvUnitTest
    {
        private ILogger logger = null;

        public SctpDataSendRecvUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a SACK chunk can trigger a retransmit.
        /// </summary>
        [Fact]
        public void SingleRetransmit()
        {
            uint arwnd = 131072;
            ushort mtu = 1400;
            uint initialTSN = Crypto.GetRandomUInt(true);

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);

            uint? blockTSN = null;
            Action<SctpDataChunk> sendChunk = (chunk) =>
            {
                if (blockTSN == null || chunk.TSN != blockTSN)
                {
                    // Forward data chunks from the sender to the receiver.
                    receiver.OnDataChunk(chunk);
                }
            };

            SctpDataSender sender = new SctpDataSender(sendChunk, mtu, initialTSN, arwnd);
            
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 1, sender.TSN);
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // This send to the receiver is blocked so the receivers ACK TSN should stay the same.
            blockTSN = sender.TSN;
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 2, sender.TSN);
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // Unblock. Receiver's ACK TSN should not advance as it has a missing chunk.
            blockTSN = null;
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 3, sender.TSN);
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // Pass the receiver's status to the sender via a SACK chunk.
            var sackChunk = receiver.GetSackChunk();
            sender.GotSack(sackChunk);
            Assert.Single(sackChunk.GapAckBlocks);

            // Check that the sender has queued the missing chunk for a retransmit.
            Assert.NotNull(sender.NextRetransmitChunk);
            Assert.Equal(initialTSN + 1, sender.NextRetransmitChunk.TSN);

            // Supply the chunk to the receiver and check that the state catches up.
            receiver.OnDataChunk(sender.NextRetransmitChunk);
            Assert.Equal(initialTSN + 2, receiver.CumulativeAckTSN);

            sackChunk = receiver.GetSackChunk();
            Assert.Empty(sackChunk.GapAckBlocks);
            Assert.Equal(initialTSN + 2, receiver.CumulativeAckTSN);
        }
    }
}
