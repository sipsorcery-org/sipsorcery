using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegFileSource: IAudioSource, IVideoSource, IDisposable
    {
        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegFileSource>();

        private FFmpegAudioSource ? _FFmpegAudioSource = null;
        private FFmpegVideoSource ? _FFmpegVideoSource = null;

        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;

        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
        public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster;

        public event SourceErrorDelegate? OnAudioSourceError;
        public event SourceErrorDelegate? OnVideoSourceError;

#pragma warning disable CS0067

        public event RawVideoSampleDelegate? OnVideoSourceRawSample;

#pragma warning restore CS0067

        public unsafe FFmpegFileSource(string path, bool repeat, IAudioEncoder? audioEncoder, uint audioFrameSize = 960, bool useVideo = true)
        {
            if (!File.Exists(path))
            {
                if (!Uri.TryCreate(path, UriKind.Absolute, out Uri? result))
                    throw new ApplicationException($"Requested path is not a valid file path or not a valid Uri: {path}.");
            }

            if ((audioEncoder != null))
            {
                _FFmpegAudioSource = new FFmpegAudioSource(audioEncoder, audioFrameSize);
                _FFmpegAudioSource.CreateAudioDecoder(path, null, repeat, false);

                _FFmpegAudioSource.OnAudioSourceEncodedSample += _FFmpegAudioSource_OnAudioSourceEncodedSample;
                _FFmpegAudioSource.OnAudioSourceRawSample += _FFmpegAudioSource_OnAudioSourceRawSample;
                _FFmpegAudioSource.OnAudioSourceError += _FFmpegAudioSource_OnAudioSourceError;
            }

            if (useVideo)
            {
                _FFmpegVideoSource = new FFmpegVideoSource();
                _FFmpegVideoSource.CreateVideoDecoder(path, null, repeat, false);

                _FFmpegVideoSource.OnVideoSourceEncodedSample += _FFmpegVideoSource_OnVideoSourceEncodedSample;
                _FFmpegVideoSource.OnVideoSourceRawSampleFaster += _FFmpegVideoSource_OnVideoSourceRawSampleFaster;
                _FFmpegVideoSource.OnVideoSourceError += _FFmpegVideoSource_OnVideoSourceError;
            }
        }

        private void _FFmpegAudioSource_OnAudioSourceError(string errorMessage)
        {
            OnAudioSourceError?.Invoke(errorMessage);
        }

        private void _FFmpegVideoSource_OnVideoSourceError(string errorMessage)
        {
            OnVideoSourceError?.Invoke(errorMessage);
        }

        private void _FFmpegVideoSource_OnVideoSourceEncodedSample(uint durationRtpUnits, byte[] sample)
        {
            OnVideoSourceEncodedSample?.Invoke(durationRtpUnits, sample);
        }

        private void _FFmpegVideoSource_OnVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage imageRawSample)
        {
            OnVideoSourceRawSampleFaster?.Invoke(durationMilliseconds, imageRawSample);
        }

        private void _FFmpegAudioSource_OnAudioSourceEncodedSample(uint durationRtpUnits, byte[] sample)
        {
            OnAudioSourceEncodedSample?.Invoke(durationRtpUnits, sample);
        }

        private void _FFmpegAudioSource_OnAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            OnAudioSourceRawSample?.Invoke(samplingRate, durationMilliseconds, sample);
        }

        public bool IsPaused() => _isPaused;

        public List<AudioFormat> GetAudioSourceFormats()
        {
            if (_FFmpegAudioSource != null)
                return _FFmpegAudioSource.GetAudioSourceFormats();
            return new List<AudioFormat>();
        }
        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            if (_FFmpegAudioSource != null)
            {
                logger.LogDebug($"Setting audio source format to {audioFormat.FormatID}:{audioFormat.Codec} {audioFormat.ClockRate}.");
                _FFmpegAudioSource.SetAudioSourceFormat(audioFormat);
            }
        }
        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            if (_FFmpegAudioSource != null)
                _FFmpegAudioSource.RestrictFormats(filter);
        }
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) => throw new NotImplementedException();
        
        public bool HasEncodedAudioSubscribers()
        {
            Boolean result = OnAudioSourceEncodedSample != null;
            if (_FFmpegAudioSource != null)
            {
                if (result)
                    _FFmpegAudioSource.OnAudioSourceEncodedSample += _FFmpegAudioSource_OnAudioSourceEncodedSample;
                else
                    _FFmpegAudioSource.OnAudioSourceEncodedSample -= _FFmpegAudioSource_OnAudioSourceEncodedSample;
            }

            return result;
        }

        public bool HasRawAudioSubscribers()
        {
            Boolean result = OnAudioSourceRawSample!= null;
            if (_FFmpegAudioSource != null)
            {
                if (result)
                    _FFmpegAudioSource.OnAudioSourceRawSample += _FFmpegAudioSource_OnAudioSourceRawSample;
                else
                    _FFmpegAudioSource.OnAudioSourceRawSample -= _FFmpegAudioSource_OnAudioSourceRawSample;
            }

            return result;
        }

        public bool IsAudioSourcePaused() => _isPaused;
        public Task StartAudio() => Start();
        public Task PauseAudio() => Pause();
        public Task ResumeAudio() => Resume();
        public Task CloseAudio() => Close();

        public List<VideoFormat> GetVideoSourceFormats()
        {
            if (_FFmpegVideoSource != null)
                return _FFmpegVideoSource.GetVideoSourceFormats();
            return new List<VideoFormat>();
        }

        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            if (_FFmpegVideoSource != null)
            {
                logger.LogDebug($"Setting video source format to {videoFormat.FormatID}:{videoFormat.Codec} {videoFormat.ClockRate}.");
                _FFmpegVideoSource.SetVideoSourceFormat(videoFormat);
            }
        }
       
        public void RestrictFormats(Func<VideoFormat, bool> filter)
        {
            if (_FFmpegVideoSource != null)
                _FFmpegVideoSource.RestrictFormats(filter);
        }

        public void ForceKeyFrame() => _FFmpegVideoSource?.ForceKeyFrame();
        
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) => throw new NotImplementedException();
        public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) => throw new NotImplementedException();

        public bool HasEncodedVideoSubscribers()
        {
            Boolean result =  OnVideoSourceEncodedSample != null;
            if (_FFmpegVideoSource != null)
            {
                if (result)
                    _FFmpegVideoSource.OnVideoSourceEncodedSample += _FFmpegVideoSource_OnVideoSourceEncodedSample;
                else
                    _FFmpegVideoSource.OnVideoSourceEncodedSample -= _FFmpegVideoSource_OnVideoSourceEncodedSample;
            }

            return result;
        }

        public bool HasRawVideoSubscribers()
        {
            Boolean result = OnVideoSourceRawSampleFaster != null;
            if (_FFmpegVideoSource != null)
            {
                if (result)
                    _FFmpegVideoSource.OnVideoSourceRawSampleFaster += _FFmpegVideoSource_OnVideoSourceRawSampleFaster;
                else
                    _FFmpegVideoSource.OnVideoSourceRawSampleFaster -= _FFmpegVideoSource_OnVideoSourceRawSampleFaster;
            }

            return result;
        }

        public bool IsVideoSourcePaused() => _isPaused;
        public Task StartVideo() => Start();
        public Task PauseVideo() => Pause();
        public Task ResumeVideo() => Resume();
        public Task CloseVideo() => Close();

        public Task Start()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _FFmpegAudioSource?.Start();
                _FFmpegVideoSource?.Start();
            }

            return Task.CompletedTask;
        }

        public async Task Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                if (_FFmpegAudioSource != null)
                    await _FFmpegAudioSource.Close();

                if (_FFmpegVideoSource != null)
                    await _FFmpegVideoSource.Close();

                Dispose();
            }
        }

        public async Task Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;

                if (_FFmpegAudioSource != null)
                    await _FFmpegAudioSource.Pause();

                if (_FFmpegVideoSource != null)
                    await  _FFmpegVideoSource.Pause();
            }
        }

        public async Task Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;

                if (_FFmpegAudioSource != null)
                    await _FFmpegAudioSource.Resume();

                if (_FFmpegVideoSource != null)
                    await _FFmpegVideoSource.Resume();
            }
        }

        public void Dispose()
        {
            if (_FFmpegAudioSource != null)
            {
                _FFmpegAudioSource.OnAudioSourceEncodedSample -= _FFmpegAudioSource_OnAudioSourceEncodedSample;
                _FFmpegAudioSource.OnAudioSourceRawSample -= _FFmpegAudioSource_OnAudioSourceRawSample;
                _FFmpegAudioSource.OnAudioSourceError -= _FFmpegAudioSource_OnAudioSourceError;

                _FFmpegAudioSource.Dispose();

                _FFmpegAudioSource = null;
            }

            if (_FFmpegVideoSource != null)
            { 
                _FFmpegVideoSource.OnVideoSourceEncodedSample -= _FFmpegVideoSource_OnVideoSourceEncodedSample;
                _FFmpegVideoSource.OnVideoSourceRawSampleFaster -= _FFmpegVideoSource_OnVideoSourceRawSampleFaster;
                _FFmpegVideoSource.OnVideoSourceError -= _FFmpegVideoSource_OnVideoSourceError;

                _FFmpegVideoSource.Dispose();

                _FFmpegVideoSource = null;
            }
        }
    }
}
