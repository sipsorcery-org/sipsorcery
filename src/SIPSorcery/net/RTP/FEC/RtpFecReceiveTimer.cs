using System;
using System.Threading;

namespace SIPSorcery.Net
{
    /// <summary>One rearmable, one-shot timer owned by a FEC receive buffer.</summary>
    internal interface IRtpFecReceiveTimer : IDisposable
    {
        void Change(TimeSpan dueTime);
    }

    internal sealed class RtpFecReceiveTimer : IRtpFecReceiveTimer
    {
        private readonly Timer _timer;

        internal RtpFecReceiveTimer(Action callback)
        {
            _timer = new Timer(_ => callback(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Change(TimeSpan dueTime) => _timer.Change(dueTime, Timeout.InfiniteTimeSpan);

        public void Dispose() => _timer.Dispose();
    }
}
