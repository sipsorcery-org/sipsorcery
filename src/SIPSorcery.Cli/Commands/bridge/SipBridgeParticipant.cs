//-----------------------------------------------------------------------------
// Filename: SipBridgeParticipant.cs
//
// Description: The "sip" bridge endpoint: a SIP call as a full-duplex participant,
// so someone on a phone can talk to the other side of the bridge (a browser, or a
// voice agent: "bridge sip:alice@host agent" lets a caller speak to Max Headroom).
//
// The SIP leg is G.711 (8 kHz PCMU/PCMA); every other bridge endpoint works in
// 48 kHz WebRTC Opus. So unlike the web<->openai passthrough this peer TRANSCODES at
// its boundary, presenting plain 48 kHz Opus to the rest of the bridge so it drops
// in against web, agent or openai unchanged:
//   inbound  : G.711 -> PCM 8k -> resample 48k -> Opus  (OnFrame)
//   outbound : Opus  -> PCM 48k -> resample 8k  -> G.711 (SendAudio on the call)
// Audio is cheap to transcode in managed code (8 kHz mono), the same reasoning the
// route AudioTranscodeTransform relies on; only video gets delegated to ffmpeg, and
// a phone has no video, so video frames from the far side are simply dropped.
//
// Mirrors the call/receive plumbing of the route sip: source (SipSourceNode); the
// difference is this also SENDS the far side's audio back into the call (the extras
// source is set to None so nothing competes with our own SendAudio).
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Commands.Route;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class SipBridgeParticipant : IBridgeParticipant, IConnectable
{
    private const int OPUS_RTP_CLOCK_RATE = 48000;          // OPUS RTP timestamp clock (RFC 7587).
    private const int FRAME_MS = 20;                        // emit one 20 ms OPUS frame at a time inbound.
    private const int INBOUND_FRAME_SAMPLES = OPUS_RTP_CLOCK_RATE / 1000 * FRAME_MS;     // 960 @ 48 kHz.
    private const uint INBOUND_FRAME_RTP_UNITS = OPUS_RTP_CLOCK_RATE / 1000 * FRAME_MS;  // 960.

    private readonly SIPURI _destination;
    private readonly string? _username;
    private readonly string? _password;
    private readonly int _ringTimeoutSeconds;
    private readonly ILogger _logger;

    private readonly SIPTransport _sipTransport = new();
    private readonly AudioEncoder _inbound = new();         // G.711 decode + OPUS encode (call -> bridge)
    private readonly AudioEncoder _outbound = new();        // OPUS decode + G.711 encode (bridge -> call)
    private readonly AudioFormat _opus48 = AudioCommonlyUsedFormats.OpusWebRTC;
    private readonly List<short> _pcmBuffer = new();        // inbound PCM @ 48 kHz awaiting a full OPUS frame
    private readonly object _inLock = new();

    private SIPUserAgent? _userAgent;
    private VoIPMediaSession? _mediaSession;
    private AudioFormat _negotiatedFormat = AudioFormat.Empty;   // the negotiated G.711 variant (PCMU/PCMA)
    private uint _inboundTimestamp;
    private int _inFrames;          // diagnostics: confirm the call audio has signal before transcoding.
    private int _inWindowPeak;
    private long? _connectTimeMs;

    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _framesToCall;
    private long _bytesToCall;

    public event Action<MediaFrame>? OnFrame;

    /// <summary>Raised once when the call is answered (the command uses it to cue the agent's greeting).</summary>
    public event Action? Connected;

    public long? ConnectTimeMs => _connectTimeMs;

    public Task Completion => _completion.Task;

    public SipBridgeParticipant(SIPURI destination, string? username, string? password, int ringTimeoutSeconds, ILogger logger)
    {
        _destination = destination;
        _username = username;
        _password = password;
        _ringTimeoutSeconds = ringTimeoutSeconds;
        _logger = logger;
    }

    public string Describe() => $"sip:{_destination}";

    public async Task StartAsync(CancellationToken ct)
    {
        var mediaSession = new VoIPMediaSession();
        mediaSession.AcceptRtpFromAny = true;
        _mediaSession = mediaSession;

        // Carry the call as G.711 (PCMU/PCMA). The extras source is set to None: we send the far side's
        // transcoded audio ourselves with SendAudio, so nothing else must drive the outgoing track.
        mediaSession.AudioExtrasSource.RestrictFormats(f => f.Codec == AudioCodecsEnum.PCMU || f.Codec == AudioCodecsEnum.PCMA);
        mediaSession.AudioExtrasSource.SetSource(new AudioSourceOptions { AudioSource = AudioSourcesEnum.None });

        mediaSession.OnAudioFormatsNegotiated += (formats) => _negotiatedFormat = formats.First();
        mediaSession.OnRtpPacketReceived += OnCallAudio;

        _userAgent = new SIPUserAgent(_sipTransport, null);
        SIPResponse? failureResponse = null;

        _userAgent.ClientCallTrying += (uac, resp) => _logger.LogDebug("sip bridge trying: {Status} {Reason}.", resp.StatusCode, resp.ReasonPhrase);
        _userAgent.ClientCallRinging += (uac, resp) => _logger.LogDebug("sip bridge ringing: {Status} {Reason}.", resp.StatusCode, resp.ReasonPhrase);
        _userAgent.ClientCallFailed += (uac, error, resp) =>
        {
            failureResponse = resp;
            _logger.LogDebug("sip bridge call failed: {Error}.", error);
        };
        _userAgent.OnCallHungup += (dialog) =>
        {
            _logger.LogDebug("sip bridge remote party hung up.");
            _completion.TrySetResult();
        };

        _logger.LogDebug("sip bridge calling {Destination} ...", _destination);

        var stopwatch = Stopwatch.StartNew();
        bool answered;
        try
        {
            answered = await _userAgent.Call(_destination.ToString(), _username, _password, mediaSession, _ringTimeoutSeconds)
                .ConfigureAwait(false);
        }
        catch (Exception excp)
        {
            throw new EdgeException($"The sip call to {_destination} failed: {excp.Message}");
        }

        if (!answered)
        {
            throw new EdgeException(failureResponse != null
                ? $"The sip call to {_destination} was not answered: {(int)failureResponse.Status} {failureResponse.ReasonPhrase}."
                : $"The sip call to {_destination} was not answered within {_ringTimeoutSeconds}s.");
        }

        _connectTimeMs = stopwatch.ElapsedMilliseconds;
        _logger.LogDebug("sip bridge answered in {Ms}ms ({Codec}).", _connectTimeMs,
            _negotiatedFormat.IsEmpty() ? "format pending" : _negotiatedFormat.Codec.ToString());

        Connected?.Invoke();
    }

    /// <summary>Inbound call audio (G.711): transcode up to 48 kHz OPUS and emit to the other side.</summary>
    private void OnCallAudio(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
    {
        if (mediaType != SDPMediaTypesEnum.audio || _negotiatedFormat.IsEmpty())
        {
            return;
        }

        byte[] payload = rtpPacket.GetPayloadBytes();
        if (payload.Length == 0)
        {
            return;
        }

        try
        {
            short[] pcm8 = _inbound.DecodeAudio(payload, _negotiatedFormat);
            if (pcm8.Length == 0)
            {
                return;
            }

            // Diagnostics (--verbose): is the phone actually sending audio? This is the raw G.711 peak,
            // before any transcode, so a 0% here means the call audio itself is silent (codec/leg issue),
            // not our Opus path. {Bytes}/{Codec}/{Rate} expose the negotiated leg.
            _inFrames++;
            for (int i = 0; i < pcm8.Length; i++)
            {
                int a = Math.Abs((int)pcm8[i]);
                if (a > _inWindowPeak) { _inWindowPeak = a; }
            }
            if (_inFrames % 100 == 0)
            {
                _logger.LogDebug("sip->bridge: {Frames} frames, raw call-audio peak {Pct}% ({Bytes}B {Codec} @ {Rate}Hz).",
                    _inFrames, _inWindowPeak * 100 / 32767, payload.Length, _negotiatedFormat.Codec, _negotiatedFormat.ClockRate);
                _inWindowPeak = 0;
            }

            short[] pcm48 = PcmResampler.Resample(pcm8, _negotiatedFormat.ClockRate, OPUS_RTP_CLOCK_RATE);

            lock (_inLock)
            {
                _pcmBuffer.AddRange(pcm48);

                // Re-frame to fixed 20 ms OPUS frames so any inbound ptime yields valid OPUS.
                while (_pcmBuffer.Count >= INBOUND_FRAME_SAMPLES)
                {
                    var slice = _pcmBuffer.GetRange(0, INBOUND_FRAME_SAMPLES).ToArray();
                    _pcmBuffer.RemoveRange(0, INBOUND_FRAME_SAMPLES);

                    byte[] opus = _inbound.EncodeAudio(slice, _opus48);
                    if (opus.Length == 0)
                    {
                        continue;
                    }

                    _inboundTimestamp += INBOUND_FRAME_RTP_UNITS;
                    OnFrame?.Invoke(MediaFrame.ForAudio(opus, _inboundTimestamp, INBOUND_FRAME_RTP_UNITS, AudioCommonlyUsedFormats.OpusWebRTC));
                }
            }
        }
        catch (Exception excp)
        {
            _logger.LogWarning("sip bridge inbound transcode failed: {Error}", excp.Message);
        }
    }

    /// <summary>The far side's audio (48 kHz OPUS): transcode down to G.711 and send it into the call.
    /// Video frames (e.g. an agent avatar) are dropped - a phone has no video.</summary>
    public void Write(MediaFrame frame)
    {
        if (frame.Kind != MediaKind.Audio || frame.Payload.Length == 0 || _negotiatedFormat.IsEmpty())
        {
            return;
        }

        try
        {
            short[] pcm48 = _outbound.DecodeAudio(frame.Payload, AudioCommonlyUsedFormats.OpusWebRTC);
            if (pcm48.Length == 0)
            {
                return;
            }
            short[] pcm8 = PcmResampler.Resample(pcm48, OPUS_RTP_CLOCK_RATE, _negotiatedFormat.ClockRate);
            byte[] g711 = _outbound.EncodeAudio(pcm8, _negotiatedFormat);
            if (g711.Length == 0)
            {
                return;
            }

            // For G.711 the 8 kHz clock advances one unit per sample and one byte carries one sample, so
            // the payload length is the frame's duration in RTP units.
            _mediaSession?.SendAudio((uint)g711.Length, g711);
            Interlocked.Increment(ref _framesToCall);
            Interlocked.Add(ref _bytesToCall, g711.Length);
        }
        catch (Exception excp)
        {
            _logger.LogWarning("sip bridge outbound transcode failed: {Error}", excp.Message);
        }
    }

    public SinkStats GetStats() => new(_framesToCall, Interlocked.Read(ref _bytesToCall), 0);

    public ValueTask DisposeAsync()
    {
        _completion.TrySetResult();

        try
        {
            if (_userAgent?.IsCallActive == true)
            {
                _userAgent.Hangup();
            }
        }
        catch (Exception excp)
        {
            _logger.LogDebug("sip bridge hangup error: {Error}", excp.Message);
        }

        try { _sipTransport.Shutdown(); } catch { /* best effort */ }
        _inbound.Dispose();
        _outbound.Dispose();

        return ValueTask.CompletedTask;
    }
}
