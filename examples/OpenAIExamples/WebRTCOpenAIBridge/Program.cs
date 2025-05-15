//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that can be used to act as a bridge
// between a browser based WebRTC peer and OpenAI's real-time API
// https://platform.openai.com/docs/guides/realtime-webrtc.
//
// Browser clients can connect directly to OpenAI. The reason to use a bridging
// asp.net app is to control and utilise the interaction on the asp.net app.
// For example the asp.net could provide a local function to look some DB info etc.
// based on user request.
//
// Usage:
// set OPENAIKEY=your_openai_key
// dotnet run
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Apr 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.Net;
using LanguageExt;
using Serilog.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace demo;

record InitPcContext(
   string CallLabel,
   string EphemeralKey,
   string OfferSdp,
   string AnswerSdp,
   OpenAIVoicesEnum Voice,
   int AudioScopeNumber
);

record CreatedPcContext(
    string CallLabel,
    string EphemeralKey,
    RTCPeerConnection Pc,
    string OfferSdp,
    string AnswerSdp,
    SemaphoreSlim PcConnectedSemaphore
 );

record PcContext(
   RTCPeerConnection Pc,
   SemaphoreSlim PcConnectedSemaphore,
   string EphemeralKey = "",
   string OfferSdp = "",
   string AnswerSdp = ""
);

