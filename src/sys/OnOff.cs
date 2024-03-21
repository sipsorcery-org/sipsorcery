using System.Threading;

namespace SIPSorcery.Sys;

/// <summary>A thread-safe struct that represents an on/off state.</summary>
struct OnOff
{
    int on;

    public bool TryTurnOn() => Interlocked.CompareExchange(ref on, 1, comparand: 0) == 0;
    public bool TryTurnOff() => Interlocked.CompareExchange(ref on, 0, comparand: 1) == 1;
    public bool IsOn() => Interlocked.CompareExchange(ref on, 0, 0) == 1;
}
