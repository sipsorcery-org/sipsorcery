//-----------------------------------------------------------------------------
// Filename: SctpUdpTransport.cs
//
// Description: Represents an UDP transport capable of encapsulating SCTP
// packets.
//
// Remarks:
// UDP encapsulation of SCTP: https://tools.ietf.org/html/rfc6951
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// St Patrick's Day 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Represents an SCTP transport that encapsulates SCTP packet in UDP.
    /// </summary>
    public class SctpUdpTransport : SctpTransport
    {
        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<SctpUdpTransport>();

        /// <summary>
        /// The UDP encapsulation socket if the instance is managing its own transport layer.
        /// For WebRTC data channels the socket will not be managed externally.
        /// </summary>
        private Socket _udpEncapSocket;

        private ConcurrentDictionary<string, SctpAssociation> _associations = new ConcurrentDictionary<string, SctpAssociation>();

        /// <summary>
        /// Creates a new UDP transport capable of encapsulating SCTP packets.
        /// </summary>
        /// <param name="udpEncapPort">The port to bind to for the UDP encapsulation socket.</param>
        public SctpUdpTransport(int udpEncapPort = 0)
        {
            NetServices.CreateRtpSocket(false, IPAddress.IPv6Any, udpEncapPort, out _udpEncapSocket, out _);
            UdpReceiver udpReceiver = new UdpReceiver(_udpEncapSocket);
            udpReceiver.OnPacketReceived += OnEncapsulationSocketPacketReceived;
            udpReceiver.OnClosed += OnEncapsulationSocketClosed;
            udpReceiver.BeginReceiveFrom();
        }

        /// <summary>
        /// Event handler for a packet receive on the UDP encapsulation socket.
        /// </summary>
        /// <param name="receiver">The UDP receiver that received the packet.</param>
        /// <param name="localPort">The local port the packet was received on.</param>
        /// <param name="remoteEndPoint">The remote end point the packet was received from.</param>
        /// <param name="packet">A buffer containing the packet.</param>
        private void OnEncapsulationSocketPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
        {
            try
            {
                logger.LogTrace($"SCTP packet received {packet.Length} bytes.");
                //logger.LogTrace(packet.HexStr());

                var pkt = SctpPacket.Parse(packet);

                // Diagnostics.
                logger.LogTrace($"SCTP with {pkt.Chunks.Count} received.");
                foreach (var chunk in pkt.Chunks)
                {
                    logger.LogTrace($" chunk {chunk.KnownType}.");
                    if (chunk.VariableParameters != null)
                    {
                        foreach (var chunkParam in chunk.VariableParameters)
                        {
                            logger.LogTrace($"  chunk Parameter {chunkParam.KnownType}.");
                        }
                    }
                }

                // Process packet.
                if (pkt.Chunks.Any(x => x.KnownType == SctpChunkType.INIT))
                {
                    // INIT packets have specific processing rules in order to prevent resource exhaustion.
                    // See Section 5 of RFC 4960 https://tools.ietf.org/html/rfc4960#section-5 "Association Initialization".
                    var initAckPacket = base.GetInitAck(pkt, remoteEndPoint);
                    var buffer = initAckPacket.GetBytes();
                    Send(null, buffer, 0, buffer.Length);
                }
                else if(pkt.Chunks.Any(x => x.KnownType == SctpChunkType.COOKIE_ECHO))
                {

                }

                else
                {
                    // TODO: Lookup the existing association for the packet.
                    _associations.Values.First().OnPacketReceived(pkt);
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception SctpTransport.OnEncapsulationSocketPacketReceived. {excp}");
            }
        }

        /// <summary>
        /// Event handler for the UDP encapsulation socket closing.
        /// </summary>
        /// <param name="reason"></param>
        private void OnEncapsulationSocketClosed(string reason)
        {
            logger.LogInformation($"SCTP transport encapsulation receiver closed with reason: {reason}.");
        }

        public override void Send(string associationID, byte[] buffer, int offset, int length)
        {
            if (_associations.TryGetValue(associationID, out var assoc))
            {
                _udpEncapSocket.SendTo(buffer, offset, length, SocketFlags.None, assoc.Destination);
            }
        }

        /// <summary>
        /// Requests a new association be created.
        /// </summary>
        /// <param name="destination">The UDP endpoint to attempt to create the association with.</param>
        /// <param name="sourcePort">The SCTP source port.</param>
        /// <param name="destinationPort">The SCTP destination port.</param>
        /// <returns>An SCTP association.</returns>
        public SctpAssociation Associate(IPEndPoint destination, ushort sourcePort, ushort destinationPort)
        {
            var association = new SctpAssociation(this, destination, sourcePort, destinationPort);

            if (_associations.TryAdd(association.ID, association))
            {
                association.Init();
                return association;
            }
            else
            {
                logger.LogWarning("SCTP transport failed to add association.");
                association.Shutdown();
                return null;
            }
        }
    }
}
