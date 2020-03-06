//-----------------------------------------------------------------------------
// Filename: AudioChannel.cs
//
// Description: This class manages the coding and decoding of audio from physical
// devices and playback from samples recevied from RTP packets. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NAudio.Codecs;
using NAudio.Wave;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SoftPhone
{
    public class AudioChannel
    {
        public const int AUDIO_INPUT_BUFFER_MILLISECONDS = 40;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private BufferedWaveProvider m_waveProvider;
        private WaveInEvent m_waveInEvent;          // Device used to get audio sample from, e.g. microphone.
        private WaveOutEvent m_waveOut;                  // Device used to play audio samples, e.g. speaker.
        private WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);   // The format that both the input and output audio streams will use, i.e. PCMU.
        private bool _recordingStarted;            // When true indicates that the input device has been opened to start receiving samples.

        public readonly List<SDPMediaFormat> SupportedAudioTypes = new List<SDPMediaFormat>() { new SDPMediaFormat((int)SDPMediaFormatsEnum.PCMU) };

        public event Action<byte[]> SampleReady;

        public AudioChannel()
        {
            // Set up the device that will play the audio from the RTP received from the remote end of the call.
            m_waveOut = new WaveOutEvent();
            m_waveProvider = new BufferedWaveProvider(_waveFormat);
            m_waveProvider.DiscardOnBufferOverflow = true; // See https://github.com/sipsorcery/sipsorcery/issues/53
            m_waveProvider.BufferLength = 100000;
            m_waveOut.Init(m_waveProvider);
            m_waveOut.Play();

            // Set up the input device that will provide audio samples that can be encoded, packaged into RTP and sent to
            // the remote end of the call.
            if (WaveInEvent.DeviceCount == 0)
            {
                logger.LogWarning("No audio input devices available. No audio will be sent.");
            }
            else
            {
                m_waveInEvent = new WaveInEvent();
                m_waveInEvent.BufferMilliseconds = AUDIO_INPUT_BUFFER_MILLISECONDS;
                m_waveInEvent.NumberOfBuffers = 2;
                m_waveInEvent.DeviceNumber = 0;
                m_waveInEvent.DataAvailable += AudioSampleAvailable;
                m_waveInEvent.WaveFormat = _waveFormat;
            }
        }

        /// <summary>
        /// Gets the media announcement to include in the SDP payload for a call.
        /// </summary>
        /// <returns>A media announcement containing all the supported audio codecs.</returns>
        public SDPMediaAnnouncement GetMediaAnnouncement()
        {
            return new SDPMediaAnnouncement()
            {
                Media = SDPMediaTypesEnum.audio,
                MediaFormats = SupportedAudioTypes
            };
        }

        public void StartRecording()
        {
            if (!_recordingStarted)
            {
                _recordingStarted = true;
                m_waveInEvent?.StartRecording();
            }
        }

        /// <summary>
        /// Event handler for receiving an RTP packet containing and audio payload from the remote end of the VoIP call.
        /// </summary>
        /// <param name="sample">The audio sample.</param>
        /// <param name="offset">The offset in the sample that the audio starts.</param>
        public void AudioSampleReceived(byte[] sample, int offset)
        {
            if (sample != null)
            {
                for (int index = offset; index < sample.Length; index++)
                {
                    short pcm = MuLawDecoder.MuLawToLinearSample(sample[index]);
                    byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                    m_waveProvider.AddSamples(pcmSample, 0, 2);
                }
            }
        }

        /// <summary>
        /// Event handler for receiving an audio sample that is ready for encoding, packaging into RTP and sending to the remote end
        /// of the VoIP call.
        /// </summary>
        private void AudioSampleAvailable(object sender, WaveInEventArgs e)
        {
            //TimeSpan samplePeriod = DateTime.Now.Subtract(_lastInputSampleReceivedAt);
            //_lastInputSampleReceivedAt = DateTime.Now;
            //_inputSampleCount++;

            //_audioLogger.Debug(_inputSampleCount + " sample period " + samplePeriod.TotalMilliseconds + "ms,  sample bytes " + e.BytesRecorded + ".");

            if (SampleReady != null)
            {
                byte[] sample = new byte[e.Buffer.Length / 2];
                int sampleIndex = 0;

                for (int index = 0; index < e.Buffer.Length; index += 2)
                {
                    var ulawByte = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(e.Buffer, index));
                    sample[sampleIndex++] = ulawByte;
                }

                SampleReady(sample);
            }
        }

        /// <summary>
        /// Called when the audio channel is no longer required, such as when the VoIP call using it has terminated, and all resources can be shutdown
        /// and closed.
        /// </summary>
        public void Close()
        {
            try
            {
                logger.LogDebug("Closing audio channel.");

                if (_recordingStarted)
                {
                    _recordingStarted = false;

                    m_waveInEvent?.StopRecording();
                }

                if (m_waveOut.PlaybackState == PlaybackState.Playing)
                {
                    m_waveOut.Stop();
                    m_waveOut.Dispose();
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception AudioChannel Close. " + excp.Message);
            }
        }
    }
}
