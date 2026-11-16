//-----------------------------------------------------------------------------
// Filename: SipSinkNodeTests.cs
//
// Description: Loopback tests for the route verb's sip: sink. A SIPSorcery user
// agent on 127.0.0.1 answers the sink's call, as a phone would, and counts the
// RTP that arrives: the graph's Opus and H264 relayed as they are, Opus
// transcoded for a phone that only takes G.711, and the far end hanging up
// ending the sink. Nothing leaves the machine.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 16 Nov 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Cli.Commands.Route;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

/// <summary>
/// Runs on its own, after the parallel tests. The SIP stack logs through the library's global
/// LogFactory, which a test that invokes a command sets to the command's logger factory and leaves
/// disposed when the command ends, so these can't share a run with one.
/// </summary>
[CollectionDefinition(nameof(SipSinkNodeTests), DisableParallelization = true)]
public class SipLoopbackCollection { }

[Trait("Category", "integration")]
[Collection(nameof(SipSinkNodeTests))]
public class SipSinkNodeTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public SipSinkNodeTests() => SIPSorcery.LogFactory.Set(NullLoggerFactory.Instance);

    // The library's H264 format (packetization-mode=1), as the route's edges offer it.
    private static readonly VideoFormat H264 =
        SIPSorceryMedia.FFmpeg.Helper.GetSupportedVideoFormats().First(f => f.Codec == VideoCodecsEnum.H264);

    private static readonly List<AudioFormat> OpusThenG711 = new()
    {
        AudioCommonlyUsedFormats.OpusWebRTC,
        new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
        new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)
    };

    [Fact]
    public async Task Relays_opus_and_h264_to_a_phone_that_takes_them()
    {
        await using var phone = await LoopbackPhone.StartAsync(
            new List<AudioFormat> { AudioCommonlyUsedFormats.OpusWebRTC, new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU) },
            H264);
        var sink = new SipSinkNode(phone.Uri, OpusThenG711, H264, videoRequired: false, null, null, 5, NullLogger.Instance);
        try
        {
            await sink.StartAsync(CancellationToken.None).WaitAsync(Limit);

            Assert.Contains("(opus, h264)", sink.Describe());
            await SendUntilAsync(sink, () => phone.AudioPackets >= 5 && phone.VideoPackets >= 5);
            // Both ends agree on Opus, the offer's first codec, and that's what arrives (not a G.711 payload type).
            Assert.Equal(AudioCodecsEnum.OPUS, phone.NegotiatedAudio);
            Assert.Equal(111, phone.LastAudioPayloadType);

            // One-way: the offer is send-only, so the phone answers receive-only and sends nothing back.
            var rtp = phone.Answered.Task.Result;
            Assert.True(rtp.RemoteDescription!.Media.All(m => m.MediaStreamStatus == MediaStreamStatusEnum.SendOnly),
                "the sink's offer is send-only");
            Assert.Equal(MediaStreamStatusEnum.RecvOnly, rtp.AudioStream.LocalTrack.StreamStatus);
            Assert.Equal(MediaStreamStatusEnum.RecvOnly, rtp.VideoStream.LocalTrack.StreamStatus);
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    [Fact]
    public async Task Transcodes_opus_for_a_phone_that_only_takes_g711()
    {
        await using var phone = await LoopbackPhone.StartAsync(
            new List<AudioFormat> { new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, video: null);
        var sink = new SipSinkNode(phone.Uri, OpusThenG711, H264, videoRequired: false, null, null, 5, NullLogger.Instance);
        try
        {
            await sink.StartAsync(CancellationToken.None).WaitAsync(Limit);

            await SendUntilAsync(sink, () => phone.AudioPackets >= 5);
            Assert.Equal(0, phone.LastAudioPayloadType);         // PCMU
            Assert.Equal(160, phone.LastAudioPayloadLength);   // 20 ms of 8 kHz G.711 from each 20 ms Opus frame
            Assert.Contains("(pcmu from opus, no video)", sink.Describe());
            Assert.Equal(0, phone.VideoPackets);
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_pinned_video_codec_the_phone_turns_down_fails_the_call()
    {
        await using var phone = await LoopbackPhone.StartAsync(
            new List<AudioFormat> { AudioCommonlyUsedFormats.OpusWebRTC }, video: null);
        var sink = new SipSinkNode(phone.Uri, OpusThenG711, H264, videoRequired: true, null, null, 5, NullLogger.Instance);
        try
        {
            var ex = await Assert.ThrowsAsync<EdgeException>(() => sink.StartAsync(CancellationToken.None).WaitAsync(Limit));
            Assert.Contains("without h264 video", ex.Message);
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_far_end_hanging_up_ends_the_sink()
    {
        await using var phone = await LoopbackPhone.StartAsync(
            new List<AudioFormat> { AudioCommonlyUsedFormats.OpusWebRTC }, H264);
        var sink = new SipSinkNode(phone.Uri, OpusThenG711, H264, videoRequired: false, null, null, 5, NullLogger.Instance);
        try
        {
            await sink.StartAsync(CancellationToken.None).WaitAsync(Limit);
            Assert.False(sink.Completion.IsCompleted);

            phone.HangUp();

            await sink.Completion.WaitAsync(Limit);
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_call_turned_down_fails_with_the_status()
    {
        await using var phone = await LoopbackPhone.StartAsync(
            new List<AudioFormat> { AudioCommonlyUsedFormats.OpusWebRTC }, H264, decline: true);
        var sink = new SipSinkNode(phone.Uri, OpusThenG711, H264, videoRequired: false, null, null, 5, NullLogger.Instance);
        try
        {
            var ex = await Assert.ThrowsAsync<EdgeException>(() => sink.StartAsync(CancellationToken.None).WaitAsync(Limit));
            Assert.Contains("486", ex.Message);
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    /// <summary>
    /// Writes 20 ms Opus frames and H264 frames to the sink, as a source would, until the phone has
    /// what the test wants or the limit passes.
    /// </summary>
    private static async Task SendUntilAsync(SipSinkNode sink, Func<bool> done)
    {
        using var opus = new AudioEncoder(includeOpus: true);
        var tone = Enumerable.Range(0, 960).Select(i => (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / 48000))).ToArray();
        byte[] opusFrame = opus.EncodeAudio(tone, AudioCommonlyUsedFormats.OpusWebRTC);

        // Not a picture, but a well-formed Annex B access unit (an IDR NAL), which is all the
        // packetiser needs.
        byte[] h264Frame = new byte[] { 0, 0, 0, 1, 0x65 }.Concat(Enumerable.Repeat((byte)0xAB, 2000)).ToArray();

        uint audioTs = 0, videoTs = 0;
        var deadline = DateTime.UtcNow + Limit;
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, "the phone didn't get the media in time");

            sink.Write(MediaFrame.ForAudio(opusFrame, audioTs += 960, 960, AudioCommonlyUsedFormats.OpusWebRTC));
            sink.Write(MediaFrame.ForVideo(h264Frame, videoTs += 3000, H264, 3000));
            await Task.Delay(20);
        }
    }

    /// <summary>A SIPSorcery user agent on 127.0.0.1 that answers one call as a phone would.</summary>
    private sealed class LoopbackPhone : IAsyncDisposable
    {
        private readonly SIPTransport _transport = new();
        private SIPUserAgent? _ua;
        private int _audioPackets;
        private int _videoPackets;

        public SIPURI Uri { get; private set; } = SIPURI.None;
        public TaskCompletionSource<RTPSession> Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AudioPackets => Volatile.Read(ref _audioPackets);
        public int VideoPackets => Volatile.Read(ref _videoPackets);
        public int LastAudioPayloadLength { get; private set; }
        public int LastAudioPayloadType { get; private set; } = -1;
        public AudioCodecsEnum? NegotiatedAudio { get; private set; }

        public static Task<LoopbackPhone> StartAsync(List<AudioFormat> audio, VideoFormat? video, bool decline = false)
        {
            var phone = new LoopbackPhone();
            var channel = new SIPUDPChannel(IPAddress.Loopback, 0);
            phone._transport.AddSIPChannel(channel);
            phone.Uri = SIPURI.ParseSIPURI($"sip:phone@127.0.0.1:{channel.ListeningEndPoint.Port}");

            phone._ua = new SIPUserAgent(phone._transport, null);
            phone._ua.OnIncomingCall += async (ua, invite) =>
            {
                var uas = ua.AcceptCall(invite);
                if (decline)
                {
                    uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null);
                    return;
                }

                var rtp = new RTPSession(false, false, false);
                rtp.AcceptRtpFromAny = true;
                rtp.addTrack(new MediaStreamTrack(audio));
                if (video is { } v)
                {
                    rtp.addTrack(new MediaStreamTrack(new List<VideoFormat> { v }));
                }
                rtp.OnAudioFormatsNegotiated += formats => phone.NegotiatedAudio = formats.First().Codec;
                rtp.OnRtpPacketReceived += (ep, media, packet) =>
                {
                    if (media == SDPMediaTypesEnum.audio)
                    {
                        phone.LastAudioPayloadLength = packet.GetPayloadBytes().Length;
                        phone.LastAudioPayloadType = packet.Header.PayloadType;
                        Interlocked.Increment(ref phone._audioPackets);
                    }
                    else if (media == SDPMediaTypesEnum.video)
                    {
                        Interlocked.Increment(ref phone._videoPackets);
                    }
                };

                if (await ua.Answer(uas, rtp))
                {
                    phone.Answered.TrySetResult(rtp);
                }
            };

            return Task.FromResult(phone);
        }

        public void HangUp() => _ua?.Hangup();

        public ValueTask DisposeAsync()
        {
            try { if (_ua?.IsCallActive == true) _ua.Hangup(); } catch { /* best effort */ }
            _transport.Shutdown();
            return ValueTask.CompletedTask;
        }
    }
}
