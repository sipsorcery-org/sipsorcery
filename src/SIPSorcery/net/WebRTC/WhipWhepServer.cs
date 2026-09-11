//-----------------------------------------------------------------------------
// Filename: WhipWhepServer.cs
//
// Description: Server-side helper for the WHIP (WebRTC-HTTP Ingestion Protocol,
// RFC 9725) and the symmetric WHEP (WebRTC-HTTP Egress Protocol) signalling: it
// applies a remote SDP offer to a peer connection the caller has configured and
// returns the SDP answer to put in the HTTP 201 response body.
//
// It is the mirror of WhipWhepClient and deliberately transport-agnostic: the caller
// owns the HTTP server (a raw HttpListener, ASP.NET Core, ...), checks any bearer
// token, and tracks the session resource URLs so a DELETE can close the matching peer
// connection. The library only performs the SDP offer/answer.
//
// The caller configures the peer connection for the session BEFORE calling:
//   WHEP (egress):  pc.addTrack(send-only tracks)            - the media to serve a viewer.
//   WHIP (ingestion): wire pc.OnAudioFrameReceived etc.      - to consume a publisher's media.
//
//   var pc = new RTCPeerConnection();
//   pc.addTrack(new MediaStreamTrack(..., MediaStreamStatusEnum.SendOnly));   // WHEP egress
//   string answerSdp = await WhipWhepServer.AnswerAsync(pc, offerSdp);
//   // return: HTTP 201 Created, body = answerSdp, header Location: /resource/{id}
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Server-side WHIP / WHEP signalling helper: applies a remote SDP offer to a caller-configured peer
    /// connection and returns the SDP answer. Transport-agnostic - see the file header for usage.
    /// </summary>
    public static class WhipWhepServer
    {
        private static readonly ILogger logger = LogFactory.CreateLogger("SIPSorcery.Net.WhipWhepServer");

        /// <summary>
        /// Applies the remote <paramref name="offerSdp"/> to the (already track-configured) peer connection
        /// and returns the SDP answer to send back in the HTTP response.
        /// </summary>
        /// <param name="pc">The peer connection the caller has configured with the tracks for the session.</param>
        /// <param name="offerSdp">The SDP offer received in the WHIP/WHEP HTTP POST body.</param>
        /// <param name="waitForIceGathering">
        /// When true (the default) the answer includes the gathered ICE candidates so a single HTTP
        /// response completes signalling (non-trickle). Turn it off only for a localhost server, where the
        /// host candidates are present immediately and there is nothing to wait for.
        /// </param>
        /// <returns>The SDP answer to return in the HTTP 201 response body.</returns>
        public static async Task<string> AnswerAsync(RTCPeerConnection pc, string offerSdp, bool waitForIceGathering = true)
        {
            if (pc == null)
            {
                throw new ArgumentNullException(nameof(pc));
            }

            if (string.IsNullOrWhiteSpace(offerSdp))
            {
                throw new ArgumentException("The WHIP/WHEP offer SDP was empty.", nameof(offerSdp));
            }

            logger.LogTrace("WHIP/WHEP server applying offer SDP:\n{Sdp}", offerSdp);

            var setResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = offerSdp
            });
            if (setResult != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException($"The WHIP/WHEP offer could not be applied: {setResult}.");
            }

            // X_WaitForIceGatheringToComplete makes the answer SDP carry the gathered candidates, the same
            // way WhipWhepClient builds its offer - so the single HTTP response is the whole exchange.
            var answer = pc.createAnswer(new RTCAnswerOptions { X_WaitForIceGatheringToComplete = waitForIceGathering });
            await pc.setLocalDescription(answer).ConfigureAwait(false);

            logger.LogTrace("WHIP/WHEP server answer SDP:\n{Sdp}", answer.sdp);

            return answer.sdp;
        }
    }
}
