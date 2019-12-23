﻿//-----------------------------------------------------------------------------
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
using System.Threading.Tasks;

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
        //NOTE: Methods return a Task for usage with third-party implementations 

        Task<string> CreateOffer(IPAddress destinationAddress = null);
        Task OfferAnswered(string remoteSDP);

        Task<string> AnswerOffer(string remoteSDP);
        Task<string> RemoteReInvite(string remoteSDP);

        void Close();

        event Action<string> SessionMediaChanged;
    }
}