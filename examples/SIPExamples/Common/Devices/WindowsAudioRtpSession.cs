//-----------------------------------------------------------------------------
// Filename: WindowsAudioRtpSession.cs
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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;

namespace demo
{
    public class WindowsAudioRtpSession : RtpAudioSession
    {
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;
        private const int AUDIO_INPUTDEVICE_INDEX = -1;
        private const int AUDIO_OUTPUTDEVICE_INDEX = -1;

        private static ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static readonly WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);

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

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public WindowsAudioRtpSession()
            : base(new AudioSourceOptions { AudioSource = AudioSourcesEnum.CaptureDevice },
                  new List<SIPSorcery.Net.SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU, SDPMediaFormatsEnum.PCMA, SDPMediaFormatsEnum.G722 })
        { }

        /// <summary>
        /// Starts the media capturing/source devices.
        /// </summary>
        public override async Task Start()
        {
            if (!IsStarted)
            {
                await base.Start();

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

                base.OnRemoteAudioSampleReady += RemoteAudioSampleAvailable;
            }
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        private void LocalAudioSampleAvailable(object sender, WaveInEventArgs args)
        {
            base.SendAudioSample(args.Buffer, args.BytesRecorded, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
        }

        /// <summary>
        /// Event handler for playing audio samples received from the remote call party.
        /// </summary>
        /// <param name="pcmSample"></param>
        private void RemoteAudioSampleAvailable(byte[] pcmSample)
        {
            _waveProvider?.AddSamples(pcmSample, 0, pcmSample.Length);
        }

        /// <summary>
        /// Closes the session.
        /// </summary>
        /// <param name="reason">Reason for the closure.</param>
        public override void Close(string reason)
        {
            if (!IsClosed)
            {
                base.Close(reason);

                base.OnRemoteAudioSampleReady -= RemoteAudioSampleAvailable;

                _waveOutEvent?.Stop();

                if (_waveInEvent != null)
                {
                    _waveInEvent.DataAvailable -= LocalAudioSampleAvailable;
                    _waveInEvent.StopRecording();
                }
            }
        }
    }
}
