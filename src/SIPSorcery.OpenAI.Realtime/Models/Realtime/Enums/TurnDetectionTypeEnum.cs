namespace SIPSorcery.OpenAI.Realtime.Models;

public enum TurnDetectionTypeEnum
{
    /// <summary>
    /// Server-based voice activity detection.
    /// </summary>
    server_vad,

    /// <summary>
    /// Semantic VAD using contextual cues to determine end of speech.
    /// </summary>
    semantic_vad
}
