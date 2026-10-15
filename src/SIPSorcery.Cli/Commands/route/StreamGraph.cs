//-----------------------------------------------------------------------------
// Filename: StreamGraph.cs
//
// Description: The v0.1 stream graph: the execution model the "route" verb runs
// on. A graph is a set of nodes joined by directed edges; media travels along the
// edges as still-ENCODED frames (the repacketise level, never raw samples) from a
// source node to one or more sink nodes. v0.1 supports the simplest shape - a
// single source fanned out to N sinks (a free tee) - but the node/edge model is
// the same one the richer shapes (transforms, fan-in, dynamic re-routing) extend.
//
// A routing decision is just a mutation of this graph; v0.1 builds the graph once
// from --from/--to and runs it. Per-frame work rides the edges (the Fan handler);
// the graph itself is only the bookkeeping of what is connected to what.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
// 23 Jun 2026	Aaron Clauson	MediaFrame carries audio as well as video, and sinks gained an
//                              async start, so a sip: source can be forwarded to a whip: sink.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

/// <summary>Which kind of media a <see cref="MediaFrame"/> carries.</summary>
public enum MediaKind
{
    Video,
    Audio
}

/// <summary>
/// One unit travelling along a graph edge: a single depacketised but still ENCODED frame (the
/// repacketise level - it carries a compressed payload, never raw samples, which is what keeps the
/// per-frame work cheap). A frame is either video or audio; <see cref="DurationRtpUnits"/> is the
/// frame's span in its media clock (needed to re-time it onto an outgoing WebRTC track). Use the
/// <see cref="ForVideo"/> / <see cref="ForAudio"/> factories rather than the positional constructor.
/// </summary>
public readonly record struct MediaFrame(
    MediaKind Kind,
    byte[] Payload,
    uint RtpTimestamp,
    uint DurationRtpUnits,
    VideoFormat VideoFormat,
    AudioFormat AudioFormat)
{
    public static MediaFrame ForVideo(byte[] payload, uint rtpTimestamp, VideoFormat format, uint durationRtpUnits = 0) =>
        new(MediaKind.Video, payload, rtpTimestamp, durationRtpUnits, format, AudioFormat.Empty);

    public static MediaFrame ForAudio(byte[] payload, uint rtpTimestamp, uint durationRtpUnits, AudioFormat format) =>
        new(MediaKind.Audio, payload, rtpTimestamp, durationRtpUnits, VideoFormat.Empty, format);
}

/// <summary>Thrown when an edge spec is invalid or unsupported. Carries a user facing message.</summary>
public sealed class EdgeException : Exception
{
    public EdgeException(string message) : base(message) { }
}

/// <summary>Common to every graph node: an edge that can be torn down and described for the result.</summary>
public interface IStreamNode : IAsyncDisposable
{
    /// <summary>A one line description of the edge for logs and the result, e.g. "testpattern vp8 @ 30fps".</summary>
    string Describe();
}

/// <summary>A node that produces frames: the ingress edge of the graph (a generator or a transport receiver).</summary>
public interface ISourceNode : IStreamNode
{
    /// <summary>Raised for each produced frame. The graph subscribes this to fan the frame to the sinks.</summary>
    event Action<MediaFrame>? OnFrame;

    /// <summary>How long the source took to come up (e.g. a transport connect), or null for a generator.</summary>
    long? ConnectTimeMs { get; }

    /// <summary>Begins producing frames. Returns once producing has started; throws <see cref="EdgeException"/> on failure.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Completes when the source ends on its own (a remote hangup, end of input). An endless source
    /// (a generator) never completes this, so the run is then bounded only by --duration / cancellation.
    /// </summary>
    Task Completion { get; }
}

/// <summary>Frame and byte counts a sink has committed, plus any frames it dropped keeping up.</summary>
public readonly record struct SinkStats(int Frames, long Bytes, int Dropped);

/// <summary>A node that consumes frames: an egress edge of the graph (a file, a player, a transport sender).</summary>
public interface ISinkNode : IStreamNode
{
    /// <summary>
    /// Brings the sink up before any frame is written. A transport sink (whip:) does its
    /// offer/answer/ICE/DTLS handshake here so it is connected before media flows; a local sink
    /// (file/play/null) starts lazily on first write and implements this as a no-op. Throws
    /// <see cref="EdgeException"/> on failure.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Hands one frame to the sink. Must not block the calling (source/receive) thread.</summary>
    void Write(MediaFrame frame);

    /// <summary>Final counts. Read after <see cref="IAsyncDisposable.DisposeAsync"/> so any worker has drained.</summary>
    SinkStats GetStats();
}

/// <summary>
/// Wires a single source to one or more sinks and pumps it. The fan-out to multiple sinks is the
/// graph's one transform in v0.1 (a tee), and it is free: every sink simply subscribes to the source.
/// </summary>
public sealed class StreamGraph
{
    private readonly ISourceNode _source;
    private readonly IReadOnlyList<ISinkNode> _sinks;
    private long _videoFramesRouted;
    private long _audioFramesRouted;

    public StreamGraph(ISourceNode source, IReadOnlyList<ISinkNode> sinks)
    {
        _source = source;
        _sinks = sinks;
    }

    public ISourceNode Source => _source;
    public IReadOnlyList<ISinkNode> Sinks => _sinks;
    public long VideoFramesRouted => Interlocked.Read(ref _videoFramesRouted);
    public long AudioFramesRouted => Interlocked.Read(ref _audioFramesRouted);
    public long FramesRouted => VideoFramesRouted + AudioFramesRouted;

    /// <summary>
    /// Brings the sinks up, starts the source, and runs until it ends, the duration elapses (0 = run
    /// until the source ends or cancellation), or cancellation. Returns the reason the run stopped.
    /// Sinks are started before the source so a transport sink (whip:) is connected before the first
    /// frame is written. Does not dispose the nodes; the caller disposes them (sinks first, to
    /// finalise files) and then reads the stats.
    /// </summary>
    public async Task<string> RunAsync(int durationSeconds, CancellationToken ct)
    {
        foreach (var sink in _sinks)
        {
            await sink.StartAsync(ct).ConfigureAwait(false);
        }

        _source.OnFrame += Fan;
        try
        {
            await _source.StartAsync(ct).ConfigureAwait(false);

            var window = durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : Timeout.InfiniteTimeSpan;
            using var timer = new CancellationTokenSource(window);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);

            try
            {
                await _source.Completion.WaitAsync(linked.Token).ConfigureAwait(false);
                return "source ended";
            }
            catch (OperationCanceledException)
            {
                return ct.IsCancellationRequested ? "cancelled" : "duration elapsed";
            }
        }
        finally
        {
            _source.OnFrame -= Fan;
        }

        void Fan(MediaFrame frame)
        {
            if (frame.Kind == MediaKind.Audio)
            {
                Interlocked.Increment(ref _audioFramesRouted);
            }
            else
            {
                Interlocked.Increment(ref _videoFramesRouted);
            }

            foreach (var sink in _sinks)
            {
                sink.Write(frame);
            }
        }
    }
}
