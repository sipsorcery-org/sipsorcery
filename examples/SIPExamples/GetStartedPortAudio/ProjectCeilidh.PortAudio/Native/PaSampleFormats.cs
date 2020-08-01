using System;

namespace ProjectCeilidh.PortAudio.Native
{
    [Flags]
    internal enum PaSampleFormats : ulong
    {
        Float32 = 0x1,
        Int32 = 0x2,
        Int24 = 0x4,
        Int16 = 0x8,
        Int8 = 0x10,
        UInt8 = 0x20,
        CustomFormat = 0x10000,
        NonInterleaved = 0x80000000
    }
}
