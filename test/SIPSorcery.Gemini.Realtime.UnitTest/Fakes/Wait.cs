using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

public static class Wait
{
    public const int DEFAULT_TIMEOUT_MS = 10000;

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or the timeout expires. Returns whether it
    /// held, so callers can assert on it and get a meaningful failure rather than a hang.
    /// </summary>
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMs = DEFAULT_TIMEOUT_MS)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(5).ConfigureAwait(false);
        }

        return condition();
    }
}
