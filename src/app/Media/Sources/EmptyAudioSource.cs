using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Media
{

    /// <summary>
    /// This class is handy when utilizing music when there is no need for speaker or mic integration
    /// See MusicMediaSession.
    /// </summary>
    public class EmptyAudioSource : IAudioSource
    {

        private readonly List<AudioFormat> _audioFormats = new List<AudioFormat>();
#pragma warning disable CS0067
        public event EncodedSampleDelegate OnAudioSourceEncodedSample;
        public event RawAudioSampleDelegate OnAudioSourceRawSample;
        public event SourceErrorDelegate OnAudioSourceError;

        protected bool _isStarted;
        protected bool _isPaused;
        protected bool _isClosed;
#pragma warning restore CS0067

        public EmptyAudioSource()
        {
        }
        public EmptyAudioSource(params AudioFormat[] audioFormats)
        {
            _audioFormats = new List<AudioFormat>(audioFormats);
        }

        public Task CloseAudio()
        {
            _isClosed = true;
            return Task.CompletedTask;
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            throw new NotImplementedException();//not needed
        }

        public List<AudioFormat> GetAudioSourceFormats()
        {
            return _audioFormats;
        }

        public bool HasEncodedAudioSubscribers()
        {
            return true;
        }

        public bool IsAudioSourcePaused()
        {
            return _isPaused;
        }

        public Task PauseAudio()
        {
            _isPaused = true;
            return Task.CompletedTask;
        }

        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
        }

        public Task ResumeAudio()
        {
            _isPaused = false;
            return Task.CompletedTask;
        }

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            _audioFormats.Add(audioFormat);
        }

        public Task StartAudio()
        {
            _isStarted = true;
            return Task.CompletedTask;
        }
    }
}
