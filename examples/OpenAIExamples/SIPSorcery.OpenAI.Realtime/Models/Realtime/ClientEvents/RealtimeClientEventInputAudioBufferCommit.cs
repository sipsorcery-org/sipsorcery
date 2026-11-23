using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Sent by the client to commit the input audio buffer. This finalizes the user's audio input,
/// creates a new user message item in the conversation, and triggers transcription (if enabled).
///
/// The server will respond with an `input_audio_buffer.committed` event. This is not needed
/// when Server VAD is enabled, as the server will commit automatically. An error is returned
/// if the buffer is empty.
/// </summary>
public class RealtimeClientEventInputAudioBufferCommit : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "input_audio_buffer.commit".
    /// </summary>
    public const string TypeName = "input_audio_buffer.commit";

    /// <summary>
    /// Overrides the base type property with the constant event type name.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;
}

