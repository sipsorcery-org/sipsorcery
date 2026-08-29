using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

public class GeminiGenerationConfig
{
    [JsonPropertyName("candidateCount")]
    public int? CandidateCount { get; set; }

    [JsonPropertyName("maxOutputTokens")]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("topP")]
    public double? TopP { get; set; }

    [JsonPropertyName("topK")]
    public int? TopK { get; set; }

    [JsonPropertyName("presencePenalty")]
    public double? PresencePenalty { get; set; }

    [JsonPropertyName("frequencyPenalty")]
    public double? FrequencyPenalty { get; set; }

    [JsonPropertyName("responseModalities")]
    public List<GeminiResponseModalityEnum>? ResponseModalities { get; set; }

    [JsonPropertyName("speechConfig")]
    public GeminiSpeechConfig? SpeechConfig { get; set; }
}
