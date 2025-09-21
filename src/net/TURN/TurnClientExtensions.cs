//-----------------------------------------------------------------------------
// Filename: TurnClientExtensions.cs
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

using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Net;

public static class TurnClientExtensions
{
    public const int DEFAULT_TURN_ALLOCATION_TIMEOUT_SECONDS = 10;

    /// <summary>
    /// An extension method for <see cref="MediaStream"/> that attempts to use a TURN relay server end point
    /// for the media stream's RTP channel.
    /// </summary>
    /// <param name="mediaStream">The media stream to attempt to use a TURN relay server end point for.</param>
    /// <param name="rtpSession">The RTP session the media stream belongs to.</param>
    /// <param name="turnClient">The TURN client to use to establish and maintain a session with the TURN server.</param>
    /// <param name="ct">A cancellation token that can be used to abort the attempt or session.</param>
    /// <param name="timeoutSeconds">The maximum number of seconds to wait when attempting to establish a
    /// new connection.</param>
    /// <returns>A reference to the media stream.</returns>
    public static async Task<MediaStream> UseTurn(this MediaStream mediaStream,
        RTPSession rtpSession,
        TurnClient turnClient,
        CancellationToken ct,
        int timeoutSeconds = DEFAULT_TURN_ALLOCATION_TIMEOUT_SECONDS)
    {
        turnClient.SetRtpChannel(mediaStream.GetRTPChannel());

        var relayDestinationEndPoint = await turnClient.GetRelayEndPoint(timeoutSeconds * 1000, ct);

        if (relayDestinationEndPoint != null)
        {
            mediaStream.RtpRelayEndPoint = new TurnRelayEndPoint
            {
                RelayServerEndPoint = turnClient.IceServer.ServerEndPoint,
                RemotePeerRelayEndPoint = relayDestinationEndPoint
            };

            rtpSession.OnRemoteDescriptionChanged += (sdp) =>
            {
                var createPermissionResult = turnClient.CreatePermission(rtpSession.AudioStream.DestinationEndPoint);
            };
        }

        return mediaStream;
    }
}
