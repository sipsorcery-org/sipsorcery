//-----------------------------------------------------------------------------
// Filename: SctpUdpTransport.cs
//
// Description: Represents an UDP transport capable of encapsulating SCTP
// packets.
//
// Remarks:
// The interface defined in https://tools.ietf.org/html/rfc4960#section-10 
// was used as a basis for this class.
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
    /// Contains the common methods that an SCTP transport layer needs to implement.
    /// As well as being able to be carried directly in IP packets, SCTP packets can
    /// also be wrapped in higher level protocols.
    /// </summary>
    /// <remarks>
    /// UDP encapsulation of SCTP: https://tools.ietf.org/html/rfc6951
    /// DTLS encapsulation of SCTP: https://tools.ietf.org/html/rfc8261
    /// </remarks>
    public interface ISctpTransport
    {
        void Send(string associationID, byte[] buffer, int offset, int length);
    }

    public class SctpUdpTransport : ISctpTransport
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
                    if (chunk.OptionalParameters != null)
                    {
                        foreach (var chunkParam in chunk.OptionalParameters)
                        {
                            logger.LogTrace($"  chunk Parameter {chunkParam.KnownType}.");
                        }
                    }

                    switch (chunk.KnownType)
                    {
                        default:
                            // TODO: Lookup association for packet.
                            if (_associations.Count > 0)
                            {
                                _associations.Values.First().OnPacketReceived(pkt);
                            }
                            break;
                    }
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

        public void Send(string associationID, byte[] buffer, int offset, int length)
        {
            if (_associations.TryGetValue(associationID, out var assoc))
            {
                _udpEncapSocket.SendTo(buffer, offset, length, SocketFlags.None, assoc.Destination);
            }
        }

        public SctpAssociation Associate(IPEndPoint destination, ushort sourcePort, ushort destinationPort)
        {
            var associationID = Guid.NewGuid().ToString();
            var association = new SctpAssociation(this, associationID, destination, sourcePort, destinationPort);

            if (_associations.TryAdd(associationID, association))
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

        /// <summary>
        /// This method allows SCTP to initialise its internal data structures
        /// and allocate necessary resources for setting up its operation
        /// environment.
        /// </summary>
        /// <param name="localPort">SCTP port number, if the application wants it to be specified.</param>
        /// <returns>The local SCTP instance name.</returns>
        public string Initialize(ushort localPort)
        {
            return "local SCTP instance name";
        }

        /// <summary>
        /// Initiates an association to a specific peer end point
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="streamCount"></param>
        /// <returns>An association ID, which is a local handle to the SCTP association.</returns>
        public string Associate(IPAddress destination, int streamCount)
        {
            return "association ID";
        }

        /// <summary>
        /// Gracefully closes an association. Any locally queued user data will
        /// be delivered to the peer.The association will be terminated only
        /// after the peer acknowledges all the SCTP packets sent.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        public void Shutdown(string associationID)
        {

        }

        /// <summary>
        /// Ungracefully closes an association. Any locally queued user data
        /// will be discarded, and an ABORT chunk is sent to the peer.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        public void Abort(string associationID)
        {

        }

        /// <summary>
        /// This is the main method to send user data via SCTP.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="buffer">The buffer holding the data to send.</param>
        /// <param name="length">The number of bytes from the buffer to send.</param>
        /// <param name="contextID">Optional. A 32-bit integer that will be carried in the
        /// sending failure notification to the application if the transportation of
        /// this user message fails.</param>
        /// <param name="streamID">Optional. To indicate which stream to send the data on. If not
        /// specified, stream 0 will be used.</param>
        /// <param name="lifeTime">Optional. specifies the life time of the user data. The user
        /// data will not be sent by SCTP after the life time expires.This
        /// parameter can be used to avoid efforts to transmit stale user
        /// messages.</param>
        /// <returns></returns>
        public string Send(string associationID, byte[] buffer, int length, int contextID, int streamID, int lifeTime)
        {
            return "ok";
        }

        /// <summary>
        /// Instructs the local SCTP to use the specified destination transport
        /// address as the primary path for sending packets.
        /// </summary>
        /// <param name="associationID"></param>
        /// <returns></returns>
        public string SetPrimary(string associationID)
        {
            // Note: Seems like this will be a noop for SCTP encapsulated in UDP.
            return "ok";
        }

        /// <summary>
        /// This method shall read the first user message in the SCTP in-queue
        /// into the buffer specified by the application, if there is one available.The
        /// size of the message read, in bytes, will be returned.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="buffer">The buffer to place the received data into.</param>
        /// <param name="length">The maximum size of the data to receive.</param>
        /// <param name="streamID">Optional. If specified indicates which stream to 
        /// receive the data on.</param>
        /// <returns></returns>
        public int Receive(string associationID, byte[] buffer, int length, int streamID)
        {
            return 0;
        }

        /// <summary>
        /// Returns the current status of the association.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <returns></returns>
        public SctpStatus Status(string associationID)
        {
            return new SctpStatus();
        }

        /// <summary>
        /// Instructs the local endpoint to enable or disable heartbeat on the
        /// specified destination transport address.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="interval">Indicates the frequency of the heartbeat if
        /// this is to enable heartbeat on a destination transport address.
        /// This value is added to the RTO of the destination transport
        /// address.This value, if present, affects all destinations.</param>
        /// <returns></returns>
        public string ChangeHeartbeat(string associationID, int interval)
        {
            return "ok";
        }

        /// <summary>
        /// Instructs the local endpoint to perform a HeartBeat on the specified
        /// destination transport address of the given association.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <returns>Indicates whether the transmission of the HEARTBEAT
        /// chunk to the destination address is successful.</returns>
        public string RequestHeartbeat(string associationID)
        {
            return "ok";
        }

        /// <summary>
        /// Instructs the local SCTP to report the current Smoothed Round Trip Time (SRTT)
        /// measurement on the specified destination transport address of the given 
        /// association.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <returns>An integer containing the most recent SRTT in milliseconds.</returns>
        public int GetSrttReport(string associationID)
        {
            return 0;
        }

        /// <summary>
        /// This method allows the local SCTP to customise the protocol
        /// parameters.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="protocolParameters">The specific names and values of the
        /// protocol parameters that the SCTP user wishes to customise.</param>
        public void SetProtocolParameters(string associationID, object protocolParameters)
        {

        }

        /// <summary>
        /// ??
        /// </summary>
        /// <param name="dataRetrievalID">The identification passed to the application in the
        /// failure notification.</param>
        /// <param name="buffer">The buffer to store the received message.</param>
        /// <param name="length">The maximum size of the data to receive.</param>
        /// <param name="streamID">This is a return value that is set to indicate which
        /// stream the data was sent to.</param>
        public void ReceiveUnsent(string dataRetrievalID, byte[] buffer, int length, int streamID)
        {

        }

        /// <summary>
        /// ??
        /// </summary>
        /// <param name="dataRetrievalID">The identification passed to the application in the
        /// failure notification.</param>
        /// <param name="buffer">The buffer to store the received message.</param>
        /// <param name="length">The maximum size of the data to receive.</param>
        /// <param name="streamID">This is a return value that is set to indicate which
        /// stream the data was sent to.</param>
        public void ReceiveUnacknowledged(string dataRetrievalID, byte[] buffer, int length, int streamID)
        {

        }

        /// <summary>
        /// Release the resources for the specified SCTP instance.
        /// </summary>
        /// <param name="instanceName"></param>
        public void Destroy(string instanceName)
        {

        }
    }
}
