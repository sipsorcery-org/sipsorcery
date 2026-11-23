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

namespace SIPSorcery.Net;

public class SDPMediaTypes
{
    public static SDPMediaTypesEnum GetSDPMediaType(string mediaType)
    {
        if (mediaType is null)
        {
            // Let Enum.Parse reject null so the original ArgumentNullException, including its parameter name, is preserved.
            return (SDPMediaTypesEnum)Enum.Parse(typeof(SDPMediaTypesEnum), mediaType, true);
        }

        return GetSDPMediaType(mediaType.AsSpan());
    }

    public static SDPMediaTypesEnum GetSDPMediaType(ReadOnlySpan<char> mediaType)
    {
        if (mediaType.Equals(nameof(SDPMediaTypesEnum.audio).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.audio;
        }
        else if (mediaType.Equals(nameof(SDPMediaTypesEnum.video).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.video;
        }
        else if (mediaType.Equals(nameof(SDPMediaTypesEnum.application).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.application;
        }
        else if (mediaType.Equals(nameof(SDPMediaTypesEnum.data).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.data;
        }
        else if (mediaType.Equals(nameof(SDPMediaTypesEnum.control).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.control;
        }
        else if (mediaType.Equals(nameof(SDPMediaTypesEnum.image).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.image;
        }
        else if (mediaType.Equals(nameof(SDPMediaTypesEnum.message).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.message;
        }
        else if (mediaType.Equals(nameof(SDPMediaTypesEnum.text).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.text;
        }
        else if (mediaType.Equals(nameof(SDPMediaTypesEnum.invalid).AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SDPMediaTypesEnum.invalid;
        }

        return (SDPMediaTypesEnum)Enum.Parse(typeof(SDPMediaTypesEnum), mediaType.ToString(), true);
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
        => IsMediaStreamStatusAttribute(attributeString.AsSpan(), out mediaStreamStatus);

    /// <summary>
    /// Checks whether an SDP attribute is one of the four possible media stream attributes.
    /// </summary>
    /// <param name="attribute">The attribute characters to check.</param>
    /// <param name="mediaStreamStatus">If the attribute was recognised as a media stream attribute this will hold it.</param>
    /// <returns>True if the attribute matched or false if not.</returns>
    public static bool IsMediaStreamStatusAttribute(ReadOnlySpan<char> attribute, out MediaStreamStatusEnum mediaStreamStatus)
    {
        mediaStreamStatus = MediaStreamStatusEnum.SendRecv;

        if (attribute.Equals(SEND_RECV_ATTRIBUTE.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        else if (attribute.Equals(SEND_ONLY_ATTRIBUTE.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            mediaStreamStatus = MediaStreamStatusEnum.SendOnly;
            return true;
        }
        else if (attribute.Equals(RECV_ONLY_ATTRIBUTE.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            mediaStreamStatus = MediaStreamStatusEnum.RecvOnly;
            return true;
        }
        else if (attribute.Equals(INACTIVE_ATTRIBUTE.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            mediaStreamStatus = MediaStreamStatusEnum.Inactive;
            return true;
        }

        return false;
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

    /// <summary>
    /// Gets the direction to answer a media stream with, as RFC 3264 section 6.1 has it: no more
    /// than the answerer wants and no more than the offer allows. A sendonly offer is answered
    /// recvonly or inactive, a recvonly offer sendonly or inactive and an inactive offer inactive.
    /// </summary>
    /// <param name="offered">The direction in the offer.</param>
    /// <param name="local">The direction the answerer wants for the stream.</param>
    /// <returns>The direction for the answer.</returns>
    public static MediaStreamStatusEnum GetAnswerMediaStreamStatus(MediaStreamStatusEnum offered, MediaStreamStatusEnum local)
    {
        // We can send if we want to and they'll receive, and receive if we want to and they'll send.
        bool send = IsSending(local) && IsReceiving(offered);
        bool receive = IsReceiving(local) && IsSending(offered);

        return send && receive ? MediaStreamStatusEnum.SendRecv :
            send ? MediaStreamStatusEnum.SendOnly :
            receive ? MediaStreamStatusEnum.RecvOnly :
            MediaStreamStatusEnum.Inactive;
    }

    private static bool IsSending(MediaStreamStatusEnum status) =>
        status == MediaStreamStatusEnum.SendRecv || status == MediaStreamStatusEnum.SendOnly;

    private static bool IsReceiving(MediaStreamStatusEnum status) =>
        status == MediaStreamStatusEnum.SendRecv || status == MediaStreamStatusEnum.RecvOnly;
}
