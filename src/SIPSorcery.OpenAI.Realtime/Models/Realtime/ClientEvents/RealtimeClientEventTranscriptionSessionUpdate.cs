using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Send this event to update a transcription session's configuration.
/// 
/// This can be used to adjust the input audio format, transcription model,
/// noise reduction settings, turn detection behavior, and which fields
/// to include in the output. Only the provided fields will be updated.
/// </summary>
public class RealtimeClientEventTranscriptionSessionUpdate : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "transcription_session.update".
    /// </summary>
    public const string TypeName = "transcription_session.update";

    /// <summary>
    /// Overrides the base type property with the constant event type.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The updated transcription session configuration.
    /// Only the fields included in this object will be modified.
    /// </summary>
    [JsonPropertyName("session")]
    public required RealtimeTranscriptionSessionCreateRequest Session { get; set; }
}
