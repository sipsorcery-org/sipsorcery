using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Carries a new resumable-session handle. Persist <see cref="NewHandle"/> and pass it via
/// <see cref="GeminiSessionResumption.Handle"/> on a future <see cref="GeminiSetup"/> to resume
/// this session after a disconnect.
/// </summary>
public class GeminiServerEventSessionResumptionUpdate : GeminiServerMessage
{
    public const string JsonKey = "sessionResumptionUpdate";

    public GeminiServerEventSessionResumptionUpdate() : base(GeminiServerMessageKind.SessionResumptionUpdate)
    {
    }

    [JsonPropertyName("newHandle")]
    public string? NewHandle { get; set; }

    [JsonPropertyName("resumable")]
    public bool? Resumable { get; set; }
}
