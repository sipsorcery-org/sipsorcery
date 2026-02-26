using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when a new Response is created. 
/// This is the first event of response creation, where the response is in an initial state of `in_progress`.
/// </summary>
public class RealtimeServerEventResponseCreated : RealtimeEventBase
{
    public const string TypeName = "response.created";

    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    [JsonPropertyName("response")]
    public required RealtimeResponse Response { get; set; }
}
