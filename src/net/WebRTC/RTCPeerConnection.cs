//-----------------------------------------------------------------------------
// Filename: RTCPeerConnection.cs
//
// Description: Represents a WebRTC RTCPeerConnection.
//
// Specification Soup (as of 13 Jul 2020):
// - "Session Description Protocol (SDP) Offer/Answer procedures for
//   Interactive Connectivity Establishment(ICE)" [ed: specification for
//   including ICE candidates in SDP]:
//   https://tools.ietf.org/html/rfc8839
// - "Session Description Protocol (SDP) Offer/Answer Procedures For Stream
//   Control Transmission Protocol(SCTP) over Datagram Transport Layer
//   Security(DTLS) Transport." [ed: specification for negotiating
//   data channels in SDP, this defines the SDP "sctp-port" attribute] 
//   https://tools.ietf.org/html/rfc8841
// - "SDP-based Data Channel Negotiation" [ed: not currently implemented,
//   actually seems like a big pain to implement this given it can already
//   be done in-band on the SCTP connection]:
//   https://tools.ietf.org/html/rfc8864
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Mar 2016	Aaron Clauson	Created.
// 25 Aug 2019  Aaron Clauson   Updated from video only to audio and video.
// 18 Jan 2020  Aaron Clauson   Combined WebRTCPeer and WebRTCSession.
// 16 Mar 2020  Aaron Clauson   Refactored to support RTCPeerConnection interface.
// 13 Jul 2020  Aaron Clauson   Added data channel support.
// 22 Mar 2021  Aaron Clauson   Refactored data channels logic for new SCTP
//                              implementation.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
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
    /// https://tools.ietf.org/html/rfc8829 "JavaScript Session Establishment Protocol (JSEP)".
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
        private const string BUNDLE_ATTRIBUTE = "BUNDLE";
        private const string ICE_OPTIONS = "ice2,trickle";          // Supported ICE options.
        private const string NORMAL_CLOSE_REASON = "normal";
        private const ushort SCTP_DEFAULT_PORT = 5000;
        private const string UNKNOWN_DATACHANNEL_ERROR = "unknown";

        /// <summary>
        /// The period to wait for the SCTP association to complete before giving up.
        /// In theory this should be very quick as the DTLS connection should already have been established
        /// and the SCTP logic only needs to send the small handshake messages to establish
        /// the association.
        /// </summary>
        private const int SCTP_ASSOCIATE_TIMEOUT_SECONDS = 2;

        private new readonly string RTP_MEDIA_PROFILE = RTP_MEDIA_NON_FEEDBACK_PROFILE;
        private readonly string RTCP_ATTRIBUTE = $"a=rtcp:{SDP.IGNORE_RTP_PORT_NUMBER} IN IP4 0.0.0.0";

        private static ILogger logger = Log.Logger;

        public string SessionID { get; private set; }
        public string SdpSessionID { get; private set; }
        public string LocalSdpSessionID { get; private set; }

        private RtpIceChannel _rtpIceChannel;

        readonly RTCDataChannelCollection dataChannels;
        public IReadOnlyCollection<RTCDataChannel> DataChannels => dataChannels;

        private Org.BouncyCastle.Crypto.Tls.Certificate _dtlsCertificate;
        private Org.BouncyCastle.Crypto.AsymmetricKeyParameter _dtlsPrivateKey;
        private DtlsSrtpTransport _dtlsHandle;
        private Task _iceGatheringTask;

        /// <summary>
        /// Local ICE candidates that have been supplied directly by the application.
        /// Useful for cases where the application may has extra information about the
        /// network set up such as 1:1 NATs as used by Azure and AWS.
        /// </summary>
        private List<RTCIceCandidate> _applicationIceCandidates = new List<RTCIceCandidate>();

        /// <summary>
        /// The ICE role the peer is acting in.
        /// </summary>
        public IceRolesEnum IceRole { get; set; } = IceRolesEnum.actpass;

        /// <summary>
        /// The DTLS fingerprint supplied by the remote peer in their SDP. Needs to be checked
        /// that the certificate supplied during the DTLS handshake matches.
        /// </summary>
        public RTCDtlsFingerprint RemotePeerDtlsFingerprint { get; private set; }

        public bool IsDtlsNegotiationComplete { get; private set; } = false;

        public RTCSessionDescription localDescription { get; private set; }

        public RTCSessionDescription remoteDescription { get; private set; }

        public RTCSessionDescription currentLocalDescription => localDescription;

        public RTCSessionDescription pendingLocalDescription => null;

        public RTCSessionDescription currentRemoteDescription => remoteDescription;

        public RTCSessionDescription pendingRemoteDescription => null;

        public RTCSignalingState signalingState { get; private set; } = RTCSignalingState.closed;

        public RTCIceGatheringState iceGatheringState
        {
            get
            {
                return _rtpIceChannel != null ? _rtpIceChannel.IceGatheringState : RTCIceGatheringState.@new;
            }
        }

        public RTCIceConnectionState iceConnectionState
        {
            get
            {
                return _rtpIceChannel != null ? _rtpIceChannel.IceConnectionState : RTCIceConnectionState.@new;
            }
        }

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
        /// The SCTP transport over which SCTP data is sent and received.
        /// </summary>
        /// <remarks>
        /// WebRTC API definition:
        /// https://www.w3.org/TR/webrtc/#attributes-15
        /// </remarks>
        public RTCSctpTransport sctp { get; private set; }

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

        protected CancellationTokenSource _cancellationSource = new CancellationTokenSource();
        protected object _renegotiationLock = new object();
        protected volatile bool _requireRenegotiation = true;

        public override bool RequireRenegotiation
        {
            get
            {
                return _requireRenegotiation;
            }

            protected internal set
            {
                lock (_renegotiationLock)
                {
                    _requireRenegotiation = value;
                    //Remove Remote Description
                    if (_requireRenegotiation)
                    {
                        RemoteDescription = null;
                    }
                }

                //Remove NegotiationTask when state not stable
                if (!_requireRenegotiation || signalingState != RTCSignalingState.stable)
                {
                    CancelOnNegotiationNeededTask();
                }
                //Call Renegotiation Delayed (We need to wait as user can try add multiple tracks in sequence)
                else
                {
                    StartOnNegotiationNeededTask();
                }
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
        public RTCPeerConnection() :
            this(null)
        { }

        /// <summary>
        /// Constructor to create a new RTC peer connection instance.
        /// </summary>
        /// <param name="configuration">Optional.</param>
        public RTCPeerConnection(RTCConfiguration configuration, int bindPort = 0) :
            base(true, true, true, configuration?.X_BindAddress, bindPort)
        {
            dataChannels = new RTCDataChannelCollection(useEvenIds: () => _dtlsHandle.IsClient);
            
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
                        _dtlsCertificate = DtlsUtils.LoadCertificateChain(usableCert.Certificate);
                        _dtlsPrivateKey = DtlsUtils.LoadPrivateKeyResource(usableCert.Certificate);
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

            if (_dtlsCertificate == null)
            {
                // No certificate was provided so create a new self signed one.
                (_dtlsCertificate, _dtlsPrivateKey) = DtlsUtils.CreateSelfSignedTlsCert();
            }

            DtlsCertificateFingerprint = DtlsUtils.Fingerprint(_dtlsCertificate);

            SessionID = Guid.NewGuid().ToString();
            LocalSdpSessionID = Crypto.GetRandomInt(5).ToString();

            // Request the underlying RTP session to create a single RTP channel that will
            // be used to multiplex all required media streams.
            addSingleTrack();

            _rtpIceChannel = GetRtpChannel();

            _rtpIceChannel.OnIceCandidate += (candidate) => _onIceCandidate?.Invoke(candidate);
            _rtpIceChannel.OnIceConnectionStateChange += IceConnectionStateChange;
            _rtpIceChannel.OnIceGatheringStateChange += (state) => onicegatheringstatechange?.Invoke(state);
            _rtpIceChannel.OnIceCandidateError += (candidate, error) => onicecandidateerror?.Invoke(candidate, error);

            OnRtpClosed += Close;
            OnRtcpBye += Close;
            
            //Cancel Negotiation Task Event to Prevent Duplicated Calls
            onnegotiationneeded += CancelOnNegotiationNeededTask;

            sctp = new RTCSctpTransport(SCTP_DEFAULT_PORT, SCTP_DEFAULT_PORT, _rtpIceChannel.RTPPort);

            onnegotiationneeded?.Invoke();

            // This is the point the ICE session potentially starts contacting STUN and TURN servers.
            // This job was moved to a background thread as it was observed that interacting with the OS network
            // calls and/or initialising DNS was taking up to 600ms, see
            // https://github.com/sipsorcery-org/sipsorcery/issues/456.
            _iceGatheringTask = Task.Run(_rtpIceChannel.StartGathering);
        }

        /// <summary>
        /// Event handler for ICE connection state changes.
        /// </summary>
        /// <param name="state">The new ICE connection state.</param>
        private async void IceConnectionStateChange(RTCIceConnectionState iceState)
        {
            oniceconnectionstatechange?.Invoke(iceConnectionState);

            if (iceState == RTCIceConnectionState.connected && _rtpIceChannel.NominatedEntry != null)
            {
                if (_dtlsHandle != null)
                {
                    if (base.AudioDestinationEndPoint?.Address.Equals(_rtpIceChannel.NominatedEntry.RemoteCandidate.DestinationEndPoint.Address) == false ||
                        base.AudioDestinationEndPoint?.Port != _rtpIceChannel.NominatedEntry.RemoteCandidate.DestinationEndPoint.Port)
                    {
                        // Already connected and this event is due to change in the nominated remote candidate.
                        var connectedEP = _rtpIceChannel.NominatedEntry.RemoteCandidate.DestinationEndPoint;
                        base.SetDestination(SDPMediaTypesEnum.audio, connectedEP, connectedEP);

                        logger.LogInformation($"ICE changing connected remote end point to {AudioDestinationEndPoint}.");
                    }

                    if (connectionState == RTCPeerConnectionState.disconnected ||
                        connectionState == RTCPeerConnectionState.failed)
                    {
                        // The ICE connection state change is due to a re-connection.
                        connectionState = RTCPeerConnectionState.connected;
                        onconnectionstatechange?.Invoke(connectionState);
                    }
                }
                else
                {
                    connectionState = RTCPeerConnectionState.connecting;
                    onconnectionstatechange?.Invoke(connectionState);

                    var connectedEP = _rtpIceChannel.NominatedEntry.RemoteCandidate.DestinationEndPoint;

                    base.SetDestination(SDPMediaTypesEnum.audio, connectedEP, connectedEP);

                    logger.LogInformation($"ICE connected to remote end point {AudioDestinationEndPoint}.");

                    bool disableDtlsExtendedMasterSecret = _configuration != null && _configuration.X_DisableExtendedMasterSecretKey;
                    _dtlsHandle = new DtlsSrtpTransport(
                                IceRole == IceRolesEnum.active ?
                                new DtlsSrtpClient(_dtlsCertificate, _dtlsPrivateKey)
                                { ForceUseExtendedMasterSecret = !disableDtlsExtendedMasterSecret } :
                                (IDtlsSrtpPeer)new DtlsSrtpServer(_dtlsCertificate, _dtlsPrivateKey)
                                { ForceUseExtendedMasterSecret = !disableDtlsExtendedMasterSecret }
                                );

                    _dtlsHandle.OnAlert += OnDtlsAlert;

                    logger.LogDebug($"Starting DLS handshake with role {IceRole}.");

                    try
                    {
                        bool handshakeResult = await Task.Run(() => DoDtlsHandshake(_dtlsHandle)).ConfigureAwait(false);

                        connectionState = (handshakeResult) ? RTCPeerConnectionState.connected : connectionState = RTCPeerConnectionState.failed;
                        onconnectionstatechange?.Invoke(connectionState);

                        if (connectionState == RTCPeerConnectionState.connected)
                        {
                            await base.Start().ConfigureAwait(false);
                            await InitialiseSctpTransport().ConfigureAwait(false);
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.LogWarning(excp, $"RTCPeerConnection DTLS handshake failed. {excp.Message}");

                        //connectionState = RTCPeerConnectionState.failed;
                        //onconnectionstatechange?.Invoke(connectionState);

                        Close("dtls handshake failed");
                    }
                }
            }

            if (iceConnectionState == RTCIceConnectionState.checking)
            {
                // Not sure about this correspondence between the ICE and peer connection states.
                // TODO: Double check spec.
                //connectionState = RTCPeerConnectionState.connecting;
                //onconnectionstatechange?.Invoke(connectionState);
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
                _configuration != null ? _configuration.iceTransportPolicy : RTCIceTransportPolicy.all,
                _configuration != null ? _configuration.X_ICEIncludeAllInterfaceAddresses : false,
                m_bindPort == 0 ? 0 : m_bindPort + m_rtpChannels.Count() * 2 + 2);

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
        /// <param name="init">Optional. The session description to set as 
        /// local description. If not supplied then an offer or answer will be created as required.
        /// </param>
        public Task setLocalDescription(RTCSessionDescriptionInit init)
        {
            localDescription = new RTCSessionDescription { type = init.type, sdp = SDP.ParseSDPDescription(init.sdp) };

            if (init.type == RTCSdpType.offer)
            {
                _rtpIceChannel.IsController = true;
            }

            if (signalingState == RTCSignalingState.have_remote_offer)
            {
                signalingState = RTCSignalingState.stable;
                onsignalingstatechange?.Invoke();
            }
            else
            {
                signalingState = RTCSignalingState.have_local_offer;
                onsignalingstatechange?.Invoke();
            }

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
        /// </summary>
        /// <param name="init">The answer/offer SDP from the remote party.</param>
        public SetDescriptionResultEnum setRemoteDescription(RTCSessionDescriptionInit init)
        {
            remoteDescription = new RTCSessionDescription { type = init.type, sdp = SDP.ParseSDPDescription(init.sdp) };

            SDP remoteSdp = SDP.ParseSDPDescription(init.sdp);

            SdpType sdpType = (init.type == RTCSdpType.offer) ? SdpType.offer : SdpType.answer;

            switch (signalingState)
            {
                case var sigState when sigState == RTCSignalingState.have_local_offer && sdpType == SdpType.offer:
                    logger.LogWarning($"RTCPeerConnection received an SDP offer but was already in {sigState} state. Remote offer rejected.");
                    return SetDescriptionResultEnum.WrongSdpTypeOfferAfterOffer;
            }

            var setResult = base.SetRemoteDescription(sdpType, remoteSdp);

            if (setResult == SetDescriptionResultEnum.OK)
            {
                string remoteIceUser = remoteSdp.IceUfrag;
                string remoteIcePassword = remoteSdp.IcePwd;
                string dtlsFingerprint = remoteSdp.DtlsFingerprint;
                IceRolesEnum? remoteIceRole = remoteSdp.IceRole;

                foreach (var ann in remoteSdp.Media)
                {
                    if (remoteIceUser == null || remoteIcePassword == null || dtlsFingerprint == null || remoteIceRole == null)
                    {
                        remoteIceUser = remoteIceUser ?? ann.IceUfrag;
                        remoteIcePassword = remoteIcePassword ?? ann.IcePwd;
                        dtlsFingerprint = dtlsFingerprint ?? ann.DtlsFingerprint;
                        remoteIceRole = remoteIceRole ?? ann.IceRole;
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
                            remoteIceRole = remoteIceRole ?? remoteSdp.IceRole;
                        }
                        else
                        {
                            logger.LogWarning($"The remote SDP requested an unsupported data channel transport of {ann.Transport}.");
                            return SetDescriptionResultEnum.DataChannelTransportNotSupported;
                        }
                    }
                }

                SdpSessionID = remoteSdp.SessionId;

                if (remoteSdp.IceImplementation == IceImplementationEnum.lite) {
                    _rtpIceChannel.IsController = true;
                }
                if (init.type == RTCSdpType.answer)
                {
                    _rtpIceChannel.IsController = true;
                    IceRole = remoteIceRole == IceRolesEnum.passive ? IceRolesEnum.active : IceRolesEnum.passive;
                }
                //As Chrome does not support changing IceRole while renegotiating we need to keep same previous IceRole if we already negotiated before
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

                UpdatedSctpDestinationPort();

                if (init.type == RTCSdpType.offer)
                {
                    signalingState = RTCSignalingState.have_remote_offer;
                    onsignalingstatechange?.Invoke();
                }
                else
                {
                    signalingState = RTCSignalingState.stable;
                    onsignalingstatechange?.Invoke();
                }

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

                if (sctp != null && sctp.state == RTCSctpTransportState.Connected)
                {
                    sctp?.Close();
                }

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
        public RTCSessionDescriptionInit createOffer(RTCOfferOptions options = null)
        {
            List<MediaStreamTrack> localTracks = GetLocalTracks();
            //Revert to DefaultStreamStatus
            foreach (var localTrack in localTracks)
            {
                if (localTrack != null && localTrack.StreamStatus == MediaStreamStatusEnum.Inactive)
                {
                    localTrack.StreamStatus = localTrack.DefaultStreamStatus;
                }
            }

            var audioLocalTrack = localTracks.Find(a => a.Kind == SDPMediaTypesEnum.audio);
            var videoLocalTrack = localTracks.Find(a => a.Kind == SDPMediaTypesEnum.video);

            var audioCapabilities = audioLocalTrack?.Capabilities;
            var videoCapabilities = videoLocalTrack?.Capabilities;

            bool excludeIceCandidates = options != null && options.X_ExcludeIceCandidates;
            var offerSdp = createBaseSdp(localTracks, audioCapabilities, videoCapabilities, excludeIceCandidates);

            foreach (var ann in offerSdp.Media)
            {
                ann.IceRole = IceRole;
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
        public RTCSessionDescriptionInit createAnswer(RTCAnswerOptions options = null)
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

                //Revert to DefaultStreamStatus
                foreach (var localTrack in localTracks)
                {
                    if (localTrack != null && localTrack.StreamStatus == MediaStreamStatusEnum.Inactive)
                    {
                        if ((localTrack.Kind == SDPMediaTypesEnum.audio && AudioRemoteTrack != null && AudioRemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive) ||
                            (localTrack.Kind == SDPMediaTypesEnum.video && VideoRemoteTrack != null && VideoRemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive))
                        {
                            localTrack.StreamStatus = localTrack.DefaultStreamStatus;
                        }
                    }
                }

                bool excludeIceCandidates = options != null && options.X_ExcludeIceCandidates;
                var answerSdp = createBaseSdp(localTracks, audioCapabilities, videoCapabilities, excludeIceCandidates);

                //if (answerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio))
                //{
                //    var audioAnnouncement = answerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single();
                //    audioAnnouncement.IceRole = IceRole;
                //}

                //if (answerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.video))
                //{
                //    var videoAnnouncement = answerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single();
                //    videoAnnouncement.IceRole = IceRole;
                //}

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
            // Make sure the ICE gathering of local IP addresses is complete.
            // This task should complete very quickly (<1s) but it is deemed very useful to wait
            // for it to complete as it allows local ICE candidates to be included in the SDP.
            // In theory it would be better to an async/await but that would result in a breaking
            // change to the API and for a one off (once per class instance not once per method call)
            // delay of a few hundred milliseconds it was decided not to break the API.
            _iceGatheringTask.Wait();

            SDP offerSdp = new SDP(IPAddress.Loopback);
            offerSdp.SessionId = LocalSdpSessionID;

            string dtlsFingerprint = this.DtlsCertificateFingerprint.ToString();
            bool iceCandidatesAdded = false;
            int mediaIndex = 0;

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

                    foreach (var iceCandidate in _applicationIceCandidates)
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
                (int mindex, string midTag) = RemoteDescription == null || RequireRenegotiation ? (mediaIndex, mediaIndex.ToString()) : RemoteDescription.GetIndexForMediaType(track.Kind);
                mediaIndex++;
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
                    announcement.MediaID = midTag;
                    announcement.MLineIndex = mindex;

                    announcement.IceUfrag = _rtpIceChannel.LocalIceUser;
                    announcement.IcePwd = _rtpIceChannel.LocalIcePassword;
                    announcement.IceOptions = ICE_OPTIONS;
                    announcement.IceRole = IceRole;
                    announcement.DtlsFingerprint = dtlsFingerprint;

                    if (iceCandidatesAdded == false && !excludeIceCandidates)
                    {
                        AddIceCandidates(announcement);
                        iceCandidatesAdded = true;
                    }

                    if (track.Ssrc != 0)
                    {
                        string trackCname = track.Kind == SDPMediaTypesEnum.video ?
                       VideoRtcpSession?.Cname : AudioRtcpSession?.Cname;

                        if (trackCname != null)
                        {
                            announcement.SsrcAttributes.Add(new SDPSsrcAttribute(track.Ssrc, trackCname, null));
                        }
                    }

                    offerSdp.Media.Add(announcement);
                }
            }

            if (DataChannels.Count > 0 || (RemoteDescription?.Media.Any(x => x.Media == SDPMediaTypesEnum.application) ?? false))
            {
                (int mindex, string midTag) = RemoteDescription == null ? (mediaIndex++, mediaIndex.ToString()) : RemoteDescription.GetIndexForMediaType(SDPMediaTypesEnum.application);

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
                    dataChannelAnnouncement.MaxMessageSize = sctp.maxMessageSize;
                    dataChannelAnnouncement.MLineIndex = mindex;
                    dataChannelAnnouncement.MediaID = midTag;
                    dataChannelAnnouncement.IceUfrag = _rtpIceChannel.LocalIceUser;
                    dataChannelAnnouncement.IcePwd = _rtpIceChannel.LocalIcePassword;
                    dataChannelAnnouncement.IceOptions = ICE_OPTIONS;
                    dataChannelAnnouncement.IceRole = IceRole;
                    dataChannelAnnouncement.DtlsFingerprint = dtlsFingerprint;

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
                foreach (var ann in offerSdp.Media.OrderBy(x => x.MLineIndex))
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

            // By this point the RTP ICE channel has already processed any STUN packets which means 
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
        /// Used to add a local ICE candidate. These are for candidates that the application may
        /// want to provide in addition to the ones that will be automatically determined. An
        /// example is when a machine is behind a 1:1 NAT and the application wants a host 
        /// candidate with the public IP address to be included.
        /// </summary>
        /// <param name="candidateInit">The ICE candidate to add.</param>
        /// <example>
        /// var natCandidate = new RTCIceCandidate(RTCIceProtocol.udp, natAddress, natPort, RTCIceCandidateType.host);
        /// pc.addLocalIceCandidate(natCandidate);
        /// </example>
        public void addLocalIceCandidate(RTCIceCandidate candidate)
        {
            candidate.usernameFragment = _rtpIceChannel.LocalIceUser;
            _applicationIceCandidates.Add(candidate);
        }

        /// <summary>
        /// Used to add remote ICE candidates to the peer connection's checklist.
        /// </summary>
        /// <param name="candidateInit">The remote ICE candidate to add.</param>
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
        /// Once the SDP exchange has been made the SCTP transport ports are known. If the destination
        /// port is not using the default value attempt to update it on teh SCTP transprot.
        /// </summary>
        private void UpdatedSctpDestinationPort()
        {
            // If a data channel was requested by the application then create the SCTP association.
            var sctpAnn = RemoteDescription.Media.Where(x => x.Media == SDPMediaTypesEnum.application).FirstOrDefault();
            ushort destinationPort = sctpAnn?.SctpPort != null ? sctpAnn.SctpPort.Value : SCTP_DEFAULT_PORT;

            if (destinationPort != SCTP_DEFAULT_PORT)
            {
                sctp.UpdateDestinationPort(destinationPort);
            }
        }

        /// <summary>
        /// These internal function is used to call Renegotiation Event with delay as the user should call addTrack/removeTrack in sequence so we need a small delay to prevent multiple renegotiation calls
        /// </summary>
        /// <returns>Current Executing Task</returns>
        protected virtual Task StartOnNegotiationNeededTask()
        {
            const int RENEGOTIATION_CALL_DELAY = 100;

            //We need to reset the timer every time that we call this function
            CancelOnNegotiationNeededTask();

            CancellationToken token;
            lock (_renegotiationLock)
            {
                _cancellationSource = new CancellationTokenSource();
                token = _cancellationSource.Token;
            }
            return Task.Run(async () =>
            {
                //Call Renegotiation Delayed
                await Task.Delay(RENEGOTIATION_CALL_DELAY, token);

                //Prevent continue with cancellation requested
                if (token.IsCancellationRequested)
                {
                    return;
                }
                else
                {
                    if (_requireRenegotiation)
                    {
                        //We Already Subscribe CancelRenegotiationEventTask in Constructor so we dont need to handle with this function again here
                        onnegotiationneeded?.Invoke();
                    }
                }
            }, token);
        }

        /// <summary>
        /// Cancel current Negotiation Event Call to prevent running thread to call OnNegotiationNeeded
        /// </summary>
        protected virtual void CancelOnNegotiationNeededTask()
        {
            lock (_renegotiationLock)
            {
                if (_cancellationSource != null)
                {
                    if (!_cancellationSource.IsCancellationRequested)
                    {
                        _cancellationSource.Cancel();
                    }

                    _cancellationSource = null;
                }
            }
        }

        /// <summary>
        /// Initialises the SCTP transport. This will result in the DTLS SCTP transport listening 
        /// for incoming INIT packets if the remote peer attempts to create the association. The local
        /// peer will NOT attempt to establish the association at this point. It's up to the
        /// application to specify it wants a data channel to initiate the SCTP association attempt.
        /// </summary>
        private async Task InitialiseSctpTransport()
        {
            try
            {
                sctp.OnStateChanged += OnSctpTransportStateChanged;
                sctp.Start(_dtlsHandle.Transport, _dtlsHandle.IsClient);

                if (DataChannels.Count > 0)
                {
                    await InitialiseSctpAssociation().ConfigureAwait(false);
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"SCTP exception establishing association, data channels will not be available. {excp}");
                sctp?.Close();
            }
        }

        /// <summary>
        /// Event handler for changes to the SCTP transport state.
        /// </summary>
        /// <param name="state">The new transport state.</param>
        private void OnSctpTransportStateChanged(RTCSctpTransportState state)
        {
            if (state == RTCSctpTransportState.Connected)
            {
                logger.LogDebug("SCTP transport successfully connected.");

                sctp.RTCSctpAssociation.OnDataChannelData += OnSctpAssociationDataChunk;
                sctp.RTCSctpAssociation.OnDataChannelOpened += OnSctpAssociationDataChannelOpened;
                sctp.RTCSctpAssociation.OnNewDataChannel += OnSctpAssociationNewDataChannel;

                // Create new SCTP streams for any outstanding data channel requests.
                foreach (var dataChannel in dataChannels.ActivatePendingChannels())
                {
                    OpenDataChannel(dataChannel);
                }
            }
        }

        /// <summary>
        /// Event handler for a new data channel being opened by the remote peer.
        /// </summary>
        private void OnSctpAssociationNewDataChannel(ushort streamID, DataChannelTypes type, ushort priority, uint reliability, string label, string protocol)
        {
            logger.LogInformation($"WebRTC new data channel opened by remote peer for stream ID {streamID}, type {type}, " +
                $"priority {priority}, reliability {reliability}, label {label}, protocol {protocol}.");

            var init = CreateDataChannelInit(streamID, type, reliability);
            var dc = new RTCDataChannel(sctp, init) { label = label, IsOpened = true, readyState = RTCDataChannelState.open };

            dc.SendDcepAck();

            if (dataChannels.AddActiveChannel(dc))
            {
                ondatachannel?.Invoke(dc);
            }
            else
            {
                // TODO: What's the correct behaviour here?? I guess use the newest one and remove the old one?
                logger.LogWarning($"WebRTC duplicate data channel requested for stream ID {streamID}.");
            }
        }

        /// <summary>
        /// Creates a data channel configuration based on an incoming type and reliability.
        /// </summary>
        /// <returns>The data channel initialization</returns>
        private static RTCDataChannelInit CreateDataChannelInit(ushort id, DataChannelTypes type, uint reliability)
        {
            var init = new RTCDataChannelInit()
            {
                id = id,
            };
            switch (type)
            {
                case DataChannelTypes.DATA_CHANNEL_RELIABLE:
                    break;
                case DataChannelTypes.DATA_CHANNEL_RELIABLE_UNORDERED:
                    init.ordered = false;
                    break;
                case DataChannelTypes.DATA_CHANNEL_PARTIAL_RELIABLE_REXMIT:
                    init.maxRetransmits = (ushort)reliability;
                    break;
                case DataChannelTypes.DATA_CHANNEL_PARTIAL_RELIABLE_REXMIT_UNORDERED:
                    init.maxRetransmits = (ushort)reliability;
                    init.ordered = false;
                    break;
                case DataChannelTypes.DATA_CHANNEL_PARTIAL_RELIABLE_TIMED:
                    init.maxPacketLifeTime = (ushort)reliability;
                    break;
                case DataChannelTypes.DATA_CHANNEL_PARTIAL_RELIABLE_TIMED_UNORDERED:
                    init.maxPacketLifeTime = (ushort)reliability;
                    init.ordered = false;
                    break;
            }
            return init;
        }

        /// <summary>
        /// Event handler for the confirmation that a data channel opened by this peer has been acknowledged.
        /// </summary>
        /// <param name="streamID">The ID of the stream corresponding to the acknowledged data channel.</param>
        private void OnSctpAssociationDataChannelOpened(ushort streamID)
        {
            dataChannels.TryGetChannel(streamID, out var dc);

            string label = dc != null ? dc.label : "<none>";
            logger.LogInformation($"WebRTC data channel opened label {label} and stream ID {streamID}.");

            if (dc != null)
            {
                dc.GotAck();
            }
            else
            {
                logger.LogWarning($"WebRTC data channel got ACK but data channel not found for stream ID {streamID}.");
            }
        }

        /// <summary>
        /// Event handler for an SCTP DATA chunk being received on the SCTP association.
        /// </summary>
        /// <param name="streamID">The stream ID of the chunk.</param>
        /// <param name="streamSeqNum">The stream sequence number of the chunk. Will be 0 for unordered streams.</param>
        /// <param name="ppID">The payload protocol ID for the chunk.</param>
        /// <param name="data">The chunk data.</param>
        private void OnSctpAssociationDataChunk(SctpDataFrame frame)
        {
            if (dataChannels.TryGetChannel(frame.StreamID, out var dc))
            {
                dc.GotData(frame.StreamID, frame.StreamSeqNum, frame.PPID, frame.UserData);
            }
            else
            {
                logger.LogWarning($"WebRTC data channel got data but no channel found for stream ID {frame.StreamID}.");
            }
        }

        /// <summary>
        /// When a data channel is requested an SCTP association is needed. This method attempts to 
        /// initialise the association if it is not already available.
        /// </summary>
        private async Task InitialiseSctpAssociation()
        {
            if (sctp.RTCSctpAssociation.State != SctpAssociationState.Established)
            {
                sctp.Associate();
            }

            if (sctp.state != RTCSctpTransportState.Connected)
            {
                TaskCompletionSource<bool> onSctpConnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                sctp.OnStateChanged += (state) =>
                {
                    logger.LogDebug($"SCTP transport for create data channel request changed to state {state}.");

                    if (state == RTCSctpTransportState.Connected)
                    {
                        onSctpConnectedTcs.TrySetResult(true);
                    }
                };

                DateTime startTime = DateTime.Now;

                var completedTask = await Task.WhenAny(onSctpConnectedTcs.Task, Task.Delay(SCTP_ASSOCIATE_TIMEOUT_SECONDS * 1000)).ConfigureAwait(false);

                if (sctp.state != RTCSctpTransportState.Connected)
                {
                    var duration = DateTime.Now.Subtract(startTime).TotalMilliseconds;

                    if (completedTask != onSctpConnectedTcs.Task)
                    {
                        throw new ApplicationException($"SCTP association timed out after {duration:0.##}ms with association in state {sctp.RTCSctpAssociation.State} when attempting to create a data channel.");
                    }
                    else
                    {
                        throw new ApplicationException($"SCTP association failed after {duration:0.##}ms with association in state {sctp.RTCSctpAssociation.State} when attempting to create a data channel.");
                    }
                }
            }
        }

        /// <summary>
        /// Adds a new data channel to the peer connection.
        /// </summary>
        /// <remarks>
        /// WebRTC API definition:
        /// https://www.w3.org/TR/webrtc/#methods-11
        /// </remarks>
        /// <param name="label">The label used to identify the data channel.</param>
        /// <returns>The data channel created.</returns>
        public async Task<RTCDataChannel> createDataChannel(string label, RTCDataChannelInit init = null)
        {
            logger.LogDebug($"Data channel create request for label {label}.");

            RTCDataChannel channel = new RTCDataChannel(sctp, init)
            {
                label = label,
            };

            if (connectionState == RTCPeerConnectionState.connected)
            {
                // If the peer connection is not in a connected state there's no point doing anything
                // with the SCTP transport. If the peer connection does connect then a check will
                // be made for any pending data channels and the SCTP operations will be done then.

                if (sctp == null || sctp.state != RTCSctpTransportState.Connected)
                {
                    throw new ApplicationException("No SCTP transport is available.");
                }
                else
                {
                    if (sctp.RTCSctpAssociation == null ||
                        sctp.RTCSctpAssociation.State != SctpAssociationState.Established)
                    {
                        await InitialiseSctpAssociation().ConfigureAwait(false);
                    }

                    dataChannels.AddActiveChannel(channel);
                    OpenDataChannel(channel);

                    // Wait for the DCEP ACK from the remote peer.
                    TaskCompletionSource<string> isopen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    channel.onopen += () => isopen.TrySetResult(string.Empty);
                    channel.onerror += (err) => isopen.TrySetResult(err);
                    var error = await isopen.Task.ConfigureAwait(false);

                    if (error != string.Empty)
                    {
                        throw new ApplicationException($"Data channel creation failed with: {error}");
                    }
                    else
                    {
                        return channel;
                    }
                }
            }
            else
            {
                // Data channels can be created prior to the SCTP transport being available.
                // They will act as placeholders and then be opened once the SCTP transport 
                // becomes available.
                dataChannels.AddPendingChannel(channel);
                return channel;
            }
        }

        /// <summary>
        /// Sends the Data Channel Establishment Protocol (DCEP) OPEN message to configure the data
        /// channel on the remote peer.
        /// </summary>
        /// <param name="dataChannel">The data channel to open.</param>
        private void OpenDataChannel(RTCDataChannel dataChannel)
        {
            if (dataChannel.id.HasValue)
            {
                logger.LogDebug($"WebRTC attempting to open data channel with label {dataChannel.label} and stream ID {dataChannel.id}.");
                dataChannel.SendDcepOpen();
            }
            else
            {
                logger.LogError("Attempt to open a data channel without an assigned ID has failed.");
            }
        }

        /// <summary>
        ///  DtlsHandshake requires DtlsSrtpTransport to work.
        ///  DtlsSrtpTransport is similar to C++ DTLS class combined with Srtp class and can perform 
        ///  Handshake as Server or Client in same call. The constructor of transport require a DtlsStrpClient 
        ///  or DtlsSrtpServer to work.
        /// </summary>
        /// <param name="dtlsHandle">The DTLS transport handle to perform the handshake with.</param>
        /// <returns>True if the DTLS handshake is successful or false if not.</returns>
        private bool DoDtlsHandshake(DtlsSrtpTransport dtlsHandle)
        {
            logger.LogDebug("RTCPeerConnection DoDtlsHandshake started.");

            var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

            dtlsHandle.OnDataReady += (buf) =>
            {
                //logger.LogDebug($"DTLS transport sending {buf.Length} bytes to {AudioDestinationEndPoint}.");
                rtpChannel.Send(RTPChannelSocketsEnum.RTP, AudioDestinationEndPoint, buf);
            };

            var handshakeResult = dtlsHandle.DoHandshake(out var handshakeError);

            if (!handshakeResult)
            {
                handshakeError = handshakeError ?? "unknown";
                logger.LogWarning($"RTCPeerConnection DTLS handshake failed with error {handshakeError}.");
                Close("dtls handshake failed");
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
                    Close("dtls fingerprint mismatch");
                    return false;
                }
                else
                {
                    logger.LogDebug($"RTCPeerConnection remote certificate fingerprint matched expected value of {remoteFingerprint.value} for {remoteFingerprint.algorithm}.");

                    base.SetSecurityContext(
                        new List<SDPMediaTypesEnum> { SDPMediaTypesEnum.audio, SDPMediaTypesEnum.video, SDPMediaTypesEnum.application },
                        dtlsHandle.ProtectRTP,
                        dtlsHandle.UnprotectRTP,
                        dtlsHandle.ProtectRTCP,
                        dtlsHandle.UnprotectRTCP);
                        
                    IsDtlsNegotiationComplete = true;

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
                logger.LogDebug($"SCTP closing transport as a result of DTLS close notification.");

                // No point keeping the SCTP association open if there is no DTLS transport available.
                sctp?.Close();
            }
            else
            {
                string alertMsg = !string.IsNullOrEmpty(alertDescription) ? $": {alertDescription}" : ".";
                logger.LogWarning($"DTLS unexpected {alertLevel} alert {alertType}{alertMsg}");
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
