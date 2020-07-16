//-----------------------------------------------------------------------------
// Filename: RTCPeerConnection.cs
//
// Description: Represents a WebRTC RTCPeerConnection.
//
// Specification Soup (as of 13 Jul 2020):
// - "Session Description Protocol (SDP) Offer/Answer procedures for
//   Interactive Connectivity Establishment(ICE)" [ed: specification for
//   including ICE candidates in SDP]:
//   https://tools.ietf.org/html/draft-ietf-mmusic-ice-sip-sdp-39
// - "Session Description Protocol (SDP) Offer/Answer Procedures For Stream
//   Control Transmission Protocol(SCTP) over Datagram Transport Layer
//   Security(DTLS) Transport." [ed: specification for negotiating
//   data channels in SDP] EXPIRED:
//   https://tools.ietf.org/html/draft-ietf-mmusic-sctp-sdp-26
// - "SDP-based Data Channel Negotiation" [ed: not currently implemented]:
//   https://tools.ietf.org/html/draft-ietf-mmusic-data-channel-sdpneg-28
//
// Author(s):
// Aaron Clauson
//
// History:
// 04 Mar 2016	Aaron Clauson	Created.
// 25 Aug 2019  Aaron Clauson   Updated from video only to audio and video.
// 18 Jan 2020  Aaron Clauson   Combined WebRTCPeer and WebRTCSession.
// 16 Mar 2020  Aaron Clauson   Refactoring to support RTCPeerConnection interface.
// 13 Jul 2020  Aaron Clauson   Added data channel support.
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
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The ICE set up roles that a peer can be in. The role determines how the DTLS
    /// handshake is performed, i.e. which peer is the client and which is the server.
    /// </summary>
    public enum IceRolesEnum
    {
        actpass = 0,
        passive = 1,
        active = 2
    }

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
        //private new const string RTP_MEDIA_PROFILE = "RTP/SAVP";
        private const string RTP_MEDIA_NON_FEEDBACK_PROFILE = "UDP/TLS/RTP/SAVP";
        private const string RTP_MEDIA_FEEDBACK_PROFILE = "UDP/TLS/RTP/SAVPF";
        private const string RTP_MEDIA_DATACHANNEL_DTLS_PROFILE = "DTLS/SCTP"; // Spec states "UDP/DTLS/SCTP" but it's clear the "UDP" is redundant.
        private const string RTP_MEDIA_DATACHANNEL_UDPDTLS_PROFILE = "UDP/DTLS/SCTP";
        private const string SDP_DATACHANNEL_FORMAT_ID = "webrtc-datachannel";
        private const string RTCP_MUX_ATTRIBUTE = "a=rtcp-mux";    // Indicates the media announcement is using multiplexed RTCP.
        private const string ICE_SETUP_ATTRIBUTE = "a=setup:";     // Indicates ICE agent can act as either the "controlling" or "controlled" peer.
        private const string BUNDLE_ATTRIBUTE = "BUNDLE";
        private const string ICE_OPTIONS = "ice2,trickle";          // Supported ICE options.
        private const string NORMAL_CLOSE_REASON = "normal";

        private new readonly string RTP_MEDIA_PROFILE = RTP_MEDIA_NON_FEEDBACK_PROFILE;
        private readonly string RTCP_ATTRIBUTE = $"a=rtcp:{SDP.IGNORE_RTP_PORT_NUMBER} IN IP4 0.0.0.0";

        private static ILogger logger = Log.Logger;

        public string SessionID { get; private set; }
        public string SdpSessionID { get; private set; }
        public string LocalSdpSessionID { get; private set; }

        private RtpIceChannel _rtpIceChannel;

        private List<RTCDataChannel> _dataChannels = new List<RTCDataChannel>();

        private DtlsSrtpTransport _dtlsHandle;
        private pe.pi.sctp4j.sctp.small.ThreadedAssociation _dataChannelAssociation;

        /// <summary>
        /// The ICE role the peer is acting in.
        /// </summary>
        public IceRolesEnum IceRole { get; set; } = IceRolesEnum.actpass;

        /// <summary>
        /// The DTLS fingerprint supplied by the remote peer in their SDP. Needs to be checked
        /// that the certificate supplied during the DTLS handshake matches.
        /// </summary>
        public RTCDtlsFingerprint RemotePeerDtlsFingerprint { get; private set; }

        public bool IsDtlsNegotiationComplete
        {
            get { return base.IsSecureContextReady; }
        }

        public RTCSessionDescription localDescription { get; private set; }

        public RTCSessionDescription remoteDescription { get; private set; }

        public RTCSessionDescription currentLocalDescription => localDescription;

        public RTCSessionDescription pendingLocalDescription => null;

        public RTCSessionDescription currentRemoteDescription => remoteDescription;

        public RTCSessionDescription pendingRemoteDescription => null;

        public RTCSignalingState signalingState { get; private set; } = RTCSignalingState.stable;

        public RTCIceGatheringState iceGatheringState { get; private set; } = RTCIceGatheringState.@new;

        public RTCIceConnectionState iceConnectionState { get; private set; } = RTCIceConnectionState.@new;

        public RTCPeerConnectionState connectionState { get; private set; } = RTCPeerConnectionState.@new;

        public bool canTrickleIceCandidates { get => true; }

        private RTCConfiguration _configuration;

        /// <summary>
        /// The certificate being used to negotiate the DTLS handshake with the 
        /// remote peer.
        /// </summary>
        private RTCCertificate _currentCertificate;
        public RTCCertificate CurrentCertificate
        {
            get
            {
                return _currentCertificate;
            }
        }

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
        public event Action<RTCIceCandidate, string> onicecandidateerror;

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
            if (_configuration != null &&
               _configuration.iceTransportPolicy == RTCIceTransportPolicy.relay &&
               _configuration.iceServers?.Count == 0)
            {
                throw new ApplicationException("RTCPeerConnection must have at least one ICE server specified for a relay only transport policy.");
            }

            if (configuration != null)
            {
                _configuration = configuration;
                if (_configuration.certificates?.Count > 0)
                {
                    // Find the first certificate that has a usable private key.
                    RTCCertificate usableCert = null;
                    foreach (var cert in _configuration.certificates)
                    {
                        // Attempting to check that a certificate has an exportable private key.
                        // TODO: Does not seem to be a particularly reliable way of checking private key exportability.
                        if (cert.Certificate.HasPrivateKey)
                        {
                            //if (cert.Certificate.PrivateKey is RSACryptoServiceProvider)
                            //{
                            //    var rsa = cert.Certificate.PrivateKey as RSACryptoServiceProvider;
                            //    if (!rsa.CspKeyContainerInfo.Exportable)
                            //    {
                            //        logger.LogWarning($"RTCPeerConnection was passed a certificate for {cert.Certificate.FriendlyName} with a non-exportable RSA private key.");
                            //    }
                            //    else
                            //    {
                            //        usableCert = cert;
                            //        break;
                            //    }
                            //}
                            //else
                            //{
                            usableCert = cert;
                            break;
                            //}
                        }
                    }

                    if (usableCert == null)
                    {
                        throw new ApplicationException("RTCPeerConnection was not able to find a certificate from the input configuration list with a usable private key.");
                    }
                    else
                    {
                        _currentCertificate = usableCert;
                    }
                }

                if (_configuration.X_UseRtpFeedbackProfile)
                {
                    RTP_MEDIA_PROFILE = RTP_MEDIA_FEEDBACK_PROFILE;
                }
            }
            else
            {
                _configuration = new RTCConfiguration();
            }

            // No certificate was provided so create a new self signed one.
            if (_configuration.certificates == null || _configuration.certificates.Count == 0)
            {
                _currentCertificate = new RTCCertificate { Certificate = DtlsUtils.CreateSelfSignedCert() };
                _configuration.certificates = new List<RTCCertificate> { _currentCertificate };
            }

            SessionID = Guid.NewGuid().ToString();
            LocalSdpSessionID = Crypto.GetRandomInt(5).ToString();

            // Request the underlying RTP session to create a single RTP channel that will
            // be used to multiplex all required media streams.
            addSingleTrack();

            _rtpIceChannel = GetRtpChannel();

            _rtpIceChannel.OnIceCandidate += (candidate) => onicecandidate?.Invoke(candidate);
            _rtpIceChannel.OnIceConnectionStateChange += (state) =>
            {
                if (state == RTCIceConnectionState.connected && _rtpIceChannel.NominatedEntry != null)
                {
                    var connectedEP = _rtpIceChannel.NominatedEntry.RemoteCandidate.DestinationEndPoint;
                    base.SetDestination(SDPMediaTypesEnum.audio, connectedEP, connectedEP);

                    logger.LogInformation($"ICE connected to remote end point {AudioDestinationEndPoint}.");

                    _dtlsHandle = new DtlsSrtpTransport(
                                IceRole == IceRolesEnum.active ?
                                (IDtlsSrtpPeer)new DtlsSrtpClient(_currentCertificate.Certificate) :
                                (IDtlsSrtpPeer)new DtlsSrtpServer(_currentCertificate.Certificate));

                    logger.LogDebug($"Starting DLS handshake with role {IceRole}.");
                    Task.Run<bool>(() => DoDtlsHandshake(_dtlsHandle))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            logger.LogWarning($"RTCPeerConnection DTLS handshake task completed in a faulted state. {t.Exception?.Flatten().Message}");

                            connectionState = RTCPeerConnectionState.failed;
                            onconnectionstatechange?.Invoke(connectionState);
                        }
                        else
                        {
                            connectionState = (t.Result) ? RTCPeerConnectionState.connected : connectionState = RTCPeerConnectionState.failed;
                            onconnectionstatechange?.Invoke(connectionState);
                        }
                    });
                }

                iceConnectionState = state;
                oniceconnectionstatechange?.Invoke(iceConnectionState);
            };
            _rtpIceChannel.OnIceGatheringStateChange += (state) => onicegatheringstatechange?.Invoke(state);
            _rtpIceChannel.OnIceCandidateError += (candidate, error) => onicecandidateerror?.Invoke(candidate, error);

            OnRtpClosed += Close;
            OnRtcpBye += Close;

            onnegotiationneeded?.Invoke();

            _rtpIceChannel.StartGathering();
        }

        /// <summary>
        /// Creates a new RTP ICE channel (which manages the UDP socket sending and receiving RTP
        /// packets) for use with this session.
        /// </summary>
        /// <param name="mediaType">The type of media the RTP channel is for. Must be audio or video.</param>
        /// <returns>A new RTPChannel instance.</returns>
        protected override RTPChannel CreateRtpChannel(SDPMediaTypesEnum mediaType)
        {
            var rtpIceChannel = new RtpIceChannel(
                _configuration?.X_BindAddress,
                RTCIceComponent.rtp,
                _configuration?.iceServers,
                _configuration != null ? _configuration.iceTransportPolicy : RTCIceTransportPolicy.all);

            m_rtpChannels.Add(mediaType, rtpIceChannel);

            rtpIceChannel.OnRTPDataReceived += OnRTPDataReceived;

            // Start the RTP, and if required the Control, socket receivers and the RTCP session.
            rtpIceChannel.Start();

            return rtpIceChannel;
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
                _rtpIceChannel.IsController = true;
            }

            // This is the point the ICE session potentially starts contacting STUN and TURN servers.
            //IceSession.StartGathering();

            signalingState = RTCSignalingState.have_local_offer;
            onsignalingstatechange?.Invoke();

            return Task.CompletedTask;
        }

        /// <summary>
        /// This set remote description overload is a convenience method for SIP/VoIP callers
        /// instead of WebRTC callers. The method signature better matches what the SIP
        /// user agent is expecting.
        /// TODO: Using two very similar overloads could cause confusion. Possibly
        /// consolidate.
        /// </summary>
        /// <param name="sdpType">Whether the remote SDP is an offer or answer.</param>
        /// <param name="sessionDescription">The SDP from the remote party.</param>
        /// <returns>The result of attempting to set the remote description.</returns>
        public override SetDescriptionResultEnum SetRemoteDescription(SdpType sdpType, SDP sessionDescription)
        {
            RTCSessionDescriptionInit init = new RTCSessionDescriptionInit
            {
                sdp = sessionDescription.ToString(),
                type = (sdpType == SdpType.answer) ? RTCSdpType.answer : RTCSdpType.offer
            };

            return setRemoteDescription(init);
        }

        /// <summary>
        /// Updates the session after receiving the remote SDP.
        /// At this point check that the codecs match. We currently only support:
        ///  - Audio: PCMU,
        ///  - Video: VP8.
        /// If they are not available there's no point carrying on.
        /// </summary>
        /// <param name="sessionDescription">The answer/offer SDP from the remote party.</param>
        public SetDescriptionResultEnum setRemoteDescription(RTCSessionDescriptionInit init)
        {
            RTCSessionDescription description = new RTCSessionDescription { type = init.type, sdp = SDP.ParseSDPDescription(init.sdp) };
            remoteDescription = description;

            SDP remoteSdp = SDP.ParseSDPDescription(init.sdp);

            SdpType sdpType = (init.type == RTCSdpType.offer) ? SdpType.offer : SdpType.answer;
            var setResult = base.SetRemoteDescription(sdpType, remoteSdp);

            if (setResult == SetDescriptionResultEnum.OK)
            {
                string remoteIceUser = remoteSdp.IceUfrag;
                string remoteIcePassword = remoteSdp.IcePwd;
                string dtlsFingerprint = remoteSdp.DtlsFingerprint;

                int mLineIndex = 0;
                foreach (var ann in remoteSdp.Media)
                {
                    if (remoteIceUser == null || remoteIcePassword == null || dtlsFingerprint == null)
                    {
                        remoteIceUser = remoteIceUser ?? ann.IceUfrag;
                        remoteIcePassword = remoteIcePassword ?? ann.IcePwd;
                        dtlsFingerprint = dtlsFingerprint ?? ann.DtlsFingerprint;
                    }

                    // Check for data channel announcements.
                    if (ann.Media == SDPMediaTypesEnum.application &&
                    ann.MediaFormats.Count() == 1 &&
                    ann.MediaFormats.Single().FormatID == SDP_DATACHANNEL_FORMAT_ID)
                    {
                        if (ann.Transport == RTP_MEDIA_DATACHANNEL_DTLS_PROFILE ||
                            ann.Transport == RTP_MEDIA_DATACHANNEL_UDPDTLS_PROFILE)
                        {
                            dtlsFingerprint = dtlsFingerprint ?? ann.DtlsFingerprint;
                            RTCDataChannel dataChannel = new RTCDataChannel
                            {
                                id = ann.SctpPort,
                                MaxMessageSize = ann.MaxMessageSize,
                                MLineIndex = mLineIndex,
                                MediaID = ann.MediaID
                            };
                            _dataChannels.Add(dataChannel);
                        }
                        else
                        {
                            logger.LogWarning($"The remote SDP requested an unsupported data channel transport of {ann.Transport}.");
                            return SetDescriptionResultEnum.DataChannelTransportNotSupported;
                        }
                    }
                    mLineIndex++;
                }

                SdpSessionID = remoteSdp.SessionId;

                if (init.type == RTCSdpType.answer)
                {
                    _rtpIceChannel.IsController = true;
                    // Set DTLS role to be server.
                    IceRole = IceRolesEnum.passive;
                }
                else
                {
                    // Set DTLS role as client.
                    IceRole = IceRolesEnum.active;
                }

                if (remoteIceUser != null && remoteIcePassword != null)
                {
                    _rtpIceChannel.SetRemoteCredentials(remoteIceUser, remoteIcePassword);
                }

                if (!string.IsNullOrWhiteSpace(dtlsFingerprint))
                {
                    dtlsFingerprint = dtlsFingerprint.Trim().ToLower();
                    if (RTCDtlsFingerprint.TryParse(dtlsFingerprint, out var remoteFingerprint))
                    {
                        RemotePeerDtlsFingerprint = remoteFingerprint;
                    }
                    else
                    {
                        logger.LogWarning($"The DTLS fingerprint was invalid or not supported.");
                        return SetDescriptionResultEnum.DtlsFingerprintDigestNotSupported;
                    }
                }
                else
                {
                    logger.LogWarning("The DTLS fingerprint was missing from the remote party's session description.");
                    return SetDescriptionResultEnum.DtlsFingerprintMissing;
                }

                // All browsers seem to have gone to trickling ICE candidates now but just
                // in case one or more are given we can start the STUN dance immediately.
                if (remoteSdp.IceCandidates != null)
                {
                    foreach (var iceCandidate in remoteSdp.IceCandidates)
                    {
                        addIceCandidate(new RTCIceCandidateInit { candidate = iceCandidate });
                    }
                }

                foreach (var media in remoteSdp.Media)
                {
                    if (media.IceCandidates != null)
                    {
                        foreach (var iceCandidate in media.IceCandidates)
                        {
                            addIceCandidate(new RTCIceCandidateInit { candidate = iceCandidate });
                        }
                    }
                }

                signalingState = RTCSignalingState.have_remote_offer;
                onsignalingstatechange?.Invoke();
            }

            return setResult;
        }

        /// <summary>
        /// Send a media sample to the remote party.
        /// </summary>
        /// <param name="mediaType">Whether the sample is audio or video.</param>
        /// <param name="sampleTimestamp">The RTP timestamp for the sample.</param>
        /// <param name="sample">The sample payload.</param>
        public void SendMedia(SDPMediaTypesEnum mediaType, uint sampleTimestamp, byte[] sample)
        {
            if (base.AudioDestinationEndPoint != null && IsDtlsNegotiationComplete && connectionState != RTCPeerConnectionState.closed)
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
                logger.LogDebug($"Peer connection closed with reason {(reason != null ? reason : "<none>")}.");

                _rtpIceChannel.Close();
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
        public RTCSessionDescriptionInit createOffer(RTCOfferOptions options)
        {
            try
            {
                var audioCapabilities = AudioLocalTrack?.Capabilities;
                var videoCapabilities = VideoLocalTrack?.Capabilities;

                List<MediaStreamTrack> localTracks = GetLocalTracks();
                var offerSdp = createBaseSdp(localTracks, audioCapabilities, videoCapabilities);

                foreach (var ann in offerSdp.Media)
                {
                    ann.AddExtra($"{ICE_SETUP_ATTRIBUTE}{IceRole}");
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
        /// Convenience overload to suit SIP/VoIP callers.
        /// TODO: Consolidate with createAnswer.
        /// </summary>
        /// <param name="connectionAddress">Not used.</param>
        /// <returns>An SDP payload to answer an offer from the remote party.</returns>
        public override SDP CreateAnswer(IPAddress connectionAddress)
        {
            var result = createAnswer(null);

            if (result?.sdp != null)
            {
                return SDP.ParseSDPDescription(result.sdp);
            }

            return null;
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
        public RTCSessionDescriptionInit createAnswer(RTCAnswerOptions options)
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
                var answerSdp = createBaseSdp(localTracks, audioCapabilities, videoCapabilities);

                if (answerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio))
                {
                    var audioAnnouncement = answerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single();
                    audioAnnouncement.AddExtra($"{ICE_SETUP_ATTRIBUTE}{IceRole}");
                }

                if (answerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.video))
                {
                    var videoAnnouncement = answerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single();
                    videoAnnouncement.AddExtra($"{ICE_SETUP_ATTRIBUTE}{IceRole}");
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
        /// For standard use this method should not need to be called. The remote peer's ICE
        /// user and password will be set when from the SDP. This method is provided for 
        /// diagnostics purposes.
        /// </summary>
        /// <param name="remoteIceUser">The remote peer's ICE user value.</param>
        /// <param name="remoteIcePassword">The remote peer's ICE password value.</param>
        public void SetRemoteCredentials(string remoteIceUser, string remoteIcePassword)
        {
            _rtpIceChannel.SetRemoteCredentials(remoteIceUser, remoteIcePassword);
        }

        /// <summary>
        /// Gets the RTP channel being used to send and receive data on this peer connection.
        /// Unlike the base RTP session peer connections only ever use a single RTP channel.
        /// Audio and video (and RTCP) are all multiplexed on the same channel.
        /// </summary>
        public RtpIceChannel GetRtpChannel()
        {
            return m_rtpChannels.FirstOrDefault().Value as RtpIceChannel;
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
        private SDP createBaseSdp(List<MediaStreamTrack> tracks, List<SDPMediaFormat> audioCapabilities, List<SDPMediaFormat> videoCapabilities)
        {
            SDP offerSdp = new SDP(IPAddress.Loopback);
            offerSdp.SessionId = LocalSdpSessionID;

            bool iceCandidatesAdded = false;

            // Add a bundle attribute as long as there's something to bundle. Indicates that audio
            // and video sessions as well as any data channels will be multiplexed on a single RTP socket.
            if (tracks.Count > 0 || _dataChannels?.Count > 0)
            {
                offerSdp.Group = BUNDLE_ATTRIBUTE;
            }

            offerSdp.DtlsFingerprint = _currentCertificate.getFingerprints().First().ToString();

            // Local function to add ICE candidates to one of the media announcements.
            void AddIceCandidates(SDPMediaAnnouncement announcement)
            {
                if (_rtpIceChannel.Candidates?.Count > 0)
                {
                    announcement.IceCandidates = new List<string>();

                    // Add ICE candidates.
                    foreach (var iceCandidate in _rtpIceChannel.Candidates)
                    {
                        announcement.IceCandidates.Add(iceCandidate.ToString());
                    }

                    if (_rtpIceChannel.IceGatheringState == RTCIceGatheringState.complete)
                    {
                        announcement.AddExtra($"a={SDP.END_ICE_CANDIDATES_ATTRIBUTE}");
                    }
                }
            };

            // Media announcements must be in the same order in the offer and answer.
            foreach (var track in tracks)
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
                announcement.MLineIndex = track.MLineIndex;

                announcement.IceUfrag = _rtpIceChannel.LocalIceUser;
                announcement.IcePwd = _rtpIceChannel.LocalIcePassword;
                announcement.IceOptions = ICE_OPTIONS;
                announcement.DtlsFingerprint = offerSdp.DtlsFingerprint;

                if (iceCandidatesAdded == false)
                {
                    AddIceCandidates(announcement);
                    iceCandidatesAdded = true;
                }

                offerSdp.Media.Add(announcement);
            }

            if (_dataChannels?.Count > 0)
            {
                foreach (var dataChannel in _dataChannels)
                {
                    SDPMediaAnnouncement dataChannelAnnouncement = new SDPMediaAnnouncement(
                        SDPMediaTypesEnum.application,
                        SDP.IGNORE_RTP_PORT_NUMBER,
                        new List<SDPMediaFormat> { new SDPMediaFormat(SDP_DATACHANNEL_FORMAT_ID) });
                    dataChannelAnnouncement.Transport = RTP_MEDIA_DATACHANNEL_UDPDTLS_PROFILE;
                    dataChannelAnnouncement.Connection = new SDPConnectionInformation(IPAddress.Any);

                    dataChannelAnnouncement.SctpPort = dataChannel.id;
                    dataChannelAnnouncement.MaxMessageSize = dataChannel.MaxMessageSize;
                    dataChannelAnnouncement.MLineIndex = dataChannel.MLineIndex;
                    dataChannelAnnouncement.MediaID = dataChannel.MediaID;
                    dataChannelAnnouncement.IceUfrag = _rtpIceChannel.LocalIceUser;
                    dataChannelAnnouncement.IcePwd = _rtpIceChannel.LocalIcePassword;
                    dataChannelAnnouncement.IceOptions = ICE_OPTIONS;
                    dataChannelAnnouncement.DtlsFingerprint = offerSdp.DtlsFingerprint;

                    if (iceCandidatesAdded == false)
                    {
                        AddIceCandidates(dataChannelAnnouncement);
                        iceCandidatesAdded = true;
                    }

                    offerSdp.Media.Add(dataChannelAnnouncement);

                    offerSdp.Group += $" {dataChannel.MLineIndex}";
                }
            }

            return offerSdp;
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

            // By this pint the RTP ICE channel has already processed any STUN packets which means 
            // it's only necessary to separate RTP/RTCP from DTLS.
            // Because DTLS packets can be fragmented and RTP/RTCP should never be use the RTP/RTCP 
            // prefix to distinguish.

            if (buffer?.Length > 0)
            {
                try
                {
                    if (buffer?.Length > RTPHeader.MIN_HEADER_LEN && buffer[0] >= 128 && buffer[0] <= 191)
                    {
                        // RTP/RTCP packet.
                        base.OnReceive(localPort, remoteEP, buffer);
                    }
                    else
                    //if (buffer[0] >= 20 && buffer[0] <= 63)
                    {
                        // DTLS packet.
                        //OnDtlsPacket?.Invoke(buffer);

                        if(_dtlsHandle != null)
                        {
                            logger.LogDebug($"DTLS transport received {buffer.Length} bytes from {AudioDestinationEndPoint}.");
                            _dtlsHandle.WriteToRecvStream(buffer);

                            //if(_dtlsHandle.IsHandshakeComplete() && _dtlsHandle.Transport != null)
                            //{
                            //    byte[] dtlsBuf = new byte[4096];
                            //    int bytesDecrypted = _dtlsHandle.Transport.Receive(dtlsBuf, 0, 4096, 0);

                            //    logger.LogDebug($"DTLS transport decrypted {bytesDecrypted} bytes from {AudioDestinationEndPoint}.");
                            //    logger.LogDebug(dtlsBuf.Take(bytesDecrypted).ToArray().HexStr());

                            //    //var sctpPacket = SCTP.SCTPPacket.FromArray(dtlsBuf, 0, bytesDecrypted);
                            //    //if(sctpPacket != null)
                            //    //{
                            //    //    logger.LogDebug($"SCTP packet {sctpPacket.Header.DestinationPort}->{sctpPacket.Header.DestinationPort}.");
                            //    //}

                            //    //var sctpPacket = new pe.pi.sctp4j.sctp.messages.Packet(new SCTP4CS.Utils.ByteBuffer(dtlsBuf, 0, bytesDecrypted));
                            //    //if (sctpPacket != null)
                            //    //{
                            //    //    logger.LogDebug($"SCTP packet {sctpPacket.getSrcPort()}->{sctpPacket.getDestPort()}.");
                            //    //}
                            //    //var assoc = new pe.pi.sctp4j.sctp.small.ThreadedAssociation(_dtlsHandle, null);
                            //}
                        }
                       else
                        {
                            logger.LogWarning($"DTLS packet received {buffer.Length} bytes from {AudioDestinationEndPoint} but no DTLS transport available.");
                        }
                    }
                }
                catch (Exception excp)
                {
                    logger.LogError($"Exception RTCPeerConnection.OnRTPDataReceived {excp.Message}");
                }
            }
        }

        /// <summary>
        /// Adds a remote ICE candidate to the list this peer is attempting to connect against.
        /// </summary>
        /// <param name="candidateInit">The remote candidate to add.</param>
        public void addIceCandidate(RTCIceCandidateInit candidateInit)
        {
            RTCIceCandidate candidate = new RTCIceCandidate(candidateInit);

            if (_rtpIceChannel.Component == candidate.component)
            {
                _rtpIceChannel.AddRemoteCandidate(candidate);
            }
            else
            {
                logger.LogWarning($"Remote ICE candidate not added as no available ICE session for component {candidate.component}.");
            }
        }

        /// <summary>
        /// Restarts the ICE session gathering and connection checks.
        /// </summary>
        public void restartIce()
        {
            _rtpIceChannel.Restart();
        }

        public RTCConfiguration getConfiguration()
        {
            return _configuration;
        }

        public void setConfiguration(RTCConfiguration configuration = null)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds a new data channel to the peer connection.
        /// </summary>
        /// <param name="label">THe label used to identify the data channel.</param>
        /// <returns>The data channel created.</returns>
        public RTCDataChannel createDataChannel(string label)
        {
            RTCDataChannel channel = new RTCDataChannel
            {
                label = label,
                id = 4000,
                MaxMessageSize = 262144
            };

            channel.MLineIndex = base.m_mLineIndex++;
            channel.MediaID = channel.MLineIndex.ToString();

            _dataChannels.Add(channel);

            return channel;
        }

        /// <summary>
        ///  DtlsHandshake requires DtlsSrtpTransport to work.
        ///  DtlsSrtpTransport is similar to C++ DTLS class combined with Srtp class and can perform 
        ///  Handshake as Server or Client in same call. The constructor of transport require a DtlsStrpClient 
        ///  or DtlsSrtpServer to work.
        /// </summary>
        /// <param name="dtlsHandle">The DTLS transport handle to perform the handshake with.</param>
        /// <returns></returns>
        private bool DoDtlsHandshake(DtlsSrtpTransport dtlsHandle)
        {
            logger.LogDebug("RTCPeerConnection DoDtlsHandshake started.");

            var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

            dtlsHandle.OnDataReady += (buf) =>
            {
                //logger.LogDebug($"DTLS transport sending {buf.Length} bytes to {AudioDestinationEndPoint}.");
                rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, AudioDestinationEndPoint, buf);
            };

            var handshakeResult = dtlsHandle.DoHandshake();

            if (!handshakeResult)
            {
                logger.LogWarning($"RTCPeerConnection DTLS handshake failed.");
                return false;
            }
            else
            {
                logger.LogDebug($"RTCPeerConnection DTLS handshake result {handshakeResult}, is handshake complete {dtlsHandle.IsHandshakeComplete()}.");

                var expectedFp = RemotePeerDtlsFingerprint;
                var remoteFingerprint = DtlsUtils.Fingerprint(expectedFp.algorithm, dtlsHandle.GetRemoteCertificate().GetCertificateAt(0));

                if (remoteFingerprint.value != expectedFp.value)
                {
                    logger.LogWarning($"RTCPeerConnection remote certificate fingerprint mismatch, expected {expectedFp}, actual {remoteFingerprint}.");
                    return false;
                }
                else
                {
                    logger.LogDebug($"RTCPeerConnection remote certificate fingerprint matched expected value of {remoteFingerprint.value} for {remoteFingerprint.algorithm}.");

                    base.SetSecurityContext(
                        dtlsHandle.ProtectRTP,
                        dtlsHandle.UnprotectRTP,
                        dtlsHandle.ProtectRTCP,
                        dtlsHandle.UnprotectRTCP);

                    DataChannelAssociationListener associationListener = new DataChannelAssociationListener();
                    _dataChannelAssociation = new pe.pi.sctp4j.sctp.small.ThreadedAssociation(_dtlsHandle.Transport, associationListener);
                    _dataChannelAssociation.associate();

                    return true;
                }
            }
        }
    }
}
