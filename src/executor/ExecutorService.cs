using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net.Sctp;

namespace SIPSorcery.executor
{
    public class ExecutorService : IDisposable
    {
        private SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        public void Dispose()
        {
            _semaphore?.Dispose();
        }

        internal void execute(SCTPMessage message)
        {
            Task.Run(async () =>
            {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    message.run();
                }
                finally
                {
                    _semaphore.Release();
                }
            });
        }
    }
}
