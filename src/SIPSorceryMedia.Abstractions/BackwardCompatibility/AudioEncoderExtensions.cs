using System;
using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorceryMedia.Abstractions;

public static class AudioEncoderExtensions
{
    extension(IAudioEncoder source)
    {
        public byte[] EncodeAudio(short[] pcm, AudioFormat format) => source.EncodeAudio(pcm.AsMemory(), format);

        public byte[] EncodeAudio(ReadOnlyMemory<short> pcm, AudioFormat format)
        {
            using var buffer = new ArrayPoolBufferWriter<byte>(0);
            source.EncodeAudio(pcm.Span, format, buffer);
            return buffer.WrittenSpan.ToArray();
        }

        public short[] DecodeAudio(byte[] encodedSample, AudioFormat format) => source.DecodeAudio(encodedSample.AsMemory(), format);

        public short[] DecodeAudio(ReadOnlyMemory<byte> encodedSample, AudioFormat format)
        {
            using var buffer = new ArrayPoolBufferWriter<short>();
            source.DecodeAudio(encodedSample.Span, format, buffer);
            return buffer.WrittenSpan.ToArray();
        }
    }
}
