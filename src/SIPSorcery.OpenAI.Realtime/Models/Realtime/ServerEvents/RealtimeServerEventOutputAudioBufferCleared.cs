using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// <b>WebRTC Only:</b> Emitted when the output audio buffer is cleared.
/// 
/// This occurs either automatically in VAD mode (when user speech interrupts playback),
/// or manually when the client sends an `output_audio_buffer.clear` event.
/// </summary>
public class RealtimeServerEventOutputAudioBufferCleared : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "output_audio_buffer.cleared".
    /// </summary>
    public const string TypeName = "output_audio_buffer.cleared";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the response whose audio output was cleared.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseID { get; set; }
}