//-----------------------------------------------------------------------------
// Filename: RTPMediaSessionManager.cs
//
// Description: Create, connects, and manages the RTP media session and the 
// media manager for the call.
//
// Author(s):
// Yizchok G.
//
// History:
// 12/23/2019	Yitzchok	  Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Sockets;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.SoftPhone
{
    /// <summary>
    /// Create, connects, and manages the RTP media session and the media manager for 
    /// the call. Acts as the bridge between the media sources (microphone, webcam
    /// music on hold), media output (speaker, screen) and the RTP media session.
    /// </summary>
    public class RTPMediaSessionManager
    {
        private readonly MediaManager _mediaManager;
        private readonly MusicOnHold _musicOnHold;

        /// <summary>
        /// The RTP media session being managed.
        /// </summary>
        public RTPMediaSession RTPMediaSession { get; private set; }

        /// <summary>
        /// The RTP timestamp to set on audio packets sent for the RTP session.
        /// </summary>
        private uint _audioTimestamp = 0;

        /// <summary>
        /// The default format supported by this application. Note that the format
        /// can only be changed to one supported by the RTP session class.
        /// </summary>
        public SDPMediaFormatsEnum DefaultAudioFormat { get; set; } = SDPMediaFormatsEnum.PCMU;

        public RTPMediaSessionManager(MediaManager mediaManager, MusicOnHold musicOnHold)
        {
            _mediaManager = mediaManager;
            _musicOnHold = musicOnHold;
        }

        /// <summary>
        /// Creates a new RTP media session object.
        /// </summary>
        /// <param name="addressFamily">The type of socket the RTP session should use, IPv4 or IPv6.</param>
        /// <returns>A new RTP media session object.</returns>
        public virtual RTPMediaSession Create(AddressFamily addressFamily)
        {
            RTPMediaSession = new RTPMediaSession(SDPMediaTypesEnum.audio, new SDPMediaFormat(DefaultAudioFormat), addressFamily);

            RTPMediaSession.OnRtpClosed += (reason) =>
            {
                _mediaManager.OnLocalAudioSampleReady -= LocalAudioSampleReadyForSession;
                _musicOnHold.OnAudioSampleReady -= LocalAudioSampleReadyForSession;
            };

            _mediaManager.OnLocalAudioSampleReady += LocalAudioSampleReadyForSession;
            RTPMediaSession.OnRtpPacketReceived += RemoteRtpPacketReceived;

            return RTPMediaSession;
        }

        /// <summary>
        /// Creates a new RTP media session object based on a remote Session Description 
        /// Protocol (SDP) offer.
        /// </summary>
        /// <param name="offerSdp">The SDP offer from the remote party.</param>
        /// <returns>A new RTP media session object.</returns>
        public virtual RTPMediaSession Create(string offerSdp)
        {
            var remoteSDP = SDP.ParseSDPDescription(offerSdp);
            var dstRtpEndPoint = remoteSDP.GetSDPRTPEndPoint();

            RTPMediaSession = Create(dstRtpEndPoint.Address.AddressFamily);

            return RTPMediaSession;
        }

        /// <summary>
        /// Sets whether the session should use music on hold as the audio source.
        /// </summary>
        /// <param name="doUse">If true music on hold will be used. If false the default
        /// audio source will be used.</param>
        public void UseMusicOnHold(bool doUse)
        {
            if (doUse)
            {
                _mediaManager.OnLocalAudioSampleReady -= LocalAudioSampleReadyForSession;
                _musicOnHold.OnAudioSampleReady += LocalAudioSampleReadyForSession;
                _musicOnHold.Start();
            }
            else
            {
                _musicOnHold.OnAudioSampleReady -= LocalAudioSampleReadyForSession;
                _mediaManager.OnLocalAudioSampleReady += LocalAudioSampleReadyForSession;
            }
        }

        /// <summary>
        /// Event handler for a default audio sample being ready from a local media source.
        /// The RTP media session Forwards samples from the local audio input device to RTP session.
        /// We leave it up to the RTP session to decide if it wants to transmit the sample or not.
        /// For example an RTP session will know whether it's on hold and whether it needs to send
        /// audio to the remote call party or not.
        /// </summary>
        /// <param name="sample">The audio sample</param>
        private void LocalAudioSampleReadyForSession(byte[] sample)
        {
            int payloadID = 0; // Convert.ToInt32(RTPMediaSession.MediaAnnouncements.First(x => x.Media == SDPMediaTypesEnum.audio).MediaFormats.First().FormatID);
            RTPMediaSession.SendAudioFrame(_audioTimestamp, payloadID, sample);
            _audioTimestamp += (uint)sample.Length; // This only works for cases where 1 sample is 1 byte.
        }

        /// <summary>
        /// Event handler for the availability of a new RTP packet from a remote party.
        /// </summary>
        /// <param name="rtpPacket">The RTP packet from the remote party.</param>
        private void RemoteRtpPacketReceived(SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            _mediaManager.EncodedAudioSampleReceived(rtpPacket.Payload);
        }
    }
}
