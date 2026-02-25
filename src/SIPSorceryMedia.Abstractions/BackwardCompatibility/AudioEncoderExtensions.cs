using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorceryMedia.Abstractions;

public static class AudioEncoderExtensions
{
    extension(IAudioEncoder source)
    {
        public byte[] EncodeAudio(short[] pcm, AudioFormat format)
        {
            using var buffer = new ArrayPoolBufferWriter<byte>();
            source.EncodeAudio(pcm, format, buffer);
            return buffer.WrittenSpan.ToArray();
        }

        public short[] DecodeAudio(byte[] encodedSample, AudioFormat format)
        {
            using var buffer = new ArrayPoolBufferWriter<short>();
            source.DecodeAudio(encodedSample, format, buffer);
            return buffer.WrittenSpan.ToArray();
        }
    }
}
