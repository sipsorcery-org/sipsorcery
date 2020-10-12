using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegFileSource : IAudioSource, IVideoSource, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAME_RATE = 30;
        private const AudioSamplingRatesEnum AUDIO_SAMPLING_RATE = AudioSamplingRatesEnum.Rate8KHz;

        private static List<AudioCodecsEnum> _supportedAudioCodecs = new List<AudioCodecsEnum>
        {
            AudioCodecsEnum.PCMU,
            AudioCodecsEnum.PCMA,
            AudioCodecsEnum.G722
        };

        private static List<VideoCodecsEnum> _supportedVideoCodecs = new List<VideoCodecsEnum>
        { 
            VideoCodecsEnum.VP8,
            VideoCodecsEnum.H264
        };

        public ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEndPoint>();

        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private FileSourceDecoder _fileSourceDecoder;
        private VideoEncoder _videoEncoder;
        private IAudioEncoder _audioEncoder;
        private bool _forceKeyFrame;

        private CodecManager<AudioCodecsEnum> _audioCodecManager;
        private CodecManager<VideoCodecsEnum> _videoCodecManager;

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;

#pragma warning disable CS0067
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event SourceErrorDelegate? OnAudioSourceError;
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event SourceErrorDelegate? OnVideoSourceError;
#pragma warning restore CS0067

        public event Action? OnEndOfFile;

        public FFmpegFileSource(string path, bool repeat, IAudioEncoder audioEncoder)
        {
            if (!File.Exists(path))
            {
                throw new ApplicationException($"Requested path for FFmpeg file source could not be found {path}.");
            }

            _audioCodecManager = new CodecManager<AudioCodecsEnum>(_supportedAudioCodecs);
            _videoCodecManager = new CodecManager<VideoCodecsEnum>(_supportedVideoCodecs);

            _audioEncoder = audioEncoder;
            _fileSourceDecoder = new FileSourceDecoder(path, repeat);
            _videoEncoder = new VideoEncoder();
            _fileSourceDecoder.OnVideoFrame += FileSourceDecdoer_OnVideoFrame;
            _fileSourceDecoder.OnAudioFrame += FileSourceDecdoer_OnAudioFrame;
            _fileSourceDecoder.OnEndOfFile += () =>
            {
                logger.LogDebug($"File source decode complete for {path}.");
                OnEndOfFile?.Invoke();
                _fileSourceDecoder.Dispose();
            };
        }

        public void Initialise() => _fileSourceDecoder.InitialiseSource();
        public bool IsPaused() => _isPaused;

        public List<AudioCodecsEnum> GetAudioSourceFormats() => _audioCodecManager.GetSourceFormats();
        public void SetAudioSourceFormat(AudioCodecsEnum audioFormat) => _audioCodecManager.SetSelectedCodec(audioFormat);
        public void RestrictCodecs(List<AudioCodecsEnum> codecs) => _audioCodecManager.RestrictCodecs(codecs);
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) => throw new NotImplementedException();
        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
        public bool IsAudioSourcePaused() => _isPaused;
        public Task StartAudio() => Start();
        public Task PauseAudio() => Pause();
        public Task ResumeAudio() => Resume();
        public Task CloseAudio() => Close();

        public List<VideoCodecsEnum> GetVideoSourceFormats() => _videoCodecManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat) => _videoCodecManager.SetSelectedCodec(videoFormat);
        public void RestrictCodecs(List<VideoCodecsEnum> codecs) => _videoCodecManager.RestrictCodecs(codecs);
        public void ForceKeyFrame() => _forceKeyFrame = true;
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) => throw new NotImplementedException();
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => _isPaused;
        public Task StartVideo() => Start();
        public Task PauseVideo() => Pause();
        public Task ResumeVideo() => Resume();
        public Task CloseVideo() => Close();

        private void FileSourceDecdoer_OnAudioFrame(byte[] buffer)
        {
            if (OnAudioSourceEncodedSample != null)
            {
                var encodedSample = _audioEncoder.EncodeAudio(buffer, _audioCodecManager.SelectedCodec, AUDIO_SAMPLING_RATE);
                OnAudioSourceEncodedSample((uint)encodedSample.Length, encodedSample);
            }
        }

        //private void FileSourceDecdoer_OnVideoFrame(byte[] buffer, int width, int height)
        private void FileSourceDecdoer_OnVideoFrame(ref AVFrame frame)
        {
            if (OnVideoSourceEncodedSample != null)
            {
                int frameRate = (int)_fileSourceDecoder.VideoAverageFrameRate;
                frameRate = (frameRate <= 0) ? DEFAULT_FRAME_RATE : frameRate;
                uint timestampDuration = (uint)(VIDEO_SAMPLING_RATE / frameRate);

                //Console.WriteLine($"framerate {frameRate}, timestamp duration {timestampDuration}.");

                //var frame = _videoEncoder.MakeFrame(buffer, width, height);
                var encodedSample = _videoEncoder.Encode(FFmpegConvert.GetAVCodecID(_videoCodecManager.SelectedCodec), frame, frameRate, _forceKeyFrame);

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
            _videoEncoder.Dispose();
        }
    }
}
