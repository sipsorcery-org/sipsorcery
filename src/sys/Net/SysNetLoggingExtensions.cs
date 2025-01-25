using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    internal static partial class SysNetLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "CreateBoundSocketStart",
            Level = LogLevel.Debug,
            Message = "CreateBoundSocket attempting to create and bind socket(s) on {EndPoint} using protocol {Protocol}.")]
        public static partial void LogCreateBoundSocketStart(
            this ILogger logger,
            IPEndPoint endPoint,
            ProtocolType protocol);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateBoundSocketEvenPortClose",
            Level = LogLevel.Debug,
            Message = "CreateBoundSocket even port required, closing socket on {LocalEndPoint}, max port reached request new bind.")]
        public static partial void LogCreateBoundSocketEvenPortClose(
            this ILogger logger,
            EndPoint localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateBoundSocketEvenPortRetry",
            Level = LogLevel.Debug,
            Message = "CreateBoundSocket even port required, closing socket on {LocalEndPoint} and retrying on {NextPort}.")]
        public static partial void LogCreateBoundSocketEvenPortRetry(
            this ILogger logger,
            EndPoint localEndPoint,
            int nextPort);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateBoundSocketSuccessDualMode",
            Level = LogLevel.Debug,
            Message = "CreateBoundSocket successfully bound on {LocalEndPoint}, dual mode {DualMode}.")]
        public static partial void LogCreateBoundSocketSuccessDualMode(
            this ILogger logger,
            EndPoint localEndPoint,
            bool dualMode);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateBoundSocketSuccess",
            Level = LogLevel.Debug,
            Message = "CreateBoundSocket successfully bound on {LocalEndPoint}.")]
        public static partial void LogCreateBoundSocketSuccess(
            this ILogger logger,
            EndPoint localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "WSLBindCheck",
            Level = LogLevel.Debug,
            Message = "WSL detected, carrying out bind check on 0.0.0.0:{Port}.")]
        public static partial void LogWSLBindCheck(
            this ILogger logger,
            int port);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateRtpSocketStart",
            Level = LogLevel.Debug,
            Message = "CreateRtpSocket attempting to create and bind RTP socket(s) on {BindEndPoint}.")]
        public static partial void LogCreateRtpSocketStart(
            this ILogger logger,
            IPEndPoint bindEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateRtpSocketBindFailed",
            Level = LogLevel.Warning,
            Message = "CreateRtpSocket failed to create and bind RTP socket(s) on {BindEndPoint}, bind attempt {BindAttempt}.")]
        public static partial void LogCreateRtpSocketBindFailed(
            this ILogger logger,
            IPEndPoint bindEndPoint,
            int bindAttempt);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateRtpSocketSuccessDualMode",
            Level = LogLevel.Debug,
            Message = "Successfully bound RTP socket {LocalEndPoint} (dual mode {DualMode}) and control socket {ControlEndPoint} (dual mode {ControlDualMode}).")]
        public static partial void LogCreateRtpSocketSuccessDualMode(
            this ILogger logger,
            EndPoint localEndPoint,
            bool dualMode,
            EndPoint controlEndPoint,
            bool controlDualMode);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateRtpSocketSuccess",
            Level = LogLevel.Debug,
            Message = "Successfully bound RTP socket {LocalEndPoint} and control socket {ControlEndPoint}.")]
        public static partial void LogCreateRtpSocketSuccess(
            this ILogger logger,
            EndPoint localEndPoint,
            EndPoint controlEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateRtpSocketSingleSuccessDualMode",
            Level = LogLevel.Debug,
            Message = "Successfully bound RTP socket {LocalEndPoint} (dual mode {DualMode}).")]
        public static partial void LogCreateRtpSocketSingleSuccessDualMode(
            this ILogger logger,
            EndPoint localEndPoint,
            bool dualMode);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreateRtpSocketSingleSuccess",
            Level = LogLevel.Debug,
            Message = "Successfully bound RTP socket {LocalEndPoint}.")]
        public static partial void LogCreateRtpSocketSingleSuccess(
            this ILogger logger,
            EndPoint localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "DualModeSupportCheckFailed",
            Level = LogLevel.Warning,
            Message = "A socket 'receive from' attempt on a dual mode socket failed (dual mode RTP sockets will not be used) with {Message}")]
        public static partial void LogDualModeSupportCheckFailed(
            this ILogger logger,
            string message,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "SocketBindAddressInUse",
            Level = LogLevel.Warning,
            Message = "Address already in use exception attempting to bind socket, attempt {bindAttempts}.")]
        public static partial void LogSocketBindAddressInUse(
            this ILogger logger,
            int bindAttempts);

        [LoggerMessage(
            EventId = 0,
            EventName = "SocketBindAccessDenied",
            Level = LogLevel.Warning,
            Message = "Access denied exception attempting to bind socket, attempt {bindAttempts}.")]
        public static partial void LogSocketBindAccessDenied(
            this ILogger logger,
            int bindAttempts);

        [LoggerMessage(
            EventId = 0,
            EventName = "SocketBindException",
            Level = LogLevel.Error,
            Message = "SocketException in NetServices.CreateBoundSocket. {errorMessage}")]
        public static partial void LogSocketBindException(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SocketBindInitialException",
            Level = LogLevel.Error,
            Message = "Exception in NetServices.CreateBoundSocket attempting the initial socket bind on address {bindAddress}.")]
        public static partial void LogSocketBindInitialException(
            this ILogger logger,
            IPAddress bindAddress,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "NetworkCertificateError",
            Level = LogLevel.Error,
            Message = "Exception loading network certificate. {ErrorMessage}")]
        public static partial void LogNetworkCertificateError(
            this ILogger logger,
            string errorMessage,
            Exception exception);
    }
}
