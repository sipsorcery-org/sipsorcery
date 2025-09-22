//-----------------------------------------------------------------------------
// Filename: STUNClientExtensions.cs
//
// Description: STUN client extension methods.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 21 Sep 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net;

public static class STUNClientExtensions
{
    public const int DEFAULT_STUN_RESOLVE_TIMEOUT_SECONDS = 5;

    /// <summary>
    /// An extension method for <see cref="MediaStream"/> that attempts to use a STUN server to set the 
    /// reflexive (typically public) IP address for media stream's RTP channel.
    /// </summary>
    /// <remarks>
    /// Using this approach is NOT recommended. It typically causes more problems than it solves to mess with
    /// SDP connection IP address. If one end of a SIP call is on a public IP address it will be able to deal
    /// with private SDP IP addresses. Setting a public connection IP address can result in the NAT handling
    /// mechanisms being skipped on the assumption the generating agent is also on a public IP address.
    /// </remarks>
    /// <param name="mediaStream">The media stream to attempt to set the IP address for.</param>
    /// <param name="stunClient">The STUN client to use to determine the RTP socket's reflexive IP address.</param>
    /// <param name="ct">A cancellation token that can be used to abort the attempt or session.</param>
    /// <param name="timeoutSeconds">The maximum number of seconds to wait when attempting to establish the reflexive IP address.</param>
    /// <returns>A reference to the media stream.</returns>
    public static async Task<MediaStream> UseStun(this MediaStream mediaStream,
        STUNClient stunClient,
        CancellationToken ct,
        ILogger logger,
        int timeoutSeconds = DEFAULT_STUN_RESOLVE_TIMEOUT_SECONDS)
    {
        var iceServer = await stunClient.ResolveStunServer(timeoutSeconds * 1000);

        if (iceServer == null)
        {
            logger?.LogWarning($"The STUN client was unable to resolve a server to use.");

            return mediaStream;
        }
        else
        {
            logger?.LogDebug("Using ICE server {iceServerUri} -> {iceServerEndPoint} to get public IP address.", iceServer.Uri, iceServer.ServerEndPoint);

            var rtpPublicEndPoint = await STUNClient.GetPublicIPEndPointForSocketAsync(iceServer.ServerEndPoint, mediaStream.GetRTPChannel());

            if(rtpPublicEndPoint != null)
            {
                logger?.LogDebug("STUN client resolved RTP channel server reflexive RTP socket to {rtpPublicEndPoint}.", rtpPublicEndPoint);

                mediaStream.GetRTPChannel().RTPSrflxEndPoint = rtpPublicEndPoint;
            }
            else
            {
                logger?.LogWarning($"STUN client failed to get server reflexive RTP end point.");
            }
        }

        return mediaStream;
    }
}
