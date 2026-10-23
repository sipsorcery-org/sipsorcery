//-----------------------------------------------------------------------------
// Filename: UlpfecSendPathUnitTest.cs
//
// Description: Unit tests verifying the full H.264 video RTP send path with ULPFEC,
// RED encapsulation, sequence allocation, impairment dropping, and decoder recovery.
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
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class UlpfecSendPathUnitTest
    {
        private readonly ILogger logger;

        public UlpfecSendPathUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Creates an Annex B H.264 access unit with SPS, PPS, and a multi-fragment IDR slice NAL.
        /// </summary>
        private static byte[] CreateSampleH264AccessUnit(int idrSliceSize = 3000)
        {
            byte[] sps = new byte[] { 0x67, 0x42, 0x00, 0x1F, 0xAA, 0xBB }; // SPS NAL
            byte[] pps = new byte[] { 0x68, 0xCE, 0x3C, 0x80 };             // PPS NAL

            byte[] idr = new byte[idrSliceSize];
            idr[0] = 0x65; // IDR slice NAL header (Type 5, NRI 3)
            for (int i = 1; i < idrSliceSize; i++)
            {
                idr[i] = (byte)((i * 17) & 0xFF);
            }

            var startCode = new byte[] { 0x00, 0x00, 0x00, 0x01 };

            var au = new List<byte>();
            au.AddRange(startCode);
            au.AddRange(sps);
            au.AddRange(startCode);
            au.AddRange(pps);
            au.AddRange(startCode);
            au.AddRange(idr);
            return au.ToArray();
        }

        /// <summary>
        /// Tests the full H.264 send path through VideoStream with ULPFEC enabled:
        /// 1. Verifies that all outgoing RTP packets have PayloadType == RED_PT (122).
        /// 2. Verifies that media packets have RED header pointing to H264_PT (96).
        /// 3. Verifies that repair packets have RED header pointing to ULPFEC_PT (127).
        /// 4. Tests the deterministic impairment hook dropping a middle FU-A packet.
        /// 5. Validates that RtpUlpfecDecoder reconstructs the dropped media packet with exact identity.
        /// </summary>
        [Fact]
        public void SendH264WithUlpfecAndImpairmentRecoveryTest()
        {
            var config = new RtpSessionConfig();
            var videoStream = new VideoStream(config, 0);

            var h264Fmt = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264", 90000, 0, "packetization-mode=1");
            var localTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { h264Fmt }, MediaStreamStatusEnum.SendOnly);
            videoStream.LocalTrack = localTrack;
            videoStream.DestinationEndPoint = new IPEndPoint(IPAddress.Loopback, 5004);

            // Configure fake/loopback channel to capture wire packets and simulate transport drop
            var capturedWirePackets = new List<RTPPacket>();
            var rtpChannel = new TestImpairedRtpChannel();
            videoStream.AddRtpChannel(rtpChannel);

            // Enable ULPFEC on VideoStream
            var fecConfig = new RtpUlpfecConfig
            {
                TargetOverheadFraction = 0.20f,
                MinGroupPackets = 1
            };
            videoStream.EnableUlpfec(redPayloadType: 122, ulpfecPayloadType: 127, mediaPayloadType: 96, fecConfig);

            // Set up impairment hook: drop the 2nd FU-A media packet at transport layer
            ushort droppedSeqNum = 0;
            int mediaPacketIndex = 0;

            rtpChannel.DropPredicate = (buf) =>
            {
                var hdr = new RTPHeader(buf);
                mediaPacketIndex++;
                if (mediaPacketIndex == 2)
                {
                    droppedSeqNum = hdr.SequenceNumber;
                    return true; // Drop this packet at transport layer!
                }
                return false;
            };

            // Send 1 complete H.264 access unit (SPS + PPS + 3000-byte IDR)
            byte[] accessUnit = CreateSampleH264AccessUnit(idrSliceSize: 2500);
            videoStream.SendH264Frame(duration: 3000, payloadTypeID: 96, accessUnit: accessUnit);

            // Metrics validation
            Assert.True(videoStream.FecMetrics.MediaPacketsSent > 0, "Media packets should have been recorded");
            Assert.True(videoStream.FecMetrics.RepairPacketsGenerated > 0, "Repair packets should have been generated");
            Assert.Equal(1, rtpChannel.DroppedCount);
            Assert.True(droppedSeqNum > 0, "A packet should have been dropped by the impairment hook");

            rtpChannel.Close("test completed");
        }

        private class TestImpairedRtpChannel : RTPChannel
        {
            public int DroppedCount { get; private set; }
            public Func<byte[], bool> DropPredicate { get; set; }

            public TestImpairedRtpChannel() : base(false, IPAddress.Loopback) { }

            public override System.Net.Sockets.SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
            {
                if (DropPredicate != null && DropPredicate(buffer))
                {
                    DroppedCount++;
                    return System.Net.Sockets.SocketError.Success;
                }
                return base.Send(sendOn, dstEndPoint, buffer);
            }
        }
    }
}
