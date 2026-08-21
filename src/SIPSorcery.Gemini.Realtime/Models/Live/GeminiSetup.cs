using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// The BidiGenerateContentSetup payload, sent as the first message on a new WebSocket connection
/// to configure the session before any audio/content is exchanged.
/// </summary>
public class GeminiSetup
{
    /// <summary>
    /// Required. Format "models/{model-id}", e.g. "models/gemini-2.5-flash-native-audio-latest". See
    /// <see cref="GeminiLiveModelsEnum"/> for well-known values and their <c>ToEnumString()</c>
    /// wire representation.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = GeminiLiveModelsEnum.Gemini25FlashNativeAudioLatest.ToEnumString();

    [JsonPropertyName("generationConfig")]
    public GeminiGenerationConfig? GenerationConfig { get; set; }

    [JsonPropertyName("systemInstruction")]
    public GeminiContent? SystemInstruction { get; set; }

    [JsonPropertyName("tools")]
    public List<GeminiTool>? Tools { get; set; }

    [JsonPropertyName("realtimeInputConfig")]
    public GeminiRealtimeInputConfig? RealtimeInputConfig { get; set; }

    [JsonPropertyName("sessionResumption")]
    public GeminiSessionResumption? SessionResumption { get; set; }

    [JsonPropertyName("contextWindowCompression")]
    public GeminiContextWindowCompression? ContextWindowCompression { get; set; }

    /// <summary>
    /// Assign an (empty) <see cref="GeminiAudioTranscriptionConfig"/> instance to enable
    /// transcription of the caller's audio.
    /// </summary>
    [JsonPropertyName("inputAudioTranscription")]
    public GeminiAudioTranscriptionConfig? InputAudioTranscription { get; set; }

    /// <summary>
    /// Assign an (empty) <see cref="GeminiAudioTranscriptionConfig"/> instance to enable
    /// transcription of the model's spoken audio output.
    /// </summary>
    [JsonPropertyName("outputAudioTranscription")]
    public GeminiAudioTranscriptionConfig? OutputAudioTranscription { get; set; }

    /// <summary>
    /// Returns a shallow copy of this setup. Used so that connecting with an explicit model does
    /// not mutate the caller's instance — the nested configuration objects are shared with the
    /// original, only the top-level scalar properties are safe to reassign on the copy.
    /// </summary>
    public GeminiSetup ShallowCopy() => (GeminiSetup)MemberwiseClone();
}
