using System;

using unsigned_long_t = System.UInt64;

namespace PortAudio.Net
{
    public class PaBlockingStream : PaStream
    {
        internal PaBlockingStream(
            IntPtr stream,
            PaSampleFormat inputSampleFormat, PaSampleFormat outputSampleFormat,
            int numInputChannels, int numOutputChannels) :
            base(stream, null, inputSampleFormat, outputSampleFormat, numInputChannels, numOutputChannels) {}

        public unsafe void ReadStream(PaBuffer buffer) =>
            ReadStreamPrivate(buffer, isUnsafe: true);

        public void ReadStream<T>(PaBuffer<T> buffer) where T: unmanaged =>
            ReadStreamPrivate(buffer);

        public void ReadStream<T>(PaNonInterleavedBuffer<T> buffer) where T: unmanaged =>
            ReadStreamPrivate(buffer, isNonInterleaved: true);

        public unsafe void WriteStream(PaBuffer buffer) =>
            WriteStreamPrivate(buffer, isUnsafe: true);

        public void WriteStream<T>(PaBuffer<T> buffer) where T: unmanaged =>
            WriteStreamPrivate(buffer);

        public void WriteStream<T>(PaNonInterleavedBuffer<T> buffer) where T: unmanaged =>
            WriteStreamPrivate(buffer, isNonInterleaved: true);

        private void ReadStreamPrivate(PaBuffer buffer, bool isUnsafe = false, bool isNonInterleaved = false)
        {
            if (numInputChannels == 0)
                throw new InvalidOperationException("Cannot read from a stream with no input channels.");
            if (numInputChannels != buffer.Channels)
                throw new ArgumentException(
                    $"Expected buffer with {numInputChannels} channels but got {buffer.Channels}.",
                    nameof(buffer));
            if (!isUnsafe)
            {
                var expectedType = SampleFormatToType(inputSampleFormat);
                if (expectedType == typeof(PaBuffer))
                    throw new InvalidOperationException(
                        "To ensure memory safety, this overload of ReadStream is not supported for streams with " +
                        "custom sample formats. Use ReadStream(PaBuffer) instead.");
                var formatNonInterleaved = (inputSampleFormat & PaSampleFormat.paNonInterleaved) != 0;
                if (isNonInterleaved && !formatNonInterleaved)
                    throw new InvalidOperationException(
                        "Only streams with non-interleaved sample formats support this overload of ReadStream, " +
                        "Use ReadStream(PaBuffer<T>) instead.");
                if (!isNonInterleaved && formatNonInterleaved)
                    throw new InvalidOperationException(
                        "Only streams with interleaved sample formats suppoort this overload of ReadStream. " +
                        "Use ReadStream(PaNonInterleavedBuffer<T>) instead.");
                if (expectedType != buffer.GetType())
                    throw new ArgumentException(
                        $"Expected a buffer of type {expectedType} for the sample format, but got {buffer.GetType()}.",
                        nameof(buffer));
            }
            PaBindings.Pa_ReadStream(stream, buffer.Pointer, (unsigned_long_t)buffer.Frames);
        }

        private void WriteStreamPrivate(PaBuffer buffer, bool isUnsafe = false, bool isNonInterleaved = false)
        {
            if (numOutputChannels == 0)
                throw new InvalidOperationException("Cannot write to a stream with no output channels.");
            if (numOutputChannels != buffer.Channels)
                throw new ArgumentException(
                    $"Expected buffer with {numOutputChannels} channels but got {buffer.Channels}.",
                    nameof(buffer));
            if (!isUnsafe)
            {
                var expectedType = SampleFormatToType(outputSampleFormat);
                if (expectedType == typeof(PaBuffer))
                    throw new InvalidOperationException(
                        "To ensure memory safety, this overload of WriteStream is not supported for streams with " +
                        "custom sample formats. Use WriteStream(PaBuffer) instead.");
                var formatNonInterleaved = (outputSampleFormat & PaSampleFormat.paNonInterleaved) != 0;
                if (isNonInterleaved && !formatNonInterleaved)
                    throw new InvalidOperationException(
                        "Only streams with non-interleaved sample formats support this overload of WriteStream, " +
                        "Use WriteStream(PaBuffer<T>) instead.");
                if (!isNonInterleaved && formatNonInterleaved)
                    throw new InvalidOperationException(
                        "Only streams with interleaved sample formats suppoort this overload of WriteStream. " +
                        "Use WriteStream(PaNonInterleavedBuffer<T>) instead.");
                if (expectedType != buffer.GetType())
                    throw new ArgumentException(
                        $"Expected a buffer of type {expectedType} for the sample format, but got {buffer.GetType()}.",
                        nameof(buffer));
            }
            PaBindings.Pa_ReadStream(stream, buffer.Pointer, (unsigned_long_t)buffer.Frames);
        }

        private Type SampleFormatToType(PaSampleFormat sampleFormat)
        {
            if ((sampleFormat & PaSampleFormat.paNonInterleaved) == 0)
            {
                switch (sampleFormat)
                {
                    case PaSampleFormat.paFloat32:
                        return typeof(PaBuffer<Single>);
                    case PaSampleFormat.paInt32:
                        return typeof(PaBuffer<Int32>);
                    case PaSampleFormat.paInt16:
                        return typeof(PaBuffer<Int16>);
                    case PaSampleFormat.paInt8:
                        return typeof(PaBuffer<SByte>);
                    case PaSampleFormat.paUInt8:
                        return typeof(PaBuffer<Byte>);
                    default:
                        return typeof(PaBuffer);
                }
            }
            else
            {
                switch (sampleFormat & ~PaSampleFormat.paNonInterleaved)
                {
                    case PaSampleFormat.paFloat32:
                        return typeof(PaNonInterleavedBuffer<Single>);
                    case PaSampleFormat.paInt32:
                        return typeof(PaNonInterleavedBuffer<Int32>);
                    case PaSampleFormat.paInt16:
                        return typeof(PaNonInterleavedBuffer<Int16>);
                    case PaSampleFormat.paInt8:
                        return typeof(PaNonInterleavedBuffer<SByte>);
                    case PaSampleFormat.paUInt8:
                        return typeof(PaNonInterleavedBuffer<Byte>);
                    default:
                        return typeof(PaBuffer);
                }
            }
        }
    }
}