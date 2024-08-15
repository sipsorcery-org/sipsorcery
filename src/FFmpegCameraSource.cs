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
        /// <remarks>Will use highest resolution and framerate after filtered.
        /// </remarks>
        /// <param name="formatFilter">Filter function.</param>
        /// <returns><see langword="true"/> If decoder resets successfully.
        /// <br/>Increase FFmpeg verbosity / loglevel for more information.</returns>
        public bool RestrictCameraFormats(Func<Camera.CameraFormat, bool> formatFilter)
        {
            var maxAllowedres = _camera.AvailableFormats?.Where(formatFilter.Invoke)
                                    .OrderByDescending(c => c.Width > c.Height ? c.Width : c.Height)
                                    .ThenByDescending(c => c.FPS)
                                    .Select(c => new Dictionary<string, string>()
                                    {
                                        { "pixel_format", ffmpeg.av_get_pix_fmt_name(c.PixelFormat) },
                                        { "video_size", $"{c.Width}x{c.Height}" },
                                        { "framerate", $"{c.FPS}" },
                                    })
                                    .FirstOrDefault();
            
            if(maxAllowedres is null)
                logger.LogWarning($"camera/input device \"{_camera.Name}\" doesn't have any recognizable filtered formats to be used.");

            return SetCameraDeviceOptions(maxAllowedres);
        }

        /// <summary>
        /// Filter for available FFmpeg camera/input device options and resets the underlying
        /// <see cref="FFmpegVideoDecoder"/> with the specified options.
        /// </summary>
        /// <remarks>
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
            var opts = _camera.AvailableOptions?.FirstOrDefault(optFilter.Invoke);

            if (opts is null)
                logger.LogWarning($"No camera/input device options to be used.");

            return SetCameraDeviceOptions(opts);
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
    }
}
