using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SIPSorcery.Sys;

internal static class ArgumentExceptionExtensions
{
    extension(global::System.ArgumentException ex)
    {
#if NET60_OR_GREATER
        [StackTraceHidden]
#endif
        public static void ThrowIfEmpty<T>(ReadOnlySpan<T> argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument.Length == 0)
            {
                ThrowArgumentException("Argument cannot be empty.", paramName);
            }
        }

#if NET60_OR_GREATER
        [StackTraceHidden]
#endif
        public static void ThrowIfEmptyWhiteSpace(ReadOnlySpan<char> argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument.IsEmptyOrWhiteSpace())
            {
                ThrowArgumentException("The value cannot be empty or composed entirely of whitespace.", paramName);
            }
        }

        [DoesNotReturn]
        private static void ThrowArgumentException(string message, string paramName) => throw new ArgumentException(message, paramName);
    }
}
