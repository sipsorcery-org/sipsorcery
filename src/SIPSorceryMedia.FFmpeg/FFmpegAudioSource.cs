using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance.Buffers;
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
        internal int bufferSizeInSamples;
        internal byte[] buffer; // Avoid to create buffer of same size

        private int frameSize;
        private string path;

        private BasicBufferShort _incomingSamples = new BasicBufferShort(48000);

        internal FFmpegAudioDecoder? _audioDecoder = null;
        internal IAudioEncoder _audioEncoder;

        internal MediaFormatManager<AudioFormat> _audioFormatManager;

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event SourceErrorDelegate? OnAudioSourceError;

#pragma warning disable CS0067
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
#pragma warning restore CS0067


#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public FFmpegAudioSource(IAudioEncoder audioEncoder, uint frameSize = 960)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            if (audioEncoder == null)
            {
                throw new ApplicationException("Audio encoder provided is null");
            }

            _audioFormatManager = new MediaFormatManager<AudioFormat>(audioEncoder.SupportedFormats);
            _audioEncoder = audioEncoder;

            this.frameSize = (int)frameSize;
        }

        public unsafe void CreateAudioDecoder(string path, AVInputFormat* avInputFormat, bool repeat = false, bool isMicrophone = false)
        {
            this.path = path;
            _audioDecoder = new FFmpegAudioDecoder(path, avInputFormat, repeat, isMicrophone);

            _audioDecoder.OnAudioFrame += AudioDecoder_OnAudioFrame;
            _audioDecoder.OnError += AudioDecoder_OnError;
            _audioDecoder.OnEndOfFile += AudioDecoder_OnEndOfFile;
        }

        private void AudioDecoder_OnEndOfFile()
        {
            AudioDecoder_OnError("End of file");
        }

        private void AudioDecoder_OnError(string errorMessage)
        {
            logger.LogDebug("Audio - Source error for {Path} - ErrorMessage:[{ErrorMessage}]", path, errorMessage);
            OnAudioSourceError?.Invoke(errorMessage);
            Dispose();
        }

        public bool InitialiseDecoder()
        {
            return _audioDecoder?.InitialiseSource(_audioFormatManager.SelectedFormat.ClockRate) == true;
        }

        public bool IsPaused() => _isPaused;

        public List<AudioFormat> GetAudioSourceFormats()
        {
            if (_audioFormatManager != null)
            {
                return _audioFormatManager.GetSourceFormats();
            }

            return new List<AudioFormat>();
        }

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            logger.LogDebug("Setting audio source format to {FormatId}:{Codec} {ClockRate}.",
                audioFormat.FormatID,
                audioFormat.Codec,
                audioFormat.ClockRate);
            _audioFormatManager.SetSelectedFormat(audioFormat);
            InitialiseDecoder();
        }

        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            if (_audioFormatManager != null)
            {
                _audioFormatManager.RestrictFormats(filter);
            }
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
            if ((OnAudioSourceEncodedSample == null) || (_audioDecoder == null))
            {
                return;
            }

            // Avoid to create several times buffer of the same size
            if (_currentNbSamples < avFrame.nb_samples)
            {
                // a * b / c,
                // handles overflow, supports different rounding methods.
                bufferSizeInSamples = (int)ffmpeg.av_rescale_rnd(
                    a: avFrame.nb_samples,
                    b: _audioDecoder.OutSampleRate,
                    c: _audioDecoder.InSampleRate,
                    AVRounding.AV_ROUND_UP);

                var bufferSizeInBytes = ffmpeg.av_samples_get_buffer_size(
                    null, _audioDecoder.OutChannelCount, bufferSizeInSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);

                buffer = new byte[bufferSizeInBytes];
                _currentNbSamples = avFrame.nb_samples;
            }

            // Convert audio
            int dstSampleCount;
            fixed (byte* pBuffer = buffer)
            {
                dstSampleCount = ffmpeg.swr_convert(_audioDecoder._swrContext, &pBuffer, bufferSizeInSamples, avFrame.extended_data, avFrame.nb_samples);
            }

            if (dstSampleCount < 0)
            {
                OnAudioSourceError?.Invoke("Cannot convert audio");
                Dispose();
                return;
            }

            if (dstSampleCount > 0)
            {
                // FFmpeg AV_SAMPLE_FMT_S16 will store the bytes in the correct endianess for the underlying platform.
                short[] pcm = new short[dstSampleCount];
                Buffer.BlockCopy(buffer, 0, pcm, 0, dstSampleCount * sizeof(short));
                _incomingSamples.Write(pcm);

                if (_incomingSamples.Available() >= frameSize)
                {
                    var pcmFrame = ArrayPool<short>.Shared.Rent(frameSize);
                    try
                    {
                        while (_incomingSamples.Available() >= frameSize)
                        {
                            _incomingSamples.Read(frameSize, pcmFrame);
                            using var encodedBuffer = new ArrayPoolBufferWriter<byte>();
                            _audioEncoder.EncodeAudio(pcmFrame.AsSpan(0, frameSize), _audioFormatManager.SelectedFormat, encodedBuffer);

                            var encodedMemory = encodedBuffer.WrittenMemory;

                            var onAudioSourceEncodedSample = OnAudioSourceEncodedSample;
                            var onAudioSourceEncodedFrameReady = OnAudioSourceEncodedFrameReady;

                            if (!encodedMemory.IsEmpty && (onAudioSourceEncodedSample is { } || onAudioSourceEncodedFrameReady is { }))
                            {
                                onAudioSourceEncodedSample?.Invoke((uint)encodedMemory.Length, encodedMemory);

                                if (onAudioSourceEncodedFrameReady is { })
                                {
                                    var audioFormat = _audioFormatManager.SelectedFormat;
                                    var numChannels = audioFormat.ChannelCount;
                                    var sampleRate = audioFormat.ClockRate;
                                    var frames = pcm.Length / numChannels;
                                    var durationMsD = sampleRate > 0 ? (frames / (double)sampleRate) * 1000.0 : 0;
                                    var durationMs = (uint)Math.Round(durationMsD);
                                    var encodedFrame = new EncodedAudioFrame(0, audioFormat, durationMs, encodedMemory);
                                    onAudioSourceEncodedFrameReady(encodedFrame);
                                }
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<short>.Shared.Return(pcmFrame);
                    }
                }
            }
        }

        public Task Start()
        {
            if (!_isStarted)
            {
                if (_audioDecoder != null)
                {
                    _isStarted = true;
                    _audioDecoder.StartDecode();
                }
            }

            return Task.CompletedTask;
        }

        public async Task Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                if (_audioDecoder != null)
                {
                    await _audioDecoder.Close();
                }

                Dispose();
            }
        }

        public Task Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;

                if (_audioDecoder != null)
                {
                    _audioDecoder.Pause();
                }
            }

            return Task.CompletedTask;
        }

        public Task Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;
                if (_audioDecoder != null)
                {
                    _audioDecoder.Resume();
                }
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _isStarted = false;

            if (_audioDecoder != null)
            {
                _audioDecoder.OnAudioFrame -= AudioDecoder_OnAudioFrame;
                _audioDecoder.OnError -= AudioDecoder_OnError;
                _audioDecoder.OnEndOfFile -= AudioDecoder_OnEndOfFile;

                _audioDecoder.Dispose();
                _audioDecoder = null;
            }
        }
    }
}
