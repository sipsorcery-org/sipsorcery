using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Gemini.Realtime;
using SIPSorcery.Gemini.Realtime.Models;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace demo;

record SipToGeminiCall(SIPUserAgent Ua, RTPSession RtpSession, GeminiLiveEndPoint GeminiEndPoint, AudioEncoder Encoder, Timer PlayoutTimer);

/// <summary>
/// Same SIP-to-Gemini bridge as the GetStartedSIP example, extended to demonstrate function
/// calling: the model is given an "endCall" tool and instructed to invoke it once the caller
/// says goodbye, which this program uses to hang up the SIP call automatically instead of
/// leaving it open until the caller (or a timeout) ends it.
/// </summary>
class Program
{
    private const string ENV_VAR_GEMINI_API_KEY = "GEMINI_API_KEY";
    private const string ENV_VAR_SIP_SERVER = "ASTERISK_SIP_SERVER";
    private const string ENV_VAR_SIP_USERNAME = "ASTERISK_SIP_USERNAME";
    private const string ENV_VAR_SIP_PASSWORD = "ASTERISK_SIP_PASSWORD";

    private const int REGISTRATION_EXPIRY_SECONDS = 120;

    private const string END_CALL_FUNCTION_NAME = "endCall";

    // How long to wait for the Gemini WebSocket connect + setup handshake before giving up. Without
    // this, a hung connect attempt (e.g. a stale/half-closed connection left over from a previous
    // call) fails completely silently — the call is answered but nothing ever happens.
    private static readonly TimeSpan GEMINI_CONNECT_TIMEOUT = TimeSpan.FromSeconds(15);

