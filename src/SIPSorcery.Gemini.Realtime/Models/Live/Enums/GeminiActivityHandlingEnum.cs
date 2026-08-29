namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Controls whether model-generated audio is interrupted when the user starts talking
/// (barge-in). Used in <see cref="GeminiRealtimeInputConfig.ActivityHandling"/>.
/// </summary>
public enum GeminiActivityHandlingEnum
{
    START_OF_ACTIVITY_INTERRUPTS,
    NO_INTERRUPTION
}
