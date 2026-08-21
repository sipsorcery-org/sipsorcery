using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

public class GeminiSpeechConfig
{
    [JsonPropertyName("voiceConfig")]
    public GeminiVoiceConfig? VoiceConfig { get; set; }

    [JsonPropertyName("languageCode")]
    public string? LanguageCode { get; set; }
}
