using System;
using System.Runtime.InteropServices;
using ProjectCeilidh.PortAudio.Native;

namespace ProjectCeilidh.PortAudio
{
    public sealed class PortAudioException : Exception
    {
        private PortAudioException(string message) : base(message)
        {

        }

        internal static Exception GetException(PaErrorCode error)
        {
            var errorPtr = Native.PortAudio.Pa_GetErrorText(error);
            var errorText = Marshal.PtrToStringAnsi(errorPtr);

            switch (error)
            {
                case PaErrorCode.NoError:
                    throw new ArgumentOutOfRangeException();
                case PaErrorCode.InsufficientMemory:
                    return new OutOfMemoryException(errorText);
                case PaErrorCode.CanNotReadFromAnOutputOnlyStream:
                case PaErrorCode.CanNotReadFromACallbackStream:
                case PaErrorCode.CanNotWriteToAnInputOnlyStream:
                case PaErrorCode.CanNotWriteToACallbackStream:
                    return new InvalidOperationException(errorText);
                case PaErrorCode.InvalidFlag:
                case PaErrorCode.IncompatibleHostApiSpecificStreamInfo:
                case PaErrorCode.InvalidChannelCount:
                case PaErrorCode.BadBufferPtr:
                case PaErrorCode.BadStreamPtr:
                case PaErrorCode.BadIoDeviceCombination:
                case PaErrorCode.InvalidDevice:
                case PaErrorCode.IncompatibleStreamHostApi:
                    return new ArgumentException(errorText);
                case PaErrorCode.UnanticipatedHostError when !RuntimeInformation.IsOSPlatform(OSPlatform.Windows):
                    return new PortAudioException(Native.PortAudio.Pa_GetLastHostErrorInfo().ErrorText);
                default:
                    return new PortAudioException(errorText);
            }
        }
    }
}
