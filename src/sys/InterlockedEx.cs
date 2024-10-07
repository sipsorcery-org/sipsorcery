using System.Threading;

namespace SIPSorcery.Sys;

static class InterlockedEx
{
    public static int Read(ref int location) => Interlocked.CompareExchange(ref location, 0, 0);
    public unsafe static uint CompareExchange(ref uint location, uint value, uint comparand)
#if NET6_0_OR_GREATER
        => Interlocked.CompareExchange(ref location, value: value, comparand: comparand);
#else
    {
        fixed (uint* ptr = &location)
        {
            return unchecked((uint)Interlocked.CompareExchange(ref *(int*)ptr, (int)value, (int)comparand));
        }
    }
#endif
    public unsafe static uint Read(ref uint location) => CompareExchange(ref location, 0, 0);
}
