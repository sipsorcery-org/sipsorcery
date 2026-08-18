using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Gemini.Realtime.Models;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// End point for the Gemini Live API. Establishes the BidiGenerateContent WebSocket connection,
/// sends the session setup message and streams raw PCM16 audio in both directions, surfacing
/// strongly typed server messages to consumers.
/// </summary>
public class GeminiLiveEndPoint : IGeminiLiveEndPoint
{
    /// <summary>
    /// Number of outbound audio chunks that may be waiting to be written to the WebSocket before
    /// new ones are dropped. At the usual capture cadence of one chunk per 20-100ms this is a
    /// backlog of a couple of seconds; anything beyond that is already too stale to be useful in a
    /// live conversation, and queueing it without limit would grow memory and add latency that
    /// never recovers.
    /// </summary>
    public const int DEFAULT_AUDIO_QUEUE_CAPACITY = 50;

    /// <summary>
    /// Sample rate assumed for received audio when Gemini's mime type doesn't state one.
    /// </summary>
    public const int DEFAULT_OUTPUT_SAMPLE_RATE = 24000;

    private const int AUDIO_DROP_LOG_INTERVAL = 100;
    private const int DISPOSE_DRAIN_TIMEOUT_MS = 2000;

    private readonly ILogger _logger;
    private readonly IGeminiLiveWebSocketClient _webSocketClient;

    /// <summary>
    /// Outbound audio is queued here and written to the socket by a single pump task. A queue is
    /// needed because <see cref="SendAudio"/> is a synchronous fire-and-forget call made from
    /// capture callbacks: starting an un-awaited send task per chunk instead would place no bound
    /// on how many sends can pile up behind a slow network, and would let chunks captured from
    /// different threads reach the socket out of order. One reader draining a bounded channel gives
    /// both a hard memory bound and strict FIFO ordering regardless of how many capture threads
    /// call in.
    /// </summary>
    private readonly Channel<byte[]> _audioQueue;
    private readonly CancellationTokenSource _audioPumpCts = new();
    private readonly Task _audioPumpTask;

    private long _droppedAudioChunks;
    private long _failedAudioChunks;
    private int _connectInProgress;
    private bool _disposed;

    /// <summary>
    /// True once the initial <c>setup</c> message has actually been written to the WebSocket.
    /// Gemini requires <c>setup</c> to be the very first message on the connection — if anything
    /// else (e.g. a realtimeInput audio chunk) arrives first it rejects the connection outright
    /// (a PolicyViolation/InvalidPayloadData close). Audio/text/tool-response sends are gated on
    /// this flag so a caller that starts streaming audio immediately on answering a call (before
    /// this end point has even finished connecting to Gemini) can never race ahead of setup.
    /// </summary>
    private volatile bool _setupSent;

    public IGeminiLiveMessenger Messenger { get; }

    /// <summary>
    /// Number of outbound audio chunks discarded because <see cref="DEFAULT_AUDIO_QUEUE_CAPACITY"/>
    /// was reached, i.e. audio was captured faster than it could be written to the socket. Exposed
    /// so a caller can surface it alongside its own media statistics.
    /// </summary>
    public long DroppedAudioChunks => Interlocked.Read(ref _droppedAudioChunks);

    public event Action? OnConnected;
    public event Action? OnClosed;
    public event Action? OnFailed;
    public event Action? OnInterrupted;
    public event Action<byte[], int>? OnAudioReceived;
    public event Action<GeminiServerMessage>? OnServerMessage;

    /// <summary>
    /// Preferred constructor for dependency injection.
    /// </summary>
    /// <param name="logger">Logging instance for this class.</param>
    /// <param name="messengerLogger">Dedicated logging instance for the messenger class.</param>
    /// <param name="webSocketClient">Transport client for the Gemini Live WebSocket connection.</param>
    public GeminiLiveEndPoint(
        ILogger<GeminiLiveEndPoint> logger,
        ILogger<GeminiLiveMessenger> messengerLogger,
        IGeminiLiveWebSocketClient webSocketClient)
        : this(webSocketClient, logger, messengerLogger)
    {
    }

    /// <summary>
    /// Constructor for use when not using dependency injection.
    /// </summary>
    /// <param name="apiKey">The Google AI Studio / Gemini API key.</param>
    /// <param name="loggerFactory">Logger factory to use for the end point.</param>
    public GeminiLiveEndPoint(string apiKey, ILoggerFactory loggerFactory)
        : this(
            new GeminiLiveWebSocketClient(apiKey, loggerFactory.CreateLogger<GeminiLiveWebSocketClient>()),
            loggerFactory.CreateLogger<GeminiLiveEndPoint>(),
            loggerFactory.CreateLogger<GeminiLiveMessenger>())
    {
    }

