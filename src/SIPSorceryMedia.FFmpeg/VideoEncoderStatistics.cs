using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.FFmpeg
{
    public struct VideoEncoderStatistics
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int FPS { get; set; }
        public VideoCodecsEnum Codec { get; set; }

        public VideoEncoderStatistics(int width, int height, int fps, VideoCodecsEnum codec) {
            Width = width;
            Height = height;
            FPS = fps;
            Codec = codec;
        }
    }
}