//-----------------------------------------------------------------------------
// Filename: TextFormat.cs
//
// Description: Representation of a text media format.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 May 2025  Aaron Clauson   Refactored from MediaEndPoints.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorceryMedia.Abstractions;

public struct TextFormat
{
    public const int DYNAMIC_ID_MIN = 96;
    public const int DYNAMIC_ID_MAX = 127;
    public const int DEFAULT_CLOCK_RATE = 1000;

    public static readonly TextFormat Empty = new TextFormat()
    { ClockRate = DEFAULT_CLOCK_RATE };

    public TextFormat(TextFormat format)
    : this(format.FormatID, format.FormatName, format.ClockRate, format.Parameters)
    { }

    public TextFormat(TextCodecsEnum codec, int formatID, int clockRate = DEFAULT_CLOCK_RATE, string parameters = null)
    : this(formatID, codec.ToString(), clockRate, parameters)
    { }

    /// <summary>
    /// Creates a new text format based on a dynamic codec (or an unsupported well known codec).
    /// </summary>
    public TextFormat(int formatID, string formatName, int clockRate = DEFAULT_CLOCK_RATE, string parameters = null)
    {
        if (formatID < 0)
        {
            // Note format ID's less than the dynamic start range are allowed as the codec list
            // does not currently support all well known codecs.
            throw new ApplicationException("The format ID for an TextFormat must be greater than 0.");
        }
        else if (formatID > DYNAMIC_ID_MAX)
        {
            throw new ApplicationException($"The format ID for an TextFormat exceeded the maximum allowed vale of {DYNAMIC_ID_MAX}.");
        }
        else if (string.IsNullOrWhiteSpace(formatName))
        {
            throw new ApplicationException($"The format name must be provided for a TextFormat.");
        }
        else if (clockRate <= 0)
        {
            throw new ApplicationException($"The clock rate for a TextFormat must be greater than 0.");
        }

        FormatID = formatID;
        FormatName = formatName;
        ClockRate = clockRate;
        Parameters = parameters;

        if (Enum.TryParse<TextCodecsEnum>(FormatName, out var textCodec))
        {
            Codec = textCodec;
        }
        else
        {
            Codec = TextCodecsEnum.Unknown;
        }
    }

    public TextCodecsEnum Codec { get; set; }

    /// <summary>
    /// The format ID for the codec. If this is a well known codec it should be set to the
    /// value from the codec enum. If the codec is a dynamic it must be set between 96–127
    /// inclusive.
    /// </summary>
    public int FormatID { get; set; }

    /// <summary>
    /// The official name for the codec. This field is critical for dynamic codecs
    /// where it is used to match the codecs in the SDP offer/answer.
    /// </summary>
    public string FormatName { get; set; }

    /// <summary>
    /// The rate used by decoded samples for this text format.
    /// </summary>
    /// <remarks>
    /// Example, 1000 is the clock rate:
    /// a=rtpmap:98 t140/1000
    /// </remarks>
    public int ClockRate { get; set; }

    /// <summary>
    /// This is the "a=fmtp" format parameter that will be set in the SDP offer/answer.
    /// This field should be set WITHOUT the "a=fmtp:0" prefix.
    /// </summary>
    /// <remarks>
    /// Example:
    /// a=fmtp:100 98/98/98
    /// </remarks>
    public string Parameters { get; set; }
}
