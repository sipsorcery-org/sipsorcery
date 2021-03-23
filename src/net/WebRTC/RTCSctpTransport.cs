//-----------------------------------------------------------------------------
// Filename: RTCSctpTransport.cs
//
// Description: Represents a DTLS based transport for sending and receiving
// SCTP packets. This transport in turn forms the base for WebRTC data
// channels.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 22 Mar 2021	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Tls;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum RTCSctpTransportState
    {
        Connecting,
        Connected,
        Closed
    };

    /// <summary>
    /// Represents an SCTP transport that uses a DTLS transport.
    /// </summary>
    /// <remarks>
    /// DTLS encapsulation of SCTP: 
    /// https://tools.ietf.org/html/rfc8261
    /// 
    /// WebRTC API RTCSctpTransport Interface definition:
    /// https://www.w3.org/TR/webrtc/#webidl-1410933428
    /// </remarks>
    public class RTCSctpTransport : SctpTransport
    {
        /// <summary>
        /// The DTLS transport has no mechanism to cancel a pending receive. The workaround is
        /// to set a timeout on each receive call.
        /// </summary>
        private const int RECEIVE_TIMEOUT_MILLISECONDS = 1000;

        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// The transport over which all SCTP packets for data channels 
        /// will be sent and received.
        /// </summary>
        public readonly DatagramTransport transport;

        /// <summary>
        /// Indicates the role of this peer in the DTLS connection. This influences
        /// the selection of stream ID's for SCTP messages.
        /// </summary>
        public readonly bool IsDtlsClient;

        /// <summary>
        /// The current state of the SCTP transport.
        /// </summary>
        public RTCSctpTransportState state { get; private set; }

        /// <summary>
        /// The maximum size of data that can be passed to RTCDataChannel's send() method.
        /// </summary>
        public readonly double maxMessageSize;

        /// <summary>
        /// The maximum number of data channel's that can be used simultaneously (where each
        /// data channel is a stream on the same SCTP association).
        /// </summary>
        public readonly ushort maxChannels;

        public RTCPeerSctpAssociation RTCSctpAssociation { get; private set; }

        /// <summary>
        /// Event for notifications about changes to the SCTP transport state.
        /// </summary>
        public event Action<RTCSctpTransportState> OnStateChanged;

        private bool _isStarted;
        private bool _isClosed;

        public RTCSctpTransport(DatagramTransport dtlsTransport, bool isDtlsClient)
        {
            transport = dtlsTransport;
            IsDtlsClient = isDtlsClient;

            SetState(RTCSctpTransportState.Closed);
        }

        /// <summary>
        /// Starts the SCTP transport receive thread.
        /// </summary>
        public void Start()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                var receiveThread = new Thread(DoReceive);
                receiveThread.Start();
            }
        }

        /// <summary>
        /// Attempts to create and initialise a new SCTP association with the remote party. Only one  
        /// end of the SCTP connection needs to initiate the association.
        /// </summary>
        /// <param name="sourcePort">The source port to use for the SCTP association.</param>
        /// <param name="destinationPort">The destination port to use for the SCTP association.</param>
        public void Associate(ushort sourcePort, ushort destinationPort)
        {
            SetState(RTCSctpTransportState.Connecting);

            RTCSctpAssociation = new RTCPeerSctpAssociation(this, sourcePort, destinationPort);
            RTCSctpAssociation.OnAssociationStateChanged += OnAssociationStateChanged;
            RTCSctpAssociation.Init();
        }

        /// <summary>
        /// Closes the SCTP association and stops the receive thread.
        /// </summary>
        public void Close()
        {
            RTCSctpAssociation?.Shutdown();
            _isClosed = true;
        }

        /// <summary>
        /// Event handler to coordinate changes to the SCTP association state with the overall
        /// SCTP transport state.
        /// </summary>
        /// <param name="associationState">The state of the SCTP association.</param>
        private void OnAssociationStateChanged(SctpAssociationState associationState)
        {
            if(associationState == SctpAssociationState.Established)
            {
                SetState(RTCSctpTransportState.Connected);
            }
            else if(associationState == SctpAssociationState.Closed)
            {
                SetState(RTCSctpTransportState.Closed);
            }
        }

        /// <summary>
        /// Sets the state for the SCTP transport.
        /// </summary>
        /// <param name="newState">The new state to set.</param>
        private void SetState(RTCSctpTransportState newState)
        {
            state = newState;
            OnStateChanged?.Invoke(state);
        }

        /// <summary>
        /// This method runs on a dedicated thread to listen for incoming SCTP
        /// packets on the DTLS transport.
        /// </summary>
        private void DoReceive(object state)
        {
            byte[] recvBuffer = new byte[SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW];

            while (!_isClosed)
            {
                try
                {
                    int bytesRead = transport.Receive(recvBuffer, 0, recvBuffer.Length, RECEIVE_TIMEOUT_MILLISECONDS);

                    if (bytesRead == DtlsSrtpTransport.DTLS_RETRANSMISSION_CODE)
                    {
                        // Timed out waiting for a packet.
                        continue;
                    }
                    else if (bytesRead > 0)
                    {
                        var pkt = SctpPacket.Parse(recvBuffer, 0, bytesRead);
                        logger.LogDebug($"SCTP Packet received {pkt.Header.DestinationPort}<-{pkt.Header.SourcePort}.");
                        foreach (var chunk in pkt.Chunks)
                        {
                            logger.LogDebug($" chunk {chunk.KnownType}.");
                        }

                        if (pkt.Chunks.Any(x => x.KnownType == SctpChunkType.INIT))
                        {
                            // INIT packets have specific processing rules in order to prevent resource exhaustion.
                            // See Section 5 of RFC 4960 https://tools.ietf.org/html/rfc4960#section-5 "Association Initialization".
                            var initAckPacket = base.GetInitAck(pkt, null);
                            var buffer = initAckPacket.GetBytes();

                            logger.LogTrace($"SCTP sending INIT ACK chunk {initAckPacket.Header.DestinationPort}->{initAckPacket.Header.SourcePort}.");

                            Send(null, buffer, 0, buffer.Length);
                        }
                        else if (pkt.Chunks.Any(x => x.KnownType == SctpChunkType.COOKIE_ECHO))
                        {
                            var cookieEcho = pkt.Chunks.Single(x => x.KnownType == SctpChunkType.COOKIE_ECHO);
                            var cookie = base.GetCookie(cookieEcho, out var errorPacket);

                            if (cookie.IsEmpty() || errorPacket != null)
                            {
                                logger.LogWarning($"SCTP error acquiring handshake cookie from COOKIE ECHO chunk.");
                            }
                            else
                            {
                                RTCSctpAssociation = new RTCPeerSctpAssociation(this, cookie);

                                var cookieAckChunk = new SctpChunk(SctpChunkType.COOKIE_ACK);
                                var cookieAckPkt = RTCSctpAssociation.GetPacket(cookieAckChunk);
                                var cookieAckBuffer = cookieAckPkt.GetBytes();

                                logger.LogTrace($"SCTP sending COOKIE ACK chunk {cookieAckPkt.Header.DestinationPort}->{cookieAckPkt.Header.SourcePort}.");

                                Send(RTCSctpAssociation.ID, cookieAckBuffer, 0, cookieAckBuffer.Length);

                                SetState(RTCSctpTransportState.Connected);
                            }
                        }
                        else
                        {
                            RTCSctpAssociation.OnPacketReceived(pkt);
                        }
                    }
                    else if (bytesRead == DtlsSrtpTransport.DTLS_RECEIVE_ERROR_CODE)
                    {
                        // The DTLS transport has been closed or is no longer available.
                        if (!_isClosed)
                        {
                            logger.LogWarning($"SCTP the RTCSctpTransport DTLS transport returned an error.");
                        }
                        break;
                    }
                    else
                    {
                        // Assume something has gone wrong with the DTLS transport.
                        logger.LogError($"SCTP unexpected result on RTCSctpTransport DoReceive {bytesRead}.");
                        break;
                    }
                }
                catch (ApplicationException appExcp)
                {
                    // Treat application exceptions as recoverable, things like SCTP packet parse failures.
                    logger.LogWarning($"SCTP error processing association receive {appExcp.Message}.");
                }
            }

            logger.LogInformation("SCTP association receive loop stopped.");

            SetState(RTCSctpTransportState.Closed);
        }

        /// <summary>
        /// This method is called by the SCTP association when it wants to send an SCTP packet
        /// to the remote party.
        /// </summary>
        /// <param name="associationID">Not used for the DTLS transport.</param>
        /// <param name="buffer">The buffer containing the data to send.</param>
        /// <param name="offset">The position in the buffer to send from.</param>
        /// <param name="length">The number of bytes to send.</param>
        public override void Send(string associationID, byte[] buffer, int offset, int length)
        {
            if (!_isClosed)
            {
                transport.Send(buffer, offset, length);
            }
        }
    }
}
