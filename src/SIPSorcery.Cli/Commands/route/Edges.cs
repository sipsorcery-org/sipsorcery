//-----------------------------------------------------------------------------
// Filename: Edges.cs
//
// Description: The v0.1 edges the "route" verb can attach to a stream graph, plus
// the factory that turns a --from/--to spec into a node. An edge spec is either a
// bare keyword/path (a file, "play", "null", "-") or "scheme:rest" (e.g.
// "whep:https://host/whep"). The factory is the single place schemes are mapped to
// node implementations, so adding a transport edge (whip, sip, livekit) later is a
// new case here and nothing else.
//
// v0.1 sources: testpattern (a generated H264 pattern) and whep (a
// live WebRTC ingress). v0.1 sinks: a file, an ffplay window ("play"), discard
// ("null") or the bitstream on stdout ("-"). All sink frames stay ENCODED - the
// graph repacketises, it does not transcode.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
// 23 Jun 2026	Aaron Clauson	Wired the sip: source (with an optional ffmpeg audio-scope video)
//                              and the whip: sink so a call can be bridged to a WebRTC endpoint.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.Cli.Commands.Route;

/// <summary>
/// The knobs an edge needs from the verb: frame rate for generators / the scope video, auth and
/// timeout for transports, the scope options for a sip: source, and SIP credentials.
/// </summary>
public sealed record EdgeOptions(
    int Fps,
    string? Token,
    int TimeoutSeconds,
    bool Scope = false,
    string ScopeMode = "waves",
    string ScopeSize = "640x360",
    string? FfmpegPath = null,
    string? SipUsername = null,
    string? SipPassword = null,
    string AudioCodec = "pcmu");

/// <summary>Resolves the --audio-codec name to the audio format a source produces and a sink offers.</summary>
public static class RouteAudio
{
    public static bool TryResolveCodec(string? name, out AudioFormat format, out string? error)
    {
        error = null;
        switch ((name ?? "pcmu").Trim().ToLowerInvariant())
        {
            case "pcmu":
                format = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU);
                return true;
            case "pcma":
                format = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA);
                return true;
            case "opus":
                format = AudioCommonlyUsedFormats.OpusWebRTC;
                return true;
            default:
                format = AudioFormat.Empty;
                error = $"Unknown --audio-codec '{name}'. Use pcmu (default), pcma or opus.";
                return false;
        }
    }
}

/// <summary>
/// A generated test pattern source: the zero dependency ingress for demonstrating the graph. Emits an
/// H264 video test pattern (the library's VideoTestPatternSource + ffmpeg encoder) AND a music audio
/// track (AudioExtrasSource) in the requested codec (PCMU by default, or Opus / PCMA), each encoded
/// frame fanned into the graph as a <see cref="MediaFrame"/>. The audio relays unchanged onto a whip:
/// sink (repacketise, not transcode); a video-only sink (file/play/-) ignores the audio frames. Per
/// stream the RTP timestamp is accumulated from the per frame duration so a sink gets a monotonic clock.
/// </summary>
public sealed class TestPatternSourceNode : ISourceNode
{
    private readonly VideoTestPatternSource _source;
    private readonly AudioExtrasSource _audioSource;
    private readonly VideoFormat _format = RouteVideoFormats.H264;
    private readonly AudioFormat _audioFormat;
    private readonly int _fps;
    private readonly ILogger _logger;
    private uint _timestamp;
    private uint _audioTimestamp;

    public event Action<MediaFrame>? OnFrame;

