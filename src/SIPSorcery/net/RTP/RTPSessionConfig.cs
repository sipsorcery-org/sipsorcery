//-----------------------------------------------------------------------------
// Filename: RTPSessionConfig.cs
//
// Description: Represents an RTP session constituted of a single media stream. The session
// does not control the sockets as they may be shared by multiple sessions.
//
// Author(s):
// Kurt Kießling
//
// History:
// 30 Jul 2021  Kurt Kießling   Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum RtpSecureMediaOptionEnum
    {
        /// <summary>
        /// Secure media not used.
        /// </summary>
        None,

        /// <summary>
        /// Secure media controled by DtlsSrtp for WebRTC.
        /// </summary>
        DtlsSrtp,

        /// <summary>
        /// Secure media negotiated with SDP crypto attributes.
        /// </summary>
        SdpCryptoNegotiation,

        /// <summary>
        /// Best-effort SRTP with SDP crypto attributes (SDES): offers are RTP/AVP with a=crypto
        /// lines, so a far end without SRTP ignores them and answers plain RTP, while one with SRTP
        /// answers with a crypto line of its own and the media is encrypted. Offers are accepted
        /// with or without crypto: RTP/SAVP ones must have a usable suite (and are answered
        /// RTP/SAVP), RTP/AVP ones with crypto are answered with crypto, and ones without are plain
        /// RTP. Each media stream settles on SRTP or plain RTP on its own; see
        /// <see cref="MediaStream.IsSecurityContextReady"/> for which one did.
        /// </summary>
        SdpCryptoBestEffort,
    }

    public sealed class RtpSessionConfig
    {
        /// <summary>
        /// If true only a single RTP socket will be used for both audio
        /// and video (standard case for WebRTC). If false two separate RTP sockets will be used for
        /// audio and video (standard case for VoIP).
        /// </summary>
        public bool IsMediaMultiplexed { get; set; }

        /// <summary>
        /// If true RTCP reports will be multiplexed with RTP on a single channel.
        /// If false (standard mode) then a separate socket is used to send and receive RTCP reports.
        /// </summary>
        public bool IsRtcpMultiplexed { get; set; }

        /// <summary>
        /// Select type of secure media to use.
        /// </summary>
        public RtpSecureMediaOptionEnum RtpSecureMediaOption { get; set; }
        
        /// <summary>
        /// Optional. If specified this address will be used as the bind address for any RTP
        /// and control sockets created. Generally this address does not need to be set. The default behaviour
        /// is to bind to [::] or 0.0.0.0,d depending on system support, which minimises network routing
        /// causing connection issues.
        /// </summary>
        public IPAddress BindAddress { get; set; }
        
        /// <summary>
        /// Optional. If specified a single attempt will be made to bind the RTP socket
        /// on this port. It's recommended to leave this parameter as the default of 0 to let the Operating
        /// System select the port number.
        /// </summary>
        public int BindPort { get; set; }

        /// <summary>
        /// Optional. If specified, overwrites BindPort and calls the PortRange whenever an RTP-Port
        /// should be created.
        /// </summary>
        public PortRange RtpPortRange { get; set; }

        public bool IsSecure { get => RtpSecureMediaOption == RtpSecureMediaOptionEnum.DtlsSrtp; }

        public bool UseSdpCryptoNegotiation { get => RtpSecureMediaOption == RtpSecureMediaOptionEnum.SdpCryptoNegotiation; }

        public bool UseSdpCryptoBestEffort { get => RtpSecureMediaOption == RtpSecureMediaOptionEnum.SdpCryptoBestEffort; }

        /// <summary>
        /// Whether SDP crypto attributes (SDES) are used at all: mandatory or best effort.
        /// </summary>
        public bool UsesSdpCrypto { get => UseSdpCryptoNegotiation || UseSdpCryptoBestEffort; }
    }
}
