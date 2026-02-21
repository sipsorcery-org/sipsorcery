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
        public const int DEFAULT_VIDEO_FRAME_RATE = 30;

        // Old hardcoded
        [Obsolete("VP8 in RTP is defined as dynamic type, this may not be used for matching.")]
        public const int VP8_FORMATID = 96;
        [Obsolete("H264 in RTP is defined as dynamic type, this may not be used for matching.")]
        public const int H264_FORMATID = 100;

        private static int _dynFmtIdCounter = VideoFormat.DYNAMIC_ID_MIN;

        internal static List<VideoFormat> GetSupportedVideoFormats() => _supportedVidFormats; // Use predefined list of supported video formats

        private static readonly List<VideoFormat> _supportedVidFormats =
        [
            new VideoFormat(VideoCodecsEnum.VP8, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE),
            new VideoFormat(VideoCodecsEnum.VP9, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE),
            new VideoFormat(VideoCodecsEnum.H264, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE, "packetization-mode=1"),
            new VideoFormat(VideoCodecsEnum.H265, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE, "packetization-mode=1"),

            // Currently disabled because MJPEG doesn't work with the current pipeline that forces pixel conversion to YUV420P
            // TODO: Fix pixel format conversion in Decode->Encode pipeline
            //new VideoFormat(VideoCodecsEnum.JPEG, _dynamicFmtIdRand.Next(DYNAMIC_ID_MIN, DYNAMIC_ID_MAX), VideoFormat.DEFAULT_CLOCK_RATE),
        ];
    }
}
