//-----------------------------------------------------------------------------
// Filename: WhipSinkNode.cs
//
// Description: A WebRTC egress edge for the "route" verb: publishes the graph's
// media to a WHIP (WebRTC-HTTP Ingestion Protocol) endpoint over a single peer
// connection carrying an audio track (PCMU) and a video track (H264). Audio
// frames are relayed onto the audio track and video frames onto the video track,
// both still ENCODED - the graph repacketises, it does not transcode.
//
// This is the publishing counterpart to WhepSourceNode (the WHEP ingress edge):
// the same offer / POST / answer / ICE / DTLS / SRTP path, send-only. It makes
// "route --from sip:... --to whip:..." bridge a SIP caller into a WebRTC
// endpoint, optionally with a generated scope video (see AudioScopeTransform).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class WhipSinkNode : ISinkNode
{
    private const int H264_PAYLOAD_ID = 96;

    private readonly string _url;
    private readonly string? _token;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;
    private readonly RTCPeerConnection _pc = new();
    private readonly HttpClient _http;

    private Uri? _resource;
    private volatile bool _connected;
    private int _framesSent;
    private long _bytesSent;
    private int _dropped;

    public WhipSinkNode(string url, string? token, int timeoutSeconds, ILogger logger)
    {
        _url = url;
        _token = token;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    }

    public string Describe() => $"whip:{_url}";

    public async Task StartAsync(CancellationToken ct)
    {
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new EdgeException($"Could not parse the whip sink '{_url}' as an HTTP or HTTPS URL.");
        }

        // Send-only PCMU audio and H264 video (H264 because the public Broadcast Box test endpoint
        // rejects VP8). Audio is pinned to PCMU and video to H264 so the source's payloads relay
        // unchanged (repacketise, not transcode); packetization-mode=1 allows the large H264 NAL
        // units to be fragmented across RTP packets.
        _pc.addTrack(new MediaStreamTrack(new List<AudioFormat>
        {
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU)
        }, MediaStreamStatusEnum.SendOnly));

        _pc.addTrack(new MediaStreamTrack(new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.H264, H264_PAYLOAD_ID, parameters: "packetization-mode=1")
        }, MediaStreamStatusEnum.SendOnly));

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("whip sink peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                _connected = true;
                connected.TrySetResult(true);
            }
            else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed ||
                     state == RTCPeerConnectionState.disconnected)
            {
                _connected = false;
                connected.TrySetResult(false);
            }
        };

        // Gather all candidates up front so the single POST carries a complete offer (no trickle).
        var offer = _pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
        await _pc.setLocalDescription(offer).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent(offer.sdp, Encoding.UTF8, "application/sdp")
        };
        if (!string.IsNullOrWhiteSpace(_token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        HttpResponseMessage response;
        string responseBody;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception excp) when (excp is not EdgeException)
        {
            throw new EdgeException($"The whip request to {_url} failed: {excp.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string detail = responseBody.Length > 200 ? responseBody[..200] : responseBody;
                throw new EdgeException($"The whip endpoint {_url} returned HTTP {(int)response.StatusCode}. {detail}".TrimEnd());
            }

            if (response.Headers.Location != null)
            {
                _resource = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(endpointUri, response.Headers.Location);
            }

            var setAnswerResult = _pc.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = responseBody
            });
            if (setAnswerResult != SetDescriptionResultEnum.OK)
            {
                throw new EdgeException($"The whip SDP answer from {_url} could not be applied: {setAnswerResult}.");
            }
        }

        var completed = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), ct)).ConfigureAwait(false);
        if (completed != connected.Task || !await connected.Task.ConfigureAwait(false))
        {
            throw new EdgeException(completed == connected.Task
                ? $"The whip peer connection to {_url} failed (state {_pc.connectionState})."
                : $"The whip peer connection to {_url} did not reach connected within {_timeoutSeconds}s.");
        }

        _logger.LogDebug("whip sink connected to {Url}.", _url);
    }

    public void Write(MediaFrame frame)
    {
        if (!_connected || frame.Payload.Length == 0)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        try
        {
            if (frame.Kind == MediaKind.Audio)
            {
                _pc.SendAudio(frame.DurationRtpUnits, frame.Payload);
            }
            else
            {
                _pc.SendVideo(frame.DurationRtpUnits, frame.Payload);
            }

            Interlocked.Increment(ref _framesSent);
            Interlocked.Add(ref _bytesSent, frame.Payload.Length);
        }
        catch (Exception excp)
        {
            _logger.LogWarning("whip sink send failed: {Error}", excp.Message);
            Interlocked.Increment(ref _dropped);
        }
    }

    public SinkStats GetStats() => new(_framesSent, Interlocked.Read(ref _bytesSent), _dropped);

    public async ValueTask DisposeAsync()
    {
        _connected = false;

        // Best effort WHIP session teardown then close the peer connection.
        if (_resource != null)
        {
            try
            {
                using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, _resource);
                if (!string.IsNullOrWhiteSpace(_token))
                {
                    deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }
                await _http.SendAsync(deleteRequest, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception excp)
            {
                _logger.LogDebug("whip sink session delete error: {Error}", excp.Message);
            }
        }

        try { _pc.Close("route whip sink disposed"); } catch { /* best effort */ }
        _http.Dispose();
    }
}
