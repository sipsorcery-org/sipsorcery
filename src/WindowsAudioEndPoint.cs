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
using NAudio.Wave; // <-- Windows Specific Library.
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.Windows
{
    public class WindowsAudioEndPoint : IAudioSource, IAudioSink
    {
        private const int DEVICE_PLAYBACK_RATE = 8000;
        private const int DEVICE_BITS_PER_SAMPLE = 16;
        private const int DEVICE_CHANNELS = 1;
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
        /// The audio playback device is hard coded to 8KHz.
        /// </summary>
        public AudioSamplingRatesEnum AudioPlaybackRate
        {
            get { return AudioSamplingRatesEnum.Rate8KHz; }
            set { }
        }

        /// <summary>
        /// Not used by this audio source.
        /// </summary>
        public event AudioEncodedSampleDelegate OnAudioSourceEncodedSample;

        /// <summary>
        /// This audio source supplies raw PCM samples.
        /// </summary>
        public event RawAudioSampleDelegate OnAudioSourceRawSample;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public WindowsAudioEndPoint()
        { }

        /// <summary>
        /// Starts the media capturing/source devices.
        /// </summary>
        public Task StartAudio()
        {
            if (!_isStarted)
            {
                _isStarted = true;

                // Render device.
                _waveOutEvent = new WaveOutEvent();
                _waveOutEvent.DeviceNumber = AUDIO_OUTPUTDEVICE_INDEX;
                _waveProvider = new BufferedWaveProvider(_waveFormat);
                _waveProvider.DiscardOnBufferOverflow = true;
                _waveOutEvent.Init(_waveProvider);
                _waveOutEvent.Play();

                // Audio source.
                if (WaveInEvent.DeviceCount > 0)
                {
                    _waveInEvent = new WaveInEvent();
                    _waveInEvent.BufferMilliseconds = AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                    _waveInEvent.NumberOfBuffers = 1;
                    _waveInEvent.DeviceNumber = AUDIO_INPUTDEVICE_INDEX;
                    _waveInEvent.WaveFormat = _waveFormat;
                    _waveInEvent.DataAvailable += LocalAudioSampleAvailable;

                    _waveInEvent.StartRecording();
                }
                else
                {
                    throw new ApplicationException("No audio capture devices are available. No audio stream will be sent.");
                }
            }

            return Task.CompletedTask;
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                AudioSource = this,
                AudioSink = this
            };
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        private void LocalAudioSampleAvailable(object sender, WaveInEventArgs args)
        {
            WaveBuffer wavBuffer = new WaveBuffer(args.Buffer.Take(args.BytesRecorded).ToArray());
            OnAudioSourceRawSample?.Invoke(AudioSourceSamplingRate, AUDIO_SAMPLE_PERIOD_MILLISECONDS, wavBuffer.ShortBuffer);
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

                if(_waveInEvent != null)
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

        public List<AudioFormat> GetAudioSourceFormats()
        {
            var formats = new List<AudioFormat>{
                new AudioFormat { Codec = AudioCodecsEnum.PCMU, PayloadID = (int)AudioCodecsEnum.PCMU },
                new AudioFormat { Codec = AudioCodecsEnum.PCMA, PayloadID = (int)AudioCodecsEnum.PCMA },
                new AudioFormat { Codec = AudioCodecsEnum.G722, PayloadID = (int)AudioCodecsEnum.G722 } };

            return formats;
        }

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            //_selectedAudioFormat = audioFormat;
        }

        /// <summary>
        /// The sink only supports decoded PCM samples.
        /// </summary>
        /// <returns>Null.</returns>
        public List<AudioFormat> GetAudioSinkFormats()
        {
            return null;
        }

        /// <summary>
        /// Not used. This sink only accepts raw PCM samples.
        /// </summary>
        public void SetAudioSinkFormat(AudioFormat audioFormat)
        {
            throw new System.NotImplementedException("WindowsAudioSession does not support encoded samples. Audio sink format cannot be set.");
        }

        /// <summary>
        /// Event handler for playing audio samples received from the remote call party.
        /// </summary>
        /// <param name="pcmSample">Raw PCM sample from remote party.</param>
        public void GotAudioSample(byte[] pcmSample)
        {
            _waveProvider?.AddSamples(pcmSample, 0, pcmSample.Length);
        }

        public void GotAudioRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
        {
            throw new System.NotImplementedException("WindowsAudioSession does not support encoded samples. Raw RTP packets cannot be processed.");
        }
    }
}
