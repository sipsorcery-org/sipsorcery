//-----------------------------------------------------------------------------
// Filename: SDPTypes.cs
//
// Description: Contains enums and helper classes for common definitions
// and attributes used in SDP payloads.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// Jacek Dzija
// Mateusz Greczek
//
// History:
// ??	Aaron Clauson	Created, Hobart, Australia.
// 30 Mar 2021 Jacek Dzija,Mateusz Greczek Added MSRP
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    public enum SDPMediaTypesEnum
    {
        audio = 1,
        video = 2,
        application = 3,
        data = 4,
        control = 5,
        image = 6,
        message = 7
    }

    public class SDPMediaTypes
    {
        public static SDPMediaTypesEnum GetSDPMediaType(string mediaType)
        {
            return (SDPMediaTypesEnum)Enum.Parse(typeof(SDPMediaTypesEnum), mediaType, true);
        }
        public static SDPMediaTypesEnum GetSDPMediaType(int mediaType)
        {
            return (SDPMediaTypesEnum)mediaType;
        }
    }

    /// <summary>
    /// The types of status that a media stream can have. Note that the stream status can
    /// be defined with an attribute at session level or at media element level. If no 
    /// attribute is defined then the default is "sendrecv".
    /// Note that this status applies to RTP streams only. If there is an RTCP stream 
    /// associated with the RTP it should carry on as normal.
    /// See https://tools.ietf.org/html/rfc4566#section-6
    /// </summary>
    public enum MediaStreamStatusEnum
    {
        SendRecv = 0,   // The offerer is prepared to send and receive packets.
        SendOnly = 1,   // The offerer only wishes to send RTP packets. They will probably ignore any received.
        RecvOnly = 2,   // The offerer only wishes to receive RTP packets. They will not send.
        Inactive = 3    // The offerer is not ready to send or receive packets.
    }

    public class MediaStreamStatusType
    {
        public const string SEND_RECV_ATTRIBUTE = "a=sendrecv";
        public const string SEND_ONLY_ATTRIBUTE = "a=sendonly";
        public const string RECV_ONLY_ATTRIBUTE = "a=recvonly";
        public const string INACTIVE_ATTRIBUTE = "a=inactive";

        /// <summary>
        /// Checks whether an SDP attribute is one of the four possible media stream attributes.
        /// </summary>
        /// <param name="attributeString">The attribute string to check.</param>
        /// <param name="mediaStreamStatus">If the attribute was recognised as a media stream attribute this will hold it.</param>
        /// <returns>True if the attribute matched or false if not.</returns>
        public static bool IsMediaStreamStatusAttribute(string attributeString, out MediaStreamStatusEnum mediaStreamStatus)
        {
            mediaStreamStatus = MediaStreamStatusEnum.SendRecv;

            if (string.IsNullOrEmpty(attributeString))
            {
                return false;
            }
            else
            {
                switch (attributeString.ToLower())
                {
                    case SEND_RECV_ATTRIBUTE:
                        mediaStreamStatus = MediaStreamStatusEnum.SendRecv;
                        return true;
                    case SEND_ONLY_ATTRIBUTE:
                        mediaStreamStatus = MediaStreamStatusEnum.SendOnly;
                        return true;
                    case RECV_ONLY_ATTRIBUTE:
                        mediaStreamStatus = MediaStreamStatusEnum.RecvOnly;
                        return true;
                    case INACTIVE_ATTRIBUTE:
                        mediaStreamStatus = MediaStreamStatusEnum.Inactive;
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Gets the attribute to include in a serialised SDP payload for a media stream status.
        /// </summary>
        /// <param name="mediaStreamStatus">The status to get the attribute for.</param>
        /// <returns>An attribute string matching the status value.</returns>
        public static string GetAttributeForMediaStreamStatus(MediaStreamStatusEnum mediaStreamStatus)
        {
            switch (mediaStreamStatus)
            {
                case MediaStreamStatusEnum.SendRecv:
                    return SEND_RECV_ATTRIBUTE;
                case MediaStreamStatusEnum.SendOnly:
                    return SEND_ONLY_ATTRIBUTE;
                case MediaStreamStatusEnum.RecvOnly:
                    return RECV_ONLY_ATTRIBUTE;
                case MediaStreamStatusEnum.Inactive:
                    return INACTIVE_ATTRIBUTE;
                default:
                    // Default is to use sendrecv.
                    return SEND_RECV_ATTRIBUTE;
            }
        }
    }
}
