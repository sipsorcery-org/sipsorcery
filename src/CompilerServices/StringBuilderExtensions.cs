using System;
using System.Text;

namespace SIPSorcery
{
    internal static class StringBuilderExtensions
    {
#if NETSTANDARD2_0|| NET462
        public static StringBuilder Append(this StringBuilder builder, ReadOnlySpan<char> value) => builder.Append(value.ToString());
#endif
    }
}
