//-----------------------------------------------------------------------------
// Filename: SDPMediaFormat.cs
//
// Description: 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// ??	        Aaron Clauson	Created, Hobart, Australia.
// 18 Oct 2020  Aaron Clauson   Renamed from SDPMediaFormat to SDPAudioVideoMediaFormat.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net;

/// <summary>
/// Represents a single media format within a media announcement. Often the whole media format can
/// be represented and described by a single character, e.g. "0" without additional info represents
/// standard "PCMU", "8" represents "PCMA" etc. For other media types that have variable parameters
/// additional attributes can be provided.
/// </summary>
/// <remarks>This struct is designed to be immutable. If new information becomes available for a 
/// media format, such as when parsing further into an SDP payload, a new media format should be
/// created.
/// TODO: With C#9 the struct could become a "record" type.
/// </remarks>
public struct SDPAudioVideoMediaFormat
{
    public const int DYNAMIC_ID_MIN = 96;
    public const int DYNAMIC_ID_MAX = 127;
    public const int DEFAULT_AUDIO_CHANNEL_COUNT = 1;

    public static SDPAudioVideoMediaFormat Empty;

    /// <summary>
    /// Indicates whether the format is for audio or video.
    /// </summary>
    public SDPMediaTypesEnum Kind { get; }

    /// <summary>
    /// The mandatory ID for the media format. Warning, even though some ID's are normally used to represent
    /// a standard media type, e.g "0" for "PCMU" etc, there is no guarantee that's the case. "0" can be used
    /// for any media format if there is a format attribute describing it. In the absence of a format attribute
    /// then it is required that it represents a standard media type.
    /// 
    /// Note (rj2): FormatID MUST be string (not int), in case ID is 't38' and type is 'image'
    /// Note to above: The FormatID is always numeric for profile "RTP/AVP" and "RTP/SAVP", see 
    /// https://tools.ietf.org/html/rfc4566#section-5.14 and section on "fmt":
    /// "If the [proto] sub-field is "RTP/AVP" or "RTP/SAVP" the [fmt]
    /// sub-fields contain RTP payload type numbers"
    /// In the case of T38 the format name is "t38" but the formatID must be set as a dynamic ID.
    /// <code>
    /// // Example
    /// // Note in this example "0" is representing a standard format so the format attribute is optional.
    /// m=audio 12228 RTP/AVP 0 101         // "0" and "101" are media format ID's.
    /// a=rtpmap:0 PCMU/8000                // "0" is the media format ID.
    /// a=rtpmap:101 telephone-event/8000   // "101" is the media format ID.
    /// a=fmtp:101 0-16
    /// </code>
    /// <code> 
    /// // t38 example from https://tools.ietf.org/html/rfc4612.
    /// m=audio 6800 RTP/AVP 0 98 
    /// a=rtpmap:98 t38/8000 
    /// a=fmtp:98 T38FaxVersion=2;T38FaxRateManagement=transferredTCF
    /// </code>
    /// </summary>
    public int ID { get; }

    /// <summary>
    /// The optional rtpmap attribute properties for the media format. For standard media types this is not necessary.
    /// <code>
    /// // Example
    /// a=rtpmap:0 PCMU/8000
    /// a=rtpmap:101 telephone-event/8000 ← "101 telephone-event/8000" is the rtpmap properties.
    /// a=fmtp:101 0-16
    /// </code>
    /// </summary>
    public string? Rtpmap { get; }

    /// <summary>
    /// The optional format parameter attribute for the media format. For standard media types this is not necessary.
    /// <code>
    /// // Example
    /// a=rtpmap:0 PCMU/8000
    /// a=rtpmap:101 telephone-event/8000 
    /// a=fmtp:101 0-16                     ← "101 0-16" is the fmtp attribute.
    /// </code>
    /// </summary>
    public string? Fmtp { get; }

    public IEnumerable<string> SupportedRtcpFeedbackMessages
    {
        get
        {
            yield return "transport-cc";
            //yield return "goog-remb";
        }
    }

