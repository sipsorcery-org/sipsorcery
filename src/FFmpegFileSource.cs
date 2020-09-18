using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegFileSource : IVideoSource, IAudioSource, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAME_RATE = 30;
        private const AudioSamplingRatesEnum AUDIO_SAMPLING_RATE = AudioSamplingRatesEnum.Rate8KHz;

        public ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEndPoint>();

        public static readonly List<VideoCodecsEnum> SupportedVideoCodecs = new List<VideoCodecsEnum>
        {
            VideoCodecsEnum.VP8,
            VideoCodecsEnum.H264
        };

        public static readonly List<AudioCodecsEnum> SupportedAudioCodecs = new List<AudioCodecsEnum>
        {
            AudioCodecsEnum.PCMU,
            AudioCodecsEnum.PCMA,
            AudioCodecsEnum.G722
        };

        private VideoCodecsEnum _selectedVideoSourceFormat = VideoCodecsEnum.VP8;
        private AudioCodecsEnum _sendingAudioFormat = AudioCodecsEnum.PCMU;
        private bool _isStarted;
        private bool _isClosed;
        private List<VideoCodecsEnum> _supportedVideoCodecs = new List<VideoCodecsEnum>(SupportedVideoCodecs);
        private List<AudioCodecsEnum> _supportedAudioCodecs = new List<AudioCodecsEnum>(SupportedAudioCodecs);
        private bool _forceKeyFrame;
        private FileSourceDecoder _fileSourceDecoder;
        private VideoEncoder _videoEncoder;
        private IAudioEncoder _audioEncoder;

        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;

#pragma warning disable CS0067
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event SourceErrorDelegate? OnVideoSourceError;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event SourceErrorDelegate? OnAudioSourceError;
#pragma warning restore CS0067

        public event Action? OnEndOfFile;

        public FFmpegFileSource(string path, IAudioEncoder audioEncoder)
        {
            if (!File.Exists(path))
            {
                throw new ApplicationException($"Requested path for FFmpeg file source could not be found {path}.");
            }

            _audioEncoder = audioEncoder;
            _fileSourceDecoder = new FileSourceDecoder(path);
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

        public void Initialise()
        {
            _fileSourceDecoder.InitialiseSource();
        }

        private void FileSourceDecdoer_OnAudioFrame(byte[] buffer)
        {
            if (OnAudioSourceEncodedSample != null)
            {
                var encodedSample = _audioEncoder.EncodeAudio(buffer, _sendingAudioFormat, AUDIO_SAMPLING_RATE);

                //Console.WriteLine($"encoded audio size {encodedSample.Length}.");

                OnAudioSourceEncodedSample((uint)encodedSample.Length, encodedSample);
            }
        }

        private void FileSourceDecdoer_OnVideoFrame(byte[] buffer, int width, int height)
        {
            if (OnVideoSourceEncodedSample != null)
            {
                int frameRate = (int)_fileSourceDecoder.VideoAverageFrameRate;
                frameRate = (frameRate <= 0) ? DEFAULT_FRAME_RATE : frameRate;
                uint timestampDuration = (uint)(VIDEO_SAMPLING_RATE / frameRate);

                //Console.WriteLine($"framerate {frameRate}, timestamp duration {timestampDuration}.");

                var frame = _videoEncoder.MakeFrame(buffer, width, height);
                var encodedSample = _videoEncoder.Encode(FFmpegConvert.GetAVCodecID(_selectedVideoSourceFormat), frame, frameRate, _forceKeyFrame);
                OnVideoSourceEncodedSample(timestampDuration, encodedSample);

                if(_forceKeyFrame)
                {
                    _forceKeyFrame = false;
                }
            }
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _fileSourceDecoder.StartDecode();
            }

            return Task.CompletedTask;
        }

        public async Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                await _fileSourceDecoder.Close();
                Dispose();
            }
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            throw new NotImplementedException();
        }

        public void ForceKeyFrame()
        {
            _forceKeyFrame = true;
        }

        public List<VideoCodecsEnum> GetVideoSourceFormats()
        {
            return _supportedVideoCodecs;
        }

        public Task PauseVideo()
        {
            throw new NotImplementedException();
        }

        public void RestrictCodecs(List<VideoCodecsEnum> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _supportedVideoCodecs = new List<VideoCodecsEnum>(SupportedVideoCodecs);
            }
            else
            {
                _supportedVideoCodecs = new List<VideoCodecsEnum>();
                foreach (var codec in codecs)
                {
                    if (SupportedVideoCodecs.Any(x => x == codec))
                    {
                        _supportedVideoCodecs.Add(codec);
                    }
                    else
                    {
                        logger.LogWarning($"Not including unsupported codec {codec} in filtered list.");
                    }
                }
            }
        }

        public Task ResumeVideo()
        {
            throw new NotImplementedException();
        }

        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat)
        {
            if (!SupportedVideoCodecs.Any(x => x == videoFormat))
            {
                throw new ApplicationException($"The FFmpeg file source does not support video codec {videoFormat}.");
            }

            _selectedVideoSourceFormat = videoFormat;
        }

        public Task PauseAudio()
        {
            throw new NotImplementedException();
        }

        public Task ResumeAudio()
        {
            throw new NotImplementedException();
        }

        public Task StartAudio()
        {
            throw new NotImplementedException();
        }

        public Task CloseAudio()
        {
            throw new NotImplementedException();
        }

        public List<AudioCodecsEnum> GetAudioSourceFormats()
        {
            return _supportedAudioCodecs;
        }

        public void SetAudioSourceFormat(AudioCodecsEnum audioFormat)
        {
            _sendingAudioFormat = audioFormat;
        }

        public void RestrictCodecs(List<AudioCodecsEnum> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _supportedAudioCodecs = new List<AudioCodecsEnum>(SupportedAudioCodecs);
            }
            else
            {
                _supportedAudioCodecs = new List<AudioCodecsEnum>();
                foreach (var codec in codecs)
                {
                    if (SupportedAudioCodecs.Any(x => x == codec))
                    {
                        _supportedAudioCodecs.Add(codec);
                    }
                    else
                    {
                        logger.LogWarning($"Not including unsupported audio codec {codec} in filtered list.");
                    }
                }
            }
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            _fileSourceDecoder.Dispose();
            _videoEncoder.Dispose();
        }
    }
}
