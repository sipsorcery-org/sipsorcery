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
    public class FFmpegAudioSource : IAudioSource, IDisposable
    {
        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegAudioSource>();

        internal bool _isStarted;
        internal bool _isPaused;
        internal bool _isClosed;

        internal int _currentNbSamples = 0;
        internal int bufferSize;
        internal byte[] buffer; // Avoid to create buffer of same size

        private int frameSize;

        private BasicBufferShort _incomingSamples = new BasicBufferShort(48000);

        internal FFmpegAudioDecoder _audioDecoder;
        internal IAudioEncoder _audioEncoder;

        internal MediaFormatManager<AudioFormat> _audioFormatManager;

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
#pragma warning disable CS0067
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event SourceErrorDelegate? OnAudioSourceError;
#pragma warning restore CS0067
        public event Action? OnEndOfFile;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public FFmpegAudioSource(IAudioEncoder audioEncoder, uint frameSize = 960)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            if (audioEncoder == null)
                throw new ApplicationException("Audio encoder provided is null");

            _audioFormatManager = new MediaFormatManager<AudioFormat>(audioEncoder.SupportedFormats);
            _audioEncoder = audioEncoder;

            this.frameSize = (int)frameSize;
        }

        public unsafe void CreateAudioDecoder(String path, AVInputFormat* avInputFormat, bool repeat = false, bool isMicrophone = false)
        {
            _audioDecoder = new FFmpegAudioDecoder(path, avInputFormat, repeat, isMicrophone);

            _audioDecoder.OnAudioFrame += AudioDecoder_OnAudioFrame;

            _audioDecoder.OnEndOfFile += () =>
            {
                logger.LogDebug($"File source decode complete for {path}.");
                OnEndOfFile?.Invoke();
                _audioDecoder.Dispose();
            };
        }
        
        public void InitialiseDecoder()
        {
            _audioDecoder?.InitialiseSource(_audioFormatManager.SelectedFormat.ClockRate);
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
                InitialiseDecoder();
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

        private unsafe void AudioDecoder_OnAudioFrame(ref AVFrame avFrame)
        {
            if (OnAudioSourceEncodedSample == null)
                return;

            // Avoid to create several times buffer of the same size
            if (_currentNbSamples != avFrame.nb_samples)
            {
                bufferSize = ffmpeg.av_samples_get_buffer_size(null, avFrame.channels, avFrame.nb_samples, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);
                buffer = new byte[bufferSize];
                _currentNbSamples = avFrame.nb_samples;
            }

            // Convert audio
            int dstSampleCount;
            fixed (byte* pBuffer = buffer)
                dstSampleCount = ffmpeg.swr_convert(_audioDecoder._swrContext, &pBuffer, bufferSize, avFrame.extended_data, avFrame.nb_samples).ThrowExceptionIfError();

            if(dstSampleCount > 0)
            {
                // FFmpeg AV_SAMPLE_FMT_S16 will store the bytes in the correct endianess for the underlying platform.
                short[] pcm = buffer.Take(dstSampleCount * 2).Where((x, i) => i % 2 == 0).Select((y, i) => BitConverter.ToInt16(buffer, i * 2)).ToArray();
                _incomingSamples.Write(pcm);

                while (_incomingSamples.Available() >= frameSize)
                {
                    var pcmFrame = _incomingSamples.Read(frameSize);
                    var encodedSample = _audioEncoder.EncodeAudio(pcmFrame, _audioFormatManager.SelectedFormat);
                    if(encodedSample.Length > 0)
                        OnAudioSourceEncodedSample?.Invoke((uint) (pcmFrame.Length * _audioFormatManager.SelectedFormat.RtpClockRate / _audioFormatManager.SelectedFormat.ClockRate ), encodedSample);
                }
            }
        }

        public Task Start()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                InitialiseDecoder();
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

