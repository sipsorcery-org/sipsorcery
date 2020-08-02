namespace ProjectCeilidh.PortAudio.Native
{
    internal enum PaStreamFlags : ulong
    {
        NoFlag = 0uL,
        ClipOff = 1uL,
        DitherOff = 2uL,
        NeverDropInput = 4uL,
        PrimeOutputBuffersUsingStreamCallback = 8uL,
        PlatformSpecificFlags = 0xFFFF0000uL
    }
}
