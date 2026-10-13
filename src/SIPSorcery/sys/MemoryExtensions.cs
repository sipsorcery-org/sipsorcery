using System;
using System.Collections.Generic;

namespace SIPSorcery.Sys;

internal static class MemoryExtensions
{
    extension(ReadOnlySpan<char> value)
    {
        public List<string> SplitToList(char separator)
        {
            var result = new List<string>();

            foreach (var token in value.Split(separator))
            {
                result.Add(value[token].Trim().ToString());
            }

            return result;
        }

        public bool IsEmptyOrWhiteSpace() => value.IsEmpty || value.Trim().IsEmpty;
    }
}
