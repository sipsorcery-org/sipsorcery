using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCeilidh.PortAudio
{
    public static class Extensions
    {
        public static async Task<bool> WaitAsyncCancellable(this SemaphoreSlim evt, CancellationToken token)
        {
            try
            {
                await evt.WaitAsync(token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
