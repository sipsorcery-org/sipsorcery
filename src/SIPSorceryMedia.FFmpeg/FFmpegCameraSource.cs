using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorcery;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegCameraSource : FFmpegVideoSource
    {
        private static ILogger logger = LogFactory.CreateLogger<FFmpegCameraSource>();

        private readonly Camera _camera;
        private IEnumerable<Camera.CameraFormat>? _filteredFormats;

        /// <summary>
        /// Construct an FFmpeg camera/input device source provided input path.
        /// </summary>
        /// <remarks>See </remarks>
        /// <param name="path"></param>
        public FFmpegCameraSource(string path) : this(FFmpegCameraManager.GetCameraByPath(path) ?? new() { Path = path })
        {
        }

        /// <summary>
        /// Construct an FFmpeg camera/input device source provided a <see cref="Camera"/>.
        /// </summary>
        /// <param name="camera"></param>
        /// <exception cref="NotSupportedException">Platform is currently not supported.</exception>
        public unsafe FFmpegCameraSource(Camera camera)
        {
            _camera = camera;

            _sourcePixFmts = _camera.AvailableFormats?.Select(f => f.PixelFormat).Distinct().ToArray();

            string inputFormat = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dshow"
                                    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "v4l2"
                                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "avfoundation"
                                    : throw new NotSupportedException($"Cannot find adequate input format" +
                                                $" - OSArchitecture:[{RuntimeInformation.OSArchitecture}]" +
                                                $" - OSDescription:[{RuntimeInformation.OSDescription}]");

            var _aVInputFormat = ffmpeg.av_find_input_format(inputFormat);

            CreateVideoDecoder(_camera.Path, _aVInputFormat, false, true);

            InitialiseDecoder();
        }

        /// <summary>
        /// Filter for the desired <see cref="Camera.CameraFormat"/>(s) to use
        /// and resets the underlying <see cref="FFmpegVideoDecoder"/>.
        /// </summary>
        /// <remarks>Will use highest framerate then resolution after filtered.
        /// </remarks>
        /// <param name="formatFilter">Filter function.</param>
        /// <returns><see langword="true"/> If decoder resets successfully.
        /// <br/>Increase FFmpeg verbosity / loglevel for more information.</returns>
        public bool RestrictCameraFormats(Func<Camera.CameraFormat, bool> formatFilter)
        {
            _filteredFormats = _camera.AvailableFormats?.Where(formatFilter.Invoke)
                                    .OrderByDescending(c => c.FPS)
                                    .ThenByDescending(c => c.Width > c.Height ? c.Width : c.Height);

            var maxAllowedres = _filteredFormats?.FirstOrDefault().ToOptionDictionary();

            if (maxAllowedres == null)
            {
                logger.LogWarning("camera/input device \"{name}\" doesn't have any recognizable filtered formats to be used.", _camera.Name);
                return false;
            }

            return SetCameraDeviceOptions(maxAllowedres);
        }

        /// <summary>
        /// Filter for available FFmpeg camera/input device options and resets the underlying
        /// <see cref="FFmpegVideoDecoder"/> with the specified options.
        /// </summary>
        /// <remarks>Will use highest framerate then resolution after filtered.
        /// <br/><br/>
        /// <i>This is an advanced control for camera/input devices options filtering.
        /// <br/>Most usage will use <see cref="RestrictCameraFormats"/> filter.</i>
        /// <br/><br/> See <see href="https://www.ffmpeg.org/ffmpeg-devices.html">FFmpeg documentation on the device options</see>
        /// for your system's <see cref="AVInputFormat"/> (i.e. dshow, avfoundation, v4l2, etc.)
        /// </remarks>
        /// <param name="optFilter">Filter function.</param>
        /// <returns><see langword="true"/> If decoder resets successfully.
        /// <br/>Increase FFmpeg verbosity / loglevel for more information.</returns>
        public bool RestrictCameraOptions(Func<Dictionary<string, string>, bool> optFilter)
        {
            var filtered = _camera.AvailableOptions?.Where(optFilter.Invoke)
                                .OrderByDescending(d => int.Parse(d["max_fps"]))
                                .ThenByDescending(d => int.Parse(d["min_fps"]))
                                .ThenByDescending(d =>
                                {
                                    var max_s = d["max_s"].Split(['x'], StringSplitOptions.RemoveEmptyEntries);
                                    var max_w = int.Parse(max_s[0]);
                                    var max_h = int.Parse(max_s[1]);

                                    return max_h > max_w ? max_h : max_w;
                                })
                                .ThenByDescending(d =>
                                {
                                    var min_s = d["min_s"].Split(['x'], StringSplitOptions.RemoveEmptyEntries);
                                    var min_w = int.Parse(min_s[0]);
                                    var min_h = int.Parse(min_s[1]);

                                    return min_h > min_w ? min_h : min_w;
                                })
                                .FirstOrDefault()?
                                .Where(kp => !kp.Key.Contains("min_"))
                                .ToDictionary(d => d.Key switch
                                    {
                                        "max_s" => "video_size",
                                        "max_fps" => "framerate",
                                        _ => d.Key
                                    },
                                    kp => kp.Value
                                );

            if (filtered is null)
                logger.LogWarning($"No camera/input device options to be used.");

            return SetCameraDeviceOptions(filtered);
        }

        /// <summary>
        /// Resets the underlying <see cref="FFmpegVideoDecoder"/> with the provided options.
        /// </summary>
        /// <remarks>
        /// <br/><i>This is an advanced options for if you know/static preconfigured device options beforehand.
        /// Most usage will use <see cref="RestrictCameraFormats"/> or <see cref="RestrictCameraOptions"/> filter.</i>
        /// <br/><br/>
        /// See <see href="https://www.ffmpeg.org/ffmpeg-devices.html">FFmpeg documentation on the device options</see>
        /// for your system's <see cref="AVInputFormat"/> (i.e. dshow, avfoundation, v4l2, etc.)
        /// </remarks>
        /// <param name="options">A dictionary of device options</param>
        /// <returns><see langword="true"/> If decoder resets successfully.
        /// <br/>Increase FFmpeg verbosity / loglevel for more information.</returns>
        public bool SetCameraDeviceOptions(Dictionary<string, string>? options)
        {
            _videoDecoder?.Dispose();

            return InitialiseDecoder(options);
        }

        internal override async void OnNegotiatedPixelFormat(AVPixelFormat ongoingFmt, AVPixelFormat chosenPixFmt)
        {
            base.OnNegotiatedPixelFormat(ongoingFmt, chosenPixFmt);

            if (ongoingFmt == chosenPixFmt)
                return;

            var formats = _filteredFormats ?? _camera.AvailableFormats;

            if (formats == null)
                return;

            var chosenfmt = formats?.FirstOrDefault(f => f.PixelFormat == chosenPixFmt);

            if (chosenfmt.HasValue && _videoDecoder != null)
            {
                _videoDecoder.OnEndOfFile -= VideoDecoder_OnEndOfFile;

                await _videoDecoder.Close();

                _videoDecoder.Dispose();

                InitialiseDecoder(chosenfmt.Value.ToOptionDictionary());

                _videoDecoder.StartDecode();

                _videoDecoder.OnEndOfFile += VideoDecoder_OnEndOfFile;
            }
        }
    }

    internal static class FFmpegCameraExtensions
    {
        internal static Dictionary<string, string>? ToOptionDictionary(this Camera.CameraFormat c)
        {
            if (c.Equals(default(Camera.CameraFormat))
                || c.FPS == 0 || c.Width == 0 || c.Height == 0
                )
                return null;

            return new Dictionary<string, string>()
            {
                { "pixel_format", ffmpeg.av_get_pix_fmt_name(c.PixelFormat) },
                { "video_size", $"{c.Width}x{c.Height}" },
                { "framerate", $"{c.FPS}" },
            };
        }

    }
}
