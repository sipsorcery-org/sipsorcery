using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Net
{
    public class SDPMediaAnnouncement
    {
        public const string MEDIA_FORMAT_ATTRIBUE_PREFIX = "a=rtpmap:";
		public const string MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX = "a=fmtp:";

        public const string m_CRLF = "\r\n";

        public SDPConnectionInformation Connection;

        // Media Announcement fields.
        public SDPMediaTypesEnum Media = SDPMediaTypesEnum.audio;	// Media type for the stream.
        public int Port;						// For UDP transports should be in the range 1024 to 65535 and for RTP compliance should be even (only even ports used for data).
        public string Transport = "RTP/AVP";	// Defined types RTP/AVP (RTP Audio Visual Profile) and udp.

        public List<string> BandwidthAttributes = new List<string>();
        public List<SDPMediaFormat> MediaFormats = new List<SDPMediaFormat>();  // For AVP these will normally be a media payload type as defined in the RTP Audio/Video Profile.
		public List<string> ExtraAttributes = new List<string>();  // Attributes that were not recognised.

        public SDPMediaAnnouncement()
        { }

        public SDPMediaAnnouncement(int port)
        {
            Port = port;
        }

        public SDPMediaAnnouncement(SDPConnectionInformation connection)
        {
            Connection = connection;
        }

        public SDPMediaAnnouncement(SDPMediaTypesEnum mediaType, int port, List<SDPMediaFormat> mediaFormats)
        {
            Media = mediaType;
            Port = port;
            MediaFormats = mediaFormats;
        }

        public void ParseMediaFormats(string formatList)
        {
            if (!String.IsNullOrWhiteSpace(formatList))
            {
                string[] formatIDs = Regex.Split(formatList, @"\s");
                if (formatIDs != null)
                {
                    foreach (string formatID in formatIDs)
                    {
                        int format;
                        if (Int32.TryParse(formatID, out format))
                        {
                            MediaFormats.Add(new SDPMediaFormat(format));
                        }
                    }
                }
            }
        }

        public bool HasMediaFormat(int formatID)
        {
            foreach (SDPMediaFormat mediaFormat in MediaFormats)
            {
                if (mediaFormat.FormatID == formatID)
                {
                    return true;
                }
            }

            return false;
        }

        public void AddFormatAttribute(int formatID, string formatAttribute)
        {
            for(int index=0; index < MediaFormats.Count; index++)
            {
                if (MediaFormats[index].FormatID == formatID)
                {
                    MediaFormats[index].SetFormatAttribute(formatAttribute);
                }
            }
        }

		public void AddFormatParameterAttribute(int formatID, string formatAttribute)
		{
			for(int index = 0; index < MediaFormats.Count; index++)
			{
				if(MediaFormats[index].FormatID == formatID)
				{
					MediaFormats[index].SetFormatParameterAttribute(formatAttribute);
				}
			}
		}

		public override string ToString()
        {
            string announcement = "m=" + Media + " " + Port + " " + Transport + " " + GetFormatListToString() + m_CRLF;
            announcement += (Connection == null) ? null : Connection.ToString();

            foreach (string bandwidthAttribute in BandwidthAttributes)
            {
                announcement += "b=" + bandwidthAttribute + m_CRLF;
            }

            announcement += GetFormatListAttributesToString();

			foreach(string extra in ExtraAttributes)
			{
				announcement += string.IsNullOrWhiteSpace(extra) ? null : extra + m_CRLF;
			}
			
			return announcement;
        }

        public string GetFormatListToString()
        {
            string mediaFormatList = null;
            foreach (SDPMediaFormat mediaFormat in MediaFormats)
            {
                mediaFormatList += mediaFormat.FormatID + " ";
            }

            return (mediaFormatList != null) ? mediaFormatList.Trim() : null;
        }

        public string GetFormatListAttributesToString()
        {
            string formatAttributes = null;

            if (MediaFormats != null)
            {
                foreach (SDPMediaFormat mediaFormat in MediaFormats.Where(x => x.IsStandardAttribute == false))
                {
                    if (mediaFormat.FormatAttribute != null)
                    {
                        formatAttributes += SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUE_PREFIX + mediaFormat.FormatID + " " + mediaFormat.FormatAttribute + m_CRLF;
                    }
                    else
                    {
                        formatAttributes += SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUE_PREFIX + mediaFormat.FormatID + " " + mediaFormat.Name + "/" + mediaFormat.ClockRate + m_CRLF;
                    }
					if(mediaFormat.FormatParameterAttribute != null)
					{
						formatAttributes += SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX + mediaFormat.FormatID + " " + mediaFormat.FormatParameterAttribute + m_CRLF;
					}
					//else if(SDPMediaFormat.GetDefaultFormatAttribute(mediaFormat.FormatID) != null)
					//{
					//    formatAttributes += SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUE_PREFIX + mediaFormat.FormatID + " " + SDPMediaFormat.GetDefaultFormatAttribute(mediaFormat.FormatID) + m_CRLF;
					//}
				}
			}

            return formatAttributes;
        }
		public void AddExtra(string attribute)
		{
			if(!string.IsNullOrWhiteSpace(attribute))
				ExtraAttributes.Add(attribute);
		}

	}
}
