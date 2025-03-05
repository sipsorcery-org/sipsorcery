//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that eastablishes two connections
// with the OpenAI realtime endpoint and wires up their audio together. An OpenGL
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
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.Net;
using AudioScope;
using System.Numerics;
using System.Text;
using SIPSorceryMedia.Windows;
using LanguageExt;
using LanguageExt.Common;

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
   string CallLabel,
   RTCPeerConnection Pc,
   WindowsAudioEndPoint AudioEndPoint,
   SemaphoreSlim PcConnectedSemaphore
);

class Program
{
    private const int AUDIO_PACKET_DURATION = 20; // 20ms of audio per RTP packet for PCMU & PCMA.

    private const string OPENAI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions";
    private const string OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
    private const string OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
    private const string OPENAI_DATACHANNEL_NAME = "oai-events";
    private const string ALICE_CALL_LABEL = "Alice";
    private const string BOB_CALL_LABEL = "Bob";

    private static Microsoft.Extensions.Logging.ILogger logger = LoggerFactory.Create(builder =>
        builder.AddSerilog(new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger()))
        .CreateLogger<Program>();

    private static FormAudioScope? _audioScopeForm;

    static async Task Main(string[] args)
    {
        Console.WriteLine("WebRTC OpenAI Alice & Bob Demo");

        if (args.Length != 1)
        {
            Console.WriteLine("Please provide your OpenAI key as a command line argument. "+
                "It's used to get the single use ephemeral secret for the WebRTC connection.");
            Console.WriteLine("The recommended approach is to use an environment variable, " +
                "for example: set OPENAIKEY=<your openai api key>");
            Console.WriteLine("Then execute the application using: dotnet run %OPENAIKEY%");
            return;
        }

        // Spin up a dedicated STA thread to run WinForms for the Audio Scope.
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

        // Reset event to keep the console thread alive until we're ready to exit.
        ManualResetEvent exitMre = new ManualResetEvent(false);

        // Get the OpenAI key from the command line arguments. Note this is the API key that's used to get the
        // ephemeral key (apparently this main API key can be used instead of the ephemeral key but I only found
        // that out afterwards).
        string openAIKey = args[0];

        // Initialise two peer connection contexts for Alice & Bob.
        var aliceInitialContext = new InitPcContext(
                ALICE_CALL_LABEL,          
                string.Empty,
                string.Empty,
                string.Empty,
                OpenAIVoicesEnum.shimmer,
                1);

        var bobInitialContext = new InitPcContext(
                BOB_CALL_LABEL,
                string.Empty,
                string.Empty,
                string.Empty,
                OpenAIVoicesEnum.ash,
                2);

        var aliceCallTask = InitiatePeerConnection(openAIKey, aliceInitialContext);
        var bobCallTask = InitiatePeerConnection(openAIKey, bobInitialContext);

        _ = await Task.WhenAll(aliceCallTask, bobCallTask);

        var combinedCtx = from aliceCtx in aliceCallTask.Result
                       from bobCtx in bobCallTask.Result
                       select (aliceCtx, bobCtx);

        if (combinedCtx.IsLeft)
        {
            logger.LogError($"There was a problem initiating the connections. {((Error)combinedCtx).Message}");
            exitMre.Set();
        }
        else
        {
            logger.LogInformation($"Both calls successfully initiated, waiting for them to connect...");

            var aliceConncectedCtx = (PcContext)aliceCallTask.Result;
            var bobConnectedCtx = (PcContext)bobCallTask.Result;

            // Wait until both calls have their data channels connected which can only happen if the underlying
            // peer connections are also connected.
            var waitForAlice = aliceConncectedCtx.PcConnectedSemaphore.WaitAsync();
            var waitForBob = bobConnectedCtx.PcConnectedSemaphore.WaitAsync();

            await Task.WhenAll(waitForAlice, waitForBob);

            logger.LogInformation($"Both calls successfully connected.");

            // Send Alice's audio to Bob.
            aliceConncectedCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                bobConnectedCtx.Pc.SendAudio(AUDIO_PACKET_DURATION, rtpPkt.Payload);
            };

            // Send Bob's audio to Alice.
            bobConnectedCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                aliceConncectedCtx.Pc.SendAudio(AUDIO_PACKET_DURATION, rtpPkt.Payload);
            };

            // Trigger the conversation by getting Alice to say something witty.
            var aliceDataChannel = aliceConncectedCtx.Pc.DataChannels.Where(x => x.label == OPENAI_DATACHANNEL_NAME).Single();

            //if (aliceDataChannel != null)
            //{
            //    SendResponseCreate(aliceDataChannel, OpenAIVoicesEnum.shimmer, "Only talk in cheesy puns. Keep it short once you'vegot you pun in. To start the conversation please repeat repeat this phrase in your corniest accent: 'You're a few tinnies short of a six-pack.'");
            //}

            logger.LogInformation($"ctrl-c to exit..");

            // Ctrl-c will gracefully exit.
            Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                aliceConncectedCtx.Pc.Close("User exit");
                bobConnectedCtx.Pc.Close("User exit");

