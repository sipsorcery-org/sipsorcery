using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Sys;

internal static class ReadOnlySpanExtensions
{
    extension(global::System.ReadOnlySpan<char> value)
    {
        public bool IsEmptyOrWhiteSpace() => value.IsEmpty || value.IsWhiteSpace();
    }
}
