using System;

namespace ProjectCeilidh.PortAudio.Native
{
    internal struct PaStreamParameters
    {
        public PaDeviceIndex DeviceIndex { get; set; }
        public int ChannelCount { get; set; }
        public PaSampleFormats SampleFormats { get; set; }
        public PaTime SuggestedLatency { get; set; }
        public IntPtr HostApiSpecificStreamInfo { get; set; }
    }
}
