using System;
using System.Runtime.InteropServices;
using PortAudio.Net;
using static PortAudio.Net.PaBindings;

using int_t = System.Int32;
using long_t = System.Int64;
using unsigned_long_t = System.UInt64;
using pa_host_api_index_t = System.Int32;
using pa_device_index_t = System.Int32;
using pa_error_t = System.Int32;

namespace PortAudio.Net
{
    public class PaLibrary : IDisposable
    {
        public const pa_device_index_t paNoDevice = -1;

        public const pa_error_t paFormatIsSupported = 0;

        private bool disposed = false;

        public static int_t Version => Pa_GetVersion();

        public const int paFramesPerBufferUnspecified = 0;

        public static PaVersionInfo VersionInfo =>
            Marshal.PtrToStructure<PaVersionInfo>(Pa_GetVersionInfo());

        public static string GetErrorText(pa_error_t error) => Pa_GetErrorText(error);

        public static PaLibrary Initialize()
        {
            PaErrorException.ThrowIfError(Pa_Initialize());
            return new PaLibrary();
        }
        public pa_host_api_index_t HostApiCount
        {
            get
            {
                var count = Pa_GetHostApiCount();
                PaErrorException.ThrowIfError(count);
                return count;
            }
        }

        public pa_host_api_index_t GetDefaultHostApi
        {
            get
            {
                var index = Pa_GetDefaultHostApi();
                PaErrorException.ThrowIfError(index);
                return index;
            }
        }

        public PaHostApiInfo? GetHostApiInfo(pa_host_api_index_t hostApi)
        {
            var ptr = Pa_GetHostApiInfo(hostApi);
            if (ptr == IntPtr.Zero)
                return null;
            return Marshal.PtrToStructure<PaHostApiInfo>(ptr);
        }

        public pa_host_api_index_t HostApiTypeIdToHostApiIndex(PaHostApiTypeId type)
        {
            var index = Pa_HostApiTypeIdToHostApiIndex(type);
            PaErrorException.ThrowIfError(index);
            return index;
        }

        public pa_device_index_t HostApiDeviceIndexToDeviceIndex(pa_host_api_index_t hostApi, int hostApiDeviceIndex)
        {
            var index = Pa_HostApiDeviceIndexToDeviceIndex(hostApi, hostApiDeviceIndex);
            PaErrorException.ThrowIfError(index);
            return index;
        }

        public pa_device_index_t DeviceCount
        {
            get
            {
                var count = Pa_GetDeviceCount();
                PaErrorException.ThrowIfError(count);
                return count;
            }
        }

        public PaHostErrorInfo GetLastHostErrorInfo() =>
            Marshal.PtrToStructure<PaHostErrorInfo>(Pa_GetLastHostErrorInfo());

        public pa_device_index_t? DefaultInputDevice => Pa_GetDefaultInputDevice();

        public pa_device_index_t DefaultOutputDevice => Pa_GetDefaultOutputDevice();

        public PaDeviceInfo? GetDeviceInfo(int device)
        {
            var ptr = Pa_GetDeviceInfo(device);
            if (ptr == IntPtr.Zero)
                return null;
            return Marshal.PtrToStructure<PaDeviceInfo>(ptr);
        }

        public pa_error_t IsFormatSupported(PaStreamParameters? inputParameters, PaStreamParameters? outputParameters, double sampleRate)
        {
            unsafe
            {
                PaStreamParameters inputParametersTemp, outputParametersTemp;
                IntPtr inputParametersPtr = IntPtr.Zero;
                if (inputParameters.HasValue)
                {
                    inputParametersPtr = new IntPtr(&inputParametersTemp);
                    Marshal.StructureToPtr<PaStreamParameters>(inputParameters.Value, inputParametersPtr, false);
                }
                IntPtr outputParametersPtr = IntPtr.Zero;
                if (outputParameters.HasValue)
                {
                    outputParametersPtr = new IntPtr(&outputParametersTemp);
                    Marshal.StructureToPtr<PaStreamParameters>(outputParameters.Value, outputParametersPtr, false);
                }
                return Pa_IsFormatSupported(inputParametersPtr, outputParametersPtr, sampleRate);
            }
        }

