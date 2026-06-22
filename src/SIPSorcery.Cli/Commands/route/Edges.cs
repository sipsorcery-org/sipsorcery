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
// v0.1 sources: testpattern (a generated VP8 pattern, no native deps) and whep (a
// live WebRTC ingress). v0.1 sinks: a file, an ffplay window ("play"), discard
// ("null") or the bitstream on stdout ("-"). All sink frames stay ENCODED - the
// graph repacketises, it does not transcode.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
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
using SIPSorceryMedia.Abstractions;
using Vpx.Net;

namespace SIPSorcery.Cli.Commands.Route;

/// <summary>The few knobs an edge needs from the verb (frame rate for generators, auth/timeout for transports).</summary>
public sealed record EdgeOptions(int Fps, string? Token, int TimeoutSeconds);

/// <summary>
/// A generated VP8 test pattern source: the zero dependency ingress for demonstrating the graph.
/// Reuses the library's VideoTestPatternSource + managed VP8 encoder, emitting each encoded frame as
/// a <see cref="MediaFrame"/>. The RTP timestamp is accumulated from the per frame duration so a sink
/// (e.g. the IVF writer) gets a monotonic clock.
/// </summary>
public sealed class TestPatternSourceNode : ISourceNode
{
    private const int VP8_PAYLOAD_ID = 96;

    private readonly VideoTestPatternSource _source;
    private readonly VideoFormat _format = new(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID);
    private readonly int _fps;
    private readonly ILogger _logger;
    private uint _timestamp;

    public event Action<MediaFrame>? OnFrame;

    // A generator never ends on its own; the run is bounded by --duration / cancellation.
    public Task Completion { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    public long? ConnectTimeMs => null;

    public TestPatternSourceNode(int fps, ILogger logger)
    {
        _fps = fps;
        _logger = logger;
        _source = new VideoTestPatternSource(new VP8Codec());
        _source.RestrictFormats(f => f.Codec == VideoCodecsEnum.VP8);
        _source.SetFrameRate(fps);
        _source.OnVideoSourceEncodedSample += HandleEncoded;
    }

    private void HandleEncoded(uint durationRtpUnits, byte[] sample)
    {
        _timestamp += durationRtpUnits;
        OnFrame?.Invoke(new MediaFrame(sample, _timestamp, _format));
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _source.SetVideoSourceFormat(_format);
        await _source.StartVideo().ConfigureAwait(false);
        _logger.LogDebug("Test pattern source started: VP8 @ {Fps}fps.", _fps);
    }

    public string Describe() => $"testpattern vp8 @ {_fps}fps";

    public async ValueTask DisposeAsync()
    {
        _source.OnVideoSourceEncodedSample -= HandleEncoded;
        try { await _source.CloseVideo().ConfigureAwait(false); } catch { /* best effort */ }
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

    public void Write(MediaFrame frame) => _sink.WriteFrame(frame.Payload, frame.RtpTimestamp, frame.Format);

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
                return new TestPatternSourceNode(options.Fps, logger);

            case "whep":
                if (string.IsNullOrWhiteSpace(rest))
                {
                    throw new EdgeException("The whep source needs a URL, e.g. --from whep:https://host/api/whep.");
                }
                return new WhepSourceNode(rest!, options.Token, options.TimeoutSeconds, logger);

            case "whip":
            case "sip":
            case "livekit":
            case "cloudflare":
                throw new EdgeException(
                    $"'{scheme}' as a --from source is not wired into route yet (v0.1 sources: testpattern, whep:<url>). " +
                    "The transport receivers exist as their own verbs today; they become route sources in a later version.");

            default:
                throw new EdgeException($"Unknown --from edge '{spec}'. v0.1 sources: testpattern, whep:<url>.");
        }
    }

    public static ISinkNode CreateSink(string spec, ILogger logger)
    {
        var (scheme, _) = Split(spec);

        switch (scheme)
        {
            case "whip":
            case "sip":
            case "livekit":
            case "cloudflare":
                throw new EdgeException(
                    $"'{scheme}' as a --to sink is not wired into route yet (v0.1 sinks: a file path, play, null, -). " +
                    "Publish into these with the dedicated verbs today (e.g. 'webrtc whip', 'livekit room'); they become route sinks in a later version.");

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
