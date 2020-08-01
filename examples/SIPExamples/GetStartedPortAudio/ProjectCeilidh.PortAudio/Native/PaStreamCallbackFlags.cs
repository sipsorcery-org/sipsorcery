using System;

namespace ProjectCeilidh.PortAudio.Native
{
    [Flags]
    internal enum PaStreamCallbackFlags : ulong
    {
        InputUnderflow = 1uL,
        InputOverflow = 2uL,
        OutputUnderflow = 4uL,
        OutputOverflow = 8uL,
        PrimingOutput = 0x10
    }
}
