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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Digests;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class IceTcpReceiver : UdpReceiver
{
    protected const int REVEIVE_TCP_BUFFER_SIZE = RECEIVE_BUFFER_SIZE * 2;

    protected int m_recvOffset;

    public IceTcpReceiver(Socket socket, int mtu = REVEIVE_TCP_BUFFER_SIZE) : base(socket, mtu)
    {
        m_recvOffset = 0;
    }

    /// <summary>
    /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
    /// return any data received.
    /// </summary>
    public override void BeginReceiveFrom()
    {
        //Prevent call BeginReceiveFrom if it is already running or into invalid state
        if ((m_isClosed || !m_socket.Connected) && m_isRunningReceive)
        {
            m_isRunningReceive = false;
        }
        if (m_isRunningReceive || m_isClosed || !m_socket.Connected)
        {
            return;
        }

        try
        {
            m_isRunningReceive = true;
            EndPoint recvEndPoint = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
            var recvLength = m_recvBuffer.Length - m_recvOffset;
            //Discard fragmentation buffer as seems that we will have an incorrect result based in cached values
            if (recvLength <= 0 || m_recvOffset < 0)
            {
                m_recvOffset = 0;
                recvLength = m_recvBuffer.Length;
            }
            m_socket.BeginReceiveFrom(m_recvBuffer, m_recvOffset, recvLength, SocketFlags.None, ref recvEndPoint, EndReceiveFrom, null);
        }
        // Thrown when socket is closed. Can be safely ignored.
        // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
        // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the 
        // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.
        catch (ObjectDisposedException)
        {
            m_isRunningReceive = false;
        }
        catch (SocketException sockExcp)
        {
            m_isRunningReceive = false;
            logger.LogWarning(sockExcp, "Socket error {SocketErrorCode} in IceTcpReceiver.BeginReceiveFrom. {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
            //Close(sockExcp.Message);
        }
        catch (Exception excp)
        {
            m_isRunningReceive = false;
            // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
            // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
            // case the socket can be considered to be unusable and there's no point trying another receive.
            logger.LogError(excp, "Exception IceTcpReceiver.BeginReceiveFrom. {ErrorMessage}", excp.Message);
            Close(excp.Message);
        }
    }

    /// <summary>
    /// Handler for end of the begin receive call.
    /// </summary>
    /// <param name="ar">Contains the results of the receive.</param>
    protected override void EndReceiveFrom(IAsyncResult ar)
    {
        try
        {
            // When socket is closed the object will be disposed of in the middle of a receive.
            if (!m_isClosed)
            {
                EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                int bytesRead = m_socket.EndReceiveFrom(ar, ref remoteEP);

                if (bytesRead > 0)
                {
                    ProcessRawBuffer(bytesRead + m_recvOffset, remoteEP as IPEndPoint);
                }
            }

            // If there is still data available it should be read now. This is more efficient than calling
            // BeginReceiveFrom which will incur the overhead of creating the callback and then immediately firing it.
            // It also avoids the situation where if the application cannot keep up with the network then BeginReceiveFrom
            // will be called synchronously (if data is available it calls the callback method immediately) which can
            // create a very nasty stack.
            if (!m_isClosed && m_socket.Available > 0)
            {
                while (!m_isClosed && m_socket.Available > 0)
                {
                    EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                    var recvLength = m_recvBuffer.Length - m_recvOffset;
                    //Discard fragmentation buffer as seems that we will have an incorrect result based in cached values
                    if (recvLength <= 0 || m_recvOffset < 0)
                    {
                        m_recvOffset = 0;
                        recvLength = m_recvBuffer.Length;
                    }
                    int bytesReadSync = m_socket.ReceiveFrom(m_recvBuffer, m_recvOffset, recvLength, SocketFlags.None, ref remoteEP);

                    if (bytesReadSync > 0)
                    {
                        if (ProcessRawBuffer(bytesReadSync + m_recvOffset, remoteEP as IPEndPoint) == 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (SocketException resetSockExcp) when (resetSockExcp.SocketErrorCode == SocketError.ConnectionReset)
        {
            // Thrown when close is called on a socket from this end. Safe to ignore.
        }
        catch (SocketException sockExcp)
        {
            // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
            // normal RTP operation. For example:
            // - the RTP connection may start sending before the remote socket starts listening,
            // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
            //   or new socket during the transition.
            // It also seems that once a UDP socket pair have exchanged packets and the remote party closes the socket exception will occur
            // in the BeginReceive method (very handy). Follow-up, this doesn't seem to be the case, the socket exception can occur in 
            // BeginReceive before any packets have been exchanged. This means it's not safe to close if BeginReceive gets an ICMP 
            // error since the remote party may not have initialised their socket yet.
            logger.LogWarning(sockExcp, "SocketException IceTcpReceiver.EndReceiveFrom ({SocketErrorCode}). {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
        }
        catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
        { }
        catch (Exception excp)
        {
            logger.LogError(excp, "Exception IceTcpReceiver.EndReceiveFrom. {ErrorMessage}", excp.Message);
            Close(excp.Message);
        }
        finally
        {
            m_isRunningReceive = false;
            if (!m_isClosed)
            {
                BeginReceiveFrom();
            }
        }
    }

    // TODO: If we miss any package because slow internet connection
    // and initial byte in buffer is not a STUNHeader (starts with 0x00 0x00)
    // and our receive buffer is full, we need a way to discard whole buffer
    // or check for 0x00 0x00 start again.
    protected virtual int ProcessRawBuffer(int bytesRead, IPEndPoint remoteEP)
    {
        var extractCount = 0;
        if (bytesRead > 0)
        {
            // During experiments IPPacketInformation wasn't getting set on Linux. Without it the local IP address
            // cannot be determined when a listener was bound to IPAddress.Any (or IPv6 equivalent). If the caller
            // is relying on getting the local IP address on Linux then something may fail.
            //if (packetInfo != null && packetInfo.Address != null)
            //{
            //    localEndPoint = new IPEndPoint(packetInfo.Address, localEndPoint.Port);
            //}

            //Try extract all StunMessages from current receive buffer
            var isFragmented = true;
            var recvRemainingSegment = new ArraySegment<byte>(m_recvBuffer, 0, bytesRead);

            while (recvRemainingSegment.Count > STUNHeader.STUN_HEADER_LENGTH)
            {
                isFragmented = false;
                STUNHeader header = null;
                try
                {
                    header = STUNHeader.ParseSTUNHeader(recvRemainingSegment);
                }
                catch
                {
                    header = null;
                }
                if (header != null)
                {
                    int stunMsgBytes = STUNHeader.STUN_HEADER_LENGTH + header.MessageLength;
                    if (stunMsgBytes % 4 != 0)
                    {
                        stunMsgBytes = stunMsgBytes - (stunMsgBytes % 4) + 4;
                    }

                    //We have the packet count all inside current receiving buffer
                    if (recvRemainingSegment.Count >= stunMsgBytes)
                    {
                        extractCount++;
                        m_recvOffset = recvRemainingSegment.Offset + recvRemainingSegment.Count;

                        byte[] packetBuffer = new byte[stunMsgBytes];
                        Buffer.BlockCopy(recvRemainingSegment.Array, recvRemainingSegment.Offset, packetBuffer, 0, stunMsgBytes);

                        CallOnPacketReceivedCallback(m_localEndPoint.Port, remoteEP, packetBuffer);

                        var newOffset = recvRemainingSegment.Offset + stunMsgBytes;
                        var newCount = recvRemainingSegment.Count - stunMsgBytes;
                        if (newCount > STUNHeader.STUN_HEADER_LENGTH && newOffset >= 0)
                        {
                            recvRemainingSegment = new ArraySegment<byte>(recvRemainingSegment.Array, newOffset, newCount);
                        }
                        else
                        {
                            if (newCount > 0 && newOffset >= 0)
                            {
                                recvRemainingSegment = new ArraySegment<byte>(recvRemainingSegment.Array, newOffset, newCount);
                                isFragmented = true;
                            }
                            else
                            {
                                recvRemainingSegment = new ArraySegment<byte>();
                                isFragmented = false;
                            }
                            break;
                        }
                    }
                    //We have a fragmentation but the header is intact, we need to cache the fragmentation for the next receive cycle
                    else
                    {
                        isFragmented = true;
                        break;
                    }
                }
                //Save Remaining Buffer in start of m_recvBuffer
                else
                {
                    isFragmented = true;
                    break;
                }
            }

            if (isFragmented)
            {
                m_recvOffset = recvRemainingSegment.Count;
                Buffer.BlockCopy(recvRemainingSegment.Array, recvRemainingSegment.Offset, m_recvBuffer, 0, recvRemainingSegment.Count);
            }
            else
            {
                m_recvOffset = 0;
            }
        }

        return extractCount;
    }

    /// <summary>
    /// Closes the socket and stops any new receives from being initiated.
    /// </summary>
    public override void Close(string reason)
    {
        if (!m_isClosed)
        {
            if (m_socket != null && m_socket.Connected)
            {
                m_socket?.Disconnect(false);
            }
            base.Close(reason);
        }
    }
}

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
            bool wasRelayed = false;

            if (packet[0] == 0x00 && packet[1] == 0x17)
            {
                wasRelayed = true;

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
                var stunMessage = STUNMessage.ParseSTUNMessage(packet, packet.Length);
                _ = ProcessStunMessage(stunMessage, remoteEndPoint, wasRelayed);
            }
            else
            {
                // If not STUN or TURN ignore. The default RTP channel handler will deal with.
                //OnRTPDataReceived?.Invoke(localPort, remoteEndPoint, packet);
            }
        }
    }

    /// <summary>
    /// Processes a received STUN request or response.
    /// </summary>
    /// <remarks>
    /// Actions to take on a successful STUN response https://tools.ietf.org/html/rfc8445#section-7.2.5.3
    /// - Discover peer reflexive remote candidates as per https://tools.ietf.org/html/rfc8445#section-7.2.5.3.1.
    /// - Construct a valid pair which means match a candidate pair in the check list and mark it as valid (since a successful STUN exchange 
    ///   has now taken place on it). A new entry may need to be created for this pair for a peer reflexive candidate.
    /// - Update state of candidate pair that generated the check to Succeeded.
    /// - If the controlling candidate set the USE_CANDIDATE attribute then the ICE agent that receives the successful response sets the nominated
    ///   flag of the pair to true. Once the nominated flag is set it concludes the ICE processing for that component.
    /// </remarks>
    /// <param name="stunMessage">The STUN message received.</param>
    /// <param name="remoteEndPoint">The remote end point the STUN packet was received from.</param>
    private async Task ProcessStunMessage(STUNMessage stunMessage, IPEndPoint remoteEndPoint, bool wasRelayed)
    {
        remoteEndPoint = (!remoteEndPoint.Address.IsIPv4MappedToIPv6) ? remoteEndPoint : new IPEndPoint(remoteEndPoint.Address.MapToIPv4(), remoteEndPoint.Port);

        //OnStunMessageReceived?.Invoke(stunMessage, remoteEndPoint, wasRelayed);

        // Check if the  STUN message is for an ICE server check.
        //var iceServer = GetIceServerForTransactionID(stunMessage.Header.TransactionId);
        //if (iceServer != null)
        //{
        //    bool candidatesAvailable = iceServer.GotStunResponse(stunMessage, remoteEndPoint);
        //    if (candidatesAvailable)
        //    {
        //        // Safe to wait here as the candidates from an ICE server will always be IP addresses only,
        //        // no DNS lookups required.
        //        await AddCandidatesForIceServer(iceServer).ConfigureAwait(false);
        //    }
        //}
        //else
        //{
        //    // If the STUN message isn't for an ICE server then it needs to be matched against a remote
        //    // candidate and a checklist entry and if no match a "peer reflexive" candidate may need to
        //    // be created.
        //    if (stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingRequest)
        //    {
        //        GotStunBindingRequest(stunMessage, remoteEndPoint, wasRelayed);
        //    }
        //    else if (stunMessage.Header.MessageClass == STUNClassTypesEnum.ErrorResponse ||
        //             stunMessage.Header.MessageClass == STUNClassTypesEnum.SuccessResponse)
        //    {
        //        // Correlate with request using transaction ID as per https://tools.ietf.org/html/rfc8445#section-7.2.5.
        //        var matchingChecklistEntry = GetChecklistEntryForStunResponse(stunMessage.Header.TransactionId);

        //        if (matchingChecklistEntry == null)
        //        {
        //            if (IceConnectionState != RTCIceConnectionState.connected)
        //            {
        //                // If the channel is connected a mismatched txid can result if the connection is very busy, i.e. streaming 1080p video,
        //                // it's likely to only be transient and does not impact the connection state.
        //                logger.LogWarning("ICE RTP channel received a STUN {MessageType} with a transaction ID that did not match a checklist entry.", stunMessage.Header.MessageType);
        //            }
        //        }
        //        else
        //        {
        //            matchingChecklistEntry.GotStunResponse(stunMessage, remoteEndPoint);

        //            if (_checklistState == ChecklistState.Running &&
        //                stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingSuccessResponse)
        //            {
        //                if (matchingChecklistEntry.Nominated)
        //                {
        //                    logger.LogDebug("ICE RTP channel remote peer nominated entry from binding response {RemoteCandidate}", matchingChecklistEntry.RemoteCandidate.ToShortString());

        //                    // This is the response to a connectivity check that had the "UseCandidate" attribute set.
        //                    SetNominatedEntry(matchingChecklistEntry);
        //                }
        //                else if (IsController)
        //                {
        //                    logger.LogDebug("ICE RTP channel binding response state {State} as Controller for {RemoteCandidate}", matchingChecklistEntry.State, matchingChecklistEntry.RemoteCandidate.ToShortString());
        //                    ProcessNominateLogicAsController(matchingChecklistEntry);
        //                }
        //            }
        //        }
        //    }
        //    else
        //    {
        //        logger.LogWarning("ICE RTP channel received an unexpected STUN message {MessageType} from {RemoteEndPoint}.\nJson: {StunMessage}", stunMessage.Header.MessageType, remoteEndPoint, stunMessage);
        //    }
        //}

        await Task.Delay(0);

        logger.LogWarning("ICE RTP channel received an unexpected STUN message {MessageType} from {RemoteEndPoint}.\nJson: {StunMessage}", stunMessage.Header.MessageType, remoteEndPoint, stunMessage);
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

}
