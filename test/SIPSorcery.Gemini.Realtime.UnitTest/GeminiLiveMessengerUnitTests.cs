using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Gemini.Realtime.Models;
using Xunit;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

[Trait("Category", "unit")]
public class GeminiLiveMessengerUnitTests
{
    private readonly ILogger logger;

    public GeminiLiveMessengerUnitTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        logger = TestLogHelper.InitTestLogger(output);
    }

    private static (GeminiLiveMessenger Messenger, FakeGeminiLiveWebSocketClient Transport, CapturingLogger Log) CreateMessenger(bool connected = true)
    {
        var transport = new FakeGeminiLiveWebSocketClient { IsConnected = connected };
        var log = new CapturingLogger();

        return (new GeminiLiveMessenger(transport, log), transport, log);
    }

    private static JsonElement RootOfOnlySentMessage(FakeGeminiLiveWebSocketClient transport)
    {
        var sent = Assert.Single(transport.Sent);
        return JsonDocument.Parse(sent).RootElement.Clone();
    }

    [Fact]
    public async Task Send_Returns_Left_When_Not_Connected()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, _) = CreateMessenger(connected: false);

        var result = await messenger.SendSetupAsync(new GeminiSetup());

        Assert.True(result.IsLeft);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task Send_Returns_Left_When_The_Transport_Throws()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, log) = CreateMessenger();
        transport.SendException = new InvalidOperationException("socket write failed");

        var result = await messenger.SendSetupAsync(new GeminiSetup());

        Assert.True(result.IsLeft);
        Assert.True(log.Contains(LogLevel.Error, "Failed to send"));
    }

    [Fact]
    public async Task SendSetupAsync_Wraps_The_Payload_Under_Setup()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, _) = CreateMessenger();

        var result = await messenger.SendSetupAsync(new GeminiSetup { Model = "models/test" });

        Assert.True(result.IsRight);
        Assert.Equal("models/test", RootOfOnlySentMessage(transport).GetProperty("setup").GetProperty("model").GetString());
    }

    [Fact]
    public async Task SendClientContentAsync_Carries_Role_And_Turn_Completion()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, _) = CreateMessenger();

        await messenger.SendClientContentAsync("some text", turnComplete: false, role: GeminiRoleEnum.model);

        var clientContent = RootOfOnlySentMessage(transport).GetProperty("clientContent");
        Assert.False(clientContent.GetProperty("turnComplete").GetBoolean());

        var turn = clientContent.GetProperty("turns")[0];
        Assert.Equal("model", turn.GetProperty("role").GetString());
        Assert.Equal("some text", turn.GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendRealtimeInputAudioAsync_Honours_A_Custom_Mime_Type()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, _) = CreateMessenger();

        var pcm = new byte[] { 9, 8, 7 };
        await messenger.SendRealtimeInputAudioAsync(pcm, "audio/pcm;rate=8000");

        var audio = RootOfOnlySentMessage(transport).GetProperty("realtimeInput").GetProperty("audio");
        Assert.Equal("audio/pcm;rate=8000", audio.GetProperty("mimeType").GetString());
        Assert.Equal(pcm, Convert.FromBase64String(audio.GetProperty("data").GetString()!));
    }

    [Fact]
    public async Task SendAudioStreamEndAsync_Sets_The_Stream_End_Flag()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, _) = CreateMessenger();

        await messenger.SendAudioStreamEndAsync();

        Assert.True(RootOfOnlySentMessage(transport).GetProperty("realtimeInput").GetProperty("audioStreamEnd").GetBoolean());
    }

    [Fact]
    public async Task SendActivityStartAsync_Sends_An_Empty_Activity_Marker()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, _) = CreateMessenger();

        await messenger.SendActivityStartAsync();

        var realtimeInput = RootOfOnlySentMessage(transport).GetProperty("realtimeInput");
        Assert.Equal(JsonValueKind.Object, realtimeInput.GetProperty("activityStart").ValueKind);
    }

    [Fact]
    public async Task SendActivityEndAsync_Sends_An_Empty_Activity_Marker()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, _) = CreateMessenger();

        await messenger.SendActivityEndAsync();

        var realtimeInput = RootOfOnlySentMessage(transport).GetProperty("realtimeInput");
        Assert.Equal(JsonValueKind.Object, realtimeInput.GetProperty("activityEnd").ValueKind);
    }

    [Fact]
    public async Task SendToolResponseAsync_Sends_Every_Function_Response()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, transport, _) = CreateMessenger();

        await messenger.SendToolResponseAsync(new[]
        {
            new GeminiFunctionResponse { Id = "call-1", Name = "one" },
            new GeminiFunctionResponse { Id = "call-2", Name = "two" }
        });

        var functionResponses = RootOfOnlySentMessage(transport).GetProperty("toolResponse").GetProperty("functionResponses");
        Assert.Equal(2, functionResponses.GetArrayLength());
        Assert.Equal("call-2", functionResponses[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Audio_Payloads_Are_Not_Written_To_The_Log()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, log) = CreateMessenger();

        // A recognisable "payload" standing in for a real 3200-byte audio chunk.
        var pcm = new byte[600];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = 0xAB;
        }

        var base64 = Convert.ToBase64String(pcm);

        await messenger.SendRealtimeInputAudioAsync(pcm);

        Assert.False(log.ContainsAnywhere(base64), "the base64 audio payload must not be logged");
        Assert.True(log.Contains(LogLevel.Trace, "base64 chars"));
    }

    [Fact]
    public void HandleIncomingMessage_Raises_The_Parsed_Message()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, _) = CreateMessenger();

        var received = new List<GeminiServerMessage>();
        messenger.OnServerMessage += received.Add;

        messenger.HandleIncomingMessage(@"{ ""setupComplete"": {} }");

        Assert.IsType<GeminiServerEventSetupComplete>(Assert.Single(received));
    }

    [Fact]
    public void HandleIncomingMessage_Falls_Back_To_Unknown_For_Malformed_Json()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, log) = CreateMessenger();

        GeminiServerMessage? received = null;
        messenger.OnServerMessage += message => received = message;

        messenger.HandleIncomingMessage(@"{ ""serverContent"": { this is not json }");

        var unknown = Assert.IsType<GeminiUnknownServerMessage>(received);
        Assert.Contains("serverContent", unknown.OriginalJson);
        Assert.True(log.Contains(LogLevel.Warning, "Failed to deserialise"));
    }

    [Fact]
    public void HandleIncomingMessage_Falls_Back_To_Unknown_For_An_Unrecognised_Key()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, log) = CreateMessenger();

        GeminiServerMessage? received = null;
        messenger.OnServerMessage += message => received = message;

        messenger.HandleIncomingMessage(@"{ ""somethingGoogleAddedLater"": { ""a"": 1 } }");

        var unknown = Assert.IsType<GeminiUnknownServerMessage>(received);
        Assert.Equal("somethingGoogleAddedLater", unknown.OriginalKey);
        Assert.True(log.Contains(LogLevel.Warning, "Unrecognised"));
    }

    [Fact]
    public void HandleIncomingMessage_Falls_Back_To_Unknown_For_A_Payload_It_Cannot_Bind()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, _) = CreateMessenger();

        GeminiServerMessage? received = null;
        messenger.OnServerMessage += message => received = message;

        // A field whose type doesn't match what this library expects — the shape of change a future
        // Gemini revision can introduce. The session must survive it with the payload intact.
        messenger.HandleIncomingMessage(@"{ ""serverContent"": { ""turnComplete"": ""yes-please"" } }");

        var unknown = Assert.IsType<GeminiUnknownServerMessage>(received);
        Assert.Equal("serverContent", unknown.OriginalKey);
        Assert.Contains("yes-please", unknown.OriginalJson);
    }

    [Fact]
    public void HandleIncomingMessage_Tolerates_An_Unknown_Enum_Value()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, _) = CreateMessenger();

        GeminiServerMessage? received = null;
        messenger.OnServerMessage += message => received = message;

        // A role value this library has never heard of. Optional enums are lenient, so the rest of
        // the message still binds and only the unknown value is dropped.
        messenger.HandleIncomingMessage(@"{ ""serverContent"": { ""modelTurn"": { ""role"": ""oracle"", ""parts"": [ { ""text"": ""hi"" } ] } } }");

        var content = Assert.IsType<GeminiServerEventContent>(received);
        Assert.Null(content.ModelTurn?.Role);
        Assert.Equal("hi", content.ModelTurn?.Parts?[0].Text);
    }

    [Fact]
    public void HandleIncomingMessage_Ignores_A_Null_Payload()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, log) = CreateMessenger();

        var raised = 0;
        messenger.OnServerMessage += _ => raised++;

        messenger.HandleIncomingMessage("null");

        Assert.Equal(0, raised);
        Assert.True(log.Contains(LogLevel.Warning, "empty/non-JSON"));
    }

    [Fact]
    public void HandleIncomingMessage_Does_Not_Let_A_Consumer_Exception_Escape()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, log) = CreateMessenger();

        messenger.OnServerMessage += _ => throw new InvalidOperationException("consumer bug");

        // On the real transport this runs on the receive loop: an escaping exception there ends the
        // session while the socket is still open.
        messenger.HandleIncomingMessage(@"{ ""setupComplete"": {} }");

        Assert.True(log.Contains(LogLevel.Error, "event handler threw an exception"));
    }

    [Fact]
    public void HandleIncomingMessage_Attaches_Usage_Metadata_To_The_Message_It_Arrived_With()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var (messenger, _, _) = CreateMessenger();

        GeminiServerMessage? received = null;
        messenger.OnServerMessage += message => received = message;

        messenger.HandleIncomingMessage(@"{ ""toolCall"": { ""functionCalls"": [] }, ""usageMetadata"": { ""totalTokenCount"": 5 } }");

        var toolCall = Assert.IsType<GeminiServerEventToolCall>(received);
        Assert.Equal(5, toolCall.UsageMetadata?.TotalTokenCount);
    }
}
