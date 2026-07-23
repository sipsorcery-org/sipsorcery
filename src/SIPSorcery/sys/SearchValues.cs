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
        WhiteSpaceChars
#if NET8_0_OR_GREATER
           { get; } =  global::System.Buffers.SearchValues.Create(
#else
            =>
#endif
            "\t\n\v\f\r\u0020\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000"
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
        NewLineChars
#if NET8_0_OR_GREATER
           { get; } =  global::System.Buffers.SearchValues.Create(
#else
            =>
#endif
            "\r\n\f\u0085\u2028\u2029"
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
