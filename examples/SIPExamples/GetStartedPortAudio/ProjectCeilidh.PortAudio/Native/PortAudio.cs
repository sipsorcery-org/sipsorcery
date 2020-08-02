using System;
using ProjectCeilidh.PortAudio.Platform;
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global

namespace ProjectCeilidh.PortAudio.Native
{
    internal static class PortAudio
    {
        public static readonly NativeLibraryHandle LibraryHandle =
            LibraryLoader.LoaderForPlatform.LoadNativeLibrary("portaudio", new Version(2, 0, 0));

        /// <summary>
        /// Retrieve the release number of the currently running PortAudio build.
        /// For example, for version "19.5.1" this will return 0x00130501
        /// </summary>
        public static readonly PaGetVersionDelegate Pa_GetVersion =
            LibraryHandle.GetSymbolDelegate<PaGetVersionDelegate>(nameof(Pa_GetVersion));

        
        /*
        /// <summary>
        /// Retrieve version information for the currently running PortAudio build.
        /// </summary>
        public static readonly PaGetVersionInfoDelegate Pa_GetVersionInfo =
            LibraryHandle.GetSymbolDelegate<PaGetVersionInfoDelegate>(nameof(Pa_GetVersionInfo));
            */

        /// <summary>
        /// Retrieve a textual description of the current PortAudio build, e.g. "PortAudio V19.5.0-devel, revision 1952M".
        /// The format of the text may change in the future. Do not try to parse the returned string.
        /// </summary>
        [Obsolete("As of 19.5.0, use Pa_getVersionInfo()->versionText instead.", false)]
        public static readonly PaGetVersionTextDelegate Pa_GetVersionText =
            LibraryHandle.GetSymbolDelegate<PaGetVersionTextDelegate>(nameof(Pa_GetVersionText));

        /// <summary>
        /// Translate the supplied PortAudio error code into a human-readable message.
        /// </summary>
        public static readonly PaGetErrorTextDelegate Pa_GetErrorText =
            LibraryHandle.GetSymbolDelegate<PaGetErrorTextDelegate>(nameof(Pa_GetErrorText));

        /// <summary>
        /// Library initialization function - call this before using PortAudio.
        /// This function initializes internal data structures and prepares underlying host APIs for use.
        /// With the exception of <see cref="Pa_GetVersion"/>, <see cref="Pa_GetVersionText"/>, and <see cref="Pa_GetErrorText"/>, this function MUST be called before using any other PortAudio API functions.
        /// </summary>
        /// <seealso cref="Pa_Terminate"/>
        public static readonly PaInitializeDelegate Pa_Initialize =
            LibraryHandle.GetSymbolDelegate<PaInitializeDelegate>(nameof(Pa_Initialize));

        /// <summary>
        /// Library termination function - call this when finished using PortAudio.
        /// This function deallocates all resources allocated by PortAudio since it was initialized by a call to <see cref="Pa_Initialize"/>.
        /// In cases where Pa_Initialize has been called multiple times, each call must be matched with a corresponding call to <see cref="Pa_Terminate"/>.
        /// The final matching call to <see cref="Pa_Terminate"/> will automatically close any PortAudio streams that are still open.
        /// <see cref="Pa_Terminate"/> MUST be called before exiting a program which uses PortAudio. Failure to do so may result in serious resource leaks, such as audio devices not being available until the next reboot.
        /// </summary>
        /// <seealso cref="Pa_Initialize"/>
        public static readonly PaTerminateDelegate Pa_Terminate =
            LibraryHandle.GetSymbolDelegate<PaTerminateDelegate>(nameof(Pa_Terminate));

        /// <summary>
        /// Retrieve the number of available host APIs. Even if a host API is available it may have no devices available.
        /// </summary>
        public static readonly PaGetHostApiCountDelegate Pa_GetHostApiCount =
            LibraryHandle.GetSymbolDelegate<PaGetHostApiCountDelegate>(nameof(Pa_GetHostApiCount));

