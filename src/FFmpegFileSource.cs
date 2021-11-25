using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegFileSource : IAudioSource, IVideoSource, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAME_RATE = 30;
        private const int VP8_INITIAL_FORMATID = 96;
        private const int H264_INITIAL_FORMATID = 100;

        private static List<AudioFormat> _supportedAudioFormats = new List<AudioFormat>
        {
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.G722)
        };

        private static List<VideoFormat> _supportedVideoFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_INITIAL_FORMATID, VIDEO_SAMPLING_RATE),
            new VideoFormat(VideoCodecsEnum.H264, H264_INITIAL_FORMATID, VIDEO_SAMPLING_RATE)
        };

        public ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegFileSource>();

        private bool _useAudio;
        private bool _useVideo;

        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;

        private VideoFrameConverter ? _videoFrameConverter = null;

        private FFmpegAudioDecoder ? _audioDecoder = null;
        private FFmpegVideoDecoder ? _videoDecoder = null;

        private FFmpegVideoEncoder ? _videoEncoder = null;
        private IAudioEncoder ? _audioEncoder = null;
        private bool _forceKeyFrame;

        private MediaFormatManager<AudioFormat> ? _audioFormatManager = null;
        private MediaFormatManager<VideoFormat> ? _videoFormatManager = null;

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
                throw new ApplicationException($"Requested path for FFmpeg file source could not be found {path}.");
            }

            _useAudio = (audioEncoder != null);
            _useVideo = useVideo;

            if(!(_useAudio || _useVideo))
            {
                throw new ApplicationException("Audio or Video must be set/used.");
            }

            if (_useAudio)
            {
                _audioFormatManager = new MediaFormatManager<AudioFormat>(_supportedAudioFormats);
                _audioEncoder = audioEncoder;

                _audioDecoder = new FFmpegAudioDecoder(path, null, repeat);
                _audioDecoder.OnAudioFrame += FileSourceDecoder_OnAudioFrame;

                _audioDecoder.OnEndOfFile += () =>
                {
                    logger.LogDebug($"File source decode complete for {path}.");
                    OnEndOfFile?.Invoke();
                    _audioDecoder.Dispose();
                };
            }
            else
                _audioDecoder = null;

            if (_useVideo)
            {
                _videoFormatManager = new MediaFormatManager<VideoFormat>(_supportedVideoFormats);
                _videoEncoder = new FFmpegVideoEncoder();

                _videoDecoder = new FFmpegVideoDecoder(path, null, repeat);
                _videoDecoder.OnVideoFrame += FileSourceDecoder_OnVideoFrame;

                _videoDecoder.OnEndOfFile += () =>
                {
                    logger.LogDebug($"File source decode complete for {path}.");
                    OnEndOfFile?.Invoke();
                    _videoDecoder.Dispose();
                };
            }
            else
                _videoDecoder = null;

            Initialise();
        }

        private void Initialise()
        {
            _audioDecoder?.InitialiseSource();
            _videoDecoder?.InitialiseSource();
        }
        public bool IsPaused() => _isPaused;

        public List<AudioFormat> GetAudioSourceFormats()
        {
            if(_audioFormatManager != null)
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

        public List<VideoFormat> GetVideoSourceFormats()
        {
            if (_videoFormatManager != null)
                return _videoFormatManager.GetSourceFormats();
            return new List<VideoFormat>();
        }

        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            if (_videoFormatManager != null)
            {
                logger.LogDebug($"Setting video source format to {videoFormat.FormatID}:{videoFormat.Codec} {videoFormat.ClockRate}.");
                _videoFormatManager.SetSelectedFormat(videoFormat);
            }
        }
        public void RestrictFormats(Func<VideoFormat, bool> filter)
        {
            if (_videoFormatManager != null)
                _videoFormatManager.RestrictFormats(filter);
        }

        public void ForceKeyFrame() => _forceKeyFrame = true;
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) => throw new NotImplementedException();
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => _isPaused;
        public Task StartVideo() => Start();
        public Task PauseVideo() => Pause();
        public Task ResumeVideo() => Resume();
        public Task CloseVideo() => Close();

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

        //private void FileSourceDecdoer_OnVideoFrame(byte[] buffer, int width, int height)
        private void FileSourceDecoder_OnVideoFrame(ref AVFrame frame)
        {
            if (OnVideoSourceEncodedSample != null && _videoEncoder != null && _videoFormatManager != null && _videoDecoder != null)
            {
                int frameRate = (int)_videoDecoder.VideoAverageFrameRate; 
                frameRate = (frameRate <= 0) ? DEFAULT_FRAME_RATE : frameRate;
                uint timestampDuration = (uint)(VIDEO_SAMPLING_RATE / frameRate);

                AVCodecID aVCodecID = FFmpegConvert.GetAVCodecID(_videoFormatManager.SelectedFormat.Codec);

                byte[]? encodedSample;
                if (frame.format == (int)AVPixelFormat.AV_PIX_FMT_YUV420P)
                {
                    encodedSample = _videoEncoder.Encode(aVCodecID, frame, frameRate, _forceKeyFrame);
                }
                else
                {
                    var width = frame.width;
                    var height = frame.height;

                    if (_videoFrameConverter == null ||
                        _videoFrameConverter.SourceWidth != width ||
                        _videoFrameConverter.SourceHeight != height)
                    {
                        _videoFrameConverter = new VideoFrameConverter(
                            width, height,
                            (AVPixelFormat)frame.format,
                            width, height,
                            AVPixelFormat.AV_PIX_FMT_YUV420P);
                    }

                    byte[] sample = _videoFrameConverter.ConvertFrame(ref frame);

                    encodedSample = _videoEncoder.Encode(FFmpegConvert.GetAVCodecID(_videoFormatManager.SelectedFormat.Codec), sample, width, height, frameRate, _forceKeyFrame);
                }

                if (encodedSample != null)
                {
                    // Note the event handler can be removed while the encoding is in progress.
                    OnVideoSourceEncodedSample?.Invoke(timestampDuration, encodedSample);

                    if (_forceKeyFrame)
                    {
                        _forceKeyFrame = false;
                    }
                }
            }
        }

        public Task Start()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _audioDecoder?.StartDecode();
                _videoDecoder?.StartDecode();
            }

            return Task.CompletedTask;
        }

        public async Task Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                if(_audioDecoder != null)
                    await _audioDecoder.Close();

                if (_videoDecoder != null)
                    await _videoDecoder.Close();

                Dispose();
            }
        }

        public Task Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;

                _audioDecoder?.Pause();
                _videoDecoder?.Pause();
            }

            return Task.CompletedTask;
        }

        public async Task Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;

                if(_audioDecoder != null)
                    await _audioDecoder.Resume();

                if (_videoDecoder != null)
                    await _videoDecoder.Resume();
            }
        }

        public void Dispose()
        {
            _audioDecoder?.Dispose();
            _videoDecoder?.Dispose();

            _videoEncoder?.Dispose();
        }
    }
}
