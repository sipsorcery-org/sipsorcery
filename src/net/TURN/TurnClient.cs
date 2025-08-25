//-----------------------------------------------------------------------------
// Filename: TurnClient.cs
//
// Description: TURN client implementation. Initial use case is to allocate a socket
// on a TURN server.
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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Digests;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class TurnClient
{
    /// <summary>
    /// The lifetime value used in refresh request.
    /// </summary>
    public static uint ALLOCATION_TIME_TO_EXPIRY_VALUE = 600;

    private static readonly ILogger logger = Log.Logger;

    private readonly IceServer _iceServer;
    private readonly RTPChannel _rtpChannel;

    public Dictionary<STUNUri, Socket> RtpTcpSocketByUri { get; private set; } = new Dictionary<STUNUri, Socket>();

    private Dictionary<STUNUri, IceTcpReceiver> m_rtpTcpReceiverByUri = new Dictionary<STUNUri, IceTcpReceiver>();

    private bool _allocateRequestSent = false;
    private int _allocateRetries = 0;

    //private bool m_tcpRtpReceiverStarted = false;

    /// <summary>
    /// This event gets fired when a STUN message is received by this channel.
    /// The event is for diagnostic purposes only.
    /// Parameters:
    ///  - STUNMessage: The received STUN message.
    ///  - IPEndPoint: The remote end point the STUN message was received from.
    ///  - bool: True if the message was received via a TURN server relay.
    /// </summary>
    //public event Action<STUNMessage, IPEndPoint, bool> OnStunMessageReceived;

    /// <summary>
    /// This event gets fired when a STUN message is sent by this channel.
    /// The event is for diagnostic purposes only.
    /// Parameters:
    ///  - STUNMessage: The STUN message that was sent.
    ///  - IPEndPoint: The remote end point the STUN message was sent to.
    ///  - bool: True if the message was sent via a TURN server relay.
    /// </summary>
    public event Action<STUNMessage, IPEndPoint, bool> OnStunMessageSent;

    public TurnClient(IceServer iceServer, RTPChannel rtpChannel)
    {
        _iceServer = iceServer;
        _rtpChannel = rtpChannel;

        _rtpChannel.OnRTPDataReceived += OnRTPPacketReceived;
    }

    /// <summary>
    /// Allocates (or returns cached) TURN relayed endpoint.
    /// </summary>
    public async Task<IPEndPoint> GetRelayEndPoint(int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        if (_iceServer.RelayEndPoint != null)
        {
            return _iceServer.RelayEndPoint;
        }

        // DNS resolution if required.
        if (_iceServer.ServerEndPoint == null)
        {
            logger.LogWarning("The TURN server end point was not available for {uri}. Has the IceServerResolver been triggered?", _iceServer.Uri);
            return null;
        }

        var start = DateTime.Now;
        while (_iceServer.RelayEndPoint == null && !cancellationToken.IsCancellationRequested)
        {
            if ((int)DateTime.Now.Subtract(start).TotalMilliseconds > timeoutMs)
            {
                logger.LogWarning("TURN allocate timed out.");
                break;
            }

            if (!_allocateRequestSent ||
                (DateTime.Now.Subtract(_iceServer.LastRequestSentAt).TotalMilliseconds > 500 &&
                 _iceServer.LastResponseReceivedAt < _iceServer.LastRequestSentAt))
            {
                if (_allocateRetries >= IceServer.MAX_REQUESTS)
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

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return _iceServer.RelayEndPoint;
    }

    /// <summary>
    /// Event handler for packets received on the RTP UDP socket. This channel will detect STUN messages
    /// and extract STUN messages to deal with ICE connectivity checks and TURN relays.
    /// </summary>
    /// <param name="localPort">The local port it was received on.</param>
    /// <param name="remoteEndPoint">The remote end point of the sender.</param>
    /// <param name="packet">The raw packet received (note this may not be RTP if other protocols are being multiplexed).</param>
    private void OnRTPPacketReceived(int localPort, IPEndPoint remoteEndPoint, byte[] packet)
    {
        if (packet?.Length > 0)
        {
            //bool wasRelayed = false;

            if (packet[0] == 0x00 && packet[1] == 0x17)
            {
                //wasRelayed = true;

                // TURN data indication. Extract the data payload and adjust the end point.
                var dataIndication = STUNMessage.ParseSTUNMessage(packet, packet.Length);
                var dataAttribute = dataIndication.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.Data).FirstOrDefault();
                packet = dataAttribute?.Value;

                var peerAddrAttribute = dataIndication.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORPeerAddress).FirstOrDefault();
                remoteEndPoint = (peerAddrAttribute as STUNXORAddressAttribute)?.GetIPEndPoint();
            }

            //base.LastRtpDestination = remoteEndPoint;

            if (packet[0] == 0x00 || packet[0] == 0x01)
            {
                // STUN packet.
                logger.LogDebug("STUN packet received from {RemoteEndPoint}.", remoteEndPoint);
                var stunMessage = STUNMessage.ParseSTUNMessage(packet, packet.Length);
                GotStunResponse(stunMessage, remoteEndPoint);
            }
            else
            {
                // If not STUN or TURN ignore. The default RTP channel handler will deal with.
                //OnRTPDataReceived?.Invoke(localPort, remoteEndPoint, packet);
            }
        }
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
    private void GotStunResponse(STUNMessage stunResponse, IPEndPoint remoteEndPoint)
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
                logger.LogWarning("TURN client received a success response for an Allocate request to {Uri} from {remoteEP}.", _iceServer.Uri, remoteEndPoint);

                _iceServer.ErrorResponseCount = 0;

                // If the relay end point is set then this connection check has already been completed.
                //if (RelayEndPoint == null)
                //{
                //    logger.LogDebug("TURN allocate success response received for ICE server check to {Uri}.", _uri);

                //    var mappedAddrAttr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORMappedAddress).FirstOrDefault();

                //    if (mappedAddrAttr != null)
                //    {
                //        ServerReflexiveEndPoint = (mappedAddrAttr as STUNXORAddressAttribute).GetIPEndPoint();
                //    }

                //    var mappedRelayAddrAttr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress).FirstOrDefault();

                //    if (mappedRelayAddrAttr != null)
                //    {
                //        RelayEndPoint = (mappedRelayAddrAttr as STUNXORAddressAttribute).GetIPEndPoint();
                //    }

                //    var lifetime = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Lifetime);

                //    if (lifetime != null)
                //    {
                //        TurnTimeToExpiry = DateTime.Now +
                //                           TimeSpan.FromSeconds(BitConverter.ToUInt32(lifetime.Value.Reverse().ToArray(), 0));
                //    }
                //    else
                //    {
                //        TurnTimeToExpiry = DateTime.Now +
                //                           TimeSpan.FromSeconds(3600);
                //    }
                //}
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
            else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.RefreshSuccessResponse)
            {
                //_iceServer.ErrorResponseCount = 0;

                //logger.LogDebug("STUN binding success response received for ICE server check to {Uri}.", _uri);

                //var lifetime = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Lifetime);

                //if (lifetime != null)
                //{
                //    TurnTimeToExpiry = DateTime.Now +
                //                       TimeSpan.FromSeconds(BitConverter.ToUInt32(lifetime.Value.Reverse().ToArray(), 0));
                //}
            }
            else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.RefreshErrorResponse)
            {
                //_iceServer.ErrorResponseCount++;

                //if (stunResponse.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode))
                //{
                //    var errCodeAttribute = stunResponse.Attributes.First(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode) as STUNErrorCodeAttribute;

                //    if (errCodeAttribute.ErrorCode == STUN_UNAUTHORISED_ERROR_CODE || errCodeAttribute.ErrorCode == STUN_STALE_NONCE_ERROR_CODE)
                //    {
                //        SetAuthenticationFields(stunResponse);

                //        // Set a new transaction ID.
                //        GenerateNewTransactionID();
                //    }
                //    else
                //    {
                //        logger.LogWarning("ICE session received an error response for a Refresh request to {Uri}, error {ErrorCode} {ReasonPhrase}.", _uri, errCodeAttribute.ErrorCode, errCodeAttribute.ReasonPhrase);
                //    }
                //}
                //else
                //{
                //    logger.LogWarning("STUN binding error response received for ICE server check to {Uri}.", _uri);
                //    // The STUN response is for a check sent to an ICE server.
                //    _iceServer.Error = SocketError.ConnectionRefused;
                //}
            }
            else
            {
                logger.LogWarning("An unrecognised STUN {MessageType} response for an ICE server check was received from {RemoteEndPoint}.", stunResponse.Header.MessageType, remoteEndPoint);
                _iceServer.ErrorResponseCount++;
            }
        }
    }

    /// <summary>
    /// Extracts the fields required for authentication from a STUN error response.
    /// </summary>
    /// <param name="stunResponse">The STUN authentication required error response.</param>
    internal void SetAuthenticationFields(STUNMessage stunResponse)
    {
        // Set the authentication properties authenticate.
        //var nonceAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Nonce);
        //Nonce = nonceAttribute?.Value;

        //var realmAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Realm);
        //Realm = realmAttribute?.Value;
    }

    /// <summary>
    /// Sends an allocate request to a TURN server.
    /// </summary>
    /// <param name="iceServer">The TURN server to send the request to.</param>
    /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
    private SocketError SendTurnAllocateRequest(IceServer iceServer)
    {
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

        var sendResult = iceServer.Protocol == ProtocolType.Tcp ?
                            SendOverTCP(iceServer, allocateReqBytes) :
                            _rtpChannel.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, allocateReqBytes);

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
    /// <param name="transactionID">The transaction ID to set on the request. This
    /// gets used to match responses back to the sender.</param>
    /// <param name="iceServer">The ICE server to send the request to.</param>
    /// <param name="peerEndPoint">The peer end point to request the channel bind for.</param>
    /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
    /// <remarks>
    /// A TURN CreatePermission request is how a client tells the TURN server which peer IP addresses it is
    /// allowed to exchange UDP with on a given allocation. The server installs (or refreshes) a “permission”
    /// for each peer IP you include, and will only relay traffic to/from those peers; packets from any other
    /// IPs are silently dropped. This prevents the relay from being abused to send data to arbitrary hosts.
    /// </remarks>
    private SocketError SendTurnCreatePermissionsRequest(string transactionID, IceServer iceServer, IPEndPoint peerEndPoint)
    {
        STUNMessage permissionsRequest = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
        permissionsRequest.Header.TransactionId = Encoding.ASCII.GetBytes(transactionID);
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

        var sendResult = iceServer.Protocol == ProtocolType.Tcp ?
                            SendOverTCP(iceServer, createPermissionReqBytes) :
                            _rtpChannel.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, createPermissionReqBytes);

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
    /// A TURN Refresh request is how a client keeps an existing allocation alive—or deletes it. In short,
    /// it updates the allocation’s time-to-expiry, or, if you set LIFETIME=0, it tears the allocation down immediately.
    /// </remarks>
    private SocketError SendTurnRefreshRequest(IceServer iceServer)
    {
        iceServer.OutstandingRequestsSent += 1;
        iceServer.LastRequestSentAt = DateTime.Now;

        STUNMessage allocateRequest = new STUNMessage(STUNMessageTypesEnum.Refresh);
        allocateRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
        //allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, 3600));
        allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, ALLOCATION_TIME_TO_EXPIRY_VALUE));

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

        var sendResult = iceServer.Protocol == ProtocolType.Tcp ?
                            SendOverTCP(iceServer, allocateReqBytes) :
                            _rtpChannel.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, allocateReqBytes);

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
    /// Sends a packet via a TURN relay server.
    /// </summary>
    /// <param name="dstEndPoint">The peer destination end point.</param>
    /// <param name="buffer">The data to send to the peer.</param>
    /// <param name="relayEndPoint">The TURN server end point to send the relayed request to.</param>
    /// <returns></returns>
    private SocketError SendRelay(ProtocolType protocol, IPEndPoint dstEndPoint, byte[] buffer, IPEndPoint relayEndPoint, IceServer iceServer)
    {
        STUNMessage sendReq = new STUNMessage(STUNMessageTypesEnum.SendIndication);
        sendReq.AddXORPeerAddressAttribute(dstEndPoint.Address, dstEndPoint.Port);
        sendReq.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Data, buffer));

        var request = sendReq.ToByteBuffer(null, false);
        var sendResult = protocol == ProtocolType.Tcp ?
            SendOverTCP(iceServer, request) :
            _rtpChannel.Send(RTPChannelSocketsEnum.RTP, relayEndPoint, request);

        if (sendResult != SocketError.Success)
        {
            logger.LogWarning("Error sending TURN relay request to TURN server at {RelayEndPoint}. {SendResult}.", relayEndPoint, sendResult);
        }
        else
        {
            OnStunMessageSent?.Invoke(sendReq, relayEndPoint, true);
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

    private SocketError SendOverTCP(IceServer iceServer, byte[] buffer)
    {
        IPEndPoint dstEndPoint = iceServer?.ServerEndPoint;

        if (dstEndPoint == null)
        {
            throw new ArgumentException("dstEndPoint", "An empty destination was specified to Send in RTPChannel.");
        }
        else if (buffer == null || buffer.Length == 0)
        {
            throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in RTPChannel.");
        }
        else if (IPAddress.Any.Equals(dstEndPoint.Address) || IPAddress.IPv6Any.Equals(dstEndPoint.Address))
        {
            logger.LogWarning("The destination address for Send in RTPChannel cannot be {Address}.", dstEndPoint.Address);
            return SocketError.DestinationAddressRequired;
        }
        else
        {
            try
            {
                //Connect to destination
                RtpTcpSocketByUri.TryGetValue(iceServer?._uri, out Socket sendSocket);
                //LastRtpDestination = dstEndPoint;

                if (sendSocket == null)
                {
                    return SocketError.Fault;
                }

                //Prevent Send to IPV4 while socket is IPV6 (Mono Error)
                if (dstEndPoint.AddressFamily == AddressFamily.InterNetwork && sendSocket.AddressFamily != dstEndPoint.AddressFamily)
                {
                    dstEndPoint = new IPEndPoint(dstEndPoint.Address.MapToIPv6(), dstEndPoint.Port);
                }

                Func<IPEndPoint, IPEndPoint, bool> equals = (IPEndPoint e1, IPEndPoint e2) =>
                {
                    return e1.Port == e2.Port && e1.Address.Equals(e2.Address);
                };

                if (!sendSocket.Connected || !(sendSocket.RemoteEndPoint is IPEndPoint) || !equals(sendSocket.RemoteEndPoint as IPEndPoint, dstEndPoint))
                {
                    if (sendSocket.Connected)
                    {
                        logger.LogDebug("SendOverTCP request disconnect.");
                        sendSocket.Disconnect(true);
                    }
                    sendSocket.Connect(dstEndPoint);

                    logger.LogDebug("SendOverTCP status: {Status} endpoint: {EndPoint}", sendSocket.Connected, dstEndPoint);
                }

                //Fix ReceiveFrom logic if any previous exception happens
                m_rtpTcpReceiverByUri.TryGetValue(iceServer?._uri, out IceTcpReceiver rtpTcpReceiver);
                if (rtpTcpReceiver != null && !rtpTcpReceiver.IsRunningReceive && !rtpTcpReceiver.IsClosed)
                {
                    rtpTcpReceiver.BeginReceiveFrom();
                }

                sendSocket.BeginSendTo(buffer, 0, buffer.Length, SocketFlags.None, dstEndPoint, EndSendToTCP, sendSocket);
                return SocketError.Success;
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            {
                return SocketError.Disconnecting;
            }
            catch (SocketException sockExcp)
            {
                return sockExcp.SocketErrorCode;
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception RTPIceChannel.SendOverTCP. {ErrorMessage}", excp.Message);
                return SocketError.Fault;
            }
        }
    }

    private void EndSendToTCP(IAsyncResult ar)
    {
        try
        {
            Socket sendSocket = (Socket)ar.AsyncState;
            int bytesSent = sendSocket.EndSendTo(ar);
        }
        catch (SocketException sockExcp)
        {
            // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
            // normal RTP operation. For example:
            // - the RTP connection may start sending before the remote socket starts listening,
            // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
            //   or new socket during the transition.
            logger.LogWarning(sockExcp, "SocketException RTPIceChannel EndSendToTCP ({SocketErrorCode}). {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
        }
        catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
        { }
        catch (Exception excp)
        {
            logger.LogError(excp, "Exception RTPIceChannel EndSendToTCP. {ErrorMessage}", excp.Message);
        }
    }

    private void RefreshTurn(Object state)
    {
        //try
        //{
        //    if (_closed)
        //    {
        //        return;
        //    }

        //    if (NominatedEntry == null || _activeIceServer == null)
        //    {
        //        return;
        //    }
        //    if (_activeIceServer._uri.Scheme != STUNSchemesEnum.turn || NominatedEntry.LocalCandidate.IceServer is null)
        //    {
        //        _refreshTurnTimer?.Dispose();
        //        return;
        //    }
        //    if (_activeIceServer.TurnTimeToExpiry.Subtract(DateTime.Now) <= TimeSpan.FromMinutes(1))
        //    {
        //        logger.LogDebug("Sending TURN refresh request to ICE server {Uri}.", _activeIceServer._uri);
        //        _activeIceServer.Error = SendTurnRefreshRequest(_activeIceServer);
        //    }

        //    if (NominatedEntry.TurnPermissionsRequestSent >= IceServer.MAX_REQUESTS)
        //    {
        //        logger.LogWarning("ICE RTP channel failed to get a Create Permissions response from {IceServerUri} after {TurnPermissionsRequestSent} attempts.", NominatedEntry.LocalCandidate.IceServer._uri, NominatedEntry.TurnPermissionsRequestSent);
        //    }
        //    else if (NominatedEntry.TurnPermissionsRequestSent != 1 || NominatedEntry.TurnPermissionsResponseAt == DateTime.MinValue || DateTime.Now.Subtract(NominatedEntry.TurnPermissionsResponseAt).TotalSeconds >
        //             REFRESH_PERMISSION_PERIOD)
        //    {
        //        // Send Create Permissions request to TURN server for remote candidate.
        //        NominatedEntry.TurnPermissionsRequestSent++;
        //        logger.LogDebug("ICE RTP channel sending TURN permissions request {TurnPermissionsRequestSent} to server {IceServerUri} for peer {RemoteCandidate} (TxID: {RequestTransactionID}).",
        //            NominatedEntry.TurnPermissionsRequestSent, NominatedEntry.LocalCandidate.IceServer._uri, NominatedEntry.RemoteCandidate.DestinationEndPoint, NominatedEntry.RequestTransactionID);
        //        SendTurnCreatePermissionsRequest(NominatedEntry.RequestTransactionID, NominatedEntry.LocalCandidate.IceServer, NominatedEntry.RemoteCandidate.DestinationEndPoint);
        //    }
        //}
        //catch (Exception excp)
        //{
        //    logger.LogError(excp, "Exception " + nameof(RefreshTurn) + ". {ErrorMessage}", excp);
        //}
    }

}
