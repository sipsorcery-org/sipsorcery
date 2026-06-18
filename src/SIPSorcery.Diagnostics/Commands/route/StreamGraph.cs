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
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Diagnostics.Commands.Route;

/// <summary>
/// One unit travelling along a graph edge: a single depacketised but still ENCODED video frame
/// (the repacketise level - it carries a compressed payload, never raw samples, which is what keeps
/// the per-frame work cheap). Audio and data are the planned next frame kinds; v0.1 routes video only.
/// </summary>
public readonly record struct MediaFrame(byte[] Payload, uint RtpTimestamp, VideoFormat Format);

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
    private long _framesRouted;

    public StreamGraph(ISourceNode source, IReadOnlyList<ISinkNode> sinks)
    {
        _source = source;
        _sinks = sinks;
    }

    public ISourceNode Source => _source;
    public IReadOnlyList<ISinkNode> Sinks => _sinks;
    public long FramesRouted => Interlocked.Read(ref _framesRouted);

    /// <summary>
    /// Starts the source and runs until it ends, the duration elapses (0 = run until the source ends
    /// or cancellation), or cancellation. Returns the reason the run stopped. Does not dispose the
    /// nodes; the caller disposes them (sinks first, to finalise files) and then reads the stats.
    /// </summary>
    public async Task<string> RunAsync(int durationSeconds, CancellationToken ct)
    {
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
            Interlocked.Increment(ref _framesRouted);
            foreach (var sink in _sinks)
            {
                sink.Write(frame);
            }
        }
    }
}
