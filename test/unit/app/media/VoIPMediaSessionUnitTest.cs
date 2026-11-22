//-----------------------------------------------------------------------------
// Filename: VoIPMediaSessionUnitTest.cs
//
// Description: Unit tests for how VoIPMediaSession sends the frames of its
// video source.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 16 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Media.UnitTests
{
    [Trait("Category", "unit")]
    public class VoIPMediaSessionUnitTest
    {
        /// <summary>
        /// A video source that raises encoded frames only when a test asks it to, and shows who has subscribed.
        /// </summary>
        private class FakeVideoSource : IVideoSource
        {
            public event EncodedSampleDelegate OnVideoSourceEncodedSample;
#pragma warning disable CS0067
            public event RawVideoSampleDelegate OnVideoSourceRawSample;
            public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;
            public event SourceErrorDelegate OnVideoSourceError;
#pragma warning restore CS0067

            public Delegate[] ArrayHandlers => OnVideoSourceEncodedSample?.GetInvocationList() ?? Array.Empty<Delegate>();

            public Task PauseVideo() => Task.CompletedTask;
            public Task ResumeVideo() => Task.CompletedTask;
            public Task StartVideo() => Task.CompletedTask;
            public Task CloseVideo() => Task.CompletedTask;
            public List<VideoFormat> GetVideoSourceFormats() => new List<VideoFormat> { new VideoFormat(VideoCodecsEnum.VP8, 96) };
            public void SetVideoSourceFormat(VideoFormat videoFormat) { }
            public void RestrictFormats(Func<VideoFormat, bool> filter) { }
            public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) { }
            public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) { }
            public void ForceKeyFrame() { }
            public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
            public bool IsVideoSourcePaused() => false;
        }

        /// <summary>
        /// The same, also able to raise its frames as a span.
        /// </summary>
        private sealed class FakeSpanVideoSource : FakeVideoSource, IVideoSourceSpan
        {
            public event EncodedSampleFasterDelegate OnVideoSourceEncodedSampleFaster;

            public Delegate[] SpanHandlers => OnVideoSourceEncodedSampleFaster?.GetInvocationList() ?? Array.Empty<Delegate>();

            public void RaiseEncodedSampleFaster(uint durationRtpUnits, ReadOnlySpan<byte> sample) =>
                OnVideoSourceEncodedSampleFaster?.Invoke(durationRtpUnits, sample);
        }

        /// <summary>
        /// A video source that can raise its frames as a span has the session's span based send subscribed to its span
        /// event, and nothing subscribed to its array event, so each frame is sent once and without an array.
        /// </summary>
        [Fact]
        public void VideoSourceWithSpanEvent_SessionSendsFromTheSpanEvent()
        {
            var source = new FakeSpanVideoSource();
            var session = new VoIPMediaSession(new MediaEndPoints { VideoSource = source });

            try
            {
                var handler = Assert.Single(source.SpanHandlers);
                Assert.Same(session, handler.Target);
                Assert.Equal(nameof(RTPSession.SendVideo), handler.Method.Name);
                Assert.Empty(source.ArrayHandlers);
            }
            finally
            {
                session.Close("test done");
                session.Dispose();
            }
        }

        /// <summary>
        /// A video source that only raises arrays has the session's send subscribed to its array event, as before.
        /// </summary>
        [Fact]
        public void VideoSourceWithoutSpanEvent_SessionSendsFromTheArrayEvent()
        {
            var source = new FakeVideoSource();
            var session = new VoIPMediaSession(new MediaEndPoints { VideoSource = source });

            try
            {
                var handler = Assert.Single(source.ArrayHandlers);
                Assert.Same(session, handler.Target);
                Assert.Equal(nameof(RTPSession.SendVideo), handler.Method.Name);
            }
            finally
            {
                session.Close("test done");
                session.Dispose();
            }
        }

        /// <summary>
        /// A frame the video source raises as a span is sent by the session, end to end over loopback.
        /// </summary>
        [Fact]
        public async Task VideoSourceWithSpanEvent_FrameRaisedAsSpanIsSent()
        {
            var source = new FakeSpanVideoSource();
            var sender = new VoIPMediaSession(new MediaEndPoints { VideoSource = source }, IPAddress.Loopback);
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
                source.RaiseEncodedSampleFaster(3000, new ReadOnlySpan<byte>(buffer, 100, frame.Length));

                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (received.Count < 3 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(20);
                }

                var packets = received.OrderBy(p => (short)(p.Header.SequenceNumber - received.First().Header.SequenceNumber)).ToList();
                Assert.Equal(3, packets.Count);

                // Strip the one byte VP8 descriptors and reassemble the frame.
                Assert.Equal(frame, packets.SelectMany(p => p.Payload.Skip(1)).ToArray());
                Assert.Equal(1, packets[2].Header.MarkerBit);
            }
            finally
            {
                sender.Close("test done");
                receiver.Close("test done");
                sender.Dispose();
                receiver.Dispose();
            }
        }
    }
}
