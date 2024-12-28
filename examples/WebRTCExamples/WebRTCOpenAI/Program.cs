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
// NOTE: As of 24 Dec 2024 the official OpenAPI dotnet SDK is missing the realtime
// models that represent the JSON datachannel messages. As such some ruidimentary
// models have been created.
// The official SDK is available at https://github.com/openai/openai-dotnet.
// The OpenAPI API realtime server events reference is available at
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
//  --header "Authorization: Bearer %OPENAPI_TOKEN%" ^
//  --header "Content-Type: application/json" ^
//  --data "{\"model\": \"gpt-4o-realtime-preview-2024-12-17\", \"voice\": \"verse\"}"
//
// Usage:
// set OPENAPIKEY=your_openapi_key
// dotnet run %OPENAPIKEY%
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 19 Dec 2024	Aaron Clauson	Created, Dublin, Ireland.
// 28 Dec 2024  Aaron Clauson   Switched to functional approach for the craic.
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
       string EphemeralKey = "",
       string OfferSdp = "",
       string AnswerSdp = ""
    );

    class Program
    {
        private const string OPENAPI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions";
        private const string OPENAPI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
        private const string OPENAPI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
        private const string OPENAPI_VERSE = "shimmer"; // Supported values are: 'alloy', 'ash', 'ballad', 'coral', 'echo', 'sage', 'shimmer', and 'verse'.
        private const string OPENAPI_DATACHANNEL_NAME = "oai-events";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC OpenAPI Demo Program");
            Console.WriteLine("Press ctrl-c to exit.");

            if (args.Length != 1)
            {
                Console.WriteLine("Please provide your OpenAPI key as a command line argument. It's used to get the single use ephemeral secret for the WebRTC connection.");
                Console.WriteLine("The recommended approach is to use an environment variable, for example: set OPENAPIKEY=<your api key>");
                Console.WriteLine("Then execute the application using: dotnet run %OPENAPIKEY%");
                return;
            }

            logger = AddConsoleLogger();

            var flow = await CreateEphemeralKeyAsync(OPENAPI_REALTIME_SESSIONS_URL, args[0], OPENAPI_MODEL, OPENAPI_VERSE)
                .BindAsync(async ephemeralKey =>
                {
                    logger.LogDebug("STEP 1: Create WebRTC PeerConnection & get SDP offer.");

                    var pc = await CreatePeerConnection();
                    var offer = pc.createOffer();
                    await pc.setLocalDescription(offer);

                    logger.LogDebug("SDP offer:");
                    logger.LogDebug(offer.sdp);

                    return Prelude.Right<Problem, PcContext>(
                        new PcContext(pc, ephemeralKey, offer.sdp, string.Empty)
                    );
                })
                .BindAsync(async ctx =>
                {
                    logger.LogDebug("STEP 2: Send offer to OpenAI REST server & get SDP answer."); 

                    var answerEither = await GetOpenApiAnswerSdpAsync(ctx.EphemeralKey, ctx.OfferSdp);
                    return answerEither.Map(answer => ctx with { AnswerSdp = answer });
                })
                .BindAsync(ctx =>
                {
                    logger.LogDebug("STEP 3: Set remote SDP & wait for ctrl-c to indicate exit.");

                    logger.LogDebug("SDP answer:");
                    logger.LogDebug(ctx.AnswerSdp);

                    var setAnswerResult = ctx.Pc.setRemoteDescription(
                        new RTCSessionDescriptionInit { sdp = ctx.AnswerSdp, type = RTCSdpType.answer }
                    );
                    logger.LogInformation($"Set answer result {setAnswerResult}.");

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

            // Finally, handle success or failure
            flow.Match(
                Left: prob => Console.WriteLine($"There was a porblem setting up the connection. {prob.detail}"),
                Right: _ => Console.WriteLine("All steps succeeded!")
            );
       }

        private static async Task<RTCPeerConnection> CreatePeerConnection()
        {
            var pcConfig = new RTCConfiguration
            {
                X_UseRtpFeedbackProfile = true,
            };

            var peerConnection = new RTCPeerConnection(pcConfig);
            var dataChannel = await peerConnection.createDataChannel(OPENAPI_DATACHANNEL_NAME);

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
                logger.LogDebug("OpenAPI data channel opened.");
            };

            dataChannel.onclose += () => logger.LogDebug($"OpenAPI data channel {dataChannel.label} closed.");

            dataChannel.onmessage += OnDataChannelMessage;

            return peerConnection;
        }

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

        private static async Task<Either<Problem, string>> CreateEphemeralKeyAsync(string sessionsUrl, string openApiToken, string model, string voice)
            => (await SendHttpPostAsync(
                sessionsUrl,
                openApiToken,
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

        private static Task<Either<Problem, string>> GetOpenApiAnswerSdpAsync(string ephemeralKey, string offerSdp)
            => SendHttpPostAsync(
                $"{OPENAPI_REALTIME_BASE_URL}?model={OPENAPI_MODEL}",
                ephemeralKey,
                offerSdp,
                "application/sdp");

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
