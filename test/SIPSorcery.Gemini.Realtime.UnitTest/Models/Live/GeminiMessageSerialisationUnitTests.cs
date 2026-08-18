using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Gemini.Realtime.Models;
using SIPSorcery.Gemini.Realtime.UnitTests;
using Xunit;

namespace SIPSorcery.Gemini.Realtime.Models.UnitTests;

[Trait("Category", "unit")]
public class GeminiMessageSerialisationUnitTests
{
    private ILogger logger = NullLogger.Instance;

    public GeminiMessageSerialisationUnitTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        logger = TestLogHelper.InitTestLogger(output);
    }

    [Fact]
    public void Deserialise_Unknown_ServerMessage_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        string json = @"{ ""someBrandNewMessageType"": { ""foo"": ""bar"" } }";

        logger.LogDebug(json);

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        Assert.NotNull(parsed);
        var unknown = Assert.IsType<GeminiUnknownServerMessage>(parsed);
        Assert.Equal("someBrandNewMessageType", unknown.OriginalKey);
        Assert.False(string.IsNullOrEmpty(unknown.OriginalJson));
        Assert.Equal(GeminiServerMessageKind.Unknown, unknown.Kind);
    }

    [Fact]
    public void Deserialise_SetupComplete_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        string json = @"{ ""setupComplete"": {} }";

        logger.LogDebug(json);

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        Assert.NotNull(parsed);
        Assert.IsType<GeminiServerEventSetupComplete>(parsed);
        Assert.Equal(GeminiServerMessageKind.SetupComplete, parsed!.Kind);
    }

    [Fact]
    public void Deserialise_ServerContent_With_ModelTurn_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        string json = @"{
  ""serverContent"": {
    ""modelTurn"": {
      ""role"": ""model"",
      ""parts"": [
        { ""text"": ""Hello there"" },
        { ""inlineData"": { ""mimeType"": ""audio/pcm;rate=24000"", ""data"": ""AAECAw=="" } }
      ]
    },
    ""turnComplete"": true,
    ""outputTranscription"": { ""text"": ""Hello there"" }
  }
}";

        logger.LogDebug(json);

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var content = Assert.IsType<GeminiServerEventContent>(parsed);
        Assert.Equal(GeminiServerMessageKind.ServerContent, content.Kind);
        Assert.True(content.TurnComplete);
        Assert.Equal("Hello there", content.OutputTranscription?.Text);
        Assert.NotNull(content.ModelTurn?.Parts);
        Assert.Equal(2, content.ModelTurn!.Parts!.Count);
        Assert.Equal("Hello there", content.ModelTurn.Parts[0].Text);
        Assert.Equal("audio/pcm;rate=24000", content.ModelTurn.Parts[1].InlineData?.MimeType);
        Assert.Equal("AAECAw==", content.ModelTurn.Parts[1].InlineData?.Data);
    }

    [Fact]
    public void Deserialise_ServerContent_Interrupted_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        string json = @"{ ""serverContent"": { ""interrupted"": true } }";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var content = Assert.IsType<GeminiServerEventContent>(parsed);
        Assert.True(content.Interrupted);
    }

    [Fact]
    public void Deserialise_ToolCall_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        string json = @"{
  ""toolCall"": {
    ""functionCalls"": [
      { ""id"": ""call-1"", ""name"": ""getWeather"", ""args"": { ""city"": ""Dublin"" } }
    ]
  }
}";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var toolCall = Assert.IsType<GeminiServerEventToolCall>(parsed);
        Assert.Equal(GeminiServerMessageKind.ToolCall, toolCall.Kind);
        Assert.NotNull(toolCall.FunctionCalls);
        var call = Assert.Single(toolCall.FunctionCalls!);
        Assert.Equal("call-1", call.Id);
        Assert.Equal("getWeather", call.Name);
        Assert.Equal("Dublin", call.Args!.Value.GetProperty("city").GetString());
    }

    [Fact]
    public void Deserialise_ToolCallCancellation_Test()
    {
        string json = @"{ ""toolCallCancellation"": { ""ids"": [""call-1"", ""call-2""] } }";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var cancellation = Assert.IsType<GeminiServerEventToolCallCancellation>(parsed);
        Assert.Equal(new[] { "call-1", "call-2" }, cancellation.Ids);
    }

    [Fact]
    public void Deserialise_UsageMetadata_Test()
    {
        string json = @"{ ""usageMetadata"": { ""promptTokenCount"": 12, ""responseTokenCount"": 34, ""totalTokenCount"": 46 } }";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var usage = Assert.IsType<GeminiServerEventUsageMetadata>(parsed);
        Assert.Equal(GeminiServerMessageKind.UsageMetadata, usage.Kind);
        Assert.Equal(12, usage.UsageMetadata?.PromptTokenCount);
        Assert.Equal(34, usage.UsageMetadata?.ResponseTokenCount);
        Assert.Equal(46, usage.UsageMetadata?.TotalTokenCount);
    }

    [Fact]
    public void Deserialise_UsageMetadata_Alongside_ServerContent_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        // Gemini carries usageMetadata as a sibling of the message-type union, so it shares the JSON
        // object with serverContent. Keying off the first recognised property would silently discard
        // one half of this message.
        string json = @"{
  ""serverContent"": {
    ""modelTurn"": { ""parts"": [ { ""inlineData"": { ""mimeType"": ""audio/pcm;rate=24000"", ""data"": ""AAECAw=="" } } ] }
  },
  ""usageMetadata"": { ""totalTokenCount"": 99, ""responseTokensDetails"": [ { ""modality"": ""AUDIO"", ""tokenCount"": 90 } ] }
}";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var content = Assert.IsType<GeminiServerEventContent>(parsed);
        Assert.Equal("AAECAw==", content.ModelTurn?.Parts?[0].InlineData?.Data);
        Assert.Equal(99, content.UsageMetadata?.TotalTokenCount);
        Assert.Equal("AUDIO", content.UsageMetadata?.ResponseTokensDetails?[0].Modality);
        Assert.Equal(90, content.UsageMetadata?.ResponseTokensDetails?[0].TokenCount);
    }

    [Fact]
    public void Deserialise_UsageMetadata_Before_ServerContent_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        // Same message with the properties the other way round: the audio must not be lost just
        // because usageMetadata was serialised first.
        string json = @"{
  ""usageMetadata"": { ""totalTokenCount"": 99 },
  ""serverContent"": {
    ""modelTurn"": { ""parts"": [ { ""inlineData"": { ""mimeType"": ""audio/pcm;rate=24000"", ""data"": ""AAECAw=="" } } ] }
  }
}";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var content = Assert.IsType<GeminiServerEventContent>(parsed);
        Assert.Equal("AAECAw==", content.ModelTurn?.Parts?[0].InlineData?.Data);
        Assert.Equal(99, content.UsageMetadata?.TotalTokenCount);
    }

    [Fact]
    public void Deserialise_Non_Object_Payload_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>("[ 1, 2 ]", JsonOptions.Default);

        var unknown = Assert.IsType<GeminiUnknownServerMessage>(parsed);
        Assert.Null(unknown.OriginalKey);
        Assert.False(string.IsNullOrEmpty(unknown.OriginalJson));
    }

    [Fact]
    public void ServerMessage_ToJson_Omits_The_Helper_Properties_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var message = JsonSerializer.Deserialize<GeminiServerMessage>(
            @"{ ""serverContent"": { ""turnComplete"": true }, ""usageMetadata"": { ""totalTokenCount"": 3 } }",
            JsonOptions.Default)!;

        string json = message.ToJson();
        logger.LogDebug(json);

        using var doc = JsonDocument.Parse(json);

        // Kind and UsageMetadata are for consumers, not for the wire.
        Assert.False(doc.RootElement.TryGetProperty("Kind", out _));
        Assert.False(doc.RootElement.TryGetProperty("UsageMetadata", out _));
        Assert.True(doc.RootElement.GetProperty("turnComplete").GetBoolean());
    }

    [Fact]
    public void TypeMap_Covers_Every_Message_Kind_Except_The_Sibling_Fields_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        Assert.DoesNotContain(GeminiServerEventUsageMetadata.JsonKey, GeminiServerMessageTypes.TypeMap.Keys);
        Assert.Equal(6, GeminiServerMessageTypes.TypeMap.Count);
    }

    [Fact]
    public void Deserialise_GoAway_Test()
    {
        string json = @"{ ""goAway"": { ""timeLeft"": ""10s"" } }";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var goAway = Assert.IsType<GeminiServerEventGoAway>(parsed);
        Assert.Equal("10s", goAway.TimeLeft);
    }

    [Fact]
    public void Deserialise_SessionResumptionUpdate_Test()
    {
        string json = @"{ ""sessionResumptionUpdate"": { ""newHandle"": ""abc123"", ""resumable"": true } }";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var update = Assert.IsType<GeminiServerEventSessionResumptionUpdate>(parsed);
        Assert.Equal("abc123", update.NewHandle);
        Assert.True(update.Resumable);
    }

    [Fact]
    public void Roundtrip_ClientSetupMessage_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var message = new GeminiClientSetupMessage
        {
            Setup = new GeminiSetup
            {
                Model = GeminiLiveModelsEnum.Gemini25FlashNativeAudioLatest.ToEnumString(),
                GenerationConfig = new GeminiGenerationConfig
                {
                    ResponseModalities = new List<GeminiResponseModalityEnum> { GeminiResponseModalityEnum.AUDIO },
                    SpeechConfig = new GeminiSpeechConfig
                    {
                        VoiceConfig = new GeminiVoiceConfig
                        {
                            PrebuiltVoiceConfig = new GeminiPrebuiltVoiceConfig { VoiceName = GeminiVoiceEnum.Puck }
                        }
                    }
                }
            }
        };

        string json = message.ToJson();
        logger.LogDebug(json);

        using var doc = JsonDocument.Parse(json);
        var setup = doc.RootElement.GetProperty("setup");
        Assert.Equal("models/gemini-2.5-flash-native-audio-latest", setup.GetProperty("model").GetString());
        Assert.Equal("AUDIO", setup.GetProperty("generationConfig").GetProperty("responseModalities")[0].GetString());
        Assert.Equal(
            "Puck",
            setup.GetProperty("generationConfig")
                 .GetProperty("speechConfig")
                 .GetProperty("voiceConfig")
                 .GetProperty("prebuiltVoiceConfig")
                 .GetProperty("voiceName")
                 .GetString());
    }

    [Fact]
    public void Roundtrip_Fully_Populated_Setup_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var message = new GeminiClientSetupMessage
        {
            Setup = new GeminiSetup
            {
                Model = GeminiLiveModelsEnum.Gemini31FlashLivePreview.ToEnumString(),
                SystemInstruction = new GeminiContent
                {
                    Parts = new List<GeminiPart> { new GeminiPart { Text = "Be brief." } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    CandidateCount = 1,
                    MaxOutputTokens = 512,
                    Temperature = 0.7,
                    TopP = 0.9,
                    TopK = 40,
                    PresencePenalty = 0.1,
                    FrequencyPenalty = 0.2,
                    ResponseModalities = new List<GeminiResponseModalityEnum> { GeminiResponseModalityEnum.AUDIO },
                    SpeechConfig = new GeminiSpeechConfig { LanguageCode = "pl-PL" }
                },
                Tools = new List<GeminiTool>
                {
                    new GeminiTool
                    {
                        FunctionDeclarations = new List<GeminiFunctionDeclaration>
                        {
                            new GeminiFunctionDeclaration
                            {
                                Name = "transferCall",
                                Description = "Transfers the caller to a human.",
                                Parameters = JsonDocument.Parse(@"{ ""type"": ""object"" }").RootElement
                            }
                        }
                    }
                },
                RealtimeInputConfig = new GeminiRealtimeInputConfig
                {
                    ActivityHandling = GeminiActivityHandlingEnum.START_OF_ACTIVITY_INTERRUPTS,
                    TurnCoverage = GeminiTurnCoverageEnum.TURN_INCLUDES_ONLY_ACTIVITY,
                    AutomaticActivityDetection = new GeminiAutomaticActivityDetection
                    {
                        Disabled = false,
                        StartOfSpeechSensitivity = GeminiStartSensitivityEnum.START_SENSITIVITY_HIGH,
                        EndOfSpeechSensitivity = GeminiEndSensitivityEnum.END_SENSITIVITY_LOW,
                        PrefixPaddingMs = 20,
                        SilenceDurationMs = 500
                    }
                },
                ContextWindowCompression = new GeminiContextWindowCompression
                {
                    TriggerTokens = 16000,
                    SlidingWindow = new GeminiSlidingWindow { TargetTokens = 8000 }
                },
                SessionResumption = new GeminiSessionResumption { Handle = "handle-123" },
                InputAudioTranscription = new GeminiAudioTranscriptionConfig(),
                OutputAudioTranscription = new GeminiAudioTranscriptionConfig()
            }
        };

        string json = message.ToJson();
        logger.LogDebug(json);

        using var doc = JsonDocument.Parse(json);
        var setup = doc.RootElement.GetProperty("setup");

        Assert.Equal("models/gemini-3.1-flash-live-preview", setup.GetProperty("model").GetString());
        Assert.Equal("Be brief.", setup.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("pl-PL", setup.GetProperty("generationConfig").GetProperty("speechConfig").GetProperty("languageCode").GetString());
        Assert.Equal(40, setup.GetProperty("generationConfig").GetProperty("topK").GetInt32());
        Assert.Equal(
            "transferCall",
            setup.GetProperty("tools")[0].GetProperty("functionDeclarations")[0].GetProperty("name").GetString());
        Assert.Equal(
            "object",
            setup.GetProperty("tools")[0].GetProperty("functionDeclarations")[0].GetProperty("parameters").GetProperty("type").GetString());

        var realtimeInputConfig = setup.GetProperty("realtimeInputConfig");
        Assert.Equal("START_OF_ACTIVITY_INTERRUPTS", realtimeInputConfig.GetProperty("activityHandling").GetString());
        Assert.Equal("TURN_INCLUDES_ONLY_ACTIVITY", realtimeInputConfig.GetProperty("turnCoverage").GetString());

        var vad = realtimeInputConfig.GetProperty("automaticActivityDetection");
        Assert.False(vad.GetProperty("disabled").GetBoolean());
        Assert.Equal("START_SENSITIVITY_HIGH", vad.GetProperty("startOfSpeechSensitivity").GetString());
        Assert.Equal("END_SENSITIVITY_LOW", vad.GetProperty("endOfSpeechSensitivity").GetString());
        Assert.Equal(20, vad.GetProperty("prefixPaddingMs").GetInt32());
        Assert.Equal(500, vad.GetProperty("silenceDurationMs").GetInt32());

        Assert.Equal(16000, setup.GetProperty("contextWindowCompression").GetProperty("triggerTokens").GetInt64());
        Assert.Equal(
            8000,
            setup.GetProperty("contextWindowCompression").GetProperty("slidingWindow").GetProperty("targetTokens").GetInt64());
        Assert.Equal("handle-123", setup.GetProperty("sessionResumption").GetProperty("handle").GetString());

        // Presence-only markers: an empty object enables transcription.
        Assert.Equal(JsonValueKind.Object, setup.GetProperty("inputAudioTranscription").ValueKind);
        Assert.Equal(JsonValueKind.Object, setup.GetProperty("outputAudioTranscription").ValueKind);
    }

    [Fact]
    public void Deserialise_UsageMetadata_All_Fields_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        string json = @"{
  ""usageMetadata"": {
    ""promptTokenCount"": 1,
    ""cachedContentTokenCount"": 2,
    ""responseTokenCount"": 3,
    ""toolUsePromptTokenCount"": 4,
    ""thoughtsTokenCount"": 5,
    ""totalTokenCount"": 15,
    ""promptTokensDetails"": [ { ""modality"": ""AUDIO"", ""tokenCount"": 1 } ],
    ""cacheTokensDetails"": [ { ""modality"": ""TEXT"", ""tokenCount"": 2 } ],
    ""responseTokensDetails"": [ { ""modality"": ""AUDIO"", ""tokenCount"": 3 } ]
  }
}";

        var parsed = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);

        var usage = Assert.IsType<GeminiServerEventUsageMetadata>(parsed).UsageMetadata;
        Assert.NotNull(usage);
        Assert.Equal(1, usage!.PromptTokenCount);
        Assert.Equal(2, usage.CachedContentTokenCount);
        Assert.Equal(3, usage.ResponseTokenCount);
        Assert.Equal(4, usage.ToolUsePromptTokenCount);
        Assert.Equal(5, usage.ThoughtsTokenCount);
        Assert.Equal(15, usage.TotalTokenCount);
        Assert.Equal("AUDIO", usage.PromptTokensDetails?[0].Modality);
        Assert.Equal("TEXT", usage.CacheTokensDetails?[0].Modality);
        Assert.Equal(3, usage.ResponseTokensDetails?[0].TokenCount);
    }

    [Fact]
    public void Roundtrip_ClientContentMessage_Test()
    {
        var message = new GeminiClientContentMessage
        {
            ClientContent = new GeminiClientContent
            {
                Turns = new List<GeminiContent>
                {
                    new GeminiContent
                    {
                        Role = GeminiRoleEnum.user,
                        Parts = new List<GeminiPart> { new GeminiPart { Text = "Say hi!" } }
                    }
                },
                TurnComplete = true
            }
        };

        string json = message.ToJson();
        logger.LogDebug(json);

        using var doc = JsonDocument.Parse(json);
        var clientContent = doc.RootElement.GetProperty("clientContent");
        Assert.True(clientContent.GetProperty("turnComplete").GetBoolean());
        var turn = clientContent.GetProperty("turns")[0];
        Assert.Equal("user", turn.GetProperty("role").GetString());
        Assert.Equal("Say hi!", turn.GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Roundtrip_RealtimeInputMessage_Audio_Test()
    {
        var pcm = new byte[] { 0, 1, 2, 3, 4, 5 };

        var message = new GeminiRealtimeInputMessage
        {
            RealtimeInput = new GeminiRealtimeInput
            {
                Audio = new GeminiBlob
                {
                    MimeType = "audio/pcm;rate=16000",
                    Data = Convert.ToBase64String(pcm)
                }
            }
        };

        string json = message.ToJson();
        logger.LogDebug(json);

        using var doc = JsonDocument.Parse(json);
        var realtimeInput = doc.RootElement.GetProperty("realtimeInput");
        var audio = realtimeInput.GetProperty("audio");
        Assert.Equal("audio/pcm;rate=16000", audio.GetProperty("mimeType").GetString());
        Assert.Equal(pcm, Convert.FromBase64String(audio.GetProperty("data").GetString()!));
    }

    [Fact]
    public void Roundtrip_ToolResponseMessage_Test()
    {
        var message = new GeminiToolResponseMessage
        {
            ToolResponse = new GeminiToolResponse
            {
                FunctionResponses = new List<GeminiFunctionResponse>
                {
                    new GeminiFunctionResponse
                    {
                        Id = "call-1",
                        Name = "getWeather",
                        Response = JsonDocument.Parse(@"{ ""tempC"": 15 }").RootElement
                    }
                }
            }
        };

        string json = message.ToJson();
        logger.LogDebug(json);

        using var doc = JsonDocument.Parse(json);
        var functionResponse = doc.RootElement.GetProperty("toolResponse").GetProperty("functionResponses")[0];
        Assert.Equal("call-1", functionResponse.GetProperty("id").GetString());
        Assert.Equal(15, functionResponse.GetProperty("response").GetProperty("tempC").GetInt32());
    }
}
