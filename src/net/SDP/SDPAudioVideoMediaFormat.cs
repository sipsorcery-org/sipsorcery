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
using System.Linq;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net
{
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

        public static SDPAudioVideoMediaFormat Empty = new SDPAudioVideoMediaFormat() { _isEmpty = true };

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
        /// "If the <proto> sub-field is "RTP/AVP" or "RTP/SAVP" the <fmt>
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
        /// a=rtpmap:101 telephone-event/8000 <-- "101 telephone-event/8000" is the rtpmap properties.
        /// a=fmtp:101 0-16
        /// </code>
        /// </summary>
        public string Rtpmap { get; }

        /// <summary>
        /// The optional format parameter attribute for the media format. For standard media types this is not necessary.
        /// <code>
        /// // Example
        /// a=rtpmap:0 PCMU/8000
        /// a=rtpmap:101 telephone-event/8000 
        /// a=fmtp:101 0-16                     <-- "101 0-16" is the fmtp attribute.
        /// </code>
        /// </summary>
        public string Fmtp { get; }

        /// <summary>
        /// The standard name of the media format.
        /// <code>
        /// // Example
        /// a=rtpmap:0 PCMU/8000                <-- "PCMU" is the media format name.
        /// a=rtpmap:101 telephone-event/8000 
        /// a=fmtp:101 0-16
        /// </code>
        /// </summary>
        //public string Name { get; set; }

        private bool _isEmpty;

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
            _isEmpty = false;

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

        /// <summary>
        /// Creates a new SDP media format for a dynamic media type. Dynamic media types are those that use 
        /// ID's between 96 and 127 inclusive and require an rtpmap attribute and optionally an fmtp attribute.
        /// </summary>
        public SDPAudioVideoMediaFormat(SDPMediaTypesEnum kind, int id, string rtpmap, string fmtp = null)
        {
            if (id < 0 || id > DYNAMIC_ID_MAX)
            {
                throw new ApplicationException($"SDP media format IDs must be between 0 and {DYNAMIC_ID_MAX}.");
            }
            else if (string.IsNullOrWhiteSpace(rtpmap))
            {
                throw new ArgumentNullException("rtpmap", "The rtpmap parameter cannot be empty for a dynamic SDPMediaFormat.");
            }

            Kind = kind;
            ID = id;
            Rtpmap = rtpmap;
            Fmtp = fmtp;
            _isEmpty = false;
        }

        /// <summary>
        /// Creates a new SDP media format for a dynamic media type. Dynamic media types are those that use 
        /// ID's between 96 and 127 inclusive and require an rtpmap attribute and optionally an fmtp attribute.
        /// </summary>
        public SDPAudioVideoMediaFormat(SDPMediaTypesEnum kind, int id, string name, int clockRate, int channels = DEFAULT_AUDIO_CHANNEL_COUNT, string fmtp = null)
        {
            if (id < 0 || id > DYNAMIC_ID_MAX)
            {
                throw new ApplicationException($"SDP media format ID must be between 0 and {DYNAMIC_ID_MAX}.");
            }
            else if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException("name", "The name parameter cannot be empty for a dynamic SDPMediaFormat.");
            }

            Kind = kind;
            ID = id;
            Rtpmap = null;
            Fmtp = fmtp;
            _isEmpty = false;

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
            _isEmpty = false;

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
            _isEmpty = false;

            Rtpmap = SetRtpmap(videoFormat.FormatName, videoFormat.ClockRate);
        }

        private string SetRtpmap(string name, int clockRate, int channels = DEFAULT_AUDIO_CHANNEL_COUNT)
            =>
             Kind == SDPMediaTypesEnum.video ? $"{name}/{clockRate}" :
            (channels == DEFAULT_AUDIO_CHANNEL_COUNT) ? $"{name}/{clockRate}" : $"{name}/{clockRate}/{channels}";
        public bool IsEmpty() => _isEmpty;
        public int ClockRate() => Kind == SDPMediaTypesEnum.video ? ToVideoFormat().ClockRate : ToAudioFormat().ClockRate;
        public int Channels() =>
             Kind == SDPMediaTypesEnum.video ? 0 :
            TryParseRtpmap(Rtpmap, out _, out _, out var channels) ? channels : DEFAULT_AUDIO_CHANNEL_COUNT;

        public string Name()
        {
            // Rtpmap taks priority over well known media type as ID's can be changed.
            if (Rtpmap != null && TryParseRtpmap(Rtpmap, out var name, out _, out _))
            {
                return name;
            }
            else if (Enum.IsDefined(typeof(SDPWellKnownMediaFormatsEnum), ID))
            {
                // If no rtpmap available then it must be a well known format.
                return Enum.ToObject(typeof(SDPWellKnownMediaFormatsEnum), ID).ToString();
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
        /// <param name="format">The existing format to copy all properties except the ID from.</param>
        /// <returns>A new format.</returns>
        public SDPAudioVideoMediaFormat WithUpdatedID(int id, SDPAudioVideoMediaFormat format) =>
            new SDPAudioVideoMediaFormat(format.Kind, id, format.Rtpmap, format.Fmtp);

        public SDPAudioVideoMediaFormat WithUpdatedRtpmap(string rtpmap, SDPAudioVideoMediaFormat format) =>
            new SDPAudioVideoMediaFormat(format.Kind, format.ID, rtpmap, format.Fmtp);

        public SDPAudioVideoMediaFormat WithUpdatedFmtp(string fmtp, SDPAudioVideoMediaFormat format) =>
            new SDPAudioVideoMediaFormat(format.Kind, format.ID, format.Rtpmap, fmtp);

        /// <summary>
        /// Maps an audio SDP media type to a media abstraction layer audio format.
        /// </summary>
        /// <returns>An audio format value.</returns>
        public AudioFormat ToAudioFormat()
        {
            // Rtpmap takes priority over well known media type as ID's can be changed.
            if (Rtpmap != null && TryParseRtpmap(Rtpmap, out var name, out int rtpClockRate, out int channels))
            {
                int clockRate = rtpClockRate;

                // G722 is a special case. It's the only audio format that uses the wrong RTP clock rate.
                // It sets 8000 in the SDP but then expects samples to be sent as 16KHz.
                // See https://tools.ietf.org/html/rfc3551#section-4.5.2.
                if (name == "G722" && rtpClockRate == 8000)
                {
                    clockRate = 16000;
                }

                return new AudioFormat(ID, name, clockRate, rtpClockRate, channels, Fmtp);
            }
            else if (ID < DYNAMIC_ID_MIN
                && Enum.TryParse<SDPWellKnownMediaFormatsEnum>(Name(), out var wellKnownFormat)
                && AudioVideoWellKnown.WellKnownAudioFormats.ContainsKey(wellKnownFormat))
            {
                return AudioVideoWellKnown.WellKnownAudioFormats[wellKnownFormat];
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
            if (TryParseRtpmap(Rtpmap, out var name, out int clockRate, out _))
            {
                return new VideoFormat(ID, name, clockRate, Fmtp);
            }
            else
            {
                return VideoFormat.Empty;
            }
        }

        /// <summary>
        /// For two formats to be a match only the codec and rtpmap parameters need to match. The
        /// fmtp parameter does not matter.
        /// </summary>
        public static bool AreMatch(SDPAudioVideoMediaFormat format1, SDPAudioVideoMediaFormat format2)
        {
            // rtpmap takes priority as well known foramt ID's can be overruled.
            if (format1.Rtpmap != null
                && format2.Rtpmap != null
                && format1.Rtpmap == format2.Rtpmap)
            {
                return true;
            }
            else if (format1.ID < DYNAMIC_ID_MIN
                && format1.ID == format2.ID
                && string.Equals(format1.Name(), format2.Name(), StringComparison.OrdinalIgnoreCase))
            {
                // Well known format type.
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to get the compatible formats between two lists. Formats for
        /// "RTP Events" are not included.
        /// </summary>
        /// <param name="a">The first list to match the media formats for.</param>
        /// <param name="b">The second list to match the media formats for.</param>
        /// <returns>A list of media formats that are compatible for BOTH lists.</returns>
        public static List<SDPAudioVideoMediaFormat> GetCompatibleFormats(List<SDPAudioVideoMediaFormat> a, List<SDPAudioVideoMediaFormat> b)
        {
            List<SDPAudioVideoMediaFormat> compatible = new List<SDPAudioVideoMediaFormat>();

            if (a == null || a.Count == 0)
            {
                // Preferable to return an empty list.
                //throw new ArgumentNullException("a", "The first media format list supplied was empty.");
            }
            else if (b == null || b.Count == 0)
            {
                // Preferable to return an empty list.
                //throw new ArgumentNullException("b", "The second media format list supplied was empty.");
            }
            else
            {
                foreach (var format in a)
                {
                    if (b.Any(x => SDPAudioVideoMediaFormat.AreMatch(format, x)))
                    {
                        compatible.Add(format);
                    }
                }
            }

            return compatible;
        }

        /// <summary>
        /// Parses an rtpmap attribute in the form "name/clock" or "name/clock/channels".
        /// </summary>
        public static bool TryParseRtpmap(string rtpmap, out string name, out int clockRate, out int channels)
        {
            name = null;
            clockRate = 0;
            channels = DEFAULT_AUDIO_CHANNEL_COUNT;

            if (string.IsNullOrWhiteSpace(rtpmap))
            {
                return false;
            }
            else
            {
                string[] fields = rtpmap.Trim().Split('/');

                if (fields.Length >= 2)
                {
                    name = fields[0].Trim();
                    if (!int.TryParse(fields[1].Trim(), out clockRate))
                    {
                        return false;
                    }

                    if (fields.Length >= 3)
                    {
                        if (!int.TryParse(fields[2].Trim(), out channels))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Attempts to get a common SDP media format that supports telephone events. 
        /// If compatible an RTP event format will be returned that matches the local format with the remote format.
        /// </summary>
        /// <param name="a">The first of supported media formats.</param>
        /// <param name="b">The second of supported media formats.</param>
        /// <returns>An SDP media format with a compatible RTP event format.</returns>
        public static SDPAudioVideoMediaFormat GetCommonRtpEventFormat(List<SDPAudioVideoMediaFormat> a, List<SDPAudioVideoMediaFormat> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count() == 0)
            {
                return Empty;
            }
            else
            {
                // Check if RTP events are supported and if required adjust the local format ID.
                var aEventFormat = a.FirstOrDefault(x => x.Name()?.ToLower() == SDP.TELEPHONE_EVENT_ATTRIBUTE);
                var bEventFormat = b.FirstOrDefault(x => x.Name()?.ToLower() == SDP.TELEPHONE_EVENT_ATTRIBUTE);

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
        }
    }
}
