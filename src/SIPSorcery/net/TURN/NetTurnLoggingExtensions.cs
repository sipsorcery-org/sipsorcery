using System;
using System.Net;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net;

internal static partial class NetTurnLoggingExtensions
{
    // TurnServer logging
    [LoggerMessage(
        EventId = 0,
        EventName = "TurnServerTcpStarted",
        Level = LogLevel.Debug,
        Message = "TURN server TCP listener started on {Address}:{Port}.")]
    public static partial void LogTurnServerTcpStarted(
        this ILogger logger,
        IPAddress address,
        int port);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnServerUdpStarted",
        Level = LogLevel.Debug,
        Message = "TURN server UDP listener started on {Address}:{Port}.")]
    public static partial void LogTurnServerUdpStarted(
        this ILogger logger,
        IPAddress address,
        int port);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnServerStarted",
        Level = LogLevel.Information,
        Message = "TURN server started on {Address}:{Port} (TCP={Tcp}, UDP={Udp}).")]
    public static partial void LogTurnServerStarted(
        this ILogger logger,
        IPAddress address,
        int port,
        bool tcp,
        bool udp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnServerStopped",
        Level = LogLevel.Information,
        Message = "TURN server stopped.")]
    public static partial void LogTurnServerStopped(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnClientConnected",
        Level = LogLevel.Debug,
        Message = "TURN TCP client connected from {Remote}.")]
    public static partial void LogTurnClientConnected(
        this ILogger logger,
        IPEndPoint remote);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnTcpAcceptError",
        Level = LogLevel.Error,
        Message = "TURN TCP accept loop error.")]
    public static partial void LogTurnTcpAcceptError(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnStunParseFail",
        Level = LogLevel.Warning,
        Message = "Failed to parse STUN message from TCP client {Client}.")]
    public static partial void LogTurnStunParseFail(
        this ILogger logger,
        string client);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnTcpHandlerError",
        Level = LogLevel.Error,
        Message = "TURN TCP client handler error for {Client}.")]
    public static partial void LogTurnTcpHandlerError(
        this ILogger logger,
        string client,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnCleanupAllocation",
        Level = LogLevel.Debug,
        Message = "Cleaned up TCP allocation for {Client}.")]
    public static partial void LogTurnCleanupAllocation(
        this ILogger logger,
        string client);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnUdpReceiveError",
        Level = LogLevel.Error,
        Message = "TURN UDP receive loop error.")]
    public static partial void LogTurnUdpReceiveError(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnStunParseFailUdp",
        Level = LogLevel.Warning,
        Message = "Failed to parse STUN message from UDP client {Client}.")]
    public static partial void LogTurnStunParseFailUdp(
        this ILogger logger,
        string client);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnSendUdpResponseFailed",
        Level = LogLevel.Debug,
        Message = "Failed to send UDP response to {Endpoint}.")]
    public static partial void LogTurnSendUdpResponseFailed(
        this ILogger logger,
        IPEndPoint endpoint,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnMessageReceived",
        Level = LogLevel.Debug,
        Message = "TURN {Type} from {Client}.")]
    public static partial void LogTurnMessageReceived(
        this ILogger logger,
        STUNMessageTypesEnum type,
        string client);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnUnhandledStunMessage",
        Level = LogLevel.Warning,
        Message = "Unhandled STUN message type: {Type}.")]
    public static partial void LogTurnUnhandledStunMessage(
        this ILogger logger,
        STUNMessageTypesEnum type);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateRestCredRejected",
        Level = LogLevel.Warning,
        Message = "TURN Allocate: REST credential rejected from {Client}: {Reason}.")]
    public static partial void LogTurnAllocateRestCredRejected(
        this ILogger logger,
        string client,
        string reason);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateIntegrityFail",
        Level = LogLevel.Warning,
        Message = "TURN Allocate: integrity check failed from {Client}.")]
    public static partial void LogTurnAllocateIntegrityFail(
        this ILogger logger,
        string client);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateNoFreePort",
        Level = LogLevel.Warning,
        Message = "TURN Allocate: no free relay port in [{Min}..{Max}] for {Client}.")]
    public static partial void LogTurnAllocateNoFreePort(
        this ILogger logger,
        int min,
        int max,
        string client);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocationCreated",
        Level = LogLevel.Information,
        Message = "TURN allocation created for {Client}: relay port {Port}.")]
    public static partial void LogTurnAllocationCreated(
        this ILogger logger,
        string client,
        int port);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocationDeleted",
        Level = LogLevel.Information,
        Message = "TURN allocation deleted by refresh (lifetime=0) for {Client}.")]
    public static partial void LogTurnAllocationDeleted(
        this ILogger logger,
        string client);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionAdded",
        Level = LogLevel.Debug,
        Message = "TURN permission added: {Address} (expires in {Seconds}s).")]
    public static partial void LogTurnPermissionAdded(
        this ILogger logger,
        IPAddress address,
        int seconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnChannelBind",
        Level = LogLevel.Debug,
        Message = "TURN channel bind: 0x{Channel:X4} -> {Peer}.")]
    public static partial void LogTurnChannelBind(
        this ILogger logger,
        int channel,
        IPEndPoint peer);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnSendIndicationDropped",
        Level = LogLevel.Debug,
        Message = "TURN SendIndication dropped: no permission for {Peer}.")]
    public static partial void LogTurnSendIndicationDropped(
        this ILogger logger,
        IPEndPoint peer);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRelayUdpFailed",
        Level = LogLevel.Debug,
        Message = "Failed to relay UDP to {Peer}.")]
    public static partial void LogTurnRelayUdpFailed(
        this ILogger logger,
        IPEndPoint peer,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRelayChannelFailed",
        Level = LogLevel.Debug,
        Message = "Failed to relay channel data to {Peer}.")]
    public static partial void LogTurnRelayChannelFailed(
        this ILogger logger,
        IPEndPoint peer,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRelayPacketDropped",
        Level = LogLevel.Debug,
        Message = "TURN relay dropped packet from {Sender}: no permission.")]
    public static partial void LogTurnRelayPacketDropped(
        this ILogger logger,
        IPEndPoint sender);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnUdpRelayLoopEnded",
        Level = LogLevel.Debug,
        Message = "UDP relay loop ended for allocation {Id}.")]
    public static partial void LogTurnUdpRelayLoopEnded(
        this ILogger logger,
        string id,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocationExpired",
        Level = LogLevel.Information,
        Message = "TURN allocation expired and removed: {Id}.")]
    public static partial void LogTurnAllocationExpired(
        this ILogger logger,
        string id);

    // TurnClient logging
    [LoggerMessage(
        EventId = 0,
        EventName = "TurnNoServer",
        Level = LogLevel.Warning,
        Message = "No TURN server was available to allocate a relay endpoint.")]
    public static partial void LogTurnNoServer(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnEndPointNotAvailable",
        Level = LogLevel.Warning,
        Message = "The TURN server end point was not available for {Uri}.")]
    public static partial void LogTurnEndPointNotAvailable(
        this ILogger logger,
        STUNUri? uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateTimeout",
        Level = LogLevel.Warning,
        Message = "TURN allocate timed out.")]
    public static partial void LogTurnAllocateTimeout(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateMaxRetries",
        Level = LogLevel.Warning,
        Message = "TURN allocate max retries reached.")]
    public static partial void LogTurnAllocateMaxRetries(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateSendError",
        Level = LogLevel.Warning,
        Message = "TURN allocate send error {Result}.")]
    public static partial void LogTurnAllocateSendError(
        this ILogger logger,
        System.Net.Sockets.SocketError result);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateFailed",
        Level = LogLevel.Warning,
        Message = "TURN allocate failed to get a relay endpoint.")]
    public static partial void LogTurnAllocateFailed(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateSucceeded",
        Level = LogLevel.Information,
        Message = "TURN allocate succeeded, relay endpoint is {RelayEndPoint}.")]
    public static partial void LogTurnAllocateSucceeded(
        this ILogger logger,
        IPEndPoint? relayEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateSuccessResponse",
        Level = LogLevel.Information,
        Message = "TURN client received a success response for an Allocate request to {Uri} from {RemoteEP}.")]
    public static partial void LogTurnAllocateSuccessResponse(
        this ILogger logger,
        STUNUri? uri,
        IPEndPoint? remoteEp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateSuccessReceived",
        Level = LogLevel.Debug,
        Message = "TURN allocate success response received for ICE server check to {Uri}.")]
    public static partial void LogTurnAllocateSuccessReceived(
        this ILogger logger,
        STUNUri? uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateErrorResponse",
        Level = LogLevel.Warning,
        Message = "TURN client received an error response for an Allocate request to {Uri} from {RemoteEP}.")]
    public static partial void LogTurnAllocateErrorResponse(
        this ILogger logger,
        STUNUri? uri,
        IPEndPoint? remoteEp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateErrorCode",
        Level = LogLevel.Warning,
        Message = "TURN client error response code {ErrorCode} for an Allocate request to {Uri} from {RemoteEP}.")]
    public static partial void LogTurnAllocateErrorCode(
        this ILogger logger,
        int errorCode,
        STUNUri? uri,
        IPEndPoint? remoteEp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateAlternateResponse",
        Level = LogLevel.Warning,
        Message = "TURN client received an alternate respose for an Allocate request to {Uri}, changed server url to {ServerEndPoint}.")]
    public static partial void LogTurnAllocateAlternateResponse(
        this ILogger logger,
        STUNUri? uri,
        IPEndPoint? serverEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateErrorWithReason",
        Level = LogLevel.Warning,
        Message = "TURN client received an error response for an Allocate request to {Uri}, error {ErrorCode} {ReasonPhrase}.")]
    public static partial void LogTurnAllocateErrorWithReason(
        this ILogger logger,
        STUNUri? uri,
        int errorCode,
        string reasonPhrase);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateGenericError",
        Level = LogLevel.Warning,
        Message = "TURN client received an error response for an Allocate request to {Uri}.")]
    public static partial void LogTurnAllocateGenericError(
        this ILogger logger,
        STUNUri? uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionSuccessResponse",
        Level = LogLevel.Information,
        Message = "TURN client received a success response for a CreatePermission request to {Uri} from {RemoteEP}.")]
    public static partial void LogTurnPermissionSuccessResponse(
        this ILogger logger,
        STUNUri? uri,
        IPEndPoint? remoteEp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionLifetime",
        Level = LogLevel.Debug,
        Message = "TURN permission lifetime attribute value {LifetimeSeconds}s.")]
    public static partial void LogTurnPermissionLifetime(
        this ILogger logger,
        double lifetimeSeconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionDefaultLifetime",
        Level = LogLevel.Debug,
        Message = "TURN permission using default lifetime of {LifetimeSeconds}s.")]
    public static partial void LogTurnPermissionDefaultLifetime(
        this ILogger logger,
        double lifetimeSeconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnSchedulePermissionRefresh",
        Level = LogLevel.Information,
        Message = "Scheduling TURN create permission refresh for server {RelayEndPoint} and peer {Peer}, allocation expires in {RenewalMilliseconds}ms, renew at {RenewalTime}.")]
    public static partial void LogTurnSchedulePermissionRefresh(
        this ILogger logger,
        IPEndPoint? relayEndPoint,
        IPEndPoint peer,
        long renewalMilliseconds,
        DateTime renewalTime);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionErrorResponse",
        Level = LogLevel.Warning,
        Message = "TURN client received an error response for a Create Permission request to {Uri} from {RemoteEP}.")]
    public static partial void LogTurnPermissionErrorResponse(
        this ILogger logger,
        STUNUri? uri,
        IPEndPoint? remoteEp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionErrorCode",
        Level = LogLevel.Warning,
        Message = "TURN client error response code {ErrorCode} for a Create Permission request to {Uri} from {RemoteEP}.")]
    public static partial void LogTurnPermissionErrorCode(
        this ILogger logger,
        int errorCode,
        STUNUri? uri,
        IPEndPoint? remoteEp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionErrorWithReason",
        Level = LogLevel.Warning,
        Message = "TURN client received an error response for a Create Permission request to {Uri}, error {ErrorCode} {ReasonPhrase}.")]
    public static partial void LogTurnPermissionErrorWithReason(
        this ILogger logger,
        STUNUri? uri,
        int errorCode,
        string reasonPhrase);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionGenericError",
        Level = LogLevel.Warning,
        Message = "TURN client received an error response for a Create Permission request to {Uri}.")]
    public static partial void LogTurnPermissionGenericError(
        this ILogger logger,
        STUNUri? uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRefreshSuccessResponse",
        Level = LogLevel.Information,
        Message = "TURN client received a success response for a Refresh request to {Uri} from {RemoteEP}.")]
    public static partial void LogTurnRefreshSuccessResponse(
        this ILogger logger,
        STUNUri? uri,
        IPEndPoint? remoteEp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnUnrecognisedStunResponse",
        Level = LogLevel.Warning,
        Message = "An unrecognised STUN {MessageType} response for an ICE server check was received from {RemoteEndPoint}.")]
    public static partial void LogTurnUnrecognisedStunResponse(
        this ILogger logger,
        STUNMessageTypesEnum messageType,
        IPEndPoint? remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRtpChannelNotReady",
        Level = LogLevel.Warning,
        Message = "RTP channel is not set or closed, cannot schedule TURN Allocate refresh.")]
    public static partial void LogTurnRtpChannelNotReady(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateLifetime",
        Level = LogLevel.Debug,
        Message = "TURN allocate lifetime attribute value {LifetimeSeconds}s.")]
    public static partial void LogTurnAllocateLifetime(
        this ILogger logger,
        double lifetimeSeconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateDefaultLifetime",
        Level = LogLevel.Debug,
        Message = "TURN allocate using default lifetime of {LifetimeSeconds}s.")]
    public static partial void LogTurnAllocateDefaultLifetime(
        this ILogger logger,
        double lifetimeSeconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnScheduleAllocationRefresh",
        Level = LogLevel.Information,
        Message = "Scheduling TURN client allocated refresh for server {RelayEndPoint} at {Uri}, allocation expires at {Expiry}.")]
    public static partial void LogTurnScheduleAllocationRefresh(
        this ILogger logger,
        IPEndPoint? relayEndPoint,
        STUNUri? uri,
        DateTime expiry);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRtpChannelNotReadyAllocate",
        Level = LogLevel.Warning,
        Message = "RTP channel is not set or closed, cannot send TURN Allocate request.")]
    public static partial void LogTurnRtpChannelNotReadyAllocate(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnAllocateSendErrorDetail",
        Level = LogLevel.Warning,
        Message = "Error sending TURN Allocate request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.")]
    public static partial void LogTurnAllocateSendErrorDetail(
        this ILogger logger,
        int outstandingRequestsSent,
        STUNUri? uri,
        IPEndPoint? serverEndPoint,
        System.Net.Sockets.SocketError sendResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRtpChannelNotReadyPermission",
        Level = LogLevel.Warning,
        Message = "RTP channel is not set or closed, cannot send TURN Create Permissions request.")]
    public static partial void LogTurnRtpChannelNotReadyPermission(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnPermissionSendErrorDetail",
        Level = LogLevel.Warning,
        Message = "Error sending TURN Create Permissions request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.")]
    public static partial void LogTurnPermissionSendErrorDetail(
        this ILogger logger,
        int outstandingRequestsSent,
        STUNUri? uri,
        IPEndPoint? serverEndPoint,
        System.Net.Sockets.SocketError sendResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRefreshSendErrorDetail",
        Level = LogLevel.Warning,
        Message = "Error sending TURN Refresh request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.")]
    public static partial void LogTurnRefreshSendErrorDetail(
        this ILogger logger,
        int outstandingRequestsSent,
        STUNUri? uri,
        IPEndPoint? serverEndPoint,
        System.Net.Sockets.SocketError sendResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRelayReceiveStopped",
        Level = LogLevel.Debug,
        Message = "TURN relay receive loop stopped (socket disposed).")]
    public static partial void LogTurnRelayReceiveStopped(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnRelayReceiveSocketError",
        Level = LogLevel.Debug,
        Message = "TURN relay receive socket error (expected during shutdown). ErrorCode={ErrorCode}.")]
    public static partial void LogTurnRelayReceiveSocketError(
        this ILogger logger,
        System.Net.Sockets.SocketError errorCode);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnTcpHandlerOperationCancelled",
        Level = LogLevel.Debug,
        Message = "TURN TCP handler stopped due to cancellation. ClientId={ClientId}.")]
    public static partial void LogTurnTcpHandlerOperationCancelled(
        this ILogger logger,
        string clientId);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnTcpHandlerIoError",
        Level = LogLevel.Debug,
        Message = "TURN TCP handler encountered I/O error (expected during shutdown). ClientId={ClientId}.")]
    public static partial void LogTurnTcpHandlerIoError(
        this ILogger logger,
        string clientId);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnServerShutdownCleanupError",
        Level = LogLevel.Warning,
        Message = "Error during TURN server shutdown cleanup: {Operation}.")]
    public static partial void LogTurnServerShutdownCleanupError(
        this ILogger logger,
        string operation);

    [LoggerMessage(
        EventId = 0,
        EventName = "TurnHandlerSendError",
        Level = LogLevel.Warning,
        Message = "Error sending to TURN client.")]
    public static partial void LogTurnHandlerError(
        this ILogger logger,
        Exception exception);
}
