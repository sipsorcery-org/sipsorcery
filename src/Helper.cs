using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorceryMedia.FFmpeg
{
    static class Helper
    {
        public const int VIDEO_SAMPLING_RATE = 90000;
        public const int DEFAULT_FRAME_RATE = 30;
        public const int VP8_FORMATID = 96;
        public const int H264_FORMATID = 100;
    }
}
