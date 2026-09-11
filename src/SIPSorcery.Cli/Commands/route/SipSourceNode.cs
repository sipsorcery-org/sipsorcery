//-----------------------------------------------------------------------------
// Filename: SipSourceNode.cs
//
// Description: A SIP ingress edge for the "route" verb: places a SIP call and
// emits each received audio packet into the graph as an (encoded) audio
// MediaFrame, so a caller can be forwarded to another edge (e.g. a whip: sink).
//
// The call sends silence to the remote party - this edge consumes the inbound
// audio, it is not a softphone. It offers the codecs the caller asks for (OPUS
// first then a G.711 fallback by default), so a modern endpoint is carried in OPUS
// end to end and a G.711-only gateway falls back; the received payload is relayed
// onward in whatever was negotiated (repacketise, not transcode - a transcode
// transform bridges it to the graph codec when they differ). No audio devices are
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
using System.Collections.Generic;
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
    private readonly IReadOnlyList<AudioFormat> _offeredFormats;
    private readonly string? _username;
    private readonly string? _password;
    private readonly int _ringTimeoutSeconds;
    private readonly ILogger _logger;

    private readonly SIPTransport _sipTransport = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SIPUserAgent? _userAgent;
    private AudioFormat _negotiatedFormat = AudioFormat.Empty;
    private long? _connectTimeMs;

    // Received RTP timestamps are absolute; a transport sink wants a per-frame duration. The codec sets
    // the per-frame increment (160 @ 8 kHz for G.711, 960 @ 48 kHz for OPUS), so we derive it from the
    // gap between successive timestamps rather than assuming one byte == one sample (true only for G.711).
    private uint _lastTimestamp;
    private bool _haveTimestamp;

    public event Action<MediaFrame>? OnFrame;

    public Task Completion => _completion.Task;

    public long? ConnectTimeMs => _connectTimeMs;

    /// <summary>The audio format negotiated on the call, once answered. Used by the scope transform.</summary>
    public AudioFormat NegotiatedFormat => _negotiatedFormat;

    public SipSourceNode(SIPURI destination, IReadOnlyList<AudioFormat> offeredFormats, string? username, string? password, int ringTimeoutSeconds, ILogger logger)
    {
        _destination = destination;
        _offeredFormats = offeredFormats;
        _username = username;
        _password = password;
        _ringTimeoutSeconds = ringTimeoutSeconds;
        _logger = logger;
    }

    public string Describe() => $"sip:{_destination}";

    public async Task StartAsync(CancellationToken ct)
    {
        // Offer exactly the requested codecs, in order, so the call uses the best the far end supports
        // (e.g. OPUS first, then a G.711 fallback). The AudioEncoder(params) overload sets the SDP offer
        // order, so OPUS is preferred when present. The source sends silence - this is an ingress that
        // consumes the inbound audio - and the received payload is relayed onward in whatever codec was
        // negotiated (repacketise, not transcode; a transcode transform bridges it to the graph codec).
        var audioSource = new AudioExtrasSource(new AudioEncoder(_offeredFormats.ToArray()),
            new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence });

        var mediaSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = audioSource });
        mediaSession.AcceptRtpFromAny = true;

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

            // Per-frame duration = the gap to the previous packet's timestamp (the first frame has no
            // predecessor, so 0). Works for any codec: G.711 advances 160/frame, OPUS 960/frame.
            uint timestamp = rtpPacket.Header.Timestamp;
            uint duration = _haveTimestamp ? timestamp - _lastTimestamp : 0;
            _lastTimestamp = timestamp;
            _haveTimestamp = true;

            OnFrame?.Invoke(MediaFrame.ForAudio(payload, timestamp, duration, _negotiatedFormat));
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
