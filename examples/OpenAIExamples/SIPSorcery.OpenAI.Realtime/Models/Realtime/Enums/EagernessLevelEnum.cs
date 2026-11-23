namespace SIPSorcery.OpenAI.Realtime.Models;

public enum EagernessLevelEnum
{
    /// <summary>
    /// Low eagerness: waits longer for user input.
    /// </summary>
    low,

    /// <summary>
    /// Medium eagerness.
    /// </summary>
    medium,

    /// <summary>
    /// High eagerness: responds quickly.
    /// </summary>
    high,

    /// <summary>
    /// Auto eagerness (default), equivalent to medium.
    /// </summary>
    auto
}
