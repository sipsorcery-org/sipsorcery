using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.PortAudio.Native
{
    /// <summary>
    /// A structure providing information and capabilities of PortAudio devices.
    /// Devices may support input, output or both input and output.
    /// </summary>
    internal readonly struct PaDeviceInfo
    {
        public string Name => _name == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(_name);

        public int StructVersion { get; }
        private IntPtr _name { get; }
        public PaHostApiIndex HostApiIndex { get; }
        public int MaxInputChannels { get; }
        public int MaxOutputChannels { get; }
        /// <summary>Default latency values for interactive peformance.</summary>
        public PaTime DefaultLowInputLatency { get; }
        public PaTime DefaultLowOutputLatency { get; }
        /// <summary>Default latency values for robust non-interactive applications (eg. playing sound files).</summary>
        public PaTime DefaultHighInputLatency { get; }
        public PaTime DefaultHighOutputLatency { get; }
        public double DefaultSampleRate { get; }
    }
}
