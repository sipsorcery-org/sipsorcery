﻿//-----------------------------------------------------------------------------
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
        public DatagramTransport transport { get; private set; }

        /// <summary>
        /// Indicates the role of this peer in the DTLS connection. This influences
        /// the selection of stream ID's for SCTP messages.
        /// </summary>
        public bool IsDtlsClient { get; private set; }

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

        public RTCSctpTransport(ushort sourcePort, ushort destinationPort)
        {
            SetState(RTCSctpTransportState.Closed);

            RTCSctpAssociation = new RTCPeerSctpAssociation(this, sourcePort, destinationPort);
            RTCSctpAssociation.OnAssociationStateChanged += OnAssociationStateChanged;
        }

        /// <summary>
        /// Attempts to update the SCTP source port the association managed by this transport will use.
        /// </summary>
        /// <param name="port">The updated source port.</param>
        public void UpdateSourcePort(ushort port)
        {
            if (state != RTCSctpTransportState.Closed)
            {
                logger.LogWarning($"SCTP source port cannot be updated when the transport is in state {state}.");
            }
            else
            {
                RTCSctpAssociation.UpdateSourcePort(port);
            }
        }

        /// <summary>
        /// Attempts to update the SCTP destination port the association managed by this transport will use.
        /// </summary>
        /// <param name="port">The updated destination port.</param>
        public void UpdateDestinationPort(ushort port)
        {
            if (state != RTCSctpTransportState.Closed)
            {
                logger.LogWarning($"SCTP destination port cannot be updated when the transport is in state {state}.");
            }
            else
            {
                RTCSctpAssociation.UpdateDestinationPort(port);
            }
        }

        /// <summary>
        /// Starts the SCTP transport receive thread.
        /// </summary>
        public void Start(DatagramTransport dtlsTransport, bool isDtlsClient)
        {
            if (!_isStarted)
            {
                _isStarted = true;

                transport = dtlsTransport;
                IsDtlsClient = isDtlsClient;

                var receiveThread = new Thread(DoReceive);
                receiveThread.Start();
            }
        }

        /// <summary>
        /// Attempts to create and initialise a new SCTP association with the remote party.
        /// </summary>
        /// <param name="sourcePort">The source port to use for the SCTP association.</param>
        /// <param name="destinationPort">The destination port to use for the SCTP association.</param>
        public void Associate()
        {
            SetState(RTCSctpTransportState.Connecting);
            RTCSctpAssociation.Init();
        }

        /// <summary>
        /// Closes the SCTP association and stops the receive thread.
        /// </summary>
        public void Close()
        {
            if (state == RTCSctpTransportState.Connected)
            {
                RTCSctpAssociation?.Shutdown();
            }
            _isClosed = true;
        }

        /// <summary>
        /// Event handler to coordinate changes to the SCTP association state with the overall
        /// SCTP transport state.
        /// </summary>
        /// <param name="associationState">The state of the SCTP association.</param>
        private void OnAssociationStateChanged(SctpAssociationState associationState)
        {
            if (associationState == SctpAssociationState.Established)
            {
                SetState(RTCSctpTransportState.Connected);
            }
            else if (associationState == SctpAssociationState.Closed)
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
        /// Gets a cookie to send in an INIT ACK chunk. This SCTP
        /// transport for a WebRTC peer connection needs to use the same
        /// local tag and TSN in every chunk as only a single association
        /// is ever maintained.
        /// </summary>
        protected override SctpTransportCookie GetInitAckCookie(
            ushort sourcePort,
            ushort destinationPort,
            uint remoteTag,
            uint remoteTSN,
            uint remoteARwnd,
            string remoteEndPoint,
            int lifeTimeExtension = 0)
        {
            var cookie = new SctpTransportCookie
            {
                SourcePort = sourcePort,
                DestinationPort = destinationPort,
                RemoteTag = remoteTag,
                RemoteTSN = remoteTSN,
                RemoteARwnd = remoteARwnd,
                RemoteEndPoint = remoteEndPoint,
                Tag = RTCSctpAssociation.VerificationTag,
                TSN = RTCSctpAssociation.TSN,
                ARwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW,
                CreatedAt = DateTime.Now.ToString("o"),
                Lifetime = DEFAULT_COOKIE_LIFETIME_SECONDS + lifeTimeExtension,
                HMAC = string.Empty
            };

            return cookie;
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
                        // Timed out waiting for a packet, this is by design and the receive attempt should
                        // be retired.
                        continue;
                    }
                    else if (bytesRead > 0)
                    {
                        if (!SctpPacket.VerifyChecksum(recvBuffer, 0, bytesRead))
                        {
                            logger.LogWarning($"SCTP packet received on DTLS transport dropped due to invalid checksum.");
                        }
                        else
                        {
                            var pkt = SctpPacket.Parse(recvBuffer, 0, bytesRead);

                            if (pkt.Chunks.Any(x => x.KnownType == SctpChunkType.INIT))
                            {
                                var initChunk = pkt.Chunks.First(x => x.KnownType == SctpChunkType.INIT) as SctpInitChunk;
                                logger.LogDebug($"SCTP INIT packet received, initial tag {initChunk.InitiateTag}, initial TSN {initChunk.InitialTSN}.");

                                GotInit(pkt, null);
                            }
                            else if (pkt.Chunks.Any(x => x.KnownType == SctpChunkType.COOKIE_ECHO))
                            {
                                // The COOKIE ECHO chunk is the 3rd step in the SCTP handshake when the remote party has
                                // requested a new association be created.
                                var cookie = base.GetCookie(pkt);

                                if (cookie.IsEmpty())
                                {
                                    logger.LogWarning($"SCTP error acquiring handshake cookie from COOKIE ECHO chunk.");
                                }
                                else
                                {
                                    RTCSctpAssociation.GotCookie(cookie);

                                    if (pkt.Chunks.Count() > 1)
                                    {
                                        // There could be DATA chunks after the COOKIE ECHO chunk.
                                        RTCSctpAssociation.OnPacketReceived(pkt);
                                    }
                                }
                            }
                            else
                            {
                                RTCSctpAssociation.OnPacketReceived(pkt);
                            }
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
                    logger.LogWarning($"SCTP error processing RTCSctpTransport receive. {appExcp.Message}");
                }
            }

            logger.LogInformation("SCTP association receive thread stopped.");

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
