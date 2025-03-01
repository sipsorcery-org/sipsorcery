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
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace demo;

public interface IWebRtcConnectionManager
{
    Task<RTCPeerConnection> CreatePeerConnection(string peerID);
}

public class WebRtcConnectionManager : IWebRtcConnectionManager, IDisposable
{
    private const string FREE_IMAGE_PATH = "media/simple_flower.jpg";
    private const string PAID_IMAGE_PATH = "media/real_flowers.jpg";

    private const int FREE_PERIOD_SECONDS = 15;
    private const int TRANSITION_PERIOD_SECONDS = 8;
    private const int MAX_ALPHA_TRANSPARENCY = 200;
    private const string INITIALISING_TITLE = "Initialising";
    private const string FREE_PERIOD_TITLE = "Free Period";
    private const string TRANSITION_PERIOD_TITLE = "Transition Period";
    private const string WAITING_FOR_PAYMENT_PERIOD_TITLE = "Waiting for Payment";
    private const string PAID_PERIOD_TITLE = "Thanks for Paying!";
    private const int FRAMES_PER_SECOND = 5; //30;
    private const int CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS = 100;

    private readonly ILogger<WebRtcConnectionManager> _logger;
    private readonly ILightningPaymentService _webRTCLightningPaymentService;
    private readonly IAnnotatedBitmapGenerator _annotatedBitmapGenerator;

    private Timer? _setBitmapSourceTimer = null;

    public WebRtcConnectionManager(
        ILogger<WebRtcConnectionManager> logger,
        ILightningPaymentService webRTCLightningPaymentService,
        IAnnotatedBitmapGenerator annotatedBitmapGenerator)
    {
        _logger = logger;
        _webRTCLightningPaymentService = webRTCLightningPaymentService;
        _annotatedBitmapGenerator = annotatedBitmapGenerator;
    }

    public Task<RTCPeerConnection> CreatePeerConnection(string peerID)
    {
        var pc = new RTCPeerConnection(null);

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

        return Task.FromResult(pc);
    }

    private Timer CreateGenerateBitmapTimer(VideoBitmapSource bitmapSource, string peerID)
    {
        var frameConfig = new FrameConfig(DateTimeOffset.Now, null, 0, Color.Green, INITIALISING_TITLE, false, FREE_IMAGE_PATH);

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
        var paymentState = _webRTCLightningPaymentService.GetPaymentState();
        int remainingSeconds = (int)paymentState.PaidUntil.Subtract(DateTimeOffset.Now).TotalSeconds;

        if (paymentState.IsFreePeriod)
        {
            if (remainingSeconds > TRANSITION_PERIOD_SECONDS)
            {
                return frameConfig with
                {
                    BorderColour = Color.Pink,
                    Title = FREE_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = null,
                    Opacity = 0,
                    ImagePath = FREE_IMAGE_PATH
                };
            }
            else if (remainingSeconds > 0)
            {
                if(!paymentState.HasLightningInvoiceBeenRequested && paymentState.LightningPaymentRequest == null)
                {
                    _webRTCLightningPaymentService.RequestLightningInvoice();
                }

                int opacity = (int)(MAX_ALPHA_TRANSPARENCY * ((TRANSITION_PERIOD_SECONDS - remainingSeconds) / (double)TRANSITION_PERIOD_SECONDS));

                return frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = paymentState.isPaidPeriod,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = opacity,
                    ImagePath = FREE_IMAGE_PATH
                };
            }
            else
            {
                return frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = false,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = FREE_IMAGE_PATH
                };
            }
        }
        else if(paymentState.isPaidPeriod)
        {
            if (remainingSeconds > TRANSITION_PERIOD_SECONDS)
            {
                return frameConfig with
                {
                    BorderColour = Color.Blue,
                    Title = PAID_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = null,
                    Opacity = 0,
                    ImagePath = PAID_IMAGE_PATH
                };
            }
            else if (remainingSeconds > 0)
            {
                if (!paymentState.HasLightningInvoiceBeenRequested && paymentState.LightningPaymentRequest == null)
                {
                    _webRTCLightningPaymentService.RequestLightningInvoice();
                }

                int opacity = (int)(MAX_ALPHA_TRANSPARENCY * ((TRANSITION_PERIOD_SECONDS - remainingSeconds) / (double)TRANSITION_PERIOD_SECONDS));

                return frameConfig with
                {
                    BorderColour = Color.Orange,
                    Title = TRANSITION_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = opacity,
                    ImagePath = PAID_IMAGE_PATH
                };
            }
            else
            {
                return frameConfig with
                {
                    BorderColour = Color.Red,
                    Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
                    IsPaid = true,
                    LightningPaymentRequest = paymentState.LightningPaymentRequest,
                    Opacity = MAX_ALPHA_TRANSPARENCY,
                    ImagePath = PAID_IMAGE_PATH
                };
            }
        }

        return frameConfig with
        {
            BorderColour = Color.Red,
            Title = WAITING_FOR_PAYMENT_PERIOD_TITLE,
            IsPaid = true,
            LightningPaymentRequest = paymentState.LightningPaymentRequest,
            Opacity = MAX_ALPHA_TRANSPARENCY,
            ImagePath = FREE_IMAGE_PATH
        };
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
                _webRTCLightningPaymentService.SetInitialFreeSeconds(FREE_PERIOD_SECONDS);

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
        _logger.LogDebug($"{nameof(WebRtcConnectionManager)} dispose.");

        _setBitmapSourceTimer?.Dispose();
    }
}
