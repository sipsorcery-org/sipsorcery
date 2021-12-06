
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegScreenSource : FFmpegVideoSource
    {
        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegScreenSource>();


        public unsafe FFmpegScreenSource(string path, Rectangle? rect = null)
        {
            string inputFormat;
            Dictionary<String, String>? options = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                inputFormat = "gdigrab";
                if (rect != null)
                {
                    options = new Dictionary<string, string>()
                    {
                        ["offset_x"] = rect.Value.X.ToString(),
                        ["offset_y"] = rect.Value.Y.ToString(),
                        ["video_size"] = $"{rect.Value.Width.ToString()}X{rect.Value.Height.ToString()}"
                    };
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                inputFormat = "avfoundation";
                if (rect != null)
                {
                    options = new Dictionary<string, string>()
                    {
                        ["vf"] = $"crop={rect.Value.Width.ToString()}:{rect.Value.Height.ToString()}:{rect.Value.X.ToString()}:{rect.Value.Y.ToString()}"
                    };
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // TODO
                inputFormat = "x11grab";
                throw new NotSupportedException($"Cannot find adequate input format - OSArchitecture:[{RuntimeInformation.OSArchitecture}] - OSDescription:[{RuntimeInformation.OSDescription}]");
            }
            else
                throw new NotSupportedException($"Cannot find adequate input format - OSArchitecture:[{RuntimeInformation.OSArchitecture}] - OSDescription:[{RuntimeInformation.OSDescription}]");

            AVInputFormat* aVInputFormat = ffmpeg.av_find_input_format(inputFormat);

            CreateVideoDecoder(path, aVInputFormat, false, true);

            InitialiseDecoder(options);
        }
    }
}
