using System;
using System.Runtime.InteropServices;
using PortAudio.Net;
using static PortAudio.Net.PaBindings;

using pa_time_t = System.Double;
using signed_long_t = System.Int64;

namespace PortAudio.Net
{

    public class PaStream : IDisposable
    {
        private bool disposed;
        protected IntPtr stream;
        protected PaSampleFormat inputSampleFormat, outputSampleFormat;
        protected int numInputChannels, numOutputChannels;
        // native to managed callback thunks are allocated in unmanaged memory by the runtime so it is not necessary to
        // pin the delegates as they may be relocated safely, however it is necessary to prevent garbage collection
        // by keeping around at least one reference
        private StreamCallbackContainer streamCallbackContainer;
        private StreamFinishedCallbackContainer streamFinishedCallbackContainer;

        public void SetStreamFinishedCallback(PaStreamFinishedCallback streamFinishedCallback, object userData)
        {
            if (streamFinishedCallback == null)
            {
                streamFinishedCallbackContainer = null;
                PaErrorException.ThrowIfError(Pa_SetStreamFinishedCallback(stream, null));
            }
            else
            {
                streamFinishedCallbackContainer = new StreamFinishedCallbackContainer(streamFinishedCallback, userData);
                PaErrorException.ThrowIfError(Pa_SetStreamFinishedCallback(stream, streamFinishedCallbackContainer.Callback));
            }
        }

        public void StartStream()
        {
            PaErrorException.ThrowIfError(Pa_StartStream(stream));
        }

        public void StopStream()
        {
            PaErrorException.ThrowIfError(Pa_StopStream(stream));
        }

        public void AbortStream()
        {
            PaErrorException.ThrowIfError(Pa_AbortStream(stream));
        }

        public bool IsStreamStopped
        {
            get
            {
                var code = Pa_IsStreamStopped(stream);
                switch (code)
                {
                    case 0:
                        return false;
                    case 1:
                        return true;
                    default:
                        PaErrorException.ThrowIfError(code);
                        break;
                }
                return false;
            }
        }

        public bool IsStreamActive
        {
            get
            {
                var code = Pa_IsStreamActive(stream);
                switch (code)
                {
                    case 0:
                        return false;
                    case 1:
                        return true;
                    default:
                        PaErrorException.ThrowIfError(code);
                        break;
                }
                return false;
            }
        }

        public PaStreamInfo? StreamInfo
        {
            get
            {
                var ptr = Pa_GetStreamInfo(stream);
                if (ptr == IntPtr.Zero)
                    return null;
                return Marshal.PtrToStructure<PaStreamInfo>(ptr);
            }
        }

        public double GetStreamCpuLoad() => Pa_GetStreamCpuLoad(stream);

        public pa_time_t GetStreamTime() => Pa_GetStreamTime(stream);

        public signed_long_t GetStreamReadAvailable() => Pa_GetStreamReadAvailable(stream);

        public signed_long_t GetStreamWriteAvailable() => Pa_GetStreamWriteAvailable(stream);

        internal PaStream(
            IntPtr stream, StreamCallbackContainer streamCallbackContainer,
            PaSampleFormat inputSampleFormat, PaSampleFormat outputSampleFormat,
            int numInputChannels, int numOutputChannels)
        {
            this.stream = stream;
            this.streamCallbackContainer = streamCallbackContainer;
        }        

        private void Dispose(bool disposing)
        {
            unsafe
            {
                PaErrorException.ThrowIfError(Pa_CloseStream(stream));
            }
        }

        public void Dispose()
        {
            lock (this)
            {
                if (!disposed)
                {
                    disposed = true;
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }
            }
        }

        ~PaStream()
        {
            if (!disposed)
            {
                disposed = true;
                Dispose(false);
            }
        }
    }
}
