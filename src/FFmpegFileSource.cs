using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;

#pragma warning disable CS0067
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event SourceErrorDelegate? OnAudioSourceError;
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event SourceErrorDelegate? OnVideoSourceError;
#pragma warning restore CS0067

        public event Action? OnEndOfFile;

        public unsafe FFmpegFileSource(string path, bool repeat, IAudioEncoder audioEncoder, bool useVideo = true)
        {
            if (!File.Exists(path))
            {
                if (!Uri.TryCreate(path, UriKind.Absolute, out Uri result))
                    throw new ApplicationException($"Requested path is not a valid file path or not a valid Uri: {path}.");
            }

            if ((audioEncoder != null))
            {
                _FFmpegAudioSource = new FFmpegAudioSource(audioEncoder);
                _FFmpegAudioSource.CreateAudioDecoder(path, null, repeat, false);
                _FFmpegAudioSource.InitialiseDecoder();

                _FFmpegAudioSource.OnAudioSourceEncodedSample += _FFmpegAudioSource_OnAudioSourceEncodedSample;
            }

            if (useVideo)
            {
                _FFmpegVideoSource = new FFmpegVideoSource();
                _FFmpegVideoSource.CreateVideoDecoder(path, null, repeat, false);
                _FFmpegVideoSource.InitialiseDecoder();

                _FFmpegVideoSource.OnVideoSourceEncodedSample += _FFmpegVideoSource_OnVideoSourceEncodedSample;
            }
        }

        private void _FFmpegVideoSource_OnVideoSourceEncodedSample(uint durationRtpUnits, byte[] sample)
        {
            OnVideoSourceEncodedSample?.Invoke(durationRtpUnits, sample);
        }

        private void _FFmpegAudioSource_OnAudioSourceEncodedSample(uint durationRtpUnits, byte[] sample)
        {
            OnAudioSourceEncodedSample?.Invoke(durationRtpUnits, sample);
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
        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
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
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
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

        public Task Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;

                _FFmpegAudioSource?.Pause();
                _FFmpegVideoSource?.Pause();
            }

            return Task.CompletedTask;
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
            _FFmpegAudioSource?.Dispose();
            _FFmpegVideoSource?.Dispose();
        }
    }
}
