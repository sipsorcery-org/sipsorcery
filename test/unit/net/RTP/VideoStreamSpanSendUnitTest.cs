//-----------------------------------------------------------------------------
// Filename: VideoStreamSpanSendUnitTest.cs
//
// Description: Unit tests for the span based video send methods, which let an
// encoded frame be sent from a re-used buffer, and for the allocation profile
// of the video send path.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 06 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class VideoStreamSpanSendUnitTest
    {
        public static IEnumerable<object[]> GoldenScenarios() => VideoStreamPacketisationGoldenUnitTest.GoldenScenarios();

        /// <summary>
        /// Sending from a span puts exactly the same packets on the wire as sending the same frame from an array.
        /// </summary>
        [Theory]
        [MemberData(nameof(GoldenScenarios))]
        public void SendVideo_Span_MatchesGoldenWireOutput(string scenarioName, string expectedDigest)
        {
            var scenario = VideoRtpGoldenScenario.Get(scenarioName);

            var packets = scenario.Run((stream, timestamp, frame) =>
            {
                if (timestamp.HasValue)
                {
                    stream.SendVideoAt(timestamp.Value, new ReadOnlySpan<byte>(frame));
                }
                else
                {
                    stream.SendVideo(VideoRtpGoldenScenario.FRAME_DURATION, new ReadOnlySpan<byte>(frame));
                }
            });

            Assert.Equal(expectedDigest, VideoRtpGoldenScenario.Digest(packets));
        }

        /// <summary>
        /// The frame can be anywhere in a larger buffer, as it is when an encoder writes it into a re-used stream, or
        /// in memory that is not an array at all.
        /// </summary>
        [Theory]
        [InlineData("VP8_Sizes")]
        [InlineData("H264_SpsPpsIdr")]
        [InlineData("H264_RandomBytes")]
        [InlineData("H265_ParameterSetsAndIdr")]
        [InlineData("VP9_KeyAndInterFrames")]
        [InlineData("AV1_TemporalUnits")]
        [InlineData("MJPEG_Frames")]
        public void SendVideo_SpanInsideLargerBufferOrOnTheStack_MatchesGoldenWireOutput(string scenarioName)
        {
            var scenario = VideoRtpGoldenScenario.Get(scenarioName);

            var fromSlice = scenario.Run((stream, timestamp, frame) =>
            {
                var buffer = new byte[frame.Length + 64];
                new Random(frame.Length).NextBytes(buffer);
                frame.CopyTo(buffer, 17);
                stream.SendVideo(VideoRtpGoldenScenario.FRAME_DURATION, new ReadOnlySpan<byte>(buffer, 17, frame.Length));
            });

            // The largest frame in these scenarios is 60 KB, small enough for the stack.
            var fromStack = scenario.Run((stream, timestamp, frame) =>
            {
                Span<byte> span = stackalloc byte[frame.Length];
                frame.CopyTo(span);
                stream.SendVideo(VideoRtpGoldenScenario.FRAME_DURATION, span);
            });

            Assert.Equal(scenario.ExpectedDigest, VideoRtpGoldenScenario.Digest(fromSlice));
            Assert.Equal(scenario.ExpectedDigest, VideoRtpGoldenScenario.Digest(fromStack));
        }

        /// <summary>
        /// The buffers handed to the transport are the transport's own: overwriting the sample once the send has
        /// returned, as a caller re-using its encode buffer does for the next frame, does not change packets that may
        /// still be in flight. The channel here keeps the arrays it was given rather than copies of them.
        /// </summary>
        [Theory]
        [InlineData("VP8", "VP8_Sizes")]
        [InlineData("H264", "H264_SpsPpsIdr")]
        [InlineData("H265", "H265_ParameterSetsAndIdr")]
        [InlineData("VP9", "VP9_KeyAndInterFrames")]
        public void SendVideo_SampleReusedAfterReturn_PacketsAlreadyHandedToTransportAreUnchanged(string codec, string scenarioName)
        {
            var scenario = VideoRtpGoldenScenario.Get(scenarioName);
            var expected = scenario.Run((stream, timestamp, frame) => stream.SendVideo(VideoRtpGoldenScenario.FRAME_DURATION, frame));

            var stream = CreateStream(codec, scenario.PayloadType);
            var channel = new RetainingRtpChannel();
            stream.AddRtpChannel(channel);
            try
            {
                var reused = new byte[scenario.CreateFrames().Max(f => f.Length)];
                foreach (var frame in scenario.CreateFrames())
                {
                    frame.CopyTo(reused, 0);
                    stream.SendVideo(VideoRtpGoldenScenario.FRAME_DURATION, new ReadOnlySpan<byte>(reused, 0, frame.Length));

                    // The next frame goes into the same buffer.
                    reused.AsSpan().Fill(0xEE);
                }

                Assert.Equal(expected.Count, channel.Packets.Count);
                for (int i = 0; i < expected.Count; i++)
                {
                    Assert.Equal(expected[i], channel.Packets[i]);
                }
            }
            finally
            {
                channel.Close("test complete");
            }
        }

        /// <summary>
        /// The array methods of the codecs whose frames are parsed (VP8, H264 and H265) reject a null frame with an
        /// ArgumentNullException, before anything is sent.
        /// </summary>
        [Theory]
        [InlineData("VP8")]
        [InlineData("H264")]
        [InlineData("H265")]
        public void SendVideo_NullFrame_Rejected(string codec)
        {
            var stream = CreateStream(codec, 96);
            var channel = new RetainingRtpChannel();
            stream.AddRtpChannel(channel);
            try
            {
                Assert.Throws<ArgumentNullException>(() => stream.SendVideo(3000, (byte[])null));
                Assert.Throws<ArgumentNullException>(() => stream.SendVideoAt(3000, (byte[])null));
                Assert.Empty(channel.Packets);
            }
            finally
            {
                channel.Close("test complete");
            }
        }

        /// <summary>
        /// A SocketException from the transport is logged rather than thrown, the same for each codec whose frames are
        /// packetised here.
        /// </summary>
        [Theory]
        [InlineData("VP8")]
        [InlineData("H264")]
        [InlineData("H265")]
        public void SendVideo_TransportSocketException_NotThrown(string codec)
        {
            var stream = CreateStream(codec, 96);
            var channel = new ThrowingRtpChannel();
            stream.AddRtpChannel(channel);
            try
            {
                byte[] frame = (codec == "VP8") ?
                    new byte[3000] :
                    new byte[] { 0x00, 0x00, 0x00, 0x01, (codec == "H264") ? (byte)0x65 : (byte)0x26, 0x01, 0xAA };

                Assert.Null(Record.Exception(() => stream.SendVideo(3000, frame)));
                Assert.Null(Record.Exception(() => stream.SendVideo(3000, new ReadOnlySpan<byte>(frame))));
                Assert.True(channel.Attempts >= 2);
            }
            finally
            {
                channel.Close("test complete");
            }
        }

#if NETCOREAPP
        /// <summary>
        /// With the transport, statistics and extensions out of the picture, sending a frame allocates little more
        /// than the wire buffer of each packet: no payload or header arrays, random number buffers, enumerators or
        /// copies of the NALs.
        /// </summary>
        [Theory]
        [InlineData("VP8", 60_000, false)]
        [InlineData("VP8", 60_000, true)]
        [InlineData("VP8", 8_000, true)]
        [InlineData("H264", 60_000, false)]
        [InlineData("H264", 60_000, true)]
        [InlineData("H265", 60_000, false)]
        [InlineData("H265", 60_000, true)]
        public void SendVideo_AllocatesLittleMoreThanTheWireBuffers(string codec, int frameLength, bool fromSpan)
        {
            var frame = new byte[frameLength];
            new Random(1).NextBytes(frame);
            if (codec == "H264" || codec == "H265")
            {
                // No accidental start codes.
                for (int i = 0; i < frame.Length; i++)
                {
                    frame[i] = (byte)Math.Max(2, (int)frame[i]);
                }
            }

            if (codec == "H264")
            {
                // One large IDR NAL.
                frame[0] = 0; frame[1] = 0; frame[2] = 0; frame[3] = 1; frame[4] = 0x65;
            }
            else if (codec == "H265")
            {
                // A start code and a two byte NAL header of the given type, returning where the NAL starts.
                int PutStartCodeAndHeader(int position, int type)
                {
                    frame[position] = 0; frame[position + 1] = 0; frame[position + 2] = 0; frame[position + 3] = 1;
                    frame[position + 4] = (byte)(type << 1);
                    frame[position + 5] = 0x01;
                    return position + 4;
                }

                // Parameter sets small enough to share an aggregation packet, then one large IDR NAL.
                int next = PutStartCodeAndHeader(0, 32) + 24;
                next = PutStartCodeAndHeader(next, 33) + 40;
                next = PutStartCodeAndHeader(next, 34) + 8;
                PutStartCodeAndHeader(next, 19);
            }

            var stream = CreateStream(codec, 96);
            var counter = new CountingRtpChannel();
            stream.AddRtpChannel(counter);
            try
            {
                void SendOne()
                {
                    if (fromSpan)
                    {
                        stream.SendVideo(3000, new ReadOnlySpan<byte>(frame));
                    }
                    else
                    {
                        stream.SendVideo(3000, frame);
                    }
                }

                // Warm up, so JIT and first use costs are not counted.
                for (int i = 0; i < 5; i++)
                {
                    SendOne();
                }

                const int frames = 20;
                long packetsBefore = counter.Packets;
                long wireBytesBefore = counter.Bytes;
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

                for (int i = 0; i < frames; i++)
                {
                    SendOne();
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                long packets = counter.Packets - packetsBefore;
                long wireBytes = counter.Bytes - wireBytesBefore;

                Assert.True(packets >= frames * (frameLength / 1200));

                // The wire buffers themselves plus a small per packet allowance for the array object headers and the
                // RTP header object.
                long budget = wireBytes + packets * 160;
                Assert.True(allocated <= budget,
                    $"{codec}: allocated {allocated} bytes for {packets} packets of {wireBytes} wire bytes ({allocated / (double)wireBytes:F2}x); budget {budget}.");
            }
            finally
            {
                counter.Close("test complete");
            }
        }
#endif

        /// <summary>
        /// The RTP session span method sends the frame on the session's primary video stream, end to end over loopback.
        /// </summary>
        [Fact]
        public async Task RTPSession_SendVideoSpan_ReceivedByRemotePeer()
        {
            var sender = new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithVideoTrack(96, "VP8", 90000, MediaStreamStatusEnum.SendOnly).Build();
            var receiver = new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithVideoTrack(96, "VP8", 90000, MediaStreamStatusEnum.RecvOnly).Build();
            var received = new ConcurrentQueue<RTPPacket>();
            receiver.OnRtpPacketReceived += (ep, mediaType, packet) => received.Enqueue(packet);

            try
            {
                Assert.Equal(SetDescriptionResultEnum.OK, receiver.SetRemoteDescription(SdpType.offer, sender.CreateOffer(IPAddress.Loopback)));
                Assert.Equal(SetDescriptionResultEnum.OK, sender.SetRemoteDescription(SdpType.answer, receiver.CreateAnswer(IPAddress.Loopback)));
                await sender.Start();
                await receiver.Start();

                var frame = new byte[3000];
                new Random(3).NextBytes(frame);
                var buffer = new byte[4000];
                frame.CopyTo(buffer, 100);

                sender.SendVideo(3000, new ReadOnlySpan<byte>(buffer, 100, frame.Length));
                sender.SendVideoAt(9000, new ReadOnlySpan<byte>(buffer, 100, frame.Length));

                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (received.Count < 6 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(20);
                }

                var packets = received.OrderBy(p => (short)(p.Header.SequenceNumber - received.First().Header.SequenceNumber)).ToList();
                Assert.Equal(6, packets.Count);

                // Strip the one byte VP8 descriptors and reassemble each frame.
                var first = packets.Take(3).SelectMany(p => p.Payload.Skip(1)).ToArray();
                var second = packets.Skip(3).SelectMany(p => p.Payload.Skip(1)).ToArray();
                Assert.Equal(frame, first);
                Assert.Equal(frame, second);
                Assert.All(packets.Skip(3), p => Assert.Equal(9000u, p.Header.Timestamp));
                Assert.Equal(1, packets[2].Header.MarkerBit);
                Assert.Equal(1, packets[5].Header.MarkerBit);
            }
            finally
            {
                sender.Close("test done");
                receiver.Close("test done");
                sender.Dispose();
                receiver.Dispose();
            }
        }

        private static VideoStream CreateStream(string codec, int payloadType)
        {
            var format = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, payloadType, codec, 90000, 0, codec == "H264" ? "packetization-mode=1" : null);
            var stream = new VideoStream(new RtpSessionConfig(), 0);
            stream.LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { format }, MediaStreamStatusEnum.SendOnly);
            stream.LocalTrack.Ssrc = VideoRtpGoldenScenario.SSRC;
            stream.LocalTrack.SeqNum = VideoRtpGoldenScenario.FIRST_SEQUENCE_NUMBER;
            stream.DestinationEndPoint = new IPEndPoint(IPAddress.Loopback, 5004);
            return stream;
        }

        /// <summary>
        /// Keeps the very arrays it is asked to send, as a real socket does until the send completes.
        /// </summary>
        private sealed class RetainingRtpChannel : RTPChannel
        {
            public List<byte[]> Packets { get; } = new List<byte[]>();

            public RetainingRtpChannel() : base(false, IPAddress.Loopback) { }

            public override SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
            {
                Packets.Add(buffer);
                return SocketError.Success;
            }
        }

        /// <summary>
        /// Fails every send with a SocketException.
        /// </summary>
        private sealed class ThrowingRtpChannel : RTPChannel
        {
            public int Attempts;

            public ThrowingRtpChannel() : base(false, IPAddress.Loopback) { }

            public override SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
            {
                Attempts++;
                throw new SocketException((int)SocketError.NoBufferSpaceAvailable);
            }
        }

        /// <summary>
        /// Counts the packets and bytes it is asked to send, without allocating.
        /// </summary>
        private sealed class CountingRtpChannel : RTPChannel
        {
            public long Packets;
            public long Bytes;

            public CountingRtpChannel() : base(false, IPAddress.Loopback) { }

            public override SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
            {
                Packets++;
                Bytes += buffer.Length;
                return SocketError.Success;
            }
        }
    }
}
