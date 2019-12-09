//-----------------------------------------------------------------------------
// Filename: RTPManager.cs
//
// Description: This class manages the RTP transmission and reception for VoIP clients.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Dec 2014  Aaron Clauson   Refactored from MediaManager, Hobart, Australia.
// 10 Feb 2015  Aaron Clauson   Switched from using internal RTP channel to use http://net7mma.codeplex.com/ (and then back again).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class RTPManager
    {
        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int VIDEO_PAYLOAD_TYPE = 96;

        private ILog logger = AppState.logger;

        private RTPSession _rtpVideoSession;
        private RTPSession _rtpAudioSession;

        // Audio and Video events.
        public event Action<byte[], int> OnRemoteAudioSampleReady;     // Fires when a remote audio sample is ready for display.
        public event Action<byte[], int> OnRemoteVideoSampleReady;     // Fires when a remote video sample is ready for display.

        public RTPManager(bool includeAudio, bool includeVideo)
        {
            if (includeAudio)
            {
                _rtpAudioSession = new RTPSession((int)SDPMediaFormatsEnum.PCMU, null, null, true);
                _rtpAudioSession.OnReceivedSampleReady += (sample) => OnRemoteAudioSampleReady?.Invoke(sample, sample.Length);
            }

            if (includeVideo)
            {
                _rtpVideoSession = new RTPSession(VIDEO_PAYLOAD_TYPE, null, null, true);
                _rtpVideoSession.OnReceivedSampleReady += (sample) => OnRemoteVideoSampleReady?.Invoke(sample, sample.Length);
            }
        }

        /// <summary>
        /// Gets an SDP packet that can be used by VoIP clients to negotiate an audio and/or video stream.
        /// </summary>
        /// <param name="callDstAddress">The destination address the call is being palced to. Given this address the 
        /// RTP socket address can be chosen based on the local address chosen by the operating system to route to it.</param>
        /// <returns>An SDP packet that can be used by a VoIP client when initiating a call.</returns>
        public SDP GetSDP(IPAddress callDstAddress)
        {
            IPAddress rtpIPAddress = NetServices.GetLocalAddressForRemote(callDstAddress);

            // TODO: Need to combine the video media announcement if required.
            return _rtpAudioSession.GetSDP(rtpIPAddress);
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
                _rtpAudioSession.DestinationEndPoint = audioRemoteEndPoint;
            }
            else if (videoRemoteEndPoint != null)
            {
                logger.Debug("Remote RTP video end point set as " + videoRemoteEndPoint + ".");
                _rtpVideoSession.DestinationEndPoint = videoRemoteEndPoint;
            }
        }

        /// <summary>
        /// Event handler for processing audio samples from the audio channel.
        /// </summary> 
        /// <param name="sample">The audio sample ready for transmission.</param>
        public void AudioChannelSampleReady(byte[] sample)
        {
            _rtpAudioSession.SendAudioFrame(160, sample);
        }

        public void LocalVideoSampleReady(byte[] sample)
        {
            _rtpVideoSession.SendVp8Frame(VP8_TIMESTAMP_SPACING, sample);
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
                _rtpAudioSession?.Close();
                _rtpVideoSession?.Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTP Manager Close. " + excp);
            }
        }
    }
}
