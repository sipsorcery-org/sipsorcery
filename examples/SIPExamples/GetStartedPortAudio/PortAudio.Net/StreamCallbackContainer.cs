using System;
using System.Runtime.InteropServices;

using unsigned_long_t = System.UInt64;

namespace PortAudio.Net
{
    internal class StreamCallbackContainer
    {
        private PaStreamCallback callbackProvider;
        private PaSampleFormat inputSampleFormat, outputSampleFormat;
        private int numInputChannels, numOutputChannels;
        private object userData;

        public StreamCallbackContainer(
            PaStreamCallback callbackProvider,
            PaSampleFormat inputSampleFormat, PaSampleFormat outputSampleFormat,
            int numInputChannels, int numOutputChannels, object userData)
        {
            this.callbackProvider = callbackProvider;
            this.inputSampleFormat = inputSampleFormat;
            this.outputSampleFormat = outputSampleFormat;
            this.numInputChannels = numInputChannels;
            this.numOutputChannels = numOutputChannels;
            this.userData = userData;
        }

        public unsafe PaStreamCallbackResult Callback(
            void* input, void* output,
            unsigned_long_t frameCount, IntPtr timeInfo,
            PaStreamCallbackFlags statusFlags, IntPtr garbage)
        {
            // Note: userData object cannot be reconstituted from IntPtr so thunking delegate curries userData instead
            return callbackProvider(
                PaBufferBySampleFormat((IntPtr)input, inputSampleFormat, numInputChannels, (int)frameCount),
                PaBufferBySampleFormat((IntPtr)output, outputSampleFormat, numOutputChannels, (int)frameCount),
                (int)frameCount, Marshal.PtrToStructure<PaStreamCallbackTimeInfo>(timeInfo),
                statusFlags, userData);
        }

        private unsafe PaBuffer PaBufferBySampleFormat(
            IntPtr pointer, PaSampleFormat sampleFormat,
            int channels, int frames)
        {
            if (channels == 0) return null;
            if ((sampleFormat & PaSampleFormat.paNonInterleaved) == 0)
            {
                switch (sampleFormat)
                {
                    case PaSampleFormat.paFloat32:
                        return new PaBuffer<Single>(pointer, channels, frames);
                    case PaSampleFormat.paInt32:
                        return new PaBuffer<Int32>(pointer, channels, frames);
                    case PaSampleFormat.paInt16:
                        return new PaBuffer<Int16>(pointer, channels, frames);
                    case PaSampleFormat.paInt8:
                        return new PaBuffer<SByte>(pointer, channels, frames);
                    case PaSampleFormat.paUInt8:
                        return new PaBuffer<Byte>(pointer, channels, frames);
                    default:
                        return new PaBuffer(pointer, channels, frames);
                }
            }
            else
            {
                switch (sampleFormat & ~PaSampleFormat.paNonInterleaved)
                {
                    case PaSampleFormat.paFloat32:
                        return new PaNonInterleavedBuffer<Single>(pointer, channels, frames);
                    case PaSampleFormat.paInt32:
                        return new PaNonInterleavedBuffer<Int32>(pointer, channels, frames);
                    case PaSampleFormat.paInt16:
                        return new PaNonInterleavedBuffer<Int16>(pointer, channels, frames);
                    case PaSampleFormat.paInt8:
                        return new PaNonInterleavedBuffer<SByte>(pointer, channels, frames);
                    case PaSampleFormat.paUInt8:
                        return new PaNonInterleavedBuffer<Byte>(pointer, channels, frames);
                    default:
                        return new PaBuffer(pointer, channels, frames);
                }
            }
        }
    }
}