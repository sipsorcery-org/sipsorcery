using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Sent by the client to clear all audio bytes currently stored in the input audio buffer.
/// 
/// This is typically used to discard uncommitted audio. The server will respond with an
/// `input_audio_buffer.cleared` event.
/// </summary>
public class RealtimeClientEventInputAudioBufferClear : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "input_audio_buffer.clear".
    /// </summary>
    public const string TypeName = "input_audio_buffer.clear";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;
}