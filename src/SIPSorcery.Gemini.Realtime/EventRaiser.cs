using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// Every event in this library is ultimately raised from the single background WebSocket receive
/// loop. An exception thrown by a consumer's handler would unwind that loop and silently end the
/// Gemini session while the socket is still open, so all handler invocations go through these
/// helpers: a faulty handler is logged and the session carries on.
///
/// Handlers are invoked one at a time off the delegate's invocation list rather than through a
/// single multicast call, so one subscriber throwing does not stop the remaining subscribers from
/// being notified.
/// </summary>
internal static class EventRaiser
{
    internal static void Raise(ILogger logger, Action? handler, string eventName)
    {
        if (handler == null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action)subscriber)();
            }
            catch (Exception ex)
            {
                LogHandlerFailure(logger, ex, eventName);
            }
        }
    }

    internal static void Raise<T>(ILogger logger, Action<T>? handler, T arg, string eventName)
    {
        if (handler == null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<T>)subscriber)(arg);
            }
            catch (Exception ex)
            {
                LogHandlerFailure(logger, ex, eventName);
            }
        }
    }

    internal static void Raise<T1, T2>(ILogger logger, Action<T1, T2>? handler, T1 arg1, T2 arg2, string eventName)
    {
        if (handler == null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<T1, T2>)subscriber)(arg1, arg2);
            }
            catch (Exception ex)
            {
                LogHandlerFailure(logger, ex, eventName);
            }
        }
    }

    private static void LogHandlerFailure(ILogger logger, Exception ex, string eventName)
        => logger.LogError(ex, "A {EventName} event handler threw an exception; it has been suppressed to keep the Gemini Live session alive.", eventName);
}
