namespace ProjectCeilidh.PortAudio.Native
{
    internal readonly struct PaStreamCallbackTimeInfo
    {
        public PaTime InputBufferAdcTime { get; }
        public PaTime CurrentTime { get; }
        public PaTime OutputBufferDacTime { get; }
    }
}
