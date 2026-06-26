//-----------------------------------------------------------------------------
// Filename: BridgeGraph.cs
//
// Description: The execution model for the "bridge" verb. Where "route" is one-way
// (a source fanned to N sinks), a bridge connects TWO duplex participants both
// ways: each participant both produces frames (ISourceNode.OnFrame) and consumes
// them (ISinkNode.Write), and the graph cross-wires them - a.OnFrame -> b.Write and
// b.OnFrame -> a.Write. So "bridge web agent" is browser.mic -> agent and
// agent.voice -> browser, full duplex.
//
// Reuses the route graph's MediaFrame / ISourceNode / ISinkNode so a participant is
// just a node that is both; no new frame type is introduced.
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
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Cli.Commands.Route;

namespace SIPSorcery.Cli.Commands.Bridge;

/// <summary>A bridge endpoint: a duplex participant that both produces and consumes media frames.</summary>
public interface IBridgeParticipant : ISourceNode, ISinkNode
{
    // ISourceNode and ISinkNode both declare StartAsync; re-declare it here so calls on the combined
    // interface are unambiguous. One implementation on the participant satisfies all three.
    new Task StartAsync(CancellationToken ct);
}

/// <summary>An agent participant that can speak first when cued (the web peer connecting). Lets the
/// command wire a greeting without knowing which concrete agent (Azure, OpenAI) is on the other side.</summary>
public interface IGreetable
{
    void Greet();
}

/// <summary>Cross-wires two participants and pumps media between them in both directions.</summary>
public sealed class BridgeGraph
{
    private readonly IBridgeParticipant _a;
    private readonly IBridgeParticipant _b;
    private long _aToB;
    private long _bToA;

    public BridgeGraph(IBridgeParticipant a, IBridgeParticipant b)
    {
        _a = a;
        _b = b;
    }

    public long AToBFrames => Interlocked.Read(ref _aToB);
    public long BToAFrames => Interlocked.Read(ref _bToA);

    /// <summary>
    /// Starts both participants, wires the two directions, and runs until either participant ends
    /// (e.g. the browser leaves), the duration elapses (0 = until an end or cancellation), or
    /// cancellation. Returns the reason the run stopped. Does not dispose the participants; the caller
    /// disposes them and then reads their stats.
    /// </summary>
    public async Task<string> RunAsync(int durationSeconds, CancellationToken ct)
    {
        void AToB(MediaFrame frame)
        {
            Interlocked.Increment(ref _aToB);
            _b.Write(frame);
        }

        void BToA(MediaFrame frame)
        {
            Interlocked.Increment(ref _bToA);
            _a.Write(frame);
        }

        _a.OnFrame += AToB;
        _b.OnFrame += BToA;
        try
        {
            // Start both before media flows; the cross-wiring is already in place so nothing is missed.
            await _a.StartAsync(ct).ConfigureAwait(false);
            await _b.StartAsync(ct).ConfigureAwait(false);

            var window = durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : Timeout.InfiniteTimeSpan;
            using var timer = new CancellationTokenSource(window);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);

            try
            {
                await Task.WhenAny(_a.Completion, _b.Completion).WaitAsync(linked.Token).ConfigureAwait(false);
                return "a participant ended";
            }
            catch (OperationCanceledException)
            {
                return ct.IsCancellationRequested ? "cancelled" : "duration elapsed";
            }
        }
        finally
        {
            _a.OnFrame -= AToB;
            _b.OnFrame -= BToA;
        }
    }
}
