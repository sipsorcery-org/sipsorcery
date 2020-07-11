/* The code in this file is adapted from the PortAudio Portable Real-Time Audio Library and is dual licensed under
 * PortAudio.Net's MPL-2.0 licensing terms and PortAudio Portable Real-Time Audio Library's MIT Expat license.
 *
 * PortAudio Portable Real-Time Audio Library
 * Original work Copyright (c) 1999-2011 Ross Bencina, Phil Burk
 * Modified work Copyright (c) 2019 Kyle Gagner
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
 * documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
 * Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
 * WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
 * COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
 * OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 *
 * The text above constitutes the entire PortAudio license; however, the PortAudio community also makes the following
 * non-binding requests:
 *
 * Any person wishing to distribute modifications to the Software is requested to send the modifications to the original
 * developer so that they can be incorporated into the canonical version. It is also requested that these non-binding
 * requests be included along with the license above.
 */

using System;
using System.Runtime.InteropServices;

using int_t = System.Int32;
using char_t = System.Byte;
using long_t = System.Int64;
using pa_device_index_t = System.Int32;
using pa_host_api_index_t = System.Int32;
using pa_time_t = System.Double;
using pa_sample_format_t = System.Int64;
using pa_stream_flags_t = System.Int64;
using pa_stream_callback_flags_t = System.Int64;
using enum_default_t = System.Int32;
using pa_error_t = System.Int32;
using unsigned_long_t = System.UInt64;
using signed_long_t = System.Int64;

namespace PortAudio.Net
{
    /// <summary>
    /// Unchanging unique identifiers for each supported host API. This type is used in the
    /// <see cref="PaHostApiInfo"/> structure. The values are guaranteed to be unique and to never change, thus allowing
    /// code to be written that conditionally uses host API specific extensions. New type ids will be allocated when
    /// support for a host API reaches "public alpha" status, prior to that developers should use the paInDevelopment
    /// type id.
    /// </summary>
    /// <remarks>
    /// This enum has an exact equivalent enum in the underlying PortAudio library.
    /// </remarks>
    public enum PaHostApiTypeId : enum_default_t
    {
        paInDevelopment   =  0,
        paDirectSound     =  1,
        paMME             =  2,
        paASIO            =  3,
        paSoundManager    =  4,
        paCoreAudio       =  5,
        paOSS             =  7,
        paALSA            =  8,
        paAL              =  9,
        paBeOS            = 10,
        paWDMKS           = 11,
        paJACK            = 12,
        paWASAPI          = 13,
        paAudioScienceHPI = 14
    }

    /// <summary>
    /// A type used to specify one or more sample formats. Each value indicates a possible format for sound data passed
    /// to and from <see cref="PaStreamCallback"/>, <see cref="PaBlockingStream.ReadStream(PaBuffer)"/>,
    /// <see cref="PaBlockingStream.WriteStream(PaBuffer)"/>, and other overloads of ReadStream and WriteStream.
    /// 
    /// The standard formats <see cref="PaSampleFormat.paFloat32"/>, <see cref="PaSampleFormat.paInt16"/>,
    /// <see cref="PaSampleFormat.paInt32"/>, <see cref="PaSampleFormat.paInt24"/>, <see cref="PaSampleFormat.paInt8"/>
    /// and <see cref="PaSampleFormat.aUInt8"/> are usually implemented by all implementations.
    ///
    /// The floating point representation (<see cref="PaSampleFormat.paFloat32"/>) uses +1.0 and -1.0 as the maximum and
    /// minimum respectively.
    /// 
    /// paUInt8 is an unsigned 8 bit format where 128 is considered "ground"
    ///
    /// The paNonInterleaved flag indicates that audio data is passed as a <see cref="PaNonInterleavedBuffer&lt;T&gt;"/>
    /// containing an array of pointers to separate buffers, one buffer for each channel.static Usually, when this flag
    /// is not used, audio data is passed as a single <see cref="PaBuffer&lt;T&gt;"/> with all channels interleaved. The
    /// type parameter <code>T</code> is determined by the format.
    /// 
    /// Custom formats are represented by the base class <see cref="PaBuffer"/> regardless of the paNonInterleaved flag.
    /// </summary>
    /// <remarks>
    /// This enum has an integer typedef equivalent in the underlying PortAudio library.
    /// </remarks>
    public enum PaSampleFormat : pa_sample_format_t
    {
        paFloat32        = 0x00000001,
        paInt32          = 0x00000002,
        paInt24          = 0x00000004,
        paInt16          = 0x00000008,
        paInt8           = 0x00000010,
        paUInt8          = 0x00000020,
        paCustomFormat   = 0x00010000,
        paNonInterleaved = 0x80000000
    }

