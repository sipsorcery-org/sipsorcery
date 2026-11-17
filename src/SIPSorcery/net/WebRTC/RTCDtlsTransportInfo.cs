//-----------------------------------------------------------------------------
// Filename: RTCDtlsTransportInfo.cs
//
// Description: The state of a peer connection's DTLS transport, with what's known
// about it, for the ondtlsstatechange event.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Nov 2026	Aaron Clauson	Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net
{
    /// <summary>
    /// The state of the DTLS transport, as https://www.w3.org/TR/webrtc/#rtcdtlstransportstate-enum has it.
    /// </summary>
    public enum RTCDtlsTransportState
    {
        /// <summary>DTLS has not started negotiating yet.</summary>
        @new,
        /// <summary>The handshake is under way (ICE has connected).</summary>
        connecting,
        /// <summary>The handshake completed and the remote certificate matched the fingerprint in their SDP.</summary>
        connected,
        /// <summary>The transport was closed: by a close_notify from the remote peer, or the peer connection closing.</summary>
        closed,
        /// <summary>The handshake failed, or the remote certificate didn't match their SDP's fingerprint.</summary>
        failed,
    }

    /// <summary>
    /// A peer connection's DTLS transport as it is now: its state, our role in the handshake, the SRTP
    /// protection profile it agreed, and why it failed if it did. Passed with
    /// <see cref="RTCPeerConnection.ondtlsstatechange"/>, and kept as <see cref="RTCPeerConnection.DtlsTransportInfo"/>.
    /// </summary>
    public sealed class RTCDtlsTransportInfo
    {
        /// <summary>The state.</summary>
        public RTCDtlsTransportState State { get; }

        /// <summary>True if we are the DTLS client (we sent a=setup:active), false if the server.</summary>
        public bool IsClient { get; }

        /// <summary>
        /// The SRTP protection profile the handshake agreed, as RFC 5764 names it ("SRTP_AES128_CM_HMAC_SHA1_80"),
        /// once connected; null before.
        /// </summary>
        public string SrtpProtectionProfile { get; }

        /// <summary>
        /// Why the transport failed (or closed): the handshake's error, "fingerprint mismatch", an exception's
        /// message, or the reason the peer connection closed. Null while it hasn't.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// The most recent DTLS alert other than a close_notify, from either side ("fatal bad_certificate"),
        /// or null if there has been none.
        /// </summary>
        public string LastAlert { get; }

        /// <summary>The fingerprint the remote peer's SDP gave for their certificate, as "sha-256 7C:1E:…".</summary>
        public string ExpectedFingerprint { get; }

        /// <summary>
        /// The fingerprint of the certificate the remote peer presented in the handshake, in the same algorithm as
        /// <see cref="ExpectedFingerprint"/>, once the handshake has completed; null before. On a mismatch, the two differ.
        /// </summary>
        public string RemoteFingerprint { get; }

        public RTCDtlsTransportInfo(RTCDtlsTransportState state, bool isClient, string srtpProtectionProfile = null,
            string reason = null, string lastAlert = null, string expectedFingerprint = null, string remoteFingerprint = null)
        {
            State = state;
            IsClient = isClient;
            SrtpProtectionProfile = srtpProtectionProfile;
            Reason = reason;
            LastAlert = lastAlert;
            ExpectedFingerprint = expectedFingerprint;
            RemoteFingerprint = remoteFingerprint;
        }

        public override string ToString() =>
            $"{State}, {(IsClient ? "client" : "server")}" +
            (SrtpProtectionProfile != null ? $", {SrtpProtectionProfile}" : "") +
            (Reason != null ? $", {Reason}" : "") +
            (LastAlert != null ? $", last alert {LastAlert}" : "");
    }
}
