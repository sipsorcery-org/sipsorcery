//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that can be used to interact with
// ChatGPT's real-time API https://platform.openai.com/docs/guides/realtime-webrtc.
//
// NOTE: As of 19 Dec 2024 this examle has never worked. It can establish the WebRTC connection
// but does not get a response on the data channel. Testing with a js version in Chrome
// was only able to randomly get one successful data channel response from 20 or 30
// attempts so seems the OpenAI end may be having capacity issues.
//
// Remarks:
// To get the ephemeral secret you first need an API key from OpenAI at
// https://platform.openai.com/settings/organization/api-keys.
//
// The API key can then be used to create an ephemeral secret using the curl comamnd below,
// NOTE each epehmeral key seems like it can ONLY be used once:
// curl -v https://api.openai.com/v1/realtime/sessions ^
//  --header "Authorization: Bearer %OPENAPI_TOKEN%" ^
//  --header "Content-Type: application/json" ^
//  --data "{\"model\": \"gpt-4o-realtime-preview-2024-12-17\", \"voice\": \"verse\"}"
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

namespace demo
{
    public class ResponseCreate
    {
        public string type { get; set; }
        public ResponseDetails response { get; set; }
    }

    public class ResponseDetails
    {
        public List<string> modalities { get; set; }
        public string instructions { get; set; }
    }

    class Program
    {
        private const string OPENAPI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
        private const string OPENAPI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
        private const string OPENAPI_DATACHANNEL_NAME = "oai-events";

        /// <summary>
        /// Ephemeral secrets can only be used once.
        /// </summary>
        private const string EPHEMERAL_SECRET = "xxx";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static async Task Main()
        {
            Console.WriteLine("WebRTC ChatGPT Demo Program");
            Console.WriteLine("Press ctrl-c to exit.");

            logger = AddConsoleLogger();

            var peerConnection = await CreatePeerConnection();

            var offerSdp = peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);

            logger.LogDebug($"SDP offer:");
            logger.LogDebug(offerSdp.sdp);

            var answerSdp = await GetOpenApiAnswerSdpAsync(EPHEMERAL_SECRET, offerSdp.sdp);

            logger.LogDebug($"SDP answer:");
            logger.LogDebug(answerSdp);

            var setAnswerResult = peerConnection.setRemoteDescription(new RTCSessionDescriptionInit { sdp = answerSdp, type = RTCSdpType.answer });

            logger.LogInformation($"Set answer result {setAnswerResult}.");

            var openApiDataChannel = peerConnection.DataChannels.FirstOrDefault(x => x.label == OPENAPI_DATACHANNEL_NAME);

            openApiDataChannel.onopen += () =>
            {
                logger.LogDebug("OpenAPI data channel now available, sending test message.");

                var responseCreate = new ResponseCreate
                {
                    type = "response.create",
                    response = new ResponseDetails
                    {
                        modalities = new List<string> { "text" },
                        instructions = "Write a haiku about code"
                    }
                };

                logger.LogDebug($"Sending response.create message: {System.Text.Json.JsonSerializer.Serialize(responseCreate)}.");

                openApiDataChannel.send(System.Text.Json.JsonSerializer.Serialize(responseCreate));
            };

            openApiDataChannel.onmessage += (datachan, type, data) =>
            {
                logger.LogInformation($"Data channel {datachan.label} message {type} received: {Encoding.UTF8.GetString(data)}.");
            };

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static async Task<RTCPeerConnection> CreatePeerConnection()
        {
            var peerConnection = new RTCPeerConnection(null);
            await peerConnection.createDataChannel(OPENAPI_DATACHANNEL_NAME);

            // Sink (speaker) only audio end point.
            WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(new AudioEncoder(), -1, -1, true, false);
            windowsAudioEP.OnAudioSinkError += err => logger.LogWarning($"Audio sink error. {err}.");

            var audioFormats = new List<AudioFormat> { new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU) };
            MediaStreamTrack audioTrack = new MediaStreamTrack(audioFormats, MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(audioTrack);

            peerConnection.OnAudioFormatsNegotiated += (audioFormats) => windowsAudioEP.SetAudioSinkFormat(audioFormats.First());
            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;
            peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
            peerConnection.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection connected changed to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
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
                logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}.");

                if (media == SDPMediaTypesEnum.audio)
                {
                    windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
                }
            };

            peerConnection.ondatachannel += (rdc) =>
            {
                rdc.onopen += () => logger.LogDebug($"Data channel {rdc.label} opened.");
                rdc.onclose += () => logger.LogDebug($"Data channel {rdc.label} closed.");
                rdc.onmessage += (datachan, type, data) =>
                {
                    switch (type)
                    {
                        case DataChannelPayloadProtocols.WebRTC_Binary_Empty:
                        case DataChannelPayloadProtocols.WebRTC_String_Empty:
                            logger.LogInformation($"Data channel {datachan.label} empty message type {type}.");
                            break;

                        case DataChannelPayloadProtocols.WebRTC_Binary:
                            logger.LogInformation($"Data channel {datachan.label} received {data.Length} binary bytes.");
                            break;

                        case DataChannelPayloadProtocols.WebRTC_String:
                            var msg = Encoding.UTF8.GetString(data);
                            logger.LogInformation($"Data channel {datachan.label} message {type} received: {msg}.");
                            break;
                    }
                };
            };

            return peerConnection;
        }

        private static async Task<string> GetOpenApiAnswerSdpAsync(string ephemeralKey, string offerSdp)
        {
            HttpClient httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ephemeralKey);

            // Prepare the request
            //var request = new HttpRequestMessage(HttpMethod.Post, $"{OPENAPI_REALTIME_BASE_URL}?model={OPENAPI_MODEL}")
            //{
            //    Content = new StringContent(offerSdp, Encoding.UTF8, "application/sdp")
            //};

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

            // The response should contain the answer SDP
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
