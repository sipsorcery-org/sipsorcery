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
using System.Threading;
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
        public const uint DEFAULT_ADVERTISED_RECEIVE_WINDOW = 262144U;
        public const int DEFAULT_NUMBER_OUTBOUND_STREAMS = 65535;
        public const int DEFAULT_NUMBER_INBOUND_STREAMS = 65535; 
        private const byte SHUTDOWN_CHUNK_TBIT_FLAG = 0x01;

        /// <summary>
        /// Length of time to wait for the INIT ACK response after sending an INIT.
        /// </summary>
        private const int T1_INIT_TIMER_MILLISECONDS = 1000;

        private const int MAX_INIT_RETRANSMITS = 3;

        /// <summary>
        /// Length of time to wait for the COOKIE ACK response after sending a COOKIE ECHO.
        /// </summary>
        private const int T1_COOKIE_TIMER_MILLISECONDS = 1000;

        private const int MAX_COOKIE_ECHO_RETRANSMITS = 3;
         
        private static ILogger logger = LogFactory.CreateLogger<SctpAssociation>();

        SctpTransport _sctpTransport;
        private ushort _sctpSourcePort;
        private ushort _sctpDestinationPort;
        private ushort _defaultMTU;
        private ushort _numberOutboundStreams;
        private ushort _numberInboundStreams;
        private bool _wasAborted;
        private bool _wasShutdown;
        private bool _initialisationFailed;
        private int _initRetransmits;
        private int _cookieEchoRetransmits;

        /// <summary>
        /// Handles logic for DATA chunk receives (fragmentation, in order delivery etc).
        /// </summary>
        private SctpDataReceiver _dataReceiver;

        /// <summary>
        /// Handles logic for sending DATA chunks (retransmits, windows management etc).
        /// </summary>
        private SctpDataSender _dataSender;

        /// <summary>
        /// T1 init timer to monitor an INIT request sent to a remote peer.
        /// </summary>
        /// <remarks>
        /// https://tools.ietf.org/html/rfc4960#section-5.1 (section A)
        /// </remarks>
        private Timer _t1Init;

        /// <summary>
        /// T1 init timer to monitor an COOKIE ECHO request sent to a remote peer.
        /// </summary>
        /// <remarks>
        /// https://tools.ietf.org/html/rfc4960#section-5.1 (section C)
        /// </remarks>
        private Timer _t1Cookie;

        /// <summary>
        /// The total size (in bytes) of outgoing user data queued in the <see cref="SctpDataSender"/>.
        /// </summary>
        public ulong SendBufferedAmount => _dataSender?.BufferedAmount ?? 0;

        public uint VerificationTag { get; private set; }

        /// <summary>
        /// Transaction Sequence Number (TSN). A monotonically increasing number that must be
        /// included in every DATA chunk.
        /// </summary>
        public uint TSN => _dataSender.TSN;

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
        private uint _remoteInitialTSN;

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

        /// <summary>
        /// Event to notify application that the association state has changed.
        /// </summary>
        public event Action<SctpAssociationState> OnAssociationStateChanged;

        /// <summary>
        /// Event to notify application that user data is available.
        /// </summary>
        public event Action<SctpDataFrame> OnData;

        /// <summary>
        /// Event to notify the application that the remote party aborted this
        /// association.
        /// </summary>
        public event Action<string> OnAbortReceived;

        /// <summary>
        /// Event to notify the application that an error occurred that caused
        /// the association to be aborted locally.
        /// </summary>
        public event Action<string> OnAborted;

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
        /// <param name="localTransportPort">Optional. The local transport (e.g. UDP or DTLS) port being 
        /// used for the underlying SCTP transport. This be set on the SCTP association's ID to aid in 
        /// diagnostics.</param>
        public SctpAssociation(
            SctpTransport sctpTransport,
            IPEndPoint destination,
            ushort sctpSourcePort,
            ushort sctpDestinationPort,
            ushort defaultMTU,
            int localTransportPort,
            ushort numberOutboundStreams = DEFAULT_NUMBER_OUTBOUND_STREAMS,
            ushort numberInboundStreams = DEFAULT_NUMBER_INBOUND_STREAMS)
        {
            _sctpTransport = sctpTransport;
            Destination = destination;
            _sctpSourcePort = sctpSourcePort;
            _sctpDestinationPort = sctpDestinationPort;
            _defaultMTU = defaultMTU;
            _numberOutboundStreams = numberOutboundStreams;
            _numberInboundStreams = numberInboundStreams;
            VerificationTag = Crypto.GetRandomUInt(true);

            ID = $"{sctpSourcePort}:{sctpDestinationPort}:{localTransportPort}";
            ARwnd = DEFAULT_ADVERTISED_RECEIVE_WINDOW;

            _dataReceiver = new SctpDataReceiver(ARwnd, _defaultMTU, 0);
            _dataSender = new SctpDataSender(ID, this.SendChunk, defaultMTU, Crypto.GetRandomUInt(true), DEFAULT_ADVERTISED_RECEIVE_WINDOW);

            State = SctpAssociationState.Closed;
        }

        /// <summary>
        /// Create a new SCTP association instance from the cookie that was previously
        /// sent to the remote party in an INIT ACK chunk.
        /// </summary>
        public SctpAssociation(
            SctpTransport sctpTransport,
            SctpTransportCookie cookie,
            int localTransportPort)
        {
            _sctpTransport = sctpTransport;
            ID = $"{cookie.SourcePort}:{cookie.DestinationPort}:{localTransportPort}";
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

        /// <summary>
        /// Attempts to initialise the association by sending an INIT chunk to the remote peer.
        /// </summary>
        public void Init()
        {
            if (_wasAborted || _wasShutdown || _initialisationFailed)
            {
                logger.LogWarning($"SCTP association cannot be initialised after an abort or shutdown.");
            }
            else if (State == SctpAssociationState.Closed)
            {
                SendInit();
            }
            else
            {
                logger.LogWarning($"SCTP association cannot be initialised in state {State}.");
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
            if (_wasAborted || _wasShutdown)
            {
                logger.LogWarning($"SCTP association cannot initialise with a cookie after an abort or shutdown.");
            }
            else if (!(State == SctpAssociationState.Closed || State == SctpAssociationState.CookieEchoed))
            {
                throw new ApplicationException($"SCTP association cannot initialise with a cookie in state {State}.");
            }
            else
            {
                _sctpSourcePort = cookie.SourcePort;
                _sctpDestinationPort = cookie.DestinationPort;
                VerificationTag = cookie.Tag;
                ARwnd = cookie.ARwnd;
                Destination = !string.IsNullOrEmpty(cookie.RemoteEndPoint) ?
                    IPSocket.Parse(cookie.RemoteEndPoint) : null;

                if (_dataReceiver == null)
                {
                    _dataReceiver = new SctpDataReceiver(ARwnd, _defaultMTU, cookie.RemoteTSN);
                }

                if (_dataSender == null)
                {
                    _dataSender = new SctpDataSender(ID, this.SendChunk, _defaultMTU, cookie.TSN, cookie.RemoteARwnd);
                }

                InitRemoteProperties(cookie.RemoteTag, cookie.RemoteTSN, cookie.RemoteARwnd);

                var cookieAckChunk = new SctpChunk(SctpChunkType.COOKIE_ACK);
                SendChunk(cookieAckChunk);

                SetState(SctpAssociationState.Established);
                _dataSender.StartSending();
                CancelTimers();
            }
        }

        /// <summary>
        /// Initialises the association's properties that record the state of the remote party.
        /// </summary>
        internal void InitRemoteProperties(
            uint remoteVerificationTag,
            uint remoteInitialTSN,
            uint remoteARwnd)
        {
            _remoteVerificationTag = remoteVerificationTag;
            _remoteInitialTSN = remoteInitialTSN;

            _dataReceiver.SetInitialTSN(remoteInitialTSN);
            _dataSender.SetReceiverWindow(remoteARwnd);
        }

        /// <summary>
        /// Implements the SCTP association state machine.
        /// </summary>
        /// <param name="packet">An SCTP packet received from the remote party.</param>
        /// <remarks>
        /// SCTP Association State Diagram:
        /// https://tools.ietf.org/html/rfc4960#section-4
        /// </remarks>
        internal void OnPacketReceived(SctpPacket packet)
        {
            if (_wasAborted)
            {
                logger.LogWarning($"SCTP packet received but association has been aborted, ignoring.");
            }
            else if (packet.Header.VerificationTag != VerificationTag)
            {
                logger.LogWarning($"SCTP packet dropped due to wrong verification tag, expected " +
                    $"{VerificationTag} got {packet.Header.VerificationTag}.");
            }
            else if (!_sctpTransport.IsPortAgnostic && packet.Header.DestinationPort != _sctpSourcePort)
            {
                logger.LogWarning($"SCTP packet dropped due to wrong SCTP destination port, expected " +
                                    $"{_sctpSourcePort} got {packet.Header.DestinationPort}.");
            }
            else if (!_sctpTransport.IsPortAgnostic && packet.Header.SourcePort != _sctpDestinationPort)
            {
                logger.LogWarning($"SCTP packet dropped due to wrong SCTP source port, expected " +
                                    $"{_sctpDestinationPort} got {packet.Header.SourcePort}.");
            }
            else
            {
                foreach (var chunk in packet.Chunks)
                {
                    var chunkType = (SctpChunkType)chunk.ChunkType;

                    switch (chunkType)
                    {
                        case SctpChunkType.ABORT:
                            string abortReason = (chunk as SctpAbortChunk).GetAbortReason();
                            logger.LogWarning($"SCTP packet ABORT chunk received from remote party, reason {abortReason}.");
                            _wasAborted = true;
                            OnAbortReceived?.Invoke(abortReason);
                            break;

                        case var ct when ct == SctpChunkType.COOKIE_ACK && State != SctpAssociationState.CookieEchoed:
                            // https://tools.ietf.org/html/rfc4960#section-5.2.5
                            // At any state other than COOKIE-ECHOED, an endpoint should silently
                            // discard a received COOKIE ACK chunk.
                            break;

                        case var ct when ct == SctpChunkType.COOKIE_ACK && State == SctpAssociationState.CookieEchoed:
                            SetState(SctpAssociationState.Established);
                            CancelTimers();
                            _dataSender.StartSending();
                            break;

                        case SctpChunkType.COOKIE_ECHO:
                            // In standard operation an SCTP association gets created when the parent transport 
                            // receives a COOKIE ECHO chunk. The association gets initialised from the chunk and 
                            // does not need to process it.
                            // The scenarios in https://tools.ietf.org/html/rfc4960#section-5.2 describe where
                            // an association could receive a COOKIE ECHO.
                            break;

                        case SctpChunkType.DATA:
                            var dataChunk = chunk as SctpDataChunk;

                            if (dataChunk.UserData == null || dataChunk.UserData.Length == 0)
                            {
                                // Fatal condition:
                                // - If an endpoint receives a DATA chunk with no user data (i.e., the
                                //   Length field is set to 16), it MUST send an ABORT with error cause
                                //   set to "No User Data". (RFC4960 pg. 80)
                                Abort(new SctpErrorNoUserData { TSN = (chunk as SctpDataChunk).TSN });
                            }
                            else
                            {
                                logger.LogTrace($"SCTP data chunk received on ID {ID} with TSN {dataChunk.TSN}, payload length {dataChunk.UserData.Length}, flags {dataChunk.ChunkFlags:X2}.");

                                // A received data chunk can result in multiple data frames becoming available.
                                // For example if a stream has out of order frames already received and the next
                                // in order frame arrives then all the in order ones will be supplied.
                                var sortedFrames = _dataReceiver.OnDataChunk(dataChunk);

                                var sack = _dataReceiver.GetSackChunk();
                                if (sack != null)
                                {
                                    SendChunk(sack);
                                }

                                foreach (var frame in sortedFrames)
                                {
                                    OnData?.Invoke(frame);
                                }
                            }

                            break;

                        case SctpChunkType.ERROR:
                            var errorChunk = chunk as SctpErrorChunk;
                            foreach (var err in errorChunk.ErrorCauses)
                            {
                                logger.LogWarning($"SCTP error {err.CauseCode}.");
                            }
                            break;

                        case SctpChunkType.HEARTBEAT:
                            // The HEARTBEAT ACK sends back the same chunk but with the type changed.
                            chunk.ChunkType = (byte)SctpChunkType.HEARTBEAT_ACK;
                            SendChunk(chunk);
                            break;

                        case var ct when ct == SctpChunkType.INIT_ACK && State != SctpAssociationState.CookieWait:
                            // https://tools.ietf.org/html/rfc4960#section-5.2.3
                            // If an INIT ACK is received by an endpoint in any state other than the
                            // COOKIE - WAIT state, the endpoint should discard the INIT ACK chunk.
                            break;

                        case var ct when ct == SctpChunkType.INIT_ACK && State == SctpAssociationState.CookieWait:

                            if (_t1Init != null)
                            {
                                _t1Init.Dispose();
                                _t1Init = null;
                            }

                            var initAckChunk = chunk as SctpInitChunk;

                            if (initAckChunk.InitiateTag == 0 ||
                                initAckChunk.NumberInboundStreams == 0 ||
                                initAckChunk.NumberOutboundStreams == 0)
                            {
                                // Fatal conditions:
                                //  - The Initiate Tag MUST NOT take the value 0. (RFC4960 pg 30).
                                //  - Note: A receiver of an INIT ACK with the OS value set to 0 SHOULD
                                //    destroy the association discarding its TCB. (RFC4960 pg 31).
                                //  - Note: A receiver of an INIT ACK with the MIS value set to 0 SHOULD
                                //    destroy the association discarding its TCB. (RFC4960 pg 31).
                                Abort(new SctpCauseOnlyError(SctpErrorCauseCode.InvalidMandatoryParameter));
                            }
                            else
                            {
                                InitRemoteProperties(initAckChunk.InitiateTag, initAckChunk.InitialTSN, initAckChunk.ARwnd);

                                var cookie = initAckChunk.StateCookie;

                                // The cookie chunk parameter can be changed to a COOKE ECHO CHUNK by changing the first two bytes.
                                // But it's more convenient to create a new chunk.
                                var cookieEchoChunk = new SctpChunk(SctpChunkType.COOKIE_ECHO) { ChunkValue = cookie };
                                var cookieEchoPkt = GetControlPacket(cookieEchoChunk);

                                if (initAckChunk.UnrecognizedPeerParameters.Count > 0)
                                {
                                    var errChunk = new SctpErrorChunk();

                                    foreach (var unrecognised in initAckChunk.UnrecognizedPeerParameters)
                                    {
                                        var unrecognisedParams = new SctpErrorUnrecognizedParameters { UnrecognizedParameters = unrecognised.GetBytes() };
                                        errChunk.AddErrorCause(unrecognisedParams);
                                    }

                                    cookieEchoPkt.AddChunk(errChunk);
                                }

                                SendPacket(cookieEchoPkt);
                                SetState(SctpAssociationState.CookieEchoed);

                                _t1Cookie = new Timer(T1CookieTimerExpired, cookieEchoPkt, T1_COOKIE_TIMER_MILLISECONDS, T1_COOKIE_TIMER_MILLISECONDS);
                            }
                            break;

                        case var ct when ct == SctpChunkType.INIT_ACK && State != SctpAssociationState.CookieWait:
                            logger.LogWarning($"SCTP association received INIT_ACK chunk in wrong state of {State}, ignoring.");
                            break;

                        case SctpChunkType.SACK:
                            _dataSender.GotSack(chunk as SctpSackChunk);
                            break;

                        case var ct when ct == SctpChunkType.SHUTDOWN && State == SctpAssociationState.Established:
                            // TODO: Check outstanding data chunks.
                            var shutdownAck = new SctpChunk(SctpChunkType.SHUTDOWN_ACK);
                            SendChunk(shutdownAck);
                            SetState(SctpAssociationState.ShutdownAckSent);
                            break;

                        case var ct when ct == SctpChunkType.SHUTDOWN_ACK && State == SctpAssociationState.ShutdownSent:
                            SetState(SctpAssociationState.Closed);
                            var shutCompleteChunk = new SctpChunk(SctpChunkType.SHUTDOWN_COMPLETE,
                                (byte)(_remoteVerificationTag != 0 ? SHUTDOWN_CHUNK_TBIT_FLAG : 0x00));
                            var shutCompletePkt = GetControlPacket(shutCompleteChunk);
                            shutCompletePkt.Header.VerificationTag = packet.Header.VerificationTag;
                            SendPacket(shutCompletePkt);
                            break;

                        case var ct when ct == SctpChunkType.SHUTDOWN_COMPLETE &&
                                (State == SctpAssociationState.ShutdownAckSent || State == SctpAssociationState.ShutdownSent):
                            _wasShutdown = true;
                            SetState(SctpAssociationState.Closed);
                            break;

                        default:
                            logger.LogWarning($"SCTP association no rule for {chunkType} in state of {State}.");
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Sends a DATA chunk to the remote peer.
        /// </summary>
        /// <param name="streamID">The stream ID to sent the data on.</param>
        /// <param name="ppid">The payload protocol ID for the data.</param>
        /// <param name="message">The string data to send.</param>
        public void SendData(ushort streamID, uint ppid, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentNullException("The message cannot be empty when sending a data chunk on an SCTP association.");
            }

            SendData(streamID, ppid, Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// Sends a DATA chunk to the remote peer.
        /// </summary>
        /// <param name="streamID">The stream ID to sent the data on.</param>
        /// <param name="ppid">The payload protocol ID for the data.</param>
        /// <param name="message">The byte data to send.</param>
        public void SendData(ushort streamID, uint ppid, byte[] data)
        {
            if (_wasAborted)
            {
                logger.LogWarning($"SCTP send data is not allowed on an aborted association.");
            }
            else if (!(State == SctpAssociationState.Established ||
                      State == SctpAssociationState.ShutdownPending ||
                      State == SctpAssociationState.ShutdownReceived))
            {
                logger.LogWarning($"SCTP send data is not allowed for an association in state {State}.");
            }
            else
            {
                _dataSender.SendData(streamID, ppid, data);
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

            pkt.AddChunk(chunk);

            return pkt;
        }

        /// <summary>
        /// Initiates the shutdown of the association by sending a shutdown
        /// control chunk to the remote party.
        /// </summary>
        public void Shutdown()
        {
            if (!_wasAborted)
            {
                SetState(SctpAssociationState.ShutdownPending);

                // TODO: Check outstanding data chunks.

                // If no DATA chunks have been received use the initial TSN - 1 from 
                // the remote party. Seems weird to use the - 1, and couldn't find anything
                // in the RFC that says to do it, but that's what usrsctp accepts.
                uint? ackTSN = _dataReceiver.CumulativeAckTSN ?? _remoteInitialTSN - 1;

                logger.LogTrace($"SCTP sending shutdown for association {ID}, ACK TSN {ackTSN}.");

                SetState(SctpAssociationState.ShutdownSent);

                SctpShutdownChunk shutdownChunk = new SctpShutdownChunk(ackTSN);
                SendChunk(shutdownChunk);

                _dataSender.Close();
            }
        }

        /// <summary>
        /// Sends an SCTP control packet with an abort chunk to terminate 
        /// the association.
        /// </summary>
        /// <param name="errorCause">The cause of the abort.</param>
        public void Abort(ISctpErrorCause errorCause)
        {
            if (!_wasAborted)
            {
                _wasAborted = true;
                bool tBit = _remoteVerificationTag != 0;
                var abortChunk = new SctpAbortChunk(tBit);
                abortChunk.AddErrorCause(errorCause);

                SendChunk(abortChunk);

                OnAborted?.Invoke(errorCause.CauseCode.ToString());

                _dataSender.Close();
            }
        }

        /// <summary>
        /// Updates the state of the association.
        /// </summary>
        /// <param name="state">The new association state.</param>
        internal void SetState(SctpAssociationState state)
        {
            logger.LogTrace($"SCTP state for association {ID} changed to {state}.");
            State = state;
            OnAssociationStateChanged?.Invoke(state);
        }

        /// <summary>
        /// Attempts to create an association with a remote party by sending an initialisation
        /// control chunk.
        /// </summary>
        private void SendInit()
        {
            if (!_wasAborted)
            {
                // A packet containing an INIT chunk MUST have a zero Verification Tag (RFC4960 Pg 15).
                SctpPacket init = new SctpPacket(_sctpSourcePort, _sctpDestinationPort, 0);

                SctpInitChunk initChunk = new SctpInitChunk(
                    SctpChunkType.INIT,
                    VerificationTag,
                    TSN,
                    ARwnd,
                    _numberOutboundStreams,
                    _numberInboundStreams);
                init.AddChunk(initChunk);

                SetState(SctpAssociationState.CookieWait);

                byte[] buffer = init.GetBytes();
                _sctpTransport.Send(ID, buffer, 0, buffer.Length);

                _t1Init = new Timer(T1InitTimerExpired, init, T1_INIT_TIMER_MILLISECONDS, T1_INIT_TIMER_MILLISECONDS);
            }
        }

        /// <summary>
        /// Sends a SCTP chunk to the remote party.
        /// </summary>
        /// <param name="chunk">The chunk to send.</param>
        internal void SendChunk(SctpChunk chunk)
        {
            if (!_wasAborted)
            {
                SctpPacket pkt = new SctpPacket(
                _sctpSourcePort,
                _sctpDestinationPort,
                _remoteVerificationTag);

                pkt.AddChunk(chunk);

                byte[] buffer = pkt.GetBytes();

                _sctpTransport.Send(ID, buffer, 0, buffer.Length);
            }
        }

        /// <summary>
        /// Sends an SCTP packet to the remote peer.
        /// </summary>
        /// <param name="pkt">The packet to send.</param>
        private void SendPacket(SctpPacket pkt)
        {
            if (!_wasAborted)
            {
                byte[] buffer = pkt.GetBytes();
                _sctpTransport.Send(ID, buffer, 0, buffer.Length);
            }
        }

        private void CancelTimers()
        {
            if (_t1Init != null)
            {
                _t1Init.Dispose();
                _t1Init = null;
            }

            if (_t1Cookie != null)
            {
                _t1Cookie.Dispose();
                _t1Cookie = null;
            }
        }

        private void T1InitTimerExpired(object state)
        {
            if (_initRetransmits >= MAX_INIT_RETRANSMITS)
            {
                _t1Init.Dispose();
                _t1Init = null;
                _initialisationFailed = true;

                logger.LogWarning($"SCTP timed out waiting for INIT ACK chunk from remote peer.");

                SetState(SctpAssociationState.Closed);
            }
            else
            {
                var init = state as SctpPacket;
                SendPacket(init);
                _initRetransmits++;
            }
        }

        private void T1CookieTimerExpired(object state)
        {
            if (_cookieEchoRetransmits >= MAX_COOKIE_ECHO_RETRANSMITS)
            {
                _t1Cookie.Dispose();
                _t1Cookie = null;
                _initialisationFailed = true;

                logger.LogWarning($"SCTP timed out waiting for COOKIE ACK chunk from remote peer.");

                SetState(SctpAssociationState.Closed);
            }
            else
            {
                var cookieEchoPkt = state as SctpPacket;
                SendPacket(cookieEchoPkt);
                _cookieEchoRetransmits++;
            }
        }
    }
}