        /// <summary>
        /// Retrieve the index of the default API. The default host API will be the lowest common denominator host API on the current platform and is unlikely to provide the best performance.
        /// </summary>
        public static readonly PaGetDefaultHostApiDelegate Pa_GetDefaultHostApi =
            LibraryHandle.GetSymbolDelegate<PaGetDefaultHostApiDelegate>(nameof(Pa_GetDefaultHostApi));

        /// <summary>
        /// Retrieve a pointer to a structure containing information about a specific host API.
        /// </summary>
        public static readonly PaGetHostApiInfoDelegate Pa_GetHostApiInfo =
            LibraryHandle.GetSymbolDelegate<PaGetHostApiInfoDelegate>(nameof(Pa_GetHostApiInfo));

        /// <summary>
        /// Convert a static host API unique identifier, into a runtime host API index.
        /// </summary>
        public static readonly PaHostApiTypeIdToHostApiIndexDelegate Pa_HostApiTypeIdToHostApiIndex =
            LibraryHandle.GetSymbolDelegate<PaHostApiTypeIdToHostApiIndexDelegate>(nameof(Pa_HostApiTypeIdToHostApiIndex));

        /// <summary>
        /// Convert a host-API-specific device index to standard PortAudio device index.
        /// This funciton may be used in conjunction with the <see cref="PaHostApiInfo.DeviceCount"/> field to enumerate all devices for the specified host API.
        /// </summary>
        public static readonly PaHostApiDeviceIndexToDeviceIndexDelegate Pa_HostApiDeviceIndexToDeviceIndex =
            LibraryHandle.GetSymbolDelegate<PaHostApiDeviceIndexToDeviceIndexDelegate>(nameof(Pa_HostApiDeviceIndexToDeviceIndex));

        /// <summary>
        /// Return information about the last error encountered.
        /// The error information returned by <see cref="Pa_GetLastHostErrorInfo"/> will never be modified asynchronously by errors occurring in other PortAudio owned threads (such as the thread that manages the stream callback).
        /// This function is provided as a last resort, primarily to enhance debugging by providing clients with access to all available error information.
        /// </summary>
        public static readonly PaGetLastHostErrorInfoDelegate Pa_GetLastHostErrorInfo =
            LibraryHandle.GetSymbolDelegate<PaGetLastHostErrorInfoDelegate>(nameof(Pa_GetLastHostErrorInfo));

        /// <summary>
        /// Retrieve the number of available devices. The number of available devices may be zero.
        /// </summary>
        public static readonly PaGetDeviceCountDelegate Pa_GetDeviceCount =
            LibraryHandle.GetSymbolDelegate<PaGetDeviceCountDelegate>(nameof(Pa_GetDeviceCount));

        /// <summary>
        /// Retrieve the index of the default input device. The result can be usid to in the inputDevice parameter to <see cref="Pa_OpenStream"/>.
        /// </summary>
        public static readonly PaGetDefaultInputDeviceDelegate Pa_GetDefaultInputDevice =
            LibraryHandle.GetSymbolDelegate<PaGetDefaultInputDeviceDelegate>(nameof(Pa_GetDefaultInputDevice));

        /// <summary>
        /// Retrieve the index of the default output device. The result can be used in the outputDevice parameter to <see cref="Pa_OpenStream"/>.
        /// </summary>
        public static readonly PaGetDefaultOutputDeviceDelegate Pa_GetDefaultOutputDevice =
            LibraryHandle.GetSymbolDelegate<PaGetDefaultOutputDeviceDelegate>(nameof(Pa_GetDefaultOutputDevice));

        /// <summary>
        /// Retrieve a pointer to a <see cref="PaDeviceInfo"/> structure containing information about the specified device.
        /// </summary>
        public static readonly PaGetDeviceInfoDelegate Pa_GetDeviceInfo =
            LibraryHandle.GetSymbolDelegate<PaGetDeviceInfoDelegate>(nameof(Pa_GetDeviceInfo));

