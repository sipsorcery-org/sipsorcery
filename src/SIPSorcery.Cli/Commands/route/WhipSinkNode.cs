//-----------------------------------------------------------------------------
// Filename: WhipSinkNode.cs
//
// Description: A WebRTC egress edge for the "route" verb: publishes the graph's
// media to a WHIP (WebRTC-HTTP Ingestion Protocol) endpoint over a single peer
// connection carrying an audio track (Opus by default, or PCMU/PCMA via
// --audio-codec) and a video track (H264). Audio frames are relayed onto the audio
// track and video frames onto the video track, both still ENCODED - the graph
// repacketises, it does not transcode.
//
// The WHIP signalling itself (offer / POST / answer / DELETE) is delegated to the
// library's WhipWhepClient; this node only configures the peer connection's tracks,
// relays the graph frames, and waits for the connection to come up.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
// 27 Jun 2026	Aaron Clauson	Delegated the WHIP signalling to the library WhipWhepClient.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class WhipSinkNode : ISinkNode
{
    private readonly string _url;
    private readonly AudioFormat _audioFormat;
    private readonly string? _token;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;
    private readonly RTCPeerConnection _pc = new();
    private readonly WhipWhepClient _whip = new();

    private volatile bool _connected;
    private int _framesSent;
    private long _bytesSent;
    private int _dropped;

    public WhipSinkNode(string url, AudioFormat audioFormat, string? token, int timeoutSeconds, ILogger logger)
    {
        _url = url;
        _audioFormat = audioFormat;
        _token = token;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;
    }

    public string Describe() => $"whip:{_url}";

    public async Task StartAsync(CancellationToken ct)
    {
        // Send-only audio (the --audio-codec, Opus by default) and H264 video, offered as the exact format
        // the source produces so the payloads relay unchanged (repacketise, not transcode);
        // packetization-mode=1 lets the large H264 NAL units be fragmented across RTP packets.
        _pc.addTrack(new MediaStreamTrack(new List<AudioFormat> { _audioFormat }, MediaStreamStatusEnum.SendOnly));
        _pc.addTrack(new MediaStreamTrack(new List<VideoFormat> { RouteVideoFormats.H264 }, MediaStreamStatusEnum.SendOnly));

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

        // Do the WHIP offer / POST / answer exchange (delegated to the library client), bounded by the timeout.
        try
        {
            using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            publishCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
            await _whip.PublishAsync(_pc, _url, _token, publishCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new EdgeException($"The whip request to {_url} did not complete within {_timeoutSeconds}s.");
        }
        catch (Exception excp) when (excp is not EdgeException)
        {
            throw new EdgeException($"The whip publish to {_url} failed: {excp.Message}");
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

        // Best effort WHIP session teardown (DELETE the resource) then close the peer connection.
        await _whip.DeleteAsync(_token).ConfigureAwait(false);

        try { _pc.Close("route whip sink disposed"); } catch { /* best effort */ }
        _whip.Dispose();
    }
}
