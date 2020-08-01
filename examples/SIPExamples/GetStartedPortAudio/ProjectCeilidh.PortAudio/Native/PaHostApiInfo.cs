using System;
using System.Runtime.InteropServices;
#pragma warning disable 649

namespace ProjectCeilidh.PortAudio.Native
{
    /// <summary>
    /// A structure containing information about a particular host API.
    /// </summary>
    internal readonly struct PaHostApiInfo
    {
        public string Name => _name == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(_name);

        public int StructVersion { get; }
        public PaHostApiTypeId Type { get; }
        private readonly IntPtr _name;
        public int DeviceCount { get; }
        public PaDeviceIndex DefaultInputDevice { get; }
        public PaDeviceIndex DefaultOutputDevice { get; }
    }
}
