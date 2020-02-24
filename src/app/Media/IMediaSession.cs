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
        /// Fired when the session description changes.
        /// </summary>
        event Action<SDP> SessionMediaChanged;

        /// <summary>
        /// Fired when the RTP channel is closed.
        /// </summary>
        event Action<string> OnRtpClosed;

        bool IsOnLocalHold { get; }
        bool IsOnRemoteHold { get; }
        RTCSessionDescription localDescription { get; }
        RTCSessionDescription remoteDescription { get; }

        Task<SDP> createOffer(RTCOfferOptions options);
        void setLocalDescription(RTCSessionDescription sessionDescription);

        Task<SDP> createAnswer(RTCAnswerOptions options);
        void setRemoteDescription(RTCSessionDescription sessionDescription);

        //Task OfferAnswered(string remoteSDP);

        //Task<SDP> AnswerOffer(string remoteSDP);
        //Task<SDP> RemoteReInvite(string remoteSDP);

        void PutOnHold();
        void TakeOffHold();

        void Close();
    }
}