    // PCMU's RTP clock rate (8000Hz) equals its sample rate, so 1 encoded byte == 1 sample ==
    // 1 RTP timestamp unit — no separate duration/clock-rate maths needed when sending.
    private static readonly AudioFormat _sipAudioFormat = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU);
    private const int SIP_SAMPLE_RATE = 8000;
    private const int GEMINI_INPUT_SAMPLE_RATE = 16000;

    // RTPSession.SendAudio() does not pace packets in real time — it just splits whatever buffer
    // it's given into MTU-sized RTP packets and sends them all back-to-back. Gemini's audio
    // arrives in irregular, often multi-hundred-millisecond chunks (not the ~20ms a phone call
    // expects per RTP packet), so sending each chunk straight to SendAudio as it arrives would
    // blast bursts of packets far ahead of real time — heard by the caller as garbled/sped-up
    // audio. Instead, encoded PCMU bytes are queued and a timer drains exactly one 20ms frame
    // (RTP_FRAME_SAMPLES bytes, since PCMU is 1 byte/sample) at a steady 20ms cadence.
    private const int RTP_FRAME_DURATION_MS = 20;
    private const int RTP_FRAME_SAMPLES = SIP_SAMPLE_RATE * RTP_FRAME_DURATION_MS / 1000;

    // Polling interval/cap used to wait for the playout queue to drain before hanging up, so the
    // model's farewell isn't cut off part-way through (see WaitForPlayoutToDrainAndHangupAsync).
    private const int PLAYOUT_DRAIN_POLL_MS = 100;
    private static readonly TimeSpan PLAYOUT_DRAIN_TIMEOUT = TimeSpan.FromSeconds(10);

    private static readonly ConcurrentDictionary<string, SipToGeminiCall> _calls = new();

    // Settings are read from the project's user secrets (dotnet user-secrets set GEMINI_API_KEY ...)
    // as well as process environment variables. Environment variables are added last so they take
    // precedence, keeping the documented "set GEMINI_API_KEY=..." workflow working unchanged.
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

    static async Task Main()
    {
        // Windows' console defaults to a legacy codepage that can't represent Polish diacritics
        // (or the checkmark glyphs used in the transcript log lines below) — without this,
        // unmappable characters silently become "?", making transcripts unreadable.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(loggerFactory);

        Log.Logger.Information("SIP-to-Gemini Live Function Calling Demo Program");

        var config = BuildConfiguration();

        var geminiApiKey = config[ENV_VAR_GEMINI_API_KEY] ?? config["GOOGLE_API_KEY"];
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            Log.Logger.Error("Please provide your Gemini API key as a user secret (dotnet user-secrets set {EnvVar} <your gemini api key>) or as an environment variable (set {EnvVar}=<your gemini api key>).", ENV_VAR_GEMINI_API_KEY);
            return;
        }

        var sipServer = config[ENV_VAR_SIP_SERVER];
        var sipUsername = config[ENV_VAR_SIP_USERNAME];
        var sipPassword = config[ENV_VAR_SIP_PASSWORD];
        if (string.IsNullOrWhiteSpace(sipServer) || string.IsNullOrWhiteSpace(sipUsername) || string.IsNullOrWhiteSpace(sipPassword))
        {
            Log.Logger.Error("Please provide the SIP server, username and password as user secrets or environment variables: {ServerVar}, {UsernameVar}, {PasswordVar}.", ENV_VAR_SIP_SERVER, ENV_VAR_SIP_USERNAME, ENV_VAR_SIP_PASSWORD);
            return;
        }

        var sipTransport = new SIPTransport();
        sipTransport.EnableTraceLogs();

        var regUserAgent = new SIPRegistrationUserAgent(sipTransport, sipUsername, sipPassword, sipServer, REGISTRATION_EXPIRY_SECONDS);
        regUserAgent.RegistrationFailed += (uri, resp, err) => Log.Logger.Error("Registration failed for {Uri}: {Error}", uri, err);
        regUserAgent.RegistrationTemporaryFailure += (uri, resp, msg) => Log.Logger.Warning("Registration temporary failure for {Uri}: {Message}", uri, msg);
        regUserAgent.RegistrationRemoved += (uri, resp) => Log.Logger.Warning("Registration removed for {Uri}.", uri);
        regUserAgent.RegistrationSuccessful += (uri, resp) => Log.Logger.Information("Registered {Uri} with the PBX.", uri);
        regUserAgent.Start();

        sipTransport.SIPTransportRequestReceived += (lep, rep, req) => OnRequestAsync(lep, rep, req, sipTransport, geminiApiKey, loggerFactory);

        Log.Logger.Information("Registering {Username} with {Server}. Waiting for an incoming call...", sipUsername, sipServer);
        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        var exitTcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            exitTcs.TrySetResult(null);
        };

        await exitTcs.Task;

        regUserAgent.Stop();

        // Allow time for the unregister request (REGISTER with 0 expiry) to be sent.
        await Task.Delay(500);

        sipTransport.Shutdown();
    }

    private static async Task OnRequestAsync(
        SIPEndPoint localSIPEndPoint,
        SIPEndPoint remoteEndPoint,
        SIPRequest sipRequest,
        SIPTransport sipTransport,
        string geminiApiKey,
        ILoggerFactory loggerFactory)
    {
        try
        {
            if (sipRequest.Header.From?.FromTag != null && sipRequest.Header.To?.ToTag != null)
            {
                // In-dialog request; handled directly by the relevant SIPUserAgent instance.
                return;
            }

            if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                Log.Logger.Information("Incoming call from {RemoteEndPoint}.", remoteEndPoint);

                var ua = new SIPUserAgent(sipTransport, null);
                ua.OnCallHungup += OnHangup;
                ua.ServerCallCancelled += (uas, cancelReq) => Log.Logger.Debug("Incoming call cancelled by remote party.");
                ua.ServerCallRingTimeout += uas =>
                {
                    Log.Logger.Warning("Incoming call timed out waiting for client ACK, terminating.");
                    ua.Hangup();
                };

                var uas = ua.AcceptCall(sipRequest);
                var rtpSession = CreateRtpSession();

                await ua.Answer(uas, rtpSession);

                if (!ua.IsCallActive)
                {
                    Log.Logger.Warning("Call from {RemoteEndPoint} failed to answer.", remoteEndPoint);
                    rtpSession.Close("answer failed");
                    return;
                }

                await rtpSession.Start();

                Log.Logger.Information("Call answered, call ID {CallId}.", ua.Dialogue.CallId);

                await BridgeToGeminiAsync(loggerFactory, geminiApiKey, ua, rtpSession);
            }
            else if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                var byeResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                await sipTransport.SendResponseAsync(byeResponse);
            }
            else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
            {
                // Answer Asterisk's "qualify" keep-alive pings so the extension shows as reachable.
                var okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await sipTransport.SendResponseAsync(okResponse);
            }
        }
        catch (Exception excp)
        {
            Log.Logger.Warning(excp, "Exception handling {Method} from {RemoteEndPoint}.", sipRequest.Method, remoteEndPoint);
        }
    }

    private static RTPSession CreateRtpSession()
    {
        var rtpSession = new RTPSession(false, false, false);
        rtpSession.addTrack(new MediaStreamTrack(_sipAudioFormat));
        rtpSession.AcceptRtpFromAny = true;

        rtpSession.OnTimeout += mediaType => Log.Logger.Warning("RTP timeout on {MediaType}.", mediaType);

        return rtpSession;
    }

    private static async Task BridgeToGeminiAsync(ILoggerFactory loggerFactory, string geminiApiKey, SIPUserAgent ua, RTPSession rtpSession)
    {
        var callId = ua.Dialogue.CallId;
        var audioEncoder = new AudioEncoder();
        var geminiEndPoint = new GeminiLiveEndPoint(geminiApiKey, loggerFactory);

        // Set once the model calls the endCall function. Checked when a turn completes so the
        // call is only hung up after the model's farewell has actually finished, not the instant
        // the function call itself arrives (the model still has audio left to speak at that point).
        var endCallRequested = false;

        // Caller's voice: PCMU @ 8kHz -> PCM16 @ 8kHz -> resample -> PCM16 @ 16kHz -> Gemini,
        // forwarded immediately as each RTP packet arrives.
        //
        // A timer-paced jitter buffer was tried here (queue + drain at a fixed 20ms cadence,
        // mirroring the outbound playout timer below) and made recognition noticeably WORSE, not
        // better. System.Threading.Timer's default resolution (~15ms on Windows) means a "20ms"
        // period doesn't actually tick evenly — it introduced more irregular timing than the raw
        // RTP arrival it was meant to smooth over. Passing each frame straight through, as
        // originally, avoids adding that extra artificial jitter source.
        rtpSession.OnAudioFrameReceived += frame =>
        {
            if (frame.EncodedAudio.Length == 0)
            {
                return;
            }

            try
            {
                var pcm8k = audioEncoder.DecodeAudio(frame.EncodedAudio, _sipAudioFormat);
                var pcm16k = PcmResampler.Resample(pcm8k, SIP_SAMPLE_RATE, GEMINI_INPUT_SAMPLE_RATE);
                geminiEndPoint.SendAudio(ShortsToLittleEndianBytes(pcm16k));
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to forward caller audio to Gemini.");
            }
        };

        // Gemini's voice: PCM16 @ 24kHz -> resample -> PCM16 @ 8kHz -> PCMU, queued for the
        // playout timer below to send to the caller at a steady 20ms/frame pace.
        var playoutLock = new object();
        var playoutQueue = new Queue<byte>();

        geminiEndPoint.OnAudioReceived += (pcm, sampleRateHz) =>
        {
            try
            {
                var pcmIn = BytesToShorts(pcm);
                var pcm8k = PcmResampler.Resample(pcmIn, sampleRateHz, SIP_SAMPLE_RATE);
                var encoded = audioEncoder.EncodeAudio(pcm8k, _sipAudioFormat);

                lock (playoutLock)
                {
                    foreach (var b in encoded)
                    {
                        playoutQueue.Enqueue(b);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to forward Gemini audio to caller.");
            }
        };

        var playoutTimer = new Timer(_ =>
        {
            byte[]? frame = null;

            lock (playoutLock)
            {
                if (playoutQueue.Count >= RTP_FRAME_SAMPLES)
                {
                    frame = new byte[RTP_FRAME_SAMPLES];
                    for (int i = 0; i < RTP_FRAME_SAMPLES; i++)
                    {
                        frame[i] = playoutQueue.Dequeue();
                    }
                }
            }

            if (frame != null)
            {
                try
                {
                    rtpSession.SendAudio(RTP_FRAME_SAMPLES, frame);
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(ex, "Failed to send paced RTP audio frame to caller.");
                }
            }
        }, null, RTP_FRAME_DURATION_MS, RTP_FRAME_DURATION_MS);

        geminiEndPoint.OnInterrupted += () =>
        {
            Log.Logger.Debug("Model turn interrupted by caller (barge-in), clearing playout queue.");
            lock (playoutLock)
            {
                playoutQueue.Clear();
            }
        };

        geminiEndPoint.OnServerMessage += message =>
        {
            var log = message switch
            {
                GeminiServerEventContent { InputTranscription.Text: { Length: > 0 } inputText } => $"CALLER ✅: {inputText.Trim()}",
                GeminiServerEventContent { OutputTranscription.Text: { Length: > 0 } outputText } => $"AI ✅: {outputText.Trim()}",
                _ => string.Empty
            };

            if (log != string.Empty)
            {
                Console.WriteLine(log);
            }

            if (message is GeminiServerEventToolCall toolCall)
            {
                foreach (var functionCall in toolCall.FunctionCalls ?? Enumerable.Empty<GeminiFunctionCall>())
                {
                    if (functionCall.Name == END_CALL_FUNCTION_NAME)
                    {
                        Log.Logger.Information("Model requested {Function} for call {CallId}; ending the call once it finishes speaking.", END_CALL_FUNCTION_NAME, callId);
                        endCallRequested = true;
                    }

                    _ = RespondToFunctionCallAsync(geminiEndPoint, functionCall, callId);
                }
            }

            // Gemini Live sessions have a maximum duration and will warn before cutting the
            // connection — surface it loudly so a mid-call drop isn't a silent mystery.
            if (message is GeminiServerEventGoAway goAway)
            {
                Log.Logger.Warning("Gemini is closing the session for call {CallId} soon (time left: {TimeLeft}).", callId, goAway.TimeLeft);
            }

            // Only hang up once the model's current turn (its farewell, spoken after the endCall
            // function response is sent) has fully finished generating.
            if (endCallRequested && message is GeminiServerEventContent { TurnComplete: true })
            {
                endCallRequested = false;
                _ = WaitForPlayoutToDrainAndHangupAsync(ua, playoutLock, playoutQueue, callId);
            }
        };

        geminiEndPoint.OnFailed += () => Log.Logger.Error("Gemini Live connection failed for call {CallId}.", callId);
        geminiEndPoint.OnClosed += () => Log.Logger.Warning(
            "Gemini Live connection closed for call {CallId} — no further audio will be processed for the rest of this call.", callId);

        // Prompt the model to greet the caller as soon as the session is up, rather than waiting
        // for the caller to say something first.
        geminiEndPoint.OnConnected += () => _ = SendGreetingPromptAsync(geminiEndPoint, callId);

        _calls.TryAdd(callId, new SipToGeminiCall(ua, rtpSession, geminiEndPoint, audioEncoder, playoutTimer));

        var endCallFunction = new GeminiFunctionDeclaration
        {
            Name = END_CALL_FUNCTION_NAME,
            Description = "Zakończ połączenie telefoniczne. Wywołaj tę funkcję wyłącznie wtedy, gdy rozmówca " +
                           "jasno sygnalizuje, że rozmowa się skończyła, np. mówi „do widzenia”, „dziękuję, to " +
                           "wszystko” albo „koniec”. Zanim ją wywołasz, pożegnaj się krótko z rozmówcą.",
            Parameters = JsonDocument.Parse("""{ "type": "object", "properties": {} }""").RootElement
        };

        var setup = new GeminiSetup
        {
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseModalities = new List<GeminiResponseModalityEnum> { GeminiResponseModalityEnum.AUDIO },
                SpeechConfig = new GeminiSpeechConfig
                {
                    VoiceConfig = new GeminiVoiceConfig
                    {
                        PrebuiltVoiceConfig = new GeminiPrebuiltVoiceConfig { VoiceName = GeminiVoiceEnum.Puck }
                    },
                    // Pin the recognition language. Automatic language ID is unstable on
                    // narrowband (8kHz G.711) telephony audio: short Polish utterances came back
                    // transcribed as Italian, Spanish and Japanese even though the phonetics were
                    // recognised correctly. A system instruction does not fix this - it steers the
                    // model's behaviour, not the audio understanding layer that picks the language.
                    LanguageCode = "pl-PL"
                }
            },
            Tools = new List<GeminiTool>
            {
                new GeminiTool
                {
                    FunctionDeclarations = new List<GeminiFunctionDeclaration> { endCallFunction }
                }
            },
            SystemInstruction = new GeminiContent
            {
                Parts = new List<GeminiPart>
                {
                    new GeminiPart
                    {
                        Text = "Ta rozmowa odbywa się WYŁĄCZNIE po polsku. Rozmówca zawsze mówi po polsku, nawet jeśli nagranie jest niewyraźne lub ciche. Nigdy nie interpretuj jego wypowiedzi jako innego języka. Nigdy nie odpowiadaj w innym języku niż polski. Jeśli wypowiedź rozmówcy jest niezrozumiała, urwana albo nie brzmi jak sensowne polskie zdanie — poproś o powtórzenie. Nigdy nie domyślaj się, o co chodziło. Rozmawiasz z kimś przez telefon, więc wypowiadaj się krótko i klarownie. " +
                               "Jesteś asystentem obsługi klienta, twoim zadaniem jest odpowiadanie tylko i wyłącznie na temat " +
                               "produktów naszej firmy. Masz na imię Ava i jesteś przyjaźnie nastawionym, ciepłym człowiekiem, " +
                               "ale przy tym wyrażasz się zwięźle i jasno. Gdy rozmówca zasygnalizuje, że chce zakończyć " +
                               $"rozmowę, pożegnaj się krótko, a następnie wywołaj funkcję {END_CALL_FUNCTION_NAME}."
                    }
                }
            },
            // The most misrecognised turns were the shortest ones ("Nie, dziekuje", ~1s). A turn
            // that brief gives language identification very little to work with, and clipping its
            // onset with VAD removes a large share of what is left. Pad the start of each detected
            // turn so the first phoneme always survives.
            RealtimeInputConfig = new GeminiRealtimeInputConfig
            {
                AutomaticActivityDetection = new GeminiAutomaticActivityDetection
                {
                    PrefixPaddingMs = 300
                }
            },
            InputAudioTranscription = new GeminiAudioTranscriptionConfig(),
            OutputAudioTranscription = new GeminiAudioTranscriptionConfig()
        };

        Either<Error, Unit> connectResult;
        using (var connectCts = new CancellationTokenSource(GEMINI_CONNECT_TIMEOUT))
        {
            try
            {
                connectResult = await geminiEndPoint.StartConnect(GeminiLiveModelsEnum.Gemini31FlashLivePreview, setup, connectCts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Logger.Error(
                    "Timed out connecting to Gemini Live for call {CallId} after {TimeoutSeconds}s. The call was answered but nothing will happen — check for leftover connections from a previous call.",
                    callId, GEMINI_CONNECT_TIMEOUT.TotalSeconds);
                ua.Hangup();
                return;
            }
        }

        if (connectResult.IsLeft)
        {
            Log.Logger.Error("Failed to connect to Gemini Live for call {CallId}: {Error}", callId, connectResult.LeftAsEnumerable().First());
            ua.Hangup();
        }
    }

    /// <summary>
    /// Acknowledges a function call so the model can continue its turn (e.g. speak the farewell
    /// that was requested alongside the endCall call). The response body only needs to exist for
    /// Gemini to proceed; its content isn't inspected for this single-tool demo.
    /// </summary>
    private static async Task RespondToFunctionCallAsync(GeminiLiveEndPoint geminiEndPoint, GeminiFunctionCall functionCall, string callId)
    {
        var response = new GeminiFunctionResponse
        {
            Id = functionCall.Id,
            Name = functionCall.Name,
            Response = JsonDocument.Parse("""{ "success": true }""").RootElement
        };

        var result = await geminiEndPoint.SendToolResponse(new[] { response });

        if (result.IsLeft)
        {
            Log.Logger.Warning("Failed to send tool response for {Function} on call {CallId}: {Error}", functionCall.Name, callId, result.LeftAsEnumerable().First());
        }
    }

    /// <summary>
    /// Hangs up the SIP call once the outbound playout queue has drained, i.e. once the model's
    /// spoken farewell has actually finished being sent to the caller rather than just generated.
    /// Hanging up as soon as the endCall function call or turnComplete arrives would cut the
    /// farewell off mid-sentence, since several hundred milliseconds of audio are typically still
    /// queued at that point.
    /// </summary>
    private static async Task WaitForPlayoutToDrainAndHangupAsync(SIPUserAgent ua, object playoutLock, Queue<byte> playoutQueue, string callId)
    {
        var deadline = DateTime.UtcNow + PLAYOUT_DRAIN_TIMEOUT;

        while (DateTime.UtcNow < deadline)
        {
            bool drained;
            lock (playoutLock)
            {
                drained = playoutQueue.Count == 0;
            }

            if (drained)
            {
                break;
            }

            await Task.Delay(PLAYOUT_DRAIN_POLL_MS);
        }

        Log.Logger.Information("Hanging up call {CallId} after the model ended the conversation.", callId);
        ua.Hangup();
    }

    private static void OnHangup(SIPDialogue dialogue)
    {
        if (dialogue == null)
        {
            return;
        }

        var callId = dialogue.CallId;
        if (_calls.TryRemove(callId, out var call))
        {
            Log.Logger.Information("Call {CallId} ended.", callId);

            call.Ua.Close();
            call.RtpSession.Close("call ended");
            call.PlayoutTimer.Dispose();
            call.Encoder.Dispose();

            // Awaited in the background (not blocking the SIP hangup handler) but logged, so a
            // slow/stuck close on this call's Gemini connection is visible before the next call
            // comes in, rather than silently leaving a half-closed connection behind.
            _ = CloseGeminiEndPointAsync(call.GeminiEndPoint, callId);
        }
    }

    private static async Task CloseGeminiEndPointAsync(GeminiLiveEndPoint geminiEndPoint, string callId)
    {
        try
        {
            await geminiEndPoint.Close();
            Log.Logger.Debug("Gemini Live connection for call {CallId} closed cleanly.", callId);

            // Non-zero means the caller was captured faster than the audio could be written to the
            // Gemini socket, i.e. the assistant heard a chopped-up version of what was said.
            Log.Logger.Information(
                "Gemini Live call {CallId} finished. Dropped outbound audio chunks: {DroppedAudioChunks}.",
                callId,
                geminiEndPoint.DroppedAudioChunks);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Error closing Gemini Live connection for call {CallId}.", callId);
        }
    }

    private static byte[] ShortsToLittleEndianBytes(short[] pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static short[] BytesToShorts(byte[] bytes)
    {
        var pcm = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, pcm, 0, bytes.Length);
        return pcm;
    }

    /// <summary>
    /// Prompts Gemini to greet the caller first (rather than waiting for them to say something) by
    /// sending a short instruction as a completed user turn.
    /// </summary>
    private static async Task SendGreetingPromptAsync(GeminiLiveEndPoint geminiEndPoint, string callId)
    {
        var result = await geminiEndPoint.SendText("Przywitaj się krótko, przedstaw się jako Ava i zapytaj, jak możesz pomóc.");

        if (result.IsLeft)
        {
            Log.Logger.Warning("Failed to send greeting prompt for call {CallId}: {Error}", callId, result.LeftAsEnumerable().First());
        }
    }
}
