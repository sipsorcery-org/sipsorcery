using System;

namespace Microsoft.Extensions.Logging
{
    public static class LoggerExtensions
    {
        public static void LogError(this ILogger logger, Exception ex, string message = null, params object[] args)
        {
            logger.LogError(default, ex, message, args);
        }
    }
}