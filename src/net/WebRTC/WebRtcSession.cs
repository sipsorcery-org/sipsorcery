//-----------------------------------------------------------------------------
// Filename: WebRtcSession.cs
//
// Description: Represents a WebRTC session with a remote peer.
//
// History:
// 04 Mar 2016	Aaron Clauson	Created.
// 25 Aug 2019  Aaron Clauson   Updated from video only to audio and video.
// 18 Jan 2020  Aaron Clauson   Combined WebRTCPeer and WebRTCSession.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate int DoDtlsHandshakeDelegate(WebRtcSession session);

    /// <summary>
    /// Represents a WebRTC session with a remote peer.
    /// </summary>
    public class WebRtcSession : RTPSession
    {
        private const int INITIAL_STUN_BINDING_PERIOD_MILLISECONDS = 1000;       // The period to send the initial STUN requests used to get an ICE candidates public IP address.
        private const int INITIAL_STUN_BINDING_ATTEMPTS_LIMIT = 3;                // The maximum number of binding attempts to determine a local socket's public IP address before giving up.
        private const int ICE_CONNECTED_NO_COMMUNICATIONS_TIMEOUT_SECONDS = 35; // If there are no messages received (STUN/RTP/RTCP) within this period the session will be closed.
        private const int MAXIMUM_TURN_ALLOCATE_ATTEMPTS = 4;
        private const int MAXIMUM_STUN_CONNECTION_ATTEMPTS = 5;
        private const int ICE_UFRAG_LENGTH = 4;
        private const int ICE_PASSWORD_LENGTH = 24;

        private const int STUN_CHECK_BASE_PERIOD_MILLISECONDS = 5000;
        private const float STUN_CHECK_LOW_RANDOMISATION_FACTOR = 0.5F;
        private const float STUN_CHECK_HIGH_RANDOMISATION_FACTOR = 1.5F;

        // SDP constants.
        private const string RTP_MEDIA_PROFILE = "RTP/SAVP";
        private const string RTCP_MUX_ATTRIBUTE = "a=rtcp-mux";       // Indicates the media announcement is using multiplexed RTCP.
        private const string SETUP_OFFER_ATTRIBUTE = "a=setup:actpass"; // Indicates the media announcement DTLS negotiation state is active/passive.
        private const string SETUP_ANSWER_ATTRIBUTE = "a=setup:passive"; // Indicates the media announcement DTLS negotiation state is passive.
        private const string MEDIA_GROUPING = "BUNDLE 0 1";

        private static ILogger logger = Log.Logger;

        public string SessionID { get; private set; }
        //public SDP SDP;
        public string SdpSessionID;
        public string LocalSdpSessionID;
        public string LocalIceUser;
        public string LocalIcePassword;
        public string RemoteIceUser;
        public string RemoteIcePassword;
        public DateTime IceNegotiationStartedAt;
        public List<IceCandidate> LocalIceCandidates;
        public IceConnectionStatesEnum IceConnectionState = IceConnectionStatesEnum.None;

        private List<IceCandidate> _remoteIceCandidates = new List<IceCandidate>();
        public List<IceCandidate> RemoteIceCandidates
        {
            get { return _remoteIceCandidates; }
        }

        public bool IsConnected
        {
            get { return IceConnectionState == IceConnectionStatesEnum.Connected; }
        }

        public bool IsDtlsNegotiationComplete
        {
            get { return base.IsSecureContextReady; }
        }

        /// <summary>
        /// The raison d'etre for the ICE checks. This represents the end point
        /// that we were able to connect to for the WebRTC session.
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; private set; }

        //RTPChannel _rtpChannel;
        private string _dtlsCertificateFingerprint;
        private IPEndPoint _turnServerEndPoint;
        private List<IPAddress> _offerAddresses;            // If set restricts which local IP addresses will be offered in ICE candidates.
        private DateTime _lastStunSentAt = DateTime.MinValue;
        private DateTime _lastStunMessageReceivedAt = DateTime.MinValue;
        private DateTime _lastCommunicationAt = DateTime.MinValue;

        public event Action<string> OnClose;

        /// <summary>
        /// Time to schedule the STUN checks on each ICE candidate.
        /// </summary>
        private Timer m_stunChecksTimer;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="dtlsFingerprint">The fingerprint of our DTLS certificate (we always act as the DTLS server).
        /// It gets placed in the SDP offer sent to the remote party.</param>
        /// <param name="offerAddresses">Optional. A list of the IP addresses used as local ICE candidates.
        /// If null then all local IP addresses get used.</param>
        /// <param name="turnServerEndPoint">Optional. A parameter that can be used include a TURN 
        /// server in this session's ICE candidate gathering.</param>
        public WebRtcSession(
            AddressFamily addrFamily,
            string dtlsFingerprint,
            List<IPAddress> offerAddresses,
            IPEndPoint turnServerEndPoint) :
            base(true, true, true)
        {
            _dtlsCertificateFingerprint = dtlsFingerprint;
            _offerAddresses = offerAddresses;
            _turnServerEndPoint = turnServerEndPoint;

            SessionID = Guid.NewGuid().ToString();

            LocalIceUser = Crypto.GetRandomString(ICE_UFRAG_LENGTH);
            LocalIcePassword = Crypto.GetRandomString(ICE_PASSWORD_LENGTH);
            LocalSdpSessionID = Crypto.GetRandomInt(5).ToString();

            OnRtpClosed += Close;
            OnRtcpBye += Close;
        }

        /// <summary>
        /// Sets the local SDP.
        /// </summary>
        /// <param name="sdp">The SDP to set.</param>
        public override void setLocalDescription(RTCSessionDescription sessionDescription)
        {
            base.setLocalDescription(sessionDescription);

            var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

            if (rtpChannel != null)
            {
                rtpChannel.OnRTPDataReceived += OnRTPDataReceived;
            }
        }

        /// <summary>
        /// Updates the session after receiving the remote SDP.
        /// At this point check that the codecs match. We currently only support:
        ///  - Audio: PCMU,
        ///  - Video: VP8.
        /// If they are not available there's no point carrying on.
        /// </summary>
        /// <param name="sessionDescription">The answer/offer SDP from the remote party.</param>
        public override void setRemoteDescription(RTCSessionDescription sessionDescription)
        {
            remoteDescription = sessionDescription;

            var audioAnnounce = sessionDescription.sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).FirstOrDefault();
            if (audioAnnounce != null)
            {
                var audioTrack = new MediaStreamTrack(audioAnnounce.MediaID, SDPMediaTypesEnum.audio, true, audioAnnounce.MediaFormats);
                addTrack(audioTrack);
            }

            var videoAnnounce = sessionDescription.sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).FirstOrDefault();
            if (videoAnnounce != null)
            {
                var videoTrack = new MediaStreamTrack(videoAnnounce.MediaID, SDPMediaTypesEnum.video, true, videoAnnounce.MediaFormats);
                addTrack(videoTrack);
            }

            SdpSessionID = sessionDescription.sdp.SessionId;
            RemoteIceUser = sessionDescription.sdp.IceUfrag ?? sessionDescription.sdp.Media.First().IceUfrag;
            RemoteIcePassword = sessionDescription.sdp.IcePwd ?? sessionDescription.sdp.Media.First().IcePwd;

            // All browsers seem to have gone to trickling ICE candidates now but just
            // in case one or more are given we can start the STUN dance immediately.
            if (sessionDescription.sdp.IceCandidates != null)
            {
                foreach (var iceCandidate in sessionDescription.sdp.IceCandidates)
                {
                    AppendRemoteIceCandidate(iceCandidate);
                }
            }

            foreach (var media in sessionDescription.sdp.Media)
            {
                if (media.IceCandidates != null)
                {
                    foreach (var iceCandidate in media.IceCandidates)
                    {
                        AppendRemoteIceCandidate(iceCandidate);
                    }
                }
            }
        }

        /// <summary>
        /// Adds an ICE candidate to the list of remote party candidates.
        /// </summary>
        /// <param name="remoteIceCandidate">The remote party candidate to add.</param>
        public void AppendRemoteIceCandidate(IceCandidate remoteIceCandidate)
        {
            IPAddress candidateIPAddress = null;

            //foreach (var iceCandidate in remoteIceCandidates)
            //{
            //    logger.LogDebug("Appending remote ICE candidate " + iceCandidate.NetworkAddress + ":" + iceCandidate.Port + ".");
            //}

            if (remoteIceCandidate.Transport.ToLower() != "udp")
            {
                logger.LogDebug("Omitting remote non-UDP ICE candidate. " + remoteIceCandidate.RawString + ".");
            }
            else if (!IPAddress.TryParse(remoteIceCandidate.NetworkAddress, out candidateIPAddress))
            {
                logger.LogDebug("Omitting ICE candidate with unrecognised IP Address. " + remoteIceCandidate.RawString + ".");
            }
            //else if (candidateIPAddress.AddressFamily == AddressFamily.InterNetworkV6)
            //{
            //    logger.LogDebug("Omitting IPv6 ICE candidate. " + remoteIceCandidate.RawString + ".");
            //}
            else
            {
                // ToDo: Add srflx and relay endpoints as hosts as well.

                if (!_remoteIceCandidates.Any(x => x.NetworkAddress == remoteIceCandidate.NetworkAddress && x.Port == remoteIceCandidate.Port))
                {
                    logger.LogDebug("Adding remote ICE candidate: " + remoteIceCandidate.CandidateType + " " + remoteIceCandidate.NetworkAddress + ":" + remoteIceCandidate.Port + " (" + remoteIceCandidate.RawString + ").");
                    _remoteIceCandidates.Add(remoteIceCandidate);
                }
            }

            // We now should have a remote ICE candidate to start the STUN dance with.
            SendStunConnectivityChecks(null);
        }

        /// <summary>
        /// Send a media sample to the remote party.
        /// </summary>
        /// <param name="mediaType">Whether the sample is audio or video.</param>
        /// <param name="sampleTimestamp">The RTP timestamp for the sample.</param>
        /// <param name="sample">The sample payload.</param>
        public void SendMedia(SDPMediaTypesEnum mediaType, uint sampleTimestamp, byte[] sample)
        {
            if (RemoteEndPoint != null && IsDtlsNegotiationComplete)
            {
                if (mediaType == SDPMediaTypesEnum.video)
                {
                    int vp8PayloadID = Convert.ToInt32(VideoLocalTrack.Capabilties.Single(x => x.FormatCodec == SDPMediaFormatsEnum.VP8).FormatID);
                    SendVp8Frame(sampleTimestamp, vp8PayloadID, sample);
                }
                else if (mediaType == SDPMediaTypesEnum.audio)
                {
                    int pcmuPayloadID = Convert.ToInt32(AudioLocalTrack.Capabilties.Single(x => x.FormatCodec == SDPMediaFormatsEnum.PCMU).FormatID);
                    SendAudioFrame(sampleTimestamp, pcmuPayloadID, sample);
                }
            }
        }

        /// <summary>
        /// Close the session including the underlying RTP session and channels.
        /// </summary>
        /// <param name="reason">An optional descriptive reason for the closure.</param>
        public void Close(string reason)
        {
            if (!IsClosed)
            {
                IceConnectionState = IceConnectionStatesEnum.Closed;
                m_stunChecksTimer.Dispose();
                CloseSession(reason);

                OnClose?.Invoke(reason);
            }
        }

        /// <summary>
        /// Generates the SDP for an offer that can be made to a remote peer.
        /// </summary>
        public override async Task<SDP> createOffer(RTCOfferOptions options)
        {
            try
            {
                var offerSdp = await createBaseSdp().ConfigureAwait(false);

                if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio))
                {
                    var audioAnnouncement = offerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single();
                    audioAnnouncement.AddExtra(SETUP_OFFER_ATTRIBUTE);
                }

                if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.video))
                {
                    var videoAnnouncement = offerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single();
                    videoAnnouncement.AddExtra(SETUP_OFFER_ATTRIBUTE);
                }

                return offerSdp;
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
        public async Task<SDP> createAnswer()
        {
            if (remoteDescription == null)
            {
                throw new ApplicationException("The remote SDP must be set before an SDP answer can be created.");
            }
            else
            {
                var answerSdp = await createBaseSdp().ConfigureAwait(false);

                if (answerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio))
                {
                    var audioAnnouncement = answerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single();
                    audioAnnouncement.AddExtra(SETUP_ANSWER_ATTRIBUTE);
                }

                if (answerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.video))
                {
                    var videoAnnouncement = answerSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single();
                    videoAnnouncement.AddExtra(SETUP_ANSWER_ATTRIBUTE);
                }

                return answerSdp;
            }
        }

        /// <summary>
        /// Generates the base SDP for an offer or answer. The SDP will then be tailored depending
        /// on whether it's being used in an offer or an answer.
        /// </summary>
        private async Task<SDP> createBaseSdp()
        {
            DateTime startGatheringTime = DateTime.Now;

            IceConnectionState = IceConnectionStatesEnum.Gathering;

            await GetIceCandidatesAsync().ConfigureAwait(false);

            logger.LogDebug($"ICE gathering completed for in {DateTime.Now.Subtract(startGatheringTime).TotalMilliseconds:#}ms, candidate count {LocalIceCandidates.Count}.");

            IceConnectionState = IceConnectionStatesEnum.GatheringComplete;

            if (LocalIceCandidates.Count == 0)
            {
                //logger.LogWarning("No local socket candidates were found for WebRTC call closing.");
                //Close("No local ICE candidates available.");
                throw new ApplicationException("No local ICE candidates available.");
            }
            else
            {
                SDP offerSdp = new SDP(IPAddress.Loopback);
                offerSdp.SessionId = LocalSdpSessionID;

                bool haveIceCandidatesBeenAdded = false;
                string localIceCandidateString = null;

                foreach (var iceCandidate in LocalIceCandidates)
                {
                    localIceCandidateString += iceCandidate.ToString();
                }

                // Add a bundle attribute. Indicates that audio and video sessions will be multiplexed
                // on a single RTP socket.
                if (AudioLocalTrack != null && VideoLocalTrack != null)
                {
                    offerSdp.Group = MEDIA_GROUPING;
                }

                // The media is being multiplexed so the audio and video RTP channel is the same.
                var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

                // --- Audio announcement ---
                if (AudioLocalTrack != null)
                {
                    SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                        SDPMediaTypesEnum.audio,
                        rtpChannel.RTPPort,
                        AudioLocalTrack.Capabilties);

                    audioAnnouncement.Transport = RTP_MEDIA_PROFILE;

                    if (!haveIceCandidatesBeenAdded)
                    {
                        audioAnnouncement.IceCandidates = LocalIceCandidates;
                        haveIceCandidatesBeenAdded = true;
                    }

                    audioAnnouncement.Connection = new SDPConnectionInformation(IPAddress.Any);
                    audioAnnouncement.IceUfrag = LocalIceUser;
                    audioAnnouncement.IcePwd = LocalIcePassword;
                    audioAnnouncement.DtlsFingerprint = _dtlsCertificateFingerprint;
                    audioAnnouncement.AddExtra(RTCP_MUX_ATTRIBUTE);
                    audioAnnouncement.MediaStreamStatus = AudioLocalTrack.Transceiver.Direction;
                    audioAnnouncement.MediaID = AudioLocalTrack.Transceiver.MID;

                    offerSdp.Media.Add(audioAnnouncement);
                }

                // --- Video announcement ---
                if (VideoLocalTrack != null)
                {
                    SDPMediaAnnouncement videoAnnouncement = new SDPMediaAnnouncement(
                        SDPMediaTypesEnum.video,
                        rtpChannel.RTPPort,
                        VideoLocalTrack.Capabilties);

                    videoAnnouncement.Transport = RTP_MEDIA_PROFILE;

                    if (!haveIceCandidatesBeenAdded)
                    {
                        videoAnnouncement.IceCandidates = LocalIceCandidates;
                        haveIceCandidatesBeenAdded = true;
                    }

                    videoAnnouncement.Connection = new SDPConnectionInformation(IPAddress.Any);
                    videoAnnouncement.IceUfrag = LocalIceUser;
                    videoAnnouncement.IcePwd = LocalIcePassword;
                    videoAnnouncement.DtlsFingerprint = _dtlsCertificateFingerprint;
                    videoAnnouncement.AddExtra(RTCP_MUX_ATTRIBUTE);
                    videoAnnouncement.MediaStreamStatus = VideoLocalTrack.Transceiver.Direction;
                    videoAnnouncement.MediaID = VideoLocalTrack.Transceiver.MID;

                    offerSdp.Media.Add(videoAnnouncement);
                }

                return offerSdp;
            }
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
        /// <param name="remoteEP"></param>
        /// <param name="buffer"></param>
        private void OnRTPDataReceived(IPEndPoint localEndPoint, IPEndPoint remoteEP, byte[] buffer)
        {
            //logger.LogDebug($"RTP channel received a packet from {remoteEP}, {buffer?.Length} bytes.");

            if (buffer?.Length > 0)
            {
                _lastCommunicationAt = DateTime.Now;

                try
                {
                    if (buffer[0] == 0x00 || buffer[0] == 0x01)
                    {
                        // STUN packet.
                        _lastStunMessageReceivedAt = DateTime.Now;
                        var stunMessage = STUNv2Message.ParseSTUNMessage(buffer, buffer.Length);
                        ProcessStunMessage(stunMessage, remoteEP);
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
        /// Attempts to get a list of local ICE candidates.
        /// </summary>
        private async Task GetIceCandidatesAsync()
        {
            // The media is being multiplexed so the audio and video RTP channel is the same.
            var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

            if (rtpChannel == null)
            {
                throw new ApplicationException("Cannot start gathering ICE candidates without an RTP channel.");
            }
            else
            {
                var localIPAddresses = _offerAddresses ?? NetServices.LocalIPAddresses;
                IceNegotiationStartedAt = DateTime.Now;
                LocalIceCandidates = new List<IceCandidate>();

                foreach (var address in localIPAddresses.Where(x => x.AddressFamily == rtpChannel.RTPLocalEndPoint.AddressFamily))
                {
                    var iceCandidate = new IceCandidate(address, rtpChannel.RTPPort);

                    if (_turnServerEndPoint != null)
                    {
                        iceCandidate.TurnServer = new TurnServer() { ServerEndPoint = _turnServerEndPoint };
                        iceCandidate.InitialStunBindingCheck = SendTurnServerBindingRequest(iceCandidate);
                    }

                    LocalIceCandidates.Add(iceCandidate);
                }

                await Task.WhenAll(LocalIceCandidates.Where(x => x.InitialStunBindingCheck != null).Select(x => x.InitialStunBindingCheck)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sends a STUN binding request to the a remote ICE candidate.
        /// </summary>
        /// <param name="iceCandidate">The ICE candidate to send the STUN binding request to.</param>
        private async Task SendTurnServerBindingRequest(IceCandidate iceCandidate)
        {
            var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

            int attempt = 1;

            while (attempt < INITIAL_STUN_BINDING_ATTEMPTS_LIMIT && !IsConnected && !IsClosed && !iceCandidate.IsGatheringComplete)
            {
                logger.LogDebug($"Sending STUN binding request {attempt} from {rtpChannel.RTPLocalEndPoint} to {iceCandidate.TurnServer.ServerEndPoint}.");

                STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);

                rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceCandidate.TurnServer.ServerEndPoint, stunReqBytes);

                await Task.Delay(INITIAL_STUN_BINDING_PERIOD_MILLISECONDS).ConfigureAwait(false);

                attempt++;
            }

            iceCandidate.IsGatheringComplete = true;
        }

        /// <summary>
        /// Periodically send a STUN binding request to check connectivity.
        /// </summary>
        /// <param name="stateInfo">Not used.</param>
        private void SendStunConnectivityChecks(Object stateInfo)
        {
            try
            {
                var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

                //logger.LogDebug($"Send STUN connectivity checks, local candidates {LocalIceCandidates.Count()}, remote candidates {_remoteIceCandidates.Count()}.");

                // If one of the ICE candidates has the remote RTP socket set then the negotiation is complete and the STUN checks are to keep the connection alive.
                if (RemoteIceUser != null && RemoteIcePassword != null)
                {
                    if (IsConnected)
                    {
                        // Remote RTP endpoint gets set when the DTLS negotiation is finished.
                        if (RemoteEndPoint != null)
                        {
                            //logger.LogDebug("Sending STUN connectivity check to client " + iceCandidate.RemoteRtpEndPoint + ".");

                            string localUser = LocalIceUser;

                            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                            stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + localUser);
                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

                            rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, RemoteEndPoint, stunReqBytes);

                            _lastStunSentAt = DateTime.Now;
                        }

                        if (_lastCommunicationAt != DateTime.MinValue)
                        {
                            var secondsSinceLastResponse = DateTime.Now.Subtract(_lastCommunicationAt).TotalSeconds;

                            if (secondsSinceLastResponse > ICE_CONNECTED_NO_COMMUNICATIONS_TIMEOUT_SECONDS)
                            {
                                logger.LogWarning($"No packets have been received from {RemoteEndPoint} within the last {secondsSinceLastResponse:#} seconds, closing session.");
                                Close("Inactivity timeout.");
                            }
                        }
                    }
                    else
                    {
                        if (_remoteIceCandidates.Count() > 0 && LocalIceCandidates != null)
                        {
                            foreach (var localIceCandidate in LocalIceCandidates.Where(x => x.IsStunLocalExchangeComplete == false && x.StunConnectionRequestAttempts < MAXIMUM_STUN_CONNECTION_ATTEMPTS))
                            {
                                localIceCandidate.StunConnectionRequestAttempts++;

                                // ToDo: Include srflx and relay addresses.

                                // Only supporting UDP candidates at this stage.
                                foreach (var remoteIceCandidate in RemoteIceCandidates.Where(x => x.Transport.ToLower() == "udp" && x.NetworkAddress.NotNullOrBlank() && x.HasConnectionError == false))
                                {
                                    try
                                    {
                                        IPAddress remoteAddress = IPAddress.Parse(remoteIceCandidate.NetworkAddress);

                                        logger.LogDebug($"Sending authenticated STUN binding request {localIceCandidate.StunConnectionRequestAttempts} from {rtpChannel.RTPLocalEndPoint} to WebRTC peer at {remoteIceCandidate.NetworkAddress}:{remoteIceCandidate.Port}.");

                                        string localUser = LocalIceUser;

                                        STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                        stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                        stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + localUser);
                                        stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                        stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));
                                        byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

                                        rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, new IPEndPoint(IPAddress.Parse(remoteIceCandidate.NetworkAddress), remoteIceCandidate.Port), stunReqBytes);

                                        localIceCandidate.LastSTUNSendAt = DateTime.Now;
                                    }
                                    catch (System.Net.Sockets.SocketException sockExcp)
                                    {
                                        logger.LogWarning($"SocketException sending STUN request to {remoteIceCandidate.NetworkAddress}:{remoteIceCandidate.Port}, removing candidate. {sockExcp.Message}");
                                        remoteIceCandidate.HasConnectionError = true;
                                    }
                                }
                            }
                        }
                    }
                }

                if (!IsClosed)
                {
                    var interval = GetNextStunCheckInterval(STUN_CHECK_BASE_PERIOD_MILLISECONDS);

                    if (m_stunChecksTimer == null)
                    {
                        m_stunChecksTimer = new Timer(SendStunConnectivityChecks, null, interval, interval);
                    }
                    else
                    {
                        m_stunChecksTimer.Change(interval, interval);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SendStunConnectivityCheck. " + excp);
                //m_stunChecksTimer?.Dispose();
            }
        }

        /// <summary>
        /// Processes a STUN message received from a remote party.
        /// </summary>
        /// <param name="stunMessage">The received STUN message.</param>
        /// <param name="remoteEndPoint">The remote end point the message was received from.</param>
        private void ProcessStunMessage(STUNv2Message stunMessage, IPEndPoint remoteEndPoint)
        {
            var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

            //logger.LogDebug("STUN message received from remote " + remoteEndPoint + " " + stunMessage.Header.MessageType + ".");

            if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
            {
                STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);

                // ToDo: Check authentication.

                string localIcePassword = LocalIcePassword;
                byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(localIcePassword, true);
                //iceCandidate.LocalRtpSocket.SendTo(stunRespBytes, remoteEndPoint);
                rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunRespBytes);

                //iceCandidate.LastStunRequestReceivedAt = DateTime.Now;
                //iceCandidate.IsStunRemoteExchangeComplete = true;

                if (RemoteEndPoint == null)
                {
                    RemoteEndPoint = remoteEndPoint;
                    SetDestination(SDPMediaTypesEnum.audio, RemoteEndPoint, RemoteEndPoint);
                    //OnIceConnected?.Invoke(iceCandidate, remoteEndPoint);
                    IceConnectionState = IceConnectionStatesEnum.Connected;
                }

                if (_remoteIceCandidates != null && !_remoteIceCandidates.Any(x =>
                     (x.NetworkAddress == remoteEndPoint.Address.ToString() || x.RemoteAddress == remoteEndPoint.Address.ToString()) &&
                     (x.Port == remoteEndPoint.Port || x.RemotePort == remoteEndPoint.Port)))
                {
                    // This STUN request has come from a socket not in the remote ICE candidates list. Add it so we can send our STUN binding request to it.
                    IceCandidate remoteIceCandidate = new IceCandidate("udp", remoteEndPoint.Address, remoteEndPoint.Port, IceCandidateTypesEnum.host);
                    logger.LogDebug("Adding missing remote ICE candidate for " + remoteEndPoint + ".");
                    _remoteIceCandidates.Add(remoteIceCandidate);

                    // Some browsers require a STUN binding request from our end before the DTLS handshake will be initiated.
                    // The STUN connectivity checks are already scheduled but we can speed things up by sending a binding
                    // request immediately.
                    SendStunConnectivityChecks(null);
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
            {
                // TODO: What needs to be done here?

                //if (_turnServerEndPoint != null && remoteEndPoint.ToString() == _turnServerEndPoint.ToString())
                //{
                //    if (iceCandidate.IsGatheringComplete == false)
                //    {
                //        var reflexAddressAttribute = stunMessage.Attributes.FirstOrDefault(y => y.AttributeType == STUNv2AttributeTypesEnum.XORMappedAddress) as STUNv2XORAddressAttribute;

                //        if (reflexAddressAttribute != null)
                //        {
                //            iceCandidate.StunRflxIPEndPoint = new IPEndPoint(reflexAddressAttribute.Address, reflexAddressAttribute.Port);
                //            iceCandidate.IsGatheringComplete = true;

                //            logger.LogDebug("ICE gathering complete for local socket " + iceCandidate.RtpChannel.RTPLocalEndPoint + ", rflx address " + iceCandidate.StunRflxIPEndPoint + ".");
                //        }
                //        else
                //        {
                //            iceCandidate.IsGatheringComplete = true;

                //            logger.LogDebug("The STUN binding response received on " + iceCandidate.RtpChannel.RTPLocalEndPoint + " from " + remoteEndPoint + " did not have an XORMappedAddress attribute, rlfx address can not be determined.");
                //        }
                //    }
                //}
                //else
                //{
                //    iceCandidate.LastStunResponseReceivedAt = DateTime.Now;

                //    if (iceCandidate.IsStunLocalExchangeComplete == false)
                //    {
                //        iceCandidate.IsStunLocalExchangeComplete = true;
                //        logger.LogDebug("WebRTC client STUN exchange complete for call " + CallID + ", candidate local socket " + iceCandidate.RtpChannel.RTPLocalEndPoint + ", remote socket " + remoteEndPoint + ".");

                //        SetIceConnectionState(IceConnectionStatesEnum.Connected);
                //    }
                //}
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
            {
                logger.LogWarning($"A STUN binding error response was received from {remoteEndPoint}.");
            }
            else
            {
                logger.LogWarning($"An unrecognised STUN request was received from {remoteEndPoint}.");
            }
        }

        /// <summary>
        /// Gets a pseudo-randomised interval for the next STUN check period.
        /// </summary>
        /// <param name="baseInterval">The base check interval to randomise.</param>
        /// <returns>A value in milliseconds to wait before performing the next STUN check.</returns>
        private int GetNextStunCheckInterval(int baseInterval)
        {
            return Crypto.GetRandomInt((int)(STUN_CHECK_LOW_RANDOMISATION_FACTOR * baseInterval),
                (int)(STUN_CHECK_HIGH_RANDOMISATION_FACTOR * baseInterval));
        }

        private void AllocateTurn(IceCandidate iceCandidate)
        {
            try
            {
                var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

                if (iceCandidate.TurnAllocateAttempts >= MAXIMUM_TURN_ALLOCATE_ATTEMPTS)
                {
                    logger.LogDebug("TURN allocation for local socket " + iceCandidate.NetworkAddress + " failed after " + iceCandidate.TurnAllocateAttempts + " attempts.");

                    iceCandidate.IsGatheringComplete = true;
                }
                else
                {
                    iceCandidate.TurnAllocateAttempts++;

                    //logger.LogDebug("Sending STUN connectivity check to client " + client.SocketAddress + ".");

                    STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.Allocate);
                    stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Lifetime, 3600));
                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.RequestedTransport, STUNv2AttributeConstants.UdpTransportType));   // UDP
                    byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);
                    rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceCandidate.TurnServer.ServerEndPoint, stunReqBytes);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception AllocateTurn. " + excp);
            }
        }

        private void CreateTurnPermissions()
        {
            try
            {
                var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

                var localTurnIceCandidate = (from cand in LocalIceCandidates where cand.TurnRelayIPEndPoint != null select cand).First();
                var remoteTurnCandidate = (from cand in RemoteIceCandidates where cand.CandidateType == IceCandidateTypesEnum.relay select cand).First();

                // Send create permission request
                STUNv2Message turnPermissionRequest = new STUNv2Message(STUNv2MessageTypesEnum.CreatePermission);
                turnPermissionRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.ChannelNumber, (ushort)3000));
                turnPermissionRequest.Attributes.Add(new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORPeerAddress, remoteTurnCandidate.Port, IPAddress.Parse(remoteTurnCandidate.NetworkAddress)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Nonce)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Realm)));

                MD5 md5 = new MD5CryptoServiceProvider();
                byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username + ":" + localTurnIceCandidate.TurnServer.Realm + ":" + localTurnIceCandidate.TurnServer.Password));

                byte[] turnPermissionReqBytes = turnPermissionRequest.ToByteBuffer(hmacKey, false);
                //localTurnIceCandidate.LocalRtpSocket.SendTo(turnPermissionReqBytes, localTurnIceCandidate.TurnServer.ServerEndPoint);
                rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, localTurnIceCandidate.TurnServer.ServerEndPoint, turnPermissionReqBytes);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateTurnPermissions. " + excp);
            }
        }
    }
}
