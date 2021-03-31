//-----------------------------------------------------------------------------
// Filename: SctpAssociation.cs
//
// Description: Represents an SCTP Association.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum SctpAssociationState
    {
        Closed,
        CookieWait,
        CookieEchoed,
        Established,
        ShutdownPending,
        ShutdownSent,
        ShutdownReceived,
        ShutdownAckSent
    }

    /// <summary>
    /// Represents the current status of an SCTP association.
    /// </summary>
    /// <remarks>
    /// The address list items have not been included due to the assumption
    /// they are not relevant for SCTP encapsulated in UDP.
    /// The status data is defined on page 115 of the SCTP RFC
    /// https://tools.ietf.org/html/rfc4960#page-115.
    /// </remarks>
    public struct SctpStatus
    {
        public SctpAssociationState AssociationConnectionState;
        public int ReceiverWindowSize;
        public int CongestionWindowSizes;
        public int UnacknowledgedChunksCount;
        public int PendingReceiptChunksCount;
    }

    /// <summary>
    /// An SCTP association represents an established connection between two SCTP endpoints.
    /// This class also represents the Transmission Control Block (TCB) referred to in RFC4960.
    /// </summary>
    public class SctpAssociation
    {
        public const uint DEFAULT_ADVERTISED_RECEIVE_WINDOW = 131072U;

        private static ILogger logger = LogFactory.CreateLogger<SctpAssociation>();

        SctpTransport _sctpTransport;
        private ushort _sctpSourcePort;
        private ushort _sctpDestinationPort;
        private ushort _defaultMTU;
        private SctpDataFramer _framer;

        public uint VerificationTag { get; private set; }

        public uint TSN { get; private set; }

        /// <summary>
        /// A unique ID for this association. The ID is not part of the SCTP protocol. It
        /// is provided as a convenience measure in case a transport of application needs
        /// to keep track of multiple associations.
        /// </summary>
        public readonly string ID;

        /// <summary>
        /// Advertised Receiver Window Credit. This value represents the dedicated 
        /// buffer space, in number of bytes, that will be used for the receive buffer 
        /// for this association.
        /// </summary>
        public uint ARwnd { get; private set; }

        private uint _remoteVerificationTag;
        private uint _remoteExpectedTSN;
        private uint _remoteARwnd;

        /// <summary>
        /// The remote destination end point for this association. The underlying transport
        /// will supply this field if it is needed (the UDP encapsulation transport needs it,
        /// the DTSL transport does not).
        /// </summary>
        public IPEndPoint Destination { get; private set; }

        /// <summary>
        /// Indicates the current connection state of the association.
        /// </summary>
        public SctpAssociationState State { get; private set; }

        public event Action<SctpAssociationState> OnAssociationStateChanged;
        public event Action<SctpDataFrame> OnData;

        /// <summary>
        /// Create a new SCTP association instance where the INIT will be generated
        /// from this end of the connection.
        /// </summary>
        /// <param name="sctpTransport">The transport layer doing the actual sending and receiving of
        /// packets, e.g. UDP, DTLS, raw sockets etc.</param>
        /// <param name="destination">Optional. The remote destination end point for this association.
        /// Some transports, such as DTLS, are already established and do not use this parameter.</param>
        /// <param name="sctpSourcePort">The source port for the SCTP packet header.</param>
        /// <param name="sctpDestinationPort">The destination port for the SCTP packet header.</param>
        /// <param name="defaultMTU">The default Maximum Transmission Unit (MTU) for the underlying
        /// transport. This determines the maximum size of an SCTP packet that will be used with
        /// the transport.</param>
        public SctpAssociation(
            SctpTransport sctpTransport,
            IPEndPoint destination,
            ushort sctpSourcePort,
            ushort sctpDestinationPort,
            ushort defaultMTU)
        {
            _sctpTransport = sctpTransport;
            Destination = destination;
            _sctpSourcePort = sctpSourcePort;
            _sctpDestinationPort = sctpDestinationPort;
            _defaultMTU = defaultMTU;
            VerificationTag = Crypto.GetRandomUInt();
            TSN = Crypto.GetRandomUInt();
            _framer = new SctpDataFramer(ARwnd, _defaultMTU, 0);

            ID = Guid.NewGuid().ToString();
            ARwnd = DEFAULT_ADVERTISED_RECEIVE_WINDOW;
            State = SctpAssociationState.Closed;
        }

        /// <summary>
        /// Create a new SCTP association instance from the cookie that was previously
        /// sent to the remote party in an INIT ACK chunk.
        /// </summary>
        public SctpAssociation(
            SctpTransport sctpTransport,
            SctpTransportCookie cookie)
        {
            _sctpTransport = sctpTransport;
            ID = Guid.NewGuid().ToString();
            State = SctpAssociationState.Closed;

            GotCookie(cookie);
        }

        /// <summary>
        /// Attempts to update the association's SCTP source port.
        /// </summary>
        /// <param name="port">The updated source port.</param>
        public void UpdateSourcePort(ushort port)
        {
            if (State != SctpAssociationState.Closed)
            {
                logger.LogWarning($"SCTP source port cannot be updated when the association is in state {State}.");
            }
            else
            {
                _sctpSourcePort = port;
            }
        }

        /// <summary>
        /// Attempts to update the association's SCTP destination port.
        /// </summary>
        /// <param name="port">The updated destination port.</param>
        public void UpdateDestinationPort(ushort port)
        {
            if (State != SctpAssociationState.Closed)
            {
                logger.LogWarning($"SCTP destination port cannot be updated when the association is in state {State}.");
            }
            else
            {
                _sctpDestinationPort = port;
            }
        }

        public void Init()
        {
            if (State == SctpAssociationState.Closed)
            {
                SendInit();
            }
            else
            {
                logger.LogWarning($"SCTP Association cannot initialise an association in state {State}.");
            }
        }

        /// <summary>
        /// Initialises the association state based on the echoed cookie (the cookie that we sent
        /// to the remote party and was then echoed back to us). An association can only be initialised
        /// from a cookie prior to it being used and prior to it ever having entered the established state.
        /// </summary>
        /// <param name="cookie">The echoed cookie that was returned from the remote party.</param>
        public void GotCookie(SctpTransportCookie cookie)
        {
            // The CookieEchoed state is allowed, even though a cookie should be creating a brand
            // new association rather than one that has already sent an INIT, in order to deal with
            // a race condition where both SCTP end points attempt to establish the association at
            // the same time using the same ports.
            if(!(State ==SctpAssociationState.Closed || State == SctpAssociationState.CookieEchoed))
            {
                throw new ApplicationException($"SCTP cannot initialise an SctpAssocation with a cookie in state {State}.");
            }
            else
            {
                _sctpSourcePort = cookie.SourcePort;
                _sctpDestinationPort = cookie.DestinationPort;
                VerificationTag = cookie.Tag;
                TSN = cookie.TSN;
                ARwnd = cookie.ARwnd;
                Destination = !string.IsNullOrEmpty(cookie.RemoteEndPoint) ?
                    IPSocket.Parse(cookie.RemoteEndPoint) : null;
                _framer = new SctpDataFramer(ARwnd, _defaultMTU, 0);

                InitRemoteProperties(cookie.RemoteTag, cookie.RemoteTSN, cookie.RemoteARwnd);

                var cookieAckChunk = new SctpChunk(SctpChunkType.COOKIE_ACK);
                SendChunk(cookieAckChunk);

                SetState(SctpAssociationState.Established);
            }
        }

        /// <summary>
        /// Initialises the association's properties that record the state of the remote party.
        /// </summary>
        internal void InitRemoteProperties(
            uint remoteVerificationTag,
            uint remoteExpectedTSN,
            uint remoteARwnd)
        {
            _remoteVerificationTag = remoteVerificationTag;
            _remoteExpectedTSN = remoteExpectedTSN;
            _remoteARwnd = remoteARwnd;

            _framer.SetInitialTSN(_remoteExpectedTSN);
        }

        /// <summary>
        /// Implements the SCTP association state machine.
        /// </summary>
        /// <param name="packet">An SCTP packet received from the remote party.</param>
        internal void OnPacketReceived(SctpPacket packet)
        {
            switch (State)
            {
                case SctpAssociationState.Closed:
                    // Send ABORT.
                    break;

                case SctpAssociationState.CookieWait:
                    if (packet.Chunks.Any(x => x.KnownType == SctpChunkType.INIT_ACK))
                    {
                        var initAckChunk = packet.Chunks.Where(x => x.KnownType == SctpChunkType.INIT_ACK).Single() as SctpInitChunk;

                        InitRemoteProperties(initAckChunk.InitiateTag, initAckChunk.InitialTSN, initAckChunk.ARwnd);

                        var cookie = initAckChunk.VariableParameters.Where(x => x.KnownType == SctpChunkParameterType.StateCookie)
                          .Single().ParameterValue;

                        // The cookie chunk parameter can be changed to a COOKE ECHO CHUNK by changing the first two bytes.
                        // But it's more convenient to create a new chunk.
                        var cookieEchoChunk = new SctpChunk(SctpChunkType.COOKIE_ECHO) { ChunkValue = cookie };
                        SendChunk(cookieEchoChunk);
                        SetState(SctpAssociationState.CookieEchoed);
                    }
                    else
                    {
                        logger.LogWarning($"SCTP association in COOKIE WAIT state received packet without INIT ACK chunk, ignoring.");
                    }
                    break;

                case SctpAssociationState.CookieEchoed:
                    if (packet.Chunks.Any(x => x.KnownType == SctpChunkType.COOKIE_ACK))
                    {
                        // Got the ACK for the COOKIE ECHO chunk we sent.
                        SetState(SctpAssociationState.Established);
                    }

                    break;

                case SctpAssociationState.Established:
                    foreach (var chunk in packet.Chunks)
                    {
                        switch (chunk.KnownType)
                        {
                            case SctpChunkType.DATA:
                                var dataChunk = chunk as SctpDataChunk;

                                var sackChunk = new SctpSackChunk(dataChunk.TSN, ARwnd);
                                SendChunk(sackChunk);

                                var frame = _framer.OnDataChunk(dataChunk);
                                if (!frame.IsEmpty())
                                {
                                    OnData?.Invoke(frame);
                                }

                                break;

                            case SctpChunkType.HEARTBEAT:
                                // The HEARTBEAT ACK sends back the same chunk but with the type changed.
                                chunk.ChunkType = (byte)SctpChunkType.HEARTBEAT_ACK;
                                SendChunk(chunk);
                                break;

                            case SctpChunkType.SACK:
                                var sackRecvChunk = chunk as SctpSackChunk;
                                //logger.LogDebug($"SCTP SACK TSN ACK={sackRecvChunk.CumulativeTsnAck}, # gap ack blocks {sackRecvChunk.NumberGapAckBlocks}" +
                                //    $", # duplicate tsn {sackRecvChunk.NumberDuplicateTSNs}.");
                                break;

                            case SctpChunkType.COOKIE_ACK:
                            case SctpChunkType.COOKIE_ECHO:
                                // NoOp. Will occur if a data chunk is included in same packet as the COOKIE_ACK or COOKIE_ECHO.
                                break;

                            default:
                                logger.LogWarning($"TODO: Add processing to SctpAssociation for chunk type {chunk.KnownType}.");
                                break;
                        }
                    }
                    break;

                case SctpAssociationState.ShutdownSent:
                    if (packet.Chunks.Any(x => x.KnownType == SctpChunkType.SHUTDOWN_ACK))
                    {
                        SetState(SctpAssociationState.Closed);
                        var shutCompleteChunk = new SctpChunk(SctpChunkType.SHUTDOWN_COMPLETE);
                        SendChunk(shutCompleteChunk);
                    }
                    break;
            }
        }

        public void SendData(ushort streamID, ushort seqnum, uint ppid, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentNullException("The message cannot be empty when sending a data chunk on an SCTP association.");
            }

            SendData(streamID, seqnum, ppid, Encoding.UTF8.GetBytes(message));
        }

        public void SendData(ushort streamID, ushort seqnum, uint ppid, byte[] data)
        {
            for (int index = 0; index * _defaultMTU < data.Length; index++)
            {
                int offset = (index == 0) ? 0 : (index * _defaultMTU);
                int payloadLength = (offset + _defaultMTU < data.Length) ? _defaultMTU : data.Length - offset;

                // Future TODO: Replace with slice when System.Memory is introduced as a dependency.
                byte[] payload = new byte[payloadLength];
                Buffer.BlockCopy(data, offset, payload, 0, payloadLength);

                bool isBegining = index == 0;
                bool isEnd = ((offset + payloadLength) >= data.Length) ? true : false;

                SctpDataChunk dataChunk = new SctpDataChunk(
                false,
                isBegining,
                isEnd,
                TSN,
                streamID,
                seqnum,
                ppid,
                payload);

                SendChunk(dataChunk);

                TSN = (TSN == UInt32.MaxValue) ? 0 : TSN + 1;
            }
        }

        /// <summary>
        /// Gets an SCTP packet for a control (non-data) chunk.
        /// </summary>
        /// <param name="chunk">The control chunk to get a packet for.</param>
        /// <returns>A single control chunk SCTP packet.</returns>
        public SctpPacket GetControlPacket(SctpChunk chunk)
        {
            SctpPacket pkt = new SctpPacket(
           _sctpSourcePort,
           _sctpDestinationPort,
           _remoteVerificationTag);

            pkt.Chunks.Add(chunk);

            return pkt;
        }

        public void Shutdown()
        {
            SetState(SctpAssociationState.ShutdownSent);

            uint tsnAck = (_remoteExpectedTSN == 0) ? UInt32.MaxValue : _remoteExpectedTSN - 1;
            SctpShutdownChunk shutdownChunk = new SctpShutdownChunk(tsnAck);
            SendChunk(shutdownChunk);
        }

        internal void SetState(SctpAssociationState state)
        {
            //logger.LogDebug($"SCTP state for association {ID} changed to {state}.");
            State = state;
            OnAssociationStateChanged?.Invoke(state);
        }

        private void SendInit()
        {
            // A packet containing an INIT chunk MUST have a zero Verification Tag (Pg 15).
            SctpPacket init = new SctpPacket(_sctpSourcePort, _sctpDestinationPort, 0);

            SctpInitChunk initChunk = new SctpInitChunk(SctpChunkType.INIT, VerificationTag, TSN, ARwnd);
            init.Chunks.Add(initChunk);

            SetState(SctpAssociationState.CookieWait);

            //logger.LogTrace($"SCTP sending INIT chunk {_sctpSourcePort}->{_sctpDestinationPort}.");

            byte[] buffer = init.GetBytes();
            _sctpTransport.Send(ID, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Sends a SCTP chunk to the remote party.
        /// </summary>
        /// <param name="chunk">The chunk to send.</param>
        private void SendChunk(SctpChunk chunk)
        {
            SctpPacket pkt = new SctpPacket(
            _sctpSourcePort,
            _sctpDestinationPort,
            _remoteVerificationTag);

            pkt.Chunks.Add(chunk);

            byte[] buffer = pkt.GetBytes();

            //logger.LogTrace($"SCTP sending {chunk.KnownType} chunk {_sctpSourcePort}->{_sctpDestinationPort}.");
            //if (chunk is SctpDataChunk)
            //{
            //    logger.LogTrace($"SCTP send chunk TSN {(chunk as SctpDataChunk).TSN}.");
            //}

            _sctpTransport.Send(ID, buffer, 0, buffer.Length);
        }
    }
}
