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
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.Net;
using LanguageExt;
using LanguageExt.Common;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

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
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);

        Log.Information("WebRTC OpenAI Browser Bridge Demo Program");

        var openAiKey = Environment.GetEnvironmentVariable("OPENAIKEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAIKEY=<your openai api key>");
            return;
        }

        //using var provider = services.BuildServiceProvider();

        //// Create the OpenAI Realtime WebRTC peer connection.
        //var openaiClient = provider.GetRequiredService<IOpenAIRealtimeRestClient>();
        //var webrtcEndPoint = provider.GetRequiredService<IOpenAIRealtimeWebRTCEndPoint>();

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

        app.Map("/ws", async context =>
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

    private static async Task StartOpenAIConnection(RTCPeerConnection browserPeerConnection)
    {
        _logger.LogDebug("Browser peer connection connected.");

        var initialContext = new InitPcContext(
            "dummy",
            string.Empty,
            string.Empty,
            string.Empty,
            OpenAIVoicesEnum.shimmer,
            1);

        var openAiResult = await InitiatePeerConnectionWithOpenAI(_openAIKey ?? string.Empty, initialContext);

        if (openAiResult.IsLeft)
        {
            _logger.LogError($"There was a problem connecting the OpenAI call. {((Error)openAiResult).Message}");
        }
        else
        {
            var openAiCtx = (CreatedPcContext)openAiResult;
            var waitForDataChannel = openAiCtx.PcConnectedSemaphore.WaitAsync();
            await waitForDataChannel;

            _logger.LogInformation($"OpenAI data channel connected, connecting audio.");

            // Use to intercept audio and sent to Windows speaker for local diagnostics.
            //var audioEncoder = new AudioEncoder(includeOpus: true);
            //var opus = audioEncoder.SupportedFormats.Single(x => x.FormatName == "OPUS");
            //var audioSource = new AudioExtrasSource(audioEncoder, new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
            //audioSource.SetAudioSourceFormat(opus);
            //audioSource.OnAudioSourceEncodedSample += webSocketPeer.RTCPeerConnection.SendAudio;
            //await audioSource.StartAudio();
            //WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(audioEncoder, -1, -1, true, false);
            //windowsAudioEP.SetAudioSinkFormat(opus);
            //await windowsAudioEP.StartAudioSink();

            uint rtpPreviousTimestampForBrowser = 0;
            openAiCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                if (rtpPreviousTimestampForBrowser == 0)
                {
                    rtpPreviousTimestampForBrowser = rtpPkt.Header.Timestamp;
                }
                else
                {
                    uint rtpDuration = rtpPkt.Header.Timestamp - rtpPreviousTimestampForBrowser;
                    rtpPreviousTimestampForBrowser = rtpPkt.Header.Timestamp;

                    browserPeerConnection.SendAudio(rtpDuration, rtpPkt.Payload);
                }
            };

            uint rtpPreviousTimestampForOpenAI = 0;
            browserPeerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
                {
                    if (rtpPreviousTimestampForOpenAI == 0)
                    {
                        rtpPreviousTimestampForOpenAI = rtpPkt.Header.Timestamp;
                    }
                    else
                    {
                        uint rtpDuration = rtpPkt.Header.Timestamp - rtpPreviousTimestampForOpenAI;
                        rtpPreviousTimestampForOpenAI = rtpPkt.Header.Timestamp;

                        openAiCtx.Pc.SendAudio(rtpDuration, rtpPkt.Payload);
                    }
                };

            browserPeerConnection.OnClosed += () => openAiCtx.Pc.Close("Browser peer closed.");

            SendResponseCreate(openAiCtx.Pc.DataChannels.First(), OpenAIVoicesEnum.alloy, "Hi there.");
        }
    }

    /// <summary>
    /// Contains a functional flow to initiate a WebRTC peer connection with the OpenAI realtime endpoint.
    /// </summary>
    /// <remarks>
    /// See https://platform.openai.com/docs/guides/realtime-webrtc for the steps required to establish a connection.
    /// </remarks>
    private static async Task<Either<Error, CreatedPcContext>> InitiatePeerConnectionWithOpenAI(string openAIKey, InitPcContext initCtx)
    {
        return await Prelude.Right<Error, Unit>(default)
            .BindAsync(async _ =>
            {
                _logger.LogInformation($"STEP 1 {initCtx.CallLabel}: Get ephemeral key from OpenAI.");
                var ephemeralKey = await OpenAIRealtimeRestClient.CreateEphemeralKeyAsync(OPENAI_REALTIME_SESSIONS_URL, openAIKey, OPENAI_MODEL, initCtx.Voice);

                return ephemeralKey.Map(ephemeralKey => initCtx with { EphemeralKey = ephemeralKey });
            })
            .BindAsync(async withkeyCtx =>
            {
                _logger.LogInformation($"STEP 2 {withkeyCtx.CallLabel}: Create WebRTC PeerConnection & get local SDP offer.");

                var onConnectedSemaphore = new SemaphoreSlim(0, 1);
                var pc = await CreatePeerConnection(onConnectedSemaphore);
                var offer = pc.createOffer();
                await pc.setLocalDescription(offer);

                _logger.LogDebug("SDP offer:");
                _logger.LogDebug(offer.sdp);

                return Prelude.Right<Error, CreatedPcContext>(new(
                    withkeyCtx.CallLabel,
                    withkeyCtx.EphemeralKey,
                    pc,
                    offer.sdp,
                    string.Empty,
                    onConnectedSemaphore));
            })
            .BindAsync(async createdCtx =>
            {
                _logger.LogInformation($"STEP 3 {createdCtx.CallLabel}: Send SDP offer to OpenAI REST server & get SDP answer.");

                var answerEither = await OpenAIRealtimeRestClient.GetOpenAIAnswerSdpAsync(createdCtx.EphemeralKey, OPENAI_REALTIME_BASE_URL, OPENAI_MODEL, createdCtx.OfferSdp);
                return answerEither.Map(answer => createdCtx with { AnswerSdp = answer });
            })
            .BindAsync(withAnswerCtx =>
            {
                _logger.LogInformation($"STEP 4 {withAnswerCtx.CallLabel}: Set remote SDP");

                _logger.LogDebug("SDP answer:");
                _logger.LogDebug(withAnswerCtx.AnswerSdp);

                var setAnswerResult = withAnswerCtx.Pc.setRemoteDescription(
                    new RTCSessionDescriptionInit { sdp = withAnswerCtx.AnswerSdp, type = RTCSdpType.answer }
                );
                _logger.LogDebug($"Set answer result {setAnswerResult}.");

                return setAnswerResult == SetDescriptionResultEnum.OK ?
                    Prelude.Right<Error, CreatedPcContext>(withAnswerCtx) :
                    Prelude.Left<Error, CreatedPcContext>(Error.New("Failed to set remote SDP."));
            });
    }

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
        peerConnection.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug($"Peer connection connected changed to {state}.");

            if (state == RTCPeerConnectionState.connected)
            {
                await StartOpenAIConnection(peerConnection);
            }
        };

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
    /// Method to create the local peer connection instance and data channel.
    /// </summary>
    /// <param name="onConnectedSemaphore">A semaphore that will get set when the data channel on the peer connection is opened. Since the data channel
    /// can only be opened once the peer connection is open this indicates both are ready for use.</param>
    private static async Task<RTCPeerConnection> CreatePeerConnection(SemaphoreSlim onConnectedSemaphore)
    {
        var pcConfig = new RTCConfiguration
        {
            X_UseRtpFeedbackProfile = true,
        };

        var peerConnection = new RTCPeerConnection(pcConfig);
        var dataChannel = await peerConnection.createDataChannel(OPENAI_DATACHANNEL_NAME);

        var audioEncoder = new AudioEncoder(includeOpus: true);
        var opusFormat = audioEncoder.SupportedFormats.Where(x => x.FormatName == "OPUS").ToList();
        MediaStreamTrack audioTrack = new MediaStreamTrack(opusFormat, MediaStreamStatusEnum.SendRecv);
        peerConnection.addTrack(audioTrack);

        peerConnection.OnAudioFormatsNegotiated += (audioFormats) => _logger.LogDebug($"Audio format negotiated {audioFormats.First().FormatName}.");
        //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
        //peerConnection.OnSendReport += RtpSession_OnSendReport;
        peerConnection.OnTimeout += (mediaType) => _logger.LogDebug($"Timeout on media {mediaType}.");
        peerConnection.oniceconnectionstatechange += (state) => _logger.LogDebug($"ICE connection state changed to {state}.");
        peerConnection.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug($"Peer connection connected changed to {state}.");
        };

        dataChannel.onopen += () =>
        {
            _logger.LogDebug("OpenAI data channel opened.");
            onConnectedSemaphore.Release();
        };

        dataChannel.onclose += () => _logger.LogDebug($"OpenAI data channel {dataChannel.label} closed.");

        dataChannel.onmessage += OnDataChannelMessage;

        return peerConnection;
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
