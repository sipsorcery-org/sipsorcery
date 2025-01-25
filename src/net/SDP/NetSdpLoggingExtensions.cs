using System;
using System.Net;
using Microsoft.Extensions.Logging;
using TinyJson;

namespace SIPSorcery.Net
{
    internal static partial class NetSdpLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "SdpInvalidSdpLineFormat",
            Level = LogLevel.Warning,
            Message = "The SDP message had an invalid SDP line format for 'o=': {sdpLineTrimmed}")]
        public static partial void LogSdpInvalidSdpLineFormat(
            this ILogger logger,
            string sdpLineTrimmed);

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpUnrecognisedMediaFormat",
            Level = LogLevel.Warning,
            Message = "Excluding unrecognised well known media format ID {formatId}.")]
        public static partial void LogSdpUnrecognisedMediaFormat(
            this ILogger logger,
            int formatId);

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpMediaFormatAttribute",
            Level = LogLevel.Warning,
            Message = "Non-numeric audio/video media format attribute in SDP: {sdpLine}")]
        public static partial void LogSdpMediaFormatAttribute(
            this ILogger logger,
            string sdpLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpInvalidMediaFormatParamAttribute",
            Level = LogLevel.Warning,
            Message = "Invalid media format parameter attribute in SDP: {sdpLine}")]
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
            Message = "An sctp-port value of {sctpPortStr} was not recognised as a valid port.")]
        public static partial void LogSdpInvalidSctpPort(
            this ILogger logger,
            string sctpPortStr);

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpInvalidMaxMessageSize",
            Level = LogLevel.Warning,
            Message = "A max-message-size value of {maxMessageSizeStr} was not recognised as a valid long.")]
        public static partial void LogSdpInvalidMaxMessageSize(
            this ILogger logger,
            string maxMessageSizeStr);

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
            Message = "Error Parsing SDP-Line(a=crypto) {exception}")]
        public static partial void LogSdpCryptoParsingError(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpParseException",
            Level = LogLevel.Error,
            Message = "Exception ParseSDPDescription. {errorMessage}")]
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
            Message = "A media line in SDP was invalid: {sdpLine}.",
            SkipEnabledCheck = true)]
        private static partial void LogSdpInvalidMediaLineUnchecked(
            this ILogger logger,
            string sdpLine);

        public static void LogSdpInvalidMediaLine(
            this ILogger logger,
            string sdpLine)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                LogSdpInvalidMediaLineUnchecked(logger, sdpLine.Substring(2));
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpInvalidIceRole",
            Level = LogLevel.Warning,
            Message = "ICE role was not recognised from SDP attribute: {sdpLineTrimmed}.")]
        public static partial void LogSdpInvalidIceRole(
            this ILogger logger,
            string sdpLineTrimmed);

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpMissingColon",
            Level = LogLevel.Warning,
            Message = "ICE role SDP attribute was missing the mandatory colon: {sdpLineTrimmed}.")]
        public static partial void LogSdpMissingColon(
            this ILogger logger,
            string sdpLineTrimmed);

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
            Message = "The Version value in an SDP description could not be parsed as a decimal: {sdpLine}.")]
        public static partial void LogSdpInvalidVersion(
            this ILogger logger,
            string sdpLine);

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
    }
}
