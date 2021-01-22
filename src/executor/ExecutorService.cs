using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net.Sctp;

namespace SIPSorcery.executor
{
    public class ExecutorService : IDisposable
    {
        public bool IsDisposed;
        private BlockingCollection<SCTPMessage> messages = new BlockingCollection<SCTPMessage>();
        private ManualResetEvent evt = new ManualResetEvent(false);
        public ExecutorService()
        {
            Task.Run(ProcessQueue);
        }
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            evt.Set();
            evt?.Dispose();
            messages.CompleteAdding();
        }

        private void ProcessQueue()
        {
            evt.WaitOne();
            while (!IsDisposed)
            {
                foreach (var msg in messages.GetConsumingEnumerable())
                {
                    msg.run();
                }
            }
        }

        internal void execute(SCTPMessage message)
        {
            messages.Add(message);
            evt.Set();
        }
    }
}
