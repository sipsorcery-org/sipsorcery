using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net.Sctp;

namespace SIPSorcery.executor
{
    public class ExecutorService : IDisposable
    {
        public bool IsDisposed;
        private BlockingCollection<SCTPMessage> messages = new BlockingCollection<SCTPMessage>();
        public ExecutorService()
        {
            Task.Run(ProcessQueue);
        }
        public void Dispose()
        {
            IsDisposed = true;
        }

        private void ProcessQueue()
        {
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
        }
    }
}
