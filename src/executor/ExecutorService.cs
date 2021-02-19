using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net.Sctp;

namespace SIPSorcery.Executor
{
    public class ExecutorService : IDisposable
    {
        public bool IsDisposed;
        private object myLock = new object();
        private BlockingCollection<SCTPMessage> messages = new BlockingCollection<SCTPMessage>();
        public ExecutorService()
        {
            Task.Factory.StartNew(ProcessQueue, TaskCreationOptions.LongRunning);
        }
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            IsDisposed = true;

            lock (myLock)
            {
                messages.CompleteAdding();
                Monitor.PulseAll(myLock);
            }
        }

        private void ProcessQueue()
        {
            while (!IsDisposed)
            {
                try
                {
                    while (!messages.IsAddingCompleted && messages.TryTake(out var msg, 10))
                    {
                        msg.run();
                    }
                    lock (myLock)
                    {
                        Monitor.Wait(myLock, 100);
                    }
                }
                catch { }
            }
        }

        internal void execute(SCTPMessage message)
        {
            lock (myLock)
            {
                messages.Add(message);
                Monitor.PulseAll(myLock);
            }
        }
    }
}
