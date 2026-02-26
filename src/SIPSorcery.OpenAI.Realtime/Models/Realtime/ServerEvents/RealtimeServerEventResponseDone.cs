using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when a Response is done streaming. 
/// Always emitted, regardless of the final state. 
/// The response will include all output items but omit the raw audio data.
/// </summary>
public class RealtimeServerEventResponseDone : RealtimeEventBase
{
    public const string TypeName = "response.done";

    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The full response object, containing all output items (but excluding raw audio data).
    /// </summary>
    [JsonPropertyName("response")]
    public required RealtimeResponse Response { get; set; }
}