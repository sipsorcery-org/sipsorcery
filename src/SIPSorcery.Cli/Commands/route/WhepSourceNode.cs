//-----------------------------------------------------------------------------
// Filename: WhepSourceNode.cs
//
// Description: A live WebRTC ingress edge: connects to a WHEP endpoint (the same
// offer/POST/answer/ICE/DTLS/SRTP path as the "webrtc whep" verb) and emits each
// received, depacketised video frame into the graph as a MediaFrame. This is the
// transport edge that makes the graph more than a local toy: "route --from
// whep:<url> --to out.ivf" pulls a live stream off the network and records it,
// with no transcode - the encoded frames are forwarded straight to the sink.
//
// v0.1 routes video only; the audio track is still negotiated (so the answer
// matches a normal browser/SFU) but audio frames are not yet emitted.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
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

public sealed class WhepSourceNode : ISourceNode
{
    private const int VP8_PAYLOAD_ID = 96;
    private const int VP9_PAYLOAD_ID = 98;
    private const int H264_PAYLOAD_ID = 100;
    private const int H265_PAYLOAD_ID = 102;
    private const int AV1_PAYLOAD_ID = 104;

    private readonly string _url;
    private readonly string? _token;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;
    private readonly RTCPeerConnection _pc = new();
    private readonly HttpClient _http;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Uri? _resource;
    private long? _connectTimeMs;

    public event Action<MediaFrame>? OnFrame;

    public Task Completion => _completion.Task;

    public long? ConnectTimeMs => _connectTimeMs;

    public WhepSourceNode(string url, string? token, int timeoutSeconds, ILogger logger)
    {
        _url = url;
        _token = token;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    }

    public string Describe() => $"whep:{_url}";

    public async Task StartAsync(CancellationToken ct)
    {
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new EdgeException($"Could not parse the whep source '{_url}' as an HTTP or HTTPS URL.");
        }

        // Receive only tracks offering every codec the library can negotiate, so the server can match
        // whatever the publisher is sending. Audio is negotiated but not emitted in v0.1.
        _pc.addTrack(new MediaStreamTrack(new List<AudioFormat>
        {
            AudioCommonlyUsedFormats.OpusWebRTC,
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)
        }, MediaStreamStatusEnum.RecvOnly));

        _pc.addTrack(new MediaStreamTrack(new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID),
            new VideoFormat(VideoCodecsEnum.VP9, VP9_PAYLOAD_ID),
            new VideoFormat(VideoCodecsEnum.H264, H264_PAYLOAD_ID, parameters: "packetization-mode=1"),
            new VideoFormat(VideoCodecsEnum.H265, H265_PAYLOAD_ID),
            new VideoFormat(VideoCodecsEnum.AV1, AV1_PAYLOAD_ID)
        }, MediaStreamStatusEnum.RecvOnly));

        _pc.OnVideoFrameReceived += (_, timestamp, frame, format) =>
            OnFrame?.Invoke(new MediaFrame(frame, timestamp, format));

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("whep source peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                connected.TrySetResult(true);
            }
            else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed ||
                     state == RTCPeerConnectionState.disconnected)
            {
                connected.TrySetResult(false);
                // Once a connected session drops, signal the graph the source has ended.
                _completion.TrySetResult();
            }
        };

        // Gather all candidates up front so the single POST carries a complete offer (no trickle).
        var offer = _pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
        await _pc.setLocalDescription(offer).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();

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
            throw new EdgeException($"The whep request to {_url} failed: {excp.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string detail = responseBody.Length > 200 ? responseBody[..200] : responseBody;
                throw new EdgeException($"The whep endpoint {_url} returned HTTP {(int)response.StatusCode}. {detail}".TrimEnd());
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
                throw new EdgeException($"The whep SDP answer from {_url} could not be applied: {setAnswerResult}.");
            }
        }

        var completed = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), ct)).ConfigureAwait(false);
        if (completed != connected.Task || !await connected.Task.ConfigureAwait(false))
        {
            throw new EdgeException(completed == connected.Task
                ? $"The whep peer connection to {_url} failed (state {_pc.connectionState})."
                : $"The whep peer connection to {_url} did not reach connected within {_timeoutSeconds}s.");
        }

        _connectTimeMs = stopwatch.ElapsedMilliseconds;
        _logger.LogDebug("whep source connected to {Url} in {Ms}ms.", _url, _connectTimeMs);
    }

    public async ValueTask DisposeAsync()
    {
        // Best effort WHEP session teardown then close the peer connection.
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
                _logger.LogDebug("whep session DELETE failed: {Error}", excp.Message);
            }
        }

        _pc.Close("route whep source disposed");
        _http.Dispose();
        _completion.TrySetResult();
    }
}
