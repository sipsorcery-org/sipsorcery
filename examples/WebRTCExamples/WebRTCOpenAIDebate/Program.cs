//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that eastablishes two conversations
// with the OpenAI realtime endpoint and gets them to talk to each other. An OpenGL
// visualisation is used to show which agent is talking.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Jan 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using AudioScope;
using System.Numerics;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using SIPSorceryMedia.Windows;
using LanguageExt;

namespace demo
{
    record Problem(string detail);

    record PcContext(
       RTCPeerConnection Pc,
       string EphemeralKey = "",
       string OfferSdp = "",
       string AnswerSdp = "",
       string CallLabel = ""
    );

    record DialogContext(
      PcContext AlicePcCtx,
      WindowsAudioEndPoint AliceAudioEP,
      PcContext? BobPcCtx,
      WindowsAudioEndPoint? BobAudioEP);

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
        private const int AUDIO_PACKET_DURATION = 20; // 20ms of audio per RTP packet for PCMU & PCMA.

        private const string OPENAI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions";
        private const string OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
        private const string OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
        private const string OPENAI_DATACHANNEL_NAME = "oai-events";
        private const string ALICE_CALL_LABEL = "Alice";
        private const string BOB_CALL_LABEL = "Bob";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static FormAudioScope _audioScopeForm;
        private static RTCPeerConnection _pc;

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC OpenAI Debate Demo");

            logger = AddConsoleLogger();

            if (args.Length != 1)
            {
                Console.WriteLine("Please provide your OpenAI key as a command line argument. It's used to get the single use ephemeral secret for the WebRTC connection.");
                Console.WriteLine("The recommended approach is to use an environment variable, for example: set OPENAIKEY=<your openai api key>");
                Console.WriteLine("Then execute the application using: dotnet run %OPENAIKEY%");
                return;
            }

            // Spin up a dedicated STA thread to run WinForms.
            Thread uiThread = new Thread(() =>
            {
                // WinForms initialization must be on an STA thread.
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                _audioScopeForm = new FormAudioScope(true);

                Application.Run(_audioScopeForm);
            });

            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();

