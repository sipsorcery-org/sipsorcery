using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Sys;

internal static class TaskExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> WaitAsync<T>(this Task<T> task, TimeSpan? timeout)
        => timeout is { } timeoutValue
            ? task.WaitAsync(timeoutValue, CancellationToken.None)
            : task;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task WaitAsync(this Task task, TimeSpan? timeout)
        => timeout is { } timeoutValue
            ? task.WaitAsync(timeoutValue, CancellationToken.None)
            : task;
}
