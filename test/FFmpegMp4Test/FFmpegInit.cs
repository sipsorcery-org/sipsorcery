using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;


namespace FFmpegMp4Test
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
        public static void Initialise(FfmpegLogLevelEnum? logLevel = null)
        {
            RegisterFFmpegBinaries();

            if (logLevel.HasValue)
            {
                ffmpeg.av_log_set_level((int)logLevel.Value);
            }
        }

        internal static void RegisterFFmpegBinaries()
        {
            var current = Environment.CurrentDirectory;
            var probe = Path.Combine("FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86");
            while (current != null)
            {
                var ffmpegBinaryPath = Path.Combine(current, probe);
                if (Directory.Exists(ffmpegBinaryPath))
                {
                    ffmpeg.RootPath = ffmpegBinaryPath;
                    return;
                }

                current = Directory.GetParent(current)?.FullName;
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
}