        public PaStream OpenStream(
            PaStreamParameters? inputParameters, PaStreamParameters? outputParameters,
            double sampleRate, int framesPerBuffer, PaStreamFlags streamFlags,
            PaStreamCallback streamCallback, object userData)
        {
            var streamCallbackContainer = new StreamCallbackContainer(
                streamCallback,
                inputParameters.HasValue ? inputParameters.Value.sampleFormat : 0,
                outputParameters.HasValue ? outputParameters.Value.sampleFormat : 0,
                inputParameters.HasValue ? inputParameters.Value.channelCount : 0,
                outputParameters.HasValue ? outputParameters.Value.channelCount : 0,
                userData);
            IntPtr stream;
            unsafe
            {
                PaStreamParameters inputParametersTemp, outputParametersTemp;
                IntPtr inputParametersPtr = IntPtr.Zero;
                if (inputParameters.HasValue)
                {
                    inputParametersPtr = new IntPtr(&inputParametersTemp);
                    Marshal.StructureToPtr<PaStreamParameters>(inputParameters.Value, inputParametersPtr, false);
                }
                IntPtr outputParametersPtr = IntPtr.Zero;
                if (outputParameters.HasValue)
                {
                    outputParametersPtr = new IntPtr(&outputParametersTemp);
                    Marshal.StructureToPtr<PaStreamParameters>(outputParameters.Value, outputParametersPtr, false);
                }
                PaErrorException.ThrowIfError(Pa_OpenStream(
                    new IntPtr(&stream),
                    inputParametersPtr, outputParametersPtr,
                    sampleRate, (unsigned_long_t)framesPerBuffer, streamFlags,
                    streamCallbackContainer.Callback, IntPtr.Zero));
            }
            return new PaStream(
                stream, streamCallbackContainer,
                inputParameters.HasValue ? inputParameters.Value.sampleFormat : 0,
                outputParameters.HasValue ? outputParameters.Value.sampleFormat : 0,
                inputParameters.HasValue ? inputParameters.Value.channelCount : 0,
                outputParameters.HasValue ? outputParameters.Value.channelCount : 0);
        }

        public PaBlockingStream OpenStream(
            PaStreamParameters? inputParameters, PaStreamParameters? outputParameters,
            double sampleRate, int framesPerBuffer, PaStreamFlags streamFlags)
        {
            IntPtr stream;
            unsafe
            {
                PaStreamParameters inputParametersTemp, outputParametersTemp;
                IntPtr inputParametersPtr = IntPtr.Zero;
                if (inputParameters.HasValue)
                {
                    inputParametersPtr = new IntPtr(&inputParametersTemp);
                    Marshal.StructureToPtr<PaStreamParameters>(inputParameters.Value, inputParametersPtr, false);
                }
                IntPtr outputParametersPtr = IntPtr.Zero;
                if (outputParameters.HasValue)
                {
                    outputParametersPtr = new IntPtr(&outputParametersTemp);
                    Marshal.StructureToPtr<PaStreamParameters>(outputParameters.Value, outputParametersPtr, false);
                }
                PaErrorException.ThrowIfError(Pa_OpenStream(
                    new IntPtr(&stream),
                    inputParametersPtr, outputParametersPtr,
                    sampleRate, (unsigned_long_t)framesPerBuffer, streamFlags,
                    null, IntPtr.Zero));
            }
            return new PaBlockingStream(
                stream,
                inputParameters.HasValue ? inputParameters.Value.sampleFormat : 0,
                outputParameters.HasValue ? outputParameters.Value.sampleFormat : 0,
                inputParameters.HasValue ? inputParameters.Value.channelCount : 0,
                outputParameters.HasValue ? outputParameters.Value.channelCount : 0);
        }
        
        public PaStream OpenDefaultStream(
            int numInputChannels, int numOutputChannels, PaSampleFormat sampleFormat,
            double sampleRate, int framesPerBuffer, PaStreamFlags streamFlags,
            PaStreamCallback streamCallback, object userData)
        {
            var streamCallbackContainer = new StreamCallbackContainer(
                streamCallback,
                sampleFormat, sampleFormat,
                numInputChannels, numOutputChannels, userData);
            IntPtr stream;
            unsafe
            {
                PaErrorException.ThrowIfError(Pa_OpenDefaultStream(
                    new IntPtr(&stream),
                    numInputChannels, numOutputChannels, sampleFormat,
                    sampleRate, (unsigned_long_t)framesPerBuffer, streamFlags,
                    streamCallbackContainer.Callback, IntPtr.Zero));
            }
            return new PaStream(
                stream, streamCallbackContainer,
                sampleFormat, sampleFormat, numInputChannels, numOutputChannels);
        }

        public PaBlockingStream OpenDefaultStream(
            int numInputChannels, int numOutputChannels, PaSampleFormat sampleFormat,
            double sampleRate, int framesPerBuffer, PaStreamFlags streamFlags)
        {
            IntPtr stream;
            unsafe
            {
                PaErrorException.ThrowIfError(Pa_OpenDefaultStream(
                    new IntPtr(&stream),
                    numInputChannels, numOutputChannels, sampleFormat,
                    sampleRate, (unsigned_long_t)framesPerBuffer, streamFlags,
                    null, IntPtr.Zero));
            }
            return new PaBlockingStream(stream, sampleFormat, sampleFormat, numInputChannels, numOutputChannels);
        }

        public pa_error_t GetSampleSize(PaSampleFormat format) => Pa_GetSampleSize(format);

        public void Sleep(long_t msec) { Pa_Sleep(msec); }

        private void Dispose(bool disposing)
        {
            PaErrorException.ThrowIfError(Pa_Terminate());
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

        ~PaLibrary()
        {
            if (!disposed)
            {
                disposed = true;
                Dispose(false);
            }
        }
    }
}