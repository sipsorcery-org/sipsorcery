//-----------------------------------------------------------------------------
// Filename: PortAudioRtpSession.cs
//
// Description: Example of an RTP session that uses PortAudio for audio
// capture and rendering.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020 Aaron Clauson	Created, Dublin, Ireland.
// 01 Aug 2020  Aaron Clauson   Switched from PortAudioSharp to 
//                              ProjectCeilidh.PortAudio.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectCeilidh.PortAudio;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions.V1;

namespace demo
{
    public class PortAudioEndPoint : IAudioSource, IAudioSink
    {
        private const int AUDIO_SAMPLING_RATE = 8000;
        private const int SAMPLING_PERIOD_MILLISECONDS = 20;
        private const int AUDIO_CHANNEL_COUNT = 1;
        private const int AUDIO_BYTES_PER_SAMPLE = 2; // 16 bit samples.

        public readonly static AudioSamplingRatesEnum AudioSourceSamplingRate = AudioSamplingRatesEnum.Rate8KHz;

        public readonly static AudioSamplingRatesEnum AudioPlaybackRate = AudioSamplingRatesEnum.Rate8KHz;


        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<PortAudioEndPoint>();

        private PortAudioDevice _portAudioOutputDevice;
        private PortAudioDevice _portAudioInputDevice;
        private PortAudioDevicePump _outputDevicePump;
        private PortAudioDevicePump _inputDevicePump;

        private List<byte> _pendingRemoteSamples = new List<byte>();
        private ManualResetEventSlim _remoteSampleReady = new ManualResetEventSlim();
        private bool _isStarted = false;
        private bool _isClosed = false;

        private IAudioEncoder _audioEncoder;
        private AudioCodecsEnum _selectedSourceFormat = AudioCodecsEnum.PCMU;
        private AudioCodecsEnum _selectedSinkFormat = AudioCodecsEnum.PCMU;
        private List<AudioCodecsEnum> _supportedCodecs = new List<AudioCodecsEnum>(SupportedCodecs);


        public event EncodedSampleDelegate OnAudioSourceEncodedSample;
        public event RawAudioSampleDelegate OnAudioSourceRawSample;
        public event SourceErrorDelegate OnAudioSourceError;
        public event SourceErrorDelegate OnAudioSinkError;

        public static readonly List<AudioCodecsEnum> SupportedCodecs = new List<AudioCodecsEnum>
        {
            AudioCodecsEnum.PCMU,
            AudioCodecsEnum.PCMA,
            AudioCodecsEnum.G722
        };

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public PortAudioEndPoint(IAudioEncoder audioEncoder)
        {
            _audioEncoder = audioEncoder;

            var apiType = PortAudioHostApiType.DirectSound;

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                apiType = PortAudioHostApiType.Alsa;
            }

            _portAudioOutputDevice = PortAudioHostApi.SupportedHostApis.Where(x => x.HostApiType == apiType).First().DefaultOutputDevice;

            _outputDevicePump = new PortAudioDevicePump(_portAudioOutputDevice, AUDIO_CHANNEL_COUNT,
                            new PortAudioSampleFormat(PortAudioSampleFormat.PortAudioNumberFormat.Signed, AUDIO_BYTES_PER_SAMPLE),
                            TimeSpan.FromMilliseconds(SAMPLING_PERIOD_MILLISECONDS), AUDIO_SAMPLING_RATE, ReadAudioDataCalback);

            _portAudioInputDevice = PortAudioHostApi.SupportedHostApis.Where(x => x.HostApiType == apiType).First().DefaultInputDevice;

            _inputDevicePump = new PortAudioDevicePump(_portAudioInputDevice, AUDIO_CHANNEL_COUNT,
                            new PortAudioSampleFormat(PortAudioSampleFormat.PortAudioNumberFormat.Signed, AUDIO_BYTES_PER_SAMPLE),
                            TimeSpan.FromMilliseconds(SAMPLING_PERIOD_MILLISECONDS), AUDIO_SAMPLING_RATE, WriteDataCallback);
        }

