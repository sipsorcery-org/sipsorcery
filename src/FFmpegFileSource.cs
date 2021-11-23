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
        private const AudioSamplingRatesEnum AUDIO_SAMPLING_RATE = AudioSamplingRatesEnum.Rate8KHz;
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

        public ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEndPoint>();

        private bool _useAudio;
        private bool _useVideo;

        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private FileSourceDecoder _fileSourceDecoder;
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

        public FFmpegFileSource(string path, bool repeat, IAudioEncoder audioEncoder, bool useVideo = true)
        {
            if (!File.Exists(path))
            {
                throw new ApplicationException($"Requested path for FFmpeg file source could not be found {path}.");
            }

            _useAudio = (audioEncoder != null);
            _useVideo = useVideo;

            
            _fileSourceDecoder = new FileSourceDecoder(path, repeat, _useVideo, _useAudio);

            if (_useAudio)
            {
                _audioFormatManager = new MediaFormatManager<AudioFormat>(_supportedAudioFormats);
                _audioEncoder = audioEncoder;
                _fileSourceDecoder.OnAudioFrame += FileSourceDecoder_OnAudioFrame;
            }

            if (_useVideo)
            {
                _videoFormatManager = new MediaFormatManager<VideoFormat>(_supportedVideoFormats);
                _videoEncoder = new FFmpegVideoEncoder();
                _fileSourceDecoder.OnVideoFrame += FileSourceDecoder_OnVideoFrame;
            }

            
            _fileSourceDecoder.OnEndOfFile += () =>
            {
                logger.LogDebug($"File source decode complete for {path}.");
                OnEndOfFile?.Invoke();
                _fileSourceDecoder.Dispose();
            };
        }

        public void Initialise() => _fileSourceDecoder.InitialiseSource();
        public bool IsPaused() => _isPaused;

        public List<AudioFormat> GetAudioSourceFormats()
        {
            if(_useAudio)
                return _audioFormatManager.GetSourceFormats();
            return new List<AudioFormat>();
        }
        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            if (_useAudio)
            {
                logger.LogDebug($"Setting audio source format to {audioFormat.FormatID}:{audioFormat.Codec} {audioFormat.ClockRate}.");
                _audioFormatManager.SetSelectedFormat(audioFormat);
            }
        }
        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            if (_useAudio)
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
            if (_useVideo)
                return _videoFormatManager.GetSourceFormats();
            return new List<VideoFormat>();
        }

        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            if (_useVideo)
            {
                logger.LogDebug($"Setting video source format to {videoFormat.FormatID}:{videoFormat.Codec} {videoFormat.ClockRate}.");
                _videoFormatManager.SetSelectedFormat(videoFormat);
            }
        }
        public void RestrictFormats(Func<VideoFormat, bool> filter)
        {
            if (_useVideo)
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
            if (OnAudioSourceEncodedSample != null && buffer.Length > 0)
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
            if (OnVideoSourceEncodedSample != null)
            {
                int frameRate = (int)_fileSourceDecoder.VideoAverageFrameRate;
                frameRate = (frameRate <= 0) ? DEFAULT_FRAME_RATE : frameRate;
                uint timestampDuration = (uint)(VIDEO_SAMPLING_RATE / frameRate);

                //Console.WriteLine($"framerate {frameRate}, timestamp duration {timestampDuration}.");

                //var frame = _videoEncoder.MakeFrame(buffer, width, height);
                var encodedSample = _videoEncoder.Encode(FFmpegConvert.GetAVCodecID(_videoFormatManager.SelectedFormat.Codec), frame, frameRate, _forceKeyFrame);

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
                _fileSourceDecoder.StartDecode();
            }

            return Task.CompletedTask;
        }

        public async Task Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                await _fileSourceDecoder.Close();
                Dispose();
            }
        }

        public Task Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;
                _fileSourceDecoder.Pause();
            }

            return Task.CompletedTask;
        }

        public async Task Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;
                await _fileSourceDecoder.Resume();
            }
        }

        public void Dispose()
        {
            _fileSourceDecoder.Dispose();
            _videoEncoder?.Dispose();
        }
    }
}
