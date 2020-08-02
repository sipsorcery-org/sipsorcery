namespace ProjectCeilidh.PortAudio.Native
{
    internal enum PaErrorCode
    {
        NoError = 0,
        NotInitialized = -10000,
        UnanticipatedHostError,
        InvalidChannelCount,
        InvalidSampleRate,
        InvalidDevice,
        InvalidFlag,
        SampleFormatNotSupported,
        BadIoDeviceCombination,
        InsufficientMemory,
        BufferTooBig,
        BufferTooSmall,
        NullCallback,
        BadStreamPtr,
        TimedOut,
        InternalError,
        DeviceUnavailable,
        IncompatibleHostApiSpecificStreamInfo,
        StreamIsStopped,
        StreamIsNotStopped,
        InputOverflowed,
        OutputUnderflowed,
        HostApiNotFound,
        InvalidHostApi,
        CanNotReadFromACallbackStream,
        CanNotWriteToACallbackStream,
        CanNotReadFromAnOutputOnlyStream,
        CanNotWriteToAnInputOnlyStream,
        IncompatibleStreamHostApi,
        BadBufferPtr
    }
}
