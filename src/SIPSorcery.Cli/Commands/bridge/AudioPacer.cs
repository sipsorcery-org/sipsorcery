//-----------------------------------------------------------------------------
// Filename: AudioPacer.cs
//
// Description: Turns bursty, gappy encoded audio into a steady stream: one frame
// per frame period of wall time, the next queued frame or a silence frame when
// nothing is queued, with an RTP timestamp that advances exactly with real time.
// Used by the term bridge endpoint so a player (mpv) gets a continuous audio
// clock even though an agent speaks in bursts with silence in between.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class AudioPacer
{
    private readonly byte[] _silenceFrame;
    private readonly uint _silenceDuration;
    private readonly int _clockRate;
    private readonly int _maxQueued;
    private readonly Action<byte[], uint, uint> _emit;
    private readonly ConcurrentQueue<(byte[] Payload, uint Duration)> _queue = new();

    private uint _rtpTimestamp;
    private long _dueMs;

    /// <param name="silenceFrame">The encoded frame to emit when nothing is queued.</param>
    /// <param name="silenceDuration">The silence frame's duration in RTP clock units.</param>
    /// <param name="clockRate">The RTP clock rate, e.g. 48000 for Opus.</param>
    /// <param name="emit">Called with (payload, RTP timestamp, duration) for each paced frame.</param>
    /// <param name="maxQueued">Queued frames beyond this drop the oldest, bounding the delay.</param>
    public AudioPacer(byte[] silenceFrame, uint silenceDuration, int clockRate, Action<byte[], uint, uint> emit, int maxQueued = 500)
    {
        _silenceFrame = silenceFrame;
        _silenceDuration = silenceDuration;
        _clockRate = clockRate;
        _emit = emit;
        _maxQueued = maxQueued;
    }

    /// <summary>Frames waiting to be paced out.</summary>
    public int Queued => _queue.Count;

    /// <summary>Queues a frame. A duration of 0 is taken as the silence frame's duration.</summary>
    public void Enqueue(byte[] payload, uint duration)
    {
        _queue.Enqueue((payload, duration > 0 ? duration : _silenceDuration));
        while (_queue.Count > _maxQueued && _queue.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Emits every frame due at or before <paramref name="nowMs"/> (time since pacing started) and
    /// returns how long until the next one is due.
    /// </summary>
    public long EmitDue(long nowMs)
    {
        while (_dueMs <= nowMs)
        {
            var (payload, duration) = _queue.TryDequeue(out var frame) ? frame : (_silenceFrame, _silenceDuration);
            _emit(payload, _rtpTimestamp, duration);
            _rtpTimestamp += duration;
            _dueMs += duration * 1000L / _clockRate;
        }

        return _dueMs - nowMs;
    }

    /// <summary>Paces frames out against <paramref name="elapsedMs"/> until cancelled.</summary>
    public async Task RunAsync(Func<long> elapsedMs, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                long waitMs = EmitDue(elapsedMs());
                if (waitMs > 0)
                {
                    await Task.Delay((int)waitMs, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }
}
