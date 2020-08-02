using System;
using System.Collections.Concurrent;
using ProjectCeilidh.PortAudio.Native;

namespace ProjectCeilidh.PortAudio
{
    internal static class PortAudioInstanceCache
    {
        private static readonly ConcurrentDictionary<PaDeviceIndex, WeakReference<PortAudioDevice>> DeviceCache =
            new ConcurrentDictionary<PaDeviceIndex, WeakReference<PortAudioDevice>>();

        private static readonly ConcurrentDictionary<PaHostApiIndex, WeakReference<PortAudioHostApi>> ApiCache =
            new ConcurrentDictionary<PaHostApiIndex, WeakReference<PortAudioHostApi>>();
        
        public static PortAudioDevice GetPortAudioDevice(PaDeviceIndex index)
        {
            if (index.TryGetErrorCode(out var err)) throw PortAudioException.GetException(err);
            
            if (DeviceCache.TryGetValue(index, out var reference) && reference.TryGetTarget(out var target))
                return target;
            
            var device = new PortAudioDevice(index);
            DeviceCache[index] = new WeakReference<PortAudioDevice>(device);
            return device;
        }

        public static PortAudioHostApi GetHostApi(PaHostApiIndex index)
        {
            if (index.TryGetErrorCode(out var err)) throw PortAudioException.GetException(err);
            
            if (ApiCache.TryGetValue(index, out var reference) && reference.TryGetTarget(out var target))
                return target;
            
            var api = new PortAudioHostApi(index);
            ApiCache[index] = new WeakReference<PortAudioHostApi>(api);
            return api;
        }

        public static void ClearCache()
        {
            DeviceCache.Clear();
            ApiCache.Clear();
        }
    }
}