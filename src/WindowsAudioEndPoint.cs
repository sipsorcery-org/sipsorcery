//-----------------------------------------------------------------------------
// Filename: WindowsAudioSession.cs
//
// Description: Example of an RTP session that uses NAUdio for audio
// capture and rendering on Windows.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020  Aaron Clauson	Created, Dublin, Ireland.
// 01 Jun 2020  Aaron Clauson   Refactored to use RtpAudioSession base class.
// 15 Aug 2020  Aaron Clauson   Moved from examples into SIPSorceryMedia.Windows
//                              assembly.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.Windows
{
    public class WindowsAudioEndPoint : IAudioSource, IAudioSink
    {
        private const int DEVICE_PLAYBACK_RATE = 8000;
        private const int DEVICE_BITS_PER_SAMPLE = 16;
        private const int DEVICE_CHANNELS = 1;
        private const int INPUT_BUFFERS = 2;          // See https://github.com/sipsorcery/sipsorcery/pull/148.
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;
        private const int AUDIO_INPUTDEVICE_INDEX = -1;
        private const int AUDIO_OUTPUTDEVICE_INDEX = -1;

        /// <summary>
        /// Microphone input is sampled at 8KHz.
        /// </summary>
        public readonly static AudioSamplingRatesEnum AudioSourceSamplingRate = AudioSamplingRatesEnum.Rate8KHz;

        private static ILogger logger = NullLogger.Instance;

        private static readonly WaveFormat _waveFormat = new WaveFormat(
            DEVICE_PLAYBACK_RATE,
            DEVICE_BITS_PER_SAMPLE,
            DEVICE_CHANNELS);

        public static readonly List<AudioCodecsEnum> SupportedCodecs = new List<AudioCodecsEnum>
        {
            AudioCodecsEnum.PCMU,
            AudioCodecsEnum.PCMA,
            AudioCodecsEnum.G722
        };

        /// <summary>
        /// Audio render device.
        /// </summary>
        private WaveOutEvent _waveOutEvent;

        /// <summary>
        /// Buffer for audio samples to be rendered.
        /// </summary>
        private BufferedWaveProvider _waveProvider;

        /// <summary>
        /// Audio capture device.
        /// </summary>
        private WaveInEvent _waveInEvent;

        private IAudioEncoder _audioEncoder;
        private AudioCodecsEnum _selectedSourceFormat = AudioCodecsEnum.PCMU;
        private AudioCodecsEnum _selectedSinkFormat = AudioCodecsEnum.PCMU;
        private IAudioSource _externalSource;
        private List<AudioCodecsEnum> _supportedCodecs = new List<AudioCodecsEnum>(SupportedCodecs);

        private bool _disableSink;
        private bool _disableSource;

        protected bool _isStarted;
        protected bool _isClosed;

        /// <summary>
        /// This audio source doesn't have capabilities to do any of it's own encoding.
        /// </summary>
        public bool EncodedSamplesOnly
        {
            get { return false; }
            set { }
        }

        /// <summary>
        /// The audio playback device sampling rate.
        /// </summary>
        public AudioSamplingRatesEnum AudioPlaybackRate
        {
            get { return AudioSourceSamplingRate; }
            set { }
        }

        /// <summary>
        /// Not used by this audio source.
        /// </summary>
        public event AudioEncodedSampleDelegate OnAudioSourceEncodedSample;

        /// <summary>
        /// This audio source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("The audio source only generates encoded samples.")]
        public event RawAudioSampleDelegate OnAudioSourceRawSample { add { } remove { } }

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        /// <param name="audioEncoder">A 3rd party audio encoder that can be used to encode and decode
        /// specific audio codecs.</param>
        /// <param name="externalSource">Optional. An external source to use in combination with the source
        /// provided by this end point. The application will need to signal which source is active.</param>
        /// <param name="disableSource">Set to true to disable the use of the audio source functionality, i.e.
        /// don't capture input from the microphone.</param>
        /// <param name="disableSink">Set to true to disable the use of the audio sink functionality, i.e.
        /// don't playback audio to the speaker.</param>
        public WindowsAudioEndPoint(IAudioEncoder audioEncoder, IAudioSource externalSource = null, bool disableSource = false, bool disableSink = false)
        {
            _audioEncoder = audioEncoder;

            _disableSource = disableSource;
            _disableSink = disableSink;

            if (externalSource != null)
            {
                _externalSource = externalSource;

                // Pass the encoded audio sample to the RTP transport. If this class ever supported additional codecs,
                // such as Opus, the idea would be to change to receive raw samples from the external source and then
                // do the custom encoding before handing over to the transport.
                _externalSource.OnAudioSourceEncodedSample += (audioFormat, durationRtpUnits, sample)
                    => OnAudioSourceEncodedSample?.Invoke(audioFormat, durationRtpUnits, sample);
            }

            if (!_disableSink)
            {
                // Render device.
                _waveOutEvent = new WaveOutEvent();
                _waveOutEvent.DeviceNumber = AUDIO_OUTPUTDEVICE_INDEX;
                _waveProvider = new BufferedWaveProvider(_waveFormat);
                _waveProvider.DiscardOnBufferOverflow = true;
                _waveOutEvent.Init(_waveProvider);
            }

            if (!_disableSource)
            {
                if (WaveInEvent.DeviceCount > 0)
                {
                    _waveInEvent = new WaveInEvent();
                    _waveInEvent.BufferMilliseconds = AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                    _waveInEvent.NumberOfBuffers = INPUT_BUFFERS;
                    _waveInEvent.DeviceNumber = AUDIO_INPUTDEVICE_INDEX;
                    _waveInEvent.WaveFormat = _waveFormat;
                    _waveInEvent.DataAvailable += LocalAudioSampleAvailable;
                }
                else
                {
                    throw new ApplicationException("No audio capture devices are available.");
                }
            }
        }

        /// <summary>
        /// Requests that the audio sink and source only advertise support for the supplied list of codecs.
        /// Only codecs that are already supported and in the <see cref="SupportedCodecs" /> list can be 
        /// used.
        /// </summary>
        /// <param name="codecs">The list of codecs restrict advertised support to.</param>
        public void RestrictCodecs(List<AudioCodecsEnum> codecs)
        {
            if(codecs == null || codecs.Count == 0)
            {
                _supportedCodecs = new List<AudioCodecsEnum>(SupportedCodecs);
            }
            else
            {
                _supportedCodecs = new List<AudioCodecsEnum>();
                foreach(var codec in codecs)
                {
                    if(SupportedCodecs.Any(x => x == codec))
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
                AudioSource = (_disableSource) ? null : this,
                AudioSink = (_disableSink) ? null : this,
            };
        }

        /// <summary>
        /// Starts the media capturing/source devices.
        /// </summary>
        public Task StartAudio()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _waveOutEvent?.Play();
                _waveInEvent?.StartRecording();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Closes the session.
        /// </summary>
        public Task CloseAudio()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                _waveOutEvent?.Stop();

                if (_waveInEvent != null)
                {
                    _waveInEvent.DataAvailable -= LocalAudioSampleAvailable;
                    _waveInEvent.StopRecording();
                }
            }

            return Task.CompletedTask;
        }

        public Task PauseAudio()
        {
            _waveInEvent?.StopRecording();
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            _waveInEvent?.StartRecording();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        private void LocalAudioSampleAvailable(object sender, WaveInEventArgs args)
        {
            //WaveBuffer wavBuffer = new WaveBuffer(args.Buffer.Take(args.BytesRecorded).ToArray());
            //byte[] encodedSample = _audioEncoder.EncodeAudio(wavBuffer.ShortBuffer, _selectedSourceFormat, AudioSourceSamplingRate);
            byte[] encodedSample = _audioEncoder.EncodeAudio(args.Buffer.Take(args.BytesRecorded).ToArray(), _selectedSourceFormat, AudioSourceSamplingRate);
            OnAudioSourceEncodedSample?.Invoke(_selectedSourceFormat, (uint)encodedSample.Length, encodedSample);
        }

        public List<AudioCodecsEnum> GetAudioSourceFormats()
        {
            return _supportedCodecs;
        }

        public void SetAudioSourceFormat(AudioCodecsEnum audioFormat)
        {
            _selectedSourceFormat = audioFormat;
            _externalSource?.SetAudioSourceFormat(audioFormat);
        }

        public List<AudioCodecsEnum> GetAudioSinkFormats()
        {
            return _supportedCodecs;
        }

        public void SetAudioSinkFormat(AudioCodecsEnum audioFormat)
        {
            _selectedSinkFormat = audioFormat;
        }

        /// <summary>
        /// Event handler for playing audio samples received from the remote call party.
        /// </summary>
        /// <param name="pcmSample">Raw PCM sample from remote party.</param>
        public void GotAudioSample(byte[] pcmSample)
        {
            if (_waveProvider != null)
            {
                _waveProvider.AddSamples(pcmSample, 0, pcmSample.Length);
            }
        }

        public void GotAudioRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
        {
            if (_waveProvider != null && _audioEncoder != null && _audioEncoder.IsSupported(_selectedSinkFormat))
            {
                var pcmSample = _audioEncoder.DecodeAudio(payload, _selectedSinkFormat, AudioPlaybackRate);
                _waveProvider?.AddSamples(pcmSample, 0, pcmSample.Length);
            }
        }
    }
}
