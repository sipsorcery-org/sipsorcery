//-----------------------------------------------------------------------------
// Filename: KeyFrameRequester.cs
//
// Description: Asks the far end for a key frame until video actually decodes.
// Attach it to any peer connection that RECEIVES video.
//
// Why every receiver needs this: a WebRTC sender emits a key frame when the
// session starts and then, as a rule, only when asked. Chrome's own getStats
// bears that out - a ten second session shows keyFramesEncoded: 1, whatever
// the codec. A receiver joins mid-stream, so the leading packets of that one
// key frame can be missed while the SRTP context is still settling, and when
// that happens there is no second key frame coming: the session runs to
// completion producing nothing. It is an all-or-nothing failure rather than
// degraded video, which is what makes it look like a codec problem.
//
// It surfaced as AV1 working roughly one run in five from a browser while the
// other codecs were fine. AV1 was not at fault; its key frames are simply the
// largest and span the most RTP packets, so they are the likeliest to be lost
// at the join. Every receiving verb has the same exposure.
//
// Two details that are easy to get wrong, both learned the hard way:
//
//   - The SENDER SSRC must be the one the far end already associates with us,
//     which is the SSRC our own RTCP receiver reports go out under
//     (RTCPSession.Ssrc). MediaStreamTrack.Ssrc on a local recvonly track is
//     never assigned, and a PLI carrying a sender SSRC of 0 is serialised,
//     SRTP protected, delivered - and silently ignored, with the far end's
//     pliCount staying at 0.
//
//   - The first request has to be driven by the arrival of RTP, not by a
//     timer. A timer set from "connected" can fire before any RTP has arrived,
//     and there is then no stream to name in the report.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2026	Aaron Clauson	Created, Wexford, Ireland, generalised from the browser receiver.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Common;

public sealed class KeyFrameRequester : IDisposable
{
    /// <summary>How long to wait between requests while nothing decodes.</summary>
    private const int RETRY_INTERVAL_MS = 700;

    /// <summary>How many requests to make before giving up and letting the run report the silence.</summary>
    private const int MAX_REQUESTS = 8;

    private readonly RTCPeerConnection _pc;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;

    private uint _remoteVideoSsrc;
    private int _videoDecoded;

    private KeyFrameRequester(RTCPeerConnection pc, ILogger logger, CancellationToken ct)
    {
        _pc = pc;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    /// <summary>
    /// Starts requesting key frames for <paramref name="pc"/>'s video. Self-contained: it learns
    /// the sender's SSRC from the first video RTP packet, asks straight away, and keeps asking
    /// while no frame has been depacketised. Dispose it when the session ends.
    /// </summary>
    public static KeyFrameRequester Attach(RTCPeerConnection pc, ILogger logger, CancellationToken ct = default)
    {
        var requester = new KeyFrameRequester(pc, logger, ct);

        pc.OnVideoFrameReceived += requester.OnVideoFrame;
        pc.OnRtpPacketReceived += requester.OnRtpPacket;

        return requester;
    }

    private void OnVideoFrame(System.Net.IPEndPoint endPoint, uint timestamp, byte[] frame, VideoFormat format) =>
        Interlocked.Exchange(ref _videoDecoded, 1);

    private void OnRtpPacket(System.Net.IPEndPoint endPoint, SDPMediaTypesEnum mediaType, RTPPacket packet)
    {
        if (mediaType != SDPMediaTypesEnum.video)
        {
            return;
        }

        // The first video packet is both where the SSRC comes from and the earliest moment a key
        // frame can be asked for, so do both here rather than on a timer that may fire first.
        if (Interlocked.CompareExchange(ref _remoteVideoSsrc, packet.Header.SyncSource, 0) == 0)
        {
            // Off the RTP receive thread: this sends an RTCP report and must not block it.
            _ = Task.Run(RequestUntilDecodingAsync);
        }
    }

    private async Task RequestUntilDecodingAsync()
    {
        try
        {
            Request(1);

            for (int attempt = 2; attempt <= MAX_REQUESTS; attempt++)
            {
                await Task.Delay(RETRY_INTERVAL_MS, _cts.Token).ConfigureAwait(false);

                if (Volatile.Read(ref _videoDecoded) != 0 || _pc.connectionState != RTCPeerConnectionState.connected)
                {
                    return;
                }

                Request(attempt);
            }

            _logger.LogWarning("Still no decodable video after {Max} key frame requests; the sender may not be " +
                               "honouring PLI, or frames are arriving but cannot be depacketised.", MAX_REQUESTS);
        }
        catch (OperationCanceledException)
        {
            // The session ended; nothing left to ask for.
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Key frame request loop stopped: {Error}", excp.Message);
        }
    }

    /// <summary>Sends one Picture Loss Indication for the video stream, best effort.</summary>
    private void Request(int attempt)
    {
        try
        {
            uint remoteSsrc = Volatile.Read(ref _remoteVideoSsrc);
            if (remoteSsrc == 0)
            {
                return;
            }

            // See the file header: the local track's SSRC is unset for a recvonly track, and a PLI
            // with a sender SSRC of 0 is ignored without trace.
            uint localSsrc = _pc.VideoRtcpSession?.Ssrc ?? _pc.VideoStream?.LocalTrack?.Ssrc ?? 0;

            _pc.SendRtcpFeedback(SDPMediaTypesEnum.video,
                new RTCPFeedback(localSsrc, remoteSsrc, PSFBFeedbackTypesEnum.PLI));

            _logger.LogDebug("Sent key frame request {Attempt} of {Max} (PLI, sender ssrc {LocalSsrc}, media ssrc {RemoteSsrc}).",
                attempt, MAX_REQUESTS, localSsrc, remoteSsrc);
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Could not send a key frame request: {Error}", excp.Message);
        }
    }

    public void Dispose()
    {
        try
        {
            _pc.OnVideoFrameReceived -= OnVideoFrame;
            _pc.OnRtpPacketReceived -= OnRtpPacket;
        }
        catch (Exception)
        {
            // The peer connection may already be torn down.
        }

        _cts.Cancel();
        _cts.Dispose();
    }
}
