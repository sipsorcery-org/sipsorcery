using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net;

internal static partial class NetSdpLoggingExtensions
{
    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidSdpLineFormat",
        Level = LogLevel.Warning,
        Message = "The SDP message had an invalid SDP line format for 'o=': {SdpLineTrimmed}",
        SkipEnabledCheck = true)]
    private static partial void LogSdpInvalidSdpLineFormatUnchecked(
        this ILogger logger,
        string sdpLineTrimmed);

    public static void LogSdpInvalidSdpLineFormat(
        this ILogger logger,
        ReadOnlySpan<char> sdpLineTrimmed)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogSdpInvalidSdpLineFormatUnchecked(logger, sdpLineTrimmed.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpUnrecognisedMediaFormat",
        Level = LogLevel.Warning,
        Message = "Excluding unrecognised well known media format ID {FormatId}.")]
    public static partial void LogSdpUnrecognisedMediaFormat(
        this ILogger logger,
        int formatId);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpMediaFormatAttribute",
        Level = LogLevel.Warning,
        Message = "Non-numeric audio/video media format attribute in SDP: {SdpLine}")]
    public static partial void LogSdpMediaFormatAttribute(
        this ILogger logger,
        string sdpLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidMediaFormatParamAttribute",
        Level = LogLevel.Warning,
        Message = "Invalid media format parameter attribute in SDP: {SdpLine}")]
    public static partial void LogSdpInvalidMediaFormatParamAttribute(
        this ILogger logger,
        string sdpLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpNoActiveMediaAnnouncement",
        Level = LogLevel.Warning,
        Message = "There was no active media announcement for a media format attribute, ignoring.")]
    public static partial void LogSdpNoActiveMediaAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpNoActiveMediaAnnouncementForParam",
        Level = LogLevel.Warning,
        Message = "There was no active media announcement for a media format parameter attribute, ignoring.")]
    public static partial void LogSdpNoActiveMediaAnnouncementForParam(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpMediaIdOnlyOnAnnouncement",
        Level = LogLevel.Warning,
        Message = "A media ID can only be set on a media announcement.")]
    public static partial void LogSdpMediaIdOnlyOnAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpSsrcGroupIdOnlyOnAnnouncement",
        Level = LogLevel.Warning,
        Message = "A ssrc-group ID can only be set on a media announcement.")]
    public static partial void LogSdpSsrcGroupIdOnlyOnAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpSsrcAttributeOnlyOnAnnouncement",
        Level = LogLevel.Warning,
        Message = "An ssrc attribute can only be set on a media announcement.")]
    public static partial void LogSdpSsrcAttributeOnlyOnAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidSctpPort",
        Level = LogLevel.Warning,
        Message = "An sctp-port value of {SctpPortStr} was not recognised as a valid port.",
        SkipEnabledCheck = true)]
    private static partial void LogSdpInvalidSctpPortUnchecked(
        this ILogger logger,
        string sctpPortStr);

    public static void LogSdpInvalidSctpPort(
        this ILogger logger,
        ReadOnlySpan<char> sctpPortSpan)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogSdpInvalidSctpPortUnchecked(logger, sctpPortSpan.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidMaxMessageSize",
        Level = LogLevel.Warning,
        Message = "A max-message-size value of {MaxMessageSizeStr} was not recognised as a valid long.",
        SkipEnabledCheck = true)]
    private static partial void LogSdpInvalidMaxMessageSizeUnchecked(
        this ILogger logger,
        string maxMessageSizeStr);

    public static void LogSdpInvalidMaxMessageSize(
        this ILogger logger,
        ReadOnlySpan<char> maxMessageSizeSpan)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogSdpInvalidMaxMessageSizeUnchecked(logger, maxMessageSizeSpan.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpSctpMapOnlyOnAnnouncement",
        Level = LogLevel.Warning,
        Message = "An sctpmap attribute can only be set on a media announcement.")]
    public static partial void LogSdpSctpMapOnlyOnAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpSctpPortOnlyOnAnnouncement",
        Level = LogLevel.Warning,
        Message = "An sctp-port attribute can only be set on a media announcement.")]
    public static partial void LogSdpSctpPortOnlyOnAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpMaxMessageSizeOnlyOnAnnouncement",
        Level = LogLevel.Warning,
        Message = "A max-message-size attribute can only be set on a media announcement.")]
    public static partial void LogSdpMaxMessageSizeOnlyOnAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpAcceptTypesOnlyOnAnnouncement",
        Level = LogLevel.Warning,
        Message = "A accept-types attribute can only be set on a media announcement.")]
    public static partial void LogSdpAcceptTypesOnlyOnAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpPathOnlyOnAnnouncement",
        Level = LogLevel.Warning,
        Message = "A path attribute can only be set on a media announcement.")]
    public static partial void LogSdpPathOnlyOnAnnouncement(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpCryptoParsingError",
        Level = LogLevel.Warning,
        Message = "Error Parsing SDP-Line(a=crypto)")]
    public static partial void LogSdpCryptoParsingError(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpParseException",
        Level = LogLevel.Error,
        Message = "Exception ParseSDPDescription. {ErrorMessage}")]
    public static partial void LogSdpParseException(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpDuplicateConnectionAttribute",
        Level = LogLevel.Warning,
        Message = "The SDP message had a duplicate connection attribute which was ignored.")]
    public static partial void LogSdpDuplicateConnectionAttribute(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidMediaLine",
        Level = LogLevel.Warning,
        Message = "A media line in SDP was invalid: {SdpLine}.",
        SkipEnabledCheck = true)]
    private static partial void LogSdpInvalidMediaLineUnchecked(
        this ILogger logger,
        string sdpLine);

    public static void LogSdpInvalidMediaLine(
        this ILogger logger,
        ReadOnlySpan<char> sdpLine)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogSdpInvalidMediaLineUnchecked(logger, sdpLine.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidIceRole",
        Level = LogLevel.Warning,
        Message = "ICE role was not recognised from SDP attribute: {SdpLineTrimmed}.",
        SkipEnabledCheck = true)]
    private static partial void LogSdpInvalidIceRoleUnchecked(
        this ILogger logger,
        string sdpLineTrimmed);

    public static void LogSdpInvalidIceRole(
        this ILogger logger,
        ReadOnlySpan<char> sdpLineTrimmed)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogSdpInvalidIceRoleUnchecked(logger, sdpLineTrimmed.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpMissingColon",
        Level = LogLevel.Warning,
        Message = "ICE role SDP attribute was missing the mandatory colon: {SdpLineTrimmed}.",
        SkipEnabledCheck = true)]
    private static partial void LogSdpMissingColonUnchecked(
        this ILogger logger,
        string sdpLineTrimmed);

    public static void LogSdpMissingColon(
        this ILogger logger,
        ReadOnlySpan<char> sdpLineTrimmed)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogSdpMissingColonUnchecked(logger, sdpLineTrimmed.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidHeaderExtension",
        Level = LogLevel.Warning,
        Message = "Invalid id of header extension in " + SDPMediaAnnouncement.MEDIA_EXTENSION_MAP_ATTRIBUE_PREFIX)]
    public static partial void LogSdpInvalidHeaderExtension(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidVersion",
        Level = LogLevel.Warning,
        Message = "The Version value in an SDP description could not be parsed as a decimal: {SdpLine}.",
        SkipEnabledCheck = true)]
    private static partial void LogSdpInvalidVersionUnchecked(
        this ILogger logger,
        string sdpLine);

    public static void LogSdpInvalidVersion(
        this ILogger logger,
        ReadOnlySpan<char> sdpLine)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogSdpInvalidVersionUnchecked(logger, sdpLine.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpParseError",
        Level = LogLevel.Error,
        Message = "Failed to parse SDP announcement. {ErrorMessage}")]
    public static partial void LogSdpParseError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpMediaFormatError",
        Level = LogLevel.Warning,
        Message = "Media format error in SDP announcement. {ErrorMessage}")]
    public static partial void LogSdpMediaFormatError(
        this ILogger logger,
        string errorMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpInvalidIceCandidate",
        Level = LogLevel.Error,
        Message = "Invalid ICE candidate: {IceCandidate}")]
    private static partial void LogSdpInvalidIceCandidateUnchecked(
        this ILogger logger,
        string iceCandidate);

    public static void LogSdpInvalidIceCandidate(
        this ILogger logger,
        ReadOnlySpan<char> iceCandidate)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogSdpInvalidIceCandidateUnchecked(logger, iceCandidate.ToString());
        }
    }
}