    // A generator never ends on its own; the run is bounded by --duration / cancellation.
    public Task Completion { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    public long? ConnectTimeMs => null;

    public TestPatternSourceNode(int fps, AudioFormat audioFormat, ILogger logger)
    {
        _fps = fps;
        _audioFormat = audioFormat;
        _logger = logger;

        _source = new VideoTestPatternSource(new FFmpegVideoEncoder());
        _source.RestrictFormats(f => f.Codec == VideoCodecsEnum.H264);
        _source.SetFrameRate(fps);
        _source.OnVideoSourceEncodedSample += HandleEncoded;

        // The AudioEncoder only offers Opus when explicitly asked, so build it to match the format.
        _audioSource = new AudioExtrasSource(new AudioEncoder(includeOpus: audioFormat.Codec == AudioCodecsEnum.OPUS),
            new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
        _audioSource.RestrictFormats(f => f.Codec == audioFormat.Codec);
        _audioSource.OnAudioSourceEncodedSample += HandleAudioEncoded;
    }

    private void HandleEncoded(uint durationRtpUnits, byte[] sample)
    {
        _timestamp += durationRtpUnits;
        OnFrame?.Invoke(MediaFrame.ForVideo(sample, _timestamp, _format, durationRtpUnits));
    }

    private void HandleAudioEncoded(uint durationRtpUnits, byte[] sample)
    {
        _audioTimestamp += durationRtpUnits;
        OnFrame?.Invoke(MediaFrame.ForAudio(sample, _audioTimestamp, durationRtpUnits, _audioFormat));
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _source.SetVideoSourceFormat(_format);
        _audioSource.SetAudioSourceFormat(_audioFormat);
        await _source.StartVideo().ConfigureAwait(false);
        await _audioSource.StartAudio().ConfigureAwait(false);
        _logger.LogDebug("Test pattern source started: H264 @ {Fps}fps + {Codec} music.", _fps, _audioFormat.Codec);
    }

    public string Describe() => $"testpattern h264 @ {_fps}fps + music ({_audioFormat.Codec.ToString().ToLowerInvariant()})";

    public async ValueTask DisposeAsync()
    {
        _source.OnVideoSourceEncodedSample -= HandleEncoded;
        _audioSource.OnAudioSourceEncodedSample -= HandleAudioEncoded;
        try { await _source.CloseVideo().ConfigureAwait(false); } catch { /* best effort */ }
        try { await _audioSource.CloseAudio().ConfigureAwait(false); } catch { /* best effort */ }
        _source.Dispose();
    }
}

/// <summary>
/// An egress edge backed by the shared <see cref="VideoSink"/>: writes the still encoded bitstream to
/// a file (VP8 in IVF, H264/H265 as Annex B), an ffplay window ("play"), stdout ("-") or discards it
/// ("null"). The sink does its IO on its own worker thread, so Write never blocks the source.
/// </summary>
public sealed class VideoSinkNode : ISinkNode
{
    private readonly VideoSink _sink;
    private readonly string _spec;

    public VideoSinkNode(VideoSink sink, string spec)
    {
        _sink = sink;
        _spec = spec;
    }

    public bool IsStdout => _sink.IsStdout;

    // The underlying VideoSink starts lazily on the first frame, so there is nothing to bring up.
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public void Write(MediaFrame frame)
    {
        // A video sink ignores any audio frames a source fans to it (e.g. a sip: source forwarding
        // audio alongside a scope video).
        if (frame.Kind != MediaKind.Video)
        {
            return;
        }

        _sink.WriteFrame(frame.Payload, frame.RtpTimestamp, frame.VideoFormat);
    }

    public SinkStats GetStats() => new(_sink.FramesWritten, _sink.BytesWritten, _sink.DroppedFrames);

    public string Describe() => _spec;

    public ValueTask DisposeAsync()
    {
        _sink.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Maps a --from/--to edge spec to a node. The single extension point for new edge schemes.</summary>
public static class EdgeFactory
{
    public static ISourceNode CreateSource(string spec, EdgeOptions options, ILogger logger)
    {
        var (scheme, rest) = Split(spec);

        switch (scheme)
        {
            case "testpattern":
                return new TestPatternSourceNode(options.Fps, ResolveAudioFormat(options), logger);

            case "whep":
                if (string.IsNullOrWhiteSpace(rest))
                {
                    throw new EdgeException("The whep source needs a URL, e.g. --from whep:https://host/api/whep.");
                }
                return new WhepSourceNode(rest!, options.Token, options.TimeoutSeconds, logger);

            case "sip":
            case "sips":
                if (string.IsNullOrWhiteSpace(rest))
                {
                    throw new EdgeException("The sip source needs a destination, e.g. --from sip:music@iptel.org.");
                }
                // SipDestination handles both the "sip:"/"sips:" form and a bare user@host, so pass
                // the original spec rather than the scheme-stripped rest.
                return CreateSipSource(spec, options, logger);

            case "whip":
            case "livekit":
            case "cloudflare":
                throw new EdgeException(
                    $"'{scheme}' as a --from source is not wired into route yet (sources: testpattern, whep:<url>, sip:<uri>). " +
                    "The transport receivers exist as their own verbs today; they become route sources in a later version.");

            default:
                // A bare user@host (no scheme) is taken as a SIP destination, so "--from music@iptel.org" works.
                if (spec.Contains('@'))
                {
                    return CreateSipSource(spec, options, logger);
                }
                throw new EdgeException($"Unknown --from edge '{spec}'. Sources: testpattern, whep:<url>, sip:<uri>.");
        }
    }

    /// <summary>
    /// Builds a sip: source: a SIP call whose received audio is forwarded into the graph. When the
    /// verb's --scope option is set the source is wrapped in an <see cref="AudioScopeTransform"/> so
    /// it also emits a generated audio-scope video.
    /// </summary>
    private static ISourceNode CreateSipSource(string sipSpec, EdgeOptions options, ILogger logger)
    {
        if (!SipDestination.TryParse(sipSpec, out SIPURI uri, out string? error))
        {
            throw new EdgeException(error!);
        }

        var sinkFormat = ResolveAudioFormat(options);

        // The SIP leg is G.711: carry the call in the sink codec directly when that is a G.711 variant
        // (pure relay), otherwise (Opus) carry it as PCMU and transcode up to the sink codec.
        var sipLegFormat = sinkFormat.Codec == AudioCodecsEnum.OPUS
            ? new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU)
            : sinkFormat;

        ISourceNode source = new SipSourceNode(uri, sipLegFormat, options.SipUsername, options.SipPassword, options.TimeoutSeconds, logger);

        // The scope decodes the (G.711) call audio for its waveform, so it sits inside the transcode.
        if (options.Scope)
        {
            source = new AudioScopeTransform(source, options, logger);
        }

        // Bridge the G.711 call audio up to an Opus sink when the codecs differ (no-op otherwise).
        if (sinkFormat.Codec != sipLegFormat.Codec)
        {
            source = new AudioTranscodeTransform(source, sinkFormat, logger);
        }

        return source;
    }

    /// <summary>Resolves the verb's --audio-codec to a format, throwing <see cref="EdgeException"/> if invalid.</summary>
    private static AudioFormat ResolveAudioFormat(EdgeOptions options)
    {
        if (!RouteAudio.TryResolveCodec(options.AudioCodec, out var format, out string? error))
        {
            throw new EdgeException(error!);
        }
        return format;
    }

    public static ISinkNode CreateSink(string spec, EdgeOptions options, ILogger logger)
    {
        var (scheme, rest) = Split(spec);

        switch (scheme)
        {
            case "whip":
                if (string.IsNullOrWhiteSpace(rest))
                {
                    throw new EdgeException("The whip sink needs a URL, e.g. --to whip:http://host/whip.");
                }
                if (!Uri.TryCreate(rest, UriKind.Absolute, out var whipUri) ||
                    (whipUri.Scheme != Uri.UriSchemeHttp && whipUri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new EdgeException($"Could not parse the whip sink '{rest}' as an HTTP or HTTPS URL.");
                }
                return new WhipSinkNode(rest!, ResolveAudioFormat(options), options.Token, options.TimeoutSeconds, logger);

            case "sip":
            case "sips":
            case "livekit":
            case "cloudflare":
                throw new EdgeException(
                    $"'{scheme}' as a --to sink is not wired into route yet (sinks: a file path, play, null, -, whip:<url>). " +
                    "Publish into these with the dedicated verbs today (e.g. 'livekit room'); they become route sinks in a later version.");

            default:
                // Anything else is a VideoSink spec: a file path, "play", "null" or "-".
                var sink = VideoSink.Create(spec, logger, out string? error);
                if (error != null)
                {
                    throw new EdgeException(error);
                }
                return new VideoSinkNode(sink, spec);
        }
    }

    /// <summary>
    /// Splits "scheme:rest" into its parts. A scheme must be at least two letters so a Windows drive
    /// path ("C:\out.ivf") is treated as a bare path, not a scheme. Anything without a scheme (a path,
    /// "play", "null", "-") comes back as (loweredSpec, null).
    /// </summary>
    private static (string scheme, string? rest) Split(string spec)
    {
        int colon = spec.IndexOf(':');
        if (colon >= 2 && spec[..colon].All(char.IsLetter))
        {
            return (spec[..colon].ToLowerInvariant(), spec[(colon + 1)..]);
        }
        return (spec.ToLowerInvariant(), null);
    }
}
