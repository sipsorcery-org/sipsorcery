//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that can be used to interact with
// OpenAI's real-time API https://platform.openai.com/docs/guides/realtime-webrtc.
//
// NOTE: As of 24 Dec 2024 this example does work to establish an audio stream and is
// able to receive data channel messages. There is no echo cancellation feature in this
// demo so if not provided by the OS then ChatGPT will end up talking to itself.
//
// NOTE: As of 24 Dec 2024 the official OpenAI dotnet SDK is missing the realtime
// models that represent the JSON datachannel messages. As such some rudimentary
// models have been created.
// The official SDK is available at https://github.com/openai/openai-dotnet.
// The OpenAI API realtime server events reference is available at
// https://platform.openai.com/docs/api-reference/realtime-server-events.
//
// Remarks:
// To get the ephemeral secret you first need an API key from OpenAI at
// https://platform.openai.com/settings/organization/api-keys.
//
// If you don't want to pass your OpenAI API key to this app an alternative approach is
// to create an ephemeral secret using the curl comamnd below and then hard code it into
// the application.
// NOTE each ephemeral key seems like it can ONLY be used once:
// curl -v https://api.openai.com/v1/realtime/sessions ^
//  --header "Authorization: Bearer %OPENAI_TOKEN%" ^
//  --header "Content-Type: application/json" ^
//  --data "{\"model\": \"gpt-4o-realtime-preview-2024-12-17\", \"voice\": \"verse\"}"
//
// Usage:
// set OPENAIKEY=your_openai_key
// dotnet run %OPENAIKEY%
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 19 Dec 2024	Aaron Clauson	Created, Dublin, Ireland.
// 28 Dec 2024  Aaron Clauson   Switched to functional approach for The Craic.
// 17 Jan 2025  Aaron Clauson   Added create resposne data channel message to trigger conversation start.
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
using SIPSorceryMedia.Windows;
using LanguageExt;
using LanguageExt.Common;

namespace demo;

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
    private const OpenAIVoicesEnum OPENAI_VOICE = OpenAIVoicesEnum.shimmer;
    private const string OPENAI_DATACHANNEL_NAME = "oai-events";

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

        if (args.Length != 1)
        {
            Console.WriteLine("Please provide your OpenAI key as a command line argument. It's used to get the single use ephemeral secret for the WebRTC connection.");
            Console.WriteLine("The recommended approach is to use an environment variable, for example: set OPENAIKEY=<your openai api key>");
            Console.WriteLine("Then execute the application using: dotnet run %OPENAIKEY%");
            return;
        }

        var flow = await Prelude.Right<Error, Unit>(default)
            .BindAsync(_ =>
            {
                logger.LogInformation("STEP 1: Get ephemeral key from OpenAI.");
                return OpenAIRealtimeRestClient.CreateEphemeralKeyAsync(OPENAI_REALTIME_SESSIONS_URL, args[0], OPENAI_MODEL, OPENAI_VOICE);
            })
            .BindAsync(async ephemeralKey =>
            {
                logger.LogDebug("STEP 2: Create WebRTC PeerConnection & get local SDP offer.");

                var onConnectedSemaphore = new SemaphoreSlim(0, 1);
                var pc = await CreatePeerConnection(onConnectedSemaphore);
                var offer = pc.createOffer();
                await pc.setLocalDescription(offer);

                logger.LogDebug("SDP offer:");
                logger.LogDebug(offer.sdp);

                return Prelude.Right<Error, PcContext>(
                    new PcContext(pc, onConnectedSemaphore, ephemeralKey, offer.sdp, string.Empty)
                );
            })
            .BindAsync(async ctx =>
            {
                logger.LogInformation("STEP 3: Send SDP offer to OpenAI REST server & get SDP answer.");

                var answerEither = await OpenAIRealtimeRestClient.GetOpenAIAnswerSdpAsync(ctx.EphemeralKey, OPENAI_REALTIME_BASE_URL, OPENAI_MODEL, ctx.OfferSdp);
                return answerEither.Map(answer => ctx with { AnswerSdp = answer });
            })
            .BindAsync(ctx =>
            {
                logger.LogInformation("STEP 4: Set remote SDP");

                logger.LogDebug("SDP answer:");
                logger.LogDebug(ctx.AnswerSdp);

                var setAnswerResult = ctx.Pc.setRemoteDescription(
                    new RTCSessionDescriptionInit { sdp = ctx.AnswerSdp, type = RTCSdpType.answer }
                );
                logger.LogInformation($"Set answer result {setAnswerResult}.");

                return setAnswerResult == SetDescriptionResultEnum.OK ?
                    Prelude.Right<Error, PcContext>(ctx) :
                    Prelude.Left<Error, PcContext>(Error.New("Failed to set remote SDP."));
            })
            .MapAsync(async ctx =>
            {
                logger.LogInformation("STEP 5: Wait for data channel to connect and then trigger conversation.");

                await ctx.PcConnectedSemaphore.WaitAsync();

                // NOTE: If you want to trigger the convesation by using the audio from your microphone comment
                // out this line.
                SendResponseCreate(ctx.Pc.DataChannels.First(), OpenAIVoicesEnum.alloy, "Introduce urself. Keep it short.");

                return ctx;
            })
            .BindAsync(ctx =>
            {
                logger.LogInformation("STEP 6: Wait for ctrl-c to indicate user exit.");

                ManualResetEvent exitMre = new(false);
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    exitMre.Set();
                };
                exitMre.WaitOne();

                ctx.Pc.Close("User exit.");

                return Prelude.Right<Error, PcContext>(ctx);
            });

        flow.Match(
            Left: prob => Console.WriteLine($"There was a problem setting up the connection. {prob.Message}"),
            Right: _ => Console.WriteLine("The call was successful.")
        );
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

        // Sink (speaker) only audio end point.
        WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(new AudioEncoder(includeOpus: true), -1, -1, false, false);
        windowsAudioEP.RestrictFormats(x => x.FormatName == "OPUS");
        windowsAudioEP.OnAudioSinkError += err => logger.LogWarning($"Audio sink error. {err}.");
        windowsAudioEP.OnAudioSourceEncodedSample +=  peerConnection.SendAudio;

        MediaStreamTrack audioTrack = new MediaStreamTrack(windowsAudioEP.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
        peerConnection.addTrack(audioTrack);

        peerConnection.OnAudioFormatsNegotiated += (audioFormats) =>
        {
            logger.LogDebug($"Audio format negotiated {audioFormats.First().FormatName}.");
            windowsAudioEP.SetAudioSinkFormat(audioFormats.First());
            windowsAudioEP.SetAudioSourceFormat(audioFormats.First());
        };
        //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
        //peerConnection.OnSendReport += RtpSession_OnSendReport;
        peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
        peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
        peerConnection.onconnectionstatechange += async (state) =>
        {
            logger.LogDebug($"Peer connection connected changed to {state}.");

            if (state == RTCPeerConnectionState.connected)
            {
                await windowsAudioEP.StartAudio();
                await windowsAudioEP.StartAudioSink();
            }
            else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
            {
                await windowsAudioEP.CloseAudio();
            }
        };

        peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
        {
            //logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}.");

            if (media == SDPMediaTypesEnum.audio)
            {
                windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
            }
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
