using System;
using AVFoundation;

namespace SIPSorceryMedia.MacOS
{
    public sealed class MacAudioSink : IDisposable
    {
        private readonly AVAudioEngine _audioEngine = new AVAudioEngine();

        public void Dispose()
        {
            _audioEngine.Dispose();
        }
    }
}