using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server when the input audio buffer is cleared in response to
/// a client-initiated `input_audio_buffer.clear` event.
/// </summary>
public class RealtimeServerEventInputAudioBufferCleared : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "input_audio_buffer.cleared".
    /// </summary>
    public const string TypeName = "input_audio_buffer.cleared";

    /// <summary>
    /// Overrides the base type property with the constant event type.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;
}
