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
//   data channels in SDP, this defines the SDP "sctp-port" attribute] 
//   The document is also EXPIRED:
//   https://tools.ietf.org/html/draft-ietf-mmusic-sctp-sdp-26
// - "SDP-based Data Channel Negotiation" [ed: not currently implemented,
//   actually seems like a big pain to implement this given it can already
//   be done in-band on the SCTP connection]:
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

        public string toJSON()
        {
            //return "{" +
            //    $"  \"type\": \"{type}\"," +
            //    $"  \"sdp\": \"{sdp.Replace(SDP.CRLF, @"\\n").Replace("\"", "\\\"")}\"" +
            //    "}";

            return TinyJson.JSONWriter.ToJson(this);
        }

        public static bool TryParse(string json, out RTCSessionDescriptionInit init)
        {
            init = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }
            else
            {
                init = TinyJson.JSONParser.FromJson<RTCSessionDescriptionInit>(json);

                // To qualify as parsed all required fields must be set.
                return init != null &&
                    init.sdp != null;
            }
        }
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
        private const string RTP_MEDIA_DATACHANNEL_DTLS_PROFILE = "DTLS/SCTP"; // Legacy.
        private const string RTP_MEDIA_DATACHANNEL_UDPDTLS_PROFILE = "UDP/DTLS/SCTP";
        private const string SDP_DATACHANNEL_FORMAT_ID = "webrtc-datachannel";
        private const string RTCP_MUX_ATTRIBUTE = "a=rtcp-mux";    // Indicates the media announcement is using multiplexed RTCP.
        private const string ICE_SETUP_ATTRIBUTE = "a=setup:";     // Indicates ICE agent can act as either the "controlling" or "controlled" peer.
        private const string BUNDLE_ATTRIBUTE = "BUNDLE";
        private const string ICE_OPTIONS = "ice2,trickle";          // Supported ICE options.
        private const string NORMAL_CLOSE_REASON = "normal";
        private const int SCTP_DEFAULT_PORT = 5000;
        private const long SCTP_DEFAULT_MAX_MESSAGE_SIZE = 262144;
        private const string UNKNOWN_DATACHANNEL_ERROR = "unknown";

        private new readonly string RTP_MEDIA_PROFILE = RTP_MEDIA_NON_FEEDBACK_PROFILE;
        private readonly string RTCP_ATTRIBUTE = $"a=rtcp:{SDP.IGNORE_RTP_PORT_NUMBER} IN IP4 0.0.0.0";

        private static ILogger logger = Log.Logger;

        public string SessionID { get; private set; }
        public string SdpSessionID { get; private set; }
        public string LocalSdpSessionID { get; private set; }

        private RtpIceChannel _rtpIceChannel;

        public List<RTCDataChannel> DataChannels { get; private set; } = new List<RTCDataChannel>();

        private DtlsSrtpTransport _dtlsHandle;
        public RTCPeerSctpAssociation _peerSctpAssociation;

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
        //private RTCCertificate _currentCertificate;
        //public RTCCertificate CurrentCertificate
        //{
        //    get
        //    {
        //        return _currentCertificate;
        //    }
        //}

        /// <summary>
        /// The fingerprint of the certificate being used to negotiate the DTLS handshake with the 
        /// remote peer.
        /// </summary>
        public RTCDtlsFingerprint DtlsCertificateFingerprint { get; private set; }

        /// <summary>
        /// Informs the application that session negotiation needs to be done (i.e. a createOffer call 
        /// followed by setLocalDescription).
        /// </summary>
        public event Action onnegotiationneeded;

        private event Action<RTCIceCandidate> _onIceCandidate;
        /// <summary>
        /// A new ICE candidate is available for the Peer Connection.
        /// </summary>
        public event Action<RTCIceCandidate> onicecandidate
        {
            add
            {
                var notifyIce = _onIceCandidate == null && value != null;
                _onIceCandidate += value;
                if (notifyIce)
                {
                    foreach (var ice in _rtpIceChannel.Candidates)
                    {
                        _onIceCandidate?.Invoke(ice);
                    }
                }
            }
            remove
            {
                _onIceCandidate -= value;
            }
        }

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
        /// Fires when a new data channel is created by the remote peer.
        /// </summary>
        public event Action<RTCDataChannel> ondatachannel;

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

            Org.BouncyCastle.Crypto.Tls.Certificate dtlsCertificate = null;
            Org.BouncyCastle.Crypto.AsymmetricKeyParameter dtlsPrivateKey = null;

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
                        dtlsCertificate = DtlsUtils.LoadCertificateChain(usableCert.Certificate);
                        dtlsPrivateKey = DtlsUtils.LoadPrivateKeyResource(usableCert.Certificate);
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


            if (dtlsCertificate == null)
            {
                // No certificate was provided so create a new self signed one.
                (dtlsCertificate, dtlsPrivateKey) = DtlsUtils.CreateSelfSignedTlsCert();
            }

            DtlsCertificateFingerprint = DtlsUtils.Fingerprint(dtlsCertificate);

            SessionID = Guid.NewGuid().ToString();
            LocalSdpSessionID = Crypto.GetRandomInt(5).ToString();

            // Request the underlying RTP session to create a single RTP channel that will
            // be used to multiplex all required media streams.
            addSingleTrack();

            _rtpIceChannel = GetRtpChannel();

            _rtpIceChannel.OnIceCandidate += (candidate) => _onIceCandidate?.Invoke(candidate);
            _rtpIceChannel.OnIceConnectionStateChange += async (state) =>
            {
                if (iceConnectionState == RTCIceConnectionState.connected &&
                    state == RTCIceConnectionState.connected)
                {
                    // Already connected. This event is due to change in the nominated remote candidate.
                    var connectedEP = _rtpIceChannel.NominatedEntry.RemoteCandidate.DestinationEndPoint;
                    base.SetDestination(SDPMediaTypesEnum.audio, connectedEP, connectedEP);

                    logger.LogInformation($"ICE changing connected remote end point to {AudioDestinationEndPoint}.");
                }
                else
                {
                    if (state == RTCIceConnectionState.connected && _rtpIceChannel.NominatedEntry != null)
                    {
                        if (_dtlsHandle != null)
                        {
                            // The ICE connection state change is due to a re-connection.
                            iceConnectionState = state;
                            oniceconnectionstatechange?.Invoke(iceConnectionState);

                            connectionState = RTCPeerConnectionState.connected;
                            onconnectionstatechange?.Invoke(connectionState);
                        }
                        else
                        {
                            var connectedEP = _rtpIceChannel.NominatedEntry.RemoteCandidate.DestinationEndPoint;
                            base.SetDestination(SDPMediaTypesEnum.audio, connectedEP, connectedEP);

                            logger.LogInformation($"ICE connected to remote end point {AudioDestinationEndPoint}.");

                            _dtlsHandle = new DtlsSrtpTransport(
                                        IceRole == IceRolesEnum.active ?
                                        (IDtlsSrtpPeer)new DtlsSrtpClient(dtlsCertificate, dtlsPrivateKey) :
                                        (IDtlsSrtpPeer)new DtlsSrtpServer(dtlsCertificate, dtlsPrivateKey));

                            _dtlsHandle.OnAlert += OnDtlsAlert;

                            logger.LogDebug($"Starting DLS handshake with role {IceRole}.");

                            try
                            {
                                _ = Task.Run(async () =>
                                {
                                    bool handshakeResult = DoDtlsHandshake(_dtlsHandle);

                                    connectionState = (handshakeResult) ? RTCPeerConnectionState.connected : connectionState = RTCPeerConnectionState.failed;
                                    onconnectionstatechange?.Invoke(connectionState);

                                    if (connectionState == RTCPeerConnectionState.connected)
                                    {
                                        await base.Start().ConfigureAwait(false);

                                        if (RemoteDescription.Media.Any(x => x.Media == SDPMediaTypesEnum.application))
                                        {
                                            InitialiseSctpAssociation();
                                        }
                                    }
                                });
                            }
                            catch (Exception excp)
                            {
                                logger.LogWarning($"RTCPeerConnection DTLS handshake failed. {excp.Message}");

                                connectionState = RTCPeerConnectionState.failed;
                                onconnectionstatechange?.Invoke(connectionState);
                            }
                        }
                    }

                    iceConnectionState = state;
                    oniceconnectionstatechange?.Invoke(iceConnectionState);

                    if (iceConnectionState == RTCIceConnectionState.checking)
                    {
                        connectionState = RTCPeerConnectionState.connecting;
                        onconnectionstatechange?.Invoke(connectionState);
                    }
                    else if (iceConnectionState == RTCIceConnectionState.disconnected)
                    {
                        if (connectionState == RTCPeerConnectionState.connected)
                        {
                            connectionState = RTCPeerConnectionState.disconnected;
                            onconnectionstatechange?.Invoke(connectionState);
                        }
                        else
                        {
                            connectionState = RTCPeerConnectionState.failed;
                            onconnectionstatechange?.Invoke(connectionState);
                        }
                    }
                    else if (iceConnectionState == RTCIceConnectionState.failed)
                    {
                        connectionState = RTCPeerConnectionState.failed;
                        onconnectionstatechange?.Invoke(connectionState);
                    }
                }
            };
            _rtpIceChannel.OnIceGatheringStateChange += (state) => onicegatheringstatechange?.Invoke(state);
            _rtpIceChannel.OnIceCandidateError += (candidate, error) => onicecandidateerror?.Invoke(candidate, error);

            OnRtpClosed += Close;
            OnRtcpBye += Close;

            onnegotiationneeded?.Invoke();

            _rtpIceChannel.StartGathering();
        }

        /// <summary>
        /// Initialises the SCTP association and will attempt to create any pending data channel requests.
        /// </summary>
        private void InitialiseSctpAssociation()
        {
            // If a data channel was requested by the application then create the SCTP association.
            var sctpAnn = RemoteDescription.Media.Where(x => x.Media == SDPMediaTypesEnum.application).FirstOrDefault();
            int destinationPort = sctpAnn?.SctpPort != null ? (int)sctpAnn.SctpPort : SCTP_DEFAULT_PORT;

            _peerSctpAssociation = new RTCPeerSctpAssociation(_dtlsHandle.Transport, _dtlsHandle.IsClient, SCTP_DEFAULT_PORT, destinationPort);
            _peerSctpAssociation.OnAssociated += () =>
            {
                logger.LogDebug("SCTP association successfully initialised.");

                // Create new SCTP streams for any outstanding data channel requests.
                foreach (var dataChannel in DataChannels)
                {
                    CreateSctpStreamForDataChannel(dataChannel);
                }
            };
            _peerSctpAssociation.OnSCTPStreamOpen += (stm, isLocal) =>
            {
                logger.LogDebug($"SCTP stream opened for label {stm.getLabel()} and stream ID {stm.getNum()} (is local stream ID {isLocal}).");

                if (!isLocal)
                {
                    // A new data channel that was opened by the remote peer.
                    RTCDataChannel dataChannel = new RTCDataChannel
                    {
                        label = stm.getLabel(),
                        id = (ushort)stm.getNum()
                    };
                    dataChannel.SetStream(stm);
                    DataChannels.Add(dataChannel);
                    ondatachannel?.Invoke(dataChannel);
                }
            };

            try
            { 
                _peerSctpAssociation.Associate();
            }
            catch(Exception excp)
            {
                logger.LogWarning($"SCTP exception initialising association. {excp.Message}");
            }
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

            switch(signalingState)
            {
                case var sigState when sigState == RTCSignalingState.have_local_offer && sdpType == SdpType.offer:
                    logger.LogWarning($"RTCPeerConnection received an SDP offer but was already in {sigState} state. Remote offer rejected.");
                    return SetDescriptionResultEnum.WrongSdpTypeOfferAfterOffer;
                default:
                    break;
            }

            var setResult = base.SetRemoteDescription(sdpType, remoteSdp);

            if (setResult == SetDescriptionResultEnum.OK)
            {
                string remoteIceUser = remoteSdp.IceUfrag;
                string remoteIcePassword = remoteSdp.IcePwd;
                string dtlsFingerprint = remoteSdp.DtlsFingerprint;

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
                        ann.ApplicationMediaFormats.Single().Key == SDP_DATACHANNEL_FORMAT_ID)
                    {
                        if (ann.Transport == RTP_MEDIA_DATACHANNEL_DTLS_PROFILE ||
                            ann.Transport == RTP_MEDIA_DATACHANNEL_UDPDTLS_PROFILE)
                        {
                            dtlsFingerprint = dtlsFingerprint ?? ann.DtlsFingerprint;
                        }
                        else
                        {
                            logger.LogWarning($"The remote SDP requested an unsupported data channel transport of {ann.Transport}.");
                            return SetDescriptionResultEnum.DataChannelTransportNotSupported;
                        }
                    }
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

                // Trigger the ICE candidate events for any non-host candidates, host candidates are always included in the
                // SDP offer/answer. The reason for the trigger is that ICE candidates cannot be sent to the remote peer
                // until it is ready to receive them which is indicated by the remote offer being received.
                foreach (var nonHostCand in _rtpIceChannel.Candidates.Where(x => x.type != RTCIceCandidateType.host))
                {
                    _onIceCandidate?.Invoke(nonHostCand);
                }
            }

            return setResult;
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

                _rtpIceChannel?.Close();
                _dtlsHandle?.Close();
                _dtlsHandle = null;
                _peerSctpAssociation?.Close();

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
            var audioCapabilities = AudioLocalTrack?.Capabilities;
            var videoCapabilities = VideoLocalTrack?.Capabilities;

            List<MediaStreamTrack> localTracks = GetLocalTracks();
            bool excludeIceCandidates = options != null && options.X_ExcludeIceCandidates;
            var offerSdp = createBaseSdp(localTracks, audioCapabilities, videoCapabilities, excludeIceCandidates);

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

        /// <summary>
        /// Convenience overload to suit SIP/VoIP callers.
        /// TODO: Consolidate with createAnswer.
        /// </summary>
        /// <param name="connectionAddress">Not used.</param>
        /// <returns>An SDP payload to answer an offer from the remote party.</returns>
        public override SDP CreateOffer(IPAddress connectionAddress)
        {
            var result = createOffer(null);

            if (result?.sdp != null)
            {
                return SDP.ParseSDPDescription(result.sdp);
            }

            return null;
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
                    SDPAudioVideoMediaFormat.GetCompatibleFormats(AudioLocalTrack.Capabilities, AudioRemoteTrack.Capabilities) : null;
                var videoCapabilities = (VideoLocalTrack != null && VideoRemoteTrack != null) ?
                    SDPAudioVideoMediaFormat.GetCompatibleFormats(VideoLocalTrack.Capabilities, VideoRemoteTrack.Capabilities) : null;

                List<MediaStreamTrack> localTracks = GetLocalTracks();
                bool excludeIceCandidates = options != null && options.X_ExcludeIceCandidates;
                var answerSdp = createBaseSdp(localTracks, audioCapabilities, videoCapabilities, excludeIceCandidates);

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
        /// <param name="tracks">THe local media tracks to add to the SDP description.</param>
        /// <param name="audioCapabilities">Optional. The audio formats to support in the SDP. This list can differ from
        /// the local audio track if an answer is being generated and only mutually supported formats are being
        /// used.</param>
        /// <param name="videoCapabilities">Optional. The video formats to support in the SDP. This list can differ from
        /// the local video track if an answer is being generated and only mutually supported formats are being
        /// used.</param>
        /// <param name="excludeIceCandidates">If true it indicates the caller does not want ICE candidates added
        /// to the SDP.</param>
        /// <remarks>
        /// From https://tools.ietf.org/html/draft-ietf-mmusic-ice-sip-sdp-39#section-4.2.5:
        ///   "The transport address from the peer for the default destination
        ///   is set to IPv4/IPv6 address values "0.0.0.0"/"::" and port value
        ///   of "9".  This MUST NOT be considered as a ICE failure by the peer
        ///   agent and the ICE processing MUST continue as usual."
        /// </remarks>
        private SDP createBaseSdp(List<MediaStreamTrack> tracks,
            List<SDPAudioVideoMediaFormat> audioCapabilities,
            List<SDPAudioVideoMediaFormat> videoCapabilities,
            bool excludeIceCandidates = false)
        {
            SDP offerSdp = new SDP(IPAddress.Loopback);
            offerSdp.SessionId = LocalSdpSessionID;

            bool iceCandidatesAdded = false;
            int mediaIndex = 0;

            //oferSdp.DtlsFingerprint = _currentCertificate.getFingerprints().First().ToString();
            offerSdp.DtlsFingerprint = this.DtlsCertificateFingerprint.ToString();

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
                int mindex = RemoteDescription == null ? mediaIndex++ : RemoteDescription.GetIndexForMediaType(track.Kind);

                if (mindex == SDP.MEDIA_INDEX_NOT_PRESENT)
                {
                    logger.LogWarning($"Media announcement for {track.Kind} omitted due to no reciprocal remote announcement.");
                }
                else
                {
                    SDPMediaAnnouncement announcement = new SDPMediaAnnouncement(
                     track.Kind,
                     SDP.IGNORE_RTP_PORT_NUMBER,
                     (track.Kind == SDPMediaTypesEnum.video) ? videoCapabilities : audioCapabilities);

                    announcement.Transport = RTP_MEDIA_PROFILE;
                    announcement.Connection = new SDPConnectionInformation(IPAddress.Any);
                    announcement.AddExtra(RTCP_MUX_ATTRIBUTE);
                    announcement.AddExtra(RTCP_ATTRIBUTE);
                    announcement.MediaStreamStatus = track.StreamStatus;
                    announcement.MediaID = mindex.ToString();
                    announcement.MLineIndex = mindex;

                    announcement.IceUfrag = _rtpIceChannel.LocalIceUser;
                    announcement.IcePwd = _rtpIceChannel.LocalIcePassword;
                    announcement.IceOptions = ICE_OPTIONS;
                    announcement.DtlsFingerprint = offerSdp.DtlsFingerprint;

                    if (iceCandidatesAdded == false && !excludeIceCandidates)
                    {
                        AddIceCandidates(announcement);
                        iceCandidatesAdded = true;
                    }

                    offerSdp.Media.Add(announcement);
                }
            }

            if (DataChannels.Count > 0 || (RemoteDescription?.Media.Any(x => x.Media == SDPMediaTypesEnum.application) ?? false))
            {
                int mindex = RemoteDescription == null ? mediaIndex++ : RemoteDescription.GetIndexForMediaType(SDPMediaTypesEnum.application);

                if (mindex == SDP.MEDIA_INDEX_NOT_PRESENT)
                {
                    logger.LogWarning($"Media announcement for data channel establishment omitted due to no reciprocal remote announcement.");
                }
                else
                {
                    SDPMediaAnnouncement dataChannelAnnouncement = new SDPMediaAnnouncement(
                        SDPMediaTypesEnum.application,
                        SDP.IGNORE_RTP_PORT_NUMBER,
                        new List<SDPApplicationMediaFormat> { new SDPApplicationMediaFormat(SDP_DATACHANNEL_FORMAT_ID) });
                    dataChannelAnnouncement.Transport = RTP_MEDIA_DATACHANNEL_UDPDTLS_PROFILE;
                    dataChannelAnnouncement.Connection = new SDPConnectionInformation(IPAddress.Any);

                    dataChannelAnnouncement.SctpPort = SCTP_DEFAULT_PORT;
                    dataChannelAnnouncement.MaxMessageSize = SCTP_DEFAULT_MAX_MESSAGE_SIZE;
                    dataChannelAnnouncement.MLineIndex = mindex;
                    dataChannelAnnouncement.MediaID = mindex.ToString();
                    dataChannelAnnouncement.IceUfrag = _rtpIceChannel.LocalIceUser;
                    dataChannelAnnouncement.IcePwd = _rtpIceChannel.LocalIcePassword;
                    dataChannelAnnouncement.IceOptions = ICE_OPTIONS;
                    dataChannelAnnouncement.DtlsFingerprint = offerSdp.DtlsFingerprint;

                    if (iceCandidatesAdded == false && !excludeIceCandidates)
                    {
                        AddIceCandidates(dataChannelAnnouncement);
                        iceCandidatesAdded = true;
                    }

                    offerSdp.Media.Add(dataChannelAnnouncement);
                }
            }

            // Set the Bundle attribute to indicate all media announcements are being multiplexed.
            if (offerSdp.Media?.Count > 0)
            {
                offerSdp.Group = BUNDLE_ATTRIBUTE;
                foreach (var ann in offerSdp.Media.OrderBy(x => x.MediaID))
                {
                    offerSdp.Group += $" {ann.MediaID}";
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
                    {
                        if (_dtlsHandle != null)
                        {
                            //logger.LogDebug($"DTLS transport received {buffer.Length} bytes from {AudioDestinationEndPoint}.");
                            _dtlsHandle.WriteToRecvStream(buffer);
                        }
                        else
                        {
                            logger.LogWarning($"DTLS packet received {buffer.Length} bytes from {remoteEP} but no DTLS transport available.");
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

        /// <summary>
        /// Gets the initial optional configuration settings this peer connection was created
        /// with.
        /// </summary>
        /// <returns>If available the initial configuration options.</returns>
        public RTCConfiguration getConfiguration()
        {
            return _configuration;
        }

        /// <summary>
        /// Not implemented. Configuration options cannot currently be changed once the peer
        /// connection has been initialised.
        /// </summary>
        public void setConfiguration(RTCConfiguration configuration = null)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds a new data channel to the peer connection.
        /// </summary>
        /// <param name="label">THe label used to identify the data channel.</param>
        /// <returns>The data channel created.</returns>
        public RTCDataChannel createDataChannel(string label, RTCDataChannelInit init)
        {
            logger.LogDebug($"Data channel create request for label {label}.");

            RTCDataChannel channel = new RTCDataChannel
            {
                label = label,
            };

            DataChannels.Add(channel);

            // If the SCTP association is ready attempt to create a new SCTP stream for the data channel.
            // If the association is not ready the stream creation attempt will be triggered once it is.
            if (_peerSctpAssociation != null && _peerSctpAssociation.IsAssociated)
            {
                CreateSctpStreamForDataChannel(channel);
            }

            return channel;
        }

        /// <summary>
        /// Attempts to create and wire up the SCTP stream for a data channel.
        /// </summary>
        /// <param name="dataChannel">The data channel to create the SCTP stream for.</param>
        /// <returns>The Task being used to create the SCTP stream.</returns>
        private void CreateSctpStreamForDataChannel(RTCDataChannel dataChannel)
        {
            logger.LogDebug($"Attempting to create SCTP stream for data channel with label {dataChannel.label}.");

            Task.Run(async () =>
            {
                try
                {
                    var s = await _peerSctpAssociation.CreateStream(dataChannel.label).ConfigureAwait(false);
                    dataChannel.SetStream(s);
                }
                catch (Exception e)
                {
                    logger.LogWarning($"Exception creating data channel {e.Message}");
                    dataChannel.SetError(e.Message);
                }
            });
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
                rtpChannel.Send(RTPChannelSocketsEnum.RTP, AudioDestinationEndPoint, buf);
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

                if (remoteFingerprint.value?.ToUpper() != expectedFp.value?.ToUpper())
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

                    return true;
                }
            }
        }

        /// <summary>
        /// Event handler for TLS alerts from the DTLS transport.
        /// </summary>
        /// <param name="alertLevel">The level of the alert: warning or critical.</param>
        /// <param name="alertType">The type of the alert.</param>
        /// <param name="alertDescription">An optional description for the alert.</param>
        private void OnDtlsAlert(AlertLevelsEnum alertLevel, AlertTypesEnum alertType, string alertDescription)
        {
            if (alertType == AlertTypesEnum.close_notify)
            {
                logger.LogDebug($"SCTP closing association as a result of DTLS transport closure.");

                // No point keeping the SCTP association open if there is no DTLS transport available.
                _peerSctpAssociation.Close();
            }
            else
            {
                logger.LogWarning($"DTLS unexpected {alertLevel} alert {alertType}: {alertDescription}");
            }
        }

        /// <summary>
        /// Close the session if the instance is out of scope.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            Close("disposed");
        }

        /// <summary>
        /// Close the session if the instance is out of scope.
        /// </summary>
        public override void Dispose()
        {
            Close("disposed");
        }
    }
}
