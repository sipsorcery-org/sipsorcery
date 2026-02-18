  //-----------------------------------------------------------------------------
// Filename: TurnClient.cs
//
// Description: TURN client implementation. Initial use case is to allocate a relay
// socket on a TURN server for use on a SIP call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 14 Aug 2025  Aaron Clauson   Created, Wexford, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Digests;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class TurnClient
{
    private const int MAX_ALLOCATE_ATTEMPTS = 5;

    /// <summary>
    /// The lifetime value used in refresh request.
    /// </summary>
    private const uint ALLOCATION_TIME_TO_EXPIRY_SECONDS = 600;

    private const int ALLOCATE_RETRY_PERIOD_MILLISECONDS = 1000;

    private const int ALLOCATE_DEFAULT_TIMEOUT_MILLISECONDS = 5000;

    private const int ALLOCATE_DEFAULT_LIFETIME_SECONDS = 600;

    private const int PERMISSION_DEFAULT_LIFETIME_SECONDS = 300;

    private const int GRACE_RENEWAL_SECONDS = 10;

    private static readonly ILogger logger = Log.Logger;

    private readonly IceServerResolver _iceServerResolver = new IceServerResolver();

    private IceServer _iceServer;
    public IceServer IceServer => _iceServer;

    private RTPChannel _rtpChannel;

    private bool _allocateRequestSent = false;
    private int _allocateRetries = 0;

    private IPEndPoint _peerEndPoint;

    private Timer _allocateRenewalTimer = null;

    private Timer _permissionsRenewalTimer = null;

    /// <summary>
    /// This event gets fired when a STUN message is sent by this channel.
    /// The event is for diagnostic purposes only.
    /// Parameters:
    ///  - STUNMessage: The STUN message that was sent.
    ///  - IPEndPoint: The remote end point the STUN message was sent to.
    ///  - bool: True if the message was sent via a TURN server relay.
    /// </summary>
    public event Action<STUNMessage, IPEndPoint, bool> OnStunMessageSent;

    public TurnClient(string turnServerUrl)
    {
        _iceServerResolver.InitialiseIceServers([RTCIceServer.Parse(turnServerUrl)], RTCIceTransportPolicy.all);
    }

    public void SetRtpChannel(RTPChannel rtpChannel)
    {
        _rtpChannel = rtpChannel;
        _rtpChannel.OnStunMessageReceived += GotStunResponse;
        _rtpChannel.OnClosed += OnClosed;
    }

    /// <summary>
    /// Allocates (or returns cached) TURN relayed endpoint.
    /// </summary>
    public async Task<IPEndPoint> GetRelayEndPoint(int timeoutMilliseconds = ALLOCATE_DEFAULT_TIMEOUT_MILLISECONDS, CancellationToken cancellationToken = default)
    {
        if (_iceServer?.RelayEndPoint != null)
        {
            // Already resolved and allocated.
            return _iceServer.RelayEndPoint;
        }

        if (_iceServer == null)
        {
            await _iceServerResolver.WaitForAllIceServersAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));

            _iceServer = _iceServerResolver.IceServers.Select(x => x.Value).FirstOrDefault();
        }

        if(_iceServer == null)
        {
            logger.LogWarning("No TURN server was available to allocate a relay endpoint.");
            return null;
        }
        else if (_iceServer.ServerEndPoint == null)
        {
            logger.LogWarning("The TURN server end point was not available for {uri}.", _iceServer?.Uri);
            return null;
        }

        var start = DateTime.Now;

        while (_iceServer.RelayEndPoint == null && !cancellationToken.IsCancellationRequested)
        {
            if ((int)DateTime.Now.Subtract(start).TotalMilliseconds > timeoutMilliseconds)
            {
                logger.LogWarning("TURN allocate timed out.");
                break;
            }

            if (!_allocateRequestSent ||
                (DateTime.Now.Subtract(_iceServer.LastRequestSentAt).TotalMilliseconds > 500 &&
                 _iceServer.LastResponseReceivedAt < _iceServer.LastRequestSentAt))
            {
                if (_allocateRetries >= MAX_ALLOCATE_ATTEMPTS)
                {
                    logger.LogWarning("TURN allocate max retries reached.");
                    break;
                }

                var sendRes = SendTurnAllocateRequest(_iceServer);

                _allocateRequestSent = true;
                _allocateRetries++;

                if (sendRes != SocketError.Success)
                {
                    logger.LogWarning("TURN allocate send error {Result}.", sendRes);
                }
            }

            await Task.Delay(ALLOCATE_RETRY_PERIOD_MILLISECONDS, cancellationToken).ConfigureAwait(false);
        }

        if(_iceServer.RelayEndPoint == null)
        {
            logger.LogWarning("TURN allocate failed to get a relay endpoint.");
        }
        else
        {
            logger.LogInformation("TURN allocate succeeded, relay endpoint is {relayEndPoint}.", _iceServer.RelayEndPoint);
        }

        return _iceServer?.RelayEndPoint;
    }

    public SocketError CreatePermission(IPEndPoint remoteEndPoint)
    {
        _peerEndPoint = remoteEndPoint;

        return SendTurnCreatePermissionsRequest(_iceServer, remoteEndPoint);
    }

    /// <summary>
    /// Handler for a STUN response received in response to an ICE server connectivity check.
    /// Note that no STUN requests are expected to be received from an ICE server during the initial
    /// connection to an ICE server. Requests will only arrive if a TURN relay is used and data
    /// indications arrive but this will be at a later stage.
    /// </summary>
    /// <param name="stunResponse">The STUN response received.</param>
    /// <param name="remoteEndPoint">The remote end point the STUN response was received from.</param>
    /// <returns>True if the STUN response resulted in new ICE candidates being available (which
    /// will be either a "server reflexive" or "relay" candidate.</returns>
    private void GotStunResponse(STUNMessage stunResponse, IPEndPoint remoteEndPoint, bool wasRelayed)
    {
        string txID = Encoding.ASCII.GetString(stunResponse.Header.TransactionId);

        // Ignore responses to old requests on the assumption they are retransmits.
        if (_iceServer.TransactionID == txID)
        {
            // The STUN response is for a check sent to an ICE server.
            _iceServer.LastResponseReceivedAt = DateTime.Now;
            _iceServer.OutstandingRequestsSent = 0;

            if (stunResponse.Header.MessageType == STUNMessageTypesEnum.AllocateSuccessResponse)
            {
                logger.LogInformation("TURN client received a success response for an Allocate request to {Uri} from {remoteEP}.", _iceServer.Uri, remoteEndPoint);

                _iceServer.ErrorResponseCount = 0;

                logger.LogDebug("TURN allocate success response received for ICE server check to {Uri}.", _iceServer.Uri);

                var mappedAddrAttr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORMappedAddress).FirstOrDefault();

                if (mappedAddrAttr != null)
                {
                    _iceServer.ServerReflexiveEndPoint = (mappedAddrAttr as STUNXORAddressAttribute).GetIPEndPoint();
                }

                var mappedRelayAddrAttr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress).FirstOrDefault();

                if (mappedRelayAddrAttr != null)
                {
                    _iceServer.RelayEndPoint = (mappedRelayAddrAttr as STUNXORAddressAttribute).GetIPEndPoint();
                }

                var lifetime = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Lifetime);

                ScheduleAllocateRefresh(lifetime);
            }
            else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.AllocateErrorResponse)
            {
                logger.LogWarning("TURN client received an error response for an Allocate request to {Uri} from {remoteEP}.", _iceServer.Uri, remoteEndPoint);

                _iceServer.ErrorResponseCount++;

                if (stunResponse.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode))
                {
                    STUNErrorCodeAttribute errCodeAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode) as STUNErrorCodeAttribute;
                    STUNAddressAttribute alternateServerAttribute = alternateServerAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.AlternateServer) as STUNAddressAttribute;

                    if (errCodeAttribute.ErrorCode == IceServer.STUN_UNAUTHORISED_ERROR_CODE || errCodeAttribute.ErrorCode == IceServer.STUN_STALE_NONCE_ERROR_CODE)
                    {
                        logger.LogWarning("TURN client error response code {errorCode} for an Allocate request to {Uri} from {remoteEP}.", errCodeAttribute.ErrorCode, _iceServer.Uri, remoteEndPoint);

                        // Set the authentication properties authenticate.
                        SetAuthenticationFields(stunResponse);

                        // Set a new transaction ID.
                        _iceServer.GenerateNewTransactionID();

                        _iceServer.ErrorResponseCount = 1;

                        SendTurnAllocateRequest(_iceServer);
                    }
                    else if (alternateServerAttribute != null)
                    {
                        _iceServer.ServerEndPoint = new IPEndPoint(alternateServerAttribute.Address, alternateServerAttribute.Port);

                        logger.LogWarning("TURN client received an alternate respose for an Allocate request to {Uri}, changed server url to {ServerEndPoint}.", _iceServer.Uri, _iceServer.ServerEndPoint);

                        // Set a new transaction ID.
                        _iceServer.GenerateNewTransactionID();

                        _iceServer.ErrorResponseCount = 1;
                    }
                    else
                    {
                        logger.LogWarning("TURN client received an error response for an Allocate request to {Uri}, error {ErrorCode} {ReasonPhrase}.", _iceServer.Uri, errCodeAttribute.ErrorCode, errCodeAttribute.ReasonPhrase);
                    }
                }
                else
                {
                    logger.LogWarning("TURN client received an error response for an Allocate request to {Uri}.", _iceServer.Uri);
                }
            }
            else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.CreatePermissionSuccessResponse)
            {
                logger.LogInformation("TURN client received a success response for a CreatePermission request to {Uri} from {remoteEP}.", _iceServer.Uri, remoteEndPoint);

                var permissionLifetime = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Lifetime);
                TimeSpan permissionDuration = TimeSpan.FromSeconds(PERMISSION_DEFAULT_LIFETIME_SECONDS);

                if (permissionLifetime != null)
                {
                    permissionDuration = TimeSpan.FromSeconds(BitConverter.ToUInt32(permissionLifetime.Value.FluentReverse().ToArray(), 0));

                    logger.LogDebug("TURN permission lifetime attribute value {lifetimeSeconds}s.", permissionDuration.TotalSeconds);
                }
                else
                {
                    logger.LogDebug("TURN permission using default lifetime of {lifetimeSeconds}s.", PERMISSION_DEFAULT_LIFETIME_SECONDS);
                }

                var renewalTime = DateTime.Now.Add(permissionDuration).Subtract(TimeSpan.FromSeconds(GRACE_RENEWAL_SECONDS));
                var renewalMilliseconds = Convert.ToInt32(renewalTime.Subtract(DateTime.Now).TotalMilliseconds);

                logger.LogInformation("Scheduling TURN create permission refresh for server {RelayEndPoint} and peer {peer}, allocation expires in {renewalMilliseconds}ms, renew at {renewalTime}.", _iceServer.RelayEndPoint, _peerEndPoint, renewalMilliseconds, renewalTime.ToString("o"));

                _permissionsRenewalTimer = new Timer((e) =>
                {
                    _iceServer.GenerateNewTransactionID();
                    SendTurnCreatePermissionsRequest(_iceServer, _peerEndPoint);
                }, null, renewalMilliseconds, -1);
            }
            else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.CreatePermissionErrorResponse)
            {
                logger.LogWarning("TURN client received an error response for a Create Permission request to {Uri} from {remoteEP}.", _iceServer.Uri, remoteEndPoint);

                _iceServer.ErrorResponseCount++;

                if (stunResponse.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode))
                {
                    STUNErrorCodeAttribute errCodeAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode) as STUNErrorCodeAttribute;

                    if (errCodeAttribute.ErrorCode == IceServer.STUN_UNAUTHORISED_ERROR_CODE || errCodeAttribute.ErrorCode == IceServer.STUN_STALE_NONCE_ERROR_CODE)
                    {
                        logger.LogWarning("TURN client error response code {errorCode} for a Create Permission request to {Uri} from {remoteEP}.", errCodeAttribute.ErrorCode, _iceServer.Uri, remoteEndPoint);

                        // Set the authentication properties authenticate.
                        SetAuthenticationFields(stunResponse);

                        // Set a new transaction ID.
                        _iceServer.GenerateNewTransactionID();

                        _iceServer.ErrorResponseCount = 1;

                        SendTurnCreatePermissionsRequest(_iceServer, _peerEndPoint);
                    }
                    else
                    {
                        logger.LogWarning("TURN client received an error response for a Create Permission request to {Uri}, error {ErrorCode} {ReasonPhrase}.", _iceServer.Uri, errCodeAttribute.ErrorCode, errCodeAttribute.ReasonPhrase);
                    }
                }
                else
                {
                    logger.LogWarning("TURN client received an error response for a Create Permission request to {Uri}.", _iceServer.Uri);
                }
            }
            else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.RefreshSuccessResponse)
            {
                logger.LogInformation("TURN client received a success response for a Refresh request to {Uri} from {remoteEP}.", _iceServer.Uri, remoteEndPoint);

                ScheduleAllocateRefresh(stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Lifetime));
            }
            else
            {
                logger.LogWarning("An unrecognised STUN {MessageType} response for an ICE server check was received from {RemoteEndPoint}.", stunResponse.Header.MessageType, remoteEndPoint);
                _iceServer.ErrorResponseCount++;
            }
        }
    }

    private void ScheduleAllocateRefresh(STUNAttribute lifetimeAttribute)
    {
        if(_rtpChannel == null || _rtpChannel.IsClosed)
        {
            logger.LogWarning("RTP channel is not set or closed, cannot schedule TURN Allocate refresh.");
            return;
        }

        if (lifetimeAttribute != null)
        {
            var lifetimeSpan = TimeSpan.FromSeconds(BitConverter.ToUInt32(lifetimeAttribute.Value.FluentReverse().ToArray(), 0));

            logger.LogDebug("TURN allocate lifetime attribute value {lifetimeSeconds}s.", lifetimeSpan.TotalSeconds);

            _iceServer.TurnTimeToExpiry = DateTime.Now + lifetimeSpan;
        }
        else
        {
            logger.LogDebug("TURN allocate using default lifetime of {lifetimeSeconds}s.", ALLOCATE_DEFAULT_LIFETIME_SECONDS);

            _iceServer.TurnTimeToExpiry = DateTime.Now + TimeSpan.FromSeconds(ALLOCATE_DEFAULT_LIFETIME_SECONDS);
        }

        var renewalMilliseconds = Convert.ToInt32(_iceServer.TurnTimeToExpiry.Subtract(DateTime.Now).Subtract(TimeSpan.FromSeconds(GRACE_RENEWAL_SECONDS)).TotalMilliseconds);
        var renewalTime = _iceServer.TurnTimeToExpiry;

        logger.LogInformation("Scheduling TURN client allocated refresh for server {RelayEndPoint} at {Uri}, allocation expires at {Expiry}.",
            _iceServer.RelayEndPoint, _iceServer.Uri, renewalTime.ToString("o"));

        _allocateRenewalTimer = new Timer((e) =>
        {
            _iceServer.GenerateNewTransactionID();
            SendTurnRefreshRequest(_iceServer);
        }, null, renewalMilliseconds, -1);
    }

    /// <summary>
    /// Extracts the fields required for authentication from a STUN error response.
    /// </summary>
    /// <param name="stunResponse">The STUN authentication required error response.</param>
    private void SetAuthenticationFields(STUNMessage stunResponse)
    {
        // Set the authentication properties authenticate.
        var nonceAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Nonce);
        _iceServer.Nonce = nonceAttribute?.Value;

        var realmAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Realm);
        _iceServer.Realm = realmAttribute?.Value;
    }

    /// <summary>
    /// Sends an allocate request to a TURN server.
    /// </summary>
    /// <param name="iceServer">The TURN server to send the request to.</param>
    /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
    private SocketError SendTurnAllocateRequest(IceServer iceServer)
    {
        if (_rtpChannel == null || _rtpChannel.IsClosed)
        {
            logger.LogWarning("RTP channel is not set or closed, cannot send TURN Allocate request.");
            return SocketError.NotConnected;
        }

        iceServer.OutstandingRequestsSent += 1;
        iceServer.LastRequestSentAt = DateTime.Now;

        STUNMessage allocateRequest = new STUNMessage(STUNMessageTypesEnum.Allocate);
        allocateRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
        allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.RequestedTransport, STUNAttributeConstants.UdpTransportType));
        allocateRequest.Attributes.Add(
            new STUNAttribute(STUNAttributeTypesEnum.RequestedAddressFamily,
            iceServer.ServerEndPoint.AddressFamily == AddressFamily.InterNetwork ?
            STUNAttributeConstants.IPv4AddressFamily : STUNAttributeConstants.IPv6AddressFamily));

        byte[] allocateReqBytes = null;

        if (iceServer.Nonce != null && iceServer.Realm != null && iceServer._username != null && iceServer._password != null)
        {
            allocateReqBytes = GetAuthenticatedStunRequest(allocateRequest, iceServer._username, iceServer.Realm, iceServer._password, iceServer.Nonce);
        }
        else
        {
            allocateReqBytes = allocateRequest.ToByteBuffer(null, false);
        }

        var sendResult = _rtpChannel.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, allocateReqBytes);

        if (sendResult != SocketError.Success)
        {
            logger.LogWarning("Error sending TURN Allocate request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.",
                iceServer.OutstandingRequestsSent, iceServer._uri, iceServer.ServerEndPoint, sendResult);
        }
        else
        {
            OnStunMessageSent?.Invoke(allocateRequest, iceServer.ServerEndPoint, false);
        }

        return sendResult;
    }

    /// <summary>
    /// Sends a create permissions request to a TURN server for a peer end point.
    /// </summary>
    /// <param name="iceServer">The ICE server to send the request to.</param>
    /// <param name="peerEndPoint">The peer end point to request the channel bind for.</param>
    /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
    /// <remarks>
    /// A TURN CreatePermission request is how a client tells the TURN server which peer IP addresses it is
    /// allowed to exchange UDP with on a given allocation. The server installs (or refreshes) a “permission”
    /// for each peer IP you include, and will only relay traffic to/from those peers; packets from any other
    /// IPs are silently dropped. This prevents the relay from being abused to send data to arbitrary hosts.
    /// </remarks>
    private SocketError SendTurnCreatePermissionsRequest(IceServer iceServer, IPEndPoint peerEndPoint)
    {
        if(_rtpChannel == null || _rtpChannel.IsClosed)
        {
            logger.LogWarning("RTP channel is not set or closed, cannot send TURN Create Permissions request.");
            return SocketError.NotConnected;
        }

        STUNMessage permissionsRequest = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
        permissionsRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
        permissionsRequest.Attributes.Add(new STUNXORAddressAttribute(STUNAttributeTypesEnum.XORPeerAddress, peerEndPoint.Port, peerEndPoint.Address, permissionsRequest.Header.TransactionId));

        byte[] createPermissionReqBytes = null;

        if (iceServer.Nonce != null && iceServer.Realm != null && iceServer._username != null && iceServer._password != null)
        {
            createPermissionReqBytes = GetAuthenticatedStunRequest(permissionsRequest, iceServer._username, iceServer.Realm, iceServer._password, iceServer.Nonce);
        }
        else
        {
            createPermissionReqBytes = permissionsRequest.ToByteBuffer(null, false);
        }

        var sendResult = _rtpChannel.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, createPermissionReqBytes);

        if (sendResult != SocketError.Success)
        {
            logger.LogWarning("Error sending TURN Create Permissions request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.",
                iceServer.OutstandingRequestsSent, iceServer._uri, iceServer.ServerEndPoint, sendResult);
        }
        else
        {
            OnStunMessageSent?.Invoke(permissionsRequest, iceServer.ServerEndPoint, false);
        }

        return sendResult;
    }

    /// <summary>
    /// Sends a refresh request to a TURN server.
    /// </summary>
    /// <param name="iceServer">The TURN server to send the request to.</param>
    /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
    /// <remarks>
    /// A TURN Refresh request is how a client keeps an existing allocation alive—or deletes it.
    /// It updates the allocation’s time-to-expiry, or, if you set LIFETIME=0, it tears the allocation down immediately.
    /// </remarks>
    private SocketError SendTurnRefreshRequest(IceServer iceServer)
    {
        iceServer.OutstandingRequestsSent += 1;
        iceServer.LastRequestSentAt = DateTime.Now;

        STUNMessage allocateRequest = new STUNMessage(STUNMessageTypesEnum.Refresh);
        allocateRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
        allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, ALLOCATION_TIME_TO_EXPIRY_SECONDS));

        allocateRequest.Attributes.Add(
            new STUNAttribute(STUNAttributeTypesEnum.RequestedAddressFamily,
            iceServer.ServerEndPoint.AddressFamily == AddressFamily.InterNetwork ?
            STUNAttributeConstants.IPv4AddressFamily : STUNAttributeConstants.IPv6AddressFamily));

        byte[] allocateReqBytes = null;

        if (iceServer.Nonce != null && iceServer.Realm != null && iceServer._username != null && iceServer._password != null)
        {
            allocateReqBytes = GetAuthenticatedStunRequest(allocateRequest, iceServer._username, iceServer.Realm, iceServer._password, iceServer.Nonce);
        }
        else
        {
            allocateReqBytes = allocateRequest.ToByteBuffer(null, false);
        }

        var sendResult = _rtpChannel.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, allocateReqBytes);

        if (sendResult != SocketError.Success)
        {
            logger.LogWarning("Error sending TURN Refresh request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.",
                iceServer.OutstandingRequestsSent, iceServer._uri, iceServer.ServerEndPoint, sendResult);
        }
        else
        {
            OnStunMessageSent?.Invoke(allocateRequest, iceServer.ServerEndPoint, false);
        }

        return sendResult;
    }

    /// <summary>
    /// Adds the authentication fields to a STUN request.
    /// </summary>
    /// <returns>The serialised STUN request.</returns>
    private byte[] GetAuthenticatedStunRequest(STUNMessage stunRequest, string username, byte[] realm, string password, byte[] nonce)
    {
        stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, nonce));
        stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm, realm));
        stunRequest.AddUsernameAttribute(username);

        // See https://tools.ietf.org/html/rfc5389#section-15.4
        string key = $"{username}:{Encoding.UTF8.GetString(realm)}:{password}";
        var buffer = Encoding.UTF8.GetBytes(key);
        var md5Digest = new MD5Digest();
        var hash = new byte[md5Digest.GetDigestSize()];

        md5Digest.BlockUpdate(buffer, 0, buffer.Length);
        md5Digest.DoFinal(hash, 0);

        return stunRequest.ToByteBuffer(hash, true);
    }

    private void OnClosed(string closeReason)
    {
        if (_rtpChannel != null)
        {
            _rtpChannel.OnStunMessageReceived -= GotStunResponse;
            _rtpChannel.OnClosed -= OnClosed;
        }

        _allocateRenewalTimer?.Dispose();
        _permissionsRenewalTimer?.Dispose();
    }
}
