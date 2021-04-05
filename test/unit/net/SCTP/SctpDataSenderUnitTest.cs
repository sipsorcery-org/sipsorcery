//-----------------------------------------------------------------------------
// Filename: SctpDataSenderUnitTest.cs
//
// Description: Unit tests for the SctpDataSender class.
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
    public class SctpDataSenderUnitTest
    {
        private ILogger logger = null;

        public SctpDataSenderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a small buffer, less than MTU, gets processed correctly.
        /// </summary>
        [Fact]
        public void SmallBufferSend()
        {
            BlockingCollection<byte[]> inStm = new BlockingCollection<byte[]>();
            BlockingCollection<byte[]> outStm = new BlockingCollection<byte[]>();
            var mockTransport = new MockB2BSctpTransport(outStm, inStm);
            SctpAssociation assoc = new SctpAssociation(mockTransport, null, 5000, 5000, 1400);

            SctpDataSender sender = new SctpDataSender(assoc.SendChunk, 1400, 0, 1000);
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });

            Assert.Single(outStm);

            byte[] sendBuffer = outStm.Single();
            SctpPacket pkt = SctpPacket.Parse(sendBuffer, 0, sendBuffer.Length);

            Assert.NotNull(pkt);
            Assert.NotNull(pkt.Chunks.Single() as SctpDataChunk);

            var datachunk = pkt.Chunks.Single() as SctpDataChunk;

            Assert.Equal("000102", datachunk.UserData.HexStr());
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

            // This send to the receiver is blocked so the receivers ack TSN should stay the same.
            blockTSN = sender.TSN;
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 2, sender.TSN);
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // Unblock. Receiver's ack TSN should not advance as it has a missing chunk.
            blockTSN = null;
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 3, sender.TSN);
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // Pass the receiver's status to the sender via a SACK chunk.
            var sackChunk = receiver.GetSackChunk();
            sender.GotSack(sackChunk);

        }
    }
}
