//-----------------------------------------------------------------------------
// Filename: AudioFormat.cs
//
// Description: Representation of an audio media format.
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

public struct AudioFormat
{
    public const int DYNAMIC_ID_MIN = 96;
    public const int DYNAMIC_ID_MAX = 127;
    public const int DEFAULT_CLOCK_RATE = 8000;
    public const int DEFAULT_CHANNEL_COUNT = 1;

    public static readonly AudioFormat Empty = new AudioFormat()
    { _isNonEmpty = false, ClockRate = DEFAULT_CLOCK_RATE, ChannelCount = DEFAULT_CHANNEL_COUNT };

    public AudioCodecsEnum Codec { get; set; }

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
    /// The rate used to set RTP timestamps and to be set in the SDP format
    /// attribute for this format. It should almost always be the same as the
    /// <seealso cref="ClockRate"/>. An example of where it's not is G722 which
    /// uses a sample rate of 16KHz but an RTP rate of 8KHz for historical reasons.
    /// </summary>
    /// <example>
    /// In the SDP format attribute below the RTP clock rate is 48000.
    /// a=rtpmap:109 opus/48000/2
    /// </example>
    public int RtpClockRate { get; set; }

    /// <summary>
    /// The rate used by decoded samples for this audio format.
    /// </summary>
    public int ClockRate { get; set; }

    /// <summary>
    /// The number of channels for the audio format.
    /// </summary>
    /// <example>
    /// In the SDP format attribute below the channel count is 2.
    /// Note for single channel codecs the parameter is typically omitted from the
    /// SDP format attribute.
    /// a=rtpmap:109 opus/48000/2
    /// </example>
    public int ChannelCount { get; set; }

    /// <summary>
    /// This is the string that goes in the SDP "a=fmtp" parameter.
    /// This field should be set WITHOUT the "a=fmtp:" prefix.
    /// </summary>
    /// <example>
    /// In the case below this filed should be set as "emphasis=50-15".
    /// a=fmtp:97 emphasis=50-15
    /// </example>
    public string Parameters { get; set; }

    private bool _isNonEmpty;

    /// <summary>
    /// Creates a new audio format based on a well known SDP format.
    /// </summary>
    public AudioFormat(SDPWellKnownMediaFormatsEnum wellKnown) :
        this(AudioVideoWellKnown.WellKnownAudioFormats[wellKnown])
    { }

    /// <summary>
    /// Creates a new audio format based on a well known codec.
    /// </summary>
    public AudioFormat(
        AudioCodecsEnum codec,
        int formatID,
        int clockRate = DEFAULT_CLOCK_RATE,
        int channelCount = DEFAULT_CHANNEL_COUNT,
        string parameters = null) :
        this(codec, formatID, clockRate, clockRate, channelCount, parameters)
    { }

    /// <summary>
    /// Creates a new audio format based on a well known codec.
    /// </summary>
    public AudioFormat(
        AudioCodecsEnum codec,
        int formatID,
        int clockRate,
        int rtpClockRate,
        int channelCount,
        string parameters)
         : this(formatID, codec.ToString(), clockRate, rtpClockRate, channelCount, parameters)
    { }

    /// <summary>
    /// Creates a new audio format based on a dynamic codec (or an unsupported well known codec).
    /// </summary>
    public AudioFormat(
        int formatID,
        string formatName,
        int clockRate = DEFAULT_CLOCK_RATE,
        int channelCount = DEFAULT_CHANNEL_COUNT,
        string parameters = null) :
        this(formatID, formatName, clockRate, clockRate, channelCount, parameters)
    { }

    public AudioFormat(AudioFormat format)
        : this(format.FormatID, format.FormatName, format.ClockRate, format.RtpClockRate, format.ChannelCount, format.Parameters)
    { }

    /// <summary>
    /// Creates a new audio format based on a dynamic codec (or an unsupported well known codec).
    /// </summary>
    public AudioFormat(int formatID, string formatName, int clockRate, int rtpClockRate, int channelCount, string parameters)
    {
        if (formatID < 0)
        {
            // Note format ID's less than the dynamic start range are allowed as the codec list
            // does not currently support all well known codecs.
            throw new ApplicationException("The format ID for an AudioFormat must be greater than 0.");
        }
        else if (formatID > DYNAMIC_ID_MAX)
        {
            throw new ApplicationException($"The format ID for an AudioFormat exceeded the maximum allowed vale of {DYNAMIC_ID_MAX}.");
        }
        else if (string.IsNullOrWhiteSpace(formatName))
        {
            throw new ApplicationException($"The format name must be provided for an AudioFormat.");
        }
        else if (clockRate <= 0)
        {
            throw new ApplicationException($"The clock rate for an AudioFormat must be greater than 0.");
        }
        else if (rtpClockRate <= 0)
        {
            throw new ApplicationException($"The RTP clock rate for an AudioFormat must be greater than 0.");
        }
        else if (channelCount <= 0)
        {
            throw new ApplicationException($"The channel count for an AudioFormat must be greater than 0.");
        }

        FormatID = formatID;
        FormatName = formatName;
        ClockRate = clockRate;
        RtpClockRate = rtpClockRate;
        ChannelCount = channelCount;
        Parameters = parameters;
        _isNonEmpty = true;

        if (Enum.TryParse<AudioCodecsEnum>(FormatName.ToUpper(), out var audioCodec))
        {
            Codec = audioCodec;
        }
        else
        {
            Codec = AudioCodecsEnum.Unknown;
        }
    }

    public bool IsEmpty() => !_isNonEmpty;
}
