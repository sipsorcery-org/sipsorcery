//-----------------------------------------------------------------------------
// Filename: UlpfecProtectionPolicyUnitTest.cs
//
// Description: Unit tests verifying protection policy, group splitting,
// frame boundaries, flushing deadlines, and disabled FEC behavior.
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
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP.App;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class UlpfecProtectionPolicyUnitTest
    {
        private readonly ILogger logger;

        public UlpfecProtectionPolicyUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        private static RTPPacket CreatePacket(ushort seq, uint ts, int marker, int payloadLen = 200)
        {
            var pkt = new RTPPacket(payloadLen);
            pkt.Header.SequenceNumber = seq;
            pkt.Header.Timestamp = ts;
            pkt.Header.MarkerBit = marker;
            pkt.Header.PayloadType = 96;
            pkt.Header.SyncSource = 0x55555555;
            pkt.Payload = new byte[payloadLen];
            return pkt;
        }

        /// <summary>
        /// Tests that a frame completion (MarkerBit = 1) triggers prompt repair generation
        /// even when the group is smaller than the target ratio count.
        /// </summary>
        [Fact]
        public void MarkerBitTriggersImmediateFecGenerationTest()
        {
            ushort seqCounter = 1000;
            Func<ushort> nextSeq = () => ++seqCounter;

            var config = new RtpUlpfecConfig
            {
                TargetOverheadFraction = 0.20f, // 1 per 5 packets normally
                MinGroupPackets = 1
            };
            var encoder = new RtpUlpfecEncoder(122, 127, 96, config);

            // Packet 1: MarkerBit = 0 -> no FEC yet
            var p1 = CreatePacket(100, 90000, 0);
            encoder.AddMediaPacket(p1, nextSeq, out var rep1);
            Assert.Null(rep1);

            // Packet 2: MarkerBit = 1 (end of frame) -> triggers FEC immediately!
            var p2 = CreatePacket(101, 90000, 1);
            encoder.AddMediaPacket(p2, nextSeq, out var rep2);
            Assert.NotNull(rep2);
            Assert.Single(rep2);
            Assert.Equal(122, rep2[0].Header.PayloadType);
        }

        /// <summary>
        /// Tests that a large multi-packet IDR frame is safely partitioned across multiple
        /// protection groups without corrupting timestamps or exceeding the group limit.
        /// </summary>
        [Fact]
        public void LargeIdrFrameSplitAcrossGroupsTest()
        {
            ushort seqCounter = 5000;
            Func<ushort> nextSeq = () => ++seqCounter;

            var config = new RtpUlpfecConfig
            {
                MaxGroupPackets = 5,
                TargetOverheadFraction = 0.20f,
                MinGroupPackets = 1
            };
            var encoder = new RtpUlpfecEncoder(122, 127, 96, config);

            // Large frame with 12 packets, all sharing the same timestamp, last packet has MarkerBit = 1
            int totalPackets = 12;
            uint idrTimestamp = 180000;
            int totalFecPacketsProduced = 0;

            for (int i = 0; i < totalPackets; i++)
            {
                int marker = (i == totalPackets - 1) ? 1 : 0;
                var pkt = CreatePacket((ushort)(1000 + i), idrTimestamp, marker, 1000);

                encoder.AddMediaPacket(pkt, nextSeq, out var rep);
                if (rep != null)
                {
                    totalFecPacketsProduced += rep.Count;
                    foreach (var f in rep)
                    {
                        Assert.Equal(idrTimestamp, f.Header.Timestamp);
                    }
                }
            }

            // 12 packets with MaxGroupPackets=5 -> Group 1 (5 pkts), Group 2 (5 pkts), Group 3 (2 pkts with marker) -> exactly 3 FEC packets
            Assert.Equal(3, totalFecPacketsProduced);
        }

        /// <summary>
        /// Tests that partial groups can be flushed on demand (e.g. on stream pause or timeout)
        /// using Flush(force: true).
        /// </summary>
        [Fact]
        public void FlushForcesPartialGroupEmissionTest()
        {
            ushort seqCounter = 3000;
            Func<ushort> nextSeq = () => ++seqCounter;

            var config = new RtpUlpfecConfig
            {
                TargetOverheadFraction = 0.10f, // 1 per 10 normally
                MinGroupPackets = 1
            };
            var encoder = new RtpUlpfecEncoder(122, 127, 96, config);

            // Add 2 packets without marker bit
            encoder.AddMediaPacket(CreatePacket(200, 90000, 0), nextSeq, out var rep1);
            encoder.AddMediaPacket(CreatePacket(201, 90000, 0), nextSeq, out var rep2);
            Assert.Null(rep1);
            Assert.Null(rep2);

            // Flush forced
            var flushed = encoder.Flush(nextSeq, force: true);
            Assert.NotNull(flushed);
            Assert.Single(flushed);

            // Subseqent flush yields nothing because group is empty
            var flushedAgain = encoder.Flush(nextSeq, force: true);
            Assert.Null(flushedAgain);
        }

        /// <summary>
        /// Tuning alone must not opt a video track into RED or ULPFEC negotiation.
        /// </summary>
        [Fact]
        public void TrackWithoutRedundancyNeverActivatesFecDespiteTuningTest()
        {
            using var session = new RTPSession(new RtpSessionConfig());
            var track = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
            }) { FecConfig = new RtpUlpfecConfig { TargetOverheadFraction = 0.5f } };
            session.addTrack(track);
            var offer = new SDP(IPAddress.Loopback) { Connection = new SDPConnectionInformation(IPAddress.Loopback) };
            offer.Media.Add(new SDPMediaAnnouncement(SDPMediaTypesEnum.video, 9000, new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000),
                SDPAudioVideoMediaFormat.CreateRedFormat(122),
                SDPAudioVideoMediaFormat.CreateUlpfecFormat(127)
            }) { Transport = "RTP/AVP" });

            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, offer));
            Assert.Equal(RtpRedundancyOptions.None, track.Redundancy);
            Assert.Null(session.VideoStream.FecEncoder);
            Assert.Null(session.VideoStream.FecDecoder);
            Assert.DoesNotContain(session.CreateAnswer(IPAddress.Loopback).Media.Single().MediaFormats.Values,
                format => format.IsRed || format.IsUlpfec);
        }
    }
}