    private GeminiLiveEndPoint(
        IGeminiLiveWebSocketClient webSocketClient,
        ILogger? logger,
        ILogger? messengerLogger)
    {
        _logger = logger ?? NullLogger.Instance;
        _webSocketClient = webSocketClient;

        var messenger = new GeminiLiveMessenger(webSocketClient, messengerLogger);
        messenger.OnServerMessage += OnMessengerServerMessage;
        Messenger = messenger;

        _audioQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(DEFAULT_AUDIO_QUEUE_CAPACITY)
        {
            // Wait (rather than one of the Drop modes) so that a full queue is visible to
            // SendAudio as a failed TryWrite and can be counted and logged, instead of the channel
            // silently discarding audio.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        WireWebSocketEvents();

        _audioPumpTask = Task.Run(() => AudioPumpAsync(_audioPumpCts.Token));
    }

    private void WireWebSocketEvents()
    {
        _webSocketClient.OnMessage += Messenger.HandleIncomingMessage;
        _webSocketClient.OnClosed += () =>
        {
            // The session's setup no longer applies once the socket is gone; a reconnect has to
            // send a fresh one before any audio may follow.
            _setupSent = false;
            EventRaiser.Raise(_logger, OnClosed, nameof(OnClosed));
        };
        _webSocketClient.OnError += ex =>
        {
            _logger.LogError(ex, "Gemini Live WebSocket error.");
            EventRaiser.Raise(_logger, OnFailed, nameof(OnFailed));
        };
    }

    public async Task<Either<Error, Unit>> StartConnect(
        GeminiLiveModelsEnum? model = null,
        GeminiSetup? setupOverrides = null,
        CancellationToken ct = default)
    {
        if (_disposed)
        {
            return Error.New("Gemini Live end point has been disposed.");
        }

        if (Interlocked.CompareExchange(ref _connectInProgress, 1, 0) == 1)
        {
            return Error.New("A Gemini Live connect attempt is already in progress.");
        }

        try
        {
            _setupSent = false;

            try
            {
                await _webSocketClient.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Gemini Live WebSocket endpoint.");
                return Error.New($"Failed to connect to Gemini Live WebSocket endpoint: {ex.Message}");
            }

            var setup = setupOverrides ?? new GeminiSetup();
            if (model != null)
            {
                // Copy rather than assign to the caller's instance: the same GeminiSetup is
                // commonly reused across calls/reconnects and must not be mutated here.
                setup = setup.ShallowCopy();
                setup.Model = model.Value.ToEnumString();
            }

            var result = await Messenger.SendSetupAsync(setup, ct).ConfigureAwait(false);

            // Only flip the gate once the setup bytes have actually been written to the socket, so
            // nothing sent via SendAudio/SendText/SendToolResponse below can ever precede it.
            _setupSent = result.IsRight;

            if (result.IsLeft)
            {
                // Leaving the socket open after a rejected setup would strand a connection that can
                // never be used: Gemini only accepts setup as the first message.
                _logger.LogWarning("Closing the Gemini Live WebSocket because the setup message could not be sent.");
                await _webSocketClient.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            Interlocked.Exchange(ref _connectInProgress, 0);
        }
    }

    public void SendAudio(byte[] pcm16LittleEndian)
    {
        if (_disposed)
        {
            return;
        }

        if (!_setupSent)
        {
            // Expected/benign: a caller can start streaming audio (e.g. as soon as a phone call
            // is answered) before the Gemini session has finished its setup handshake. Drop it
            // rather than risk it racing ahead of the setup message.
            _logger.LogDebug("Dropping audio chunk: Gemini Live session setup not sent yet.");
            return;
        }

        if (!_audioQueue.Writer.TryWrite(pcm16LittleEndian))
        {
            var dropped = Interlocked.Increment(ref _droppedAudioChunks);
            if (dropped == 1 || dropped % AUDIO_DROP_LOG_INTERVAL == 0)
            {
                _logger.LogWarning(
                    "Gemini Live outbound audio queue is full ({Capacity} chunks); dropped chunk {DroppedCount}. Audio is being captured faster than it can be sent.",
                    DEFAULT_AUDIO_QUEUE_CAPACITY,
                    dropped);
            }
        }
    }

    /// <summary>
    /// Single consumer of <see cref="_audioQueue"/>. Runs for the lifetime of the end point.
    /// </summary>
    private async Task AudioPumpAsync(CancellationToken ct)
    {
        try
        {
            while (await _audioQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_audioQueue.Reader.TryRead(out var chunk))
                {
                    var result = await Messenger.SendRealtimeInputAudioAsync(chunk, ct: ct).ConfigureAwait(false);

                    if (result.IsLeft)
                    {
                        var failed = Interlocked.Increment(ref _failedAudioChunks);
                        if (failed == 1 || failed % AUDIO_DROP_LOG_INTERVAL == 0)
                        {
                            _logger.LogWarning(
                                "Failed to send audio to Gemini Live (failure {FailureCount}): {Error}",
                                failed,
                                result.LeftAsEnumerable().First());
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Dispose.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini Live outbound audio pump terminated unexpectedly.");
        }
    }

    public Task<Either<Error, Unit>> SendText(string text, bool turnComplete = true)
    {
        if (!_setupSent)
        {
            return Task.FromResult<Either<Error, Unit>>(Error.New("Gemini Live session setup not sent yet."));
        }

        return Messenger.SendClientContentAsync(text, turnComplete);
    }

    public Task<Either<Error, Unit>> SendToolResponse(IEnumerable<GeminiFunctionResponse> functionResponses)
    {
        if (!_setupSent)
        {
            return Task.FromResult<Either<Error, Unit>>(Error.New("Gemini Live session setup not sent yet."));
        }

        return Messenger.SendToolResponseAsync(functionResponses);
    }

    /// <summary>
    /// Called for every parsed server message. Raises <see cref="OnServerMessage"/>
    /// unconditionally, plus the more specific
    /// <see cref="OnConnected"/>/<see cref="OnInterrupted"/>/<see cref="OnAudioReceived"/> events
    /// for the message kinds those correspond to. Runs on the WebSocket receive loop, so every
    /// step is defensive: neither a malformed payload nor a throwing consumer handler may escape.
    /// </summary>
    private void OnMessengerServerMessage(GeminiServerMessage message)
    {
        EventRaiser.Raise(_logger, OnServerMessage, message, nameof(OnServerMessage));

        if (message is GeminiServerEventSetupComplete)
        {
            EventRaiser.Raise(_logger, OnConnected, nameof(OnConnected));
        }
        else if (message is GeminiServerEventContent content)
        {
            if (content.Interrupted == true)
            {
                EventRaiser.Raise(_logger, OnInterrupted, nameof(OnInterrupted));
            }

            // A single serverContent message can carry more than one inlineData audio part —
            // raise OnAudioReceived for every one of them, in order, rather than only the first.
            var parts = content.ModelTurn?.Parts;
            if (parts != null)
            {
                foreach (var part in parts)
                {
                    RaiseAudioReceived(part);
                }
            }
        }
    }

    private void RaiseAudioReceived(GeminiPart part)
    {
        if (part.InlineData?.Data == null ||
            part.InlineData.MimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        byte[] pcm16LittleEndian;
        try
        {
            pcm16LittleEndian = Convert.FromBase64String(part.InlineData.Data);
        }
        catch (FormatException ex)
        {
            // Skip just this part: the rest of the message (further audio parts, transcripts) is
            // still usable, and throwing here would take the receive loop down with it.
            _logger.LogWarning(ex, "Discarding a Gemini Live audio part with an invalid base64 payload.");
            return;
        }

        var sampleRateHz = ParseSampleRate(part.InlineData.MimeType) ?? DEFAULT_OUTPUT_SAMPLE_RATE;

        EventRaiser.Raise(_logger, OnAudioReceived, pcm16LittleEndian, sampleRateHz, nameof(OnAudioReceived));
    }

    /// <summary>
    /// Extracts the "rate" parameter from a mime type such as "audio/pcm;rate=24000".
    /// </summary>
    private static int? ParseSampleRate(string mimeType)
    {
        const string ratePrefix = "rate=";

        var idx = mimeType.IndexOf(ratePrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var ratePart = mimeType.Substring(idx + ratePrefix.Length);
        var end = ratePart.IndexOf(';');
        if (end >= 0)
        {
            ratePart = ratePart.Substring(0, end);
        }

        return int.TryParse(ratePart, out var rate) ? rate : null;
    }

    public async Task Close()
    {
        if (_disposed)
        {
            return;
        }

        _setupSent = false;

        await _webSocketClient.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Preferred over <see cref="Dispose"/>: closing the WebSocket and draining the outbound audio
    /// queue are both asynchronous, and the synchronous path has to abandon them rather than block.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _setupSent = false;

        _audioQueue.Writer.TryComplete();

        try
        {
            await _webSocketClient.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exception closing the Gemini Live WebSocket during dispose.");
        }

        _audioPumpCts.Cancel();

        try
        {
            await _audioPumpTask.WaitAsync(TimeSpan.FromMilliseconds(DISPOSE_DRAIN_TIMEOUT_MS)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Outbound audio pump did not complete within the dispose timeout.");
        }

        _audioPumpCts.Dispose();
        DisposeTransport();

        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _setupSent = false;

        _audioQueue.Writer.TryComplete();
        _audioPumpCts.Cancel();

        // Deliberately not waiting on CloseAsync/the audio pump here. Blocking on them
        // (.GetAwaiter().GetResult()) risks a deadlock when Dispose is called on a thread with a
        // synchronization context, which is exactly where this library gets used (WinForms/WPF and
        // SIP call teardown). Disposing the transport aborts the socket; use DisposeAsync for a
        // graceful close handshake.
        DisposeTransport();

        GC.SuppressFinalize(this);
    }

    private void DisposeTransport()
    {
        // _audioPumpCts is deliberately not disposed on this path: the pump task has only been
        // signalled, not awaited, and disposing the source out from under it can fault its pending
        // wait. An unused, cancelled CancellationTokenSource holds nothing but managed memory.
        if (_webSocketClient is IDisposable disposableClient)
        {
            disposableClient.Dispose();
        }
    }
}
