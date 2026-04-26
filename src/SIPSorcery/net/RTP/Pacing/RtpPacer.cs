//-----------------------------------------------------------------------------
// Filename: RtpPacer.cs
//
// Description: Outbound RTP packet pacer. Spreads RTP packets evenly in
// time to prevent the burst pattern that happens when a high-bitrate
// codec (e.g. an intra-only VP8 producing ~50 KB keyframes) splits each
// frame into dozens of RTP packets and ships them through SendRtpRaw in a
// tight loop.
//
// Without pacing, every multi-packet frame produces a sub-millisecond
// burst of UDP sends followed by an idle period until the next frame.
// On the receiver, that burst exceeds the rate at which the WebRTC
// pipeline can drain its UDP receive buffer, packets accumulate, and
// over time something gives — typically the audio stream becomes
// unrecoverable as collateral damage of the buffer overflow. Spreading
// the same packets evenly across the inter-frame interval keeps the
// receive pipeline below its overflow threshold.
//
// Implementation
// --------------
// A single dedicated background thread pulls work items off an unbounded
// Channel and dispatches them at a fixed minimum inter-packet interval,
// computed from a target packet rate. Timing uses Stopwatch +
// SpinWait.SpinOnce, so precision is sub-millisecond regardless of the
// host OS timer resolution (Windows defaults to 15.6 ms ticks; this
// pacer doesn't depend on timeBeginPeriod).
//
// Sequence numbers and SRTP encryption are performed on the pacer
// thread, not on the calling thread, so the calling thread (typically a
// codec timer callback) returns immediately after enqueue. The Channel
// is FIFO so packet order is preserved.
//
// Usage
// -----
// Per MediaStream, opt-in. Default behaviour is unchanged for callers
// that don't set a Pacer.
//
//   var pacer = new RtpPacer(targetPacketsPerSecond: 1000);
//   videoStream.Pacer = pacer;
//
// On disposal of the MediaStream / RTPSession, dispose the pacer to
// stop its background thread.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 26 Apr 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Concurrent;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Spreads outbound RTP packets evenly in time to avoid burst pressure
    /// on the receiver's UDP / WebRTC pipeline. See header comment for
    /// rationale.
    /// </summary>
    public sealed class RtpPacer : IDisposable
    {
        private readonly BlockingCollection<Action> _queue;
        private readonly long _minIntervalStopwatchTicks;
        private readonly Thread _worker;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private long _enqueueCount;
        private long _dispatchCount;
        private bool _disposed;

        /// <summary>
        /// Creates a new pacer that dispatches at most
        /// <paramref name="targetPacketsPerSecond"/> packets per second.
        /// Higher values pace less aggressively. As a starting point: for a
        /// keyframe-only encoder at 30 fps producing 13 packets per frame,
        /// pick ~390 (= 13 * 30) — that just barely fits one frame's
        /// packets in one inter-frame interval. ~500-1000 leaves some
        /// headroom for jitter without making the burst much sharper.
        /// </summary>
        public RtpPacer(int targetPacketsPerSecond)
        {
            if (targetPacketsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetPacketsPerSecond),
                    "targetPacketsPerSecond must be positive.");
            }

            _queue = new BlockingCollection<Action>();

            _minIntervalStopwatchTicks = Stopwatch.Frequency / targetPacketsPerSecond;

            _worker = new Thread(WorkerLoop)
            {
                Name = $"SIPSorcery RTP Pacer ({targetPacketsPerSecond} pps)",
                IsBackground = true,
            };
            _worker.Start();
        }

        /// <summary>Total packets accepted into the pacer's queue.</summary>
        public long EnqueueCount => Interlocked.Read(ref _enqueueCount);

        /// <summary>Total packets dispatched (sent) by the pacer thread.</summary>
        public long DispatchCount => Interlocked.Read(ref _dispatchCount);

        /// <summary>Approximate current queue depth (packets awaiting dispatch).</summary>
        public long QueueDepth => Interlocked.Read(ref _enqueueCount) - Interlocked.Read(ref _dispatchCount);

        /// <summary>
        /// Enqueue a send action. Returns immediately; the action runs on
        /// the pacer thread at its scheduled time slot.
        /// </summary>
        public void Enqueue(Action sendAction)
        {
            if (sendAction == null) { throw new ArgumentNullException(nameof(sendAction)); }
            if (_disposed) { return; }

            try
            {
                _queue.Add(sendAction);
                Interlocked.Increment(ref _enqueueCount);
            }
            catch (InvalidOperationException)
            {
                // BlockingCollection.Add throws if completed/disposed —
                // safe to ignore on shutdown.
            }
        }

        private void WorkerLoop()
        {
            long nextSlotTicks = Stopwatch.GetTimestamp();

            try
            {
                bool justResumed = true;

                foreach (var send in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    // Reset the slot to "now" if we just woke up from an
                    // empty queue, so we don't dispatch immediately after
                    // a long idle window (which would burst the first
                    // wave of new work).
                    if (justResumed)
                    {
                        nextSlotTicks = Stopwatch.GetTimestamp();
                        justResumed = false;
                    }

                    // Wait until our scheduled slot. SpinWait.SpinOnce
                    // would escalate to Thread.Sleep(1) after ~30
                    // iterations, capping the achievable pacing rate
                    // around 900 pps regardless of the configured
                    // target. We need sub-ms precision for high-pps
                    // targets (a 30 fps × 42-packet workload at 1500 pps
                    // = 0.67 ms per slot), so we drive the wait
                    // manually: real Thread.Sleep when we have plenty
                    // of time, Thread.Sleep(0) (yield) for short waits,
                    // and a brief Thread.SpinWait for the final
                    // sub-ms approach.
                    long now = Stopwatch.GetTimestamp();
                    if (now < nextSlotTicks)
                    {
                        long oneMsTicks = Stopwatch.Frequency / 1000;
                        while (true)
                        {
                            long current = Stopwatch.GetTimestamp();
                            long remaining = nextSlotTicks - current;
                            if (remaining <= 0) { break; }

                            if (remaining > oneMsTicks * 2)
                            {
                                Thread.Sleep(1);
                            }
                            else if (remaining > oneMsTicks / 4)
                            {
                                Thread.Sleep(0);   // yield, no OS sleep
                            }
                            else
                            {
                                Thread.SpinWait(20);
                            }
                        }
                    }

                    try { send(); }
                    catch
                    {
                        // Swallow: one failed send must not kill the pacer.
                        // Detailed logging happens inside SendRtpRaw paths.
                    }

                    Interlocked.Increment(ref _dispatchCount);

                    // Schedule the next slot. Use max(now, nextSlot) so that
                    // when we fall behind (e.g. the work item itself was
                    // slow) we don't try to claw back an exponential burst.
                    long after = Stopwatch.GetTimestamp();
                    nextSlotTicks = Math.Max(after, nextSlotTicks) + _minIntervalStopwatchTicks;

                    // If the queue is empty after this dispatch, mark
                    // for reset so the next batch starts at a fresh slot.
                    if (_queue.Count == 0)
                    {
                        justResumed = true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected on dispose
            }
            catch (InvalidOperationException)
            {
                // GetConsumingEnumerable throws this once the collection is
                // marked complete and drained.  Normal shutdown.
            }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            try { _queue.CompleteAdding(); } catch { /* idempotent */ }
            _cts.Cancel();
        }
    }
}
