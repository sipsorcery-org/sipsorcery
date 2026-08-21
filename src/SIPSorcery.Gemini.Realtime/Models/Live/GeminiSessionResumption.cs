using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Requests resumption of a previous session. Populate <see cref="Handle"/> with the value
/// last received via <see cref="GeminiServerEventSessionResumptionUpdate.NewHandle"/> to resume.
/// Leave empty to start a new resumable session.
/// </summary>
public class GeminiSessionResumption
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}
