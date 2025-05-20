using System;
using System.Threading.Tasks;

namespace SIPSorcery.Sys
{
    internal static class TaskExtensions
    {
        public static Task<T> WaitAsync<T>(this Task<T> task, TimeSpan? timeout)
            => timeout is { } timeoutValue
                ? task.WaitAsync(timeoutValue)
                : task;

        public static Task WaitAsync(this Task task, TimeSpan? timeout)
            => timeout is { } timeoutValue
                ? task.WaitAsync(timeoutValue)
                : task;

#if !NET6_0_OR_GREATER
        public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
        {
            if (task == await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false))
            {
                return await task; // Task completed within timeout
            }
            else
            {
                throw new TimeoutException($"The operation has timed out after {timeout}.");
            }
        }

        public static async Task WaitAsync(this Task task, TimeSpan timeout)
        {
            if (task == await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false))
            {
                await task; // Task completed within timeout
            }
            else
            {
                throw new TimeoutException($"The operation has timed out after {timeout}.");
            }
        }
#endif
    }
}
