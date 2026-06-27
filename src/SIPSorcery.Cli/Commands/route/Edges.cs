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
    string AudioCodec = "opus",
    bool OpenBrowser = false,
    string? LiveKitUrl = null,
    string? LiveKitApiKey = null,
    string? LiveKitApiSecret = null,
    string? CloudflareAppId = null);

/// <summary>Resolves the --audio-codec name to the audio format a source produces and a sink offers.</summary>
public static class RouteAudio
{
    public static bool TryResolveCodec(string? name, out AudioFormat format, out string? error)
    {
        error = null;
        switch ((string.IsNullOrWhiteSpace(name) ? "opus" : name).Trim().ToLowerInvariant())
        {
            case "opus":
                format = AudioCommonlyUsedFormats.OpusWebRTC;
                return true;
            case "pcmu":
                format = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU);
                return true;
            case "pcma":
                format = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA);
                return true;
            default:
                format = AudioFormat.Empty;
                error = $"Unknown --audio-codec '{name}'. Use opus (default), pcmu or pcma.";
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

            case "livekit":
                return CreateLiveKitSource(rest, options, logger);

            case "cloudflare":
                return CreateCloudflareSource(rest, options, logger);

            case "whip":
                throw new EdgeException(
                    $"'{scheme}' as a --from source is not wired into route yet (sources: testpattern, whep:<url>, sip:<uri>, livekit[:room], cloudflare:<sessionId>). " +
                    "Use whep:<url> to receive a WHIP/WHEP stream today.");

            default:
                // A bare user@host (no scheme) is taken as a SIP destination, so "--from music@iptel.org" works.
                if (spec.Contains('@'))
                {
                    return CreateSipSource(spec, options, logger);
                }
                throw new EdgeException($"Unknown --from edge '{spec}'. Sources: testpattern, whep:<url>, sip:<uri>, livekit[:room], cloudflare:<sessionId>.");
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

        var graphFormat = ResolveAudioFormat(options);   // the codec the graph/sinks want (opus by default)

