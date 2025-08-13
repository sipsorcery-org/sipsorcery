using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if !NET6_0_OR_GREATER
namespace SIPSorcery.Sys
{
    internal sealed class PeriodicTimer : IDisposable
    {

        private readonly TimeSpan _period;
        private readonly Timer _timer;
        private readonly object _lock = new object();
        private TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
        private bool _disposed;

        public PeriodicTimer(TimeSpan period)
        {
            if (period <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(period));
            }

            _period = period;
            _timer = new Timer(OnTimerTick, null, _period, _period);
        }

        private void OnTimerTick(object state)
        {
            lock (_lock)
            {
                if (_disposed || _tcs.Task.IsCompleted)
                {
                    return;
                }

                _tcs.TrySetResult(true);
            }
        }

        public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return new ValueTask<bool>(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));
                }

                var currentTcs = _tcs;

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() =>
                    {
                        lock (_lock)
                        {
                            if (!_disposed && !currentTcs.Task.IsCompleted)
                            {
                                currentTcs.TrySetCanceled();
                            }
                        }
                    });
                }

                _tcs = new TaskCompletionSource<bool>();
                return new ValueTask<bool>(currentTcs.Task);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timer.Dispose();
                _tcs.TrySetResult(false);
            }
        }
    }
}
#endif