                exitMre.Set();
            };
        }

        // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
        exitMre.WaitOne();

        Console.WriteLine("Exiting...");

        _audioScopeForm?.Invoke(() => _audioScopeForm.Close());
    }

    /// <summary>
    /// Initiaites the creation and media session wiring for a local peer connection.
    /// </summary>
    private static Task<Either<Error, PcContext>> InitiatePeerConnection(string openAIKey, InitPcContext ctx)
        => InitiatePeerConnectionWithOpenAI(openAIKey, ctx)
            .BindAsync(async createdPcCtx =>
            {
                var audioEncoder = new AudioEncoder(includeOpus: true);
                WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(audioEncoder, -1, -1, true, false);
                windowsAudioEP.OnAudioSinkError += err => logger.LogWarning($"Audio sink error. {err}.");
                var opusOnly = audioEncoder.SupportedFormats.Where(x => x.FormatName == "OPUS").Single();

                windowsAudioEP.SetAudioSinkFormat(opusOnly);

                // Can be used to send microphone input to the remote peer connection.
                //windowsAudioEP.SetAudioSourceFormat(opusOnly);
                //windowsAudioEP.OnAudioSourceEncodedSample += createdPcCtx.Pc.SendAudio;
                //await windowsAudioEP.StartAudio();

                createdPcCtx.Pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
                {
                    if (media == SDPMediaTypesEnum.audio)
                    {
                        windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);

                        var decodedSample = audioEncoder.DecodeAudio(rtpPkt.Payload, opusOnly);

                        var samples = decodedSample
                            .Select(s => new Complex(s / 32768f, 0f))
                            .ToArray();

                        var frame = _audioScopeForm?.Invoke(() => _audioScopeForm.ProcessAudioSample(samples, ctx.AudioScopeNumber));
                    }
                };

                await windowsAudioEP.StartAudioSink();

                logger.LogInformation($"{createdPcCtx.CallLabel} call successfully intiated, waiting for connect...");

                return Prelude.Right<Error, PcContext>(new (
                    createdPcCtx.CallLabel,
                    createdPcCtx.Pc,
                    windowsAudioEP,
                    createdPcCtx.PcConnectedSemaphore));
            });

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
                var pc = await CreatePeerConnection(withkeyCtx.CallLabel, onConnectedSemaphore);
                var offer = pc.createOffer();
                await pc.setLocalDescription(offer);

                logger.LogDebug("SDP offer:");
                logger.LogDebug(offer.sdp);

                return Prelude.Right<Error, CreatedPcContext>(new (
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
    /// Method to create the local peer connection instance and data channel.
    /// </summary>
    /// <param name="callLabel">A friendly label to identify the peer conenction. Helps in this app as there are two local peer connections.</param>
    /// <param name="onDcConnected">A semaphore that will get set when the data channel on the peer connection is opened. Since the data channel
    /// can only be opened once the peer connection is open this indicates both are ready for use.</param>
    private static async Task<RTCPeerConnection> CreatePeerConnection(string callLabel, SemaphoreSlim onDcConnected)
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
            onDcConnected.Release();
        };

        dataChannel.onclose += () => logger.LogDebug($"{callLabel} OpenAI data channel {dataChannel.label} closed.");

        dataChannel.onmessage += (dc, protocol, data) => OnDataChannelMessage(dc, protocol, data, callLabel);

        return peerConnection;
    }

    /// <summary>
    /// Event handler for WebRTC data channel messages.
    /// </summary>
    private static void OnDataChannelMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data, string callLabel)
    {
        //logger.LogInformation($"Data channel {dc.label}, protocol {protocol} message length {data.Length}.");

        var message = Encoding.UTF8.GetString(data);

        //logger.LogDebug(message);

        var serverEventModel = OpenAIDataChannelManager.ParseDataChannelMessage(data);

        serverEventModel.IfSome(e =>
        {
            switch (e)
            {
                case OpenAISessionCreated sessionCreated:
                    //logger.LogInformation($"Session created: {sessionCreated.ToJson()}");
                    OnSessionCreated(dc);
                    if (callLabel == ALICE_CALL_LABEL)
                    {
                        SendResponseCreate(dc, OpenAIVoicesEnum.alloy, "Tell me your first Dad joke.");
                    }
                    break;

                case OpenAISessionUpdated sessionUpdated:
                    logger.LogInformation($"Session updated: {sessionUpdated.ToJson()}");
                    break;

                case OpenAIResponseAudioTranscriptDone transcriptionDone:
                    logger.LogInformation($"Transcript done: {transcriptionDone.Transcript}");
                    break;

                default:
                    //logger.LogInformation($"Data Channel {e.Type} message received.");
                    break;
            }
        });

        if (serverEventModel.IsNone)
        {
            logger.LogWarning($"Failed to parse server event for: {message}");
        }
    }

    /// <summary>
    /// Sends a session update message to add the get weather demo function.
    /// </summary>
    private static void OnSessionCreated(RTCDataChannel dc)
    {
        var sessionUpdate = new OpenAISessionUpdate
        {
            EventID = Guid.NewGuid().ToString(),
            Session = new OpenAISession
            {
                Model = OPENAI_MODEL,
                Instructions = "You are a joke bot. Tell a Dad joke every chance you get.",
            }
        };

        logger.LogInformation($"Sending OpenAI session update to data channel {dc.label}.");
        logger.LogDebug(sessionUpdate.ToJson());

        dc.send(sessionUpdate.ToJson());
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
}
