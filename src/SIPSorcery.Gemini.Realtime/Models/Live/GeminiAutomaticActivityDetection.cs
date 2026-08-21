using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

public class GeminiAutomaticActivityDetection
{
    [JsonPropertyName("disabled")]
    public bool? Disabled { get; set; }

    [JsonPropertyName("startOfSpeechSensitivity")]
    public GeminiStartSensitivityEnum? StartOfSpeechSensitivity { get; set; }

    [JsonPropertyName("endOfSpeechSensitivity")]
    public GeminiEndSensitivityEnum? EndOfSpeechSensitivity { get; set; }

    [JsonPropertyName("prefixPaddingMs")]
    public int? PrefixPaddingMs { get; set; }

    [JsonPropertyName("silenceDurationMs")]
    public int? SilenceDurationMs { get; set; }
}
