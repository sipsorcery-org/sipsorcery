using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.sys
{
    internal static class SearchValuesExtensions
    {
        public static ReadOnlySpan<char> DigitChars => "0123456789".AsSpan();
        public static ReadOnlySpan<char> WhitespaceChars => " \t\r\n\f\v".AsSpan();
    }
}
