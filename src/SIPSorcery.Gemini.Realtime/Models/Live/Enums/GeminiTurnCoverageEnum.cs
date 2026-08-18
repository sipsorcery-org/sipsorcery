namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Controls what realtime input is included in a turn. Used in
/// <see cref="GeminiRealtimeInputConfig.TurnCoverage"/>.
/// </summary>
public enum GeminiTurnCoverageEnum
{
    TURN_INCLUDES_ONLY_ACTIVITY,
    TURN_INCLUDES_ALL_INPUT
}
