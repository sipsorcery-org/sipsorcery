﻿//-----------------------------------------------------------------------------
// Filename: AudioChannel.cs
//
// Description: This class manages the coding and decoding of audio from physical
// devices into and for RTP packets. 
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2018 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using NAudio.Codecs;
using NAudio.Wave;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class AudioChannel
    {
        private ILog logger = AppState.logger;

        private BufferedWaveProvider m_waveProvider;
        private WaveInEvent m_waveInEvent;          // Device used to get audio sample from, e.g. microphone.
        private WaveOut m_waveOut;                  // Device used to play audio samples, e.g. speaker.
        private WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);   // The format that both the input and output audio streams will use, i.e. PCMU.
        private bool _recordingStarted;            // When true indicates that the input device has been opended to start receiving samples.

        public readonly List<SDPMediaFormat> SupportedAudioTypes = new List<SDPMediaFormat>(){ new SDPMediaFormat((int)SDPMediaFormatsEnum.PCMU) };

        public event Action<byte[]> SampleReady;

        public AudioChannel()
        {
            // Set up the device that will play the audio from the RTP received from the remote end of the call.
            m_waveOut = new WaveOut();
            m_waveProvider = new BufferedWaveProvider(_waveFormat);
            m_waveProvider.BufferLength = 100000;
            m_waveOut.Init(m_waveProvider);
            m_waveOut.Play();

            // Set up the input device that will provide audio samples that can be encoded, packaged into RTP and sent to
            // the remote end of the call.
            m_waveInEvent = new WaveInEvent();
            m_waveInEvent.BufferMilliseconds = 20;
            m_waveInEvent.NumberOfBuffers = 1;
            m_waveInEvent.DeviceNumber = 0;
            m_waveInEvent.DataAvailable += AudioSampleAvailable;
            m_waveInEvent.WaveFormat = _waveFormat;
        }

        /// <summary>
        /// Gets the media announcement to include in the SDP payload for a call.
        /// </summary>
        /// <returns>A media announcement containing all the suuported audio codecs.</returns>
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
            if(!_recordingStarted)
            {
                _recordingStarted = true;
                m_waveInEvent.StartRecording();
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
        /// Called when the audo channel is no longer required, such as when the VoIP call using it has terminated, and all resources can be shutdown
        /// and closed.
        /// </summary>
        public void Close()
        {
            try
            {
                logger.Debug("Closing audio channel.");

                if (_recordingStarted)
                {
                    _recordingStarted = false;
                    m_waveInEvent.StopRecording();
                }

                if (m_waveOut.PlaybackState == PlaybackState.Playing)
                {
                    m_waveOut.Stop(); 
                    m_waveOut.Dispose();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AudioChannel Close. " + excp.Message);
            }
        }
    }
}