        // Offer OPUS first then a G.711 fallback when the graph is OPUS, so the call is carried in OPUS
        // end to end whenever the far end supports it and only falls back to (and transcodes) G.711 when
        // that is all there is. When the user forced a G.711 graph (--audio-codec pcmu/pcma) offer just it.
        List<AudioFormat> offered = graphFormat.Codec == AudioCodecsEnum.OPUS
            ? new List<AudioFormat>
              {
                  AudioCommonlyUsedFormats.OpusWebRTC,
                  new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                  new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)
              }
            : new List<AudioFormat> { graphFormat };

        ISourceNode source = new SipSourceNode(uri, offered, options.SipUsername, options.SipPassword, options.TimeoutSeconds, logger);

        // The scope decodes the call audio for its waveform; it sits inside the transcode so it sees the
        // negotiated codec directly (it decodes by the frame's format, so it handles G.711 or OPUS).
        if (options.Scope)
        {
            source = new AudioScopeTransform(source, options, logger);
        }

        // Bridge whatever the call negotiated up to the graph codec. The transform is a no-op pass-through
        // when the negotiated codec already matches (e.g. OPUS negotiated for an OPUS graph) and only
        // transcodes otherwise (e.g. a G.711 gateway -> OPUS), so it is always safe to wrap.
        source = new AudioTranscodeTransform(source, graphFormat, logger);

        return source;
    }

    /// <summary>
    /// Builds a livekit: sink: publish the graph's media into a LiveKit room. The room is the edge's
    /// "rest" (a random name if omitted); the URL/key/secret come from the --url/--api-key/--api-secret
    /// options, falling back to the LIVEKIT_* environment variables. LiveKit's room pipeline requires
    /// OPUS audio, so the edge requires --audio-codec opus.
    /// </summary>
    private static ISinkNode CreateLiveKitSink(string? rest, EdgeOptions options, ILogger logger)
    {
        string? url = options.LiveKitUrl ?? Environment.GetEnvironmentVariable("LIVEKIT_WEBSOCKET_URL");
        string? apiKey = options.LiveKitApiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
        string? apiSecret = options.LiveKitApiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new EdgeException(
                "The livekit sink needs a URL, API key and API secret (--url/--api-key/--api-secret or " +
                "LIVEKIT_WEBSOCKET_URL/LIVEKIT_API_KEY/LIVEKIT_API_SECRET).");
        }

        var audioFormat = ResolveAudioFormat(options);
        if (audioFormat.Codec != AudioCodecsEnum.OPUS)
        {
            throw new EdgeException("The livekit sink requires OPUS audio; pass --audio-codec opus (LiveKit's room pipeline does not carry G.711).");
        }

        string room = string.IsNullOrWhiteSpace(rest) ? $"cli-{Guid.NewGuid().ToString("N")[..8]}" : rest!;

        return new LiveKitSinkNode(url!, apiKey!, apiSecret!, room, audioFormat, options.TimeoutSeconds, logger);
    }

    /// <summary>
    /// Builds a cloudflare: sink: create a Cloudflare Realtime SFU session and publish the graph's media
    /// to it. The app ID and API token come from --app-id/--token, falling back to the CLOUDFLARE_APPID
    /// and CLOUDFLARE_API_TOKEN environment variables. Cloudflare allocates the session id (printed at
    /// start), so unlike a livekit room there is no name to pass.
    /// </summary>
    private static ISinkNode CreateCloudflareSink(EdgeOptions options, ILogger logger)
    {
        string? appId = options.CloudflareAppId ?? Environment.GetEnvironmentVariable("CLOUDFLARE_APPID");
        string? token = options.Token ?? Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(token))
        {
            throw new EdgeException(
                "The cloudflare sink needs an app ID and API token (--app-id/--token or " +
                "CLOUDFLARE_APPID/CLOUDFLARE_API_TOKEN).");
        }

        return new CloudflareSinkNode(appId!, token!, ResolveAudioFormat(options), options.TimeoutSeconds, logger);
    }

    /// <summary>
    /// Builds a cloudflare: source: subscribe to (pull) the tracks a CloudflareSinkNode published into an
    /// SFU session. The "rest" is the publisher's session id (printed by the sink); the pulled track names
    /// are the ones the sink publishes (cli-video / cli-audio). App ID and token resolve as for the sink.
    /// </summary>
    private static ISourceNode CreateCloudflareSource(string? rest, EdgeOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(rest))
        {
            throw new EdgeException("The cloudflare source needs the publisher's session id, e.g. --from cloudflare:<sessionId> (printed by the cloudflare sink).");
        }

        string? appId = options.CloudflareAppId ?? Environment.GetEnvironmentVariable("CLOUDFLARE_APPID");
        string? token = options.Token ?? Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(token))
        {
            throw new EdgeException(
                "The cloudflare source needs an app ID and API token (--app-id/--token or " +
                "CLOUDFLARE_APPID/CLOUDFLARE_API_TOKEN).");
        }

        return new CloudflareSourceNode(appId!, token!, rest!, options.TimeoutSeconds, logger);
    }

    /// <summary>
    /// Builds a livekit: source: subscribe to a LiveKit room and emit the received media. The room is the
    /// edge's "rest" and is required (you must name the room to subscribe to). Credentials resolve the
    /// same way as the sink (--url/--api-key/--api-secret or the LIVEKIT_* environment variables).
    /// </summary>
    private static ISourceNode CreateLiveKitSource(string? rest, EdgeOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(rest))
        {
            throw new EdgeException("The livekit source needs a room name, e.g. --from livekit:my-room.");
        }

        string? url = options.LiveKitUrl ?? Environment.GetEnvironmentVariable("LIVEKIT_WEBSOCKET_URL");
        string? apiKey = options.LiveKitApiKey ?? Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
        string? apiSecret = options.LiveKitApiSecret ?? Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new EdgeException(
                "The livekit source needs a URL, API key and API secret (--url/--api-key/--api-secret or " +
                "LIVEKIT_WEBSOCKET_URL/LIVEKIT_API_KEY/LIVEKIT_API_SECRET).");
        }

        return new LiveKitSourceNode(url!, apiKey!, apiSecret!, rest!, options.TimeoutSeconds, logger);
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

            case "web":
                // Self-hosting WHEP server: bind a local HTTP listener that serves a player page and
                // answers browser offers. The spec is an optional port ("web" or "web:8080").
                int port = 8080;
                if (!string.IsNullOrWhiteSpace(rest))
                {
                    if (!int.TryParse(rest, out port) || port < 1 || port > 65535)
                    {
                        throw new EdgeException($"The web sink port '{rest}' is not a valid port number (1-65535), e.g. --to web:8080.");
                    }
                }
                return new WebSinkNode(port, ResolveAudioFormat(options), options.Token, options.OpenBrowser, logger);

            case "livekit":
                return CreateLiveKitSink(rest, options, logger);

            case "cloudflare":
                return CreateCloudflareSink(options, logger);

            case "sip":
            case "sips":
                throw new EdgeException(
                    $"'{scheme}' as a --to sink is not wired into route yet (sinks: a file path, play, null, -, whip:<url>, web[:port], livekit[:room], cloudflare). " +
                    "A sip: call sink becomes a route sink in a later version.");

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
