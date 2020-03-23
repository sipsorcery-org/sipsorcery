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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate int DoDtlsHandshakeDelegate(RTCPeerConnection rtcPeerConnection);

    /// <summary>
    /// Represents a WebRTC RTCPeerConnection.
    /// </summary>
    /// <remarks>
    /// Interface is defined in https://www.w3.org/TR/webrtc/#interface-definition.
    /// The Session Description offer/answer mechanisms are detailed in
    /// https://tools.ietf.org/html/draft-ietf-rtcweb-jsep-26 (or later if the
    /// draft has been updated).
    /// </remarks>
    public class RTCPeerConnection : RTPSession //, IRTCPeerConnection
    {
        // SDP constants.
        private const string RTP_MEDIA_PROFILE = "RTP/SAVP";
        private const string RTCP_MUX_ATTRIBUTE = "a=rtcp-mux";       // Indicates the media announcement is using multiplexed RTCP.
        private const string SETUP_OFFER_ATTRIBUTE = "a=setup:actpass"; // Indicates the media announcement DTLS negotiation state is active/passive.
        private const string SETUP_ANSWER_ATTRIBUTE = "a=setup:passive"; // Indicates the media announcement DTLS negotiation state is passive.
        private const string MEDIA_GROUPING = "BUNDLE 0 1";

        private static ILogger logger = Log.Logger;

        public string SessionID { get; private set; }
        public string SdpSessionID;
        public string LocalSdpSessionID;
        //public string LocalIceUser;
        //public string LocalIcePassword;
        //public string RemoteIceUser;
        //public string RemoteIcePassword;
        //public DateTime IceNegotiationStartedAt;
        //public List<IceCandidate> LocalIceCandidates;
        //public RTCIceConnectionState IceConnectionState = RTCIceConnectionState.@new;

        //private List<IceCandidate> _remoteIceCandidates = new List<IceCandidate>();
        //public List<IceCandidate> RemoteIceCandidates
        //{
        //    get { return _remoteIceCandidates; }
        //}

        //public bool IsConnected
        //{
        //    get { return IceConnectionState == RTCIceConnectionState.connected; }
        //}

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
        /// Constructor to create a new RTC peer connection instance.
        /// </summary>
        /// <param name="configuration">Optional. </param>
        public RTCPeerConnection(RTCConfiguration configuration) :
            base(true, true, true)
        {
            //_dtlsCertificateFingerprint = dtlsFingerprint;
            //_offerAddresses = offerAddresses;
            //_turnServerEndPoint = turnServerEndPoint;

            SessionID = Guid.NewGuid().ToString();
            LocalSdpSessionID = Crypto.GetRandomInt(5).ToString();
            IceSession = new IceSession(GetRtpChannel(SDPMediaTypesEnum.audio));

            OnRtpClosed += Close;
            OnRtcpBye += Close;
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
        public async override Task setLocalDescription(RTCSessionDescriptionInit description)
        {
            await base.setLocalDescription(description).ConfigureAwait(false);

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
        public async override Task setRemoteDescription(RTCSessionDescriptionInit description)
        {
            await base.setRemoteDescription(description).ConfigureAwait(false);

            SDP remoteSdp = SDP.ParseSDPDescription(remoteDescription.sdp);

            var audioAnnounce = remoteSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).FirstOrDefault();
            if (audioAnnounce != null)
            {
                var audioTrack = new MediaStreamTrack(audioAnnounce.MediaID, SDPMediaTypesEnum.audio, true, audioAnnounce.MediaFormats);
                addTrack(audioTrack);
            }

            var videoAnnounce = remoteSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).FirstOrDefault();
            if (videoAnnounce != null)
            {
                var videoTrack = new MediaStreamTrack(videoAnnounce.MediaID, SDPMediaTypesEnum.video, true, videoAnnounce.MediaFormats);
                addTrack(videoTrack);
            }

            SdpSessionID = remoteSdp.SessionId;
            //RemoteIceUser = remoteSdp.IceUfrag ?? remoteSdp.Media.First().IceUfrag;
            //RemoteIcePassword = remoteSdp.IcePwd ?? remoteSdp.Media.First().IcePwd;

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
        }

        /// <summary>
        /// Adds an ICE candidate to the list of remote party candidates.
        /// </summary>
        /// <param name="remoteIceCandidate">The remote party candidate to add.</param>
        //public void AppendRemoteIceCandidate(IceCandidate remoteIceCandidate)
        //{
        //    IPAddress candidateIPAddress = null;

        //    //foreach (var iceCandidate in remoteIceCandidates)
        //    //{
        //    //    logger.LogDebug("Appending remote ICE candidate " + iceCandidate.NetworkAddress + ":" + iceCandidate.Port + ".");
        //    //}

        //    if (remoteIceCandidate.Transport.ToLower() != "udp")
        //    {
        //        logger.LogDebug("Omitting remote non-UDP ICE candidate. " + remoteIceCandidate.RawString + ".");
        //    }
        //    else if (!IPAddress.TryParse(remoteIceCandidate.NetworkAddress, out candidateIPAddress))
        //    {
        //        logger.LogDebug("Omitting ICE candidate with unrecognised IP Address. " + remoteIceCandidate.RawString + ".");
        //    }
        //    //else if (candidateIPAddress.AddressFamily == AddressFamily.InterNetworkV6)
        //    //{
        //    //    logger.LogDebug("Omitting IPv6 ICE candidate. " + remoteIceCandidate.RawString + ".");
        //    //}
        //    else
        //    {
        //        // ToDo: Add srflx and relay endpoints as hosts as well.

        //        if (!_remoteIceCandidates.Any(x => x.NetworkAddress == remoteIceCandidate.NetworkAddress && x.port == remoteIceCandidate.port))
        //        {
        //            logger.LogDebug("Adding remote ICE candidate: " + remoteIceCandidate.type + " " + remoteIceCandidate.NetworkAddress + ":" + remoteIceCandidate.port + " (" + remoteIceCandidate.RawString + ").");
        //            _remoteIceCandidates.Add(remoteIceCandidate);
        //        }
        //    }

        //    // We now should have a remote ICE candidate to start the STUN dance with.
        //    SendStunConnectivityChecks(null);
        //}

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
                //IceConnectionState = RTCIceConnectionState.closed;
                IceSession.Close();
                m_stunChecksTimer.Dispose();
                CloseSession(reason);

                OnClose?.Invoke(reason);
            }
        }

        /// <summary>
        /// Generates the SDP for an offer that can be made to a remote peer.
        /// </summary>
        /// <remarks>
        /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcpeerconnection-createoffer.
        /// </remarks>
        /// <param name="options">Optional. If supplied the options will be sued to apply additional
        /// controls over the generated offer SDP.</param>
        public override async Task<RTCSessionDescriptionInit> createOffer(RTCOfferOptions options)
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
        /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcpeerconnection-createanswer.
        /// </remarks>
        /// <param name="options">Optional. If supplied the options will be used to apply additional
        /// controls over the generated answer SDP.</param>
        public override async Task<RTCSessionDescriptionInit> createAnswer(RTCAnswerOptions options)
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
        private Task<SDP> createBaseSdp()
        {
            DateTime startGatheringTime = DateTime.Now;

            //IceConnectionState = RTCIceConnectionState.@new;

            //await GetIceCandidatesAsync().ConfigureAwait(false);

            //logger.LogDebug($"ICE gathering completed for in {DateTime.Now.Subtract(startGatheringTime).TotalMilliseconds:#}ms, candidate count {LocalIceCandidates.Count}.");

            //IceConnectionState = IceConnectionStatesEnum.GatheringComplete;
            SDP offerSdp = new SDP(IPAddress.Loopback);
            offerSdp.SessionId = LocalSdpSessionID;

            bool haveIceCandidatesBeenAdded = false;
            string localIceCandidateString = null;

            //if (LocalIceCandidates != null)
            //{
            //    foreach (var iceCandidate in LocalIceCandidates)
            //    {
            //        localIceCandidateString += iceCandidate.ToString();
            //    }
            //}

            // Add a bundle attribute. Indicates that audio and video sessions will be multiplexed
            // on a single RTP socket.
            if (AudioLocalTrack != null && VideoLocalTrack != null)
            {
                offerSdp.Group = MEDIA_GROUPING;
            }

            // The media is being multiplexed so the audio and video RTP channel is the same.
            var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

            // --- Audio announcement ---
            if (AudioLocalTrack != null && rtpChannel != null)
            {
                SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                    SDPMediaTypesEnum.audio,
                    rtpChannel.RTPPort,
                    AudioLocalTrack.Capabilties);

                audioAnnouncement.Transport = RTP_MEDIA_PROFILE;

                //if (!haveIceCandidatesBeenAdded)
                //{
                //    audioAnnouncement.IceCandidates = LocalIceCandidates;
                //    haveIceCandidatesBeenAdded = true;
                //}

                audioAnnouncement.Connection = new SDPConnectionInformation(IPAddress.Any);
                //audioAnnouncement.IceUfrag = LocalIceUser;
                //audioAnnouncement.IcePwd = LocalIcePassword;
                audioAnnouncement.DtlsFingerprint = _dtlsCertificateFingerprint;
                audioAnnouncement.AddExtra(RTCP_MUX_ATTRIBUTE);
                audioAnnouncement.MediaStreamStatus = AudioLocalTrack.Transceiver.Direction;
                audioAnnouncement.MediaID = AudioLocalTrack.Transceiver.MID;

                offerSdp.Media.Add(audioAnnouncement);
            }

            // --- Video announcement ---
            if (VideoLocalTrack != null && rtpChannel != null)
            {
                SDPMediaAnnouncement videoAnnouncement = new SDPMediaAnnouncement(
                    SDPMediaTypesEnum.video,
                    rtpChannel.RTPPort,
                    VideoLocalTrack.Capabilties);

                videoAnnouncement.Transport = RTP_MEDIA_PROFILE;

                //if (!haveIceCandidatesBeenAdded)
                //{
                //    videoAnnouncement.IceCandidates = LocalIceCandidates;
                //    haveIceCandidatesBeenAdded = true;
                //}

                videoAnnouncement.Connection = new SDPConnectionInformation(IPAddress.Any);
                //videoAnnouncement.IceUfrag = LocalIceUser;
                //videoAnnouncement.IcePwd = LocalIcePassword;
                videoAnnouncement.DtlsFingerprint = _dtlsCertificateFingerprint;
                videoAnnouncement.AddExtra(RTCP_MUX_ATTRIBUTE);
                videoAnnouncement.MediaStreamStatus = VideoLocalTrack.Transceiver.Direction;
                videoAnnouncement.MediaID = VideoLocalTrack.Transceiver.MID;

                offerSdp.Media.Add(videoAnnouncement);
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
                        //ProcessStunMessage(stunMessage, remoteEP);
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
    }
}
