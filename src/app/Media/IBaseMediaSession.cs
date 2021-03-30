//-----------------------------------------------------------------------------
// Filename: SDPMessageMediaFormat.cs
//
// Description: Contains enums and helper classes for common definitions
// and attributes used in SDP payloads.
//
// Author(s):
// Jacek Dzija
// Mateusz Greczek
//
// History:
// 30 Mar 2021 Jacek Dzija,Mateusz Greczek Added MSRP
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------
using System.Net;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.app.Media
{
    public interface IBaseMediaSession
    {
        /// <summary>
        /// Indicates whether the session has been closed.
        /// </summary>
        bool IsClosed { get; }

        /// <summary>
        /// The SDP description from the remote party describing
        /// their audio/video sending and receive capabilities.
        /// </summary>
        SDP RemoteDescription { get; }

        /// <summary>
        /// Creates a new SDP offer based on the local media tracks in the session.
        /// Calling this method does NOT change the state of the media tracks. It is
        /// safe to call at any time if a session description of the local media state is
        /// required.
        /// </summary> 
        /// <param name="connectionAddress">Optional. If set this address will be used
        /// as the Connection address in the SDP offer. If not set an attempt will be 
        /// made to determine the best matching address.</param>
        /// <returns>A new SDP offer representing the session's local media tracks.</returns>
        SDP CreateOffer(IPAddress connectionAddress);

        /// <summary>
        /// Sets the remote description. Calling this method can result in the local
        /// media tracks being disabled if not supported or setting the RTP/RTCP end points
        /// if they are.
        /// </summary>
        /// <param name="sdpType">Whether the SDP being set is an offer or answer.</param>
        /// <param name="sdp">The SDP description from the remote party.</param>
        /// <returns>If successful an OK enum result. If not an enum result indicating the 
        /// failure cause.</returns>
        SetDescriptionResultEnum SetRemoteDescription(SdpType sdpType, SDP sessionDescription);

        /// <summary>
        /// Generates an SDP answer to an offer based on the local media tracks. Calling
        /// this method does NOT result in any changes to the local tracks. To apply the
        /// changes the SetRemoteDescription method must be called.
        /// </summary>
        /// <param name="connectionAddress">Optional. If set this address will be used as 
        /// the SDP Connection address. If not specified the Operating System routing table
        /// will be used to lookup the address used to connect to the SDP connection address
        /// from the remote offer.</param>
        /// <returns>An SDP answer matching the offer and the local media tracks contained
        /// in the session.</returns>
        SDP CreateAnswer(IPAddress connectionAddress);

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
