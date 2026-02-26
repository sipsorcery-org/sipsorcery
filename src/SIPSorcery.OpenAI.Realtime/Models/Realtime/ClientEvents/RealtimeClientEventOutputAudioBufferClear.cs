using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// <b>WebRTC Only:</b> Sent by the client to immediately cut off the current audio response.
/// 
/// This instructs the server to stop generating audio and emit an `output_audio_buffer.cleared` event.
/// 
/// This event should follow a `response.cancel` event to ensure the server halts response generation.
/// </summary>
public class RealtimeClientEventOutputAudioBufferClear : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "output_audio_buffer.clear".
    /// </summary>
    public const string TypeName = "output_audio_buffer.clear";

    /// <summary>
    /// Overrides the base type property with the constant event type.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;
}