        /// <summary>
        /// Determine whether it would be possible to open a stream with the specified parameters.
        /// </summary>
        public static readonly PaIsFormatSupportedDelegate Pa_IsFormatSupported =
            LibraryHandle.GetSymbolDelegate<PaIsFormatSupportedDelegate>(nameof(Pa_IsFormatSupported));

        /// <summary>
        /// Open a stream for either input, output or both.
        /// </summary>
        public static readonly PaOpenStreamDelegate Pa_OpenStream =
            LibraryHandle.GetSymbolDelegate<PaOpenStreamDelegate>(nameof(Pa_OpenStream));

        /// <summary>
        /// A simplified version of <see cref="Pa_OpenStream"/> that opens the default input and/or output devices.
        /// </summary>
        public static readonly PaOpenDefaultStreamDelegate Pa_OpenDefaultStream =
            LibraryHandle.GetSymbolDelegate<PaOpenDefaultStreamDelegate>(nameof(Pa_OpenDefaultStream));

        /// <summary>
        /// Closes an audio stream. If the audio stream is active it discards any pending buffers as if <see cref="Pa_AbortStream"/> had been called.
        /// </summary>
        public static readonly PaCloseStreamDelegate Pa_CloseStream =
            LibraryHandle.GetSymbolDelegate<PaCloseStreamDelegate>(nameof(Pa_CloseStream));

        /// <summary>
        /// Register a stream finished callback funciton which will be called when the stream becomes inactive. See the description of <see cref="PaStreamFinishedCallback"/> for futher details about when the callback is called.
        /// </summary>
        public static readonly PaSetStreamFinishedCallbackDelegate Pa_SetStreamFinishedCallback =
            LibraryHandle.GetSymbolDelegate<PaSetStreamFinishedCallbackDelegate>(nameof(Pa_SetStreamFinishedCallback));

        /// <summary>
        /// Commences audio processing.
        /// </summary>
        public static readonly PaStartStreamDelegate Pa_StartStream =
            LibraryHandle.GetSymbolDelegate<PaStartStreamDelegate>(nameof(Pa_StartStream));

        /// <summary>
        /// Terminates audio processing. It waits until all pending audio buffers have been played before it returns.
        /// </summary>
        public static readonly PaStopStreamDelegate Pa_StopStream =
            LibraryHandle.GetSymbolDelegate<PaStopStreamDelegate>(nameof(Pa_StopStream));

        /// <summary>
        /// Terminates audio processing immediately without waiting for pending buffers to complete.
        /// </summary>
        public static readonly PaAbortStreamDelegate Pa_AbortStream =
            LibraryHandle.GetSymbolDelegate<PaAbortStreamDelegate>(nameof(Pa_AbortStream));

        /// <summary>
        /// Determine whether the stream is stopped.
        /// A stream is considered to be stopped prior to a successfull call to <see cref="Pa_StartStream"/> and after a successful call to <see cref="Pa_StopStream"/> or <see cref="Pa_AbortStream"/>.
        /// If a stream callback returns a value other than <see cref="PaStreamCallbackResult.Continue"/> the stream is NOT considered to be stopped.
        /// </summary>
        public static readonly PaIsStreamStoppedDelegate Pa_IsStreamStopped =
            LibraryHandle.GetSymbolDelegate<PaIsStreamStoppedDelegate>(nameof(Pa_IsStreamStopped));

        /// <summary>
        /// Determine whether the stream is active.
        /// A stream is active after a successful call to <see cref="Pa_StartStream"/>, until it becomes inactive either as a result of a call to <see cref="Pa_StopStream"/> or <see cref="Pa_AbortStream"/>,
        /// or as a result of a return value other than <see cref="PaStreamCallbackResult.Continue"/> from the stream callback.
        /// In the latter case, the stream is considered inactive after the last buffered has finished playing.
        /// </summary>
        public static readonly PaIsStreamActiveDelegate Pa_IsStreamActive =
            LibraryHandle.GetSymbolDelegate<PaIsStreamActiveDelegate>(nameof(Pa_IsStreamActive));

