//-----------------------------------------------------------------------------
// Filename: UlpfecRecoveryUnitTest.cs
//
// Description: Unit tests verifying packet recovery behavior under single-loss,
// variable-length, sequence-wraparound, and unrecoverable multi-loss conditions.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class UlpfecRecoveryUnitTest
    {
        private readonly ILogger logger;

        public UlpfecRecoveryUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        private static List<RTPPacket> CreateTestMediaPackets(ushort startSeq, uint timestamp, int count, int payloadSize = 500)
        {
            var list = new List<RTPPacket>();
            for (int i = 0; i < count; i++)
            {
                byte[] payload = new byte[payloadSize + (i * 50)]; // variable lengths!
                for (int b = 0; b < payload.Length; b++)
                {
                    payload[b] = (byte)((i * 37 + b) & 0xFF);
                }

                var pkt = new RTPPacket(payload.Length);
                pkt.Header.SequenceNumber = (ushort)(startSeq + i);
                pkt.Header.Timestamp = timestamp;
                pkt.Header.PayloadType = 96;
                pkt.Header.MarkerBit = (i == count - 1) ? 1 : 0;
                pkt.Header.SyncSource = 0x87654321;
                pkt.Payload = payload;

                list.Add(pkt);
            }
            return list;
        }

        /// <summary>
        /// Tests that EVERY possible single media packet dropped from a 4-packet group
        /// can be recovered with exact bitwise identity.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void RecoverEverySinglePacketInGroupTest(int droppedIndex)
        {
            var mediaPackets = CreateTestMediaPackets(startSeq: 2000, timestamp: 90000, count: 4, payloadSize: 600);
            var expectedLost = mediaPackets[droppedIndex];

            var encoder = new RtpUlpfecEncoder(122, 127, 96);
            var fecPacket = encoder.CreateFecPacket(mediaPackets, () => 2004);
            Assert.NotNull(fecPacket);

            // Simulate loss: build received dictionary omitting droppedIndex
            var received = new Dictionary<ushort, RTPPacket>();
            for (int i = 0; i < mediaPackets.Count; i++)
            {
                if (i != droppedIndex)
                {
                    received[mediaPackets[i].Header.SequenceNumber] = mediaPackets[i];
                }
            }

            var decoder = new RtpUlpfecDecoder(122, 127);
            bool recovered = decoder.TryRecoverPacket(fecPacket, received, out var recoveredPacket);

            Assert.True(recovered, $"Failed to recover packet at index {droppedIndex}");
            Assert.NotNull(recoveredPacket);
            Assert.Equal(expectedLost.Header.SequenceNumber, recoveredPacket.Header.SequenceNumber);
            Assert.Equal(expectedLost.Header.Timestamp, recoveredPacket.Header.Timestamp);
            Assert.Equal(expectedLost.Header.MarkerBit, recoveredPacket.Header.MarkerBit);
            Assert.Equal(expectedLost.Header.PayloadType, recoveredPacket.Header.PayloadType);
            Assert.Equal(expectedLost.Header.SyncSource, recoveredPacket.Header.SyncSource);
            Assert.Equal(expectedLost.Payload.Length, recoveredPacket.Payload.Length);
            Assert.Equal(expectedLost.Payload, recoveredPacket.Payload);
        }

        /// <summary>
        /// Tests recovery across the 16-bit sequence number wraparound boundary (65534, 65535, 0, 1).
        /// </summary>
        [Theory]
        [InlineData(65534)]
        [InlineData(65535)]
        [InlineData(0)]
        [InlineData(1)]
        public void RecoverAcrossSequenceWraparoundTest(ushort seqToDrop)
        {
            var mediaPackets = new List<RTPPacket>();
            ushort[] seqs = new ushort[] { 65534, 65535, 0, 1 };

            for (int i = 0; i < seqs.Length; i++)
            {
                byte[] payload = new byte[300 + i * 40];
                for (int b = 0; b < payload.Length; b++)
                {
                    payload[b] = (byte)(i + b);
                }

                var pkt = new RTPPacket(payload.Length);
                pkt.Header.SequenceNumber = seqs[i];
                pkt.Header.Timestamp = 123456;
                pkt.Header.PayloadType = 96;
                pkt.Header.MarkerBit = (i == seqs.Length - 1) ? 1 : 0;
                pkt.Header.SyncSource = 0xAABBCCDD;
                pkt.Payload = payload;

                mediaPackets.Add(pkt);
            }

            var encoder = new RtpUlpfecEncoder(122, 127, 96);
            var fecPacket = encoder.CreateFecPacket(mediaPackets, () => 2);
            Assert.NotNull(fecPacket);

            var expectedLost = mediaPackets.First(p => p.Header.SequenceNumber == seqToDrop);

            var received = new Dictionary<ushort, RTPPacket>();
            foreach (var p in mediaPackets)
            {
                if (p.Header.SequenceNumber != seqToDrop)
                {
                    received[p.Header.SequenceNumber] = p;
                }
            }

            var decoder = new RtpUlpfecDecoder(122, 127);
            bool recovered = decoder.TryRecoverPacket(fecPacket, received, out var recoveredPacket);

            Assert.True(recovered, $"Failed to recover sequence {seqToDrop} across wrap");
            Assert.Equal(expectedLost.Header.SequenceNumber, recoveredPacket.Header.SequenceNumber);
            Assert.Equal(expectedLost.Header.Timestamp, recoveredPacket.Header.Timestamp);
            Assert.Equal(expectedLost.Payload, recoveredPacket.Payload);
        }

        /// <summary>
        /// Tests that when two or more media packets are missing from the same single-parity group,
        /// recovery correctly fails and does NOT fabricate false success.
        /// </summary>
        [Fact]
        public void UnrecoverableMultiLossReturnsFalseTest()
        {
            var mediaPackets = CreateTestMediaPackets(startSeq: 100, timestamp: 90000, count: 4, payloadSize: 500);

            var encoder = new RtpUlpfecEncoder(122, 127, 96);
            var fecPacket = encoder.CreateFecPacket(mediaPackets, () => 104);

            // Drop 2 packets: 101 and 102
            var received = new Dictionary<ushort, RTPPacket>
            {
                [100] = mediaPackets[0],
                [103] = mediaPackets[3]
            };

            var decoder = new RtpUlpfecDecoder(122, 127);
            bool recovered = decoder.TryRecoverPacket(fecPacket, received, out var recoveredPacket);

            Assert.False(recovered, "Decoder must not claim recovery when 2 packets are missing");
            Assert.Null(recoveredPacket);
        }

        /// <summary>
        /// Tests that when no packets are lost, the decoder indicates no recovery needed.
        /// </summary>
        [Fact]
        public void NoLossRequiresNoRecoveryTest()
        {
            var mediaPackets = CreateTestMediaPackets(startSeq: 100, timestamp: 90000, count: 4, payloadSize: 500);

            var encoder = new RtpUlpfecEncoder(122, 127, 96);
            var fecPacket = encoder.CreateFecPacket(mediaPackets, () => 104);

            var received = mediaPackets.ToDictionary(p => p.Header.SequenceNumber);

            var decoder = new RtpUlpfecDecoder(122, 127);
            bool recovered = decoder.TryRecoverPacket(fecPacket, received, out var recoveredPacket);

            Assert.False(recovered);
            Assert.Null(recoveredPacket);
        }

        /// <summary>
        /// Tests that a truncated or corrupt FEC packet is handled gracefully without crashing.
        /// </summary>
        [Fact]
        public void MalformedFecPacketHandledGracefullyTest()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);

            var corruptPkt = new RTPPacket(5);
            corruptPkt.Header.PayloadType = 122;
            corruptPkt.Payload = new byte[] { 127, 0x00, 0x01 }; // Truncated RFC 5109 payload

            bool recovered = decoder.TryRecoverPacket(corruptPkt, new Dictionary<ushort, RTPPacket>(), out var result);
            Assert.False(recovered);
            Assert.Null(result);
        }
    }
}
