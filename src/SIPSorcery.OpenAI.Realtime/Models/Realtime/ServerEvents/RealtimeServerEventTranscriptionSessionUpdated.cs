using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when a transcription session is updated with a
/// `transcription_session.update` event, unless there is an error.
/// </summary>
public class RealtimeServerEventTranscriptionSessionUpdated : RealtimeEventBase
{
    public const string TypeName = "transcription_session.updated";

    /// <summary>
    /// The event type, must be "transcription_session.updated".
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The updated transcription session object.
    /// </summary>
    [JsonPropertyName("session")]
    public required RealtimeTranscriptionSessionCreateResponse Session { get; set; }
}
