
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


        public unsafe FFmpegScreenSource(string path, Rectangle rect, int frameRate = 20)
        {
            string inputFormat;
            Dictionary<String, String>? options = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                inputFormat = "gdigrab";
                options = new Dictionary<string, string>()
                {
                    ["offset_x"] = rect.X.ToString(),
                    ["offset_y"] = rect.Y.ToString(),
                    ["video_size"] = $"{rect.Width.ToString()}X{rect.Height.ToString()}",
                    ["framerate"] = frameRate.ToString()
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                inputFormat = "avfoundation";
                options = new Dictionary<string, string>()
                {
                    ["vf"] = $"crop={rect.Width.ToString()}:{rect.Height.ToString()}:{rect.X.ToString()}:{rect.Y.ToString()}",
                    ["framerate"] = frameRate.ToString()
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                inputFormat = "x11grab";
                //https://superuser.com/questions/1562228/how-to-specify-the-size-to-record-the-screen-with-ffmpeg
                options = new Dictionary<string, string>()
                {
                    ["video_size"] = $"{rect.Width.ToString()}X{rect.Height.ToString()}",
                    ["grab_x"] = rect.X.ToString(),
                    ["grab_y"] = rect.Y.ToString(),
                    ["framerate"] = frameRate.ToString()
                };
            }
            else
                throw new NotSupportedException($"Cannot find adequate input format - OSArchitecture:[{RuntimeInformation.OSArchitecture}] - OSDescription:[{RuntimeInformation.OSDescription}]");

            AVInputFormat* aVInputFormat = ffmpeg.av_find_input_format(inputFormat);

            CreateVideoDecoder(path, aVInputFormat, false, true);

            InitialiseDecoder(options);
        }
    }
}
