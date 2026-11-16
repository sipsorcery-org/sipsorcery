//-----------------------------------------------------------------------------
// Filename: SipSinkNode.cs
//
// Description: A SIP egress edge for the "route" verb: places a SIP call and sends
// the graph's media down it, so a stream can be watched on a SIP phone, e.g. the
// Softphone v2 example started with --auto-answer:
//
//   route --from testpattern --to sip:view@127.0.0.1:5080
//
// It's the mirror of the sip: source (SipSourceNode places a call and takes the
// far end's audio into the graph). Here the call's audio and video are send-only,
// offered in the graph's codecs so the frames relay unchanged (repacketise, not
// transcode), with two allowances for what a phone will take:
//
//   - Audio. With an Opus graph the offer is Opus first, then G.711, as the sip:
//     source's is, so a G.711-only phone still gets the call. When it picks G.711
//     the sink decodes the Opus and re-encodes it (audio is cheap to transcode in
//     managed code, as the bridge's sip: peer does).
//   - Video. A phone with no video (it turns the video m-line down) gets an audio-
//     only call. But when --video-codec pinned a codec, not getting it fails the
//     run, as it does on every other edge.
//
// A call's far end can hang up, which a file or a player can't, so this sink is an
// IEndableSink: once every sink of the run has ended, the route stops.
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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Common;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class SipSinkNode : ISinkNode, IEndableSink
{
    private readonly SIPURI _destination;
    private readonly IReadOnlyList<AudioFormat> _offeredAudio;
    private readonly VideoFormat _offeredVideo;
    private readonly bool _videoRequired;
    private readonly string? _username;
    private readonly string? _password;
    private readonly int _ringTimeoutSeconds;
    private readonly ILogger _logger;

    private readonly SIPTransport _sipTransport = new();
    private readonly AudioEncoder _transcoder = new(includeOpus: true);
    private readonly object _transcodeLock = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SIPUserAgent? _userAgent;
    private RTPSession? _rtpSession;
    private AudioFormat _audioFormat = AudioFormat.Empty;     // the audio codec the call agreed
    private VideoFormat _videoFormat = VideoFormat.Empty;     // the video codec the call agreed, if any
    private volatile bool _hasVideo;
    private volatile bool _connected;
    private long? _connectTimeMs;

    private int _framesSent;
    private long _bytesSent;
    private int _dropped;
    private int _transcoded;
    private int _warnedVideoCodec;
    private int _warnedAudioCodec;

    /// <summary>Completes when the far end hangs up.</summary>
    public Task Completion => _completion.Task;

    /// <summary>How long the call took to be answered, once it has been.</summary>
    public long? ConnectTimeMs => _connectTimeMs;

    /// <param name="offeredAudio">The audio codecs to offer, in order of preference: the graph's codec first.</param>
    /// <param name="offeredVideo">The single video codec to offer: the graph's.</param>
    /// <param name="videoRequired">True when --video-codec pinned the codec, so a call without it fails the run.</param>
    public SipSinkNode(SIPURI destination, IReadOnlyList<AudioFormat> offeredAudio, VideoFormat offeredVideo, bool videoRequired,
        string? username, string? password, int ringTimeoutSeconds, ILogger logger)
    {
        _destination = destination;
        _offeredAudio = offeredAudio;
        _offeredVideo = offeredVideo;
        _videoRequired = videoRequired;
        _username = username;
        _password = password;
        _ringTimeoutSeconds = ringTimeoutSeconds;
        _logger = logger;
    }

    /// <summary>The codecs this sink offers, for tests and the log.</summary>
    public IReadOnlyList<AudioFormat> OfferedAudio => _offeredAudio;

    public VideoFormat OfferedVideo => _offeredVideo;

    /// <summary>
    /// The edge and, once the call has been answered, what it agreed: e.g. "sip:view@127.0.0.1:5080
    /// (pcmu from opus, h264)" for a G.711 phone fed an Opus graph.
    /// </summary>
    public string Describe()
    {
        // The URI carries its own sip:/sips: scheme.
        if (_audioFormat.IsEmpty())
        {
            return _destination.ToString();
        }

        string audio = _audioFormat.Codec.ToString().ToLowerInvariant();
        if (Volatile.Read(ref _transcoded) > 0)
        {
            audio += $" from {_offeredAudio[0].Codec.ToString().ToLowerInvariant()}";
        }
        string video = _hasVideo ? _videoFormat.Codec.ToString().ToLowerInvariant() : "no video";
        return $"{_destination} ({audio}, {video})";
    }

    public async Task StartAsync(CancellationToken ct)
    {
        SipCallSupport.TraceMessages(_sipTransport, _logger);
        SipCallSupport.AnswerKeepalives(_sipTransport, () => _userAgent, _logger, "sip sink");

        // A plain RTP session rather than a VoIPMediaSession: that one brings its own audio source,
        // and here every packet comes from the graph. Both tracks are send-only: this is one-way.
        var rtp = new RTPSession(false, false, false);
        rtp.AcceptRtpFromAny = true;
        rtp.addTrack(new MediaStreamTrack(_offeredAudio.ToList(), MediaStreamStatusEnum.SendOnly));
        rtp.addTrack(new MediaStreamTrack(new List<VideoFormat> { _offeredVideo }, MediaStreamStatusEnum.SendOnly));
        rtp.OnAudioFormatsNegotiated += formats => _audioFormat = formats.First();
        rtp.OnVideoFormatsNegotiated += formats => _videoFormat = formats.First();
        _rtpSession = rtp;

        _userAgent = new SIPUserAgent(_sipTransport, null);
        SIPResponse? failureResponse = null;

        _userAgent.ClientCallTrying += (uac, resp) => _logger.LogDebug("sip sink trying: {Status} {Reason}.", resp.StatusCode, resp.ReasonPhrase);
        _userAgent.ClientCallRinging += (uac, resp) => _logger.LogDebug("sip sink ringing: {Status} {Reason}.", resp.StatusCode, resp.ReasonPhrase);
        _userAgent.ClientCallFailed += (uac, error, resp) =>
        {
            failureResponse = resp;
            _logger.LogDebug("sip sink call failed: {Error}.", error);
        };
        _userAgent.OnCallHungup += (dialog) =>
        {
            _connected = false;
            _logger.LogInformation("sip sink: {Destination} hung up.", _destination);
            _completion.TrySetResult();
        };

        _logger.LogDebug("sip sink calling {Destination} ...", _destination);

        var stopwatch = Stopwatch.StartNew();
        bool answered;

        // Ctrl-c while it rings cancels the call, rather than leaving the phone ringing until it times out.
        using (ct.Register(() => { try { _userAgent.Cancel(); } catch { /* best effort */ } }))
        {
            try
            {
                answered = await _userAgent.Call(_destination.ToString(), _username, _password, rtp, _ringTimeoutSeconds)
                    .ConfigureAwait(false);
            }
            catch (Exception excp)
            {
                throw new EdgeException($"The sip call to {_destination} failed: {excp.Message}");
            }
        }

        ct.ThrowIfCancellationRequested();

        if (!answered)
        {
            throw new EdgeException(failureResponse != null
                ? $"The sip call to {_destination} was not answered: {(int)failureResponse.Status} {failureResponse.ReasonPhrase}."
                : $"The sip call to {_destination} was not answered within {_ringTimeoutSeconds}s.");
        }

        _connectTimeMs = stopwatch.ElapsedMilliseconds;

        // SIPSorcery makes a video stream for any m=video in the answer, so "the call has video" is
        // the far end's video line still having a port and a codec agreed for it.
        _hasVideo = !_videoFormat.IsEmpty() &&
            rtp.RemoteDescription?.Media.Any(m => m.Media == SDPMediaTypesEnum.video && m.Port != 0) == true;

        if (_videoRequired && !_hasVideo)
        {
            _userAgent.Hangup();
            throw new EdgeException(
                $"The sip call to {_destination} was answered without {_offeredVideo.Codec.ToString().ToLowerInvariant()} video, " +
                $"which {CodecSelection.VIDEO_OPTION} asked for.");
        }

        _connected = true;
        _logger.LogInformation("sip sink: {Destination} answered in {Ms}ms ({Audio}, {Video}).", _destination, _connectTimeMs,
            _audioFormat.IsEmpty() ? "no audio" : _audioFormat.Codec.ToString().ToLowerInvariant(),
            _hasVideo ? _videoFormat.Codec.ToString().ToLowerInvariant() : "no video");
    }

    public void Write(MediaFrame frame)
    {
        if (frame.Payload.Length == 0)
        {
            return;
        }

        var rtp = _rtpSession;
        if (!_connected || rtp == null)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        try
        {
            if (frame.Kind == MediaKind.Video)
            {
                WriteVideo(rtp, frame);
            }
            else
            {
                WriteAudio(rtp, frame);
            }
        }
        catch (Exception excp)
        {
            _logger.LogWarning("sip sink send failed: {Error}", excp.Message);
            Interlocked.Increment(ref _dropped);
        }
    }

    private void WriteVideo(RTPSession rtp, MediaFrame frame)
    {
        // An audio-only call: the phone never wanted the video, so it isn't counted as dropped.
        if (!_hasVideo)
        {
            return;
        }

        // Frames in another codec than the call's would only be garbage on the phone. It happens when a
        // receiving source (whep:, livekit:...) was free to take any codec: pinning one fixes it.
        if (frame.VideoFormat.Codec != _videoFormat.Codec)
        {
            if (Interlocked.Exchange(ref _warnedVideoCodec, 1) == 0)
            {
                _logger.LogWarning("sip sink: the source's video is {Source} but the call agreed {Call}, so video isn't sent. " +
                    "Pin both with {Option} {Call}.", frame.VideoFormat.Codec, _videoFormat.Codec, CodecSelection.VIDEO_OPTION,
                    _videoFormat.Codec.ToString().ToLowerInvariant());
            }
            Interlocked.Increment(ref _dropped);
            return;
        }

        rtp.SendVideo(frame.DurationRtpUnits, frame.Payload);
        Count(frame.Payload.Length);
    }

    private void WriteAudio(RTPSession rtp, MediaFrame frame)
    {
        var target = _audioFormat;
        if (target.IsEmpty())
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        if (frame.AudioFormat.Codec == target.Codec)
        {
            rtp.SendAudio(frame.DurationRtpUnits, frame.Payload);
            Count(frame.Payload.Length);
            return;
        }

        // The phone picked a G.711 fallback: decode the frame and re-encode it. G.711 is one byte per
        // sample, so any frame length makes valid packets. A non-G.711 target only comes up when the
        // source's audio isn't the graph codec we offered first, and isn't worth a re-framing encoder.
        if (target.Codec != AudioCodecsEnum.PCMU && target.Codec != AudioCodecsEnum.PCMA)
        {
            if (Interlocked.Exchange(ref _warnedAudioCodec, 1) == 0)
            {
                _logger.LogWarning("sip sink: the source's audio is {Source} but the call agreed {Call}, so audio isn't sent.",
                    frame.AudioFormat.Codec, target.Codec);
            }
            Interlocked.Increment(ref _dropped);
            return;
        }

        byte[] g711;
        lock (_transcodeLock)
        {
            short[] pcm = _transcoder.DecodeAudio(frame.Payload, frame.AudioFormat);
            if (pcm.Length == 0)
            {
                return;
            }
            // The library's PCM is mono at the codec's sample rate (48 kHz for Opus).
            int sourceRate = frame.AudioFormat.ClockRate;
            short[] resampled = sourceRate == target.ClockRate ? pcm : PcmResampler.Resample(pcm, sourceRate, target.ClockRate);
            g711 = _transcoder.EncodeAudio(resampled, target);
        }

        if (g711.Length == 0)
        {
            return;
        }

        // For G.711 the 8 kHz clock advances one unit per sample, and one byte carries one sample.
        rtp.SendAudio((uint)g711.Length, g711);
        Interlocked.Increment(ref _transcoded);
        Count(g711.Length);
    }

    private void Count(int bytes)
    {
        Interlocked.Increment(ref _framesSent);
        Interlocked.Add(ref _bytesSent, bytes);
    }

    public SinkStats GetStats() => new(Volatile.Read(ref _framesSent), Interlocked.Read(ref _bytesSent), Volatile.Read(ref _dropped));

    public ValueTask DisposeAsync()
    {
        _connected = false;
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
            _logger.LogDebug("sip sink hangup error: {Error}", excp.Message);
        }

        try { _sipTransport.Shutdown(); } catch { /* best effort */ }

        lock (_transcodeLock)
        {
            _transcoder.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
