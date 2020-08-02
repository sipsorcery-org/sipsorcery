using System;

namespace ProjectCeilidh.PortAudio.Native
{
    /// <summary>
    /// Used to represent monotonic time in seconds.
    /// </summary>
    internal struct PaTime
    {
        private readonly double _value;

        public TimeSpan Value => TimeSpan.FromSeconds(_value);

        public PaTime(TimeSpan span)
        {
            _value = span.TotalSeconds;
        }
    }
}
