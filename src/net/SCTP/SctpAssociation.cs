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

    public class SctpAssociation
    {
        public const uint DEFAULT_ADVERTISED_RECEIVE_WINDOW = 131072U;

        private static ILogger logger = LogFactory.CreateLogger<SctpAssociation>();

        ISctpTransport _sctpTransport;
        private string _id;
        private SctpAssociationState _associationState;
        private ushort _sctpSourcePort;
        private ushort _sctpDestinationPort;

        private uint _verificationTag;
        private uint _tsn;
        
        /// <summary>
        /// Advertised Receiver Window Credit. This value represents the dedicated 
        /// buffer space, in number of bytes, that will be used for the receive buffer 
        /// for this association.
        /// </summary>
        public uint ARwnd { get; private set; }

        private uint _remoteVerificationTag;
        private uint _remoteTSN;

        /// <summary>
        /// The remote destination end point for this association. The underlying transport
        /// will supply this field if it is needed (the UDP encapsulation transport needs it,
        /// the DTSL transport does not).
        /// </summary>
        public IPEndPoint Destination { get; private set; }

        public event Action<SctpAssociationState> OnAssociationStateChanged;
        public event Action<byte[]> OnData;

        public SctpAssociation(
            ISctpTransport sctpTransport,
            string id,
            IPEndPoint destination,
            ushort sctpSourcePort,
            ushort sctpDestinationPort)
        {
            _sctpTransport = sctpTransport;
            _id = id;
            Destination = destination;
            _sctpSourcePort = sctpSourcePort;
            _sctpDestinationPort = sctpDestinationPort;
            _verificationTag = Crypto.GetRandomUInt();
            _tsn = Crypto.GetRandomUInt();
            ARwnd = DEFAULT_ADVERTISED_RECEIVE_WINDOW;
        }

        public void Init()
        {
            if (_associationState == SctpAssociationState.Closed)
            {
                SendInit();
            }
            else
            {
                logger.LogWarning($"SCTP Association cannot initialise an association in state {_associationState}.");
            }
        }

        /// <summary>
        /// Implements the SCTP association state machine.
        /// </summary>
        /// <param name="packet">An SCTP packet received from the remote party.</param>
        internal void OnPacketReceived(SctpPacket packet)
        {
            switch (_associationState)
            {
                case SctpAssociationState.Closed:
                    // TODO.
                    break;

                case SctpAssociationState.CookieWait:
                    if (packet.Chunks.Any(x => x.KnownType == SctpChunkType.INIT_ACK))
                    {
                        var initAckChunk = packet.Chunks.Where(x => x.KnownType == SctpChunkType.INIT_ACK).Single() as SctpInitChunk;

                        _remoteVerificationTag = initAckChunk.InitiateTag;
                        _remoteTSN = initAckChunk.InitialTSN;

                        var cookie = initAckChunk.OptionalParameters.Where(x => x.KnownType == SctpChunkParameterType.StateCookie)
                            .Single().ParameterValue;
                        SendCoookieEcho(cookie);
                    }
                    else
                    {
                        logger.LogWarning($"SCTP association in COOKIE WAIT state received packet without INIT ACK chunk, ignoring.");
                    }
                    break;

                case SctpAssociationState.CookieEchoed:
                    if (packet.Chunks.Any(x => x.KnownType == SctpChunkType.COOKIE_ACK))
                    {
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

        public void Shutdown()
        {
            SetState(SctpAssociationState.ShutdownSent);

            SctpShutdownChunk shutdownChunk = new SctpShutdownChunk(_remoteTSN);
            SendChunk(shutdownChunk);
        }

        private void SetState(SctpAssociationState state)
        {
            logger.LogDebug($"SCTP state for association {_id} changed to {state}.");
            _associationState = state;
            OnAssociationStateChanged?.Invoke(state);
        }

        private void SendInit()
        {
            // A packet containing an INIT chunk MUST have a zero Verification Tag (Pg 15).
            SctpPacket init = new SctpPacket(_sctpSourcePort, _sctpDestinationPort, 0);

            SctpInitChunk initChunk = new SctpInitChunk(SctpChunkType.INIT, _verificationTag, _tsn, ARwnd);
            init.Chunks.Add(initChunk);

            SetState(SctpAssociationState.CookieWait);

            byte[] buffer = init.GetBytes();
            _sctpTransport.Send(_id, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Sends the COOKIE ECHO packet after the INIT ACK has been received.
        /// </summary>
        /// <param name="initChunk">The INIT chunk from the INIT ACK packet.</param>
        private void SendCoookieEcho(byte[] cookie)
        {
            SctpCookieEchoChunk cookieEchoChunk = new SctpCookieEchoChunk();
            cookieEchoChunk.Cookie = cookie;

            SendChunk(cookieEchoChunk);

            SetState(SctpAssociationState.CookieEchoed);
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
            _sctpTransport.Send(_id, buffer, 0, buffer.Length);
        }
    }
}
