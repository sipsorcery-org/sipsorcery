//-----------------------------------------------------------------------------
// Filename: RTCPeerSctpAssociation.cs
//
// Description: Represents an SCTP association on top of the DTLS
// transport. Each peer connection only requires a single SCTP 
// association. Multiple data channels can be created on top
// of the association.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Jul 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
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
    /// 
    /// </summary>
    /// <remarks>
    /// https://www.w3.org/TR/webrtc/#webidl-1410933428
    /// </remarks>
    public class RTCSctpTransport : ISctpTransport
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

        private RTCPeerSctpAssociation _rtcSctpAssociation;

        private bool _isStarted;
        private bool _isClosed;

        public RTCSctpTransport(DatagramTransport dtlsTransport, bool isDtlsClient)
        {
            transport = dtlsTransport;
            IsDtlsClient = isDtlsClient;
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
        /// Attempts to initialise the SCTP association with the remote party.
        /// </summary>
        /// <param name="sourcePort">The source port to use for the SCTP association.</param>
        /// <param name="destinationPort">The destination port to use for the SCTP association.</param>
        public void Associate(ushort sourcePort, ushort destinationPort)
        {
            _rtcSctpAssociation = new RTCPeerSctpAssociation(this, Guid.NewGuid().ToString(), sourcePort, destinationPort);
            _rtcSctpAssociation.Init();
        }

        /// <summary>
        /// Closes the SCTP association and stops the receive thread.
        /// </summary>
        public void Close()
        {
            _isClosed = true;
        }

        /// <summary>
        /// This method runs on a dedicated thread to listen for incoming SCTP
        /// packets on the DTLS transport.
        /// </summary>
        private void DoReceive(object state)
        {
            byte[] recvBuffer = new byte[_rtcSctpAssociation.ARwnd];

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
                        _rtcSctpAssociation.OnPacketReceived(pkt);
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
                catch(ApplicationException appExcp)
                {
                    // Treat application exceptions as recoverable, things like SCTP packet parse failures.
                    logger.LogWarning($"SCTP error processing association receive {appExcp.Message}.");
                }
            }

            logger.LogInformation("SCTP association receive loop stopped.");
        }

        /// <summary>
        /// This method is called by the SCTP association when it wants to send an SCTP packet
        /// to the remote party.
        /// </summary>
        /// <param name="associationID">Not used for the DTLS transport.</param>
        /// <param name="buffer">The buffer containing the data to send.</param>
        /// <param name="offset">The position in the buffer to send from.</param>
        /// <param name="length">The number of bytes to send.</param>
        public void Send(string associationID, byte[] buffer, int offset, int length)
        {
            if (!_isClosed)
            {
                transport.Send(buffer, offset, length);
            }
        }
    }

    public class RTCPeerSctpAssociation : SctpAssociation
    {
        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// The DTLS transport to send and receive SCTP packets on.
        /// </summary>
        private RTCSctpTransport _rtcSctpTransport;

        /// <summary>
        /// Indicates whether the SCTP association is ready for communications.
        /// </summary>
        public bool IsAssociated { get; private set; } = false;

        /// <summary>
        /// Event to indicate the SCTP association is ready.
        /// </summary>
        public Action OnAssociated;

        /// <summary>
        /// Event to indicate an SCTP stream has been opened. The stream open
        /// could have been initiated by a new data channel request on the local
        /// or remote peer.
        /// </summary>
        /// <remarks>
        /// Parameters:
        ///  - The newly opened stream,
        ///  - A boolean indicating whether the stream ID is from a local create stream request or not.
        /// </remarks>
        //public Action<SCTPStream, bool> OnSCTPStreamOpen;

        /// <summary>
        /// Creates a new SCTP association with the remote peer.
        /// </summary>
        /// <param name="rtcSctpTransport">The DTLS transport that will be used to encapsulate the
        /// SCTP packets.</param>
        /// <param name="isClient">True if this peer will be the client within the association. This
        /// dictates whether streams created use odd or even ID's.</param>
        /// <param name="srcPort">The source port to use when forming the association.</param>
        /// <param name="dstPort">The destination port to use when forming the association.</param>
        public RTCPeerSctpAssociation(RTCSctpTransport rtcSctpTransport, string id, ushort srcPort, ushort dstPort)
            : base(rtcSctpTransport, id, null, srcPort, dstPort)
        {
            _rtcSctpTransport = rtcSctpTransport;

            logger.LogDebug($"SCTP creating association is client { _rtcSctpTransport.IsDtlsClient} {srcPort}:{dstPort}.");

            //_sctpAssociation = new ThreadedAssociation(dtlsTransport, this, isClient, srcPort, dstPort);
        }

        /// <summary>
        /// Initiates the association with the remote peer.
        /// </summary>
        public void Associate()
        {
            //_sctpAssociation.associate();
        }

        /// <summary>
        /// Closes all the streams for this SCTP association.
        /// </summary>
        public void Close()
        {
            //if (_sctpAssociation != null)
            //{
            //    logger.LogDebug($"SCTP closing all streams for association.");

            //    foreach (int streamID in _sctpAssociation.allStreams())
            //    {
            //        _sctpAssociation.getStream(streamID)?.close();
            //        _sctpAssociation.delStream(streamID);
            //    }
            //}
        }

        /// <summary>
        /// Creates a new SCTP stream to act as a WebRTC data channel.
        /// </summary>
        /// <param name="label">Optional. The label to attach to the stream.</param>
        //public Task<SCTPStream> CreateStream(string label)
        //{
        //    logger.LogDebug($"SCTP creating stream for label {label}.");
        //    return Task.FromResult(_sctpAssociation.mkStream(label));
        //}

        /// <summary>
        /// Event handler for the SCTP association successfully initialising.
        /// </summary>
        //public void onAssociated(Association a)
        //{
        //    IsAssociated = true;
        //    OnAssociated?.Invoke();
        //}

        /// <summary>
        /// Event handler for a new data channel being created by the remote peer.
        /// </summary>
        /// <param name="s">The SCTP stream that was created.</param>
        /// <param name="label">The label for the stream. Can be empty and can also be a duplicate.</param>
        /// <param name="payloadProtocolID">The payload protocol ID of the new stream.</param>
        //public void onDCEPStream(SCTPStream s, string label, int payloadProtocolID)
        //{
        //    logger.LogDebug($"SCTP data channel stream opened for label {label}, ppid {payloadProtocolID}, stream id {s.getNum()}.");

        //    bool isLocalStreamID = (_isClient) ? s.getNum() % 2 == 0 : s.getNum() % 2 != 0;

        //    OnSCTPStreamOpen?.Invoke(s, isLocalStreamID);
        //}

        //public void onDisAssociated(Association a)
        //{
        //    logger.LogDebug($"SCTP disassociated.");
        //}

        /// <summary>
        /// Event handler that gets fired as part of creating a new data channel.
        /// </summary>
        //public void onRawStream(SCTPStream s)
        //{
        //    // Do nothing.
        //}
    }
}
