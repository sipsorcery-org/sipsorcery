//-----------------------------------------------------------------------------
// Filename: UlpfecReceivePipelineUnitTest.cs
//
// Description: Unit tests validating receive-side RED unwrapping, depacketizer
// routing, and ULPFEC recovery pipeline in MediaStream and VideoStream.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class UlpfecReceivePipelineUnitTest
    {
        [Fact]
        public void ReceiveRedPacket_WithVp8Media_UnwrapsRedAndDispatchesVp8PayloadType()
        {
            var videoStream = new VideoStream(new RtpSessionConfig(), 0);
            var vp8Format = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000);
            var redFormat = SDPAudioVideoMediaFormat.CreateRedFormat(122);
            var ulpfecFormat = SDPAudioVideoMediaFormat.CreateUlpfecFormat(127);

            var track = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { vp8Format, redFormat, ulpfecFormat }, MediaStreamStatusEnum.SendRecv);
            videoStream.LocalTrack = track;
            videoStream.RemoteTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, true, new List<SDPAudioVideoMediaFormat> { vp8Format, redFormat, ulpfecFormat }, MediaStreamStatusEnum.SendRecv);

            videoStream.EnableUlpfec(redPayloadType: 122, ulpfecPayloadType: 127, mediaPayloadType: 96);

            var receivedPackets = new List<RTPPacket>();
            videoStream.OnRtpPacketReceivedByIndex += (idx, ep, mediaType, pkt) =>
            {
                receivedPackets.Add(pkt);
            };

            // Browser sends a VP8 media packet wrapped in RED (PT=122 on wire, inner PT=96)
            byte[] vp8Payload = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };
            byte[] redPayload = RtpRedPayload.EncodeSingleBlock(vp8Payload, 96);

            var redPacket = new RTPPacket(new ArraySegment<byte>(redPayload), 0);
            redPacket.Header.SyncSource = 0x5555;
            redPacket.Header.SequenceNumber = 100;
            redPacket.Header.Timestamp = 90000;
            redPacket.Header.MarkerBit = 1;
            redPacket.Header.PayloadType = 122; // RED on wire

            var ep = new IPEndPoint(IPAddress.Loopback, 5004);
            videoStream.OnReceiveRTPPacket(redPacket.Header, 5004, ep, redPacket.GetBytes());

            // Verify packet was unwrapped before delivery to depacketizers/consumers
            Assert.Single(receivedPackets);
            var dispatched = receivedPackets[0];
            Assert.Equal(96, dispatched.Header.PayloadType); // Inner VP8 payload type restored!
            Assert.Equal(vp8Payload, dispatched.Payload);
            Assert.Equal(100, dispatched.Header.SequenceNumber);
            Assert.Equal(90000u, dispatched.Header.Timestamp);
        }

        [Fact]
        public void ReceiveRedPacket_WithPacketLossAndFecRepair_RecoversMissingMediaPacket()
        {
            var videoStream = new VideoStream(new RtpSessionConfig(), 0);
            var h264Format = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264", 90000);
            var redFormat = SDPAudioVideoMediaFormat.CreateRedFormat(122);
            var ulpfecFormat = SDPAudioVideoMediaFormat.CreateUlpfecFormat(127);

            var track = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { h264Format, redFormat, ulpfecFormat }, MediaStreamStatusEnum.SendRecv);
            videoStream.LocalTrack = track;
            videoStream.RemoteTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, true, new List<SDPAudioVideoMediaFormat> { h264Format, redFormat, ulpfecFormat }, MediaStreamStatusEnum.SendRecv);

            videoStream.EnableUlpfec(redPayloadType: 122, ulpfecPayloadType: 127, mediaPayloadType: 96);

            var receivedPackets = new List<RTPPacket>();
            videoStream.OnRtpPacketReceivedByIndex += (idx, ep, mediaType, pkt) =>
            {
                receivedPackets.Add(pkt);
            };

            // Sender transmits media packets 101 and 102
            byte[] media1 = new byte[] { 0xAA, 0x11, 0x22 };
            byte[] media2 = new byte[] { 0xBB, 0x33, 0x44, 0x55 };

            var pkt1 = new RTPPacket(new ArraySegment<byte>(media1), 0);
            pkt1.Header.SyncSource = 0x7777;
            pkt1.Header.SequenceNumber = 101;
            pkt1.Header.Timestamp = 3000;
            pkt1.Header.PayloadType = 96;

            var pkt2 = new RTPPacket(new ArraySegment<byte>(media2), 0);
            pkt2.Header.SyncSource = 0x7777;
            pkt2.Header.SequenceNumber = 102;
            pkt2.Header.Timestamp = 3000;
            pkt2.Header.PayloadType = 96;
            pkt2.Header.MarkerBit = 1;

            // Generate FEC repair packet covering pkt1 and pkt2
            var encoder = new RtpUlpfecEncoder(122, 127, 96);
            var fecPacket = encoder.CreateFecPacket(new List<RTPPacket> { pkt1, pkt2 }, () => 200);

            var ep = new IPEndPoint(IPAddress.Loopback, 5004);

            // 1. Deliver pkt1 wrapped in RED
            byte[] red1 = RtpRedPayload.EncodeSingleBlock(media1, 96);
            var wirePkt1 = new RTPPacket(new ArraySegment<byte>(red1), 0);
            wirePkt1.Header = pkt1.Header;
            wirePkt1.Header.PayloadType = 122;
            videoStream.OnReceiveRTPPacket(wirePkt1.Header, 5004, ep, wirePkt1.GetBytes());

            // 2. pkt2 is LOST in transit! (Not submitted to OnReceiveRTPPacket)

            // 3. Deliver FEC repair packet (PT=122 on wire, inner PT=127)
            videoStream.OnReceiveRTPPacket(fecPacket.Header, 5004, ep, fecPacket.GetBytes());

            // Verify that packet 102 was recovered and delivered to the pipeline!
            Assert.Equal(2, receivedPackets.Count);
            Assert.Equal(101, receivedPackets[0].Header.SequenceNumber);
            Assert.Equal(102, receivedPackets[1].Header.SequenceNumber);
            Assert.Equal(media2, receivedPackets[1].Payload);
            Assert.Equal(96, receivedPackets[1].Header.PayloadType);

            // Telemetry validation
            Assert.Equal(1, videoStream.FecMetrics.PacketsRecovered);
        }

        [Fact]
        public void RtpRedPayload_MultiBlockEncodeAndDecode_PreservesAllGenerationsAndPrimary()
        {
            byte[] redundantData1 = new byte[] { 0x01, 0x02 };
            byte[] redundantData2 = new byte[] { 0x03, 0x04, 0x05 };
            byte[] primaryData = new byte[] { 0x06, 0x07, 0x08, 0x09 };

            var redundantBlocks = new List<RtpRedBlock>
            {
                new RtpRedBlock(payloadType: 127, timestampOffset: 200, data: redundantData1),
                new RtpRedBlock(payloadType: 127, timestampOffset: 100, data: redundantData2)
            };
            var primaryBlock = new RtpRedBlock(payloadType: 96, timestampOffset: 0, data: primaryData);

            byte[] encoded = RtpRedPayload.Encode(redundantBlocks, primaryBlock);
            Assert.NotNull(encoded);

            bool success = RtpRedPayload.TryDecode(encoded, out var decodedRedundant, out var decodedPrimary);
            Assert.True(success);
            Assert.Equal(2, decodedRedundant.Count);

            Assert.Equal(127, decodedRedundant[0].PayloadType);
            Assert.Equal(200, decodedRedundant[0].TimestampOffset);
            Assert.Equal(redundantData1, decodedRedundant[0].Data);

            Assert.Equal(127, decodedRedundant[1].PayloadType);
            Assert.Equal(100, decodedRedundant[1].TimestampOffset);
            Assert.Equal(redundantData2, decodedRedundant[1].Data);

            Assert.Equal(96, decodedPrimary.PayloadType);
            Assert.Equal(primaryData, decodedPrimary.Data);
        }
    }
}
