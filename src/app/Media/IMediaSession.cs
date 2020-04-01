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
using System.Net;
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
        /// <summary>
        /// Indicates whether the session supports audio.
        /// </summary>
        bool HasAudio { get; }

        /// <summary>
        /// Indicates whether the session supports video.
        /// </summary>
        bool HasVideo { get; }

        /// <summary>
        /// Indicates whether the session has been closed.
        /// </summary>
        bool IsClosed { get; }

        /// <summary>
        /// The SDP description describing the local audio/video
        /// sending and receive capabilities.
        /// </summary>
        SDP LocalDescription { get; }

        /// <summary>
        /// The SDP description from the remote party describing
        /// their audio/video sending and receive capabilities.
        /// </summary>
        SDP RemoteDescription { get; }

        /// <summary>
        /// Fired when the RTP channel is closed.
        /// </summary>
        event Action<string> OnRtpClosed;

        /// <summary>
        /// Fired when an RTP event (typically representing a DTMF tone) is
        /// detected.
        /// </summary>
        event Action<RTPEvent, RTPHeader> OnRtpEvent;

        /// <summary>
        /// Creates a new SDP offer based on the local media tracks in the session.
        /// </summary>
        /// <param name="connectionAddress">Optional. If set this address will be used
        /// as the Connection address in the SDP offer. If not set an attempt will be 
        /// made to determine the best matching address.</param>
        /// <returns>A new SDP offer representing the session's local media tracks.</returns>
        SDP CreateOffer(IPAddress connectionAddress);

        /// <summary>
        /// Sets the local SDP description.
        /// </summary>
        /// <param name="sdp">The SDP to set as the local description.</param>
        void SetLocalDescription(SDP sdp);

        /// <summary>
        /// Generates an SDP answer to an offer based on the local media tracks. Calling
        /// this method does NOT result in any changes to the local tracks. To apply the
        /// changes the SetRemoteDescription method must be called.
        /// </summary>
        /// <param name="offer">The SDP offer to generate an answer for.</param>
        /// <returns>An SDP answer matching the offer and the local media tracks contained
        /// in the session.</returns>
        SDP CreateAnswer();

        /// <summary>
        /// Sets the remote description. Calling this method can result in the local
        /// media tracks being disabled if not supported or setting the RTP/RTCP end points
        /// if they are.
        /// </summary>
        /// <param name="sdp">The SDP description from the remote party.</param>
        /// <returns>If successful an OK enum result. If not an enum result indicating the 
        /// failure cause.</returns>
        SetDescriptionResultEnum SetRemoteDescription(SDP sessionDescription);

        /// <summary>
        /// Attempts to send a DTMF tone to the remote party.
        /// </summary>
        /// <param name="tone">The digit representing the DTMF tone to send.</param>
        /// <param name="ct">A cancellation token that should be set if the DTMF send should be 
        /// cancelled before completing. Depending on the duration a DTMF send can require 
        /// multiple RTP packets. This token can be used to cancel any further RTP packets
        /// being sent for the tone.</param>
        Task SendDtmf(byte tone, CancellationToken ct);

        /// <summary>
        /// Needs to be called prior to sending media. Performs any set up tasks such as 
        /// starting audio/video capture devices and starting RTCP reporting.
        /// </summary>
        Task Start();

        /// <summary>
        /// Closes the session. This will stop any audio/video capturing and rendering devices as
        /// well as the RTP and RTCP sessions and sockets.
        /// </summary>
        /// <param name="reason">Optional. A descriptive reason for closing the session.</param>
        void Close(string reason);
    }
}