class Program
{
    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;   

    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);
        _logger = factory.CreateLogger<Program>();

        Log.Information("WebRTC OpenAI Browser Bridge Demo Program");

        var openAiKey = Environment.GetEnvironmentVariable("OPENAIKEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAIKEY=<your openai api key>");
            return;
        }

        var builder = WebApplication.CreateBuilder();

        builder.Host.UseSerilog();

        builder.Services.AddLogging(builder =>
        {
            builder.AddSerilog(dispose: true);
        });

        builder.Services
          .AddHttpClient()
          .AddHttpClient(OpenAIRealtimeRestClient.OPENAI_HTTP_CLIENT_NAME, client =>
          {
              client.Timeout = TimeSpan.FromSeconds(5);
              client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openAiKey);
          });
        builder.Services.AddTransient<IOpenAIRealtimeRestClient, OpenAIRealtimeRestClient>();
        builder.Services.AddTransient<IOpenAIRealtimeWebRTCEndPoint, OpenAIRealtimeWebRTCEndPoint>();

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        var webSocketOptions = new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        };

        app.UseWebSockets(webSocketOptions);

        app.Map("/ws", async (HttpContext context,
            [FromServices] IOpenAIRealtimeRestClient openAiClient,
            [FromServices] IOpenAIRealtimeWebRTCEndPoint openAiWebRTCEndPoint) =>
        {
            Log.Debug("Web socket client connection established.");

            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                RTCConfiguration config = new RTCConfiguration
                {
                    X_ICEIncludeAllInterfaceAddresses = true
                };

                //if (!string.IsNullOrWhiteSpace(_stunUrl))
                //{
                //    config.iceServers = new List<RTCIceServer> { new RTCIceServer { urls = _stunUrl } };
                //}

                var webSocketPeer = new WebRTCWebSocketPeerAspNet(
                    webSocket,
                    CreateBrowserPeerConnection,
                    config,
                    RTCSdpType.offer);

                //webSocketPeer.OfferOptions = new RTCOfferOptions
                //{
                //    X_WaitForIceGatheringToComplete = _waitForIceGatheringToSendOffer
                //};

                await webSocketPeer.Run();

                Log.Debug("Web socket closing with WebRTC peer connection in state {state}.", webSocketPeer.RTCPeerConnection?.connectionState);
            }
            else
            {
                // Not a WebSocket request
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });

        await app.RunAsync();
    }

    //private static async Task StartOpenAIConnection(RTCPeerConnection browserPeerConnection)
    //{
    //    _logger.LogDebug("Browser peer connection connected.");

    //    var initialContext = new InitPcContext(
    //        "dummy",
    //        string.Empty,
    //        string.Empty,
    //        string.Empty,
    //        OpenAIVoicesEnum.shimmer,
    //        1);

    //    var openAiResult = await InitiatePeerConnectionWithOpenAI(_openAIKey ?? string.Empty, initialContext);

    //    if (openAiResult.IsLeft)
    //    {
    //        _logger.LogError($"There was a problem connecting the OpenAI call. {((Error)openAiResult).Message}");
    //    }
    //    else
    //    {
    //        var openAiCtx = (CreatedPcContext)openAiResult;
    //        var waitForDataChannel = openAiCtx.PcConnectedSemaphore.WaitAsync();
    //        await waitForDataChannel;

    //        _logger.LogInformation($"OpenAI data channel connected, connecting audio.");

    //        // Use to intercept audio and sent to Windows speaker for local diagnostics.
    //        //var audioEncoder = new AudioEncoder(includeOpus: true);
    //        //var opus = audioEncoder.SupportedFormats.Single(x => x.FormatName == "OPUS");
    //        //var audioSource = new AudioExtrasSource(audioEncoder, new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
    //        //audioSource.SetAudioSourceFormat(opus);
    //        //audioSource.OnAudioSourceEncodedSample += webSocketPeer.RTCPeerConnection.SendAudio;
    //        //await audioSource.StartAudio();
    //        //WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(audioEncoder, -1, -1, true, false);
    //        //windowsAudioEP.SetAudioSinkFormat(opus);
    //        //await windowsAudioEP.StartAudioSink();

    //        uint rtpPreviousTimestampForBrowser = 0;
    //        openAiCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
    //        {
    //            if (rtpPreviousTimestampForBrowser == 0)
    //            {
    //                rtpPreviousTimestampForBrowser = rtpPkt.Header.Timestamp;
    //            }
    //            else
    //            {
    //                uint rtpDuration = rtpPkt.Header.Timestamp - rtpPreviousTimestampForBrowser;
    //                rtpPreviousTimestampForBrowser = rtpPkt.Header.Timestamp;

    //                browserPeerConnection.SendAudio(rtpDuration, rtpPkt.Payload);
    //            }
    //        };

    //        uint rtpPreviousTimestampForOpenAI = 0;
    //        browserPeerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
    //            {
    //                if (rtpPreviousTimestampForOpenAI == 0)
    //                {
    //                    rtpPreviousTimestampForOpenAI = rtpPkt.Header.Timestamp;
    //                }
    //                else
    //                {
    //                    uint rtpDuration = rtpPkt.Header.Timestamp - rtpPreviousTimestampForOpenAI;
    //                    rtpPreviousTimestampForOpenAI = rtpPkt.Header.Timestamp;

    //                    openAiCtx.Pc.SendAudio(rtpDuration, rtpPkt.Payload);
    //                }
    //            };

    //        browserPeerConnection.OnClosed += () => openAiCtx.Pc.Close("Browser peer closed.");

    //        SendResponseCreate(openAiCtx.Pc.DataChannels.First(), OpenAIVoicesEnum.alloy, "Hi there.");
    //    }
    //}


    /// <summary>
    /// Sends a response create message to the OpenAI data channel to trigger the conversation.
    /// </summary>
    private static void SendResponseCreate(RTCDataChannel dc, OpenAIVoicesEnum voice, string message)
    {
        var responseCreate = new OpenAIResponseCreate
        {
            EventID = Guid.NewGuid().ToString(),
            Response = new OpenAIResponseCreateResponse
            {
                Instructions = message,
                Voice = voice.ToString()
            }
        };

        _logger.LogInformation($"Sending initial response create to first call data channel {dc.label}.");
        _logger.LogDebug(responseCreate.ToJson());

        dc.send(responseCreate.ToJson());
    }

    /// <summary>
    /// Method to create the peer connection with the browser.
    /// </summary>
    /// <param name="onConnectedSemaphore">A semaphore that will get set when the data channel on the peer connection is opened. Since the data channel
    /// can only be opened once the peer connection is open this indicates both are ready for use.</param>
    private static Task<RTCPeerConnection> CreateBrowserPeerConnection(RTCConfiguration pcConfig)
    {
        var peerConnection = new RTCPeerConnection(pcConfig);

        var audioEncoder = new AudioEncoder(includeOpus: true);
        var opusFormat = audioEncoder.SupportedFormats.Single(x => x.FormatName == "OPUS");
        MediaStreamTrack audioTrack = new MediaStreamTrack(opusFormat, MediaStreamStatusEnum.SendRecv);
        peerConnection.addTrack(audioTrack);

        peerConnection.OnAudioFormatsNegotiated += (audioFormats) =>
        {
            _logger.LogDebug($"Audio format negotiated {audioFormats.First().FormatName}.");
        };
        //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
        //peerConnection.OnSendReport += RtpSession_OnSendReport;
        peerConnection.OnTimeout += (mediaType) => _logger.LogDebug($"Timeout on media {mediaType}.");
        peerConnection.oniceconnectionstatechange += (state) => _logger.LogDebug($"ICE connection state changed to {state}.");
        peerConnection.onconnectionstatechange += (state) => _logger.LogDebug($"Peer connection connected changed to {state}.");
        
        peerConnection.onsignalingstatechange += () =>
        {
            if (peerConnection.signalingState == RTCSignalingState.have_local_offer)
            {
                _logger.LogDebug("Local SDP:\n{sdp}", peerConnection.localDescription.sdp);
            }
            else if (peerConnection.signalingState is RTCSignalingState.have_remote_offer or RTCSignalingState.stable)
            {
                _logger.LogDebug("Remote SDP:\n{sdp}", peerConnection.remoteDescription.sdp);
            }
        };

        return Task.FromResult(peerConnection);
    }

    /// <summary>
    /// Event handler for WebRTC data channel messages.
    /// </summary>
    private static void OnDataChannelMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
    {
        //logger.LogInformation($"Data channel {dc.label}, protocol {protocol} message length {data.Length}.");

        var message = Encoding.UTF8.GetString(data);
        var serverEvent = JsonSerializer.Deserialize<OpenAIServerEventBase>(message, JsonOptions.Default);

        var serverEventModel = OpenAIDataChannelManager.ParseDataChannelMessage(data);
        serverEventModel.IfSome(e =>
        {
            if (e is OpenAIResponseAudioTranscriptDone done)
            {
                _logger.LogInformation($"Transcript done: {done.Transcript}");
            }
        });
    }
}
