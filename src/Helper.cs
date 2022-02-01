using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorceryMedia.FFmpeg
{
    public class Helper
    {
        public const int MIN_SLEEP_MILLISECONDS = 15;

        public const int VIDEO_SAMPLING_RATE = 90000;
        public const int AUDIO_SAMPLING_RATE = 8000;

        public const int DEFAULT_VIDEO_FRAME_RATE = 30;

        public const int VP8_FORMATID = 96;
        public const int H264_FORMATID = 100;

        internal static List<VideoFormat> GetSupportedVideoFormats() => new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, Helper.VP8_FORMATID, Helper.VIDEO_SAMPLING_RATE),
            new VideoFormat(VideoCodecsEnum.H264, Helper.H264_FORMATID, Helper.VIDEO_SAMPLING_RATE)
        };

        internal static List<AudioFormat> GetSupportedAudioFormats() => new List<AudioFormat>
        {
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.G722)
        };

    }
}
