//-----------------------------------------------------------------------------
// Filename: RTCPeerConnection.cs
//
// Description: Represents a WebRTC RTCPeerConnection.
// Specification for including ICE candidates with a
// Session Description:
// -  "Session Description Protocol (SDP) Offer/Answer procedures for
//    Interactive Connectivity Establishment(ICE)"
//    https://tools.ietf.org/html/draft-ietf-mmusic-ice-sip-sdp-39
//
//
// History:
// 04 Mar 2016	Aaron Clauson	Created.
// 25 Aug 2019  Aaron Clauson   Updated from video only to audio and video.
// 18 Jan 2020  Aaron Clauson   Combined WebRTCPeer and WebRTCSession.
// 16 Mar 2020  Aaron Clauson   Refactoring to support RTCPeerConnection interface.
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
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate int DoDtlsHandshakeDelegate(RTCPeerConnection rtcPeerConnection);

    /// <summary>
    /// Options for creating the SDP offer.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dictionary-rtcofferoptions-members.
    /// </remarks>
    //public class RTCOfferOptions
    //{
    //    /// <summary>
    //    /// If true then a new set of ICE credentials will be generated otherwise any
    //    /// existing set of credentials will be used.
    //    /// </summary>
    //    public bool iceRestart;
    //}

    /// <summary>
    /// Initialiser for the RTCSessionDescription instance.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#rtcsessiondescription-class.
    /// </remarks>
    public class RTCSessionDescriptionInit
    {
        /// <summary>
        /// The type of the Session Description.
        /// </summary>
        public RTCSdpType type { get; set; }

        /// <summary>
        /// A string representation of the Session Description.
        /// </summary>
        public string sdp { get; set; }
    }

    /// <summary>
    /// Describes a pairing of an RTP sender and receiver and their shared state. The state
    /// is set by and relevant for the SDP that is controlling the RTP.
    /// </summary>
    //public class RTCRtpTransceiver
    //{
    //    /// <summary>
    //    /// The media ID of the SDP m-line associated with this transceiver.
    //    /// </summary>
    //    public string MID { get; private set; }

    //    /// <summary>
    //    /// The current state of the RTP flow between us and the remote party.
    //    /// </summary>
    //    public MediaStreamStatusEnum Direction { get; private set; } = MediaStreamStatusEnum.SendRecv;

    //    public RTCRtpTransceiver(string mid)
    //    {
    //        MID = mid;
    //    }

    //    public void SetStreamStatus(MediaStreamStatusEnum direction)
    //    {
    //        Direction = direction;
    //    }
    //}

    ///// <summary>
    ///// 
    ///// </summary>
    ///// <remarks>
    ///// As specified in https://www.w3.org/TR/webrtc/#rtcsessiondescription-class.
    ///// </remarks>
    //public class RTCSessionDescription
    //{
    //    /// <summary>
    //    /// The type of the Session Description.
    //    /// </summary>
    //    public RTCSdpType type;

    //    /// <summary>
    //    /// A string representation of the Session Description.
    //    /// </summary>
    //    public string sdp;

    //    /// <summary>
    //    /// Creates a new session description instance.
    //    /// </summary>
    //    /// <param name="init">Optional. Initialisation properties to control the creation of the session object.</param>
    //    public RTCSessionDescription(RTCSessionDescriptionInit init)
    //    {
    //        if (init != null)
    //        {
    //            type = init.type;
    //            sdp = init.sdp;
    //        }
    //    }
    //}

    /// <summary>
    /// Represents a WebRTC RTCPeerConnection.
    /// </summary>
    /// <remarks>
    /// Interface is defined in https://www.w3.org/TR/webrtc/#interface-definition.
    /// The Session Description offer/answer mechanisms are detailed in
    /// https://tools.ietf.org/html/draft-ietf-rtcweb-jsep-26 (or later if the
    /// draft has been updated).
    /// </remarks>
    public class RTCPeerConnection : RTPSession, IRTCPeerConnection
    {
        // SDP constants.
        private new const string RTP_MEDIA_PROFILE = "RTP/SAVP";
        private const string RTCP_MUX_ATTRIBUTE = "a=rtcp-mux";       // Indicates the media announcement is using multiplexed RTCP.
        private const string ICE_SETUP_OFFER_ATTRIBUTE = "a=setup:actpass";     // Indicates ICE agent can act as either the "controlling" or "controlled" peer.
        private const string ICE_SETUP_ANSWER_ATTRIBUTE = "a=setup:passive";    // Indicates ICE agent will act as the "controlled" peer.
        private const string BUNDLE_ATTRIBUTE = "BUNDLE";
        private const string ICE_OPTIONS = "ice2,trickle";                   // Supported ICE options.
        private const string NORMAL_CLOSE_REASON = "normal";

        private readonly string RTCP_ATTRIBUTE = $"a=rtcp:{SDP.IGNORE_RTP_PORT_NUMBER} IN IP4 0.0.0.0";

        private static ILogger logger = Log.Logger;

        public string SessionID { get; private set; }
        public string SdpSessionID;
        public string LocalSdpSessionID;

        public IceSession IceSession { get; private set; }

        public bool IsDtlsNegotiationComplete
        {
            get { return base.IsSecureContextReady; }
        }

        /// <summary>
        /// The raison d'etre for the ICE checks. This represents the end point
        /// that we were able to connect to for the WebRTC session.
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; private set; }

        public RTCSessionDescription localDescription { get; private set; }

        public RTCSessionDescription remoteDescription { get; private set; }

        public RTCSessionDescription currentLocalDescription => throw new NotImplementedException();

        public RTCSessionDescription pendingLocalDescription => throw new NotImplementedException();

        public RTCSessionDescription currentRemoteDescription => throw new NotImplementedException();

        public RTCSessionDescription pendingRemoteDescription => throw new NotImplementedException();

        public RTCSignalingState signalingState { get; private set; } = RTCSignalingState.stable;

        public RTCIceGatheringState iceGatheringState { get; private set; } = RTCIceGatheringState.@new;

        public RTCIceConnectionState iceConnectionState { get; private set; } = RTCIceConnectionState.@new;

        public RTCPeerConnectionState connectionState { get; private set; } = RTCPeerConnectionState.@new;

        public bool canTrickleIceCandidates { get => throw new NotImplementedException(); }

        private RTCConfiguration _configuration;
        private RTCCertificate _currentCertificate;

        /// <summary>
        /// Informs the application that session negotiation needs to be done (i.e. a createOffer call 
        /// followed by setLocalDescription).
        /// </summary>
        public event Action onnegotiationneeded;

        /// <summary>
        /// A new ICE candidate is available for the Peer Connection.
        /// </summary>
        public event Action<RTCIceCandidate> onicecandidate;

        /// <summary>
        /// A failure occurred when gathering ICE candidates.
        /// </summary>
        public event Action onicecandidateerror;

        /// <summary>
        /// The signaling state has changed. This state change is the result of either setLocalDescription or 
        /// setRemoteDescription being invoked.
        /// </summary>
        public event Action onsignalingstatechange;

        /// <summary>
        /// This Peer Connection's ICE connection state has changed.
        /// </summary>
        public event Action<RTCIceConnectionState> oniceconnectionstatechange;

        /// <summary>
        /// This Peer Connection's ICE gathering state has changed.
        /// </summary>
        public event Action<RTCIceGatheringState> onicegatheringstatechange;

        /// <summary>
        /// The state of the peer connection. A state of connected means the ICE checks have 
        /// succeeded and the DTLS handshake has completed. Once in the connected state it's
        /// suitable for media packets can be exchanged.
        /// </summary>
        public event Action<RTCPeerConnectionState> onconnectionstatechange;

        /// <summary>
        /// Constructor to create a new RTC peer connection instance.
        /// </summary>
        /// <param name="configuration">Optional.</param>
        public RTCPeerConnection(RTCConfiguration configuration) :
            base(true, true, true, configuration?.X_BindAddress)
        {
            _configuration = configuration;

            if (_configuration != null && _configuration.certificates.Count > 0)
            {
                _currentCertificate = _configuration.certificates.First();
            }

            //_offerAddresses = offerAddresses;
            //_turnServerEndPoint = turnServerEndPoint;

            SessionID = Guid.NewGuid().ToString();
            LocalSdpSessionID = Crypto.GetRandomInt(5).ToString();

            // Request the underlying RTP session to create the RTP channel.
            addSingleTrack();

            IceSession = new IceSession(GetRtpChannel(SDPMediaTypesEnum.audio), RTCIceComponent.rtp);
            IceSession.OnIceCandidate += (candidate) => onicecandidate?.Invoke(candidate);
            IceSession.OnIceConnectionStateChange += (state) =>
            {
                if (state == RTCIceConnectionState.connected && IceSession.NominatedCandidate != null)
                {
                    RemoteEndPoint = IceSession.NominatedCandidate.GetEndPoint();
                }

                iceConnectionState = state;
                oniceconnectionstatechange?.Invoke(iceConnectionState);

                if (base.IsSecureContextReady &&
                    iceConnectionState == RTCIceConnectionState.connected &&
                    connectionState != RTCPeerConnectionState.connected)
                {
                    // This is the case where the ICE connection checks completed after the DTLS handshake.
                    connectionState = RTCPeerConnectionState.connected;
                    onconnectionstatechange?.Invoke(RTCPeerConnectionState.connected);
                }
            };
            IceSession.OnIceGatheringStateChange += (state) => onicegatheringstatechange?.Invoke(state);
            IceSession.OnIceCandidateError += onicecandidateerror;

            OnRtpClosed += Close;
            OnRtcpBye += Close;

            onnegotiationneeded?.Invoke();
        }

        /// <summary>
        /// Sets the local SDP.
        /// </summary>
        /// <remarks>
        /// As specified in https://www.w3.org/TR/webrtc/#dom-peerconnection-setlocaldescription.
        /// </remarks>
        /// <param name="description">Optional. The session description to set as 
        /// local description. If not supplied then an offer or answer will be created as required. 
        /// </param>
        public Task setLocalDescription(RTCSessionDescriptionInit init)
        {
            RTCSessionDescription description = new RTCSessionDescription { type = init.type, sdp = SDP.ParseSDPDescription(init.sdp) };
            localDescription = description;

            if (init.type == RTCSdpType.offer)
            {
                IceSession.IsController = true;
            }

            var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);
            rtpChannel.OnRTPDataReceived += OnRTPDataReceived;

            // This is the point the ICE session potentially starts contacting STUN and TURN servers.
            IceSession.StartGathering();

            signalingState = RTCSignalingState.have_local_offer;
            onsignalingstatechange?.Invoke();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Updates the session after receiving the remote SDP.
        /// At this point check that the codecs match. We currently only support:
        ///  - Audio: PCMU,
        ///  - Video: VP8.
        /// If they are not available there's no point carrying on.
        /// </summary>
        /// <param name="sessionDescription">The answer/offer SDP from the remote party.</param>
        public Task setRemoteDescription(RTCSessionDescriptionInit init)
        {
            RTCSessionDescription description = new RTCSessionDescription { type = init.type, sdp = SDP.ParseSDPDescription(init.sdp) };
            remoteDescription = description;

            SDP remoteSdp = SDP.ParseSDPDescription(init.sdp);

            var setResult = base.SetRemoteDescription(remoteSdp);

            if (setResult != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException($"Error setting remote description {setResult}.");
            }
            else
            {
                string remoteIceUser = null;
                string remoteIcePassword = null;

                var audioAnnounce = remoteSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).FirstOrDefault();
                if (audioAnnounce != null)
                {
                    remoteIceUser = audioAnnounce.IceUfrag;
                    remoteIcePassword = audioAnnounce.IcePwd;
                }

                var videoAnnounce = remoteSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).FirstOrDefault();
                if (videoAnnounce != null)
                {
                    if (remoteIceUser == null && remoteIcePassword == null)
                    {
                        remoteIceUser = videoAnnounce.IceUfrag;
                        remoteIcePassword = videoAnnounce.IcePwd;
                    }
                }

                SdpSessionID = remoteSdp.SessionId;

                if (init.type == RTCSdpType.answer)
                {
                    IceSession.IsController = true;
                }

                if (remoteIceUser != null && remoteIcePassword != null)
                {
                    IceSession.SetRemoteCredentials(remoteIceUser, remoteIcePassword);
                }

                //// All browsers seem to have gone to trickling ICE candidates now but just
                //// in case one or more are given we can start the STUN dance immediately.
                //if (remoteSdp.IceCandidates != null)
                //{
                //    foreach (var iceCandidate in remoteSdp.IceCandidates)
                //    {
                //        AppendRemoteIceCandidate(iceCandidate);
                //    }
                //}

                //foreach (var media in remoteSdp.Media)
                //{
                //    if (media.IceCandidates != null)
                //    {
                //        foreach (var iceCandidate in media.IceCandidates)
                //        {
                //            AppendRemoteIceCandidate(iceCandidate);
                //        }
                //    }
                //}

                signalingState = RTCSignalingState.have_remote_offer;
                onsignalingstatechange?.Invoke();

                return Task.CompletedTask;
            }
        }

        public override void SetSecurityContext(
            ProtectRtpPacket protectRtp,
            ProtectRtpPacket unprotectRtp,
            ProtectRtpPacket protectRtcp,
            ProtectRtpPacket unprotectRtcp)
        {
            base.SetSecurityContext(protectRtp, unprotectRtp, protectRtcp, unprotectRtcp);

            if (iceConnectionState == RTCIceConnectionState.connected &&
                connectionState != RTCPeerConnectionState.connected)
            {
                // This is the case where the DTLS handshake completed before the ICE connection checks.
                connectionState = RTCPeerConnectionState.connected;
                onconnectionstatechange?.Invoke(RTCPeerConnectionState.connected);
            }
        }

        /// <summary>
        /// Send a media sample to the remote party.
        /// </summary>
        /// <param name="mediaType">Whether the sample is audio or video.</param>
        /// <param name="sampleTimestamp">The RTP timestamp for the sample.</param>
        /// <param name="sample">The sample payload.</param>
        public void SendMedia(SDPMediaTypesEnum mediaType, uint sampleTimestamp, byte[] sample)
        {
            if (RemoteEndPoint != null && IsDtlsNegotiationComplete && connectionState != RTCPeerConnectionState.closed)
            {
                if (mediaType == SDPMediaTypesEnum.video)
                {
                    int vp8PayloadID = Convert.ToInt32(VideoLocalTrack.Capabilities.Single(x => x.FormatCodec == SDPMediaFormatsEnum.VP8).FormatID);
                    SendVp8Frame(sampleTimestamp, vp8PayloadID, sample);
                }
                else if (mediaType == SDPMediaTypesEnum.audio)
                {
                    int pcmuPayloadID = Convert.ToInt32(AudioLocalTrack.Capabilities.Single(x => x.FormatCodec == SDPMediaFormatsEnum.PCMU).FormatID);
                    SendAudioFrame(sampleTimestamp, pcmuPayloadID, sample);
                }
            }
        }

        /// <summary>
        /// Close the session including the underlying RTP session and channels.
        /// </summary>
        /// <param name="reason">An optional descriptive reason for the closure.</param>
        public override void Close(string reason)
        {
            if (!IsClosed)
            {
                IceSession.Close(reason);
                base.Close(reason);

                connectionState = RTCPeerConnectionState.closed;
                onconnectionstatechange?.Invoke(RTCPeerConnectionState.closed);
            }
        }

        /// <summary>
        /// Closes the connection with the default reason.
        /// </summary>
        public void close()
        {
            Close(NORMAL_CLOSE_REASON);
        }

        /// <summary>
        /// Generates the SDP for an offer that can be made to a remote peer.
        /// </summary>
        /// <remarks>
        /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcpeerconnection-createoffer.
        /// </remarks>
        /// <param name="options">Optional. If supplied the options will be sued to apply additional
        /// controls over the generated offer SDP.</param>
        public async Task<RTCSessionDescriptionInit> createOffer(RTCOfferOptions options)
        {
            try
            {
                var audioCapabilities = AudioLocalTrack?.Capabilities;
                var videoCapabilities = VideoLocalTrack?.Capabilities;

                List<MediaStreamTrack> localTracks = GetLocalTracks();
                var offerSdp = await createBaseSdp(localTracks, audioCapabilities, videoCapabilities).ConfigureAwait(false);

                if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio))
                {
                    var audioAnnouncement = offerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single();
                    audioAnnouncement.AddExtra(ICE_SETUP_OFFER_ATTRIBUTE);
                }

                if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.video))
                {
                    var videoAnnouncement = offerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single();
                    videoAnnouncement.AddExtra(ICE_SETUP_OFFER_ATTRIBUTE);
                }

                RTCSessionDescriptionInit initDescription = new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = offerSdp.ToString()
                };

                return initDescription;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception createOffer. " + excp);
                throw;
            }
        }

        /// <summary>
        /// Creates an answer to an SDP offer from a remote peer.
        /// </summary>
        /// <remarks>
        /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcpeerconnection-createanswer and
        /// https://tools.ietf.org/html/rfc3264#section-6.1.
        /// </remarks>
        /// <param name="options">Optional. If supplied the options will be used to apply additional
        /// controls over the generated answer SDP.</param>
        public async Task<RTCSessionDescriptionInit> createAnswer(RTCAnswerOptions options)
        {
            if (remoteDescription == null)
            {
                throw new ApplicationException("The remote SDP must be set before an SDP answer can be created.");
            }
            else
            {
                var audioCapabilities = (AudioLocalTrack != null && AudioRemoteTrack != null) ?
                    SDPMediaFormat.GetCompatibleFormats(AudioLocalTrack.Capabilities, AudioRemoteTrack.Capabilities) : null;
                var videoCapabilities = (VideoLocalTrack != null && VideoRemoteTrack != null) ?
                    SDPMediaFormat.GetCompatibleFormats(VideoLocalTrack.Capabilities, VideoRemoteTrack.Capabilities) : null;

                List<MediaStreamTrack> localTracks = GetLocalTracks();
                var answerSdp = await createBaseSdp(localTracks, audioCapabilities, videoCapabilities).ConfigureAwait(false);

                if (answerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio))
                {
                    var audioAnnouncement = answerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single();
                    audioAnnouncement.AddExtra(ICE_SETUP_ANSWER_ATTRIBUTE);
                }

                if (answerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.video))
                {
                    var videoAnnouncement = answerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single();
                    videoAnnouncement.AddExtra(ICE_SETUP_ANSWER_ATTRIBUTE);
                }

                RTCSessionDescriptionInit initDescription = new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.answer,
                    sdp = answerSdp.ToString()
                };

                return initDescription;
            }
        }

        /// <summary>
        /// Generates the base SDP for an offer or answer. The SDP will then be tailored depending
        /// on whether it's being used in an offer or an answer.
        /// </summary>
        /// <param name="audioCapabilities">Optional. The audio formats to support in the SDP. This list can differ from
        /// the local audio track if an answer is being generated and only mutually supported formats are being
        /// used.</param>
        /// <param name="videoCapabilities">Optional. The video formats to support in the SDP. This list can differ from
        /// the local video track if an answer is being generated and only mutually supported formats are being
        /// used.</param>
        /// <remarks>
        /// From https://tools.ietf.org/html/draft-ietf-mmusic-ice-sip-sdp-39#section-4.2.5:
        ///   "The transport address from the peer for the default destination
        ///   is set to IPv4/IPv6 address values "0.0.0.0"/"::" and port value
        ///   of "9".  This MUST NOT be considered as a ICE failure by the peer
        ///   agent and the ICE processing MUST continue as usual."
        /// </remarks>
        private Task<SDP> createBaseSdp(List<MediaStreamTrack> tracks, List<SDPMediaFormat> audioCapabilities, List<SDPMediaFormat> videoCapabilities)
        {
            SDP offerSdp = new SDP(IPAddress.Loopback);
            offerSdp.SessionId = LocalSdpSessionID;

            bool iceCandidatesAdded = false;

            // Add a bundle attribute. Indicates that audio and video sessions will be multiplexed
            // on a single RTP socket.
            offerSdp.Group = BUNDLE_ATTRIBUTE;

            // Media announcements must be in the same order in the offer and answer.
            foreach (var track in tracks.OrderBy(x => x.MID))
            {
                offerSdp.Group += $" {track.MID}";

                SDPMediaAnnouncement announcement = new SDPMediaAnnouncement(
                 track.Kind,
                 SDP.IGNORE_RTP_PORT_NUMBER,
                 (track.Kind == SDPMediaTypesEnum.video) ? videoCapabilities : audioCapabilities);

                announcement.Transport = RTP_MEDIA_PROFILE;
                announcement.Connection = new SDPConnectionInformation(IPAddress.Any);
                announcement.AddExtra(RTCP_MUX_ATTRIBUTE);
                announcement.AddExtra(RTCP_ATTRIBUTE);
                announcement.MediaStreamStatus = track.StreamStatus;
                announcement.MediaID = track.MID;

                announcement.IceUfrag = IceSession.LocalIceUser;
                announcement.IcePwd = IceSession.LocalIcePassword;
                announcement.IceOptions = ICE_OPTIONS;
                announcement.DtlsFingerprint = _currentCertificate != null ? _currentCertificate.X_Fingerprint : null;

                if (iceCandidatesAdded == false)
                {
                    announcement.IceCandidates = new List<string>();

                    // Add ICE candidates.
                    foreach (var iceCandidate in IceSession.Candidates)
                    {
                        announcement.IceCandidates.Add(iceCandidate.ToString());
                    }

                    iceCandidatesAdded = true;
                }

                offerSdp.Media.Add(announcement);
            }

            return Task.FromResult(offerSdp);
        }

        /// <summary>
        /// From RFC5764:
        ///             +----------------+
        ///             | 127 < B< 192  -+--> forward to RTP
        ///             |                |
        /// packet -->  |  19 < B< 64   -+--> forward to DTLS
        ///             |                |
        ///             |       B< 2    -+--> forward to STUN
        ///             +----------------+
        /// </summary>
        /// <paramref name="localPort">The local port on the RTP socket that received the packet.</paramref>
        /// <param name="remoteEP">The remote end point the packet was received from.</param>
        /// <param name="buffer">The data received.</param>
        private void OnRTPDataReceived(int localPort, IPEndPoint remoteEP, byte[] buffer)
        {
            //logger.LogDebug($"RTP channel received a packet from {remoteEP}, {buffer?.Length} bytes.");

            if (buffer?.Length > 0)
            {
                try
                {
                    if (buffer[0] == 0x00 || buffer[0] == 0x01)
                    {
                        // STUN packet.
                        var stunMessage = STUNv2Message.ParseSTUNMessage(buffer, buffer.Length);
                        IceSession?.ProcessStunMessage(stunMessage, remoteEP);
                    }
                    else if (buffer[0] >= 128 && buffer[0] <= 191)
                    {
                        // RTP/RTCP packet.
                        // Do nothing. The RTPSession takes care of these.
                    }
                    else if (buffer[0] >= 20 && buffer[0] <= 63)
                    {
                        // DTLS packet.
                        // Do nothing. The DTLSContext already has the socket handle and is monitoring
                        // for DTLS packets.
                    }
                    else
                    {
                        logger.LogWarning("Unknown packet type received on RTP channel.");
                    }
                }
                catch (Exception excp)
                {
                    logger.LogError($"Exception WebRtcSession.OnRTPDataReceived {excp.Message}.");
                }
            }
        }

        /// <summary>
        /// Adds a remote ICE candidate to the list this peer is attempting to connect against.
        /// </summary>
        /// <param name="candidateInit">The remote candidate to add.</param>
        public Task addIceCandidate(RTCIceCandidateInit candidateInit)
        {
            RTCIceCandidate candidate = new RTCIceCandidate(candidateInit);

            if (IceSession.Component == candidate.component)
            {
                IceSession.AddRemoteCandidate(candidate);
            }
            else
            {
                logger.LogWarning($"Remote ICE candidate not added as no available ICE session for component {candidate.component}.");
            }

            return Task.CompletedTask;
        }

        public void restartIce()
        {
            throw new NotImplementedException();
        }

        public RTCConfiguration getConfiguration()
        {
            throw new NotImplementedException();
        }

        public void setConfiguration(RTCConfiguration configuration = null)
        {
            throw new NotImplementedException();
        }
    }
}