            var result = await PlaceCallToOpenAI(args[0], ALICE_CALL_LABEL, VoicesEnum.shimmer)
                .BindAsync(async alicePcCtx =>
                {
                    // First call leg to AI Alice.

                    var aliceAudioEncoder = new AudioEncoder(includeOpus: true);
                    WindowsAudioEndPoint aliceWindowsAudioEP = new WindowsAudioEndPoint(aliceAudioEncoder, -1, -1, true, false);
                    aliceWindowsAudioEP.OnAudioSinkError += err => logger.LogWarning($"Audio sink error. {err}.");
                    var opusOnly = aliceAudioEncoder.SupportedFormats.Where(x => x.FormatName == "OPUS").Single();

                    aliceWindowsAudioEP.SetAudioSinkFormat(opusOnly);
                    //aliceWindowsAudioEP.SetAudioSourceFormat(opusOnly);

                    alicePcCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
                    {
                        if (media == SDPMediaTypesEnum.audio)
                        {
                            aliceWindowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);

                            var decodedSample = aliceAudioEncoder.DecodeAudio(rtpPkt.Payload, opusOnly);

                            var samples = decodedSample
                                .Select(s => new Complex(s / 32768f, 0f))
                                .ToArray();

                            var frame = _audioScopeForm.Invoke(() => _audioScopeForm.ProcessAudioSample1(samples));
                        }
                    };

                    //aliceWindowsAudioEP.OnAudioSourceEncodedSample += aliceCtx.Pc.SendAudio;

                    //await aliceWindowsAudioEP.StartAudio();
                    await aliceWindowsAudioEP.StartAudioSink();

                    logger.LogInformation($"{ALICE_CALL_LABEL} call successfully intiated, waiting for connect...");

                    return Prelude.Right<Problem, DialogContext>(new DialogContext(alicePcCtx, aliceWindowsAudioEP, null, null));
                })
                .BindAsync(async dialogCtx =>
                {
                    // Wait for Alice's call to connect.

                    var connectProb = await WaitForConnectOrFail(dialogCtx.AlicePcCtx).ConfigureAwait(false);

                    return connectProb switch
                    {
                        Problem p => Prelude.Left<Problem, DialogContext>(p),
                        _ => Prelude.Right<Problem, DialogContext>(dialogCtx)
                    };
                })
                .BindAsync(async dialogCtx =>
                {
                    var either = await PlaceCallToOpenAI(args[0], BOB_CALL_LABEL, VoicesEnum.ash);

                    return either.Match(
                        bobPcCtx => Prelude.Right<Problem, DialogContext>(
                            dialogCtx with { BobPcCtx = bobPcCtx }
                        ),
                        problem => Prelude.Left<Problem, DialogContext>(problem)
                    );
                })
                .BindAsync(async dialogCtx =>
                 {
                     // Second call leg to AI Bob.

                     var bobAudioEncoder = new AudioEncoder(includeOpus: true);
                     WindowsAudioEndPoint bobWindowsAudioEP = new WindowsAudioEndPoint(bobAudioEncoder, -1, -1, true, false);
                     var opusOnly = bobAudioEncoder.SupportedFormats.Where(x => x.FormatName == "OPUS").Single();

                     bobWindowsAudioEP.SetAudioSinkFormat(opusOnly);

                     dialogCtx.BobPcCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
                     {
                         if (media == SDPMediaTypesEnum.audio)
                         {
                             bobWindowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);

                             var decodedSample = bobAudioEncoder.DecodeAudio(rtpPkt.Payload, opusOnly);

                             var samples = decodedSample
                                 .Select(s => new Complex(s / 32768f, 0f))
                                 .ToArray();

                             var frame = _audioScopeForm.Invoke(() => _audioScopeForm.ProcessAudioSample2(samples));
                         }
                     };

                     await bobWindowsAudioEP.StartAudioSink();

                     logger.LogDebug($"{BOB_CALL_LABEL} call audio source and sink started.");

                     return Prelude.Right<Problem, DialogContext>(dialogCtx with { BobAudioEP = bobWindowsAudioEP });
                 })
                .BindAsync(async diaogCtx =>
                {
                    // Wait for Bob's call to connect.

                    var connectProb = await WaitForConnectOrFail(diaogCtx.BobPcCtx);

                    return connectProb switch
                    {
                        Problem p => Prelude.Left<Problem, DialogContext>(p),
                        _ => Prelude.Right<Problem, DialogContext>(diaogCtx)
                    };
                });

            if (result.IsLeft)
            {
                logger.LogError($"Failed to place call to OpenAI. {((Problem)result).detail}");
                return;
            }
            else
            {
                logger.LogInformation($"Successfully establised both calls.");

                var dc = (DialogContext)result;

                // Send Alice's audio to Bob.
                dc.AlicePcCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
                {
                    dc.BobPcCtx.Pc.SendAudio(AUDIO_PACKET_DURATION, rtpPkt.Payload);
                };

                //// Send Bob's audio to Alice.
                dc.BobPcCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
                {
                    dc.AlicePcCtx.Pc.SendAudio(AUDIO_PACKET_DURATION, rtpPkt.Payload);
                };

                var dataChannel = dc.AlicePcCtx.Pc.DataChannels.First();

                await Task.Delay(2000);

                //dataChannel.onopen += () =>
                //{
                    logger.LogDebug("Alice's data channel opened.");

                    // Trigger the conversation.
                    var responseCreate = new OpenAIResponseCreate
                    {
                        EventID = Guid.NewGuid().ToString(),
                        Response = new OpenAIResponseCreateResponse
                        {
                            //Instructions = "Hi There!  Give me an example of a funny insult that I can use in an English teaching example and that's not disrespectful.",
                            Instructions = "Please repeat repeat this phrase in an Irish accent: 'You're a few kangaroos short in the top paddock mate.'",
                            Voice = VoicesEnum.shimmer.ToString()
                        }
                    };

                    logger.LogInformation($"Sending initial response create to {ALICE_CALL_LABEL} on data channel {dc.AlicePcCtx.Pc.DataChannels.First().label}.");
                    logger.LogDebug(responseCreate.ToJson());

                    dataChannel.send(responseCreate.ToJson());
                //};

                logger.LogInformation($"ctrl-c to exit..");

                // Ctrl-c will gracefully exit the call at any point.
                ManualResetEvent exitMre = new ManualResetEvent(false);
                Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
                {
                    Console.WriteLine("Exiting...");

                    e.Cancel = true;

                    _pc?.Close("User exit");

                    _audioScopeForm?.Invoke(() => _audioScopeForm.Close());

                    exitMre.Set();
                };

                // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
                exitMre.WaitOne();
            }
        }

        private static async Task<Either<Problem, PcContext>> PlaceCallToOpenAI(string openAIKey, string callLabel, VoicesEnum voice)
        {
            return await Prelude.Right<Problem, Unit>(default)
                .BindAsync(_ =>
                {
                    logger.LogInformation($"STEP 1 {callLabel}: Get ephemeral key from OpenAI.");
                    return CreateEphemeralKeyAsync(OPENAI_REALTIME_SESSIONS_URL, openAIKey, OPENAI_MODEL, voice.ToString());
                })
                .BindAsync(async ephemeralKey =>
                {
                    logger.LogDebug($"STEP 2 {callLabel}: Create WebRTC PeerConnection & get local SDP offer.");

                    var pc = await CreatePeerConnectionOpenAI(callLabel);
                    var offer = pc.createOffer();
                    await pc.setLocalDescription(offer);

                    logger.LogDebug("SDP offer:");
                    logger.LogDebug(offer.sdp);

                    return Prelude.Right<Problem, PcContext>(
                        new PcContext(pc, ephemeralKey, offer.sdp, string.Empty, callLabel)
                    );
                })
                .BindAsync(async ctx =>
                {
                    logger.LogInformation($"STEP 3 {callLabel}: Send SDP offer to OpenAI REST server & get SDP answer.");

                    var answerEither = await GetOpenAIAnswerSdpAsync(ctx.EphemeralKey, ctx.OfferSdp);
                    return answerEither.Map(answer => ctx with { AnswerSdp = answer });
                })
                .BindAsync(ctx =>
                {
                    logger.LogInformation($"STEP 4 {callLabel}: Set remote SDP");

                    logger.LogDebug("SDP answer:");
                    logger.LogDebug(ctx.AnswerSdp);

                    var setAnswerResult = ctx.Pc.setRemoteDescription(
                        new RTCSessionDescriptionInit { sdp = ctx.AnswerSdp, type = RTCSdpType.answer }
                    );
                    logger.LogInformation($"Set answer result {setAnswerResult}.");

                    return setAnswerResult == SetDescriptionResultEnum.OK ?
                        Prelude.Right<Problem, PcContext>(ctx) :
                        Prelude.Left<Problem, PcContext>(new Problem("Failed to set remote SDP."));
                });
        }

        private static async Task<Problem?> WaitForConnectOrFail(PcContext pcCtx)
        {
            var semaphore = new SemaphoreSlim(0, 1);
            Problem? result = null;

            if (pcCtx.Pc.connectionState == RTCPeerConnectionState.connected)
            {
                result = null;
                semaphore.Release();
            }
            else if (pcCtx.Pc.connectionState is RTCPeerConnectionState.@new or RTCPeerConnectionState.connecting)
            {
                pcCtx.Pc.onconnectionstatechange += (state) =>
                {
                    logger.LogInformation($"{pcCtx.CallLabel} connection state changed to {state}.");

                    if (state == RTCPeerConnectionState.connected)
                    {
                        result = null;
                        semaphore.Release();
                    }
                    else if(state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed or RTCPeerConnectionState.disconnected)
                    {
                        result = new Problem($"{pcCtx.CallLabel} call failed, connection state {pcCtx.Pc.connectionState}.");
                        semaphore.Release();
                    }
                };
            }
            else
            {
                result = new Problem($"{pcCtx.CallLabel} call failed, connection state {pcCtx.Pc.connectionState}.");
                semaphore.Release();
            }

            await semaphore.WaitAsync().ConfigureAwait(false);

            return result;
        }

        private static async Task<RTCPeerConnection> CreatePeerConnectionOpenAI(string callLabel)
        {
            var pcConfig = new RTCConfiguration
            {
                X_UseRtpFeedbackProfile = true,
            };

            var peerConnection = new RTCPeerConnection(pcConfig);
            var dataChannel = await peerConnection.createDataChannel(OPENAI_DATACHANNEL_NAME);

            var audioEncoder = new AudioEncoder(includeOpus: true);
            var opusOnly = audioEncoder.SupportedFormats.Where(x => x.FormatName == "OPUS").ToList();

            MediaStreamTrack audioTrack = new MediaStreamTrack(opusOnly, MediaStreamStatusEnum.SendRecv);
            peerConnection.addTrack(audioTrack);

            //peerConnection.OnAudioFormatsNegotiated += (audioFormats) => logger.LogDebug($"Audio format negotiated {audioFormats.First().FormatName}.";
            //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            //peerConnection.OnSendReport += RtpSession_OnSendReport;
            peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"{callLabel} Timeout on media {mediaType}.");
            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"{callLabel} ICE connection state changed to {state}.");
            peerConnection.onconnectionstatechange += (state) => logger.LogDebug($"{callLabel} Peer connection connected changed to {state}.");

            dataChannel.onopen += () =>
            {
                logger.LogDebug($"{callLabel} OpenAI data channel opened.");
            };

            dataChannel.onclose += () => logger.LogDebug($"{callLabel} OpenAI data channel {dataChannel.label} closed.");

            dataChannel.onmessage += (dc, protocol, data) => OnDataChannelMessage(dc, protocol, data, callLabel);

            return peerConnection;
        }

        private static void OnDataChannelMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data, string callLabel)
        {
            //logger.LogInformation($"Data channel {dc.label}, protocol {protocol} message length {data.Length}.");

            var message = Encoding.UTF8.GetString(data);

            //logger.LogDebug(message);

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
                        logger.LogInformation($"Transcript done {callLabel}: {done.Transcript}");
                    }
                });
            }
            else
            {
                logger.LogWarning($"Failed to parse server event for: {message}");
            }
        }

        private static async Task<Either<Problem, string>> CreateEphemeralKeyAsync(string sessionsUrl, string openAIToken, string model, string voice)
            => (await SendHttpPostAsync(
                sessionsUrl,
                openAIToken,
                JsonSerializer.Serialize(
                    new
                    {
                        model,
                        voice,
                    }),
                  "application/json"))
            .Bind(responseContent =>
                JsonSerializer.Deserialize<JsonElement>(responseContent)
                    .GetProperty("client_secret")
                    .GetProperty("value")
                    .GetString() ??
                Prelude.Left<Problem, string>(new Problem("Failed to get ephemeral secret."))
            );

        private static Task<Either<Problem, string>> GetOpenAIAnswerSdpAsync(string ephemeralKey, string offerSdp)
            => SendHttpPostAsync(
                $"{OPENAI_REALTIME_BASE_URL}?model={OPENAI_MODEL}",
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
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
