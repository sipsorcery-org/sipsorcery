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
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace demo;

public interface IPaidWebRtcConnection
{
    event Action? OnPeerConnectionClosedOrFailed;

    Task<RTCPeerConnection> CreatePeerConnection(RTCConfiguration confi);
}

public class PaidWebRtcConnection : IPaidWebRtcConnection, IDisposable
{
    private const int FRAMES_PER_SECOND = 5; //30;
    private const int CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS = 100;

    private readonly ILogger<PaidWebRtcConnection> _logger;
    private readonly IFrameConfigStateMachine _frameConfigStateMachine;
    private readonly IAnnotatedBitmapGenerator _annotatedBitmapGenerator;

    private Timer? _setBitmapSourceTimer = null;

    public event Action? OnPeerConnectionClosedOrFailed = null;

    public PaidWebRtcConnection(
        ILogger<PaidWebRtcConnection> logger,
        IAnnotatedBitmapGenerator annotatedBitmapGenerator,
        IFrameConfigStateMachine frameConfigStateMachine)
    {
        _logger = logger;
        _frameConfigStateMachine = frameConfigStateMachine;
        _annotatedBitmapGenerator = annotatedBitmapGenerator;
    }

    public Task<RTCPeerConnection> CreatePeerConnection(RTCConfiguration config)
    {
        var pc = new RTCPeerConnection(config);

        var sourceImage = Image.Load<Rgba32>(PaymentStateMachine.FREE_IMAGE_PATH);

        var imageSharpBitmapSource = new VideoBitmapSource(new FFmpegVideoEncoder());
        imageSharpBitmapSource.SetFrameRate(FRAMES_PER_SECOND);
        imageSharpBitmapSource.SetSourceBitmap(sourceImage);

        MediaStreamTrack track = new MediaStreamTrack(imageSharpBitmapSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(track);

        imageSharpBitmapSource.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += (formats) => imageSharpBitmapSource.SetVideoSourceFormat(formats.First());

        HandlePeerConnectionStateChange(pc, imageSharpBitmapSource);

        SetDiagnosticLogging(pc);

        return Task.FromResult(pc);
    }

    private Timer ImageSharpCreateGenerateBitmapTimer(VideoBitmapSource bitmapSource)
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

    private void HandlePeerConnectionStateChange(RTCPeerConnection pc, VideoBitmapSource videoSource)
    {
        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug($"Peer connection state change to {state}.");

            if (state is RTCPeerConnectionState.closed or
                        RTCPeerConnectionState.failed or
                        RTCPeerConnectionState.disconnected)
            {
                await ClosePeerConnectionResources(_setBitmapSourceTimer, videoSource);

                OnPeerConnectionClosedOrFailed?.Invoke();
            }

            if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.connected)
            {
                _logger.LogDebug($"Starting bitmap source.");
                await videoSource.StartVideo();

                if (_setBitmapSourceTimer == null)
                {
                    _logger.LogDebug($"Starting bitmap create bitmap frame.");
                    _setBitmapSourceTimer = ImageSharpCreateGenerateBitmapTimer(videoSource);
                }
            }
        };
    }

    private async Task ClosePeerConnectionResources(Timer? setBitmapSourceTimer, IVideoSource videoSource)
    {
        _logger.LogDebug($"{nameof(ClosePeerConnectionResources)}.");

        if (setBitmapSourceTimer != null)
        {
            await setBitmapSourceTimer.DisposeAsync();
            setBitmapSourceTimer = null;
        }

        if (videoSource != null)
        {
            await videoSource!.CloseVideo();
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
