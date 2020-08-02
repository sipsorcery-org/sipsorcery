using System;
using System.Collections.Generic;
using ProjectCeilidh.PortAudio.Native;

namespace ProjectCeilidh.PortAudio
{
    public class PortAudioHostApi : IDisposable
    {
        /// <summary>
        /// A set of supported host APIs.
        /// Note: These MUST be disposed, or you risk breaking audio on the host until the next reboot
        /// </summary>
        public static IEnumerable<PortAudioHostApi> SupportedHostApis
        {
            get
            {
                using (PortAudioContext.EnterContext())
                {
                    var count = Native.PortAudio.Pa_GetHostApiCount();

                    for (PaHostApiIndex i = default; i < count; i++)
                        yield return PortAudioInstanceCache.GetHostApi(i);
                }
            }
        }

        /// <summary>
        /// The APIs human-readable name
        /// </summary>
        public string Name => ApiInfo.Name;

        /// <summary>
        /// The devices that belong to this API.
        /// Note: These MUST be disposed, or you risk breaking audio on the host until the next reboot
        /// </summary>
        public IEnumerable<PortAudioDevice> Devices
        {
            get
            {
                for (var i = 0; i < ApiInfo.DeviceCount; i++)
                {
                    var deviceIndex = Native.PortAudio.Pa_HostApiDeviceIndexToDeviceIndex(_apiIndex, i);
                    if (deviceIndex.TryGetErrorCode(out var err)) throw PortAudioException.GetException(err);
                    yield return PortAudioInstanceCache.GetPortAudioDevice(deviceIndex);
                }
            }
        }

        /// <summary>
        /// The default output device for this API
        /// Note: This MUST be disposed, or you risk breaking audio on the host until the next reboot
        /// </summary>
        public PortAudioDevice DefaultOutputDevice => PortAudioInstanceCache.GetPortAudioDevice(ApiInfo.DefaultOutputDevice);
        /// <summary>
        /// The default input device for this API
        /// Note: This MUST be disposed, or you risk breaking audio on the host until the next reboot
        /// </summary>
        public PortAudioDevice DefaultInputDevice => PortAudioInstanceCache.GetPortAudioDevice(ApiInfo.DefaultInputDevice);
        /// <summary>
        /// The type of host API this instance represents.
        /// </summary>
        public PortAudioHostApiType HostApiType => (PortAudioHostApiType) ApiInfo.Type;

        private ref PaHostApiInfo ApiInfo => ref Native.PortAudio.Pa_GetHostApiInfo(_apiIndex);
        private readonly PaHostApiIndex _apiIndex;

        internal PortAudioHostApi(PaHostApiIndex index)
        {
            PortAudioLifetimeRegistry.Register(this);

            _apiIndex = index;
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

        ~PortAudioHostApi()
        {
            ReleaseUnmanagedResources();
        }
    }
}
