using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net.Sctp
{
    public class ackTimer
    {
        private static ILogger logger = Log.Logger;

        private static readonly TimeSpan ackInterval = TimeSpan.FromMilliseconds(200);

        private IAckTimerObserver _ackTimerObserver;
        private TimeSpan interval;
        private System.Timers.Timer _timer;
        private object myLock = new object();
        // newAckTimer creates a new acknowledgement timer used to enable delayed ack.
        public static ackTimer newAckTimer(IAckTimerObserver observer)
        {
            return new ackTimer()
            {
                _ackTimerObserver = observer,
                interval = ackInterval
            };
        }
        public void start()
        {
            _timer = new System.Timers.Timer();
            _timer.AutoReset = false;
            _timer.Interval = interval.TotalMilliseconds;
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_timer == null)
            {
                return;
            }

            try
            {
                _ackTimerObserver?.onAckTimeout();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
            }

            lock (myLock)
            {
                if (_timer == null)
                {
                    return;
                }
                _timer.Start();
            }
        }

        public void stop()
        {
            lock (myLock)
            {
                _timer?.Dispose();
                _timer = null;
                _ackTimerObserver?.onAckTimeout();
            }
        }

        public void close()
        {
            stop();
        }

        public bool isRunning()
        {
            return _timer == null;
        }
    }

    // ackTimerObserver is the inteface to an ack timer observer.
    public interface IAckTimerObserver
    {
        void onAckTimeout();
    }
}
