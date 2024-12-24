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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

namespace demo
{
    class Program
    {
        private const string OPENAPI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions";
        private const string OPENAPI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
        private const string OPENAPI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
        private const string OPENAPI_DATACHANNEL_NAME = "oai-events";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC OpenAPI Demo Program");
            Console.WriteLine("Press ctrl-c to exit.");

            if(args.Length != 1)
            {
                Console.WriteLine("Please provide your OpenAPI key as a command line argument. It's used to get the single use ephemeral secret for the WebRTC connection.");
                Console.WriteLine("The recommended approach is: set OPENAPIKEY=<your api key>");
                Console.WriteLine("And then: dotnet run %OPENAPIKEY%");
                return;
            }

            var ephemeralKey = await CreateEphemeralKeyAsync(OPENAPI_REALTIME_SESSIONS_URL, args[0], OPENAPI_MODEL, "verse");

            if(string.IsNullOrWhiteSpace(ephemeralKey))
            {
                Console.WriteLine("Failed to get ephemeral key.");
                return;
            }

            logger = AddConsoleLogger();

            var peerConnection = await CreatePeerConnection();

            var offerSdp = peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);

            logger.LogDebug($"SDP offer:");
            logger.LogDebug(offerSdp.sdp);

            var answerSdp = await GetOpenApiAnswerSdpAsync(ephemeralKey, offerSdp.sdp);

            logger.LogDebug($"SDP answer:");
            logger.LogDebug(answerSdp);

            var setAnswerResult = peerConnection.setRemoteDescription(new RTCSessionDescriptionInit { sdp = answerSdp, type = RTCSdpType.answer });

            logger.LogInformation($"Set answer result {setAnswerResult}.");

            //var openApiDataChannel = peerConnection.DataChannels.FirstOrDefault(x => x.label == OPENAPI_DATACHANNEL_NAME);

            // Plumbing code to facilitate a graceful exit.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            // Ctrl-c will gracefully exit the app at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the atempt failed or was cancelled with ctrl-c.
            exitMre.WaitOne();
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

            if(serverEvent != null)
            {
                //logger.LogInformation($"Server event ID {serverEvent.EventID} and type {serverEvent.Type}.");

                OpenAIServerEventBase serverEventModel = serverEvent.Type switch
                {
                    "response.audio_transcript.delta" => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDelta>(message, JsonOptions.Default),
                    "response.audio_transcript.done" => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDone>(message, JsonOptions.Default),
                    _ => null
                };

                if(serverEventModel is OpenAIResponseAudioTranscriptDone)
                {
                    logger.LogInformation($"Transcript done: {(serverEventModel as OpenAIResponseAudioTranscriptDone)?.Transcript}.");
                }
            }
            else
            {
                logger.LogWarning($"Failed to parse server event for: {message}");
            }
        }

        private static async Task<string> CreateEphemeralKeyAsync(string sessionsUrl, string openApiToken, string model, string voice)
        {
            HttpClient httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", openApiToken);

            //OpenAI.RealtimeConversation.ConversationItemCreatedUpdate conversationItemCreatedUpdate = new OpenAI.RealtimeConversation.ConversationItemCreatedUpdate
            //{
            //    type = "conversation.create",
            //    conversation = new OpenAI.RealtimeConversation.ConversationDetails
            //    {
            //        model = model,
            //        voice = voice
            //    }
            //};

            // Create the request body
            var requestBody = new
            {
                model = model,
                voice = voice
            };

            // Serialize the request body to JSON
            string jsonRequestBody = JsonSerializer.Serialize(requestBody);

            // Create the content with appropriate headers
            var content = new StringContent(jsonRequestBody, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // Make the POST request
            var response = await httpClient.PostAsync(sessionsUrl, content);

            // Check for success
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError($"Failed to create ephemeral key. Status code {response.StatusCode}.");
                logger.LogError(await response.Content.ReadAsStringAsync());
                return string.Empty;
            }

            // Read the response body (assumed to contain the ephemeral key)
            string responseContent = await response.Content.ReadAsStringAsync();

            try
            {
                // Deserialize the response JSON
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

                // Extract the client_secret.value
                string clientSecret = responseJson
                    .GetProperty("client_secret")
                    .GetProperty("value")
                    .GetString();

                logger.LogInformation("Ephemeral key created successfully.");
                return clientSecret;
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to parse client secret: {ex.Message}");
                return string.Empty;
            }
        }

        private static async Task<string> GetOpenApiAnswerSdpAsync(string ephemeralKey, string offerSdp)
        {
            HttpClient httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ephemeralKey);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{OPENAPI_REALTIME_BASE_URL}?model={OPENAPI_MODEL}");

            var content = new StringContent(offerSdp, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/sdp");

            request.Content = content;

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError($"Failed to get answer SDP. Status code {response.StatusCode}.");
                logger.LogError(await response.Content.ReadAsStringAsync());
                return null;
            }

            // The response should contain the answer SDP.
            string answerSdp = await response.Content.ReadAsStringAsync();
            return answerSdp;
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
