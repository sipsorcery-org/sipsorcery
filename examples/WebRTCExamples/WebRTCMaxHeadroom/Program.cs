//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A WebRTC demo that serves a stylised "Max Headroom" talking
// avatar. Video is rendered with SkiaSharp (MaxHeadroomVideoSource), speech is
// synthesised with Azure Cognitive Services and the mouth is lip-synced to the
// Azure viseme events (AzureTtsSpeaker). An optional local LLM (Ollama / LM
// Studio / llama.cpp) generates the replies (LocalLlmClient).
//
// Endpoints:
//   POST /offer  - WebRTC SDP offer/answer exchange (called by the browser).
//   POST /say    - body = text. Speaks the text verbatim.
//   POST /ask    - body = prompt. Runs the prompt through the local LLM (if
//                  configured) and speaks the reply.
//
// Configuration (environment variables):
//   AZURE_SPEECH_KEY     - required, Azure Speech resource key.
//   AZURE_SPEECH_REGION  - required, e.g. "westeurope".
//   AZURE_SPEECH_VOICE   - optional, defaults to en-US-GuyNeural.
//   LLM_ENDPOINT         - optional, e.g. http://localhost:11434/v1/chat/completions
//   LLM_MODEL            - optional, e.g. llama3.2
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.FFmpeg;

namespace demo;

class Program
{
    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;

    // The demo drives a single connected viewer.
    private static MaxHeadroomVideoSource _videoSource;
    private static AudioExtrasSource _audioSource;
    private static AzureTtsSpeaker _speaker;
    private static LocalLlmClient _llm;

    private static string _azureKey;
    private static string _azureRegion;
    private static string _azureVoice;
    private static int _visemeLeadMs = 0;

