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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave; // <-- Windows Specific Library.
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.Windows
{
    public class WindowsAudioSession : IPlatformMediaSession
    {
        private const int DEVICE_SAMPLING_RATE = 8000;
        private const int DEVICE_BITS_PER_SAMPLE = 16;
        private const int DEVICE_CHANNELS = 1;
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;
        private const int AUDIO_INPUTDEVICE_INDEX = -1;
        private const int AUDIO_OUTPUTDEVICE_INDEX = -1;

        private static ILogger Log = NullLogger.Instance;

        private static readonly WaveFormat _waveFormat = new WaveFormat(
            DEVICE_SAMPLING_RATE,
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
        private AudioFormat _selectedAudioFormat;

        public event AudioEncodedSampleReadyDelegate OnEncodedAudioSampleReady;
        public event RawAudioSampleReadyDelegate OnRawAudioSampleReady;
        public event VideoEncodedSampleReadyDelegate OnEncodedVideoSampleReady;
        public event RawVideoSampleReadyDelegate OnRawVideoSampleReady;

        public event SourceErrorDelegate OnAudioSourceFailure;
        public event SourceErrorDelegate OnVideoSourceFailure;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public WindowsAudioSession()
        { }

        /// <summary>
        /// Starts the media capturing/source devices.
        /// </summary>
        public Task Start()
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
                    Log.LogWarning("No audio capture devices are available. No audio stream will be sent.");
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        private void LocalAudioSampleAvailable(object sender, WaveInEventArgs args)
        {
            OnRawAudioSampleReady?.Invoke(AUDIO_SAMPLE_PERIOD_MILLISECONDS, args.Buffer.Take(args.BytesRecorded).ToArray(), AudioSamplingRatesEnum.Rate8KHz);
        }

        /// <summary>
        /// Closes the session.
        /// </summary>
        public Task Close()
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

        public List<AudioFormat> GetAudioFormats()
        {
            var formats = new List<AudioFormat>{
                new AudioFormat { Codec = AudioCodecsEnum.PCMU, PayloadID = (int)AudioCodecsEnum.PCMU },
                new AudioFormat { Codec = AudioCodecsEnum.PCMA, PayloadID = (int)AudioCodecsEnum.PCMA },
                new AudioFormat { Codec = AudioCodecsEnum.G722, PayloadID = (int)AudioCodecsEnum.G722 } };

            return formats;
        }

        public void SetAudioSendingFormat(AudioFormat audioFormat)
        {
            _selectedAudioFormat = audioFormat;
        }

        /// <summary>
        /// Event handler for playing audio samples received from the remote call party.
        /// </summary>
        /// <param name="pcmSample"></param>
        public void GotRemoteAudioSample(byte[] pcmSample)
        {
            _waveProvider?.AddSamples(pcmSample, 0, pcmSample.Length);
        }

        public List<VideoFormat> GetVideoFormats()
        {
            return null;
        }

        public void GotRemoteAudioFrame(int payloadID, int timestampDuration, byte[] encodedFrame)
        {
            throw new System.NotImplementedException();
        }

        public void GotRemoteVideoFrame(int payloadID, int timestampDuration, byte[] encodedFrame)
        {
            throw new System.NotImplementedException();
        }

        public void GotRemoteVideoSample(int pixelFormat, byte[] bmpSample)
        {
            throw new System.NotImplementedException();
        }

        public void SetVideoSendingFormat(VideoFormat videoFormat)
        {
            throw new System.NotImplementedException();
        }

        public void PauseAudioSource()
        {
            _waveInEvent?.StopRecording();
        }

        public void ResumeAudioSource()
        {
            _waveInEvent?.StartRecording();
        }

        public void PauseVideoSource()
        {
            throw new System.NotImplementedException();
        }

        public void ResumeVideoSource()
        {
            throw new System.NotImplementedException();
        }
    }
}
