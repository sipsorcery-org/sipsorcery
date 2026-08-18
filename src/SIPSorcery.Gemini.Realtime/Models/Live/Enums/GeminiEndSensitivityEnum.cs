namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// How eager the automatic voice activity detector is to mark a user turn as finished.
/// Used in <see cref="GeminiAutomaticActivityDetection.EndOfSpeechSensitivity"/>.
/// </summary>
public enum GeminiEndSensitivityEnum
{
    END_SENSITIVITY_UNSPECIFIED,
    END_SENSITIVITY_HIGH,
    END_SENSITIVITY_LOW
}
