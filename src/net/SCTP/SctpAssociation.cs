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
        private uint _duplicateTSN;

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
        public event Action<SctpDataChunk> OnDataChunk;

        /// <summary>
        /// Create a new SCTP association instance where the INIT will be generated
        /// from this end of the connection.
        /// </summary>
        public SctpAssociation(
            SctpTransport sctpTransport,
            IPEndPoint destination,
            ushort sctpSourcePort,
            ushort sctpDestinationPort)
        {
            _sctpTransport = sctpTransport;
            Destination = destination;
            _sctpSourcePort = sctpSourcePort;
            _sctpDestinationPort = sctpDestinationPort;
            VerificationTag = Crypto.GetRandomUInt();
            TSN = Crypto.GetRandomUInt();

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

            _sctpSourcePort = cookie.SourcePort;
            _sctpDestinationPort = cookie.DestinationPort;
            VerificationTag = cookie.Tag;
            TSN = cookie.TSN;
            ARwnd = cookie.ARwnd;
            Destination = !string.IsNullOrEmpty(cookie.RemoteEndPoint) ?
                IPSocket.Parse(cookie.RemoteEndPoint) : null;

            InitRemoteProperties(cookie.RemoteTag, cookie.RemoteTSN, cookie.RemoteARwnd);

            ID = Guid.NewGuid().ToString();

            SetState(SctpAssociationState.Established);
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
            if (State != SctpAssociationState.Closed )
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

                                // TDOD: Use sliding window to deal with TSN wrapping.
                                if (dataChunk.TSN >= _remoteExpectedTSN)
                                {
                                    _remoteExpectedTSN = (_remoteExpectedTSN == UInt32.MaxValue) ? 0 : _remoteExpectedTSN + 1;
                                    logger.LogTrace($"SCTP DATA chunk TSN {dataChunk.TSN}, PPID {dataChunk.PPID}, stream ID {dataChunk.StreamID}, seq num {dataChunk.StreamSeqNum}.");
                                    OnDataChunk?.Invoke(dataChunk);
                                }
                                else
                                {
                                    logger.LogDebug($"SCTP duplicate data chunk received with TSN {dataChunk.TSN}.");
                                    _duplicateTSN++;
                                }

                                break;

                            case SctpChunkType.HEARTBEAT:
                                // The HEARTBEAT ACK sends back the same chunk but with the type changed.
                                chunk.ChunkType = (byte)SctpChunkType.HEARTBEAT_ACK;
                                SendChunk(chunk);
                                break;

                            case SctpChunkType.SACK:
                                var sackRecvChunk = chunk as SctpSackChunk;
                                logger.LogDebug($"SCTP SACK TSN ACK={sackRecvChunk.CumulativeTsnAck}, # gap ack blocks {sackRecvChunk.NumberGapAckBlocks}" +
                                    $", # duplicate tsn {sackRecvChunk.NumberDuplicateTSNs}.");
                                break;

                            case SctpChunkType.COOKIE_ACK:
                            case SctpChunkType.COOKIE_ECHO:
                                // These chunks get processed by the SCTP transport layer prior to the association being instantiated.
                                // This is part of the SCTP design to mitigate resource depletion attacks in the handshake.
                                // There can be DATA or other chunks following the COOKIE chunks which is why this case may be hit.
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
            SctpDataChunk dataChunk = new SctpDataChunk(
                TSN,
                streamID,
                seqnum,
                ppid,
                data);
            SendChunk(dataChunk);

            TSN = (TSN == UInt32.MaxValue) ? 0 : TSN + 1;
        }

        public SctpPacket GetPacket(SctpChunk chunk)
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
            logger.LogDebug($"SCTP state for association {ID} changed to {state}.");
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

            logger.LogTrace($"SCTP sending INIT chunk {_sctpSourcePort}->{_sctpDestinationPort}.");

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

            logger.LogTrace($"SCTP sending {chunk.KnownType} chunk {_sctpSourcePort}->{_sctpDestinationPort}.");
            if (chunk is SctpDataChunk)
            {
                logger.LogDebug($"SCTP send chunk TSN {(chunk as SctpDataChunk).TSN}.");
            }

            //logger.LogTrace(buffer.HexStr());

            _sctpTransport.Send(ID, buffer, 0, buffer.Length);
        }
    }
}
