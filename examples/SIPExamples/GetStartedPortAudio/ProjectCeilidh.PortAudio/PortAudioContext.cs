using System;

namespace ProjectCeilidh.PortAudio
{
    /// <summary>
    /// Used to wrap a set of calls to the PortAudio API that require initialization.
    /// </summary>
    /// <inheritdoc />
    internal class PortAudioContext : IDisposable
    {
        private PortAudioContext()
        {
            PortAudioLifetimeRegistry.Register(this);
        }

        private void ReleaseUnmanagedResources()
        {
            PortAudioLifetimeRegistry.UnRegister(this);
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~PortAudioContext()
        {
            ReleaseUnmanagedResources();
        }

        /// <summary>
        /// Enter a state where you are gaurenteed to be allowed to invoke PortAudio API functions.
        /// </summary>
        /// <returns>A handle that gates the usage of the PortAudio API.</returns>
        public static IDisposable EnterContext() => new PortAudioContext();
    }
}
