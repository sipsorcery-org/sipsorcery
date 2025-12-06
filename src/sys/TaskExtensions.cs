using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery;

static class TaskExtensions
{
    public static async void Forget(this Task task, ILogger logger)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "An unhandled exception occurred in a fire-and-forget task.");
        }
    }
}
