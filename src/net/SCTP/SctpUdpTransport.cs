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
        public const ushort DEFAULT_UDP_MTU = 1300;

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
        /// <param name="portRange">Optional. The portRange which should be used to get a listening port.</param>
        public SctpUdpTransport(int udpEncapPort = 0, PortRange portRange = null)
        {
            NetServices.CreateRtpSocket(false, IPAddress.IPv6Any, udpEncapPort, portRange, out _udpEncapSocket, out _);
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
                if (!SctpPacket.VerifyChecksum(packet, 0, packet.Length))
                {
                    logger.LogWarning("SCTP packet from UDP {RemoteEndPoint} dropped due to invalid checksum.", remoteEndPoint);
                }
                else
                {
                    var sctpPacket = SctpPacket.Parse(packet, 0, packet.Length);

                    // Process packet.
                    if (sctpPacket.Header.VerificationTag == 0)
                    {
                        GotInit(sctpPacket, remoteEndPoint);
                    }
                    else if (sctpPacket.Chunks.Any(x => x.KnownType == SctpChunkType.COOKIE_ECHO))
                    {
                        // The COOKIE ECHO chunk is the 3rd step in the SCTP handshake when the remote party has
                        // requested a new association be created.
                        var cookie = base.GetCookie(sctpPacket);

                        if (cookie.IsEmpty())
                        {
                            logger.LogWarning("SCTP error acquiring handshake cookie from COOKIE ECHO chunk.");
                        }
                        else
                        {
                            logger.LogDebug("SCTP creating new association for {RemoteEndPoint}.", remoteEndPoint);

                            var association = new SctpAssociation(this, cookie, localPort);

                            if (_associations.TryAdd(association.ID, association))
                            {
                                if (sctpPacket.Chunks.Count > 1)
                                {
                                    // There could be DATA chunks after the COOKIE ECHO chunk.
                                    association.OnPacketReceived(sctpPacket);
                                }
                            }
                            else
                            {
                                logger.LogError("SCTP failed to add new association to dictionary.");
                            }
                        }
                    }
                    else
                    {
                        // TODO: Lookup the existing association for the packet.
                        _associations.Values.First().OnPacketReceived(sctpPacket);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception SctpTransport.OnEncapsulationSocketPacketReceived. {ErrorMessage}", excp.Message);
            }
        }

        /// <summary>
        /// Event handler for the UDP encapsulation socket closing.
        /// </summary>
        /// <param name="reason"></param>
        private void OnEncapsulationSocketClosed(string reason)
        {
            logger.LogInformation("SCTP transport encapsulation receiver closed with reason: {Reason}.", reason);
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
        public SctpAssociation Associate(
            IPEndPoint destination, 
            ushort sourcePort, 
            ushort destinationPort, 
            ushort numberOutboundStreams = SctpAssociation.DEFAULT_NUMBER_OUTBOUND_STREAMS,
            ushort numberInboundStreams = SctpAssociation.DEFAULT_NUMBER_INBOUND_STREAMS)
        {
            var association = new SctpAssociation(
                this, 
                destination, 
                sourcePort, 
                destinationPort, 
                DEFAULT_UDP_MTU,
                numberOutboundStreams,
                numberInboundStreams);

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
