//-----------------------------------------------------------------------------
// Filename: PaidWebRtcConnection.cs
//
// Description: Manages the creation and lifeftime of a paid WebRTC connection.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.FFmpeg;
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace demo;

public interface IPaidWebRtcConnection
{
    Task<RTCPeerConnection> CreatePeerConnection(string peerID);
}

public class PaidWebRtcConnection : IPaidWebRtcConnection, IDisposable
{
    private const int FRAMES_PER_SECOND = 5; //30;
    private const int CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS = 100;

    private readonly ILogger<PaidWebRtcConnection> _logger;
    private readonly IAnnotatedBitmapGenerator _annotatedBitmapGenerator;
    private readonly IFrameConfigStateMachine _frameConfigStateMachine; 

    private Timer? _setBitmapSourceTimer = null;

    public PaidWebRtcConnection(
        ILogger<PaidWebRtcConnection> logger,
        IAnnotatedBitmapGenerator annotatedBitmapGenerator,
        IFrameConfigStateMachine frameConfigStateMachine)
    {
        _logger = logger;
        _annotatedBitmapGenerator = annotatedBitmapGenerator;
        _frameConfigStateMachine = frameConfigStateMachine;
    }

    public Task<RTCPeerConnection> CreatePeerConnection(string peerID)
    {
        var pc = new RTCPeerConnection(null);

        Bitmap sourceBitmap = new Bitmap(PaymentStateMachine.FREE_IMAGE_PATH);

        var bitmapSource = new VideoBitmapSource(new FFmpegVideoEncoder());
        bitmapSource.SetFrameRate(FRAMES_PER_SECOND);
        bitmapSource.SetSourceBitmap(sourceBitmap);

        MediaStreamTrack track = new MediaStreamTrack(bitmapSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(track);

        bitmapSource.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += (formats) => bitmapSource.SetVideoSourceFormat(formats.First());

        HandlePeerConnectionStateChange(pc, bitmapSource, peerID);
        SetDiagnosticLogging(pc);

        return Task.FromResult(pc);
    }

    private Timer CreateGenerateBitmapTimer(VideoBitmapSource bitmapSource, string peerID)
    {
        return new Timer(_ =>
        {
            var frameConfig = _frameConfigStateMachine.GetPaidFrameConfig();

            var annotatedBitmap = _annotatedBitmapGenerator.GetAnnotatedBitmap(frameConfig);

            if (annotatedBitmap != null)
            {
                bitmapSource.SetSourceBitmap(annotatedBitmap);
                annotatedBitmap.Dispose();
            }
        },
        null, TimeSpan.Zero, TimeSpan.FromMilliseconds(CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS));
    }

    private void HandlePeerConnectionStateChange(RTCPeerConnection pc, VideoBitmapSource bitmapSource, string peerID)
    {
        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug($"Peer {peerID} connection state change to {state}.");

            if (state is RTCPeerConnectionState.closed or
                        RTCPeerConnectionState.failed or
                        RTCPeerConnectionState.disconnected)
            {
                await ClosePeerConnectionResources(peerID, _setBitmapSourceTimer, bitmapSource);
            }

            if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.connected)
            {
                _logger.LogDebug($"Starting bitmap source for peer {peerID}.");
                await bitmapSource.StartVideo();

                if (_setBitmapSourceTimer == null)
                {
                    _logger.LogDebug($"Starting bitmap create bitmap frame for peer {peerID}.");
                    _setBitmapSourceTimer = CreateGenerateBitmapTimer(bitmapSource, peerID);
                }
            }
        };
    }

    private async Task ClosePeerConnectionResources(string peerID, Timer? setBitmapSourceTimer, VideoBitmapSource bitmapSource)
    {
        _logger.LogDebug($"{nameof(ClosePeerConnectionResources)} for peer ID {peerID}.");

        if (setBitmapSourceTimer != null)
        {
            await setBitmapSourceTimer.DisposeAsync();
            setBitmapSourceTimer = null;
        }

        if (bitmapSource != null && !bitmapSource.IsClosed)
        {
            await bitmapSource.CloseVideo();
            bitmapSource.Dispose();
        }
    }

    private void SetDiagnosticLogging(RTCPeerConnection pc)
    {
        // Diagnostics.
        pc.oniceconnectionstatechange += (state) => _logger.LogDebug($"ICE connection state change to {state}.");
        pc.onsignalingstatechange += () =>
        {
            if (pc.signalingState == RTCSignalingState.have_local_offer)
            {
                _logger.LogDebug($"Local SDP set, type {pc.localDescription.type}.");
                _logger.LogDebug(pc.localDescription.sdp.ToString());
            }
            else if (pc.signalingState == RTCSignalingState.have_remote_offer)
            {
                _logger.LogDebug($"Remote SDP set, type {pc.remoteDescription.type}.");
                _logger.LogDebug(pc.remoteDescription.sdp.ToString());
            }
        };
    }

    public void Dispose()
    {
        _logger.LogDebug($"{nameof(PaidWebRtcConnection)} dispose.");

        _setBitmapSourceTimer?.Dispose();
    }
}
