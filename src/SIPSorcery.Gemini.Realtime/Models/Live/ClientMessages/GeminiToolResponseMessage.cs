using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Wire shape: {"toolResponse": {"functionResponses": [...]}}. Sent in reply to a
/// <see cref="GeminiServerEventToolCall"/>.
/// </summary>
public class GeminiToolResponseMessage : GeminiClientMessage
{
    [JsonPropertyName("toolResponse")]
    public GeminiToolResponse ToolResponse { get; set; } = new();
}

public class GeminiToolResponse
{
    [JsonPropertyName("functionResponses")]
    public List<GeminiFunctionResponse>? FunctionResponses { get; set; }
}
