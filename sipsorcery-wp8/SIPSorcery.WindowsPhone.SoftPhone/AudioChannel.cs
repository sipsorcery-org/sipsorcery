//-----------------------------------------------------------------------------
// Filename: AudioChannel.cs
//
// Description: This class manages the coding and decoding of audio from physical
// devices into and for RTP packets. 
// 
// History:
// 01 Apr 2013	Aaron Clauson	Modified for Windows Phone from the Windows desktop version.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2013 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using SIPSorcery.Net;
using SIPSorcery.Sys;
//using SIPSorcery.Sys.Net;
//using NAudio;
//using NAudio.Codecs;
//using NAudio.CoreAudioApi;
//using NAudio.Wave;
using log4net;

namespace SIPSorcery.WP.SoftPhone
{
    public class AudioChannel
    {
        private const int DEFAULT_START_RTP_PORT = 10000;

        //private IPAddress _defaultLocalAddress = SIPSoftPhoneState.DefaultLocalAddress;

        private ILog _audioLogger = AppState.GetLogger("audiodevice");

        private WPRTPChannel m_rtpChannel;            // Manages the UDP connection that RTP packets will be sent back and forth on.
        private IPEndPoint _rtpEndPoint;            // The local end point being used for the RTP channel.
        //private BufferedWaveProvider m_waveProvider;
        //private WaveInEvent m_waveInEvent;          // Device used to get audio sample from, e.g. microphone.
        //private WaveOut m_waveOut;                  // Device used to play audio samples, e.g. speaker.
        //private WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);   // The format that both the input and output audio streams will use, i.e. PCMU.
        private bool m_recordingStarted;            // When true indicates that the input device has been opended to start receiving samples.
        private bool m_closed = false;

        // Diagnostics variables.
        private DateTime _lastInputSampleReceivedAt;
        private int _inputSampleCount;
        private int _dummyRTPSampleCount;

        public AudioChannel(IPEndPoint rtpEndPoint)
        {
            // Set up the device that will play the audio from the RTP received from the remote end of the call.
            //m_waveOut = new WaveOut();
            //m_waveProvider = new BufferedWaveProvider(_waveFormat);
            //m_waveOut.Init(m_waveProvider);
            //m_waveOut.Play();

            // Set up the input device that will provide audio samples that can be encoded, packaged into RTP and sent to
            // the remote end of the call.
            //m_waveInEvent = new WaveInEvent();
            //m_waveInEvent.BufferMilliseconds = 20;
            //m_waveInEvent.NumberOfBuffers = 1;
            //m_waveInEvent.DeviceNumber = 0;
            //m_waveInEvent.DataAvailable += RTPChannelSampleAvailable;
            //m_waveInEvent.WaveFormat = _waveFormat;


            // Create a UDP socket to use for sending and receiving RTP packets.
            //int port = FreePort.FindNextAvailableUDPPort(DEFAULT_START_RTP_PORT);
            //_rtpEndPoint = new IPEndPoint(_defaultLocalAddress, port);
            _rtpEndPoint = rtpEndPoint;
            m_rtpChannel = new WPRTPChannel(_rtpEndPoint);
            m_rtpChannel.SampleReceived += RTPChannelSampleReceived;

            _audioLogger.Debug("RTP channel endpoint " + _rtpEndPoint.ToString());
        }

        /// <summary>
        /// Gets an SDP packet that can be used by VoIP clients to negotiate an audio connection. The SDP will only
        /// offer PCMU since that's all I've gotten around to handling.
        /// </summary>
        /// <param name="usePublicIP">If true and the public IP address is available from the STUN client then
        /// the public IP address will be used in the SDP otherwise the hsot machine's default IPv4 address will
        /// be used.</param>
        /// <returns>An SDP packet that can be used by a VoIP client when initiating a call.</returns>
        public SDP GetSDP()
        {
            IPAddress rtpIPAddress = _rtpEndPoint.Address;
            int rtpPort = _rtpEndPoint.Port;

            var sdp = new SDP()
            {
                SessionId = Crypto.GetRandomInt(6).ToString(),
                Address = rtpIPAddress.ToString(),
                SessionName = "sipsorcery",
                Timing = "0 0",
                Connection = new SDPConnectionInformation(rtpIPAddress.ToString()),
                Media = new List<SDPMediaAnnouncement>() 
                {
                    new SDPMediaAnnouncement()
                    {
                        Media = SDPMediaTypesEnum.audio,
                        Port = rtpPort,
                        MediaFormats = new List<SDPMediaFormat>() { new SDPMediaFormat((int)SDPMediaFormatsEnum.PCMU) }
                    }
                }
            };

            return sdp;
        }

        /// <summary>
        /// Allows an arbitrary block of bytes to be sent on the RTP channel. This is mainly used for the Gingle
        /// client which needs to send a STUN binding request to the Google Voice gateway.
        /// </summary>
        public void SendRTPRaw(byte[] buffer, int length)
        {
            m_rtpChannel.SendRaw(buffer, length);
        }

        /// <summary>
        /// Sets the remote end point for the RTP channel. This will be set from the SDP packet received from the remote
        /// end of the VoIP call.
        /// </summary>
        /// <param name="remoteEndPoint">The remote end point to send RTP to.</param>
        public void SetRemoteRTPEndPoint(IPEndPoint remoteEndPoint)
        {
            _audioLogger.Debug("Remote RTP end point set as " + remoteEndPoint + ".");
            m_rtpChannel.SetRemoteEndPoint(remoteEndPoint);
            //m_waveInEvent.StartRecording();
            m_recordingStarted = true;
            _lastInputSampleReceivedAt = DateTime.Now;

            ThreadPool.QueueUserWorkItem(delegate { SendDummyRTPStream(); });
        }

