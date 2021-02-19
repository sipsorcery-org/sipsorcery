// package sctp -- go2cs converted at 2021 January 15 20:32:52 UTC
// import "sctp" ==> using sctp = go.sctp_package
// Original source: C:\Users\Mark\go\src\sctp\rtx_timer.go
//using math = go.math_package;
//using sync = go.sync_package;
//using time = go.time_package;
//using static go.builtin;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Net.Sctp
{
    public partial class rtoManager
    {
        private static readonly double rtoInitial = 3.0F * 1000; // msec
        private static readonly double rtoMin = 1.0F * 1000; // msec
        public static readonly double rtoMax = 60.0F * 1000; // msec
        private static readonly double rtoAlpha = 0.125F;
        private static readonly double rtoBeta = 0.25F;
        public static readonly uint maxInitRetrans = 8;
        public static readonly uint pathMaxRetrans = 5;
        public static readonly uint noMaxRetrans = 0;

        public double srtt;
        public double rttvar;
        public double rto;
        public bool noUpdate;
        public Mutex mutex = new Mutex();

        // newRTOManager creates a new rtoManager.
        public static rtoManager newRTOManager()
        {
            return new rtoManager()
            {
                rto = rtoInitial
            };
        }

        // setNewRTT takes a newly measured RTT then adjust the RTO in msec.
        public double setNewRTT(double rtt)
        {
            try
            {
                mutex.WaitOne();

                if (noUpdate)
                {
                    return srtt;
                }
                if (srtt == 0)
                {
                    // First measurement
                    srtt = rtt;
                    rttvar = rtt / 2;
                }
                else
                {
                    // Subsequent rtt measurement
                    rttvar = (1 - rtoBeta) * rttvar + rtoBeta * (Math.Abs(srtt - rtt));
                    srtt = (1 - rtoAlpha) * srtt + rtoAlpha * rtt;
                }
                rto = Math.Min(Math.Max(srtt + 4 * rttvar, rtoMin), rtoMax);
                return srtt;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        // getRTO simply returns the current RTO in msec.
        public double getRTO()
        {
            try
            {
                mutex.WaitOne();

                return this.rto;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        // reset resets the RTO variables to the initial values.
        private void reset()
        {
            try
            {
                mutex.WaitOne();
                if (this.noUpdate)
                {
                    return;
                }
                this.srtt = 0;
                this.rttvar = 0;
                this.rto = rtoInitial;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        // set RTO value for testing
        private void setRTO(double rto, bool noUpdate)
        {
            try
            {
                mutex.WaitOne();

                this.rto = rto;
                this.noUpdate = noUpdate;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }

    // rtxTimer provides the retnransmission timer conforms with RFC 4960 Sec 6.3.1
    public partial class rtxTimer
    {
        private uint nRtos;
        public int id;
        public IRtxTimerObserver observer;
        public uint maxRetrans;
        public stopTimerLoop stopFunc;
        public bool closed;
        public Mutex mutex = new Mutex();
        private CancellationTokenSource cancelToken;

        public delegate void stopTimerLoop();

        private System.Timers.Timer timer = new System.Timers.Timer();

        // newRTXTimer creates a new retransmission timer.
        // if maxRetrans is set to 0, it will keep retransmitting until stop() is called.
        // (it will never make onRetransmissionFailure() callback.
        public static rtxTimer newRTXTimer(int id, IRtxTimerObserver observer, uint maxRetrans)
        {
            return new rtxTimer()
            {
                id = id,
                observer = observer,
                maxRetrans = maxRetrans
            };
        }

        // start starts the timer.
        public bool start(double rto)
        {
            try
            {
                mutex.WaitOne();

                // this timer is already closed
                if (closed)
                {
                    return false;
                }

                // this is a noop if the timer is always running
                if (stopFunc != null)
                {
                    return false;
                }

                // Note: rto value is intentionally not capped by RTO.Min to allow
                // fast timeout for the tests. Non-test code should pass in the
                // rto generated by rtoManager getRTO() method which caps the
                // value at RTO.Min or at RTO.Max.
                nRtos = default;

                timer?.Stop();

                timer.Interval = calculateNextTimeout(rto, nRtos);
                timer.AutoReset = true;
                timer.Elapsed += Timer_Elapsed;

                stopFunc = () =>
                {
                    close();
                };

                timer.Start();

                return true;

            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            nRtos++;
            if (maxRetrans == 0 || nRtos <= maxRetrans)
            {
                observer.onRetransmissionTimeout(id, nRtos);
            }
            else
            {
                timer.Stop();
                observer.onRetransmissionFailure(id);
            }
        }

        // stop stops the timer.
        public void stop()
        {
            try
            {
                mutex.WaitOne();

                timer.Stop();

                if (stopFunc != null)
                {
                    stopFunc = null;
                }

            }
            finally
            {
                mutex.ReleaseMutex();
            }

        }

        // closes the timer. this is similar to stop() but subsequent start() call
        // will fail (the timer is no longer usable)
        public void close()
        {
            try
            {
                mutex.WaitOne();

                timer?.Dispose();
                cancelToken?.Cancel();
                closed = true;
                stopFunc = null;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        // isRunning tests if the timer is running.
        // Debug purpose only
        private bool isRunning()
        {
            try
            {
                mutex.WaitOne();

                return (stopFunc != null);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private int calculateNextTimeout(double rto, uint nRtos)
        {
            // RFC 4096 sec 6.3.3.  Handle T3-rtx Expiration
            //   E2)  For the destination address for which the timer expires, set RTO
            //        <- RTO * 2 ("back off the timer").  The maximum value discussed
            //        in rule C7 above (RTO.max) may be used to provide an upper bound
            //        to this doubling operation.
            if (nRtos < 31)
            {
                int m = 1 << (int)(nRtos);
                return Math.Min((int)rto * m, (int)rtoManager.rtoMax);
            }
            return (int)rtoManager.rtoMax;
        }
    }
}
