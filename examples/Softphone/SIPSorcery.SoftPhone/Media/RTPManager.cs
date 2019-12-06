﻿//-----------------------------------------------------------------------------
// Filename: RTPManager.cs
//
// Description: This class manages the RTP transmission and reception for VoIP clients.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Dec 2014	Aaron Clauson	Refactored from MediaManager, Hobart, Australia.
// 10 Feb 2015  Aaron Clauson   Switched from using internal RTP channel to use http://net7mma.codeplex.com/ (and then back again).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class RTPManager
    {
        private const int DEFAULT_START_RTP_PORT = 15000;
        private const int DEFAULT_END_RTP_PORT = 15200;
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int VIDEO_PAYLOAD_TYPE = 96;
        private const string SDP_TRANSPORT = "RTP/AVP";

        private ILog logger = AppState.logger;

        private SDP _remoteSDP;
        private RTPChannel _rtpVideoChannel;            // Manages the UDP connection that RTP video packets will be sent back and forth on.
        private RTPChannel _rtpAudioChannel;            // Manages the UDP connection that RTP video packets will be sent back and forth on.
        private bool _stop = false;

        private IPEndPoint _remoteAudioEP;
        private IPEndPoint _remoteVideoEP;

        // Audio and Video events.
        public event Action<byte[], int> OnRemoteAudioSampleReady;     // Fires when a remote audio sample is ready for display.
        public event Action<byte[], int> OnRemoteVideoSampleReady;     // Fires when a remote video sample is ready for display.

        public RTPManager(bool includeAudio, bool includeVideo)
        {
            if (includeAudio)
            {
                // Create a UDP socket to use for sending and receiving RTP audio packets.
                _rtpAudioChannel = new RTPChannel();
                _rtpAudioChannel.SetFrameType(FrameTypesEnum.Audio);
                _rtpAudioChannel.ReservePorts(DEFAULT_START_RTP_PORT, DEFAULT_END_RTP_PORT);
                _rtpAudioChannel.OnFrameReady += AudioFrameReady;
            }

            if (includeVideo)
            {
                _rtpVideoChannel = new RTPChannel();
                _rtpVideoChannel.SetFrameType(FrameTypesEnum.VP8);
                _rtpVideoChannel.ReservePorts(DEFAULT_START_RTP_PORT, DEFAULT_END_RTP_PORT);
                _rtpVideoChannel.OnFrameReady += VideoFrameReady;
                _rtpVideoChannel.OnRTPSocketDisconnected += () => { };
            }
        }

        /// <summary>
        /// Gets an SDP packet that can be used by VoIP clients to negotiate an audio connection. The SDP will only
        /// offer PCMU since that's all I've gotten around to handling.
        /// </summary>
        /// <param name="callDstAddress">The destination address the call is being palced to. Given this address the 
        /// RTP socket address can be chosen based on the local address chosen by the operating system to route to it.</param>
        /// <returns>An SDP packet that can be used by a VoIP client when initiating a call.</returns>
        public SDP GetSDP(IPAddress callDstAddress)
        {
            IPAddress rtpIPAddress = NetServices.GetLocalAddressForRemote(callDstAddress);

            var sdp = new SDP()
            {
                SessionId = Crypto.GetRandomInt(5).ToString(),
                Address = rtpIPAddress.ToString(),
                SessionName = "sipsorcery",
                Timing = "0 0",
                Connection = new SDPConnectionInformation(rtpIPAddress.ToString()),
            };

            if (_rtpAudioChannel != null)
            {
                var audioAnnouncement = new SDPMediaAnnouncement()
                {
                    Media = SDPMediaTypesEnum.audio,
                    MediaFormats = new List<SDPMediaFormat>() { new SDPMediaFormat((int)SDPMediaFormatsEnum.PCMU, "PCMU", 8000) }
                };
                audioAnnouncement.Port = _rtpAudioChannel.RTPPort;
                sdp.Media.Add(audioAnnouncement);
            }

            if (_rtpVideoChannel != null)
            {
                var videoAnnouncement = new SDPMediaAnnouncement()
                {
                    Media = SDPMediaTypesEnum.video,
                    MediaFormats = new List<SDPMediaFormat>() { new SDPMediaFormat(96, "VP8", 90000) }
                };
                videoAnnouncement.Port = _rtpVideoChannel.RTPPort;
                sdp.Media.Add(videoAnnouncement);
            }

            return sdp;
        }

        /// <summary>
        /// Allows an arbitrary block of bytes to be sent on the RTP channel. This is mainly used for the Gingle
        /// client which needs to send a STUN binding request to the Google Voice gateway.
        /// </summary>
        public void SendRTPRaw(byte[] buffer, int length)
        {
            _rtpAudioChannel?.SendRTPRaw(buffer);
        }

        /// <summary>
        /// Sets the remote end point for the RTP channel. This will be set from the SDP packet received from the remote
        /// end of the VoIP call.
        /// </summary>
        /// <param name="remoteEndPoint">The remote end point to send RTP to.</param>
        public void SetRemoteRTPEndPoints(IPEndPoint audioRemoteEndPoint, IPEndPoint videoRemoteEndPoint)
        {
            if (audioRemoteEndPoint != null)
            {
                logger.Debug("Remote RTP audio end point set as " + audioRemoteEndPoint + ".");
                _rtpAudioChannel.RemoteEndPoint = audioRemoteEndPoint;
                _rtpAudioChannel.Start();
            }
            else if (videoRemoteEndPoint != null)
            {
                logger.Debug("Remote RTP video end point set as " + videoRemoteEndPoint + ".");
                _remoteVideoEP = videoRemoteEndPoint;
                _rtpVideoChannel.RemoteEndPoint = videoRemoteEndPoint;
                _rtpVideoChannel.Start();
            }
        }

        private void AudioFrameReady(RTPFrame frame)
        {
            if (OnRemoteAudioSampleReady != null)
            {
                var payload = frame.GetFramePayload();
                //System.Diagnostics.Debug.WriteLine("Remote audio frame received " + payload.Length + " bytes.");
                OnRemoteAudioSampleReady(payload, payload.Length);
            }
        }

        private void VideoFrameReady(RTPFrame frame)
        {
            if (OnRemoteVideoSampleReady != null)
            {
                //System.Diagnostics.Debug.WriteLine("Remote video frame received " + frame.FramePayload.Length + " bytes.");
                var payload = frame.GetFramePayload();
                OnRemoteVideoSampleReady(payload, payload.Length);
            }
        }

        public void SetRemoteSDP(SDP sdp)
        {
            _remoteSDP = sdp;

            IPAddress remoteRTPIPAddress = IPAddress.Parse(sdp.Connection.ConnectionAddress);
            int remoteAudioPort = 0;
            int remoteVideoPort = 0;

            var audioAnnouncement = sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).FirstOrDefault();
            if (audioAnnouncement != null)
            {
                remoteAudioPort = audioAnnouncement.Port;
            }

            var videoAnnouncement = sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).FirstOrDefault();
            if (videoAnnouncement != null)
            {
                remoteVideoPort = videoAnnouncement.Port;
            }

            if (remoteAudioPort != 0)
            {
                _remoteAudioEP = new IPEndPoint(remoteRTPIPAddress, remoteAudioPort);
                logger.Debug("RTP channel remote end point set to audio socket of " + _remoteAudioEP + ".");
                _rtpAudioChannel.RemoteEndPoint = _remoteAudioEP;
                _rtpAudioChannel.Start();
            }

            if (remoteVideoPort != 0)
            {
                _remoteVideoEP = new IPEndPoint(remoteRTPIPAddress, remoteVideoPort);
                logger.Debug("RTP channel remote end point set to video socket of " + _remoteVideoEP + ".");
                _rtpVideoChannel.RemoteEndPoint = new IPEndPoint(remoteRTPIPAddress, remoteVideoPort);
                _rtpVideoChannel.Start();
            }

            if (remoteAudioPort == 0 && remoteVideoPort == 0)
            {
                logger.Warn("No audio or video end point could be extracted from the remote SDP.");
            }
        }

        /// <summary>
        /// Event handler for processing audio samples from the audio channel.
        /// </summary>
        /// <param name="sample">The audio sample ready for transmission.</param>
        public void AudioChannelSampleReady(byte[] sample)
        {
            if (_rtpAudioChannel.RemoteEndPoint != null)
            {
                _rtpAudioChannel.SendAudioFrame(sample, 160, (int)SDPMediaFormatsEnum.PCMU);
            }
        }

        public void LocalVideoSampleReady(byte[] sample)
        {
            if (sample != null && _remoteVideoEP != null && !_stop)
            {
                _rtpVideoChannel.SendVP8Frame(sample, VP8_TIMESTAMP_SPACING, VIDEO_PAYLOAD_TYPE);
            }
        }

        /// <summary>
        /// Called when the media channels are no longer required, such as when the VoIP call using it has terminated, and all resources can be shutdown
        /// and closed.
        /// </summary>
        public void Close()
        {
            try
            {
                logger.Debug("RTP Manager closing.");

                _stop = true;

                if (_rtpAudioChannel != null)
                {
                    _rtpAudioChannel.OnFrameReady -= AudioFrameReady;
                    _rtpAudioChannel.Close();
                }

                if (_rtpVideoChannel != null)
                {
                    _rtpVideoChannel.OnFrameReady -= VideoFrameReady;
                    _rtpVideoChannel.Close();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTP Manager Close. " + excp);
            }
        }
    }
}
