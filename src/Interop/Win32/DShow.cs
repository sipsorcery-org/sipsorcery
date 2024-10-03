using DirectShowLib;
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg.Interop.Win32
{
    internal class DShow
    {
        const string inputfmt = nameof(DShow);

        static private string[] DSHOW_VIDEO_DEVICE_LOG_OUTPUT = ["(video)", "camera", "webcam"];
        static private string DSHOW_AUDIO_DEVICE_LOG_OUTPUT = "(audio)";

        static private string[] DSHOW_CAMERA_FORMAT_LOG_OUTPUT = ["pixel_format"];

        /// <summary>
        /// Gets all DirectShow camera and input devices.
        /// </summary>
        /// <remarks>This includes FFmpeg and falls back to DirectShowLib.</remarks>
        /// <returns>a <see cref="List{T}"/> of <see cref="Camera"/>s found on the system.</returns>
        public static List<Camera>? GetCameraDevices()
        {
            var ffmpegcams = ParseDShowLogsForCameras(GetDShowLogsForDevice());
            // FFmpeg doesn't implement avdevice_list_input_sources() for the DShow input format yet.
            // Get DShowLib cameras in case of something wrong.
            var dshowcams = GetDShowLibCameras();

            return (ffmpegcams?.Union(dshowcams ?? Enumerable.Empty<Camera>()) ?? dshowcams)?.ToList();
        }

        private static List<Camera>? ParseDShowLogsForCameras(string? logs)
        {
            return logs?.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Where(logline => DSHOW_VIDEO_DEVICE_LOG_OUTPUT.Any(logline.ToLower().Contains))
                .Select(splitline =>
                {
                    var cam = splitline.Split(['"'], StringSplitOptions.RemoveEmptyEntries).First();

                    var opts = GetDShowLogsForDevice(cam)?
                        .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                        .Where(s => s.Contains("="))
                        .Select(optline =>
                            optline.Split(["min", "max"], StringSplitOptions.RemoveEmptyEntries)
                            .Select((sections, i) => sections
                                .Split([' '], StringSplitOptions.RemoveEmptyEntries)
                                .Where(s => s.Contains("="))
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
                            .SelectMany(f => f)
                            .ToList()
                    };

                })
                .ToList();
        }

        private static unsafe string? GetDShowLogsForDevice(string? name = null)
        {
            AVInputFormat* avInputFormat = ffmpeg.av_find_input_format(inputfmt.ToLower());
            AVFormatContext* pFormatCtx = ffmpeg.avformat_alloc_context();
            AVDictionary* options = null;

            ffmpeg.av_dict_set(&options, string.IsNullOrEmpty(name) ? "list_devices" : "list_options", "true", 0);

            UseSpecificLogCallback();

            ffmpeg.avformat_open_input(&pFormatCtx, string.IsNullOrEmpty(name) ? null : $"video={name}", avInputFormat, &options); // Here nb is < 0 ... But we have anyway an output from av_log which can be parsed ...
            ffmpeg.avformat_close_input(&pFormatCtx);

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

        private static List<Camera>? GetDShowLibCameras()
        {
            List<Camera>? result = null;
            var dsDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            for (int i = 0; i < dsDevices.Length; i++)
            {
                var dsDevice = dsDevices[i];
                if ((dsDevice.Name != null) && (dsDevice.Name.Length > 0))
                {
                    var camera = new Camera()
                    {
                        Name = dsDevice.Name,
                        Path = $"video={dsDevice.Name}"
                    };

                    result ??= [];

                    result.Add(camera);
                }
            }
            return result;
        }
    }
}
