//-----------------------------------------------------------------------------
// Filename: WebRtcConnectionManager.cs
//
// Description: Manages the creation and lifeftime of WebRTC connections established
// with remote peers.
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
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace demo;

public class WebRtcConnectionManager
{
    private static string FREE_IMAGE_PATH = "media/simple_flower.jpg";
    private static string PAID_IMAGE_PATH = "media/real_flowers.jpg";

    private const int FREE_PERIOD_SECONDS = 3;
    private const int TRANSPARENCY_PERIOD_SECONDS = 3;
    private const int MAX_ALPHA_TRANSPARENCY = 200;
    private const string FREE_PERIOD_TITLE = "Taster Content";
    private const string TRANSITION_PERIOD_TITLE = "Pay for More";
    private const int FRAMES_PER_SECOND = 5; //30;
    private const int CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS = 100;

    private readonly ConcurrentDictionary<string, Lazy<Task<Lnrpc.AddInvoiceResponse>>> _lightningInvoiceCache = new();

    private readonly ILogger<WebRtcConnectionManager> _logger;
    private readonly PeerConnectionPayState _peerConnectionPayState;
    private readonly ILightningService _lightningService;
    private readonly IAnnotatedBitmapGenerator _annotatedBitmapGenerator;

    public WebRtcConnectionManager(
        ILogger<WebRtcConnectionManager> logger,
        PeerConnectionPayState peerConnectionPayState,
        ILightningService lightningService,
        IAnnotatedBitmapGenerator annotatedBitmapGenerator)
    {
        _logger = logger;
        _peerConnectionPayState = peerConnectionPayState;
        _lightningService = lightningService;
        _annotatedBitmapGenerator = annotatedBitmapGenerator;
    }

    public Task<RTCPeerConnection> CreatePeerConnection(string peerID)
    {
        var pc = new RTCPeerConnection(null);
        _peerConnectionPayState.TryAddPeer(peerID);

        Bitmap sourceBitmap = new Bitmap(FREE_IMAGE_PATH);

        var bitmapSource = new VideoBitmapSource(new FFmpegVideoEncoder());
        bitmapSource.SetFrameRate(FRAMES_PER_SECOND);
        bitmapSource.SetSourceBitmap(sourceBitmap);

        MediaStreamTrack track = new MediaStreamTrack(bitmapSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(track);

        bitmapSource.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += (formats) => bitmapSource.SetVideoSourceFormat(formats.First());

        HandlePeerConnectionStateChange(pc, bitmapSource, peerID);
        SetDiagnosticLogging(pc);

        pc.onconnectionstatechange += (state) =>
        {
            if (state is RTCPeerConnectionState.closed or
                        RTCPeerConnectionState.failed or
                        RTCPeerConnectionState.disconnected)
            {
                _peerConnectionPayState.TryRemovePeer(peerID);
            }
        };

        return Task.FromResult(pc);
    }

    private Timer CreateGenerateBitmapTimer(VideoBitmapSource bitmapSource, string peerID)
    {
        var frameConfig = new FrameConfig(DateTime.Now, null, 0, Color.Blue, FREE_PERIOD_TITLE, false, FREE_IMAGE_PATH);

        return new Timer(_ =>
        {
            frameConfig = GetUpdatedFrameConfig(frameConfig, peerID);

            var annotatedBitmap = _annotatedBitmapGenerator.GetAnnotatedBitmap(frameConfig);

            if (annotatedBitmap != null)
            {
                bitmapSource.SetSourceBitmap(annotatedBitmap);
                annotatedBitmap.Dispose();
            }
        },
        null, TimeSpan.Zero, TimeSpan.FromMilliseconds(CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS));
    }

    private FrameConfig GetUpdatedFrameConfig(FrameConfig frameConfig, string peerID)
    {
        if (_peerConnectionPayState.TryGetIsPaid(peerID))
        {
            return frameConfig with
            {
                BorderColour = Color.Pink,
                Title = string.Empty,
                IsPaid = true,
                LightningPaymentRequest = null,
                Opacity = 0,
                ImagePath = PAID_IMAGE_PATH
            };
        }

        if (DateTime.Now.Subtract(frameConfig.StartTime).TotalSeconds < FREE_PERIOD_SECONDS)
        {
            return frameConfig with
            {
                BorderColour = Color.Blue,
                Title = FREE_PERIOD_TITLE,
                LightningPaymentRequest = null
            };
        }
        else
        {
            // Request lightning invoice generation asynchronously without blocking
            var lightningInvoiceTask = GetLightningInvoice(peerID);

            bool isTransitionPeriod = DateTime.Now.Subtract(frameConfig.StartTime).TotalSeconds < (FREE_PERIOD_SECONDS + TRANSPARENCY_PERIOD_SECONDS);
            double freeSecondsRemaining = FREE_PERIOD_SECONDS + TRANSPARENCY_PERIOD_SECONDS - DateTime.Now.Subtract(frameConfig.StartTime).TotalSeconds;

            return frameConfig with
            {
                BorderColour = isTransitionPeriod ? Color.Yellow : Color.Orange,
                LightningPaymentRequest = lightningInvoiceTask.IsCompletedSuccessfully ? lightningInvoiceTask.Result.PaymentRequest : null,
                Opacity = isTransitionPeriod ? (int)(MAX_ALPHA_TRANSPARENCY - MAX_ALPHA_TRANSPARENCY * (freeSecondsRemaining / TRANSPARENCY_PERIOD_SECONDS)) : MAX_ALPHA_TRANSPARENCY,
                Title = TRANSITION_PERIOD_TITLE
            };
        }
    }

    private Task<Lnrpc.AddInvoiceResponse> GetLightningInvoice(string peerID)
    {
        var lazyTask = _lightningInvoiceCache.GetOrAdd(peerID, _ => new Lazy<Task<Lnrpc.AddInvoiceResponse>>(() => GetLightningInvoiceInternal(peerID)));
        return lazyTask.Value;
    }

    private Task<Lnrpc.AddInvoiceResponse> GetLightningInvoiceInternal(string peerID)
        => _lightningService.CreateInvoiceAsync(10000, "Pay me for flowers LOLZ.", 600);

    private void HandlePeerConnectionStateChange(RTCPeerConnection pc, VideoBitmapSource bitmapSource, string peerID)
    {
        Timer? setBitmapSourceTimer = null;

        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug($"Peer connection state change to {state}.");

            if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                await CloseCustomBitmapSource(setBitmapSourceTimer, bitmapSource);
            }
            else if (state == RTCPeerConnectionState.connected)
            {
                await bitmapSource.StartVideo();

                if (setBitmapSourceTimer == null)
                {
                    setBitmapSourceTimer = CreateGenerateBitmapTimer(bitmapSource, peerID);
                }
            }
        };
    }

    private async Task CloseCustomBitmapSource(Timer? setBitmapSourceTimer, VideoBitmapSource bitmapSource)
    {
        if (setBitmapSourceTimer != null)
        {
            await setBitmapSourceTimer.DisposeAsync();
            setBitmapSourceTimer = null;
        }

        await bitmapSource.CloseVideo();
        bitmapSource.Dispose();
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
}