    /// <summary>
    /// The standard name of the media format.
    /// <code>
    /// // Example
    /// a=rtpmap:0 PCMU/8000                ← "PCMU" is the media format name.
    /// a=rtpmap:101 telephone-event/8000 
    /// a=fmtp:101 0-16
    /// </code>
    /// </summary>
    //public string Name { get; set; }

    private bool _isNotEmpty;

    /// <summary>
    /// Creates a new SDP media format for a well known media type. Well known type are those that use 
    /// ID's less than 96 and don't require rtpmap or fmtp attributes.
    /// </summary>
    public SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum knownFormat)
    {
        Kind = AudioVideoWellKnown.WellKnownAudioFormats.ContainsKey(knownFormat) ? SDPMediaTypesEnum.audio :
            SDPMediaTypesEnum.video;

        ID = (int)knownFormat;
        Rtpmap = null;
        Fmtp = null;
        _isNotEmpty = true;

        if (Kind == SDPMediaTypesEnum.audio)
        {
            var audioFormat = AudioVideoWellKnown.WellKnownAudioFormats[knownFormat];
            Rtpmap = SetRtpmap(audioFormat.FormatName, audioFormat.RtpClockRate, audioFormat.ChannelCount);
        }
        else
        {
            var videoFormat = AudioVideoWellKnown.WellKnownVideoFormats[knownFormat];
            Rtpmap = SetRtpmap(videoFormat.FormatName, videoFormat.ClockRate, 0);
        }
    }

    public bool IsH264 => RtmapIs("H264");

    public bool IsMJPEG => RtmapIs("JPEG");

    public bool isH265 => RtmapIs("H265");

    private bool RtmapIs(string codec)
    {
        if (Rtpmap is null)
        {
            return false;
        }

        return Rtpmap.AsSpan().TrimStart().StartsWith(codec.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    public bool CheckCompatible()
    {
        if (IsH264 || IsMJPEG || isH265)
        {
            Debug.Assert(Fmtp is { });
            var parameters = ParseWebRtcParameters(Fmtp);
            if (parameters.TryGetValue("packetization-mode", out var packetizationMode))
            {
                if (packetizationMode != "1")
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static Dictionary<string, string> ParseWebRtcParameters(ReadOnlySpan<char> input)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (input.IsEmpty)
        {
            return parameters;
        }

        foreach (var pairRange in input.Split(';'))
        {
            var pairSpan = input[pairRange];
            var eq = pairSpan.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = pairSpan.Slice(0, eq).Trim();
            var value = pairSpan.Slice(eq + 1).Trim();
            if (!key.IsEmpty && !value.IsEmpty)
            {
                parameters[key.ToLowerString()] = value.ToString();
            }
        }

        return parameters;
    }

    /// <summary>
    /// Creates a new SDP media format for a dynamic media type. Dynamic media types are those that use 
    /// ID's between 96 and 127 inclusive and require an rtpmap attribute and optionally an fmtp attribute.
    /// </summary>
    public SDPAudioVideoMediaFormat(SDPMediaTypesEnum kind, int id, string rtpmap, string? fmtp = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, DYNAMIC_ID_MAX);
        ArgumentException.ThrowIfNullOrWhiteSpace(rtpmap);

        Kind = kind;
        ID = id;
        Rtpmap = rtpmap;
        Fmtp = fmtp;
        _isNotEmpty = true;
    }

    /// <summary>
    /// Creates a new SDP media format for a dynamic media type. Dynamic media types are those that use 
    /// ID's between 96 and 127 inclusive and require an rtpmap attribute and optionally an fmtp attribute.
    /// </summary>
    public SDPAudioVideoMediaFormat(SDPMediaTypesEnum kind, int id, string name, int clockRate, int channels = DEFAULT_AUDIO_CHANNEL_COUNT, string? fmtp = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, DYNAMIC_ID_MAX);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Kind = kind;
        ID = id;
        Rtpmap = null;
        Fmtp = fmtp;
        _isNotEmpty = true;

        Rtpmap = SetRtpmap(name, clockRate, channels);
    }

    /// <summary>
    /// Creates a new SDP media format from a Audio Format instance. The Audio Format contains the 
    /// equivalent information to the SDP format object but has well defined audio properties separate
    /// from the SDP serialisation.
    /// </summary>
    /// <param name="audioFormat">The Audio Format to map to an SDP format.</param>
    public SDPAudioVideoMediaFormat(AudioFormat audioFormat)
    {
        Kind = SDPMediaTypesEnum.audio;
        ID = audioFormat.FormatID;
        Rtpmap = null;
        Fmtp = audioFormat.Parameters;
        _isNotEmpty = true;

        Rtpmap = SetRtpmap(audioFormat.FormatName, audioFormat.RtpClockRate, audioFormat.ChannelCount);
    }

    /// <summary>
    /// Creates a new SDP media format from a Video Format instance. The Video Format contains the 
    /// equivalent information to the SDP format object but has well defined video properties separate
    /// from the SDP serialisation.
    /// </summary>
    /// <param name="videoFormat">The Video Format to map to an SDP format.</param>
    public SDPAudioVideoMediaFormat(VideoFormat videoFormat)
    {
        Kind = SDPMediaTypesEnum.video;
        ID = videoFormat.FormatID;
        Rtpmap = null;
        Fmtp = videoFormat.Parameters;
        _isNotEmpty = true;

        Rtpmap = SetRtpmap(videoFormat.FormatName, videoFormat.ClockRate);
    }

    public SDPAudioVideoMediaFormat(TextFormat textFormat)
    {
        Kind = SDPMediaTypesEnum.text;
        ID = textFormat.FormatID;
        Rtpmap = null;
        Fmtp = textFormat.Parameters;
        _isNotEmpty = true;

        Rtpmap = SetRtpmap(textFormat.FormatName, textFormat.ClockRate);
    }

    private string SetRtpmap(string name, int clockRate, int channels = DEFAULT_AUDIO_CHANNEL_COUNT) => Kind is SDPMediaTypesEnum.video or SDPMediaTypesEnum.text
            ? $"{name}/{clockRate}"
            : (channels == DEFAULT_AUDIO_CHANNEL_COUNT) ? $"{name}/{clockRate}" : $"{name}/{clockRate}/{channels}";

    public bool IsEmpty() => !_isNotEmpty;
    public int ClockRate()
    {
        if (Kind == SDPMediaTypesEnum.video)
        {
            return ToVideoFormat().ClockRate;
        }
        else if (Kind == SDPMediaTypesEnum.text)
        {
            return ToTextFormat().ClockRate;
        }
        else
        {
            return ToAudioFormat().ClockRate;
        }
    }

    public int Channels()
        => Kind is SDPMediaTypesEnum.video or SDPMediaTypesEnum.text
            ? 0
            : TryParseRtpmap(Rtpmap.AsSpan(), out _, out _, out var channels) ? channels : DEFAULT_AUDIO_CHANNEL_COUNT;

    public string? Name()
    {
        // Rtpmap taks priority over well known media type as ID's can be changed.
        if (Rtpmap is { } && TryParseRtpmap(Rtpmap.AsSpan(), out var name, out _, out _))
        {
            return name;
        }
        else if (SDPWellKnownMediaFormatsEnum.IsDefined((SDPWellKnownMediaFormatsEnum)ID))
        {
            // If no rtpmap available then it must be a well known format.
            return ((SDPWellKnownMediaFormatsEnum)ID).ToStringFast();
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a new media format based on an existing format but with a different ID.
    /// The typical case for this is during the SDP offer/answer exchange the dynamic format ID's for the
    /// equivalent type need to be adjusted by one party.
    /// </summary>
    /// <param name="id">The ID to set on the new format.</param>
    /// <returns>A new format.</returns>
    public SDPAudioVideoMediaFormat WithUpdatedID(int id)
    {
        Debug.Assert(!string.IsNullOrEmpty(Rtpmap));
        return new SDPAudioVideoMediaFormat(Kind, id, Rtpmap, Fmtp);
    }

    public SDPAudioVideoMediaFormat WithUpdatedRtpmap(string rtpmap)
    {
        Debug.Assert(!string.IsNullOrEmpty(Rtpmap));
        return new SDPAudioVideoMediaFormat(Kind, ID, rtpmap, Fmtp);
    }

    public SDPAudioVideoMediaFormat WithUpdatedFmtp(string fmtp)
    {
        Debug.Assert(!string.IsNullOrEmpty(Rtpmap));
        return new SDPAudioVideoMediaFormat(Kind, ID, Rtpmap, fmtp);
    }

    /// <summary>
    /// Maps an audio SDP media type to a media abstraction layer audio format.
    /// </summary>
    /// <returns>An audio format value.</returns>
    public AudioFormat ToAudioFormat()
    {
        // Rtpmap takes priority over well known media type as ID's can be changed.
        if (Rtpmap is { } && TryParseRtpmap(Rtpmap.AsSpan(), out var name, out var rtpClockRate, out var channels))
        {
            var clockRate = rtpClockRate;

            // G722 is a special case. It's the only audio format that uses the wrong RTP clock rate.
            // It sets 8000 in the SDP but then expects samples to be sent as 16KHz.
            // See https://tools.ietf.org/html/rfc3551#section-4.5.2.
            if (string.Equals(name, "G722", StringComparison.OrdinalIgnoreCase) && rtpClockRate == 8000)
            {
                clockRate = 16000;
            }

            name = name?.ToUpperInvariant();
            Debug.Assert(!string.IsNullOrWhiteSpace(name));
            return new AudioFormat(ID, name, clockRate, rtpClockRate, channels, Fmtp);
        }
        else if (ID < DYNAMIC_ID_MIN
            && SDPWellKnownMediaFormatsEnumExtensions.TryParse(Name(), out var wellKnownFormat)
            && AudioVideoWellKnown.WellKnownAudioFormats.TryGetValue(wellKnownFormat, out var value))
        {
            return value;
        }
        else
        {
            return AudioFormat.Empty;
        }
    }

    /// <summary>
    /// Maps a video SDP media type to a media abstraction layer video format.
    /// </summary>
    /// <returns>A video format value.</returns>
    public VideoFormat ToVideoFormat()
    {
        // Rtpmap taks priority over well known media type as ID's can be changed.
        // But we don't currently support any of the well known video types any way.
        if (TryParseRtpmap(Rtpmap.AsSpan(), out var name, out var clockRate, out _))
        {
            name = name?.ToUpperInvariant();
            Debug.Assert(!string.IsNullOrWhiteSpace(name));
            return new VideoFormat(ID, name, clockRate, Fmtp);
        }
        else
        {
            return VideoFormat.Empty;
        }
    }

    /// <summary>
    /// Maps a video SDP media type to a media abstraction layer text format.
    /// </summary>
    /// <returns>A text format value.</returns>
    public TextFormat ToTextFormat()
    {
        // Rtpmap taks priority over well known media type as ID's can be changed.
        // But we don't currently support any of the well known text types any way.
        if (TryParseRtpmap(Rtpmap.AsSpan(), out var name, out var clockRate, out _))
        {
            name = name?.ToUpperInvariant();
            Debug.Assert(!string.IsNullOrWhiteSpace(name));
            return new TextFormat(ID, name, clockRate, Fmtp);
        }
        else
        {
            return TextFormat.Empty;
        }
    }

    /// <summary>
    /// For two formats to be a match only the codec and rtpmap parameters need to match. The
    /// fmtp parameter does not matter.
    /// </summary>
    public static bool AreMatch(SDPAudioVideoMediaFormat format1, SDPAudioVideoMediaFormat format2)
    {
        // rtpmap takes priority as well known format ID's can be overruled.
        if (format1.Rtpmap is { } && format2.Rtpmap is { })
        {
            if (string.Equals(format1.Rtpmap.Trim(), format2.Rtpmap.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        if (format1.ID < DYNAMIC_ID_MIN
            && format1.ID == format2.ID
            && string.Equals(format1.Name(), format2.Name(), StringComparison.OrdinalIgnoreCase))
        {
            // Well known format type.
            return true;
        }
        return false;

    }

    /// <summary>
    /// Attempts to get the compatible formats between two lists. Formats for
    /// "RTP Events" are not included.
    /// </summary>
    /// <param name="a">The first list to match the media formats for.</param>
    /// <param name="b">The second list to match the media formats for.</param>
    /// <returns>A list of media formats that are compatible for BOTH lists.</returns>
    public static List<SDPAudioVideoMediaFormat> GetCompatibleFormats(List<SDPAudioVideoMediaFormat>? a, List<SDPAudioVideoMediaFormat>? b)
    {
        var compatible = new List<SDPAudioVideoMediaFormat>();

        if (a is null || a.Count == 0)
        {
            // Preferable to return an empty list.
            //throw new ArgumentNullException("a", "The first media format list supplied was empty.");
        }
        else if (b is null || b.Count == 0)
        {
            // Preferable to return an empty list.
            //throw new ArgumentNullException("b", "The second media format list supplied was empty.");
        }
        else
        {
            foreach (var format in a)
            {
                var hasMatch = false;
                foreach (var otherFormat in b)
                {
                    if (AreMatch(format, otherFormat))
                    {
                        hasMatch = true;
                        break;
                    }
                }

                if (hasMatch && format.CheckCompatible())
                {
                    compatible.Add(format);
                }
            }
        }

        return compatible;
    }

    /// <summary>
    /// Attempts to get the first compatible format between two lists without using LINQ or full iteration.
    /// This method is optimized for performance by returning immediately when the first compatible format is found.
    /// </summary>
    /// <param name="a">The first list to match the media formats for.</param>
    /// <param name="b">The second list to match the media formats for.</param>
    /// <returns>The first compatible media format found, or Empty if no compatible formats exist.</returns>
    public static SDPAudioVideoMediaFormat GetFirstCompatibleFormat(List<SDPAudioVideoMediaFormat>? a, List<SDPAudioVideoMediaFormat>? b)
    {
        // Early exit for null or empty lists
        if (a is null or { Count: 0 } || b is null or { Count: 0 })
        {
            return Empty;
        }

        // Iterate through the first list to find the first compatible format
        foreach (var format in a)
        {
            // Check if this format has a match in the second list
            foreach (var otherFormat in b)
            {
                if (AreMatch(format, otherFormat))
                {
                    // Check compatibility before returning
                    if (format.CheckCompatible())
                    {
                        return format;
                    }
                    // If not compatible, break out of inner loop to try next format in 'a'
                    break;
                }
            }
        }

        return Empty;
    }

    /// <summary>
    /// Attempts to get the first compatible format between two lists while excluding a specific RTP event payload ID.
    /// This method is optimized for performance by returning immediately when the first compatible format is found.
    /// </summary>
    /// <param name="a">The first list to match the media formats for.</param>
    /// <param name="b">The second list to match the media formats for.</param>
    /// <param name="excludePayloadID">The payload ID to exclude from the search (typically RTP event payload ID).</param>
    /// <returns>The first compatible media format found that doesn't match the excluded payload ID, or Empty if no compatible formats exist.</returns>
    public static SDPAudioVideoMediaFormat GetFirstCompatibleFormatExcluding(List<SDPAudioVideoMediaFormat>? a, List<SDPAudioVideoMediaFormat>? b, int excludePayloadID)
    {
        // Early exit for null or empty lists
        if (a is null or { Count: 0 } || b is null or { Count: 0 })
        {
            return Empty;
        }

        // Iterate through the first list to find the first compatible format
        foreach (var format in a)
        {
            // Skip formats that match the excluded payload ID
            if (format.ID == excludePayloadID)
            {
                continue;
            }

            // Check if this format has a match in the second list
            foreach (var otherFormat in b)
            {
                if (AreMatch(format, otherFormat))
                {
                    // Check compatibility before returning
                    if (format.CheckCompatible())
                    {
                        return format;
                    }
                    // If not compatible, break out of inner loop to try next format in 'a'
                    break;
                }
            }
        }

        return Empty;
    }

    /// <summary>
    /// Sort capabilities array based on another capability array
    /// </summary>
    /// <param name="capabilities"></param>
    /// <param name="priorityOrder"></param>
    public static void SortMediaCapability(List<SDPAudioVideoMediaFormat>? capabilities, List<SDPAudioVideoMediaFormat>? priorityOrder)
    {
        //Fix Capabilities Order
        if (priorityOrder is { } && capabilities is { })
        {
            capabilities.Sort((a, b) =>
            {
                //Sort By Indexes
                var aSort = priorityOrder.FindIndex(c => c.ID == a.ID);
                var bSort = priorityOrder.FindIndex(c => c.ID == b.ID);

                //Sort Values
                if (aSort < 0)
                {
                    aSort = int.MaxValue;
                }
                if (bSort < 0)
                {
                    bSort = int.MaxValue;
                }

                return aSort.CompareTo(bSort);
            });
        }
    }

    /// <summary>
    /// Parses an rtpmap attribute in the form "name/clock" or "name/clock/channels".
    /// </summary>
    public static bool TryParseRtpmap(ReadOnlySpan<char> rtpmap, out string? name, out int clockRate, out int channels)
    {
        name = null;
        clockRate = 0;
        channels = DEFAULT_AUDIO_CHANNEL_COUNT;

        rtpmap = rtpmap.Trim();
        if (rtpmap.IsEmpty)
        {
            return false;
        }

        var firstSlash = rtpmap.IndexOf('/');
        if (firstSlash < 0)
        {
            return false;
        }

        var secondSlash = rtpmap.Slice(firstSlash + 1).IndexOf('/');
        var nameSpan = rtpmap.Slice(0, firstSlash).Trim();
        ReadOnlySpan<char> clockRateSpan;
        ReadOnlySpan<char> channelsSpan = default;

        if (secondSlash >= 0)
        {
            clockRateSpan = rtpmap.Slice(firstSlash + 1, secondSlash).Trim();
            channelsSpan = rtpmap.Slice(firstSlash + 1 + secondSlash + 1).Trim();
        }
        else
        {
            clockRateSpan = rtpmap.Slice(firstSlash + 1).Trim();
        }

        if (!int.TryParse(clockRateSpan, out clockRate))
        {
            return false;
        }

        if (!channelsSpan.IsEmpty && !int.TryParse(channelsSpan, out channels))
        {
            return false;
        }

        name = nameSpan.ToString();

        return true;
    }

    /// <summary>
    /// Attempts to get a common SDP media format that supports telephone events. 
    /// If compatible an RTP event format will be returned that matches the local format with the remote format.
    /// </summary>
    /// <param name="a">The first of supported media formats.</param>
    /// <param name="b">The second of supported media formats.</param>
    /// <returns>An SDP media format with a compatible RTP event format.</returns>
    public static SDPAudioVideoMediaFormat GetCommonRtpEventFormat(IEnumerable<SDPAudioVideoMediaFormat> a, IEnumerable<SDPAudioVideoMediaFormat> b)
    {
        // Check if RTP events are supported and if required adjust the local format ID.
        var aEventFormat = GetFormatForName(a, SDP.TELEPHONE_EVENT_ATTRIBUTE);
        var bEventFormat = GetFormatForName(b, SDP.TELEPHONE_EVENT_ATTRIBUTE);

        if (!aEventFormat.IsEmpty() && !bEventFormat.IsEmpty())
        {
            // Both support RTP events. If using different format ID's choose the first one.
            return aEventFormat;
        }
        else
        {
            return Empty;
        }
    }

    /// <summary>
    /// Attempts to get a matching entry in a list of media formats for a specific format name.
    /// </summary>
    /// <param name="formats">The list of formats to search.</param>
    /// <param name="formatName">The format name to search for.</param>
    /// <returns>If found the matching format or the empty format if not.</returns>
    public static SDPAudioVideoMediaFormat GetFormatForName(IEnumerable<SDPAudioVideoMediaFormat> formats, string formatName)
    {
        if (formatName is { })
        {
            foreach (var format in formats)
            {
                if (string.Equals(format.Name(), formatName, StringComparison.OrdinalIgnoreCase))
                {
                    return format;
                }
            }

            return Empty;
        }

        return Empty;
    }
}
