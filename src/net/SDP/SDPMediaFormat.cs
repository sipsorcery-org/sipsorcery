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
    public enum SDPMediaFormatsEnum
    {
        PCMU = 0,   // Audio.
        GSM = 3,    // Audio.
        G723 = 4,   // Audio.
        G722 = 9,   // Audio
        PCMA = 8,   // Audio.
        G729 = 18,  // Audio.
        JPEG = 26,  // Video
        H263 = 34,  // Video.
    }

    public class SDPMediaFormat
    {
        private const int DEFAULT_AUDIO_CLOCK_RATE = 8000;

        public int FormatID;
        public string FormatAttribute { get; private set; }
        public string FormatParameterAttribute { get; private set; }
        public string Name { get; private set; }
        public int ClockRate { get; private set; }
        public bool IsStandardAttribute { get; set; }           // If true this is a standard media format and the attribute line is not required.

        public SDPMediaFormat(int formatID)
        {
            FormatID = formatID;
            if (Enum.IsDefined(typeof(SDPMediaFormatsEnum), formatID))
            {
                Name = Enum.Parse(typeof(SDPMediaFormatsEnum), formatID.ToString(), true).ToString();
            }
            ClockRate = DEFAULT_AUDIO_CLOCK_RATE;
        }

        public SDPMediaFormat(int formatID, string name)
        {
            FormatID = formatID;
            Name = name;
            FormatAttribute = (ClockRate == 0) ? Name : Name;
        }

        public SDPMediaFormat(int formatID, string name, int clockRate)
        {
            FormatID = formatID;
            Name = name;
            ClockRate = clockRate;
            FormatAttribute = (ClockRate == 0) ? Name : Name + "/" + ClockRate;
        }

        public SDPMediaFormat(SDPMediaFormatsEnum format)
        {
            FormatID = (int)format;
            Name = format.ToString();
            IsStandardAttribute = true;
        }

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
    }
}
