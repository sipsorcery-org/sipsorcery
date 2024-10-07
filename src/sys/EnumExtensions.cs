using System;

namespace SIPSorcery.Sys
{
    static class EnumExtensions
    {
        public static bool IsDefined<T>(this T value) where T : struct, Enum
        {
            return Array.IndexOf(EnumDefined<T>.Values, value) >= 0;
        }

        class EnumDefined<T> where T : struct, Enum
        {
            public static readonly T[] Values = (T[])Enum.GetValues(typeof(T));
        }
    }
}