        private int ReadAudioDataCalback(byte[] buffer, int offset, int count)
        {
            int bytesAvail = _pendingRemoteSamples.Count < count ? _pendingRemoteSamples.Count : count;

            if (bytesAvail == 0 && !_isClosed)
            {
                _remoteSampleReady.Reset();
                _remoteSampleReady.Wait();
                bytesAvail = _pendingRemoteSamples.Count < count ? _pendingRemoteSamples.Count : count;
            }

            if (bytesAvail > 0)
            {
                lock (_pendingRemoteSamples)
                {
                    Buffer.BlockCopy(_pendingRemoteSamples.ToArray(), 0, buffer, offset, bytesAvail);

                    if (bytesAvail == _pendingRemoteSamples.Count)
                    {
                        _pendingRemoteSamples.Clear();
                    }
                    else
                    {
                        _pendingRemoteSamples = _pendingRemoteSamples.Skip(bytesAvail).ToList();
                    }

                    return bytesAvail;
                }
            }
            else
            {
                return 0;
            }
        }

        void WriteDataCallback(byte[] buffer, int offset, int count)
        {
            byte[] encodedSample = _audioEncoder.EncodeAudio(buffer.Skip(offset).Take(count).ToArray(), _selectedSourceFormat, AudioSourceSamplingRate);
            OnAudioSourceEncodedSample?.Invoke((uint)encodedSample.Length, encodedSample);
        }

        /// <summary>
        /// Requests that the audio sink and source only advertise support for the supplied list of codecs.
        /// Only codecs that are already supported and in the <see cref="SupportedCodecs" /> list can be 
        /// used.
        /// </summary>
        /// <param name="codecs">The list of codecs to restrict advertised support to.</param>
        public void RestrictCodecs(List<AudioCodecsEnum> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _supportedCodecs = new List<AudioCodecsEnum>(SupportedCodecs);
            }
            else
            {
                _supportedCodecs = new List<AudioCodecsEnum>();
                foreach (var codec in codecs)
                {
                    if (SupportedCodecs.Any(x => x == codec))
                    {
                        _supportedCodecs.Add(codec);
                    }
                    else
                    {
                        logger.LogWarning($"Not including unsupported codec {codec} in filtered list.");
                    }
                }
            }
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                AudioSource = this,
                AudioSink = this,
            };
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
            if (!_isStarted)
            {
                _isStarted = true;
                _outputDevicePump.Start();
                _inputDevicePump.Start();
            }

            return Task.CompletedTask;
        }

        public Task CloseAudio()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                _outputDevicePump?.Dispose();
                _inputDevicePump?.Dispose();
                _portAudioOutputDevice?.Dispose();
                _portAudioInputDevice?.Dispose();
            }

            return Task.CompletedTask;
        }

        public List<AudioCodecsEnum> GetAudioSourceFormats()
        {
            return _supportedCodecs;
        }

        public void SetAudioSourceFormat(AudioCodecsEnum audioFormat)
        {
            _selectedSourceFormat = audioFormat;
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            throw new NotImplementedException();
        }

        public List<AudioCodecsEnum> GetAudioSinkFormats()
        {
            return _supportedCodecs;
        }

        public void SetAudioSinkFormat(AudioCodecsEnum audioFormat)
        {
            _selectedSinkFormat = audioFormat;
        }

        public void GotAudioRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
        {
            if (_audioEncoder != null && _audioEncoder.IsSupported(_selectedSinkFormat))
            {
                var pcmSample = _audioEncoder.DecodeAudio(payload, _selectedSinkFormat, AudioPlaybackRate);

                lock (_pendingRemoteSamples)
                {
                    _pendingRemoteSamples.AddRange(pcmSample);
                }

                _remoteSampleReady.Set();
            }
        }
    }
}
