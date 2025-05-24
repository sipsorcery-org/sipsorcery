using System;
using System.Text;

namespace SIPSorcery;

internal static class MemoryExtensions
{
    public static string ToLowerString(this ReadOnlySpan<char> span)
    {
        var stringBuilder = new StringBuilder(span.Length);

        foreach (var c in span)
        {
            stringBuilder.Append(char.ToLower(c));
        }

        return stringBuilder.ToString();
    }
}
