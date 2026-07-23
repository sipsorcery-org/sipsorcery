//-----------------------------------------------------------------------------
// Filename: WhipWhepClient.cs
//
// Description: A convenience client for the WHIP (WebRTC-HTTP Ingestion Protocol,
// RFC 9725) and the symmetric WHEP (WebRTC-HTTP Egress Protocol) signalling. Both
// are the same minimal HTTP exchange: POST the local SDP offer to an endpoint URL
// (optionally with a bearer token), receive the SDP answer in the response body,
// note the resource URL from the Location header, and DELETE that resource to tear
// the session down.
//
// It is NOT a required component for using WebRTC; like WebRTCRestSignalingPeer it
// is a convenience wrapper around an RTCPeerConnection the caller has already
// configured with the tracks (and/or data channels) they want. WHIP and WHEP differ
// only in intent and the track directions the caller sets - send-only to publish
// (WHIP), recv-only to play (WHEP) - so a single client serves both; PublishAsync
// and PlayAsync are intent-revealing aliases for the identical exchange.
//
//   var pc = new RTCPeerConnection();
//   pc.addTrack(new MediaStreamTrack(..., MediaStreamStatusEnum.SendOnly));   // publish
//   using var whip = new WhipWhepClient();
//   await whip.PublishAsync(pc, "https://host/whip", bearerToken);            // POST offer, apply answer
//   // ... media flows once pc.connectionState reaches connected ...
//   await whip.DeleteAsync(bearerToken);                                       // tear down
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

#nullable disable

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Convenience client for the WHIP / WHEP HTTP signalling exchange. Wraps an RTCPeerConnection the
    /// caller has configured with the tracks they want; this class performs only the HTTP offer/answer
    /// exchange and the teardown DELETE. See the file header for usage.
    /// </summary>
    public class WhipWhepClient : IDisposable
    {
        private const string SDP_CONTENT_TYPE = "application/sdp";
        private const int ERROR_BODY_MAX_CHARS = 200;

        private static readonly ILogger logger = LogFactory.CreateLogger<WhipWhepClient>();

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        /// <summary>
        /// The session resource URL the server returned (its Location header), used by <see cref="DeleteAsync"/>
        /// to tear the session down. Null until a successful exchange that returned a Location header.
        /// </summary>
        public Uri ResourceUrl { get; private set; }

        /// <summary>Creates a client with its own internally managed HttpClient.</summary>
        public WhipWhepClient() : this(null)
        { }

        /// <param name="httpClient">
        /// An HttpClient to use (e.g. a shared, pooled instance), or null to create and own one internally.
        /// A caller-supplied client is not disposed by this class.
        /// </param>
        public WhipWhepClient(HttpClient httpClient)
        {
            _ownsHttpClient = httpClient == null;
            _httpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// WHIP: publishes the peer connection's media to the endpoint - POSTs the offer and applies the
        /// answer. Add the send-only tracks to <paramref name="pc"/> before calling, then await the
        /// connection coming up via pc.onconnectionstatechange.
        /// </summary>
        public Task PublishAsync(RTCPeerConnection pc, string endpointUrl, string bearerToken = null, CancellationToken ct = default)
            => ExchangeAsync(pc, endpointUrl, bearerToken, ct);

        /// <summary>
        /// WHEP: plays the endpoint's media into the peer connection - POSTs the offer and applies the
        /// answer. Add the recv-only tracks to <paramref name="pc"/> before calling. Identical exchange to
        /// <see cref="PublishAsync"/>; named separately to reveal intent.
        /// </summary>
        public Task PlayAsync(RTCPeerConnection pc, string endpointUrl, string bearerToken = null, CancellationToken ct = default)
            => ExchangeAsync(pc, endpointUrl, bearerToken, ct);

        private async Task ExchangeAsync(RTCPeerConnection pc, string endpointUrl, string bearerToken, CancellationToken ct)
        {
            if (pc == null)
            {
                throw new ArgumentNullException(nameof(pc));
            }

            if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"The WHIP/WHEP endpoint '{endpointUrl}' is not an absolute HTTP or HTTPS URL.", nameof(endpointUrl));
            }

            // Gather all candidates up front so the single POST carries a complete offer (WHIP/WHEP do not
            // trickle ICE over the HTTP exchange).
            var offer = pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
            await pc.setLocalDescription(offer).ConfigureAwait(false);

            logger.LogDebug("WHIP/WHEP posting SDP offer to {Endpoint}.", endpoint);
            logger.LogTrace("WHIP/WHEP offer SDP:\n{Sdp}", offer.sdp);

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(offer.sdp, Encoding.UTF8, SDP_CONTENT_TYPE);
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }

                using (var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    logger.LogTrace("WHIP/WHEP answer SDP ({StatusCode}):\n{Sdp}", response.StatusCode, body);

                    if (!response.IsSuccessStatusCode)
                    {
                        string detail = body != null && body.Length > ERROR_BODY_MAX_CHARS ? body.Substring(0, ERROR_BODY_MAX_CHARS) : body;
                        throw new ApplicationException($"The WHIP/WHEP endpoint {endpoint} returned HTTP {(int)response.StatusCode}. {detail}".TrimEnd());
                    }

                    // The resource URL (Location header) is how the session is later deleted; resolve a
                    // relative value against the endpoint.
                    if (response.Headers.Location != null)
                    {
                        ResourceUrl = response.Headers.Location.IsAbsoluteUri
                            ? response.Headers.Location
                            : new Uri(endpoint, response.Headers.Location);
                    }

                    var setResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.answer,
                        sdp = body
                    });
                    if (setResult != SetDescriptionResultEnum.OK)
                    {
                        throw new ApplicationException($"The WHIP/WHEP SDP answer from {endpoint} could not be applied: {setResult}.");
                    }
                }
            }

            logger.LogDebug("WHIP/WHEP answer applied for {Endpoint} (resource {ResourceUrl}).", endpoint, ResourceUrl);
        }

        /// <summary>
        /// Tears the session down by sending an HTTP DELETE to the resource URL the server returned. Best
        /// effort and a no-op when there is no resource URL (the server returned no Location header).
        /// </summary>
        public async Task DeleteAsync(string bearerToken = null, CancellationToken ct = default)
        {
            var resource = ResourceUrl;
            if (resource == null)
            {
                return;
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Delete, resource))
                {
                    if (!string.IsNullOrWhiteSpace(bearerToken))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                    }
                    await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                }
            }
            catch (Exception excp)
            {
                logger.LogDebug("WHIP/WHEP session delete failed: {Error}", excp.Message);
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
