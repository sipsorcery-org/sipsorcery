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

        private uint _verificationTag;
        private uint _tsn;

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
        private uint _remoteTSN;
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
        public event Action<byte[]> OnData;

        /// <summary>
        /// Create a new SCTP association instance where the INIT will be generated
        /// from this end of the connection.
        /// </summary>
        /// <param name="sctpTransport"></param>
        /// <param name="destination"></param>
        /// <param name="sctpSourcePort"></param>
        /// <param name="sctpDestinationPort"></param>
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
            _verificationTag = Crypto.GetRandomUInt();
            _tsn = Crypto.GetRandomUInt();

            ID = Guid.NewGuid().ToString();
            ARwnd = DEFAULT_ADVERTISED_RECEIVE_WINDOW;

            SetState(SctpAssociationState.Closed);
        }

        /// <summary>
        /// Create a new SCTP association instance where the INIT will be generated
        /// from this end of the connection.
        /// </summary>
        /// <param name="sctpTransport"></param>
        /// <param name="destination"></param>
        /// <param name="cookie"></param>
        public SctpAssociation(
            SctpTransport sctpTransport,
            SctpTransportCookie cookie)
        {
            _sctpTransport = sctpTransport;

            _sctpSourcePort = cookie.SourcePort;
            _sctpDestinationPort = cookie.DestinationPort;
            _verificationTag = cookie.Tag;
            _tsn = cookie.TSN;
            ARwnd = cookie.ARwnd;
            Destination = !string.IsNullOrEmpty(cookie.RemoteEndPoint) ?
                IPSocket.Parse(cookie.RemoteEndPoint) : null;
            _remoteVerificationTag = cookie.RemoteTag;
            _remoteTSN = cookie.RemoteTSN;
            _remoteARwnd = cookie.RemoteARwnd;

            ID = Guid.NewGuid().ToString();

            SetState(SctpAssociationState.Established);
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

                        _remoteVerificationTag = initAckChunk.InitiateTag;
                        _remoteTSN = initAckChunk.InitialTSN;
                        _remoteARwnd = initAckChunk.ARwnd;

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
                                _remoteTSN = dataChunk.TSN;
                                var sackChunk = new SctpSackChunk(_remoteTSN, ARwnd);
                                SendChunk(sackChunk);

                                if (dataChunk.UserData != null)
                                {
                                    OnData?.Invoke(dataChunk.UserData);
                                }

                                break;

                            case SctpChunkType.HEARTBEAT:
                                // The HEARTBEAT ACK sends back the same chunk but with the type changed.
                                chunk.ChunkType = (byte)SctpChunkType.HEARTBEAT_ACK;
                                SendChunk(chunk);
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

        public void Send(string message)
        {
            SctpDataChunk dataChunk = new SctpDataChunk(
                _tsn,
                Encoding.UTF8.GetBytes(message));
            SendChunk(dataChunk);

            _tsn = (_tsn == UInt32.MaxValue) ? 0 : _tsn++;
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

            SctpShutdownChunk shutdownChunk = new SctpShutdownChunk(_remoteTSN);
            SendChunk(shutdownChunk);
        }

        private void SetState(SctpAssociationState state)
        {
            logger.LogDebug($"SCTP state for association {ID} changed to {state}.");
            State = state;
            OnAssociationStateChanged?.Invoke(state);
        }

        private void SendInit()
        {
            // A packet containing an INIT chunk MUST have a zero Verification Tag (Pg 15).
            SctpPacket init = new SctpPacket(_sctpSourcePort, _sctpDestinationPort, 0);

            SctpInitChunk initChunk = new SctpInitChunk(SctpChunkType.INIT, _verificationTag, _tsn, ARwnd);
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

            _sctpTransport.Send(ID, buffer, 0, buffer.Length);
        }
    }
}
