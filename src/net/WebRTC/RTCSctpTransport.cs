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
using System.Buffers;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;
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
        private const string THREAD_NAME_PREFIX = "rtcsctprecv-";

        /// <summary>
        /// The DTLS transport has no mechanism to cancel a pending receive. The workaround is
        /// to set a timeout on each receive call.
        /// </summary>
        private const int RECEIVE_TIMEOUT_MILLISECONDS = 1000;

        /// <summary>
        /// The default maximum size of payload that can be sent on a data channel.
        /// </summary>
        /// <remarks>
        /// https://www.w3.org/TR/webrtc/#sctp-transport-update-mms
        /// </remarks>
        internal const uint SCTP_DEFAULT_MAX_MESSAGE_SIZE = 262144;

        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// The SCTP ports are redundant for a DTLS transport. There will only ever be one
        /// SCTP association so the SCTP ports do not need to be used for end point matching.
        /// </summary>
        public override bool IsPortAgnostic => true;

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
        /// <remarks>
        /// See https://www.w3.org/TR/webrtc/#sctp-transport-update-mms.
        /// </remarks>
        public uint maxMessageSize => SCTP_DEFAULT_MAX_MESSAGE_SIZE;

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

        private Once _isStarted;
        private Once _isClosed;
        private Thread _receiveThread;

        /// <summary>
        /// Creates a new SCTP transport that runs on top of an established DTLS connection.
        /// </summary>
        /// <param name="sourcePort">The SCTP source port.</param>
        /// <param name="destinationPort">The SCTP destination port.</param>
        /// <param name="dtlsPort">Optional. The local UDP port being used for the DTLS connection. This
        /// will be set on the SCTP association to aid in diagnostics.</param>
        public RTCSctpTransport(ushort sourcePort, ushort destinationPort, int dtlsPort)
        {
            SetState(RTCSctpTransportState.Closed);

            RTCSctpAssociation = new RTCPeerSctpAssociation(this, sourcePort, destinationPort, dtlsPort);
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
            if (_isStarted.TryMarkOccurred())
            {
                transport = dtlsTransport;
                IsDtlsClient = isDtlsClient;

                _receiveThread = new Thread(DoReceive);
                _receiveThread.Name = $"{THREAD_NAME_PREFIX}{RTCSctpAssociation.ID}";
                _receiveThread.IsBackground = true;
                _receiveThread.Start();
            }
            else
            {
                logger.LogWarning($"RTCSctpTransport for association {RTCSctpAssociation.ID} has already been started.");
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
            _isClosed.TryMarkOccurred();
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
#if NET6_0_OR_GREATER
            Span<byte> recvBuffer = stackalloc byte[checked((int)SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW)];
#else
            byte[] recvBufferArray = new byte[SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW];
            Span<byte> recvBuffer = recvBufferArray.AsSpan();
#endif

            while (!_isClosed.HasOccurred)
            {
                try
                {
#if NET6_0_OR_GREATER
                    int bytesRead = transport.Receive(recvBuffer, RECEIVE_TIMEOUT_MILLISECONDS);
#else
                    int bytesRead = transport.Receive(recvBufferArray, 0, recvBuffer.Length, RECEIVE_TIMEOUT_MILLISECONDS);
#endif

                    if (bytesRead == DtlsSrtpTransport.DTLS_RETRANSMISSION_CODE)
                    {
                        // Timed out waiting for a packet, this is by design and the receive attempt should
                        // be retired.
                        continue;
                    }
                    else if (bytesRead > 0)
                    {
                        if (!SctpPacket.VerifyChecksum(recvBuffer.Slice(0, bytesRead)))
                        {
                            logger.LogWarning($"SCTP packet received on DTLS transport dropped due to invalid checksum.");
                        }
                        else
                        {
                            var pkt = SctpPacketView.Parse(recvBuffer.Slice(0, bytesRead));

                            if (pkt.Has(SctpChunkType.INIT))
                            {
                                var initChunk = pkt.GetChunk(SctpChunkType.INIT);
                                logger.LogDebug($"SCTP INIT packet received, initial tag {initChunk.InitiateTag}, initial TSN {initChunk.InitialTSN}.");

                                GotInit(pkt, null);
                            }
                            else if (pkt.Has(SctpChunkType.COOKIE_ECHO))
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

                                    if (pkt.ChunkCount > 1)
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
                    else if (_isClosed.HasOccurred)
                    {
                        // The DTLS transport has been closed or is no longer available.
                        logger.LogWarning($"SCTP the RTCSctpTransport DTLS transport returned an error.");
                        break;
                    }
                }
                catch (ApplicationException appExcp)
                {
                    // Treat application exceptions as recoverable, things like SCTP packet parse failures.
                    logger.LogWarning($"SCTP error processing RTCSctpTransport receive. {appExcp.Message}");
                }
                catch(TlsFatalAlert alert)  when (alert.InnerException is SocketException)
                {
                    var sockExcp = alert.InnerException as SocketException;
                    logger.LogWarning($"SCTP RTCSctpTransport receive socket failure {sockExcp.SocketErrorCode}.");
                    break;
                }
                catch (Exception excp)
                {
                    logger.LogError($"SCTP fatal error processing RTCSctpTransport receive. {excp}");
                    break;
                }
            }

            if (!_isClosed.HasOccurred)
            {
                logger.LogWarning($"SCTP association {RTCSctpAssociation.ID} receive thread stopped.");
            }

            SetState(RTCSctpTransportState.Closed);
        }

        /// <summary>
        /// This method is called by the SCTP association when it wants to send an SCTP packet
        /// to the remote party.
        /// </summary>
        /// <param name="associationID">Not used for the DTLS transport.</param>
        public override void Send(string associationID, ReadOnlySpan<byte> data)
        {
            if (data.Length > maxMessageSize)
            {
                throw new ApplicationException($"RTCSctpTransport was requested to send data of length {data.Length} " +
                    $" that exceeded the maximum allowed message size of {maxMessageSize}.");
            }

            if (!_isClosed.HasOccurred)
            {
                lock (transport)
                {
#if NET6_0_OR_GREATER
                    transport.Send(data);
#else
                    byte[] tmp = ArrayPool<byte>.Shared.Rent(data.Length);
                    try
                    {
                        data.CopyTo(tmp);
                        transport.Send(tmp, 0, data.Length);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(tmp);
                    }
#endif
                }
            }
        }
    }
}
