//-----------------------------------------------------------------------------
// Filename: UlpfecByteVectorsUnitTest.cs
//
// Description: Unit tests verifying RFC 5109 (ULPFEC) and RFC 2198 (RED)
// exact wire byte representations against independent reference vectors.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class UlpfecByteVectorsUnitTest
    {
        private readonly ILogger logger;

        public UlpfecByteVectorsUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Verifies RFC 2198 single-block RED encapsulation and unwrapping against
        /// known wire byte patterns.
        /// RFC 2198 single-block header has F=0 (bit 7 clear) followed by 7-bit block PT.
        /// </summary>
        [Fact]
        public void RedSingleBlockEncapsulationByteVectorTest()
        {
            byte innerPt = 96; // 0x60
            byte[] innerPayload = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            byte[] redPayload = RtpUlpfecEncoder.WrapInRed(innerPayload, innerPt);

            Assert.Equal(5, redPayload.Length);
            Assert.Equal(0x60, redPayload[0]); // F=0, PT=96
            Assert.Equal(0x01, redPayload[1]);
            Assert.Equal(0x04, redPayload[4]);

            bool unwrapped = RtpUlpfecEncoder.TryUnwrapRed(redPayload, out byte unwrappedPt, out byte[] unwrappedData);
            Assert.True(unwrapped);
            Assert.Equal(innerPt, unwrappedPt);
            Assert.Equal(innerPayload, unwrappedData);
        }

        /// <summary>
        /// Tests RFC 5109 FEC header layout for a deterministic two-packet group
        /// with known sequence numbers, timestamps, payload types, and payloads.
        /// Validates exact byte offsets for SN base, TS recovery, length recovery,
        /// protection length, mask, and payload parity.
        /// </summary>
        [Fact]
        public void IndependentFecHeaderByteVectorTest()
        {
            ushort seq1 = 1000;
            ushort seq2 = 1001;
            uint ts1 = 0x11223344;
            uint ts2 = 0x11223388;
            byte pt = 96; // 0x60

            byte[] payload1 = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            byte[] payload2 = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 }; // 5 bytes

            RTPPacket p1 = new RTPPacket(payload1.Length);
            p1.Header.SequenceNumber = seq1;
            p1.Header.Timestamp = ts1;
            p1.Header.PayloadType = pt;
            p1.Header.MarkerBit = 0;
            p1.Header.SyncSource = 0x12345678;
            p1.Payload = payload1;

            RTPPacket p2 = new RTPPacket(payload2.Length);
            p2.Header.SequenceNumber = seq2;
            p2.Header.Timestamp = ts2;
            p2.Header.PayloadType = pt;
            p2.Header.MarkerBit = 1; // End of frame
            p2.Header.SyncSource = 0x12345678;
            p2.Payload = payload2;

            var encoder = new RtpUlpfecEncoder(122, 127, 96);
            ushort fecSeq = 1002;
            var fecPacket = encoder.CreateFecPacket(new List<RTPPacket> { p1, p2 }, () => fecSeq);

            Assert.NotNull(fecPacket);
            Assert.Equal(122, fecPacket.Header.PayloadType);
            Assert.Equal(fecSeq, fecPacket.Header.SequenceNumber);
            Assert.Equal(ts2, fecPacket.Header.Timestamp);
            Assert.Equal(0, fecPacket.Header.MarkerBit);

            // Unwrap RED header
            Assert.True(RtpUlpfecEncoder.TryUnwrapRed(fecPacket.Payload, out byte innerPt, out byte[] rfc5109));
            Assert.Equal(127, innerPt); // ULPFEC

            // RFC 5109 header verification:
            // Byte 0: E=0, L=0 (short mask), P=0, X=0, CC=0 -> 0x00
            Assert.Equal(0x00, rfc5109[0]);

            // Byte 1: M = (0 ^ 1) = 1 (bit 7). PT recovery = (96 ^ 96) = 0 -> 0x80
            Assert.Equal(0x80, rfc5109[1]);

            // Bytes 2-3: SN base = 1000 (0x03E8)
            ushort parsedSnBase = BinaryPrimitives.ReadUInt16BigEndian(rfc5109.AsSpan(2));
            Assert.Equal(1000, parsedSnBase);

            // Bytes 4-7: TS recovery = 0x11223344 ^ 0x11223388 = 0x000000CC
            uint parsedTsRec = BinaryPrimitives.ReadUInt32BigEndian(rfc5109.AsSpan(4));
            Assert.Equal(0x11223344u ^ 0x11223388u, parsedTsRec);
            Assert.Equal(0x000000CCu, parsedTsRec);

            // Bytes 8-9: Length recovery = 4 ^ 5 = 1 (0x0001)
            ushort parsedLenRec = BinaryPrimitives.ReadUInt16BigEndian(rfc5109.AsSpan(8));
            Assert.Equal((ushort)(4 ^ 5), parsedLenRec);
            Assert.Equal(1, parsedLenRec);

            // Level 0 Header:
            // Bytes 10-11: Protection Length = max(4, 5) = 5 (0x0005)
            ushort parsedProtLen = BinaryPrimitives.ReadUInt16BigEndian(rfc5109.AsSpan(10));
            Assert.Equal(5, parsedProtLen);

            // Bytes 12-13: 16-bit mask. Packet 0 (diff 0) and packet 1 (diff 1) set -> bits 7 and 6 of byte 12 set -> 0xC0, 0x00
            Assert.Equal(0xC0, rfc5109[12]);
            Assert.Equal(0x00, rfc5109[13]);

            // Bytes 14..18: Parity payload (5 bytes):
            // byte 0: 0xAA ^ 0x11 = 0xBB
            // byte 1: 0xBB ^ 0x22 = 0x99
            // byte 2: 0xCC ^ 0x33 = 0xFF
            // byte 3: 0xDD ^ 0x44 = 0x99
            // byte 4: 0x00 ^ 0x55 = 0x55
            Assert.Equal((byte)(0xAA ^ 0x11), rfc5109[14]);
            Assert.Equal((byte)(0xBB ^ 0x22), rfc5109[15]);
            Assert.Equal((byte)(0xCC ^ 0x33), rfc5109[16]);
            Assert.Equal((byte)(0xDD ^ 0x44), rfc5109[17]);
            Assert.Equal((byte)(0x00 ^ 0x55), rfc5109[18]);
        }

        /// <summary>
        /// Tests that a group spanning more than 16 packets automatically sets the L bit
        /// and emits a 48-bit (6-byte) mask.
        /// </summary>
        [Fact]
        public void LongMaskByteVectorTest()
        {
            var packets = new List<RTPPacket>();
            ushort baseSeq = 500;
            // 20 packets spanning sequence numbers 500 to 519
            for (int i = 0; i < 20; i++)
            {
                var p = new RTPPacket(10);
                p.Header.SequenceNumber = (ushort)(baseSeq + i);
                p.Header.Timestamp = 90000;
                p.Header.PayloadType = 96;
                p.Payload = new byte[10];
                packets.Add(p);
            }

            var encoder = new RtpUlpfecEncoder(122, 127, 96, new RtpUlpfecConfig { AllowLongMask = true, MaxGroupPackets = 48 });
            var fecPacket = encoder.CreateFecPacket(packets, () => 520);

            Assert.NotNull(fecPacket);
            Assert.True(RtpUlpfecEncoder.TryUnwrapRed(fecPacket.Payload, out _, out byte[] rfc5109));

            // Bit 6 of byte 0 should be 1 (L bit set)
            Assert.True((rfc5109[0] & 0x40) != 0, "L bit must be set for group span > 16 packets");

            Assert.True(RtpUlpfecDecoder.TryParseFecPayload(rfc5109, out var info));
            Assert.True(info.LongMask);
            Assert.Equal(6, info.Mask.Length);
            Assert.Equal(20, info.ProtectedSequenceNumbers.Count);
            Assert.Equal(500, info.ProtectedSequenceNumbers[0]);
            Assert.Equal(519, info.ProtectedSequenceNumbers[19]);
        }
    }
}
