//-----------------------------------------------------------------------------
// Filename: MockTurnServer.cs
//
// Description: Serves a mock STUN/TURN server for RtpIceChannel unit tests.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 24 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
// 14 Dec 2020  Aaron Clauson   Moved from unit to integration tests.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net.IntegrationTests
{
    public class MockTurnServer : IDisposable
    {
        private Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private int _listenPort = STUNConstants.DEFAULT_STUN_PORT;
        private IPAddress _listenAddress = IPAddress.Loopback;
        private SocketUdpConnection _listener;

        // If acting as a TRUN server and an allocation is requested.
        private IPEndPoint _relayEndPoint;
        private SocketUdpConnection _relayListener;
        private IPEndPoint _clientEndPoint;

        public IPEndPoint ListeningEndPoint { get; private set; }

        public MockTurnServer() : this(IPAddress.Loopback, STUNConstants.DEFAULT_STUN_PORT)
        { }

        public MockTurnServer(IPAddress listenAddress, int port)
        {
            _listenAddress = listenAddress;
            _listenPort = port;

            NetServices.CreateRtpSocket(false, _listenAddress, 0, null, out var clientSocket, out _);

            ListeningEndPoint = clientSocket.LocalEndPoint as IPEndPoint;

            logger.LogDebug("MockTurnServer listening on {ListeningEndPoint}.", ListeningEndPoint);

            _listener = new SocketUdpConnection(clientSocket);
            _listener.OnPacketReceived += OnPacketReceived;
            _listener.OnClosed += (reason) => logger.LogDebug("MockTurnServer on {ListeningEndPoint} closed.", ListeningEndPoint);
            _listener.BeginReceiveFrom();
        }

        private void OnPacketReceived(SocketConnection receiver, int localPort, IPEndPoint remoteEndPoint, ReadOnlyMemory<byte> packet)
        {
            STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(packet.Span);

            switch (stunMessage.Header.MessageType)
            {
                case STUNMessageTypesEnum.Allocate:

                    {
                        logger.LogDebug("MockTurnServer received Allocate request from {RemoteEndPoint}.", remoteEndPoint);

                        if (_relayListener == null)
                        {
                            _clientEndPoint = remoteEndPoint;

                            // Create a new relay socket.
                            NetServices.CreateRtpSocket(false, _listenAddress, 0, null, out var relaySocket, out _);

                            _relayEndPoint = relaySocket.LocalEndPoint as IPEndPoint;

                            logger.LogDebug("MockTurnServer created relay socket on {RelayEndPoint}.", _relayEndPoint);

                            _relayListener = new SocketUdpConnection(relaySocket);
                            _relayListener.OnPacketReceived += OnRelayPacketReceived;
                            _relayListener.OnClosed += (reason) => logger.LogDebug("MockTurnServer relay on {RelayEndPoint} closed.", _relayEndPoint);
                            _relayListener.BeginReceiveFrom();
                        }

                        STUNMessage allocateResponse = new STUNMessage(STUNMessageTypesEnum.AllocateSuccessResponse);
                        allocateResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                        allocateResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);
                        allocateResponse.AddXORAddressAttribute(STUNAttributeTypesEnum.XORRelayedAddress, _relayEndPoint.Address, _relayEndPoint.Port);

                        var allocateBuffer = new byte[allocateResponse.GetByteBufferSize(ReadOnlySpan<byte>.Empty, false)];
                        allocateResponse.WriteToBuffer(allocateBuffer, null, false);
                        _listener.SendTo(remoteEndPoint, allocateBuffer.AsMemory(), null);
                    }
                    break;

                case STUNMessageTypesEnum.BindingRequest:

                    {
                        logger.LogDebug("MockTurnServer received Binding request from {RemoteEndPoint}.", remoteEndPoint);

                        STUNMessage stunResponse = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
                        stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                        stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);
                        var stunBuffer = new byte[stunResponse.GetByteBufferSize(null, false)];
                        stunResponse.WriteToBuffer(stunBuffer, null, false);
                        _listener.SendTo(remoteEndPoint, stunBuffer.AsMemory(), null);
                    }
                    break;

                case STUNMessageTypesEnum.CreatePermission:

                    {
                        logger.LogDebug("MockTurnServer received CreatePermission request from {RemoteEndPoint}.", remoteEndPoint);

                        STUNMessage permResponse = new STUNMessage(STUNMessageTypesEnum.CreatePermissionSuccessResponse);
                        permResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                        var permBuffer = new byte[permResponse.GetByteBufferSize(null, false)];
                        permResponse.WriteToBuffer(permBuffer, null, false);
                        _listener.SendTo(remoteEndPoint, permBuffer.AsMemory(), null);
                    }
                    break;

                case STUNMessageTypesEnum.SendIndication:

                    {
                        logger.LogDebug("MockTurnServer received SendIndication request from {RemoteEndPoint}.", remoteEndPoint);
                        var stunBuffer = stunMessage.Attributes.Single(x => x.AttributeType == STUNAttributeTypesEnum.Data).Value;
                        var destEP = (stunMessage.Attributes.Single(x => x.AttributeType == STUNAttributeTypesEnum.XORPeerAddress) as STUNXORAddressAttribute).GetIPEndPoint();

                        logger.LogDebug("MockTurnServer relaying {BufferLength} bytes to {DestinationEndPoint}.", stunBuffer.Length, destEP);

                        _relayListener.SendTo(destEP, stunBuffer, null);
                    }

                    break;

                default:
                    logger.LogDebug("MockTurnServer received unknown STUN message from {RemoteEndPoint}.", remoteEndPoint);
                    break;
            }
        }

        /// <summary>
        /// Handler for receiving packets from a Peer on a dummy TURN relay socket. The packets arrive from a peer
        /// and need to be forwarded to the client in a STUN Data Indication message.
        /// </summary>
        /// <param name="receiver">The receiver the packet was received on.</param>
        /// <param name="localPort">The port number the packet was received on.</param>
        /// <param name="remoteEndPoint">The end point of the peer sending traffic to the TURN server.</param>
        /// <param name="packet">The byes received from the peer.</param>
        private void OnRelayPacketReceived(SocketConnection receiver, int localPort, IPEndPoint remoteEndPoint, ReadOnlyMemory<byte> packet)
        {
            STUNMessage dataInd = new STUNMessage(STUNMessageTypesEnum.DataIndication);
            dataInd.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Data, packet));
            dataInd.AddXORPeerAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);

            var buffer = new byte[dataInd.GetByteBufferSize(ReadOnlySpan<byte>.Empty, false)];
            dataInd.WriteToBuffer(buffer, ReadOnlySpan<byte>.Empty, false);
            _listener.SendTo(_clientEndPoint, buffer.AsMemory(), null);
        }

        public void Dispose()
        {
            _relayListener?.Close("disposed");
            _listener?.Close("disposed");
        }
    }
}
