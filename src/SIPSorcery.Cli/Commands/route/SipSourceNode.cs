//-----------------------------------------------------------------------------
// Filename: SipSourceNode.cs
//
// Description: A SIP ingress edge for the "route" verb: places a SIP call and
// emits each received audio packet into the graph as an (encoded) audio
// MediaFrame, so a caller can be forwarded to another edge (e.g. a whip: sink).
//
// The call sends silence to the remote party - this edge consumes the inbound
// audio, it is not a softphone. The audio is pinned to PCMU so the received
// payload can be relayed onto an outgoing WebRTC track unchanged (repacketise,
// not transcode) and decoded cheaply for the audio scope. No audio devices are
// used, so the edge behaves identically on every OS.
//
// Mirrors the call/receive plumbing of the diagnostics "sip call" verb
// (SipCallCommand); the difference is that the received audio is fanned into the
// stream graph rather than to a local sink.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class SipSourceNode : ISourceNode
{
    private readonly SIPURI _destination;
    private readonly AudioFormat _audioFormat;
    private readonly string? _username;
    private readonly string? _password;
    private readonly int _ringTimeoutSeconds;
    private readonly ILogger _logger;

    private readonly SIPTransport _sipTransport = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SIPUserAgent? _userAgent;
    private AudioFormat _negotiatedFormat = AudioFormat.Empty;
    private long? _connectTimeMs;

    public event Action<MediaFrame>? OnFrame;

    public Task Completion => _completion.Task;

    public long? ConnectTimeMs => _connectTimeMs;

    /// <summary>The audio format negotiated on the call, once answered. Used by the scope transform.</summary>
    public AudioFormat NegotiatedFormat => _negotiatedFormat;

    public SipSourceNode(SIPURI destination, AudioFormat audioFormat, string? username, string? password, int ringTimeoutSeconds, ILogger logger)
    {
        _destination = destination;
        _audioFormat = audioFormat;
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

        // Pin the audio to the chosen G.711 codec (PCMU/PCMA): the received payload is relayed onto the
        // outgoing WebRTC track unchanged (repacketise, not transcode) and decoded cheaply for the scope.
        mediaSession.AudioExtrasSource.RestrictFormats(f => f.Codec == _audioFormat.Codec);
        mediaSession.AudioExtrasSource.SetSource(new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence });

        mediaSession.OnAudioFormatsNegotiated += (formats) => _negotiatedFormat = formats.First();

        mediaSession.OnRtpPacketReceived += (remoteEndPoint, mediaType, rtpPacket) =>
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

            // For PCMU/PCMA the 8 kHz clock advances one unit per sample, and one byte carries one
            // sample, so the payload length is the frame's duration in RTP units.
            OnFrame?.Invoke(MediaFrame.ForAudio(payload, rtpPacket.Header.Timestamp, (uint)payload.Length, _negotiatedFormat));
        };

        _userAgent = new SIPUserAgent(_sipTransport, null);
        SIPResponse? failureResponse = null;

        _userAgent.ClientCallTrying += (uac, resp) => _logger.LogDebug("sip source trying: {Status} {Reason}.", resp.StatusCode, resp.ReasonPhrase);
        _userAgent.ClientCallRinging += (uac, resp) => _logger.LogDebug("sip source ringing: {Status} {Reason}.", resp.StatusCode, resp.ReasonPhrase);
        _userAgent.ClientCallFailed += (uac, error, resp) =>
        {
            failureResponse = resp;
            _logger.LogDebug("sip source call failed: {Error}.", error);
        };
        _userAgent.OnCallHungup += (dialog) =>
        {
            _logger.LogDebug("sip source remote party hung up.");
            _completion.TrySetResult();
        };

        _logger.LogDebug("sip source calling {Destination} ...", _destination);

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
        _logger.LogDebug("sip source answered in {Ms}ms ({Codec}).", _connectTimeMs,
            _negotiatedFormat.IsEmpty() ? "format pending" : _negotiatedFormat.Codec.ToString());
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_userAgent?.IsCallActive == true)
            {
                _userAgent.Hangup();
            }
        }
        catch (Exception excp)
        {
            _logger.LogDebug("sip source hangup error: {Error}", excp.Message);
        }

        try { _sipTransport.Shutdown(); } catch { /* best effort */ }

        return ValueTask.CompletedTask;
    }
}
