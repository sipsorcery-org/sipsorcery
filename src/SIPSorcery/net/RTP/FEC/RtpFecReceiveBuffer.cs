//-----------------------------------------------------------------------------
// Filename: RtpFecReceiveBuffer.cs
// Description: Bounded, frame-aware ordering of original and recovered video RTP.
// License: BSD 3-Clause, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Holds video frames until media gaps have been repaired. Repair-only RTP sequence
    /// numbers participate in ordering but are never delivered as video packets.
    /// </summary>
    internal sealed class RtpFecReceiveBuffer : IDisposable
    {
        internal const int MAX_BUFFERED_MEDIA = 128;
        private const int MAX_REPAIR_SLOTS = 256;
        private static readonly ILogger logger = LogFactory.CreateLogger<RtpFecReceiveBuffer>();

        private sealed class Frame
        {
            internal uint Timestamp;
            internal readonly SortedDictionary<long, RTPPacket> Media = new SortedDictionary<long, RTPPacket>();
            internal long? Start;
            internal bool HasStart;
            internal long? Marker;
            internal long LastUpdated;
            internal long? GapStarted;
            internal long FirstSequence => Start.HasValue ? Math.Min(Start.Value, Media.First().Key) : Media.First().Key;
        }

        private sealed class Delivery
        {
            internal int Generation;
            internal readonly List<(RTPPacket Packet, ushort FramingSequence)> Packets = new List<(RTPPacket, ushort)>();
        }

        private readonly object _lock = new object();
        private readonly Dictionary<uint, Frame> _frames = new Dictionary<uint, Frame>();
        private readonly SortedSet<long> _repairSlots = new SortedSet<long>();
        private readonly HashSet<uint> _finishedTimestamps = new HashSet<uint>();
        private readonly Queue<uint> _finishedOrder = new Queue<uint>();
        private readonly Queue<Delivery> _deliveries = new Queue<Delivery>();
        private readonly Action<RTPPacket, ushort> _deliver;
        private readonly Func<long> _getMilliseconds;
        private readonly IRtpFecReceiveTimer _timer;
        private readonly Func<RTPPacket, bool> _isFrameStart;
        private TimeSpan _repairWait;
        private long? _timerDeadline;
        private long? _newestSequence;
        private long? _committedSequence;
        private long _retiredRepairSlots;
        private int _mediaCount;
        private int _deliveryCount;
        private int _generation;
        private bool _needsFrameStart = true;
        private bool _dispatching;
        private bool _disposed;

        internal RtpFecReceiveBuffer(TimeSpan repairWait, Action<RTPPacket, ushort> deliver,
            Func<long> getMilliseconds = null,
            Func<Action, IRtpFecReceiveTimer> timerFactory = null,
            Func<RTPPacket, bool> isFrameStart = null)
        {
            ValidateWait(repairWait);
            _repairWait = repairWait;
            _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
            _getMilliseconds = getMilliseconds ?? (() => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency));
            _timer = timerFactory?.Invoke(OnTimer) ?? new RtpFecReceiveTimer(OnTimer);
            _isFrameStart = isFrameStart;
        }

        internal int BufferedPacketCount
        {
            get { lock (_lock) { return _mediaCount + _deliveryCount; } }
        }

        /// <summary>
        /// Adds all media recovered from one arrival before attempting any frame delivery.
        /// Protected sequence numbers establish the tentative beginning of the first frame.
        /// </summary>
        internal void AddBatch(IReadOnlyList<RTPPacket> media, ushort wireSequence, bool repairOnly,
            IReadOnlyList<ushort> protectedSequenceNumbers = null)
        {
            bool dispatch;
            lock (_lock)
            {
                if (_disposed) { return; }
                long now = _getMilliseconds();
                // Enforce elapsed deadlines even if the timer callback was delayed by
                // thread scheduling. A late repair must not resurrect an expired frame.
                Process(now);
                long wire = Extend(wireSequence);
                if (repairOnly && (!_committedSequence.HasValue || wire > _committedSequence.Value))
                {
                    _repairSlots.Add(wire);
                }

                if (media != null)
                {
                    foreach (var packet in media)
                    {
                        if (packet == null || _finishedTimestamps.Contains(packet.Header.Timestamp)) { continue; }
                        long sequence = Extend(packet.Header.SequenceNumber);
                        if (_committedSequence.HasValue && sequence <= _committedSequence.Value) { continue; }
                        if (!_frames.TryGetValue(packet.Header.Timestamp, out var frame))
                        {
                            frame = new Frame { Timestamp = packet.Header.Timestamp, LastUpdated = now };
                            _frames.Add(frame.Timestamp, frame);
                        }
                        if (frame.Media.ContainsKey(sequence)) { continue; }
                        frame.Media.Add(sequence, packet);
                        frame.LastUpdated = now;
                        _mediaCount++;
                        if (_isFrameStart?.Invoke(packet) == true)
                        {
                            frame.Start = !frame.Start.HasValue ? sequence : Math.Min(frame.Start.Value, sequence);
                            frame.HasStart = true;
                        }
                        if (packet.Header.MarkerBit == 1)
                        {
                            frame.Marker = !frame.Marker.HasValue ? sequence : Math.Max(frame.Marker.Value, sequence);
                        }
                    }
                }

                if (protectedSequenceNumbers != null && protectedSequenceNumbers.Count > 0)
                {
                    var covered = protectedSequenceNumbers.Select(Extend).ToArray();
                    if (_frames.Count > 0)
                    {
                        var frame = OldestFrame();
                        // Only coverage overlapping this frame establishes a start. Groups
                        // from another timestamp must not move its tentative lower bound.
                        if (covered.Any(sequence => frame.Media.ContainsKey(sequence)))
                        {
                            long lowerBound = covered.Min();
                            if (!_committedSequence.HasValue || lowerBound > _committedSequence.Value)
                            {
                                frame.Start = !frame.Start.HasValue ? lowerBound : Math.Min(frame.Start.Value, lowerBound);
                                // A parity mask identifies a protection group, not necessarily
                                // the beginning of a frame. In codec-aware mode a recovered
                                // terminal fragment must wait for a genuine frame start.
                                if (_isFrameStart == null) { frame.HasStart = true; }
                            }
                        }
                    }
                }

                Process(now);
                while (_mediaCount + _deliveryCount > MAX_BUFFERED_MEDIA && _frames.Count > 0)
                {
                    Finish(OldestFrame(), false);
                    Process(now);
                }
                while (_repairSlots.Count > MAX_REPAIR_SLOTS) { _repairSlots.Remove(_repairSlots.Min); }
                Schedule(now);
                dispatch = TakeDispatchOwnership();
            }
            if (dispatch) { Dispatch(); }
        }

        internal void SetRepairWait(TimeSpan repairWait)
        {
            ValidateWait(repairWait);
            bool dispatch;
            lock (_lock)
            {
                if (_disposed) { return; }
                _repairWait = repairWait;
                long now = _getMilliseconds();
                Process(now);
                Schedule(now);
                dispatch = TakeDispatchOwnership();
            }
            if (dispatch) { Dispatch(); }
        }

        private static void ValidateWait(TimeSpan repairWait)
        {
            if (repairWait < TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(repairWait)); }
        }

        private long Extend(ushort sequence)
        {
            long extended = !_newestSequence.HasValue ? sequence :
                _newestSequence.Value + unchecked((short)(sequence - (ushort)_newestSequence.Value));
            if (!_newestSequence.HasValue || extended > _newestSequence.Value) { _newestSequence = extended; }
            return extended;
        }

        private Frame OldestFrame() => _frames.Values.OrderBy(frame => frame.FirstSequence).First();

        private void Process(long now)
        {
            while (_frames.Count > 0)
            {
                var frame = OldestFrame();
                if (frame.Media.Count > MAX_BUFFERED_MEDIA)
                {
                    Finish(frame, false);
                    continue;
                }
                long start = !_needsFrameStart && _committedSequence.HasValue ? _committedSequence.Value + 1 : frame.FirstSequence;
                while (_repairSlots.Contains(start)) { start++; }
                bool complete = frame.Marker.HasValue && (!_needsFrameStart || frame.HasStart);
                bool gap = false;
                if (frame.Marker.HasValue)
                {
                    // The bounded media window also bounds a plausible sequence span.
                    if (frame.Marker.Value - start > MAX_BUFFERED_MEDIA + MAX_REPAIR_SLOTS)
                    {
                        complete = false;
                        gap = true;
                    }
                    else
                    {
                        for (long sequence = start; sequence <= frame.Marker.Value; sequence++)
                        {
                            if (!frame.Media.ContainsKey(sequence) && !_repairSlots.Contains(sequence))
                            {
                                complete = false;
                                gap = true;
                                break;
                            }
                        }
                    }
                }
                if (complete)
                {
                    Finish(frame, true);
                    continue;
                }
                if (gap && !frame.GapStarted.HasValue) { frame.GapStarted = now; }
                long deadline = Deadline(frame);
                if (now >= deadline)
                {
                    Finish(frame, false);
                    continue;
                }
                break;
            }
        }

        private long WaitMilliseconds => (long)Math.Ceiling(_repairWait.TotalMilliseconds);

        private long Deadline(Frame frame) => frame.GapStarted.HasValue
            ? frame.GapStarted.Value + WaitMilliseconds
            : frame.LastUpdated + (frame.Marker.HasValue ? WaitMilliseconds : Math.Max(1, WaitMilliseconds));

        private void Finish(Frame frame, bool deliver)
        {
            if (deliver)
            {
                var batch = new Delivery { Generation = _generation };
                foreach (var media in frame.Media)
                {
                    long repairs = _retiredRepairSlots + _repairSlots.Count(sequence => sequence < media.Key);
                    batch.Packets.Add((media.Value, unchecked((ushort)(media.Key - repairs))));
                }
                // A slow application cannot grow the queued callback backlog indefinitely.
                while (_deliveryCount + batch.Packets.Count > MAX_BUFFERED_MEDIA && _deliveries.Count > 0)
                {
                    _deliveryCount -= _deliveries.Dequeue().Packets.Count;
                }
                _deliveries.Enqueue(batch);
                _deliveryCount += batch.Packets.Count;
            }
            _mediaCount -= frame.Media.Count;
            _frames.Remove(frame.Timestamp);
            long last = frame.Marker ?? frame.Media.Last().Key;
            _committedSequence = !_committedSequence.HasValue ? last : Math.Max(_committedSequence.Value, last);
            _needsFrameStart = !deliver;
            _finishedTimestamps.Add(frame.Timestamp);
            _finishedOrder.Enqueue(frame.Timestamp);
            while (_finishedOrder.Count > MAX_BUFFERED_MEDIA) { _finishedTimestamps.Remove(_finishedOrder.Dequeue()); }
            while (_repairSlots.Count > 0 && _repairSlots.Min <= _committedSequence.Value)
            {
                _repairSlots.Remove(_repairSlots.Min);
                _retiredRepairSlots++;
            }
        }

        private void Schedule(long now, bool force = false)
        {
            if (_disposed) { return; }
            long? deadline = _frames.Count == 0 ? (long?)null : Deadline(OldestFrame());
            if (!force && _timerDeadline == deadline) { return; }
            _timerDeadline = deadline;
            _timer.Change(deadline.HasValue
                ? TimeSpan.FromMilliseconds(Math.Max(0, deadline.Value - now))
                : Timeout.InfiniteTimeSpan);
        }

        private void OnTimer()
        {
            bool dispatch;
            lock (_lock)
            {
                if (_disposed || !_timerDeadline.HasValue) { return; }
                long now = _getMilliseconds();
                // Change/stop cannot recall an already queued callback. Always inspect
                // the current absolute deadline, and rearm if that callback arrived early.
                Process(now);
                Schedule(now, force: true);
                dispatch = TakeDispatchOwnership();
            }
            if (dispatch) { Dispatch(); }
        }

        private bool TakeDispatchOwnership()
        {
            if (_dispatching || _deliveries.Count == 0) { return false; }
            _dispatching = true;
            return true;
        }

        private void Dispatch()
        {
            while (true)
            {
                Delivery batch;
                lock (_lock)
                {
                    if (_deliveries.Count == 0)
                    {
                        _dispatching = false;
                        return;
                    }
                    batch = _deliveries.Dequeue();
                    _deliveryCount -= batch.Packets.Count;
                }
                foreach (var item in batch.Packets)
                {
                    lock (_lock) { if (_disposed || batch.Generation != _generation) { break; } }
                    try { _deliver(item.Packet, item.FramingSequence); }
                    catch (Exception ex) { logger.LogWarning(ex, "Exception delivering FEC-reordered video RTP."); }
                }
            }
        }

        internal void Reset()
        {
            lock (_lock) { Clear(); }
        }

        private void Clear()
        {
            _generation++;
            _timerDeadline = null;
            if (!_disposed) { _timer.Change(Timeout.InfiniteTimeSpan); }
            _frames.Clear();
            _repairSlots.Clear();
            _finishedTimestamps.Clear();
            _finishedOrder.Clear();
            _deliveries.Clear();
            _newestSequence = null;
            _committedSequence = null;
            _retiredRepairSlots = 0;
            _mediaCount = 0;
            _deliveryCount = 0;
            _needsFrameStart = true;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) { return; }
                _disposed = true;
                Clear();
                _timer.Dispose();
            }
        }
    }
}
