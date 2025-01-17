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
// models that represent the JSON datachannel messages. As such some ruidimentary
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
// NOTE each epehmeral key seems like it can ONLY be used once:
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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using LanguageExt;

namespace demo
{
    record Problem(string detail);

    record PcContext(
       RTCPeerConnection Pc,
       SemaphoreSlim PcConnectedSemaphore,
       string EphemeralKey = "",
       string OfferSdp = "",
       string AnswerSdp = ""
    );

    enum VoicesEnum
    {
        alloy,
        ash,
        ballad,
        coral,
        echo,
        sage,
        shimmer,
        verse
    }

    class Program
    {
        private const string OPENAI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions";
        private const string OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
        private const string OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
        private const VoicesEnum OPENAI_VERSE = VoicesEnum.shimmer;
        private const string OPENAI_DATACHANNEL_NAME = "oai-events";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

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

            logger = AddConsoleLogger();

            var flow = await Prelude.Right<Problem, Unit>(default)
                .BindAsync(_ =>
                {
                    logger.LogInformation("STEP 1: Get ephemeral key from OpenAI.");
                    return CreateEphemeralKeyAsync(OPENAI_REALTIME_SESSIONS_URL, args[0], OPENAI_MODEL, OPENAI_VERSE.ToString());
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

                    return Prelude.Right<Problem, PcContext>(
                        new PcContext(pc, onConnectedSemaphore, ephemeralKey, offer.sdp, string.Empty)
                    );
                })
                .BindAsync(async ctx =>
                {
                    logger.LogInformation("STEP 3: Send SDP offer to OpenAI REST server & get SDP answer.");

                    var answerEither = await GetOpenAIAnswerSdpAsync(ctx.EphemeralKey, ctx.OfferSdp);
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
                        Prelude.Right<Problem, PcContext>(ctx) :
                        Prelude.Left<Problem, PcContext>(new Problem("Failed to set remote SDP."));
                })
                .MapAsync(async ctx =>
                {
                    logger.LogInformation("STEP 5: Wait for data channel to connect and then trigger conversation.");

                    await ctx.PcConnectedSemaphore.WaitAsync();

                    // NOTE: If you want to trigger the convesation by using the audio from your microphone comment
                    // out this line.
                    SendResponseCreate(ctx.Pc.DataChannels.First(), VoicesEnum.alloy, "Introduce urself.");

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

                    return Prelude.Right<Problem, PcContext>(ctx);
                });

            flow.Match(
                Left: prob => Console.WriteLine($"There was a problem setting up the connection. {prob.detail}"),
                Right: _ => Console.WriteLine("The call was successful.")
            );
        }

        /// <summary>
        /// Sends a response create message to the OpenAI data channel to trigger the conversation.
        /// </summary>
        private static void SendResponseCreate(RTCDataChannel dc, VoicesEnum voice, string message)
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
                    peerConnection.OnReceiveReport -= RtpSession_OnReceiveReport;
                    peerConnection.OnSendReport -= RtpSession_OnSendReport;

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

            if (serverEvent != null)
            {
                //logger.LogInformation($"Server event ID {serverEvent.EventID} and type {serverEvent.Type}.");

                Option<OpenAIServerEventBase> serverEventModel = serverEvent.Type switch
                {
                    "response.audio_transcript.delta" => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDelta>(message, JsonOptions.Default),
                    "response.audio_transcript.done" => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDone>(message, JsonOptions.Default),
                    _ => Option<OpenAIServerEventBase>.None
                };

                serverEventModel.IfSome(e =>
                {
                    if (e is OpenAIResponseAudioTranscriptDone done)
                    {
                        logger.LogInformation($"Transcript done: {done.Transcript}");
                    }
                });
            }
            else
            {
                logger.LogWarning($"Failed to parse server event for: {message}");
            }
        }

        /// <summary>
        /// Completes the steps required to get an ephemeral key from the OpenAI REST server. The ephemeral key is needed
        /// to send an SDP offer, and get the SDP answer.
        /// </summary>
        private static async Task<Either<Problem, string>> CreateEphemeralKeyAsync(string sessionsUrl, string openAIToken, string model, string voice)
            => (await SendHttpPostAsync(
                sessionsUrl,
                openAIToken,
                JsonSerializer.Serialize(
                    new
                    {
                        model,
                        voice
                    }),
                  "application/json"))
            .Bind(responseContent =>
                JsonSerializer.Deserialize<JsonElement>(responseContent)
                    .GetProperty("client_secret")
                    .GetProperty("value")
                    .GetString() ??
                Prelude.Left<Problem, string>(new Problem("Failed to get ephemeral secret."))
            );

        /// <summary>
        /// Attempts to get the SDP answer from the OpenAI REST server. This is the way OpenAI does the signalling. The
        /// ICE candidates will be returned in the SDP answer and are publicly accessible IP's.
        /// </summary>
        /// <remarks>
        /// See https://platform.openai.com/docs/guides/realtime-webrtc#creating-an-ephemeral-token.
        /// </remarks>
        private static Task<Either<Problem, string>> GetOpenAIAnswerSdpAsync(string ephemeralKey, string offerSdp)
            => SendHttpPostAsync(
                $"{OPENAI_REALTIME_BASE_URL}?model={OPENAI_MODEL}",
                ephemeralKey,
                offerSdp,
                "application/sdp");

        /// <summary>
        /// Helper method to send an HTTP psot request with the required headers.
        /// </summary>
        private static async Task<Either<Problem, string>> SendHttpPostAsync(
            string url,
            string token,
            string body,
            string contentType)
        {
            using var httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            var response = await httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return new Problem($"HTTP POST to {url} failed: {response.StatusCode}. Error body: {errorBody}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP sent BYE {mediaType}.");
            }
            else if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                logger.LogDebug($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                if (sentRtcpReport.ReceiverReport.ReceptionReports?.Count > 0)
                {
                    var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                    logger.LogDebug($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
                }
                else
                {
                    logger.LogDebug($"RTCP sent RR {mediaType}, no packets sent or received.");
                }
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            if (recvRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP recv BYE {mediaType}.");
            }
            else
            {
                var rr = recvRtcpReport.ReceiverReport?.ReceptionReports?.FirstOrDefault();
                if (rr != null)
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
                }
                else
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
                }
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
