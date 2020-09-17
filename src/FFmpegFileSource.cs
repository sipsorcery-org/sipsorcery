using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegFileSource : IVideoSource, IAudioSource
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;

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
        private FileSourceDecoder _fileSourceDecdoer;
        private VideoEncoder _videoEncoder;
        private IAudioEncoder _audioEncoder;

        public event EncodedSampleDelegate OnVideoSourceEncodedSample;
        public event RawVideoSampleDelegate OnVideoSourceRawSample;
        public event SourceErrorDelegate OnVideoSourceError;
        public event EncodedSampleDelegate OnAudioSourceEncodedSample;
        public event RawAudioSampleDelegate OnAudioSourceRawSample;
        public event SourceErrorDelegate OnAudioSourceError;

        public FFmpegFileSource(string path, IAudioEncoder audioEncoder)
        {
            if (!File.Exists(path))
            {
                throw new ApplicationException($"Requested path for FFmpeg file source could not be found {path}.");
            }

            _audioEncoder = audioEncoder;
            _fileSourceDecdoer = new FileSourceDecoder(path);
            _videoEncoder = new VideoEncoder();
            _fileSourceDecdoer.OnVideoFrame += FileSourceDecdoer_OnVideoFrame;
            _fileSourceDecdoer.OnAudioFrame += FileSourceDecdoer_OnAudioFrame;
        }

        private void FileSourceDecdoer_OnAudioFrame(byte[] buffer)
        {
            var encodedSample = _audioEncoder.EncodeAudio(buffer, _sendingAudioFormat, AudioSamplingRatesEnum.Rate8KHz);
            OnAudioSourceEncodedSample((uint)buffer.Length, encodedSample);
        }

        private void FileSourceDecdoer_OnVideoFrame(byte[] buffer, int width, int height)
        {
            var frame = _videoEncoder.MakeFrame(buffer, width, height);
            var encodedSample = _videoEncoder.Encode(AVCodecID.AV_CODEC_ID_VP8, frame, 30);
            OnVideoSourceEncodedSample?.Invoke(3000, encodedSample);
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _fileSourceDecdoer.StartDecode();
            }

            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
            }

            return Task.CompletedTask;
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
    }
}