    static async Task Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Debug)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);
        _logger = factory.CreateLogger<Program>();

        _logger.LogInformation("WebRTC Max Headroom Avatar Demo");

        // Quick visual sanity check: render a few frames to PNG and exit.
        if (Array.Exists(Environment.GetCommandLineArgs(), a => a == "--snapshot"))
        {
            using var preview = new MaxHeadroomVideoSource();
            foreach (var v in new[] { 0, 2, 7, 21 })
            {
                preview.SaveSnapshot($"maxheadroom_viseme{v}.png", v);
            }
            _logger.LogInformation("Wrote snapshot PNGs to {Dir}.", Environment.CurrentDirectory);
            return;
        }

        _azureKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        _azureRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        _azureVoice = Environment.GetEnvironmentVariable("AZURE_SPEECH_VOICE") ?? "en-US-GuyNeural";
        if (int.TryParse(Environment.GetEnvironmentVariable("VISEME_LEAD_MS"), out var lead)) { _visemeLeadMs = lead; }

        if (string.IsNullOrWhiteSpace(_azureKey) || string.IsNullOrWhiteSpace(_azureRegion))
        {
            _logger.LogWarning("AZURE_SPEECH_KEY / AZURE_SPEECH_REGION not set. The avatar will render but cannot speak.");
        }

        _llm = new LocalLlmClient(
            Environment.GetEnvironmentVariable("LLM_ENDPOINT"),
            Environment.GetEnvironmentVariable("LLM_MODEL"));
        _logger.LogInformation("Local LLM {State}.", _llm.IsConfigured ? "configured" : "not configured (text will be spoken verbatim)");

        var builder = WebApplication.CreateBuilder();
        builder.Host.UseSerilog();
        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapPost("/offer", HandleOffer);

        app.MapPost("/say", async (HttpRequest request) =>
        {
            var text = await ReadBody(request);
            var notReady = SpeakerNotReady();
            if (notReady != null) { return notReady; }
            _ = _speaker.SpeakAsync(text);
            return Results.Ok();
        });

        app.MapPost("/ask", async (HttpRequest request) =>
        {
            var prompt = await ReadBody(request);
            var notReady = SpeakerNotReady();
            if (notReady != null) { return notReady; }
            var reply = await _llm.GenerateReplyAsync(prompt);
            _logger.LogInformation("LLM reply: {Reply}", reply);
            _ = _speaker.SpeakAsync(reply);
            return Results.Text(reply);
        });

        await app.RunAsync();
    }

    private static async Task<IResult> HandleOffer(HttpRequest request)
    {
        var sdpOffer = await ReadBody(request);
        _logger.LogDebug("Received SDP offer.");

        var pc = CreatePeerConnection();

        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = sdpOffer, type = RTCSdpType.offer });
        if (result != SetDescriptionResultEnum.OK)
        {
            _logger.LogError("Failed to set remote description: {Result}", result);
            return Results.BadRequest(result.ToString());
        }

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer);

        return Results.Text(pc.localDescription.sdp.ToString());
    }

    private static RTCPeerConnection CreatePeerConnection()
    {
        var pc = new RTCPeerConnection(null);

        var videoSource = new MaxHeadroomVideoSource(new FFmpegVideoEncoder());
        var videoTrack = new MediaStreamTrack(videoSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);
        videoSource.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += formats => videoSource.SetVideoSourceFormat(formats.First());

        // Use Silence rather than None so audio RTP packets flow continuously between
        // prompts. A continuous audio clock keeps the browser's jitter buffer and the
        // RTCP A/V sync stable; with bursty audio (None) the lip-sync drifts ahead of
        // the voice over successive prompts.
        var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence });
        var audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(audioTrack);
        audioSource.OnAudioSourceEncodedSample += pc.SendAudio;
        pc.OnAudioFormatsNegotiated += formats => audioSource.SetAudioSourceFormat(formats.First());

        AzureTtsSpeaker speaker = null;
        if (!string.IsNullOrWhiteSpace(_azureKey) && !string.IsNullOrWhiteSpace(_azureRegion))
        {
            speaker = new AzureTtsSpeaker(_azureKey, _azureRegion, _azureVoice, videoSource, audioSource, _visemeLeadMs);
        }

        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug("Peer connection state change to {State}.", state);

            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    // Register as the active call first so /say and /ask are reachable
                    // even if media start-up below hiccups.
                    _videoSource = videoSource;
                    _audioSource = audioSource;
                    _speaker = speaker;
                    try
                    {
                        await audioSource.StartAudio();
                        await videoSource.StartVideo();
                        if (speaker != null)
                        {
                            _ = speaker.SpeakAsync("M-m-max Headroom here. Welcome to the show!");
                        }
                        else
                        {
                            _logger.LogWarning("Connected but Azure Speech is not configured; the avatar cannot speak.");
                        }
                    }
                    catch (Exception excp)
                    {
                        _logger.LogError(excp, "Error starting avatar media on connect.");
                    }
                    break;

                case RTCPeerConnectionState.failed:
                    pc.Close("ice disconnection");
                    break;

                case RTCPeerConnectionState.closed:
                    await audioSource.CloseAudio();
                    await videoSource.CloseVideo();
                    videoSource.Dispose();
                    if (_videoSource == videoSource) { _videoSource = null; _audioSource = null; _speaker = null; }
                    break;
            }
        };

        pc.oniceconnectionstatechange += (state) => _logger.LogDebug("ICE connection state change to {State}.", state);

        return pc;
    }

    /// <summary>
    /// Returns a descriptive 400 result if the avatar can't speak yet, otherwise null.
    /// Distinguishes "no active call" from "TTS not configured" so the cause is obvious.
    /// </summary>
    private static IResult SpeakerNotReady()
    {
        if (_videoSource == null)
        {
            return Results.BadRequest("No active call. Connect a viewer in the browser first.");
        }
        if (_speaker == null)
        {
            return Results.BadRequest("Azure Speech is not configured. Set AZURE_SPEECH_KEY and AZURE_SPEECH_REGION (and optionally AZURE_SPEECH_VOICE), then restart.");
        }
        return null;
    }

    private static async Task<string> ReadBody(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }
}
