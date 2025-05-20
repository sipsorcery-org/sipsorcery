using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Sys;

internal static class ReadOnlySpanExtensions
{
    extension(global::System.ReadOnlySpan<char> value)
    {
        public bool IsEmptyWhiteSpace() => value.IsEmpty || value.IndexOfAnyExcept(SearchValues.WhiteSpaceChars) < 0;
    }
}
