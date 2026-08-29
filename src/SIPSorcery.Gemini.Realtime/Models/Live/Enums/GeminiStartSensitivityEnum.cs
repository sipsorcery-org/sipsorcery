namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// How eager the automatic voice activity detector is to mark a user turn as started.
/// Used in <see cref="GeminiAutomaticActivityDetection.StartOfSpeechSensitivity"/>.
/// </summary>
public enum GeminiStartSensitivityEnum
{
    START_SENSITIVITY_UNSPECIFIED,
    START_SENSITIVITY_HIGH,
    START_SENSITIVITY_LOW
}
