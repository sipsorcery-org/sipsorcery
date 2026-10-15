using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;

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

        // Returns a copy so external callers (e.g. the CLI route nodes that build their own tracks) can
        // share the one canonical codec set + fmtp without risk of mutating the backing list.
        public static List<VideoFormat> GetSupportedVideoFormats() => new List<VideoFormat>(_supportedVidFormats);

        private static readonly List<VideoFormat> _supportedVidFormats =
        [
            new VideoFormat(VideoCodecsEnum.VP8, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE),
            new VideoFormat(VideoCodecsEnum.VP9, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE),
            // Advertise a profile-level-id so strict codec matchers (e.g. Pion/Broadcast Box) keep H264 in
            // the answer. 42e01f (Constrained Baseline 3.1) is the WebRTC de-facto baseline and matches what
            // the encoder produces (libx264 is configured for the baseline profile).
            new VideoFormat(VideoCodecsEnum.H264, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE, "packetization-mode=1;profile-level-id=42e01f"),
            // H265 is offered bare: packetization-mode is an H264-only parameter (RFC 6184), and strict
            // matchers (Pion/Broadcast Box) match H265 against an unparameterised registration - a bare
            // offer is kept, a parameterised one (profile-id/tier-flag/...) is dropped. So, unlike H264,
            // H265 must NOT carry an fmtp profile here.
            new VideoFormat(VideoCodecsEnum.H265, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE),
            new VideoFormat(VideoCodecsEnum.AV1, _dynFmtIdCounter++, VideoFormat.DEFAULT_CLOCK_RATE),

            // Currently disabled because MJPEG doesn't work with the current pipeline that forces pixel conversion to YUV420P
            // TODO: Fix pixel format conversion in Decode->Encode pipeline
            //new VideoFormat(VideoCodecsEnum.JPEG, _dynamicFmtIdRand.Next(DYNAMIC_ID_MIN, DYNAMIC_ID_MAX), VideoFormat.DEFAULT_CLOCK_RATE),
        ];
    }
}
