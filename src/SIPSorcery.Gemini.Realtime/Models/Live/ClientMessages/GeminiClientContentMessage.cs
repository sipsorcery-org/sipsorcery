using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Wire shape: {"clientContent": {"turns": [...], "turnComplete": true}}. Used to send text (or
/// pre-recorded, non-realtime) conversation turns, as an alternative/complement to
/// <see cref="GeminiRealtimeInputMessage"/>.
/// </summary>
public class GeminiClientContentMessage : GeminiClientMessage
{
    [JsonPropertyName("clientContent")]
    public GeminiClientContent ClientContent { get; set; } = new();
}

public class GeminiClientContent
{
    [JsonPropertyName("turns")]
    public List<GeminiContent>? Turns { get; set; }

    [JsonPropertyName("turnComplete")]
    public bool? TurnComplete { get; set; }
}
