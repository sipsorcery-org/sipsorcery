/*
 * Copyright 2017 pi.pe gmbh .
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */
// Modified by Andrés Leone Gámez


using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

/**
 *
 * @author tim
 * Assumption is that timers _always_ go off - it is up to the 
 * runnable to decide if something needs to be done or not.
 */
namespace SIPSorcery.Net.Sctp
{
    internal class SimpleSCTPTimer : SCTPTimer
    {
        private static TimerScheduler _timer = new TimerScheduler();
        public void setRunnable(IRunnable r, long at)
        {
            _timer.Schedule(r, at);
        }
    }

    /**
    * interface that hides the threading implementation.
    * @author Westhawk Ltd<thp@westhawk.co.uk>
    */
    public interface SCTPTimer
    {
        void setRunnable(IRunnable r, long at);
    }

    public class Runnable : IRunnable
    {
        public bool IsFinished { get; }
        public Action RunAction { get; set; }
        public virtual void run()
        {
            RunAction?.Invoke();
        }
    }

    public interface IRunnable
    {
        bool IsFinished { get; }
        void run();
    }

    public class TimerTask : Runnable
    {

    }

    public class TimerScheduler : IDisposable
    {
        private bool IsDisposed { get; set; }
        private ConcurrentDictionary<IRunnable,TimerSchedule> _timerSchedules;
        private static ILogger logger = Log.Logger;
        private object myLock = new object();

        public TimerScheduler()
        {
            _timerSchedules = new ConcurrentDictionary<IRunnable, TimerSchedule>();
            Task.Run(Schedule);
        }

        public void Schedule(IRunnable timerTask, long at)
        {
            var tt = new TimerSchedule()
            {
                Runnable = timerTask,
                At = TimeExtension.CurrentTimeMillis() + at
            };
            _timerSchedules.AddOrUpdate(timerTask, tt, (a, b) => tt);
        }

        public void Schedule()
        {
            try
            {
                while (!this.IsDisposed)
                {
                    var schedules = new TimerSchedule[0];

                    lock (myLock)
                    {
                        schedules = _timerSchedules.Values.ToArray();
                    }

                    foreach (var schedule in schedules)
                    {
                        if (schedule.At < TimeExtension.CurrentTimeMillis())
                        {
                            _timerSchedules.AddOrUpdate(schedule.Runnable, schedule, (a, b) =>
                            {
                                if (b.At > schedule.At)
                                {
                                    return b;
                                }
                                return schedule;
                            });
                            continue;
                        }

                        try
                        {
                            schedule.Runnable.run();
                            Thread.Sleep(100);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Runnable error");
                        }
                    }

                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "ScheduleError");
            }
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        private struct TimerSchedule
        {
            public IRunnable Runnable;
            public long At;
        }
    }
}
