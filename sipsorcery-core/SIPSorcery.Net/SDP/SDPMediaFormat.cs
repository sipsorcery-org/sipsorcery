using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Net
{
    public enum SDPMediaFormatsEnum
    {
        PCMU = 0,
        GSM = 3,
        PCMA = 8,
        G723 = 96,
    }

    public class SDPMediaFormat
    {
        private const int DEFAULT_CLOCK_RATE = 8000;

        //private static Dictionary<int, string> m_defaultFormatNames = new Dictionary<int, string>();

        public int FormatID;
        public string FormatAttribute { get; private set; }
        public string Name { get; private set; }
        public int ClockRate { get; private set; }

        static SDPMediaFormat()
        {
            //m_defaultFormatNames.Add((int)SDPMediaFormatsEnum.PCMU, "PCMU/8000");
            //m_defaultFormatNames.Add((int)SDPMediaFormatsEnum.GSM, "GSM/8000");
            //m_defaultFormatNames.Add((int)SDPMediaFormatsEnum.PCMA, "PCMA/8000");
            //m_defaultFormatNames.Add((int)SDPMediaFormatsEnum.G723, "G723/8000");
        }

        public SDPMediaFormat(int formatID)
        {
            FormatID = formatID;
            if (Enum.IsDefined(typeof(SDPMediaFormatsEnum), formatID))
            {
                Name = Enum.Parse(typeof(SDPMediaFormatsEnum), formatID.ToString()).ToString();
            }
            ClockRate = DEFAULT_CLOCK_RATE;
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

        //public static string GetDefaultFormatAttribute(int mediaFormat)
        //{
        //    if (m_defaultFormats.ContainsKey(mediaFormat))
        //    {
        //        return m_defaultFormats[mediaFormat];
        //    }
        //    else
        //    {
        //        return null;
        //    }
        //}
    }
}