        /// <summary>
        /// Retrieve a pointer to a <see cref="PaStreamInfo"/> structure containing information about the specified stream.
        /// </summary>
        public static readonly PaGetStreamInfoDelegate Pa_GetStreamInfo =
            LibraryHandle.GetSymbolDelegate<PaGetStreamInfoDelegate>(nameof(Pa_GetStreamInfo));

        /// <summary>
        /// Returns the current time in seconds for a stream according to the same clock used to generate callback <see cref="PaStreamCallbackTimeInfo"/> timestamps.
        /// The time values are monotonically increasing and have unspecified origin.
        /// <see cref="Pa_GetStreamTime"/> returns valid time values for the entire life of the stream, from when the stream is opened until it is closed.
        /// Starting and stopping the stream does not affect the passage of time returned by <see cref="Pa_GetStreamTime"/>.
        /// This time may be used for synchronizing other events to the audio stream, for example synchronizing audio to MIDI.
        /// </summary>
        public static readonly PaGetStreamTimeDelegate Pa_GetStreamTime =
            LibraryHandle.GetSymbolDelegate<PaGetStreamTimeDelegate>(nameof(Pa_GetStreamTime));

        /// <summary>
        /// Retrieve CPU usage information for the specified stream. The "CPU Load" is a fractional of total CPU time consumed by a callback stream's audio processing routines including, but not limited to the client supplied stream callback.
        /// This function does not work with blocking read/write streams.
        /// This function may be called from the stream callback function or the application.
        /// </summary>
        public static readonly PaGetStreamCpuLoadDelegate Pa_GetStreamCpuLoad =
            LibraryHandle.GetSymbolDelegate<PaGetStreamCpuLoadDelegate>(nameof(Pa_GetStreamCpuLoad));

        /// <summary>
        /// Read samples from an input stream. The function doesn't return until the entire buffer has been filled - this may involve waiting fro the operating system to supply the data.
        /// </summary>
        public static readonly PaReadStreamDelegate Pa_ReadStream =
            LibraryHandle.GetSymbolDelegate<PaReadStreamDelegate>(nameof(Pa_ReadStream));

        /// <summary>
        /// Write samples to an output stream. The function doesn't return until the entire buffer has been written - this may involve waiting for the operating system to consume the data.
        /// </summary>
        public static readonly PaWriteStreamDelegate Pa_WriteStream =
            LibraryHandle.GetSymbolDelegate<PaWriteStreamDelegate>(nameof(Pa_WriteStream));

        /// <summary>
        /// Retrieve the number of frame that can be read from the stream without waiting.
        /// </summary>
        public static readonly PaGetStreamReadAvailableDelegate Pa_GetStreamReadAvailable =
            LibraryHandle.GetSymbolDelegate<PaGetStreamReadAvailableDelegate>(nameof(Pa_GetStreamReadAvailable));

        /// <summary>
        /// Retrieve the number of frames that can be written to the stream without waiting.
        /// </summary>
        public static readonly PaGetStreamWriteAvailableDelegate Pa_GetStreamWriteAvailable =
            LibraryHandle.GetSymbolDelegate<PaGetStreamWriteAvailableDelegate>(nameof(Pa_GetStreamWriteAvailable));

        /// <summary>
        /// Retrieve the size of a given sample format in bytes.
        /// </summary>
        public static readonly PaGetSampleSize Pa_GetSampleSize =
            LibraryHandle.GetSymbolDelegate<PaGetSampleSize>(nameof(Pa_GetSampleSize));

        /// <summary>
        /// Put the caller to sleep for at least 'msec' milliseconds. This function is provided as a convenience for authors of portable code (such as the tests and examples in the PortAudio distribution.
        /// This function may sleep longer than requested so don't rely on this for accurate musical timing.
        /// </summary>
        public static readonly PaSleepDelegate Pa_Sleep =
            LibraryHandle.GetSymbolDelegate<PaSleepDelegate>(nameof(Pa_Sleep));
    }
}
