//-----------------------------------------------------------------------------
// Filename: IRTCPeerConnection.cs
//
// Description: Contains the interface definition for the RTCPeerConnection
// class as defined by the W3C WebRTC specification. Should be kept up to 
// date with:
// https://www.w3.org/TR/webrtc/#interface-definition
//
// See also:
// https://tools.ietf.org/html/draft-ietf-rtcweb-jsep-25#section-3.5.4
//
// Author(s):
// Aaron Clauson
//
// History:
// 16 Mar 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum RTCSdpType
    {
        answer = 0,
        offer = 1,
        pranswer = 2,
        rollback = 3
    }

    public class RTCOfferOptions
    {
        /// <summary>
        /// If set it indicates that any available ICE candidates should NOT be added
        /// to the offer SDP. By default "host" candidates should always be available
        /// and will be added to the offer SDP.
        /// </summary>
        public bool X_ExcludeIceCandidates;
    }

    /// <summary>
    /// Options for creating an SDP answer.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dictionary-rtcofferansweroptions-members.
    /// </remarks>
    public class RTCAnswerOptions
    {
        /// If set it indicates that any available ICE candidates should NOT be added
        /// to the offer SDP. By default "host" candidates should always be available
        /// and will be added to the offer SDP.
        /// </summary>
        public bool X_ExcludeIceCandidates;
    }

    public class RTCSessionDescription
    {
        public RTCSdpType type;
        public SDP sdp;
    }

    /// <summary>
    /// The types of credentials for an ICE server.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#rtcicecredentialtype-enum.
    /// </remarks>
    public enum RTCIceCredentialType
    {
        password
    }

    /// <summary>
    /// Used to specify properties for a STUN or TURN server that can be used by an ICE agent.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#rtciceserver-dictionary.
    /// </remarks>
    public class RTCIceServer
    {
        public string urls;
        public string username;
        public RTCIceCredentialType credentialType;
        public string credential;
    }

    /// <summary>
    /// Determines which ICE candidates can be used for a peer connection.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcicetransportpolicy.
    /// </remarks>
    public enum RTCIceTransportPolicy
    {
        all,
        relay
    }

    /// <summary>
    /// Affects which media tracks are negotiated if the remote end point is not bundle aware.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcbundlepolicy.
    /// </remarks>
    public enum RTCBundlePolicy
    {
        balanced,
        max_compat,
        max_bundle
    }

    /// <summary>
    /// The RTCP multiplex options for ICE candidates. This option is currently redundant
    /// since the single option means RTCP multiplexing MUST be available or the SDP negotiation
    /// will fail.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcrtcpmuxpolicy.
    /// </remarks>
    public enum RTCRtcpMuxPolicy
    {
        require
    }

    /// <summary>
    /// Represents a fingerprint of a certificate used to authenticate WebRTC communications.
    /// </summary>
    public class RTCDtlsFingerprint
    {
        /// <summary>
        /// One of the hash function algorithms defined in the 'Hash function Textual Names' registry.
        /// </summary>
        public string algorithm;

        /// <summary>
        /// The value of the certificate fingerprint in lower-case hex string as expressed utilising 
        /// the syntax of 'fingerprint' in [RFC4572] Section 5.
        /// </summary>
        public string value;

        public override string ToString()
        {
            // FireFox wasn't happy unless the fingerprint hash was in upper case.
            return $"{algorithm} {value.ToUpper()}";
        }

        /// <summary>
        /// Attempts to parse the fingerprint fields from a string.
        /// </summary>
        /// <param name="str">The string to parse from.</param>
        /// <param name="fingerprint">If successful a fingerprint object.</param>
        /// <returns>True if a fingerprint was successfully parsed. False if not.</returns>
        public static bool TryParse(string str, out RTCDtlsFingerprint fingerprint)
        {
            fingerprint = null;

            if (string.IsNullOrEmpty(str))
            {
                return false;
            }
            else
            {
                int spaceIndex = str.IndexOf(' ');
                if (spaceIndex == -1)
                {
                    return false;
                }
                else
                {
                    string algStr = str.Substring(0, spaceIndex);
                    string val = str.Substring(spaceIndex + 1);

                    if (!DtlsUtils.IsHashSupported(algStr))
                    {
                        return false;
                    }
                    else
                    {
                        fingerprint = new RTCDtlsFingerprint
                        {
                            algorithm = algStr,
                            value = val
                        };
                        return true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Represents a certificate used to authenticate WebRTC communications.
    /// </summary>
    /// <remarks>
    /// TODO:
    /// From https://www.w3.org/TR/webrtc/#methods-4:
    /// "Implementations SHOULD store the sensitive keying material in a secure module safe from 
    /// same-process memory attacks."
    /// </remarks>
    public class RTCCertificate
    {
        /// <summary>
        /// The expires attribute indicates the date and time in milliseconds relative to 1970-01-01T00:00:00Z 
        /// after which the certificate will be considered invalid by the browser.
        /// </summary>
        public long expires
        {
            get
            {
                if (Certificate == null)
                {
                    return 0;
                }
                else
                {
                    return Certificate.NotAfter.GetEpoch();
                }
            }
        }

        public X509Certificate2 Certificate;

        public List<RTCDtlsFingerprint> getFingerprints()
        {
            return new List<RTCDtlsFingerprint> { DtlsUtils.Fingerprint(Certificate) };
        }
    }

    /// <summary>
    /// Defines the parameters to configure how a new RTCPeerConnection is created.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#rtcconfiguration-dictionary.
    /// </remarks>
    public class RTCConfiguration
    {
        public List<RTCIceServer> iceServers;
        public RTCIceTransportPolicy iceTransportPolicy;
        public RTCBundlePolicy bundlePolicy;
        public RTCRtcpMuxPolicy rtcpMuxPolicy;
        public List<RTCCertificate> certificates;

        /// <summary>
        /// Size of the pre-fetched ICE pool. Defaults to 0.
        /// </summary>
        public int iceCandidatePoolSize = 0;

        /// <summary>
        /// Optional. If specified this address will be used as the bind address for any RTP
        /// and control sockets created. Generally this address does not need to be set. The default behaviour
        /// is to bind to [::] or 0.0.0.0, depending on system support, which minimises network routing
        /// causing connection issues.
        /// </summary>
        public IPAddress X_BindAddress;

        /// <summary>
        /// Optional. If set to true the feedback profile set in the SDP offers and answers will be
        /// UDP/TLS/RTP/SAVPF instead of UDP/TLS/RTP/SAVP.
        /// </summary>
        public bool X_UseRtpFeedbackProfile;
    }

    /// <summary>
    /// Signalling states for a WebRTC peer connection.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcsignalingstate.
    /// </remarks>
    public enum RTCSignalingState
    {
        stable,
        have_local_offer,
        have_remote_offer,
        have_local_pranswer,
        have_remote_pranswer,
        closed
    }

    /// <summary>
    /// The states a peer connection transitions through.
    /// The difference between the IceConnectionState and the PeerConnectionState is somewhat subtle:
    /// - IceConnectionState: applies to the connection checks amongst ICE candidates and is
    ///   set as completed as soon as a local and remote candidate have set their nominated candidate,
    /// - PeerConnectionState: takes into account the IceConnectionState but also includes the DTLS
    ///   handshake and actions at the application layer such as a request to close the peer connection.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#rtcpeerconnectionstate-enum.
    /// </remarks>
    public enum RTCPeerConnectionState
    {
        closed,
        failed,
        disconnected,
        @new,
        connecting,
        connected
    }

    interface IRTCPeerConnection
    {
        //IRTCPeerConnection(RTCConfiguration configuration = null);
        RTCSessionDescriptionInit createOffer(RTCOfferOptions options = null);
        RTCSessionDescriptionInit createAnswer(RTCAnswerOptions options = null);
        Task setLocalDescription(RTCSessionDescriptionInit description);
        RTCSessionDescription localDescription { get; }
        RTCSessionDescription currentLocalDescription { get; }
        RTCSessionDescription pendingLocalDescription { get; }
        SetDescriptionResultEnum setRemoteDescription(RTCSessionDescriptionInit description);
        RTCSessionDescription remoteDescription { get; }
        RTCSessionDescription currentRemoteDescription { get; }
        RTCSessionDescription pendingRemoteDescription { get; }
        void addIceCandidate(RTCIceCandidateInit candidate = null);
        RTCSignalingState signalingState { get; }
        RTCIceGatheringState iceGatheringState { get; }
        RTCIceConnectionState iceConnectionState { get; }
        RTCPeerConnectionState connectionState { get; }
        bool canTrickleIceCandidates { get; }
        void restartIce();
        RTCConfiguration getConfiguration();
        void setConfiguration(RTCConfiguration configuration = null);
        void close();
        event Action onnegotiationneeded;
        event Action<RTCIceCandidate> onicecandidate;
        event Action<RTCIceCandidate, string> onicecandidateerror;
        event Action onsignalingstatechange;
        event Action<RTCIceConnectionState> oniceconnectionstatechange;
        event Action<RTCIceGatheringState> onicegatheringstatechange;
        event Action<RTCPeerConnectionState> onconnectionstatechange;

        // TODO: Extensions for the RTCMediaAPI
        // https://www.w3.org/TR/webrtc/#rtcpeerconnection-interface-extensions.
        //List<IRTCRtpSender> getSenders();
        //List<IRTCRtpReceiver> getReceivers();
        //List<RTCRtpTransceiver> getTransceivers();
        //RTCRtpSender addTrack(MediaStreamTrack track, param MediaStream[] streams);
        //void removeTrack(RTCRtpSender sender);
        //RTCRtpTransceiver addTransceiver((MediaStreamTrack or DOMString) trackOrKind,
        ////optional RTCRtpTransceiverInit init = {});
        //event ontrack;
    };

    /// <summary>
    /// The RTCRtpSender interface allows an application to control how a given MediaStreamTrack 
    /// is encoded and transmitted to a remote peer. When setParameters is called on an 
    /// RTCRtpSender object, the encoding is changed appropriately.
    /// </summary>
    /// <remarks>
    /// As specified at https://www.w3.org/TR/webrtc/#rtcrtpsender-interface.
    /// </remarks>
    public interface IRTCRtpSender
    {
        MediaStreamTrack track { get; }
        //readonly attribute RTCDtlsTransport? transport;
        //static RTCRtpCapabilities? getCapabilities(DOMString kind);
        //Task setParameters(RTCRtpSendParameters parameters);
        //RTCRtpSendParameters getParameters();
        //Task replaceTrack(MediaStreamTrack withTrack);
        //void setStreams(MediaStream... streams);
        //Task<RTCStatsReport> getStats();
    };

    /// <summary>
    /// The RTCRtpReceiver interface allows an application to inspect the receipt of a MediaStreamTrack.
    /// </summary>
    /// <remarks>
    /// As specified at https://www.w3.org/TR/webrtc/#rtcrtpreceiver-interface.
    /// </remarks>
    public interface IRTCRtpReceiver
    {
        MediaStreamTrack track { get; }
        //readonly attribute RTCDtlsTransport? transport;
        //static RTCRtpCapabilities? getCapabilities(DOMString kind);
        //RTCRtpReceiveParameters getParameters();
        //sequence<RTCRtpContributingSource> getContributingSources();
        //sequence<RTCRtpSynchronizationSource> getSynchronizationSources();
        //Task<RTCStatsReport> getStats();
    };
}
