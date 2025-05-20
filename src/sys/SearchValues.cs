using System;

namespace SIPSorcery.Sys;

internal static class SearchValues
{
    public static
#if NET8_0_OR_GREATER
        global::System.Buffers.SearchValues<char>
#else
        ReadOnlySpan<char>
#endif
        DigitChars
#if NET8_0_OR_GREATER
            { get; } =  global::System.Buffers.SearchValues.Create(
#else
            =>
#endif
            "0123456789"
#if NET8_0_OR_GREATER
            )
#else
            .AsSpan()
#endif
            ;

    public static
#if NET8_0_OR_GREATER
        global::System.Buffers.SearchValues<char>
#else
        ReadOnlySpan<char>
#endif
        WhitespaceChars
#if NET8_0_OR_GREATER
           { get; } =  global::System.Buffers.SearchValues.Create(
#else
            =>
#endif
            " \t\r\n\f\v"
#if NET8_0_OR_GREATER
            )
#else
            .AsSpan()
#endif
            ;

    public static
#if NET8_0_OR_GREATER
        global::System.Buffers.SearchValues<char>
#else
        ReadOnlySpan<char>
#endif
        InvalidHostNameChars
#if NET8_0_OR_GREATER
            { get; } = global::System.Buffers.SearchValues.Create(
#else
            =>
#endif
            " \t\r\n\f\v/:"
#if NET8_0_OR_GREATER
            )
#else
            .AsSpan()
#endif
            ;
}
