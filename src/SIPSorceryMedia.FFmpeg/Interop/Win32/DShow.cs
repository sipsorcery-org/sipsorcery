using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SIPSorceryMedia.FFmpeg.Interop.Win32
{
    internal class DShow
    {
        private static readonly string inputfmt = nameof(DShow).ToLower();

        private static readonly string[] DSHOW_CAMERA_FORMAT_LOG_OUTPUT = ["pixel_format"];

        private static List<Camera>? _cachedCameras;

        /// <summary>
        /// Gets all DirectShow camera and input devices.
        /// </summary>
        /// <returns>a <see cref="List{T}"/> of <see cref="Camera"/>s found on the system.</returns>
        public static unsafe List<Camera>? GetCameraDevices()
        {
            _cachedCameras ??= [];

            AVDeviceInfoList* dvls = null;
            ffmpeg.avdevice_list_input_sources(ffmpeg.av_find_input_format(inputfmt), null, null, &dvls);

            if (dvls is not null)
            {
                var devNames = new Span<IntPtr>(dvls->devices, dvls->nb_devices).ToArray()
                    .Where(dv => new Span<AVMediaType>(((AVDeviceInfo*)dv)->media_types, ((AVDeviceInfo*)dv)->nb_media_types)
                                    .ToArray().Any(t => t == AVMediaType.AVMEDIA_TYPE_VIDEO))
                    .Select(dv => Marshal.PtrToStringAnsi((IntPtr)((AVDeviceInfo*)dv)->device_description) ?? string.Empty)
                    .Distinct().Except([string.Empty])
                    .Except(_cachedCameras.Select(c => c.Name))
                    .ToArray();

                ffmpeg.avdevice_free_list_devices(&dvls);

                if (GetDShowCameras(devNames) is var add && add is not null)
                    _cachedCameras.AddRange(add);

                return _cachedCameras;
            }
            return null;
        }

        private static List<Camera>? GetDShowCameras(string[]? videoDevs)
        {
            return videoDevs?
                .Select(cam =>
                {
                    var opts = GetDShowLogsForDevice(cam)?
                        .Split(['\n'], StringSplitOptions.RemoveEmptyEntries)
                        .Where(s => s.Contains('='))
                        .Select(optline =>
                            optline.Split(["min", "max"], StringSplitOptions.RemoveEmptyEntries)
                            .Select((sections, i) => sections
                                .Split([' '], StringSplitOptions.RemoveEmptyEntries)
                                .Where(s => s.Contains('='))
                                .Select(opt =>
                                {
                                    var kp = opt.Split(['='], StringSplitOptions.RemoveEmptyEntries);

                                    return string.IsNullOrEmpty(kp.ElementAtOrDefault(0))
                                        ?
                                        new KeyValuePair<string, string>()
                                        :
                                        new KeyValuePair<string, string>(
                                            value: kp.ElementAtOrDefault(1) ?? string.Empty,
                                            key: i switch
                                            {
                                                1 => "min_",
                                                2 => "max_",
                                                _ => string.Empty
                                            }
                                            + kp.ElementAtOrDefault(0)!
                                        );
                                })
                                .Where(kp => !string.IsNullOrEmpty(kp.Key))
                            )
                            .SelectMany(d => d)
                            .ToDictionary(g => g.Key, g => g.Value)
                        );

                    return new Camera()
                    {
                        Name = cam,
                        Path = $"video={cam}",
                        AvailableOptions = opts?.ToList(),
                        AvailableFormats = opts?
                            .Where(d => DSHOW_CAMERA_FORMAT_LOG_OUTPUT.Any(d.ContainsKey))
                            .Select(d =>
                            {
                                var pixfmt = d.Keys.First(DSHOW_CAMERA_FORMAT_LOG_OUTPUT.Contains);
                                var min_s = d["min_s"].Split(['x'], StringSplitOptions.RemoveEmptyEntries);
                                var max_s = d["max_s"].Split(['x'], StringSplitOptions.RemoveEmptyEntries);

                                return new[]
                                {
                                    new Camera.CameraFormat()
                                    {
                                        PixelFormat = ffmpeg.av_get_pix_fmt(d[pixfmt]),
                                        Width = int.Parse(min_s[0]),
                                        Height = int.Parse(min_s[1]),
                                        FPS = double.Parse(d["min_fps"])
                                    },
                                    new Camera.CameraFormat()
                                    {
                                        PixelFormat = ffmpeg.av_get_pix_fmt(d[pixfmt]),
                                        Width = int.Parse(max_s[0]),
                                        Height = int.Parse(max_s[1]),
                                        FPS = double.Parse(d["max_fps"])
                                    }
                                }
                                .Distinct();
                            })
                            .SelectMany(f => f).Distinct().ToList()
                    };
                })
                .ToList();
        }

        private static unsafe string? GetDShowLogsForDevice(string name)
        {
            AVInputFormat* avInputFormat = ffmpeg.av_find_input_format(inputfmt);
            AVFormatContext* pFormatCtx = ffmpeg.avformat_alloc_context();
            AVDictionary* options = null;

            ffmpeg.av_dict_set(&options, "list_options", "true", 0);

            UseSpecificLogCallback();

            ffmpeg.avformat_open_input(&pFormatCtx, $"video={name}", avInputFormat, &options);
            ffmpeg.avformat_close_input(&pFormatCtx);
            ffmpeg.avformat_free_context(pFormatCtx);

            // We no more need to use temporarily a specific callback to log FFmpeg entries
            FFmpegInit.UseDefaultLogCallback();
            // returns logs 
            return storedLogs;
        }

        static string? storedLogs;

        private static unsafe void UseSpecificLogCallback()
        {
            // We clear previous stored logs
            if (!string.IsNullOrEmpty(storedLogs))
                storedLogs = string.Empty;

            av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level()) return;

                var lineSize = 4096;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 0;
                
                var num = ffmpeg.av_log_format_line2(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);

                storedLogs += Encoding.Default.GetString(lineBuffer, num);
            };
            ffmpeg.av_log_set_callback(logCallback);
        }
    }
}