    public enum PaStreamCallbackResult : enum_default_t
    {
        paContinue = 0,
        paComplete = 1,
        paAbort    = 2
    }

    public enum PaStreamFlags : pa_stream_flags_t
    {
        paNoFlag                                = 0x00000000,
        paClipOff                               = 0x00000001,
        paDitherOff                             = 0x00000002,
        paNeverDropInput                        = 0x00000004,
        paPrimeOutputBuffersUsingStreamCallback = 0x00000008,
        paPlatformSpecificFlags                 = 0xFFFF0000
    }

    public enum PaStreamCallbackFlags : pa_stream_callback_flags_t
    {
        paInputUnderflow  = 0x00000001,
        paInputOverflow   = 0x00000002,
        paOutputUnderflow = 0x00000004,
        paOutputOverflow  = 0x00000008,
        paPrimingOutput   = 0x00000010
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaVersionInfo
    {
        public int_t versionMajor;
        public int_t versionMinor;
        public int_t versionSubMinor;
        [MarshalAs(UnmanagedType.LPStr)]
        public string versionControlRevision;
        [MarshalAs(UnmanagedType.LPStr)]
        public string versionText;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaHostApiInfo
    {
        public int_t structVersion;
        public PaHostApiTypeId type;
        [MarshalAs(UnmanagedType.LPStr)]
        public string name;
        public int_t deviceCount;
        public pa_device_index_t defaultInputDevice;
        public pa_device_index_t defaultOutputDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaHostErrorInfo
    {
        public PaHostApiTypeId hostApiType;
        public long_t errorCode;
        [MarshalAs(UnmanagedType.LPStr)]
        public string errorText;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaDeviceInfo
    {
        public int_t structVersion;
        [MarshalAs(UnmanagedType.LPStr)]
        public string name;
        public pa_host_api_index_t hostApi;
        public int_t maxInputChannels;
        public int_t maxOutputChannels;
        public pa_time_t defaultLowInputLatency;
        public pa_time_t defaultLowOutputLatency;
        public pa_time_t defaultHighInputLatency;
        public pa_time_t defaultHighOutputLatency;
        public double defaultSampleRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaStreamParameters
    {
        public pa_device_index_t device;
        public int_t channelCount;
        public PaSampleFormat sampleFormat;
        public pa_time_t suggestedLatency;
        public IntPtr hostApiSpecificStreamInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaStreamCallbackTimeInfo
    {
        public pa_time_t inputBufferAdcTime;
        public pa_time_t currentTime;
        public pa_time_t outputBufferDacTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaStreamInfo
    {
        public int_t structVersion;
        public pa_time_t inputLatency;
        public pa_time_t outputLatency;
        public double sampleRate;
    }

    internal unsafe delegate PaStreamCallbackResult _PaStreamCallback(
        void* input, void* output,
        unsigned_long_t frameCount, IntPtr timeInfo,
        PaStreamCallbackFlags statusFlags, IntPtr userData);
        
    internal delegate void _PaStreamFinishedCallback(IntPtr userData);

    public delegate PaStreamCallbackResult PaStreamCallback(
        PaBuffer input, PaBuffer output,
        int_t frameCount, PaStreamCallbackTimeInfo timeInfo,
        PaStreamCallbackFlags statusFlags, object userData);

    public delegate void PaStreamFinishedCallback(object userData);

    internal class PaBindings
    {
        [DllImport("libportaudio", EntryPoint = "Pa_GetVersion")]
        public static extern int_t Pa_GetVersion();

        [DllImport("libportaudio", EntryPoint = "Pa_GetVersionInfo")]
        public unsafe static extern IntPtr Pa_GetVersionInfo();

        [DllImport("libportaudio", EntryPoint = "Pa_GetErrorText")]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public static extern string Pa_GetErrorText(pa_error_t errorCode);

        [DllImport("libportaudio", EntryPoint = "Pa_Initialize")]
        public static extern pa_error_t Pa_Initialize();

        [DllImport("libportaudio", EntryPoint = "Pa_Terminate")]
        public static extern pa_error_t Pa_Terminate();

        [DllImport("libportaudio", EntryPoint = "Pa_GetHostApiCount")]
        public static extern pa_host_api_index_t Pa_GetHostApiCount();

        [DllImport("libportaudio", EntryPoint = "Pa_GetDefaultHostApi")]
        public static extern pa_host_api_index_t Pa_GetDefaultHostApi();

        [DllImport("libportaudio", EntryPoint = "Pa_GetHostApiInfo")]
        public static extern IntPtr Pa_GetHostApiInfo(pa_host_api_index_t hostApi);

        [DllImport("libportaudio", EntryPoint = "Pa_HostApiTypeIdToHostApiIndex")]
        public static extern pa_host_api_index_t Pa_HostApiTypeIdToHostApiIndex(PaHostApiTypeId type);

        [DllImport("libportaudio", EntryPoint = "Pa_HostApiDeviceIndexToDeviceIndex")]
        public static extern pa_device_index_t Pa_HostApiDeviceIndexToDeviceIndex(
            pa_host_api_index_t hostApi, int hostApiDeviceIndex);

        [DllImport("libportaudio", EntryPoint = "Pa_GetLastHostErrorInfo")]
        public static extern IntPtr Pa_GetLastHostErrorInfo();

        [DllImport("libportaudio", EntryPoint = "Pa_GetDeviceCount")]
        public static extern pa_device_index_t Pa_GetDeviceCount();

        [DllImport("libportaudio", EntryPoint = "Pa_GetDefaultInputDevice")]
        public static extern pa_device_index_t Pa_GetDefaultInputDevice();

        [DllImport("libportaudio", EntryPoint = "Pa_GetDefaultOutputDevice")]
        public static extern pa_device_index_t Pa_GetDefaultOutputDevice();

        [DllImport("libportaudio", EntryPoint = "Pa_GetDeviceInfo")]
        public static extern IntPtr Pa_GetDeviceInfo(pa_device_index_t device);

        [DllImport("libportaudio", EntryPoint = "Pa_IsFormatSupported")]
        public static extern pa_error_t Pa_IsFormatSupported(
            IntPtr inputParameters, IntPtr outputParameters, double sampleRate);

        [DllImport("libportaudio", EntryPoint = "Pa_OpenStream")]
        public static extern pa_error_t Pa_OpenStream(
            IntPtr stream,
            IntPtr inputParameters, IntPtr outputParameters,
            double sampleRate, unsigned_long_t framesPerBuffer, PaStreamFlags streamFlags,
            _PaStreamCallback streamCallback, IntPtr userData);
        
        [DllImport("libportaudio", EntryPoint = "Pa_OpenDefaultStream")]
        public static extern pa_error_t Pa_OpenDefaultStream(
            IntPtr stream,
            int_t numInputChannels, int_t numOutputChannels, PaSampleFormat sampleFormat,
            double sampleRate, unsigned_long_t framesPerBuffer, PaStreamFlags streamFlags,
            _PaStreamCallback streamCallback, IntPtr userData);
        
        [DllImport("libportaudio", EntryPoint = "Pa_CloseStream")]
        public static extern pa_error_t Pa_CloseStream(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_SetStreamFinishedCallback")]
        public static extern pa_error_t Pa_SetStreamFinishedCallback(
            IntPtr stream, _PaStreamFinishedCallback streamFinishedCallback);

        [DllImport("libportaudio", EntryPoint = "Pa_StartStream")]
        public static extern pa_error_t Pa_StartStream(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_StopStream")]
        public static extern pa_error_t Pa_StopStream(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_AbortStream")]
        public static extern pa_error_t Pa_AbortStream(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_IsStreamStopped")]
        public static extern pa_error_t Pa_IsStreamStopped(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_IsStreamActive")]
        public static extern pa_error_t Pa_IsStreamActive(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_GetStreamInfo")]
        public static extern IntPtr Pa_GetStreamInfo(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_GetStreamTime")]
        public static extern pa_time_t Pa_GetStreamTime(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_GetStreamCpuLoad")]
        public static extern double Pa_GetStreamCpuLoad(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_ReadStream")]
        public static extern  pa_error_t Pa_ReadStream(IntPtr stream, IntPtr buffer, unsigned_long_t frames);

        [DllImport("libportaudio", EntryPoint = "Pa_WriteStream")]
        public  static extern pa_error_t Pa_WriteStream(IntPtr stream, IntPtr buffer, unsigned_long_t frames);

        [DllImport("libportaudio", EntryPoint = "Pa_GetStreamReadAvailable")]
        public static extern signed_long_t Pa_GetStreamReadAvailable(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_GetStreamWriteAvailable")]
        public static extern signed_long_t Pa_GetStreamWriteAvailable(IntPtr stream);

        [DllImport("libportaudio", EntryPoint = "Pa_GetSampleSize")]
        public static extern pa_error_t Pa_GetSampleSize(PaSampleFormat format);

        [DllImport("libportaudio", EntryPoint = "Pa_Sleep")]
        public static extern void Pa_Sleep(long_t msec);
    }
}