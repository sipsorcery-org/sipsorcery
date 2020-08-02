using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.PortAudio.Native
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaStreamCallbackResult PaStreamCallback(IntPtr input, IntPtr output, ulong frameCount,
        in PaStreamCallbackTimeInfo timeInfo, PaStreamCallbackFlags statusFlags, IntPtr userData);
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PaStreamFinishedCallback(IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int PaGetVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr PaGetVersionTextDelegate();

    // Note: this function is not published in the shared objects by default, so I'm ignoring it.
    /*[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ref PaVersionInfo PaGetVersionInfoDelegate();*/

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr PaGetErrorTextDelegate(PaErrorCode errorCode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaInitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaTerminateDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaHostApiIndex PaGetHostApiCountDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaHostApiIndex PaGetDefaultHostApiDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ref PaHostApiInfo PaGetHostApiInfoDelegate(PaHostApiIndex hostApi);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaHostApiIndex PaHostApiTypeIdToHostApiIndexDelegate(PaHostApiTypeId type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaDeviceIndex PaHostApiDeviceIndexToDeviceIndexDelegate(PaHostApiIndex hostApi, int hostApiDeviceIndex);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ref PaHostErrorInfo PaGetLastHostErrorInfoDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaDeviceIndex PaGetDeviceCountDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaDeviceIndex PaGetDefaultInputDeviceDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaDeviceIndex PaGetDefaultOutputDeviceDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ref PaDeviceInfo PaGetDeviceInfoDelegate(PaDeviceIndex device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate PaErrorCode PaIsFormatSupportedDelegate(PaStreamParameters* inputParameters,
        PaStreamParameters* outputParameters, double sampleRate);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate PaErrorCode PaOpenStreamDelegate(out PaStream stream, PaStreamParameters* inputParameters,
        PaStreamParameters* outputParameters, double sampleRate, ulong framesPerBuffer, PaStreamFlags streamFlags,
        PaStreamCallback streamCallback, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaOpenDefaultStreamDelegate(out PaStream stream, int numInputChannels,
        int numOutputChannels, PaSampleFormats sampleFormat, double sampleRate, ulong framesPerBUffer,
        PaStreamCallback streamCallback, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaCloseStreamDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaSetStreamFinishedCallbackDelegate(PaStream stream,
        PaStreamFinishedCallback streamFinishedCallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaStartStreamDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaStopStreamDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaAbortStreamDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaIsStreamStoppedDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaIsStreamActiveDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ref PaStreamInfo PaGetStreamInfoDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaTime PaGetStreamTimeDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate double PaGetStreamCpuLoadDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaReadStreamDelegate(PaStream stream, IntPtr buffer, ulong frames);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaWriteStreamDelegate(PaStream stream, IntPtr buffer, ulong frames);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate long PaGetStreamReadAvailableDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate long PaGetStreamWriteAvailableDelegate(PaStream stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate PaErrorCode PaGetSampleSize(PaSampleFormats sameFormats);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PaSleepDelegate(long msec);
}
