using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

public class GeminiVoiceConfig
{
    [JsonPropertyName("prebuiltVoiceConfig")]
    public GeminiPrebuiltVoiceConfig? PrebuiltVoiceConfig { get; set; }
}
