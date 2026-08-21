using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Wire shape: {"setup": {...}}. Must be the first message sent after the WebSocket connects.
/// </summary>
public class GeminiClientSetupMessage : GeminiClientMessage
{
    [JsonPropertyName("setup")]
    public GeminiSetup Setup { get; set; } = new();
}
