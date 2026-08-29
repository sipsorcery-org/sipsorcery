namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Values for <see cref="GeminiGenerationConfig.ResponseModalities"/>. Gemini Live currently
/// only supports a single response modality per session (either audio OR text, not both).
/// </summary>
public enum GeminiResponseModalityEnum
{
    AUDIO,
    TEXT
}
