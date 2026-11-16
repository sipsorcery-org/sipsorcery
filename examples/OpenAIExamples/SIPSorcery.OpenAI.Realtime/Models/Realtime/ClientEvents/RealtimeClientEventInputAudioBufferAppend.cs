using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Sent by the client to append base64-encoded audio bytes to the input audio buffer.
/// 
/// The buffer is temporary and may later be committed either automatically (e.g., in Server VAD mode)
/// or manually (when VAD is disabled). Up to 15 MiB of audio may be sent per event.
/// 
/// The server does not send a confirmation response to this event.
/// </summary>
public class RealtimeClientEventInputAudioBufferAppend : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "input_audio_buffer.append".
    /// </summary>
    public const string TypeName = "input_audio_buffer.append";

    /// <summary>
    /// Overrides the base type property with the constant event name.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// Base64-encoded audio bytes. Must match the session's input_audio_format.
    /// </summary>
    [JsonPropertyName("audio")]
    public required string Audio { get; set; }
}