        /// <summary>
        /// Event handler for receiving an RTP packet from the remote end of the VoIP call.
        /// </summary>
        /// <param name="rtpPacket">The full RTP packet received.</param>
        /// <param name="payloadOffset">The position in the packet where the payload (audio portion) starts.</param>
        private void RTPChannelSampleReceived(byte[] rtpPacket, int payloadOffset)
        {
            if (rtpPacket != null)
            {
                for (int index = payloadOffset; index < rtpPacket.Length; index++)
                {
                    //short pcm = MuLawDecoder.MuLawToLinearSample(rtpPacket[index]);
                    //byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                    //m_waveProvider.AddSamples(pcmSample, 0, 2);
                }
            }
        }

        private void SendDummyRTPStream()
        {
            try
            {
                while (!m_closed)
                {
                    TimeSpan samplePeriod = DateTime.Now.Subtract(_lastInputSampleReceivedAt);
                    _lastInputSampleReceivedAt = DateTime.Now;
                    _inputSampleCount++;

                    byte[] sample = GetDummyRTPSample();

                    _audioLogger.Debug(_inputSampleCount + " sample period " + samplePeriod.TotalMilliseconds + "ms,  sample bytes " + sample.Length + ".");

                    //byte[] sample = new byte[e.Buffer.Length / 2];
                    //int sampleIndex = 0;

                    //for (int index = 0; index < e.Buffer.Length; index += 2)
                    //{
                    //    var ulawByte = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(e.Buffer, index));
                    //    sample[sampleIndex++] = ulawByte;
                    //}

                    m_rtpChannel.Send(sample, Convert.ToUInt32(sample.Length));

                    Thread.Sleep(20);
                }
            }
            catch (Exception excp)
            {
                _audioLogger.Error("Exception SendDummyRTPStream. " + excp.Message);
            }
        }

        private byte[] GetDummyRTPSample()
        {
            List<byte> sample = new List<byte>();

            //for (int j = 0; j < 10; j++)
            //{
                //double waves = 0;
                //// dtmf tone 1

                //waves += Math.Sin(((Math.PI * 2.0f / 8000) * 697.0f) * _dummyRTPSampleCount);
                //waves += Math.Sin(((Math.PI * 2.0f / 8000) * 1209.0f) * _dummyRTPSampleCount);
                //waves *= 8191.0f;   //amplitude   
                
                //values[j] = (UInt16)waves;

                sample.AddRange(BitConverter.GetBytes(Math.Sin(((Math.PI * 2.0f / 8000) * 697.0f) * _dummyRTPSampleCount)));
                _dummyRTPSampleCount++;
                sample.AddRange(BitConverter.GetBytes(Math.Sin(((Math.PI * 2.0f / 8000) * 697.0f) * _dummyRTPSampleCount)));
                _dummyRTPSampleCount++;
                sample.AddRange(BitConverter.GetBytes(Math.Sin(((Math.PI * 2.0f / 8000) * 697.0f) * _dummyRTPSampleCount)));
                _dummyRTPSampleCount++;
                sample.AddRange(BitConverter.GetBytes(Math.Sin(((Math.PI * 2.0f / 8000) * 697.0f) * _dummyRTPSampleCount)));
                _dummyRTPSampleCount++;
            //}

                return sample.ToArray();
        }

        /// <summary>
        /// Event handler for receiving an audio sample that is ready for encoding, packaging into RTP and sending to the remote end
        /// of the VoIP call.
        /// </summary>
        //private void RTPChannelSampleAvailable(object sender, WaveInEventArgs e)
        //{
        //    TimeSpan samplePeriod = DateTime.Now.Subtract(_lastInputSampleReceivedAt);
        //    _lastInputSampleReceivedAt = DateTime.Now;
        //    _inputSampleCount++;

        //    _audioLogger.Debug(_inputSampleCount + " sample period " + samplePeriod.TotalMilliseconds + "ms,  sample bytes " + e.BytesRecorded + ".");

        //    byte[] sample = new byte[e.Buffer.Length / 2];
        //    int sampleIndex = 0;

        //    for (int index = 0; index < e.Buffer.Length; index += 2)
        //    {
        //        var ulawByte = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(e.Buffer, index));
        //        sample[sampleIndex++] = ulawByte;
        //    }

        //    m_rtpChannel.Send(sample, Convert.ToUInt32(sample.Length));
        //}

        /// <summary>
        /// Called when the audo channel is no longer required, such as when the VoIP call using it has terminated, and all resources can be shutdown
        /// and closed.
        /// </summary>
        public void Close()
        {
            try
            {
                _audioLogger.Debug("Closing audio channel.");

                m_closed = true;

                m_rtpChannel.Close();

                if (m_recordingStarted)
                {
                    //m_waveInEvent.StopRecording();
                }

                //if (m_waveOut.PlaybackState == PlaybackState.Playing)
                //{
                //    m_waveOut.Stop(); m_waveOut.Dispose();
                //}
            }
            catch (Exception excp)
            {
                _audioLogger.Error("Exception AudioChannel Close. " + excp.Message);
            }
        }
    }
}
