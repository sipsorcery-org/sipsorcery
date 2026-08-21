using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Warns that the server will terminate the connection soon (e.g. approaching the maximum
/// session duration). <see cref="TimeLeft"/> is an ISO 8601 duration string.
/// </summary>
public class GeminiServerEventGoAway : GeminiServerMessage
{
    public const string JsonKey = "goAway";

    public GeminiServerEventGoAway() : base(GeminiServerMessageKind.GoAway)
    {
    }

    [JsonPropertyName("timeLeft")]
    public string? TimeLeft { get; set; }
}
