using System;

namespace SIPSorcery.Sys
{
    static class MemoryExtensions
    {
        public static unsafe string ToString(this ReadOnlySpan<byte> buffer, System.Text.Encoding encoding)
        {
            fixed (byte* ptr = buffer)
            {
                return encoding.GetString(ptr, buffer.Length);
            }
        }
    }
}
