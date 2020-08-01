namespace ProjectCeilidh.PortAudio.Native
{
    internal readonly struct PaStreamInfo
    {
        public int StructVersion { get; }
        public PaTime InputLatency { get; }
        public PaTime OutputLatency { get; }
        public double SampleRate { get; }
    }
}
