using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

public class GeminiPrebuiltVoiceConfig
{
    [JsonPropertyName("voiceName")]
    public GeminiVoiceEnum? VoiceName { get; set; }
}
