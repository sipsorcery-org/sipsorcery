//-----------------------------------------------------------------------------
// Filename: IMediaSession.cs
//
// Description: An interface for managing the Media in a SIP session
//
// Author(s):
// Yizchok G.
// Jacek Dzija
// Mateusz Greczek
//
// History:
// 12/23/2019	Yitzchok	  Created.
// 30 Mar 2021 Jacek Dzija,Mateusz Greczek Added MSRP
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.app.Media;
using SIPSorcery.Net;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// The type of the SDP packet being set.
    /// </summary>
    public enum SdpType
    {
        answer = 0,
        offer = 1
    }

    /// <summary>
    /// Offering and Answering SDP messages so that it can be
    /// signaled to the other party using the SIPUserAgent.
    /// 
    /// The implementing class is responsible for ensuring that the client
    /// can send media to the other party including creating and managing
    /// the RTP streams and processing the audio and video.
    /// </summary>
    public interface IMediaSession : IBaseMediaSession
    {
        /// <summary>
        /// Indicates whether the session supports audio.
        /// </summary>
        bool HasAudio { get; }

        /// <summary>
        /// Indicates whether the session supports video.
        /// </summary>
        bool HasVideo { get; }

        /// <summary>
        /// Set if the session has been bound to a specific IP address.
        /// Normally not required but some esoteric call or network set ups may need.
        /// </summary>
        IPAddress RtpBindAddress { get; }

        /// <summary>
        /// Fired when the RTP channel is closed.
        /// </summary>
        event Action<string> OnRtpClosed;

        /// <summary>
        /// Fired when an RTP event (typically representing a DTMF tone) is
        /// detected.
        /// </summary>
        event Action<IPEndPoint, RTPEvent, RTPHeader> OnRtpEvent;

        /// <summary>
        /// Sets the stream status on a local audio or video media track.
        /// </summary>
        /// <param name="kind">The type of the media track. Must be audio or video.</param>
        /// <param name="status">The stream status for the media track.</param>
        void SetMediaStreamStatus(SDPMediaTypesEnum kind, MediaStreamStatusEnum status);

        /// <summary>
        /// Attempts to send a DTMF tone to the remote party.
        /// </summary>
        /// <param name="tone">The digit representing the DTMF tone to send.</param>
        /// <param name="ct">A cancellation token that should be set if the DTMF send should be 
        /// cancelled before completing. Depending on the duration a DTMF send can require 
        /// multiple RTP packets. This token can be used to cancel any further RTP packets
        /// being sent for the tone.</param>
        Task SendDtmf(byte tone, CancellationToken ct);

    }
}