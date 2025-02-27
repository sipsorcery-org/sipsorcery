using System;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media
{
    internal static partial class MediaLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "SettingAudioSourceFormat",
            Level = LogLevel.Debug,
            Message = "Setting audio source format to {AudioFormatID}:{AudioFormatCodec}.")]
        public static partial void LogSettingAudioSourceFormat(
            this ILogger logger,
            int audioFormatID,
            AudioCodecsEnum audioFormatCodec);

        [LoggerMessage(
            EventId = 0,
            EventName = "MusicFileNotSetOrFound",
            Level = LogLevel.Warning,
            Message = "Music file not set or not found, using default music resource.")]
        public static partial void LogMusicFileNotSetOrFound(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SendingAudioSteamLength",
            Level = LogLevel.Debug,
            Message = "Sending audio stream length {AudioStreamLength}.")]
        public static partial void LogSendingAudioSteamLength(
            this ILogger logger,
            long audioStreamLength);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpMediaPacketReceived",
            Level = LogLevel.Trace,
            Message = "audio RTP packet received from {RemoteEndPoint} ssrc {SyncSource} seqnum {SequenceNumber} timestamp {Timestamp} payload type {PayloadType}.")]
        public static partial void LogRtpMediaPacketReceived(
            this ILogger logger,
            IPEndPoint remoteEndPoint,
            uint syncSource,
            ushort sequenceNumber,
            uint timestamp,
            int payloadType);

        [LoggerMessage(
            EventId = 0,
            EventName = "SendAudioFromStreamCompleted",
            Level = LogLevel.Debug,
            Message = "Send audio from stream completed.")]
        public static partial void LogSendAudioFromStreamCompleted(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SettingAudioFormat",
            Level = LogLevel.Debug,
            Message = "Setting audio source format to {FormatID}:{Codec} {ClockRate} (RTP clock rate {RtpClockRate}).")]
        public static partial void LogSettingAudioFormat(
            this ILogger logger,
            int formatID,
            AudioCodecsEnum codec,
            int clockRate,
            int rtpClockRate);

        [LoggerMessage(
            EventId = 0,
            EventName = "SettingVideoFormat",
            Level = LogLevel.Debug,
            Message = "Setting video sink and source format to {VideoFormatID}:{VideoCodec}.")]
        public static partial void LogSettingVideoFormat(
            this ILogger logger,
            int videoFormatID,
            VideoCodecsEnum videoCodec);

        [LoggerMessage(
            EventId = 0,
            EventName = "AudioTrackDtmfNegotiated",
            Level = LogLevel.Debug,
            Message = "Audio track negotiated DTMF payload ID {AudioStreamNegotiatedRtpEventPayloadID}.")]
        public static partial void LogAudioTrackDtmfNegotiated(
            this ILogger logger,
            int audioStreamNegotiatedRtpEventPayloadID);

        [LoggerMessage(
            EventId = 0,
            EventName = "VideoCaptureDeviceFailure",
            Level = LogLevel.Warning,
            Message = "Video source for capture device failure. {errorMessage}")]
        public static partial void LogVideoCaptureDeviceFailure(
            this ILogger logger,
            string errorMessage);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebcamVideoSourceFailed",
            Level = LogLevel.Warning,
            Message = "Webcam video source failed before start, switching to test pattern source.")]
        public static partial void LogWebcamFailedSwitchingToPattern(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "StreamClosedWarning",
            Level = LogLevel.Warning,
            Message = "Stream Closed.")]
        public static partial void LogStreamClosedWarning(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "AudioStreamReadError",
            Level = LogLevel.Warning,
            Message = "Failed to read from audio stream source.")]
        public static partial void LogAudioStreamReadError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "AudioStreamNullError",
            Level = LogLevel.Warning,
            Message = "Failed to read from audio stream source, stream null or closed.")]
        public static partial void LogAudioStreamNullError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnhandledStreamException",
            Level = LogLevel.Warning,
            Message = "Caught unhandled exception")]
        public static partial void LogUnhandledStreamException(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StreamReaderCloseError",
            Level = LogLevel.Warning,
            Message = "Error occurred whilst trying to close the stream source reader.")]
        public static partial void LogStreamReaderCloseError(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "FrameRateError",
            Level = LogLevel.Warning,
            Message = "{framesPerSecond} fames per second not in the allowed range of {minimumFramesPerSecond} to {maximumFramesPerSecond}, ignoring.")]
        public static partial void LogFrameRateError(
            this ILogger logger,
            int framesPerSecond,
            int minimumFramesPerSecond,
            int maximumFramesPerSecond);

        [LoggerMessage(
            EventId = 0,
            EventName = "SilenceSampleError",
            Level = LogLevel.Error,
            Message = "Exception sending silence sample")]
        public static partial void LogSendingSilenceSampleError(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SignalGeneratorError",
            Level = LogLevel.Error,
            Message = "Exception sending signal generator sample")]
        public static partial void LogSignalGeneratorError(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "MediaEngineStartError",
            Level = LogLevel.Error,
            Message = "Error starting media engine. {ErrorMessage}")]
        public static partial void LogMediaEngineStartError(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "MediaEngineStop",
            Level = LogLevel.Debug,
            Message = "Media engine stopped")]
        public static partial void LogMediaEngineStop(
            this ILogger logger);
    }
}
