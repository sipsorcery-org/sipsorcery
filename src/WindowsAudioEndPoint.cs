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
using NAudio.Wave;
using SIPSorceryMedia.Abstractions;
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

        public readonly static AudioSamplingRatesEnum AudioPlaybackRate = AudioSamplingRatesEnum.Rate8KHz;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<WindowsAudioEndPoint>();

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
        private CodecManager<AudioCodecsEnum> _audioCodecManager;

        private bool _disableSink;
        private bool _disableSource;

        protected bool _isStarted;
        protected bool _isPaused;
        protected bool _isClosed;

        /// <summary>
        /// Not used by this audio source.
        /// </summary>
        public event EncodedSampleDelegate OnAudioSourceEncodedSample;

        /// <summary>
        /// This audio source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("The audio source only generates encoded samples.")]
        public event RawAudioSampleDelegate OnAudioSourceRawSample { add { } remove { } }

        public event SourceErrorDelegate OnAudioSourceError;

        public event SourceErrorDelegate OnAudioSinkError;

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
        public WindowsAudioEndPoint(IAudioEncoder audioEncoder,
            int audioOutDeviceIndex = AUDIO_OUTPUTDEVICE_INDEX,
            int audioInDeviceIndex = AUDIO_INPUTDEVICE_INDEX,
            bool disableSource = false,
            bool disableSink = false)
        {
            logger = SIPSorcery.LogFactory.CreateLogger<WindowsAudioEndPoint>();

            _audioCodecManager = new CodecManager<AudioCodecsEnum>(SupportedCodecs);
            _audioEncoder = audioEncoder;

            _disableSource = disableSource;
            _disableSink = disableSink;

            if (!_disableSink)
            {
                try
                {
                    // Playback device.
                    _waveOutEvent = new WaveOutEvent();
                    _waveOutEvent.DeviceNumber = audioOutDeviceIndex;
                    _waveProvider = new BufferedWaveProvider(_waveFormat);
                    _waveProvider.DiscardOnBufferOverflow = true;
                    _waveOutEvent.Init(_waveProvider);
                }
                catch (Exception excp)
                {
                    logger.LogWarning(0, excp, "WindowsAudioEndPoint failed to initialise playback device.");
                    OnAudioSinkError?.Invoke($"WindowsAudioEndPoint failed to initialise playback device. {excp.Message}");
                }
            }

            if (!_disableSource)
            {
                if (WaveInEvent.DeviceCount > 0)
                {
                    if (WaveInEvent.DeviceCount > audioInDeviceIndex)
                    {
                        _waveInEvent = new WaveInEvent();
                        _waveInEvent.BufferMilliseconds = AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                        _waveInEvent.NumberOfBuffers = INPUT_BUFFERS;
                        _waveInEvent.DeviceNumber = audioInDeviceIndex;
                        _waveInEvent.WaveFormat = _waveFormat;
                        _waveInEvent.DataAvailable += LocalAudioSampleAvailable;
                    }
                    else
                    {
                        OnAudioSourceError?.Invoke($"The requested audio input device index {audioInDeviceIndex} exceeds the maximum index of {WaveInEvent.DeviceCount - 1}.");
                    }
                }
                else
                {
                    OnAudioSourceError?.Invoke("No audio capture devices are available.");
                }
            }
        }

        public void RestrictCodecs(List<AudioCodecsEnum> codecs) => _audioCodecManager.RestrictCodecs(codecs);
        public List<AudioCodecsEnum> GetAudioSourceFormats() => _audioCodecManager.GetSourceFormats();
        public void SetAudioSourceFormat(AudioCodecsEnum audioFormat) => _audioCodecManager.SetSelectedCodec(audioFormat);
        public List<AudioCodecsEnum> GetAudioSinkFormats() => _audioCodecManager.GetSourceFormats();
        public void SetAudioSinkFormat(AudioCodecsEnum audioFormat) => _audioCodecManager.SetSelectedCodec(audioFormat);

        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
        public bool IsAudioSourcePaused() => _isPaused;
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) =>
            throw new NotImplementedException();

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
            _isPaused = true;
            _waveInEvent?.StopRecording();
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            _isPaused = false;
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
            byte[] encodedSample = _audioEncoder.EncodeAudio(args.Buffer.Take(args.BytesRecorded).ToArray(), _audioCodecManager.SelectedCodec, AudioSourceSamplingRate);
            OnAudioSourceEncodedSample?.Invoke((uint)encodedSample.Length, encodedSample);
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
            if (_waveProvider != null && _audioEncoder != null && _audioEncoder.IsSupported(_audioCodecManager.SelectedCodec))
            {
                var pcmSample = _audioEncoder.DecodeAudio(payload, _audioCodecManager.SelectedCodec, AudioPlaybackRate);
                _waveProvider?.AddSamples(pcmSample, 0, pcmSample.Length);
            }
        }
    }
}
