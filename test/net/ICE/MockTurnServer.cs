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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net.UnitTests
{
    public class MockTurnServer : IDisposable
    {
        private Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private int _listenPort = STUNConstants.DEFAULT_STUN_PORT;
        private IPAddress _listenAddress = IPAddress.Loopback;
        private Socket _clientSocket;
        private UdpReceiver _listener;

        // If acting as a TRUN server and an allocation is requested.
        private Socket _relaySocket;
        private IPEndPoint _relayEndPoint;
        private UdpReceiver _relayListener;
        private IPEndPoint _clientEndPoint;

        public IPEndPoint ListeningEndPoint { get; private set; }

        public MockTurnServer() : this(IPAddress.Loopback, STUNConstants.DEFAULT_STUN_PORT)
        { }

        public MockTurnServer(IPAddress listenAddress, int port)
        {
            _listenAddress = listenAddress;
            _listenPort = port;

            NetServices.CreateRtpSocket(false, _listenAddress, 0, out _clientSocket, out _);

            ListeningEndPoint = _clientSocket.LocalEndPoint as IPEndPoint;

            logger.LogDebug($"MockTurnServer listening on {ListeningEndPoint}.");

            _listener = new UdpReceiver(_clientSocket);
            _listener.OnPacketReceived += OnPacketReceived;
            _listener.OnClosed += (reason) => logger.LogDebug($"MockTurnServer on {ListeningEndPoint} closed.");
            _listener.BeginReceiveFrom();
        }

        private void OnPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
        {
            STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(packet, packet.Length);

            switch (stunMessage.Header.MessageType)
            {
                case STUNMessageTypesEnum.Allocate:

                    logger.LogDebug($"MockTurnServer received Allocate request from {remoteEndPoint}.");

                    if (_relaySocket == null)
                    {
                        _clientEndPoint = remoteEndPoint;

                        // Create a new relay socket.
                        NetServices.CreateRtpSocket(false, _listenAddress, 0, out _relaySocket, out _);

                        _relayEndPoint = _relaySocket.LocalEndPoint as IPEndPoint;

                        logger.LogDebug($"MockTurnServer created relay socket on {_relayEndPoint}.");

                        _relayListener = new UdpReceiver(_relaySocket);
                        _relayListener.OnPacketReceived += OnRelayPacketReceived;
                        _relayListener.OnClosed += (reason) => logger.LogDebug($"MockTurnServer relay on {_relayEndPoint} closed.");
                        _relayListener.BeginReceiveFrom();
                    }

                    STUNMessage allocateResponse = new STUNMessage(STUNMessageTypesEnum.AllocateSuccessResponse);
                    allocateResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                    allocateResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);
                    allocateResponse.AddXORAddressAttribute(STUNAttributeTypesEnum.XORRelayedAddress, _relayEndPoint.Address, _relayEndPoint.Port);

                    _clientSocket.SendTo(allocateResponse.ToByteBuffer(null, false), remoteEndPoint);
                    break;

                case STUNMessageTypesEnum.BindingRequest:

                    logger.LogDebug($"MockTurnServer received Binding request from {remoteEndPoint}.");

                    STUNMessage stunResponse = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
                    stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                    stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);
                    _clientSocket.SendTo(stunResponse.ToByteBuffer(null, false), remoteEndPoint);
                    break;

                case STUNMessageTypesEnum.CreatePermission:

                    logger.LogDebug($"MockTurnServer received CreatePermission request from {remoteEndPoint}.");

                    STUNMessage permResponse = new STUNMessage(STUNMessageTypesEnum.CreatePermissionSuccessResponse);
                    permResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                    _clientSocket.SendTo(permResponse.ToByteBuffer(null, false), remoteEndPoint);
                    break;

                case STUNMessageTypesEnum.SendIndication:

                    logger.LogDebug($"MockTurnServer received SendIndication request from {remoteEndPoint}.");
                    var buffer = stunMessage.Attributes.Single(x => x.AttributeType == STUNAttributeTypesEnum.Data).Value;
                    var destEP = (stunMessage.Attributes.Single(x => x.AttributeType == STUNAttributeTypesEnum.XORPeerAddress) as STUNXORAddressAttribute).GetIPEndPoint();

                    logger.LogDebug($"MockTurnServer relaying {buffer.Length} bytes to {destEP}.");

                    _relaySocket.SendTo(buffer, destEP);

                    break;

                default:
                    logger.LogDebug($"MockTurnServer received unknown STUN message from {remoteEndPoint}.");
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
        private void OnRelayPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
        {
            STUNMessage dataInd = new STUNMessage(STUNMessageTypesEnum.DataIndication);
            dataInd.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Data, packet));
            dataInd.AddXORPeerAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);

            _clientSocket.SendTo(dataInd.ToByteBuffer(null, false), _clientEndPoint);
        }

        public void Dispose()
        {
            _relayListener?.Close("disposed");
            _relaySocket?.Close();
            _listener?.Close("disposed");
            _clientSocket?.Close();
        }
    }
}
