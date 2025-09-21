//-----------------------------------------------------------------------------
// Filename: TurnRelayEndPoint.cs
//
// Description: TURN client extension methods.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Sep 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;

namespace SIPSorcery.Net;

public class TurnRelayEndPoint
{
    /// <summary>
    /// This is the socket on the TURN server that the client is using for setting up an allocation AND
    /// for sending and receiving data to/from the TURN server. The RTP channel needs to use this end
    /// point when it wants to send TURN relay packets that will be forwarded to the remote peer.
    /// </summary>
    public IPEndPoint RelayServerEndPoint { get; set; }

    /// <summary>
    /// Gets or sets the remote peer relay endpoint. This is the end point allocated on the TURN server
    /// that the remote peer will send its packets to. This end point needs to be using in the SDP offer/answer
    /// that is ent to the remote peer so it knows where to send its RTP packets.
    /// </summary>
    public IPEndPoint RemotePeerRelayEndPoint { get; set; }
}
