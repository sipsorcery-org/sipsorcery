using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Gemini.Realtime.Models;
using Xunit;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

[Trait("Category", "unit")]
public class GeminiLiveEndPointUnitTests
{
    private readonly ILogger logger;

    public GeminiLiveEndPointUnitTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        logger = TestLogHelper.InitTestLogger(output);
    }

    private static (GeminiLiveEndPoint EndPoint, FakeGeminiLiveWebSocketClient Transport, CapturingLogger Log) CreateEndPoint()
    {
        var transport = new FakeGeminiLiveWebSocketClient();
        var endPointLog = new CapturingLogger<GeminiLiveEndPoint>();
        var endPoint = new GeminiLiveEndPoint(endPointLog, new CapturingLogger<GeminiLiveMessenger>(), transport);

        return (endPoint, transport, endPointLog);
    }

    private static byte[] SequencedChunk(int sequenceNumber) => BitConverter.GetBytes(sequenceNumber);

    private static List<int> ReceivedSequenceNumbers(FakeGeminiLiveWebSocketClient transport)
    {
        var sequenceNumbers = new List<int>();

        foreach (var json in transport.Sent)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("realtimeInput", out var realtimeInput) &&
                realtimeInput.TryGetProperty("audio", out var audio))
            {
                var payload = Convert.FromBase64String(audio.GetProperty("data").GetString()!);
                sequenceNumbers.Add(BitConverter.ToInt32(payload, 0));
            }
        }

        return sequenceNumbers;
    }

    [Fact]
    public async Task StartConnect_Sends_Setup_As_First_Message()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var result = await endPoint.StartConnect();

        Assert.True(result.IsRight);
        Assert.Equal(1, transport.ConnectCount);
        var sent = Assert.Single(transport.Sent);
        using var doc = JsonDocument.Parse(sent);
        Assert.True(doc.RootElement.TryGetProperty("setup", out _));
    }

    [Fact]
    public async Task StartConnect_Model_Override_Does_Not_Mutate_Callers_Setup()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var callerSetup = new GeminiSetup { Model = "models/caller-choice" };

        var result = await endPoint.StartConnect(GeminiLiveModelsEnum.Gemini31FlashLivePreview, callerSetup);

        Assert.True(result.IsRight);

        using var doc = JsonDocument.Parse(transport.Sent[0]);
        Assert.Equal(
            "models/gemini-3.1-flash-live-preview",
            doc.RootElement.GetProperty("setup").GetProperty("model").GetString());

        // The caller's instance is commonly reused across reconnects, so it must come back untouched.
        Assert.Equal("models/caller-choice", callerSetup.Model);
    }

    [Fact]
    public async Task StartConnect_Uses_Setup_Model_When_No_Override_Given()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        await endPoint.StartConnect(setupOverrides: new GeminiSetup { Model = "models/caller-choice" });

        using var doc = JsonDocument.Parse(transport.Sent[0]);
        Assert.Equal("models/caller-choice", doc.RootElement.GetProperty("setup").GetProperty("model").GetString());
    }

    [Fact]
    public async Task StartConnect_Connect_Failure_Returns_Left()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        transport.ConnectException = new InvalidOperationException("no route to host");

        var result = await endPoint.StartConnect();

        Assert.True(result.IsLeft);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task StartConnect_Setup_Send_Failure_Closes_The_Socket()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        transport.SendException = new InvalidOperationException("socket write failed");

        var result = await endPoint.StartConnect();

        Assert.True(result.IsLeft);

        // A connection that never accepted a setup message can never be used for anything else.
        Assert.Equal(1, transport.CloseCount);

        var textResult = await endPoint.SendText("hello");
        Assert.True(textResult.IsLeft);
    }

    [Fact]
    public async Task StartConnect_While_A_Connect_Is_In_Progress_Returns_Left()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var gate = new TaskCompletionSource<bool>();
        transport.ConnectGate = gate;

        var first = endPoint.StartConnect();
        Assert.True(await Wait.UntilAsync(() => transport.ConnectCount == 1));

        var second = await endPoint.StartConnect();
        Assert.True(second.IsLeft);

        gate.SetResult(true);
        Assert.True((await first).IsRight);
        Assert.Equal(1, transport.ConnectCount);
    }

    [Fact]
    public async Task StartConnect_After_Dispose_Returns_Left()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, _, _) = CreateEndPoint();
        endPoint.Dispose();

        var result = await endPoint.StartConnect();

        Assert.True(result.IsLeft);
    }

    [Fact]
    public async Task SendAudio_Before_Setup_Is_Dropped()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        transport.IsConnected = true;
        endPoint.SendAudio(new byte[] { 1, 2, 3, 4 });

        // Give the pump a chance to pick anything up before asserting it did not.
        await Task.Delay(100);

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task SendAudio_After_Setup_Sends_Base64_Pcm_With_Input_Mime_Type()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        await endPoint.StartConnect();

        var pcm = new byte[] { 0, 1, 2, 3, 4, 5 };
        endPoint.SendAudio(pcm);

        Assert.True(await Wait.UntilAsync(() => transport.Sent.Count == 2));

        using var doc = JsonDocument.Parse(transport.Sent[1]);
        var audio = doc.RootElement.GetProperty("realtimeInput").GetProperty("audio");
        Assert.Equal(GeminiLiveMessenger.DEFAULT_AUDIO_MIME_TYPE, audio.GetProperty("mimeType").GetString());
        Assert.Equal(pcm, Convert.FromBase64String(audio.GetProperty("data").GetString()!));
    }

    [Fact]
    public async Task SendAudio_Preserves_Per_Producer_Order_Across_Threads()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        await endPoint.StartConnect();

        // Variable write latency is what exposes an ordering bug: with an un-awaited send task per
        // chunk, a slow write lets the following chunk overtake the one before it.
        transport.MaxSendJitterMs = 2;

        // Kept below the queue capacity so this test is about ordering only — dropping is covered
        // separately.
        const int chunksPerProducer = GeminiLiveEndPoint.DEFAULT_AUDIO_QUEUE_CAPACITY / 4;

        // Two capture threads, each with its own monotonic sequence (evens and odds). Interleaving
        // between the two is inherent; what must never happen is a producer's own chunks arriving
        // out of order.
        var producerOne = Task.Run(() =>
        {
            for (int i = 0; i < chunksPerProducer; i++)
            {
                endPoint.SendAudio(SequencedChunk(i * 2));
            }
        });

        var producerTwo = Task.Run(() =>
        {
            for (int i = 0; i < chunksPerProducer; i++)
            {
                endPoint.SendAudio(SequencedChunk(i * 2 + 1));
            }
        });

        await Task.WhenAll(producerOne, producerTwo);

        Assert.True(
            await Wait.UntilAsync(() => ReceivedSequenceNumbers(transport).Count == chunksPerProducer * 2),
            "not every queued audio chunk reached the transport");

        var received = ReceivedSequenceNumbers(transport);
        var evens = received.Where(n => n % 2 == 0).ToList();
        var odds = received.Where(n => n % 2 != 0).ToList();

        Assert.Equal(Enumerable.Range(0, chunksPerProducer).Select(i => i * 2), evens);
        Assert.Equal(Enumerable.Range(0, chunksPerProducer).Select(i => i * 2 + 1), odds);
        Assert.Equal(0, endPoint.DroppedAudioChunks);
    }

    [Fact]
    public async Task SendAudio_Drops_And_Counts_Chunks_When_The_Queue_Is_Full()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, endPointLog) = CreateEndPoint();
        using var lifetime = endPoint;

        await endPoint.StartConnect();

        // Stall the socket so the queue backs up, as a congested network would.
        var gate = new TaskCompletionSource<bool>();
        transport.SendGate = gate;

        const int chunksOffered = GeminiLiveEndPoint.DEFAULT_AUDIO_QUEUE_CAPACITY + 200;

        for (int i = 0; i < chunksOffered; i++)
        {
            endPoint.SendAudio(SequencedChunk(i));
        }

        Assert.True(
            await Wait.UntilAsync(() => endPoint.DroppedAudioChunks > 0),
            "a full queue should have started dropping audio");

        // The backlog is bounded by the queue capacity (plus the chunk already being written)
        // instead of growing with everything the caller offered.
        Assert.True(
            endPoint.DroppedAudioChunks >= chunksOffered - GeminiLiveEndPoint.DEFAULT_AUDIO_QUEUE_CAPACITY - 2,
            $"expected most of the offered audio to be dropped, only {endPoint.DroppedAudioChunks} was");

        Assert.True(endPointLog.Contains(LogLevel.Warning, "outbound audio queue is full"));

        gate.SetResult(true);

        // Let the backlog drain fully, then confirm what came out is bounded by the queue rather
        // than by how much the caller offered.
        Assert.True(await Wait.UntilAsync(() => ReceivedSequenceNumbers(transport).Count > 0));

        var drained = 0;
        Assert.True(await Wait.UntilAsync(() =>
        {
            var count = ReceivedSequenceNumbers(transport).Count;
            var settled = count == drained;
            drained = count;
            return settled;
        }));

        Assert.InRange(drained, 1, GeminiLiveEndPoint.DEFAULT_AUDIO_QUEUE_CAPACITY + 2);
    }

    [Fact]
    public async Task A_Failed_Audio_Send_Is_Logged_And_The_Pump_Keeps_Going()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, endPointLog) = CreateEndPoint();
        using var lifetime = endPoint;

        await endPoint.StartConnect();

        transport.SendException = new InvalidOperationException("socket write failed");
        endPoint.SendAudio(SequencedChunk(1));

        Assert.True(await Wait.UntilAsync(() => endPointLog.Contains(LogLevel.Warning, "Failed to send audio")));

        // One failed chunk must not take the pump down with it.
        transport.SendException = null;
        endPoint.SendAudio(SequencedChunk(2));

        Assert.True(await Wait.UntilAsync(() => ReceivedSequenceNumbers(transport).Contains(2)));
    }

    [Fact]
    public async Task DisposeAsync_Completes_Even_If_Closing_The_Transport_Fails()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        await endPoint.StartConnect();

        transport.CloseException = new InvalidOperationException("socket already gone");

        await endPoint.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task SendAudio_After_Dispose_Is_Ignored()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        await endPoint.StartConnect();
        endPoint.Dispose();

        endPoint.SendAudio(new byte[] { 1, 2, 3, 4 });
        await Task.Delay(100);

        // Only the setup message.
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task SendText_Is_Gated_On_Setup_And_Sends_A_Client_Content_Turn()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var beforeSetup = await endPoint.SendText("too early");
        Assert.True(beforeSetup.IsLeft);

        await endPoint.StartConnect();

        var afterSetup = await endPoint.SendText("hello there");
        Assert.True(afterSetup.IsRight);

        using var doc = JsonDocument.Parse(transport.Sent[1]);
        var clientContent = doc.RootElement.GetProperty("clientContent");
        Assert.True(clientContent.GetProperty("turnComplete").GetBoolean());
        Assert.Equal("hello there", clientContent.GetProperty("turns")[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendToolResponse_Is_Gated_On_Setup_And_Sends_Function_Responses()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var responses = new[]
        {
            new GeminiFunctionResponse { Id = "call-1", Name = "getWeather" }
        };

        Assert.True((await endPoint.SendToolResponse(responses)).IsLeft);

        await endPoint.StartConnect();

        Assert.True((await endPoint.SendToolResponse(responses)).IsRight);

        using var doc = JsonDocument.Parse(transport.Sent[1]);
        Assert.Equal(
            "call-1",
            doc.RootElement.GetProperty("toolResponse").GetProperty("functionResponses")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void SetupComplete_Raises_OnConnected()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var connected = 0;
        endPoint.OnConnected += () => connected++;

        transport.RaiseMessage(@"{ ""setupComplete"": {} }");

        Assert.Equal(1, connected);
    }

    [Fact]
    public void ServerContent_With_Usage_Metadata_Surfaces_Both()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        GeminiServerMessage? received = null;
        endPoint.OnServerMessage += message => received = message;

        transport.RaiseMessage(@"{
  ""usageMetadata"": { ""totalTokenCount"": 42 },
  ""serverContent"": { ""outputTranscription"": { ""text"": ""hi"" } }
}");

        var content = Assert.IsType<GeminiServerEventContent>(received);
        Assert.Equal("hi", content.OutputTranscription?.Text);
        Assert.Equal(42, content.UsageMetadata?.TotalTokenCount);
    }

    [Fact]
    public void Interrupted_Content_Raises_OnInterrupted()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var interrupted = 0;
        endPoint.OnInterrupted += () => interrupted++;

        transport.RaiseMessage(@"{ ""serverContent"": { ""interrupted"": true } }");
        transport.RaiseMessage(@"{ ""serverContent"": { ""turnComplete"": true } }");

        Assert.Equal(1, interrupted);
    }

    [Fact]
    public void Every_Inline_Audio_Part_Raises_OnAudioReceived_In_Order()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var received = new List<(byte[] Pcm, int SampleRate)>();
        endPoint.OnAudioReceived += (pcm, rate) => received.Add((pcm, rate));

        transport.RaiseMessage(@"{
  ""serverContent"": {
    ""modelTurn"": {
      ""parts"": [
        { ""inlineData"": { ""mimeType"": ""audio/pcm;rate=24000"", ""data"": ""AAE="" } },
        { ""text"": ""not audio"" },
        { ""inlineData"": { ""mimeType"": ""image/png"", ""data"": ""AAE="" } },
        { ""inlineData"": { ""mimeType"": ""audio/pcm;rate=16000"", ""data"": ""AgM="" } }
      ]
    }
  }
}");

        Assert.Equal(2, received.Count);
        Assert.Equal(new byte[] { 0, 1 }, received[0].Pcm);
        Assert.Equal(24000, received[0].SampleRate);
        Assert.Equal(new byte[] { 2, 3 }, received[1].Pcm);
        Assert.Equal(16000, received[1].SampleRate);
    }

    [Theory]
    [InlineData("audio/pcm;rate=24000", 24000)]
    [InlineData("audio/pcm;rate=16000;codec=pcm", 16000)]
    [InlineData("AUDIO/PCM;RATE=8000", 8000)]
    [InlineData("audio/pcm", GeminiLiveEndPoint.DEFAULT_OUTPUT_SAMPLE_RATE)]
    [InlineData("audio/pcm;rate=abc", GeminiLiveEndPoint.DEFAULT_OUTPUT_SAMPLE_RATE)]
    public void Sample_Rate_Is_Taken_From_The_Mime_Type(string mimeType, int expectedSampleRate)
    {
        logger.LogDebug("--> {MethodName} {MimeType}", System.Reflection.MethodBase.GetCurrentMethod()?.Name, mimeType);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var sampleRates = new List<int>();
        endPoint.OnAudioReceived += (_, rate) => sampleRates.Add(rate);

        transport.RaiseMessage(
            $@"{{ ""serverContent"": {{ ""modelTurn"": {{ ""parts"": [ {{ ""inlineData"": {{ ""mimeType"": ""{mimeType}"", ""data"": ""AAE="" }} }} ] }} }} }}");

        Assert.Equal(expectedSampleRate, Assert.Single(sampleRates));
    }

    [Fact]
    public void Invalid_Base64_Audio_Is_Skipped_Without_Losing_The_Rest_Of_The_Message()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, endPointLog) = CreateEndPoint();
        using var lifetime = endPoint;

        var received = new List<byte[]>();
        endPoint.OnAudioReceived += (pcm, _) => received.Add(pcm);

        // Would previously have thrown a FormatException out of the receive loop and silently
        // ended the session.
        transport.RaiseMessage(@"{
  ""serverContent"": {
    ""modelTurn"": {
      ""parts"": [
        { ""inlineData"": { ""mimeType"": ""audio/pcm;rate=24000"", ""data"": ""not valid base64!"" } },
        { ""inlineData"": { ""mimeType"": ""audio/pcm;rate=24000"", ""data"": ""AAE="" } }
      ]
    }
  }
}");

        Assert.Equal(new byte[] { 0, 1 }, Assert.Single(received));
        Assert.True(endPointLog.Contains(LogLevel.Warning, "invalid base64"));
    }

    [Fact]
    public void A_Throwing_Event_Handler_Neither_Escapes_Nor_Silences_Other_Handlers()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, endPointLog) = CreateEndPoint();
        using var lifetime = endPoint;

        var secondHandlerCalls = 0;
        endPoint.OnServerMessage += _ => throw new InvalidOperationException("consumer bug");
        endPoint.OnServerMessage += _ => secondHandlerCalls++;
        endPoint.OnConnected += () => throw new InvalidOperationException("consumer bug in OnConnected");

        // Raising must not throw: on the real transport this runs on the receive loop, and an
        // escaping exception there kills the session.
        transport.RaiseMessage(@"{ ""setupComplete"": {} }");
        transport.RaiseMessage(@"{ ""setupComplete"": {} }");

        Assert.Equal(2, secondHandlerCalls);
        Assert.True(endPointLog.Contains(LogLevel.Error, "event handler threw an exception"));
    }

    [Fact]
    public async Task Transport_Close_Raises_OnClosed_And_Requires_A_Fresh_Setup()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        await endPoint.StartConnect();

        var closed = 0;
        endPoint.OnClosed += () => closed++;

        transport.RaiseClosed();

        Assert.Equal(1, closed);

        // The gate has to close with the socket: audio may not precede the next session's setup.
        endPoint.SendAudio(new byte[] { 1, 2, 3, 4 });
        await Task.Delay(100);
        Assert.Single(transport.Sent);
    }

    [Fact]
    public void Transport_Error_Raises_OnFailed()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        var failed = 0;
        endPoint.OnFailed += () => failed++;

        transport.RaiseError(new InvalidOperationException("socket died"));

        Assert.Equal(1, failed);
    }

    [Fact]
    public async Task Close_Closes_The_Transport_And_Allows_Reconnecting()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        using var lifetime = endPoint;

        await endPoint.StartConnect();
        await endPoint.Close();

        Assert.Equal(1, transport.CloseCount);
        Assert.True((await endPoint.SendText("after close")).IsLeft);

        Assert.True((await endPoint.StartConnect()).IsRight);
        Assert.Equal(2, transport.ConnectCount);
    }

    [Fact]
    public async Task Dispose_Disposes_The_Transport_And_Is_Idempotent()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        await endPoint.StartConnect();

        endPoint.Dispose();
        endPoint.Dispose();

        Assert.Equal(1, transport.DisposeCount);
        await endPoint.Close();
        Assert.Equal(0, transport.CloseCount);
    }

    [Fact]
    public async Task DisposeAsync_Closes_Then_Disposes_The_Transport()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (endPoint, transport, _) = CreateEndPoint();
        await endPoint.StartConnect();

        await endPoint.DisposeAsync();
        await endPoint.DisposeAsync();

        Assert.Equal(1, transport.CloseCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public void Non_Dependency_Injection_Constructor_Builds_A_Usable_End_Point()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance));

        using var endPoint = new GeminiLiveEndPoint("test-api-key", loggerFactory);

        Assert.NotNull(endPoint.Messenger);
        Assert.Equal(0, endPoint.DroppedAudioChunks);
    }
}
