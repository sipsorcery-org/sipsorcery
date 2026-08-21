using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// A single turn of conversation content, either supplied by the client (<see cref="GeminiClientContentMessage"/>,
/// <see cref="GeminiSetup.SystemInstruction"/>) or returned by the model (<see cref="GeminiServerEventContent.ModelTurn"/>).
/// </summary>
public class GeminiContent
{
    [JsonPropertyName("role")]
    public GeminiRoleEnum? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<GeminiPart>? Parts { get; set; }
}
