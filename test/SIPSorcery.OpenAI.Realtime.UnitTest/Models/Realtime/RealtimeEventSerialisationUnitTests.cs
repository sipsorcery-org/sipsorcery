using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.OpenAI.Realtime.UnitTests;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SIPSorcery.OpenAI.Realtime.Models.UnitTests;

[Trait("Category", "unit")]
public class RealtimeEventSerialisationUnitTests
{
    private ILogger logger = NullLogger.Instance;

    public RealtimeEventSerialisationUnitTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        logger = TestLogHelper.InitTestLogger(output);
    }

    [Fact]
    public void Deserialise_Unkown_Event_Json_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        string json = @"{
    ""type"": ""unknown"",
    ""event_id"": ""123""
}";

        logger.LogDebug(json);

        RealtimeEventBase? parsedEvent = JsonSerializer.Deserialize<RealtimeEventBase>(json, JsonOptions.Default);
        
        Assert.NotNull(parsedEvent);
        Assert.True(parsedEvent is RealtimeEventBase);
        Assert.True(parsedEvent is RealtimeUnknown);
        Assert.True(parsedEvent is RealtimeUnknown unknown && !string.IsNullOrEmpty(unknown.OriginalJson));
    }

    [Fact]
    public void Deserialise_Transcription_Session__Update_Event_Json_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        string json = @"{
    ""type"": ""transcription_session.update"",
    ""event_id"": ""123"",
    ""session"": {
      ""modalities"": [
        ""audio"",
        ""text""
      ],
      ""input_audio_format"": ""pcm16"",
      ""output_audio_format"": ""pcm16"",
      ""tool_choice"": ""auto""
    }
}";

        logger.LogDebug(json);

        RealtimeEventBase? parsedEvent = JsonSerializer.Deserialize<RealtimeEventBase>(json, JsonOptions.Default);

        Assert.NotNull(parsedEvent);
        Assert.True(parsedEvent is RealtimeClientEventTranscriptionSessionUpdate);
    }

    [Fact]
    public void Roundtrip_Transcription_Session__Update_Event_Json_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var transcriptionSessionUpdate = new RealtimeClientEventTranscriptionSessionUpdate
        {
            EventID = Guid.NewGuid().ToString("N"),
            Session = new RealtimeTranscriptionSessionCreateRequest()
        };

        string json = transcriptionSessionUpdate.ToJson();

        logger.LogDebug(json);

        RealtimeEventBase? parsedEvent = JsonSerializer.Deserialize<RealtimeEventBase>(json, JsonOptions.Default);

        Assert.NotNull(parsedEvent);
        Assert.True(parsedEvent is RealtimeClientEventTranscriptionSessionUpdate);
    }

    [Fact]
    public void Roundtrip_ConversationItemCreate_Test()
    {
        var testEvent = new RealtimeClientEventConversationItemCreate
        {
            EventID = Guid.NewGuid().ToString("N"),
            Item = new RealtimeConversationItem
            {
                Type = RealtimeConversationItemTypeEnum.message,
                Object = "realtime.item."
            }
        };

        RoundtripTest(testEvent);
    }

    [Fact]
    public void Roundtrip_ConversationItemDelete_Test()
    {
        var testEvent = new RealtimeClientEventConversationItemDelete
        {
            EventID = Guid.NewGuid().ToString("N"),
            ItemID = Guid.NewGuid().ToString("N")
        };

        RoundtripTest(testEvent);
    }

    [Fact]
    public void Roundtrip_ConversationItemRetrieve_Test()
    {
        var testEvent = new RealtimeClientEventConversationItemRetrieve
        {
            EventID = Guid.NewGuid().ToString("N"),
            ItemID = Guid.NewGuid().ToString("N")
        };

        RoundtripTest(testEvent);
    }

    [Fact]
    public void Roundtrip_InputAudioBufferAppend_Test()
    {
        var testEvent = new RealtimeClientEventInputAudioBufferAppend
        {
            EventID = Guid.NewGuid().ToString("N"),
            Audio = "xx"
        };

        RoundtripTest(testEvent);
    }

    [Fact]
    public void Roundtrip_SessionUpdate_Test()
    {
        var testEvent = new RealtimeClientEventSessionUpdate
        {
            EventID = Guid.NewGuid().ToString("N"),
            Session = new RealtimeSession
            {
                // Initialize required session properties
                Model = RealtimeModelsEnum.Gpt4oRealtimePreview,
                Modalities = [ RealtimeModalityEnum.audio, RealtimeModalityEnum.text ]
            }
        };

        RoundtripTest(testEvent);
    }

    private void RoundtripTest(RealtimeEventBase testEvent)
    {
        logger.LogDebug("Testing event type: {Type}", testEvent.GetType().Name);

        // Serialize
        string json = testEvent.ToJson();
        logger.LogDebug("Serialized JSON: {Json}", json);

        // Deserialize
        var deserialized = JsonSerializer.Deserialize<RealtimeEventBase>(json, JsonOptions.Default);

        // Verify
        Assert.NotNull(deserialized);
        Assert.IsType(testEvent.GetType(), deserialized);
        Assert.Equal(testEvent.EventID, deserialized.EventID);

        // Verify type discriminator
        using var doc = JsonDocument.Parse(json);
        var typeName = doc.RootElement.GetProperty("type").GetString();
        Assert.Equal(RealtimeEventTypes.TypeMap.First(x => x.Value == testEvent.GetType()).Key, typeName);
    }
}

