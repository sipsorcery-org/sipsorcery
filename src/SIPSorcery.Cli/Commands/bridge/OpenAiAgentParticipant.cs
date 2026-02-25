//-----------------------------------------------------------------------------
// Filename: OpenAiAgentParticipant.cs
//
// Description: The "openai" bridge endpoint: a voice agent backed by the OpenAI
// Realtime WebRTC API. Inbound audio (the other participant's OPUS) is sent
// straight to OpenAI, and the model's spoken reply (also OPUS) is sent straight
// back - both directions are 48kHz OPUS, so this is a repacketise, not a
// transcode. OpenAI does the speech-to-text, the thinking and the text-to-speech;
// the CLI is just the media path and (optionally) a face.
//
// The hard part is the optional --avatar. Unlike the Azure agent, the OpenAI voice
// comes with NO viseme timeline, so the lip-sync has to be AUDIO-DRIVEN: each
// received voice frame is decoded to PCM, its envelope (a smoothed RMS) is measured
// and fed to the avatar's mouth openness. It reads as generic talking rather than
// true phoneme-accurate lip-sync, but tracks the speech closely.
//
// A duplex participant: Write consumes inbound audio (the ears -> OpenAI), OnFrame
// produces the model's voice (the mouth) plus, with an avatar, the rendered face.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Commands.Route;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.OpenAI.Realtime;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class OpenAiAgentParticipant : IBridgeParticipant, IGreetable, ITranscriptSource
{
    public const string DEFAULT_GREETING = "Say a brief, friendly hello and ask how you can help.";

    private const int OPUS_RTP_CLOCK_RATE = 48000;          // OPUS RTP timestamp clock (RFC 7587).
    private const uint DEFAULT_FRAME_RTP_UNITS = 960;       // a 20 ms OPUS frame at 48 kHz, when none is given.

    // Audio-driven mouth envelope. The model voice RMS is low (a fraction of full scale) so it is
    // scaled up, then an attack/decay follower smooths it: open fast on a syllable, close gently after.
    private const float ENVELOPE_GAIN = 6.0f;
    private const float ENVELOPE_ATTACK = 0.6f;
    private const float ENVELOPE_DECAY = 0.15f;
    private const float SPEAKING_LEVEL = 0.06f;             // above this the avatar counts as "speaking".

    private readonly WebRTCEndPoint _endpoint;
    private readonly RealtimeVoicesEnum _voice;
    private readonly string? _instructions;
    private readonly string _greeting;
    private readonly ILogger _logger;

    private readonly MaxHeadroomVideoSource? _avatar;       // null unless --avatar: the lip-synced face
    private readonly VideoFormat _h264 = RouteVideoFormats.H264;
    private readonly AudioEncoder _modelDecoder = new();    // decode the model OPUS -> PCM for the envelope
    private uint _videoTimestamp;
    private float _envelope;                                // smoothed mouth level, audio thread only

    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _stopwatch = new();
    private long? _connectTimeMs;
    private volatile bool _connected;
    private bool _greetRequested;
    private bool _greeted;
    private readonly object _greetLock = new();

    private int _voiceFrames;
    private long _voiceBytes;

    public event Action<MediaFrame>? OnFrame;

    public event Action<string, string>? OnTranscript;

    public long? ConnectTimeMs => _connectTimeMs;

    public Task Completion => _completion.Task;

    public OpenAiAgentParticipant(
        string apiKey, RealtimeVoicesEnum voice, string? instructions, string? greeting, bool avatar,
        ILoggerFactory loggerFactory, ILogger logger)
    {
        _logger = logger;
        _voice = voice;
        _instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions;
        _greeting = string.IsNullOrWhiteSpace(greeting) ? DEFAULT_GREETING : greeting!;

        _endpoint = new WebRTCEndPoint(apiKey, loggerFactory);

        if (avatar)
        {
            // The Max Headroom face, lip-synced from the audio envelope (no visemes from OpenAI).
            _avatar = new MaxHeadroomVideoSource(new FFmpegVideoEncoder()) { AudioDrivenMouth = true };
            _avatar.RestrictFormats(vf => vf.Codec == VideoCodecsEnum.H264);
            _avatar.OnVideoSourceEncodedSample += (durationRtpUnits, sample) =>
            {
                _videoTimestamp += durationRtpUnits;
                OnFrame?.Invoke(MediaFrame.ForVideo(sample.ToArray(), _videoTimestamp, _h264, durationRtpUnits));
            };
        }
    }

    public string Describe() => $"openai ({_voice}{(_avatar != null ? " + avatar" : "")})";

    public async Task StartAsync(CancellationToken ct)
    {
        _endpoint.OnAudioFrameReceived += OnModelAudio;
        _endpoint.OnDataChannelMessage += OnDataChannelMessage;
        _endpoint.OnPeerConnectionConnected += OnConnected;
        _endpoint.OnPeerConnectionFailed += () => _completion.TrySetResult();
        _endpoint.OnPeerConnectionClosed += () => _completion.TrySetResult();

        if (_avatar != null)
        {
            // The renderer runs continuously (idle face when silent), so video flows the whole session.
            _avatar.SetVideoSourceFormat(_h264);
            await _avatar.StartVideo().ConfigureAwait(false);
        }

        _stopwatch.Restart();
        var connect = await _endpoint.StartConnect().ConfigureAwait(false);
        if (connect.IsLeft)
        {
            throw new EdgeException($"Could not connect to the OpenAI Realtime API: {connect.LeftAsEnumerable().First().Message}");
        }

        _logger.LogDebug("OpenAI voice agent connecting{Avatar}.", _avatar != null ? " with avatar" : "");
    }

    private void OnConnected()
    {
        _connectTimeMs = _stopwatch.ElapsedMilliseconds;
        _connected = true;
        _logger.LogDebug("OpenAI Realtime connected in {Ms}ms.", _connectTimeMs);

        // Set the voice and (optional) persona for the session. Server-side VAD is on by default, so the
        // model then replies on its own to each utterance the user speaks - no further prompting needed.
        // Whisper transcription of the input is requested so the user's turns appear in the transcript.
        var update = _endpoint.DataChannelMessenger.SendSessionUpdate(_voice, _instructions, transcriptionModel: TranscriptionModelEnum.Whisper1);
        if (update.IsLeft)
        {
            _logger.LogWarning("OpenAI session update failed: {Error}", update.LeftAsEnumerable().First().Message);
        }

        TrySendGreeting();
    }

    /// <summary>Inbound audio from the other participant: relay it straight to OpenAI (OPUS passthrough).</summary>
    public void Write(MediaFrame frame)
    {
        if (frame.Kind != MediaKind.Audio || frame.Payload.Length == 0)
        {
            return;
        }

        uint duration = frame.DurationRtpUnits != 0 ? frame.DurationRtpUnits : DEFAULT_FRAME_RTP_UNITS;
        _endpoint.SendAudio(duration, frame.Payload);
    }

    /// <summary>Surfaces the conversation transcript from OpenAI's data-channel events: the user's speech
    /// (input transcription) and the model's spoken reply. Relayed to the web peer's browser console.</summary>
    private void OnDataChannelMessage(RTCDataChannel dc, RealtimeEventBase message)
    {
        switch (message)
        {
            case RealtimeServerEventConversationItemInputAudioTranscriptionCompleted you
                when !string.IsNullOrWhiteSpace(you.Transcript):
                OnTranscript?.Invoke("you", you.Transcript!.Trim());
                break;
            case RealtimeServerEventResponseAudioTranscriptDone ai
                when !string.IsNullOrWhiteSpace(ai.Transcript):
                OnTranscript?.Invoke("ai", ai.Transcript!.Trim());
                break;
        }
    }

    /// <summary>The model's voice: forward it to the other participant, and (with an avatar) drive the
    /// mouth from its envelope.</summary>
    private void OnModelAudio(EncodedAudioFrame frame)
    {
        if (frame.EncodedAudio.Length == 0)
        {
            return;
        }

        OnFrame?.Invoke(MediaFrame.ForAudio(frame.EncodedAudio.ToArray(), 0, ToRtpUnits(frame), AudioCommonlyUsedFormats.OpusWebRTC));
        Interlocked.Increment(ref _voiceFrames);
        Interlocked.Add(ref _voiceBytes, frame.EncodedAudio.Length);

        if (_avatar != null)
        {
            DriveMouth(frame);
        }
    }

    /// <summary>Measures the frame's speech envelope and feeds it to the avatar's mouth openness.</summary>
    private void DriveMouth(EncodedAudioFrame frame)
    {
        try
        {
            short[] pcm = _modelDecoder.DecodeAudio(frame.EncodedAudio, AudioCommonlyUsedFormats.OpusWebRTC);
            if (pcm.Length == 0)
            {
                return;
            }

            double sumSquares = 0;
            for (int i = 0; i < pcm.Length; i++)
            {
                sumSquares += (double)pcm[i] * pcm[i];
            }
            float rms = (float)Math.Sqrt(sumSquares / pcm.Length) / 32768f;
            float target = Math.Min(1f, rms * ENVELOPE_GAIN);

            // Attack fast on louder audio, decay slower so the mouth doesn't chatter between samples.
            float coeff = target > _envelope ? ENVELOPE_ATTACK : ENVELOPE_DECAY;
            _envelope += (target - _envelope) * coeff;

            _avatar!.AudioLevel = _envelope;
            _avatar.IsSpeaking = _envelope > SPEAKING_LEVEL;
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Avatar envelope decode failed: {Error}", excp.Message);
        }
    }

    /// <summary>Cues the model to speak first (the web peer has connected). Safe before the OpenAI
    /// connection is up: the greeting is sent once the data channel opens.</summary>
    public void Greet()
    {
        lock (_greetLock)
        {
            _greetRequested = true;
        }
        TrySendGreeting();
    }

    private void TrySendGreeting()
    {
        lock (_greetLock)
        {
            if (!_connected || !_greetRequested || _greeted)
            {
                return;
            }
            _greeted = true;
        }

        var result = _endpoint.DataChannelMessenger.SendResponseCreate(_voice, _greeting);
        if (result.IsLeft)
        {
            _logger.LogWarning("OpenAI greeting failed: {Error}", result.LeftAsEnumerable().First().Message);
        }
    }

    /// <summary>Converts a frame's millisecond duration to its 48 kHz OPUS RTP clock units.</summary>
    private static uint ToRtpUnits(EncodedAudioFrame frame)
    {
        uint units = (uint)((long)frame.DurationMilliSeconds * frame.AudioFormat.RtpClockRate / 1000);
        return units != 0 ? units : DEFAULT_FRAME_RTP_UNITS;
    }

    public SinkStats GetStats() => new(_voiceFrames, Interlocked.Read(ref _voiceBytes), 0);

    public async ValueTask DisposeAsync()
    {
        _completion.TrySetResult();
        if (_avatar != null)
        {
            try { await _avatar.CloseVideo().ConfigureAwait(false); } catch { /* best effort */ }
            _avatar.Dispose();
        }
        try { _endpoint.Close(); } catch { /* best effort */ }
        _modelDecoder.Dispose();
    }
}
