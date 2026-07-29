using System;
using SIPSorceryMedia.Abstractions;

#if WINDOWS
using SIPSorceryMedia.Windows;
#elif MACOS
using SIPSorceryMedia.MacOS;
#endif

namespace SIPSorceryMedia.Platform
{
    public static class DefaultAudioEndPointFactory
    {
        public static IAudioEndPoint Create(
            IAudioEncoder audioEncoder,
            bool disableSource = false,
            bool disableSink = false)
        {
#if WINDOWS
            return new WindowsAudioEndPoint(
                audioEncoder,
                disableSource: disableSource,
                disableSink: disableSink);
#elif MACOS
            return new MacAudioEndPoint(
                audioEncoder,
                disableSource: disableSource,
                disableSink: disableSink);
#else
            throw new PlatformNotSupportedException(
                "No default audio endpoint is available for this platform.");
#endif
        }
    }
}