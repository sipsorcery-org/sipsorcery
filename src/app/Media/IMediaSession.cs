//-----------------------------------------------------------------------------
// Filename: IMediaSession.cs
//
// Description: An interface for managing the Media in a SIP session
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
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// Offering and Answering SDP messages so that it can be
    /// signaled to the other party using the SIPUserAgent.
    /// 
    /// The implementing class is responsible for ensuring that the client
    /// can send media to the other party including creating and managing
    /// the RTP streams and processing the audio and video.
    /// </summary>
    public interface IMediaSession
    {
        uint AudioTimestamp { get; }
        uint VideoTimestamp { get; }
        RTCSessionDescription localDescription { get; }
        RTCSessionDescription remoteDescription { get; }

        /// <summary>
        /// Fired when the RTP channel is closed.
        /// </summary>
        event Action<string> OnRtpClosed;

        /// <summary>
        /// Fired when a media RTP packet is received.
        /// </summary>
        event Action<SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;

        /// <summary>
        /// Fired when an RTP event (typically representing a DTMF tone) is
        /// detected.
        /// </summary>
        event Action<RTPEvent> OnRtpEvent;

        Task<SDP> createOffer(RTCOfferOptions options);
        void setLocalDescription(RTCSessionDescription sessionDescription);
        Task<SDP> createAnswer(RTCAnswerOptions options);
        void setRemoteDescription(RTCSessionDescription sessionDescription);

        Task SendDtmf(byte tone, CancellationToken token);
        void SendMedia(SDPMediaTypesEnum mediaType, uint samplePeriod, byte[] sample);

        void Close();
    }
}