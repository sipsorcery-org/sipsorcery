using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.FFmpeg
{
    public enum FfmpegLogLevelEnum
    {
        AV_LOG_PANIC = 0,
        AV_LOG_FATAL = 8,
        AV_LOG_ERROR = 16,
        AV_LOG_WARNING = 24,
        AV_LOG_INFO = 32,
        AV_LOG_VERBOSE = 40,
        AV_LOG_DEBUG = 48,
        AV_LOG_TRACE = 56,
    }

    public static class FFmpegInit
    {
        private static ILogger logger = NullLogger.Instance;
        private static bool registered = false;

        public static void Initialise(FfmpegLogLevelEnum? logLevel = null, String? libPath = null)
        {
            RegisterFFmpegBinaries(libPath);

            logger.LogInformation($"FFmpeg version info: {ffmpeg.av_version_info()}");

            if (logLevel.HasValue)
            {
                ffmpeg.av_log_set_level((int)logLevel.Value);
            }
        }

        internal static void RegisterFFmpegBinaries(String? libPath = null)
        {
            if (registered)
                return;

            if (libPath == null)
            {
                var current = Environment.CurrentDirectory;
                var probe = Path.Combine("FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86");
                while (current != null)
                {
                    var ffmpegBinaryPath = Path.Combine(current, probe);
                    if (Directory.Exists(ffmpegBinaryPath))
                    {
                        logger.LogInformation($"FFmpeg binaries found in: {ffmpegBinaryPath}");
                        ffmpeg.RootPath = ffmpegBinaryPath;
                        registered = true;

                        ffmpeg.avdevice_register_all();
                        return;
                    }

                    current = Directory.GetParent(current)?.FullName;
                }
            }
            else
            {
                if (Directory.Exists(libPath))
                {
                    logger.LogInformation($"FFmpeg binaries found in: {libPath}");
                    ffmpeg.RootPath = libPath;
                    registered = true;

                    ffmpeg.avdevice_register_all();
                    return;
                }
            }
        }

        public static unsafe string? av_strerror(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message;
        }

        public static int ThrowExceptionIfError(this int error)
        {
            if (error < 0)
            {
                throw new ApplicationException(av_strerror(error));
            }
            return error;
        }
    }

    public static class FFmpegConvert
    {
        public static AVCodecID GetAVCodecID(VideoCodecsEnum videoCodec)
        {
            var avCodecID = AVCodecID.AV_CODEC_ID_VP8;
            switch (videoCodec)
            {
                case VideoCodecsEnum.VP8:
                    avCodecID = AVCodecID.AV_CODEC_ID_VP8;
                    break;
                case VideoCodecsEnum.H264:
                    avCodecID = AVCodecID.AV_CODEC_ID_H264;
                    break;
                default:
                    throw new ApplicationException($"FFmpeg video source, selected video codec {videoCodec} is not supported.");
            }

            return avCodecID;
        }
    }
}
