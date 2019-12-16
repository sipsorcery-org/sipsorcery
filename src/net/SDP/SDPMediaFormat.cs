//-----------------------------------------------------------------------------
// Filename: SDPMediaFormat.cs
//
// Description: 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// ??	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;

namespace SIPSorcery.Net
{
    /// <summary>
    /// A list of standard media formats that can be idenitifed by an ID if
    /// there is no qualifiying format attribute provided.
    /// </summary>
    public enum SDPMediaFormatsEnum
    {
        PCMU = 0,   // Audio.
        GSM = 3,    // Audio.
        G723 = 4,   // Audio.
        G722 = 9,   // Audio.
        PCMA = 8,   // Audio.
        G729 = 18,  // Audio.

        JPEG = 26,  // Video
        H263 = 34,  // Video.
    }

    /// <summary>
    /// A list of standard media types that do not have a a recognised ID but still do have
    /// recognised properties.
    /// <code>
    /// // Example
    /// m=video 49170 RTP/AVPF 98   // "98" is not a standard ID for VP8 and could be anything.
    /// a=rtpmap:98 VP8/90000
    /// a=fmtp:98 max-fr=30; max-fs=3600;
    /// </code>
    /// </summary>
    public enum SDPNonStandardMediaFormatsEnum
    {
        VP8 = 0,  // Video. Clock rate 90000.
    }

    public class SDPMediaFormatInfo
    {
        /// <summary>
        /// Attempts to get the clock rate of known payload types.
        /// </summary>
        /// <param name="mediaType">The media type to get the clock rate for.</param>
        /// <returns>An integer representing the payload type's sampling frequency or 0
        /// if it's not known.</returns>
        public static int GetClockRate(SDPMediaFormatsEnum payloadType)
        {
            switch (payloadType)
            {
                case SDPMediaFormatsEnum.PCMU:
                case SDPMediaFormatsEnum.PCMA:
                    return 8000;
                case SDPMediaFormatsEnum.G722:
                    return 16000;
                default:
                    return 0;
            }
        }
    }

    /// <summary>
    /// Represents a single media format within a media announcement. Often the whole media format can
    /// be represented and described by a single character, e.g. "0" without additional info represents
    /// standard "PCMU", "8" represents "PCMA" etc. For other media types that have variable parameters
    /// additional attributes can be provided.
    /// </summary>
    public class SDPMediaFormat
    {
        /// <summary>
        /// The mandatory ID for the media format. Warning, even though some ID's are normally used to represent
        /// a standard media type, e.g "0" for "PCMU" etc, there is no guaranteee that's the case. "0" can be used
        /// for any media format if there is a format attribute describing it. In the absence of a format attribute
        /// then it is required that it represents a standard media type.
        /// 
        /// Note (rj2): FormatID MUST be string (not int), in case ID is 't38' and type is 'image'
        /// <code>
        /// // Example
        /// // Note in this example "0" is representing a standard format so the format attribute is optional.
        /// m=audio 12228 RTP/AVP 0 101         // "0" and "101" are media format ID's.
        /// a=rtpmap:0 PCMU/8000                // "0" is the media format ID.
        /// a=rtpmap:101 telephone-event/8000   // "101" is the media format ID.
        /// a=fmtp:101 0-16
        /// </code>
        /// </summary>
        public string FormatID;
        
        /// <summary>
        /// The optional format attribute for the media format. For standard media types this is not necessary.
        /// <code>
        /// // Example
        /// a=rtpmap:0 PCMU/8000
        /// a=rtpmap:101 telephone-event/8000 // This is the format attribute.
        /// a=fmtp:101 0-16
        /// </code>
        /// </summary>
        public string FormatAttribute { get; private set; }

        /// <summary>
        /// The optional format parameter attribute for the media format. For standard media types this is not necessary.
        /// <code>
        /// // Example
        /// a=rtpmap:0 PCMU/8000
        /// a=rtpmap:101 telephone-event/8000 
        /// a=fmtp:101 0-16                     // This is the format parameter attribute.
        /// </code>
        /// </summary>
        public string FormatParameterAttribute { get; private set; }

        /// <summary>
        /// The standard name of the media format.
        /// <code>
        /// // Example
        /// a=rtpmap:0 PCMU/8000                // "PCMU" is the media format name.
        /// a=rtpmap:101 telephone-event/8000 
        /// a=fmtp:101 0-16
        /// </code>
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The clock rate set on the SDP media format attribute. This will typically not be set for well known media
        /// types and will have a default value of 0.
        /// </summary>
        public int ClockRate { get; private set; } = 0;
        
        /// <summary>
        /// For well known media types this will contain the default clock rate. Warning, if the format is not known or
        /// is dynamic this can be 0.
        /// </summary>
        public int DefaultClockRate { get; private set; }

        /// <summary>
        ///  If true this is a standard media format and the attribute line is not required.
        /// </summary>
        public bool IsStandardAttribute { get; set; }

        public SDPMediaFormat(int formatID)
        {
            FormatID = formatID.ToString();
            if (Enum.IsDefined(typeof(SDPMediaFormatsEnum), formatID))
            {
                Name = Enum.Parse(typeof(SDPMediaFormatsEnum), formatID.ToString(), true).ToString();
                DefaultClockRate = SDPMediaFormatInfo.GetClockRate((SDPMediaFormatsEnum)formatID);
                IsStandardAttribute = true;
            }
            FormatAttribute = (ClockRate == 0) ? Name : Name + "/" + ClockRate;
        }

        public SDPMediaFormat(string formatID)
        {
            Name = FormatID = formatID;
        }

        public SDPMediaFormat(int formatID, string name) : this(formatID)
        {
            Name = name;
            FormatAttribute = (ClockRate == 0) ? Name : Name + "/" + ClockRate;
        }

        public SDPMediaFormat(int formatID, string name, int clockRate) : this(formatID)
        {
            Name = name;
            ClockRate = clockRate;
            FormatAttribute = (ClockRate == 0) ? Name : Name + "/" + ClockRate;
        }

        public SDPMediaFormat(SDPMediaFormatsEnum format) : this((int)format)
        { }

        public void SetFormatAttribute(string attribute)
        {
            FormatAttribute = attribute;

            Match attributeMatch = Regex.Match(attribute, @"(?<name>\w+)/(?<clockrate>\d+)\s*");
            if (attributeMatch.Success)
            {
                Name = attributeMatch.Result("${name}");
                int clockRate;
                if (Int32.TryParse(attributeMatch.Result("${clockrate}"), out clockRate))
                {
                    ClockRate = clockRate;
                }
            }
        }

        public void SetFormatParameterAttribute(string attribute)
        {
            FormatParameterAttribute = attribute;
        }

        /// <summary>
        /// Gets the clock (or sample) rate for this media format.
        /// </summary>
        /// <returns>The clock rate for the media format. Can be 0 if not specified and this is not a 
        /// standard media format.</returns>
        public int GetClockRate()
        {
            if(ClockRate != 0)
            {
                return ClockRate;
            }
            else
            {
                return DefaultClockRate;
            }
        }
    }
}
