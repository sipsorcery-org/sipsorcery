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
        /// <returns>a <see cref="List{T}"/> of <see cref="Camera"/>s found on the system.</returns>
        public static List<Camera>? GetCameraDevices()
            => ParseDShowLogsForCameras(GetDShowLogsForDevice())?.ToList();

        private static List<Camera>? ParseDShowLogsForCameras(string? logs)
        {
            return logs?.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Where(l => DSHOW_VIDEO_DEVICE_LOG_OUTPUT.Any(l.ToLower().Contains))
                .Select(l =>
                {
                    var cam = l.Split(['"'], StringSplitOptions.RemoveEmptyEntries).First();

                    var opts = GetDShowLogsForDevice(cam)?
                            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                            .Where(l => DSHOW_CAMERA_FORMAT_LOG_OUTPUT.Any(l.ToLower().Contains))
                            .Select(l =>
                            {
                                var grouped = l.Split([' '], StringSplitOptions.RemoveEmptyEntries)
                                    .Where(s => s.Contains("="))
                                    .Distinct()
                                    .Select(s => s.Split(['='], StringSplitOptions.RemoveEmptyEntries))
                                    .Select(kp => new KeyValuePair<string, string>(value: kp[1],
                                                    key: kp[0] switch
                                                    {
                                                        "fps" => "framerate",
                                                        "s" => "video_size",
                                                        _ => kp[0]
                                                    }))
                                    .GroupBy(kp => kp.Key)
                                    .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value));

                                return Enumerable.Range(0, grouped.Values.Max(v => v.Count()))
                                    .Select(i => grouped.ToDictionary(
                                        g => g.Key,
                                        g => g.Value.ElementAtOrDefault(i) ?? g.Value.Last()))
                                    ;
                            })
                            .SelectMany(ld => ld)
                            .ToList();

                    return new Camera()
                    {
                        Name = cam, Path = $"video={cam}",
                        AvailableOptions = opts,
                        AvailableFormats = opts?
                            .Select(d =>
                            {
                                return new Camera.CameraFormat()
                                {
                                    PixelFormat = ffmpeg.av_get_pix_fmt(d[d.Keys.First(DSHOW_CAMERA_FORMAT_LOG_OUTPUT.Contains)]),
                                    Width = int.Parse(d["video_size"].Split(['x'], StringSplitOptions.RemoveEmptyEntries)[0]),
                                    Height = int.Parse(d["video_size"].Split(['x'], StringSplitOptions.RemoveEmptyEntries)[1]),
                                    FPS = double.Parse(d["framerate"])
                                };
                            })
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

                var line = Encoding.Default.GetString(lineBuffer, num);
                Console.Write(line);
                storedLogs += line;
            };
            ffmpeg.av_log_set_callback(logCallback);
        }

    }
}
