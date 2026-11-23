using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>A deterministic clock whose timer instances can be rearmed without allocation.</summary>
    internal sealed class FecTestClock
    {
        internal long Now { get; set; }
        internal readonly List<ManualTimer> Timers = new List<ManualTimer>();
        internal int PendingCount => Timers.Count(timer => !timer.Disposed && timer.Due.HasValue);

        internal IRtpFecReceiveTimer CreateTimer(Action callback)
        {
            var timer = new ManualTimer(this, callback);
            Timers.Add(timer);
            return timer;
        }

        internal void Advance(TimeSpan duration) => Advance((long)Math.Ceiling(duration.TotalMilliseconds));

        internal void Advance(long milliseconds)
        {
            long target = Now + milliseconds;
            int callbacks = 0;
            while (true)
            {
                var next = Timers.Where(timer => !timer.Disposed && timer.Due.HasValue && timer.Due.Value <= target)
                    .OrderBy(timer => timer.Due.Value).FirstOrDefault();
                if (next == null) break;
                if (++callbacks > 1000) throw new InvalidOperationException("Timer repeatedly rearmed without advancing time.");
                Now = Math.Max(Now, next.Due.Value);
                next.Due = null;
                next.Callback();
            }
            Now = target;
        }

        internal sealed class ManualTimer : IRtpFecReceiveTimer
        {
            private readonly FecTestClock _clock;
            internal readonly Action Callback;
            internal long? Due;
            internal bool Disposed;
            internal int ChangeCount;
            internal int DisposeCount;

            internal ManualTimer(FecTestClock clock, Action callback)
            {
                _clock = clock;
                Callback = callback;
            }

            public void Change(TimeSpan dueTime)
            {
                if (Disposed) throw new ObjectDisposedException(nameof(ManualTimer));
                ChangeCount++;
                Due = dueTime == Timeout.InfiniteTimeSpan ? (long?)null
                    : _clock.Now + Math.Max(0, (long)Math.Ceiling(dueTime.TotalMilliseconds));
            }

            public void Dispose()
            {
                DisposeCount++;
                Disposed = true;
                Due = null;
            }
        }
    }
}
