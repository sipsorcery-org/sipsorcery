using System;
using Microsoft.Extensions.Logging;

namespace SIPSorceryMedia.Windows
{
    internal static partial class WindowsMediaLoggingExtensions
    {
        [LoggerMessage(
            EventId = 1,
            EventName = "AudioCaptureRateAdjusted",
            Level = LogLevel.Debug,
            Message = "Windows audio end point adjusting capture rate from {oldRate} to {newRate}.")]
        public static partial void LogAudioCaptureRateAdjusted(this ILogger logger, int oldRate, int newRate);

        [LoggerMessage(
            EventId = 2,
            EventName = "AudioPlaybackRateAdjusted",
            Level = LogLevel.Debug,
            Message = "Windows audio end point adjusting playback rate from {oldRate} to {newRate}.")]
        public static partial void LogAudioPlaybackRateAdjusted(this ILogger logger, int oldRate, int newRate);

        [LoggerMessage(
            EventId = 3,
            EventName = "PlaybackDeviceInitFailed",
            Level = LogLevel.Warning,
            Message = "WindowsAudioEndPoint failed to initialise playback device.")]
        public static partial void LogPlaybackDeviceInitFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 4,
            EventName = "AudioInputDeviceIndexExceeded",
            Level = LogLevel.Warning,
            Message = "The requested audio input device index {AudioInDeviceIndex} exceeds the maximum index of {maxIndex}.")]
        public static partial void LogAudioInputDeviceIndexExceeded(this ILogger logger, int audioInDeviceIndex, int maxIndex);

        [LoggerMessage(
            EventId = 5,
            EventName = "NoAudioCaptureDevices",
            Level = LogLevel.Warning,
            Message = "No audio capture devices are available.")]
        public static partial void LogNoAudioCaptureDevices(this ILogger logger);

        [LoggerMessage(
            EventId = 6,
            EventName = "AudioCaptureDeviceInitFailed",
            Level = LogLevel.Warning,
            Message = "Failed to initialize audio capture device {DeviceIndex}.")]
        public static partial void LogAudioCaptureDeviceInitFailed(this ILogger logger, int deviceIndex, Exception exception);

        [LoggerMessage(
            EventId = 7,
            EventName = "VideoCaptureDeviceFormat",
            Level = LogLevel.Debug,
            Message = "Video Capture device {deviceName} format {Width}x{Height} {Fps:0.##}fps {PixelFormat}")]
        public static partial void LogVideoCaptureDeviceFormat(this ILogger logger, string deviceName, uint width, uint height, float fps, string pixelFormat);

        [LoggerMessage(
            EventId = 8,
            EventName = "VideoDeviceNotFound",
            Level = LogLevel.Warning,
            Message = "Could not find video capture device for specified ID {VideoDeviceID}, using default device.")]
        public static partial void LogVideoDeviceNotFound(this ILogger logger, string videoDeviceID);

        [LoggerMessage(
            EventId = 9,
            EventName = "VideoDeviceSelected",
            Level = LogLevel.Information,
            Message = "Video capture device {DeviceName} selected.")]
        public static partial void LogVideoDeviceSelected(this ILogger logger, string deviceName);

        [LoggerMessage(
            EventId = 10,
            EventName = "VideoFormatNotSupported",
            Level = LogLevel.Warning,
            Message = "The video capture device did not support the requested format (or better) {Width}x{Height} {Fps}fps. Using default mode.")]
        public static partial void LogVideoFormatNotSupported(this ILogger logger, uint width, uint height, uint fps);

        [LoggerMessage(
            EventId = 11,
            EventName = "VpxDecodeFailed",
            Level = LogLevel.Warning,
            Message = "VPX decode of video sample failed.")]
        public static partial void LogVpxDecodeFailed(this ILogger logger);
    }
}
