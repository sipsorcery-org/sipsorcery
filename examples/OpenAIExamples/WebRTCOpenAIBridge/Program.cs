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
    private const string OPENAI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions";
    private const string OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
    private const string OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
    private const string OPENAI_DATACHANNEL_NAME = "oai-events"; 

    private static string? _openAIKey = string.Empty;
    private static string? _stunUrl = string.Empty;
    private static bool _waitForIceGatheringToSendOffer = false;

    private static Microsoft.Extensions.Logging.ILogger logger = LoggerFactory.Create(builder =>
        builder.AddSerilog(new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger()))
        .CreateLogger<Program>();

    static async Task Main(string[] args)
    {
        Console.WriteLine("WebRTC OpenAI Demo Program");
        Console.WriteLine("Press ctrl-c to exit.");

        _openAIKey = Environment.GetEnvironmentVariable("OPENAIKEY");
        _stunUrl = Environment.GetEnvironmentVariable("STUN_URL");
        bool.TryParse(Environment.GetEnvironmentVariable("WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER"), out _waitForIceGatheringToSendOffer);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);
        var programLogger = factory.CreateLogger<Program>();

        if (string.IsNullOrWhiteSpace(_openAIKey))
        {
            programLogger.LogError("Please provide your OpenAI key as an environment variable. It's used to get the single use ephemeral secret for the WebRTC connection, e.g. set OPENAIKEY=<your openai api key>");
        }

        var builder = WebApplication.CreateBuilder();

        builder.Host.UseSerilog();

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
            programLogger.LogDebug("Web socket client connection established.");

            if (context.WebSockets.IsWebSocketRequest)
            {
                if (string.IsNullOrWhiteSpace(_openAIKey))
                {
                    programLogger.LogError("The OPENAIKEY environment variable is not set. The application cannot proceed.");
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
                else
                {
                    var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                    var peerConfig = new RTCConfiguration
                    {
                        X_UseRtpFeedbackProfile = true,
                        X_ICEIncludeAllInterfaceAddresses = true
                    };

                    if (!string.IsNullOrWhiteSpace(_stunUrl))
                    {
                        peerConfig.iceServers = new List<RTCIceServer> { new RTCIceServer { urls = _stunUrl } };
                    }

                    var webSocketPeer = new WebRTCWebSocketPeerAspNet(
                        webSocket,
                        CreateBrowserPeerConnection,
                        peerConfig,
                        RTCSdpType.offer);

                    webSocketPeer.OfferOptions = new RTCOfferOptions
                    {
                        X_WaitForIceGatheringToComplete = _waitForIceGatheringToSendOffer
                    };

                    webSocketPeer.OnRTCPeerConnectionConnected += async () =>
                    {
                        programLogger.LogDebug("Browser peer connection connected.");

                        var initialContext = new InitPcContext(
                            "dummy",
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            OpenAIVoicesEnum.shimmer,
                            1);

                        var openAiResult = await InitiatePeerConnectionWithOpenAI(_openAIKey, initialContext);

                        if (openAiResult.IsLeft)
                        {
                            logger.LogError($"There was a problem connecting the OpenAI call. {((Error)openAiResult).Message}");
                        }
                        else
                        {
                            var openAiCtx = (CreatedPcContext)openAiResult;
                            var waitForDataChannel = openAiCtx.PcConnectedSemaphore.WaitAsync();
                            await waitForDataChannel;

                            logger.LogInformation($"OpenAI data channel connected, connecting audio.");

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

                                    webSocketPeer.RTCPeerConnection.SendAudio(rtpDuration, rtpPkt.Payload);
                                }
                            };

                            uint rtpPreviousTimestampForOpenAI = 0;
                            webSocketPeer.RTCPeerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
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

                            webSocketPeer.RTCPeerConnection.OnClosed += () => openAiCtx.Pc.Close("Browser peer closed.");

                            SendResponseCreate(openAiCtx.Pc.DataChannels.First(), OpenAIVoicesEnum.alloy, "Hi there.");
                        }
                    };

                    await webSocketPeer.Run();

                    await webSocketPeer.Close();
                }
            }
            else
            {
                // Not a WebSocket request
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });

        await app.RunAsync();
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
                logger.LogInformation($"STEP 1 {initCtx.CallLabel}: Get ephemeral key from OpenAI.");
                var ephemeralKey = await OpenAIRealtimeRestClient.CreateEphemeralKeyAsync(OPENAI_REALTIME_SESSIONS_URL, openAIKey, OPENAI_MODEL, initCtx.Voice);

                return ephemeralKey.Map(ephemeralKey => initCtx with { EphemeralKey = ephemeralKey });
            })
            .BindAsync(async withkeyCtx =>
            {
                logger.LogInformation($"STEP 2 {withkeyCtx.CallLabel}: Create WebRTC PeerConnection & get local SDP offer.");

                var onConnectedSemaphore = new SemaphoreSlim(0, 1);
                var pc = await CreatePeerConnection(onConnectedSemaphore);
                var offer = pc.createOffer();
                await pc.setLocalDescription(offer);

                logger.LogDebug("SDP offer:");
                logger.LogDebug(offer.sdp);

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
                logger.LogInformation($"STEP 3 {createdCtx.CallLabel}: Send SDP offer to OpenAI REST server & get SDP answer.");

                var answerEither = await OpenAIRealtimeRestClient.GetOpenAIAnswerSdpAsync(createdCtx.EphemeralKey, OPENAI_REALTIME_BASE_URL, OPENAI_MODEL, createdCtx.OfferSdp);
                return answerEither.Map(answer => createdCtx with { AnswerSdp = answer });
            })
            .BindAsync(withAnswerCtx =>
            {
                logger.LogInformation($"STEP 4 {withAnswerCtx.CallLabel}: Set remote SDP");

                logger.LogDebug("SDP answer:");
                logger.LogDebug(withAnswerCtx.AnswerSdp);

                var setAnswerResult = withAnswerCtx.Pc.setRemoteDescription(
                    new RTCSessionDescriptionInit { sdp = withAnswerCtx.AnswerSdp, type = RTCSdpType.answer }
                );
                logger.LogDebug($"Set answer result {setAnswerResult}.");

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

        logger.LogInformation($"Sending initial response create to first call data channel {dc.label}.");
        logger.LogDebug(responseCreate.ToJson());

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
            logger.LogDebug($"Audio format negotiated {audioFormats.First().FormatName}.");
        };
        //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
        //peerConnection.OnSendReport += RtpSession_OnSendReport;
        peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
        peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
        peerConnection.onconnectionstatechange += (state) =>
        {
            logger.LogDebug($"Peer connection connected changed to {state}.");
        };

        peerConnection.onsignalingstatechange += () =>
        {
            if (peerConnection.signalingState == RTCSignalingState.have_local_offer)
            {
                logger.LogDebug("Local SDP:\n{sdp}", peerConnection.localDescription.sdp);
            }
            else if (peerConnection.signalingState is RTCSignalingState.have_remote_offer or RTCSignalingState.stable)
            {
                logger.LogDebug("Remote SDP:\n{sdp}", peerConnection.remoteDescription.sdp);
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

        peerConnection.OnAudioFormatsNegotiated += (audioFormats) => logger.LogDebug($"Audio format negotiated {audioFormats.First().FormatName}.");
        //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
        //peerConnection.OnSendReport += RtpSession_OnSendReport;
        peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
        peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
        peerConnection.onconnectionstatechange += (state) =>
        {
            logger.LogDebug($"Peer connection connected changed to {state}.");
        };

        dataChannel.onopen += () =>
        {
            logger.LogDebug("OpenAI data channel opened.");
            onConnectedSemaphore.Release();
        };

        dataChannel.onclose += () => logger.LogDebug($"OpenAI data channel {dataChannel.label} closed.");

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
                logger.LogInformation($"Transcript done: {done.Transcript}");
            }
        });
    }
}
