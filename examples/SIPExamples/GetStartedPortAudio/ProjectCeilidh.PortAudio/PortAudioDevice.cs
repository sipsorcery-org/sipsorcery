using System;
using ProjectCeilidh.PortAudio.Native;

namespace ProjectCeilidh.PortAudio
{
    /// <summary>
    /// Represents a PortAudio multimedia device
    /// </summary>
    /// <inheritdoc />
    public class PortAudioDevice : IDisposable
    {
        /// <summary>
        /// The device's human-readable name
        /// </summary>
        public string Name => DeviceInfo.Name;
        /// <summary>
        /// The maximum number of channels this device can capture.
        /// </summary>
        public int MaxInputChannels => DeviceInfo.MaxInputChannels;
        /// <summary>
        /// The maximum number of channels the output device can render.
        /// </summary>
        public int MaxOutputChannels => DeviceInfo.MaxOutputChannels;
        /// <summary>
        /// The default render latency time for non-critical applications
        /// </summary>
        public TimeSpan DefaultHighOutputLatency => DeviceInfo.DefaultHighOutputLatency.Value;
        /// <summary>
        /// The default render latency time for critical applications
        /// </summary>
        public TimeSpan DefaultLowOutputLatency => DeviceInfo.DefaultLowOutputLatency.Value;
        /// <summary>
        /// The default capture latency time for non-critical applications
        /// </summary>
        public TimeSpan DefaultHighInputLatency => DeviceInfo.DefaultHighInputLatency.Value;
        /// <summary>
        /// The default capture latency for critical applications
        /// </summary>
        public TimeSpan DefaultLowInputLatency => DeviceInfo.DefaultLowInputLatency.Value;
        /// <summary>
        /// The default sample rate of this audio device
        /// </summary>
        public double DefaultSampleRate => DeviceInfo.DefaultSampleRate;
        /// <summary>
        /// The host API that this device is attached to
        /// Note: This MUST be disposed, or you risk breaking audio on the host until the next reboot
        /// </summary>
        public PortAudioHostApi HostApi => PortAudioInstanceCache.GetHostApi(DeviceInfo.HostApiIndex);

        internal PaDeviceIndex DeviceIndex { get; }

        private ref PaDeviceInfo DeviceInfo => ref Native.PortAudio.Pa_GetDeviceInfo(DeviceIndex);

        internal PortAudioDevice(PaDeviceIndex deviceIndex)
        {
            PortAudioLifetimeRegistry.Register(this);

            DeviceIndex = deviceIndex;
        }

        /// <summary>
        /// Determine if the device will support a given PCM format
        /// </summary>
        /// <param name="sampleFormat">The data format for PCM samples</param>
        /// <param name="channels">The number of channels</param>
        /// <param name="sampleRate">The number of frames/second the device will render/capture</param>
        /// <param name="suggestedLatency">The latency the device will attempt to match</param>
        /// <param name="asOutput">Marks if the device should be opened as an input or an output</param>
        /// <returns>True if the device supports this format, false otherwise</returns>
        public unsafe bool SupportsFormat(PortAudioSampleFormat sampleFormat, int channels, double sampleRate, TimeSpan suggestedLatency, bool asOutput)
        {
            PaStreamParameters* inputParams = default;
            PaStreamParameters* outputParams = default;

            var param = new PaStreamParameters
            {
                DeviceIndex = DeviceIndex,
                ChannelCount = channels,
                HostApiSpecificStreamInfo = IntPtr.Zero,
                SampleFormats = sampleFormat.SampleFormat,
                SuggestedLatency = new PaTime(suggestedLatency)
            };

            if (asOutput)
                outputParams = &param;
            else
                inputParams = &param;

            return Native.PortAudio.Pa_IsFormatSupported(inputParams, outputParams, sampleRate) >= PaErrorCode.NoError;
        }

        private void ReleaseUnmanagedResources()
        {
            PortAudioLifetimeRegistry.UnRegister(this);
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~PortAudioDevice()
        {
            ReleaseUnmanagedResources();
        }
    }
}
