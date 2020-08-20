//-----------------------------------------------------------------------------
// Filename: WindowsAudioVideoSession.cs
//
// Description: Extends the WindowsAudioSession to include video
// encoding and decoding capabilities.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Aug 2020  Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.Windows
{
    public class WindowsAudioVideoSession : WindowsAudioSession
    {
        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public WindowsAudioVideoSession()
        { }

        /// <summary>
        /// Starts the media capturing/source devices.
        /// </summary>
        public Task Start()
        {
            if (!_isStarted)
            {
               
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
                //new AudioFormat { Codec = AudioCodecsEnum.PCMU, PayloadID = (int)AudioCodecsEnum.PCMU },
                //new AudioFormat { Codec = AudioCodecsEnum.PCMA, PayloadID = (int)AudioCodecsEnum.PCMA },
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
        public void ProcessRemoteAudioSample(byte[] pcmSample)
        {
            _waveProvider?.AddSamples(pcmSample, 0, pcmSample.Length);
        }

        public List<VideoFormat> GetVideoFormats()
        {
            return null;
        }

        public void ProcessRemoteRtpAudioFrame(int payloadID, int timestampDuration, byte[] encodedFrame)
        {
            throw new System.NotImplementedException();
        }

        public void ProcessRemoteRtpVideoFrame(int payloadID, int timestampDuration, byte[] encodedFrame)
        {
            throw new System.NotImplementedException();
        }

        public void ProcessRemoteVideoSample(int pixelFormat, byte[] bmpSample)
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
