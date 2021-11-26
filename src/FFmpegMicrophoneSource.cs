using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegMicrophoneSource : IAudioSource, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAME_RATE = 30;

        private static List<AudioFormat> _supportedAudioFormats = new List<AudioFormat>
        {
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.G722)
        };


        public ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegFileSource>();

        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;

        private FFmpegAudioDecoder _audioDecoder;
        private IAudioEncoder _audioEncoder;

        private MediaFormatManager<AudioFormat> _audioFormatManager;

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;

#pragma warning disable CS0067
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event SourceErrorDelegate? OnAudioSourceError;
#pragma warning restore CS0067

        public event Action? OnEndOfFile;

        public unsafe FFmpegMicrophoneSource(string path, IAudioEncoder audioEncoder)
        {
            string inputFormat = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dshow"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "alsa"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "avfoundation"
                : throw new NotSupportedException($"Cannot find adequate input format - OSArchitecture:[{RuntimeInformation.OSArchitecture}] - OSDescription:[{RuntimeInformation.OSDescription}]");

            AVInputFormat* aVInputFormat = ffmpeg.av_find_input_format(inputFormat);

            _audioFormatManager = new MediaFormatManager<AudioFormat>(_supportedAudioFormats);
            _audioEncoder = audioEncoder;

            _audioDecoder = new FFmpegAudioDecoder(path, aVInputFormat, false, true);
            _audioDecoder.OnAudioFrame += FileSourceDecoder_OnAudioFrame;

            _audioDecoder.OnEndOfFile += () =>
            {
                logger.LogDebug($"File source decode complete for {path}.");
                OnEndOfFile?.Invoke();
                _audioDecoder.Dispose();
            };

            Initialise();
        }

        private void Initialise()
        {
            _audioDecoder.InitialiseSource();
        }
        public bool IsPaused() => _isPaused;

        public List<AudioFormat> GetAudioSourceFormats()
        {
            if (_audioFormatManager != null)
                return _audioFormatManager.GetSourceFormats();
            return new List<AudioFormat>();
        }
        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            if (_audioFormatManager != null)
            {
                logger.LogDebug($"Setting audio source format to {audioFormat.FormatID}:{audioFormat.Codec} {audioFormat.ClockRate}.");
                _audioFormatManager.SetSelectedFormat(audioFormat);
            }
        }
        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            if (_audioFormatManager != null)
                _audioFormatManager.RestrictFormats(filter);
        }
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) => throw new NotImplementedException();
        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
        public bool IsAudioSourcePaused() => _isPaused;
        public Task StartAudio() => Start();
        public Task PauseAudio() => Pause();
        public Task ResumeAudio() => Resume();
        public Task CloseAudio() => Close();

        private void FileSourceDecoder_OnAudioFrame(byte[] buffer)
        {
            if (OnAudioSourceEncodedSample != null && _audioEncoder != null && _audioFormatManager != null && buffer.Length > 0)
            {
                // FFmpeg AV_SAMPLE_FMT_S16 will store the bytes in the correct endianess for the underlying platform.
                short[] pcm = buffer.Where((x, i) => i % 2 == 0).Select((y, i) => BitConverter.ToInt16(buffer, i * 2)).ToArray();
                var encodedSample = _audioEncoder.EncodeAudio(pcm, _audioFormatManager.SelectedFormat);
                OnAudioSourceEncodedSample((uint)encodedSample.Length, encodedSample);
            }
        }


        public Task Start()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _audioDecoder.StartDecode();
            }

            return Task.CompletedTask;
        }

        public async Task Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                await _audioDecoder.Close();

                Dispose();
            }
        }

        public Task Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;

                _audioDecoder.Pause();
            }

            return Task.CompletedTask;
        }

        public async Task Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;

                await _audioDecoder.Resume();
            }
        }

        public void Dispose()
        {
            _audioDecoder.Dispose();
        }
    }
}
