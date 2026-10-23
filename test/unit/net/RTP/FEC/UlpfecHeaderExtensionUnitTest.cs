//-----------------------------------------------------------------------------
// Filename: UlpfecHeaderExtensionUnitTest.cs
//
// Description: Unit tests validating RFC 5109 Section 7 & 9 conformance
// for RTP header extension and CSRC protection and bit-exact reconstruction.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class UlpfecHeaderExtensionUnitTest
    {
        [Fact]
        public void ProtectAndRecover_RtpPacketWithHeaderExtension_RecoversExtensionBitExact()
        {
            // Create a media packet with an RFC 5285 1-byte header extension (e.g. abs-send-time: ID=1, len=3)
            byte[] mediaPayload1 = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
            var originalPacket = new RTPPacket(new ArraySegment<byte>(mediaPayload1), 0);
            originalPacket.Header.SyncSource = 0x12345678;
            originalPacket.Header.SequenceNumber = 1001;
            originalPacket.Header.Timestamp = 90000;
            originalPacket.Header.MarkerBit = 0;
            originalPacket.Header.PayloadType = 96;

            // Add 1-byte header extension (profile 0xBEDE, length = 1 word = 4 bytes)
            byte[] extPayload = new byte[] { 0x12, 0x01, 0x02, 0x03 }; // ID=1, len=2 (3 bytes data), followed by data
            originalPacket.Header.HeaderExtensionFlag = 1;
            originalPacket.Header.ExtensionProfile = RTPHeader.ONE_BYTE_EXTENSION_PROFILE;
            originalPacket.Header.ExtensionLength = 1; // 1 32-bit word = 4 bytes
            originalPacket.Header.ExtensionPayload = extPayload;

            // Also create a second media packet in the same group with another extension
            byte[] mediaPayload2 = new byte[] { 0x11, 0x22, 0x33 };
            var secondPacket = new RTPPacket(new ArraySegment<byte>(mediaPayload2), 0);
            secondPacket.Header.SyncSource = 0x12345678;
            secondPacket.Header.SequenceNumber = 1002;
            secondPacket.Header.Timestamp = 90000;
            secondPacket.Header.MarkerBit = 1;
            secondPacket.Header.PayloadType = 96;

            byte[] extPayload2 = new byte[] { 0x21, 0xAA, 0xBB, 0x00 }; // ID=2, len=1 (2 bytes data), 1 byte pad
            secondPacket.Header.HeaderExtensionFlag = 1;
            secondPacket.Header.ExtensionProfile = RTPHeader.ONE_BYTE_EXTENSION_PROFILE;
            secondPacket.Header.ExtensionLength = 1;
            secondPacket.Header.ExtensionPayload = extPayload2;

            // Serialize both packets to ensure their internal byte representations are finalized
            var pkt1 = new RTPPacket(originalPacket.GetBytes());
            var pkt2 = new RTPPacket(secondPacket.GetBytes());

            Assert.Equal(1, pkt1.Header.HeaderExtensionFlag);
            Assert.Equal(1, pkt2.Header.HeaderExtensionFlag);

            // Generate RFC 5109 repair packet over both media packets
            var encoder = new RtpUlpfecEncoder(redPayloadType: 122, ulpfecPayloadType: 127, mediaPayloadType: 96);
            ushort repairSeqNum = 5000;
            var fecPacket = encoder.CreateFecPacket(new List<RTPPacket> { pkt1, pkt2 }, () => repairSeqNum);

            Assert.NotNull(fecPacket);

            // Simulate loss of packet 1001: attempt recovery using only packet 1002 and the FEC packet
            var decoder = new RtpUlpfecDecoder(redPayloadType: 122, ulpfecPayloadType: 127);
            var receivedMedia = new Dictionary<ushort, RTPPacket>
            {
                { pkt2.Header.SequenceNumber, pkt2 }
            };

            bool recovered = decoder.TryRecoverPacket(fecPacket, receivedMedia, out var recoveredPacket);

            Assert.True(recovered, "ULPFEC recovery should succeed when 1 packet in group is missing");
            Assert.NotNull(recoveredPacket);

            // Verify header values
            Assert.Equal(pkt1.Header.SequenceNumber, recoveredPacket.Header.SequenceNumber);
            Assert.Equal(pkt1.Header.Timestamp, recoveredPacket.Header.Timestamp);
            Assert.Equal(pkt1.Header.MarkerBit, recoveredPacket.Header.MarkerBit);
            Assert.Equal(pkt1.Header.PayloadType, recoveredPacket.Header.PayloadType);
            Assert.Equal(pkt1.Header.SyncSource, recoveredPacket.Header.SyncSource);

            // Verify RFC 5109 §7 Header Extension recovery
            Assert.Equal(1, recoveredPacket.Header.HeaderExtensionFlag);
            Assert.Equal(RTPHeader.ONE_BYTE_EXTENSION_PROFILE, recoveredPacket.Header.ExtensionProfile);
            Assert.Equal(1, recoveredPacket.Header.ExtensionLength);
            Assert.Equal(extPayload, recoveredPacket.Header.ExtensionPayload);

            // Verify media payload recovery
            Assert.Equal(pkt1.Payload, recoveredPacket.Payload);
        }

        [Fact]
        public void ProtectAndRecover_MixedPackets_OneWithExtensionOneWithout_RecoversBothAccurately()
        {
            // Packet 1 has extension, Packet 2 does not
            var original1 = new RTPPacket(new ArraySegment<byte>(new byte[] { 0x55, 0x66 }), 0);
            original1.Header.SyncSource = 0x9999;
            original1.Header.SequenceNumber = 2001;
            original1.Header.Timestamp = 12345;
            original1.Header.PayloadType = 96;
            original1.Header.HeaderExtensionFlag = 1;
            original1.Header.ExtensionProfile = RTPHeader.ONE_BYTE_EXTENSION_PROFILE;
            original1.Header.ExtensionLength = 1;
            original1.Header.ExtensionPayload = new byte[] { 0x11, 0x22, 0x33, 0x44 };
            var pkt1 = new RTPPacket(original1.GetBytes());

            var original2 = new RTPPacket(new ArraySegment<byte>(new byte[] { 0x77, 0x88, 0x99, 0x00 }), 0);
            original2.Header.SyncSource = 0x9999;
            original2.Header.SequenceNumber = 2002;
            original2.Header.Timestamp = 12345;
            original2.Header.PayloadType = 96;
            original2.Header.HeaderExtensionFlag = 0;
            var pkt2 = new RTPPacket(original2.GetBytes());

            var encoder = new RtpUlpfecEncoder(122, 127, 96);
            var fecPacket = encoder.CreateFecPacket(new List<RTPPacket> { pkt1, pkt2 }, () => 6000);

            var decoder = new RtpUlpfecDecoder(122, 127);

            // Case A: Recover pkt1 (the one with extension)
            var receivedA = new Dictionary<ushort, RTPPacket> { { pkt2.Header.SequenceNumber, pkt2 } };
            Assert.True(decoder.TryRecoverPacket(fecPacket, receivedA, out var recovered1));
            Assert.Equal(1, recovered1.Header.HeaderExtensionFlag);
            Assert.Equal(pkt1.Header.ExtensionPayload, recovered1.Header.ExtensionPayload);
            Assert.Equal(pkt1.Payload, recovered1.Payload);

            // Case B: Recover pkt2 (the one without extension)
            var receivedB = new Dictionary<ushort, RTPPacket> { { pkt1.Header.SequenceNumber, pkt1 } };
            Assert.True(decoder.TryRecoverPacket(fecPacket, receivedB, out var recovered2));
            Assert.Equal(0, recovered2.Header.HeaderExtensionFlag);
            Assert.Equal(pkt2.Payload, recovered2.Payload);
        }
    }
}
