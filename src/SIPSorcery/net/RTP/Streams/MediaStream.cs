//-----------------------------------------------------------------------------
// Filename: MediaStream.cs
//
// Description: Define a Media Stream to centralize all related objects: local/remote tracks, rtcp session, ip end point
// The goal is to simplify RTPSession class
//
// Author(s):
// Christophe Irles
//
// History:
// 05 Apr 2022	Christophe Irles        Created (based on existing code from previous RTPSession class)
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class MediaStream
    {
        protected internal class PendingPackages
        {
            public RTPHeader hdr;
            public int localPort;
            public IPEndPoint remoteEndPoint;
            public byte[] buffer;

            public PendingPackages() { }

            public PendingPackages(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
            {
                this.hdr = hdr;
                this.localPort = localPort;
                this.remoteEndPoint = remoteEndPoint;
                this.buffer = buffer;
            }
        }

        protected object _pendingPackagesLock = new object();
        protected List<PendingPackages> _pendingPackagesBuffer = new List<PendingPackages>();

        private static  readonly ILogger logger = LogFactory.CreateLogger<MediaStream>();

        private RtpSessionConfig RtpSessionConfig;

        protected SecureContext SecureContext;
        protected SrtpHandler SrtpHandler;

        private RTPReorderBuffer RTPReorderBuffer = null;
        private readonly object _fecReceiveLock = new object();
        private RtpFecReceiveBuffer _fecReceiveBuffer;
        private TimeSpan? _explicitReceiveWait;
        private uint? _fecReceiveSsrc;
        private IPEndPoint _fecRemoteEndPoint;
        private Func<long> _fecReceiveClock;
        private Func<Action, IRtpFecReceiveTimer> _fecReceiveTimerFactory;

        MediaStreamTrack m_localTrack;
        MediaStreamTrack m_remoteTrack;

        protected RTPChannel rtpChannel = null;

        protected bool _isClosed = false;

        /// <summary>
        /// How long after the stream closes that sends are dropped quietly. A media source runs on
        /// its own thread and cannot be stopped synchronously with the close, so a short tail of
        /// sends is expected even from correct application code and is not worth reporting.
        /// </summary>
        public const int CLOSED_SEND_GRACE_PERIOD_MS = 2000;

        /// <summary>
        /// The minimum interval between warnings once sends are still arriving after the grace
        /// period. By then the media source genuinely has not been stopped, which the application
        /// needs to be told, but not once per packet.
        /// </summary>
        public const int CLOSED_SEND_WARNING_INTERVAL_MS = 5000;

        /// <summary>
        /// How many dropped sends between consulting the clock. The drop path runs at the media
        /// frame rate so it must not take a timestamp for every packet.
        /// </summary>
        private const int CLOSED_SEND_CLOCK_CHECK_INTERVAL = 50;

        private long _closedAtTicks = 0;                 // When the stream closed, 0 while it is open.
        private long _sendsAfterClose = 0;               // Sends dropped since the stream closed.
        private long _lastClosedSendWarningTicks = 0;    // When the last of those was reported, 0 if none has been.
        /// <summary>
        /// Used for keeping track of TWCC packets
        /// </summary>
        private ushort _twccPacketCount = 0;

        public int Index = -1;

        /// <summary>
        /// Tracks the global order in which this stream was added to the session,
        /// across all media types. Used to preserve m-line ordering per RFC 3264 §8.
        /// </summary>
        public int MediaInsertionOrder = -1;

        /// <summary>
        /// Snapshot of the negotiated track's forward error correction policy.
        /// </summary>
        internal RtpUlpfecConfig FecConfig { get; private set; } = new RtpUlpfecConfig();

        /// <summary>
        /// The active RFC 5109 / RFC 2198 FEC encoder when FEC is negotiated and enabled.
        /// </summary>
        public RtpUlpfecEncoder FecEncoder { get; private set; }

        /// <summary>
        /// The active RFC 5109 / RFC 2198 FEC decoder when FEC is negotiated and enabled.
        /// </summary>
        public RtpUlpfecDecoder FecDecoder { get; private set; }

        /// <summary>
        /// Metrics tracking media sends, FEC repair generation, send rates, and impairment drops.
        /// </summary>
        public RtpFecMetrics FecMetrics { get; } = new RtpFecMetrics();

        /// <summary>
        /// Enables ULPFEC / RED forward error correction on this stream with the negotiated payload types.
        /// </summary>
        internal void EnableUlpfec(int redPayloadType, int ulpfecPayloadType, int mediaPayloadType, RtpUlpfecConfig config = null)
        {
            var fecConfig = config?.Clone() ?? new RtpUlpfecConfig();
            if (fecConfig.MaxReceiveRepairWait < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(config), "The receive repair wait cannot be negative.");
            }
            lock (_fecReceiveLock)
            {
                FecConfig = fecConfig;
                _fecReceiveBuffer?.Dispose();
                _fecReceiveSsrc = null;
                ResetFecFraming();
                FecEncoder = new RtpUlpfecEncoder(redPayloadType, ulpfecPayloadType, mediaPayloadType, FecConfig);
                FecDecoder = new RtpUlpfecDecoder(redPayloadType, ulpfecPayloadType);
                _fecReceiveBuffer = CreateFecReceiveBuffer();
                logger.LogInformation("ULPFEC enabled on {MediaType} stream. RED PT={RedPt}, ULPFEC PT={FecPt}, Media PT={MediaPt}, Overhead={Overhead:P1}.",
                    MediaType, redPayloadType, ulpfecPayloadType, mediaPayloadType, FecConfig.TargetOverheadFraction);
            }
        }

        /// <summary>
        /// Disables forward error correction on this stream.
        /// </summary>
        internal void DisableUlpfec(string reason = null)
        {
            lock (_fecReceiveLock)
            {
                _fecReceiveBuffer?.Dispose();
                _fecReceiveBuffer = null;
                _fecReceiveSsrc = null;
                ResetFecFraming();
                if (FecEncoder != null || FecDecoder != null)
                {
                    logger.LogInformation("ULPFEC disabled on {MediaType} stream. Reason: {Reason}", MediaType, reason ?? "Disabled by caller");
                    FecEncoder = null;
                    FecDecoder = null;
                }
            }
        }

        private RtpFecReceiveBuffer CreateFecReceiveBuffer()
        {
            RtpFecReceiveBuffer receiver = null;
            receiver = new RtpFecReceiveBuffer(_explicitReceiveWait ?? FecConfig.MaxReceiveRepairWait,
                (packet, framingSequence) =>
                {
                    Action deliver = null;
                    lock (_fecReceiveLock)
                    {
                        if (!IsClosed && ReferenceEquals(_fecReceiveBuffer, receiver))
                        {
                            var format = LocalTrack?.GetFormatForPayloadID(packet.Header.PayloadType);
                            if (format.HasValue && format.Value.IsVideoMediaCodec)
                            {
                                if (RemoteTrack != null) RemoteTrack.LastRemoteSeqNum = packet.Header.SequenceNumber;
                                deliver = PrepareFecRtpPacket(_fecRemoteEndPoint, packet, format.Value, framingSequence);
                            }
                        }
                    }
                    // Preparing framing state is fenced against reset; application callbacks
                    // run outside the receive lock and cannot populate a replacement framer.
                    if (!IsClosed && ReferenceEquals(_fecReceiveBuffer, receiver)) deliver?.Invoke();
                }, _fecReceiveClock, _fecReceiveTimerFactory, IsFecFrameStart);
            return receiver;
        }

        internal void SetFecReceiveTiming(Func<long> clock, Func<Action, IRtpFecReceiveTimer> timerFactory)
        {
            _fecReceiveClock = clock;
            _fecReceiveTimerFactory = timerFactory;
        }

        internal void ResetFecReceiveState()
        {
            lock (_fecReceiveLock)
            {
                if (FecDecoder == null && _fecReceiveBuffer == null) return;
                FecDecoder?.Reset();
                // Replace the coordinator so an arrival already decoded before this reset cannot
                // re-enter the new source's window after releasing _fecReceiveLock.
                _fecReceiveBuffer?.Dispose();
                _fecReceiveBuffer = FecDecoder != null && !IsClosed ? CreateFecReceiveBuffer() : null;
                _fecReceiveSsrc = null;
                ResetFecFraming();
            }
        }

        protected virtual void ResetFecFraming() { }

        protected virtual Action PrepareFecRtpPacket(IPEndPoint remoteEndPoint, RTPPacket packet,
            SDPAudioVideoMediaFormat format, ushort framingSequence)
            => () => ProcessRtpPacket(remoteEndPoint, packet, format);

        private bool IsFecFrameStart(RTPPacket packet)
        {
            var format = LocalTrack?.GetFormatForPayloadID(packet.Header.PayloadType);
            var payload = packet.GetPayloadBytes();
            if (!format.HasValue || payload.Length == 0) return false;
            string codec = format.Value.Name();
            if (codec.Equals("VP8", StringComparison.OrdinalIgnoreCase))
            {
                return (payload[0] & 0x1f) == 0x10;
            }
            if (format.Value.IsH264)
            {
                int nalType = payload[0] & 0x1f;
                return (nalType >= 1 && nalType <= 24) ||
                    ((nalType == 28 || nalType == 29) && payload.Length > 1 && (payload[1] & 0x80) != 0);
            }
            if (codec.Equals("VP9", StringComparison.OrdinalIgnoreCase)) return (payload[0] & 0x08) != 0;
            if (codec.Equals("AV1", StringComparison.OrdinalIgnoreCase)) return (payload[0] & 0x80) == 0;
            if (format.Value.isH265 && payload.Length >= 2)
            {
                int nalType = (payload[0] >> 1) & 0x3f;
                return nalType < 49 || (nalType == 49 && payload.Length > 2 && (payload[2] & 0x80) != 0);
            }
            if (codec.Equals("JPEG", StringComparison.OrdinalIgnoreCase) && payload.Length >= 8)
            {
                return payload[1] == 0 && payload[2] == 0 && payload[3] == 0;
            }
            return false;
        }

        /// <summary>
        /// Fires when the connection for a media type is classified as timed out due to not
        /// receiving any RTP or RTCP packets within the given period.
        /// </summary>
        public event Action<int, SDPMediaTypesEnum> OnTimeoutByIndex;

        /// <summary>
        /// Gets fired when an RTCP report is sent. This event is for diagnostics only.
        /// </summary>
        public event Action<int, SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReportByIndex;

        /// <summary>
        /// Gets fired when an RTP packet is received from a remote party.
        /// Parameters are:
        ///  - index of the AudioStream or VideoStream
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The full RTP packet.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceivedByIndex;

        /// <summary>
        /// Gets fired when an RTP Header packet is received from a remote party.
        /// Parameters are:
        ///  - index of the AudioStream or VideoStream
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The RTP Header exension URI.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, string, object> OnRtpHeaderReceivedByIndex;

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
        /// </summary>
        public event Action<int, IPEndPoint, RTPEvent, RTPHeader> OnRtpEventByIndex;

        /// <summary>
        /// Gets fired when an RTCP report is received. This event is for diagnostics only.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReportByIndex;

        public event Action<bool> OnIsClosedStateChanged;

        public bool AcceptRtpFromAny { get; set; } = false;

        /// <summary>
        /// Indicates whether the session has been closed. Once a session is closed it cannot
        /// be restarted.
        /// </summary>
        public bool IsClosed
        {
            get
            {
                return _isClosed;
            }
            set
            {
                if (_isClosed == value)
                {
                    return;
                }
                _isClosed = value;

                // Stamp the transition so sends arriving afterwards can distinguish the expected
                // shutdown tail from a media source that was never stopped.
                Interlocked.Exchange(ref _closedAtTicks, _isClosed ? DateTime.Now.Ticks : 0);
                Interlocked.Exchange(ref _sendsAfterClose, 0);
                Interlocked.Exchange(ref _lastClosedSendWarningTicks, 0);

                //Clear previous buffer
                ClearPendingPackages();

                FecEncoder?.Reset();
                ResetFecReceiveState();

                OnIsClosedStateChanged?.Invoke(_isClosed);
            }
        }

        /// <summary>
        /// In order to detect RTP events from the remote party this property needs to 
        /// be negotiated to a common payload ID. RTP events are typically DTMF tones.
        /// </summary>
        public int NegotiatedRtpEventPayloadID { get; set; } = RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID;

        /// <summary>
        /// To type of this media
        /// </summary>
        public SDPMediaTypesEnum MediaType { get; set; }

        /// <summary>
        /// The local track. Will be null if we are not sending this media.
        /// </summary>
        public MediaStreamTrack LocalTrack
        {
            get
            {
                return m_localTrack;
            }
            set
            {
                m_localTrack = value;
                if (m_localTrack != null)
                {
                    // Need to create a sending SSRC and set it on the RTCP session. 
                    if (RtcpSession != null)
                    {
                        RtcpSession.Ssrc = m_localTrack.Ssrc;
                    }

                    if (MediaType == SDPMediaTypesEnum.audio)
                    {
                        if (m_localTrack.Capabilities != null && !m_localTrack.NoDtmfSupport &&
                            !m_localTrack.Capabilities.Any(x => x.ID == RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID))
                        {
                            m_localTrack.Capabilities.Add(DefaultRTPEventFormat);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The remote track. Will be null if the remote party is not sending this media
        /// </summary>
        public MediaStreamTrack RemoteTrack
        {
            get
            {
                return m_remoteTrack;
            }
            set
            {
                if (!ReferenceEquals(m_remoteTrack, value))
                {
                    ResetFecReceiveState();
                }
                m_remoteTrack = value;
            }
        }

        /// <summary>
        /// The reporting session for this media stream.
        /// </summary>
        public RTCPSession RtcpSession { get; set; }

        /// <summary>
        /// The remote RTP end point this stream is sending media to.
        /// </summary>
        public IPEndPoint DestinationEndPoint { get; set; }

        /// <summary>
        /// The remote RTP control end point this stream is sending to RTCP reports for the media stream to.
        /// </summary>
        public IPEndPoint ControlDestinationEndPoint { get; set; }

        /// <summary>
        /// This endpoint is used when a relay server (TURN) is being used for the RTP session. All RTP packets
        /// will be sent to the relay end point instead of the DestinationEndPoint.
        /// </summary>
        public TurnRelayEndPoint RtpRelayEndPoint { get; set; }

        /// <summary>
        /// This endpoint is used when a relay server (TURN) is being used for the RTCP session. All RTCP packets
        /// will be sent to the relay end point instead of the ControlDestinationEndPoint.
        /// </summary>
        public IPEndPoint RelayControlDestinationEndPoint { get; set; }

        /// <summary>
        /// If set to true indicates the RTP and RTCP sockets are for a relay server (TURN).
        /// All traffic for the session should then be sent to/from the relay and not updated.
        /// </summary>
        public bool IsUsingRelayEndPoint => RtpRelayEndPoint != null;

        /// <summary>
        /// Default RTP event format that we support.
        /// </summary>
        public static SDPAudioVideoMediaFormat DefaultRTPEventFormat
        {
            get
            {
                return new SDPAudioVideoMediaFormat(
                                SDPMediaTypesEnum.audio,
                                RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID,
                                SDP.TELEPHONE_EVENT_ATTRIBUTE,
                                RTPSession.DEFAULT_AUDIO_CLOCK_RATE,
                                SDPAudioVideoMediaFormat.DEFAULT_AUDIO_CHANNEL_COUNT,
                                "0-16");
            }
        }

        public MediaStream(RtpSessionConfig config, int index)
        {
            RtpSessionConfig = config;
            this.Index = index;
        }

        public void AddBuffer(TimeSpan dropPacketTimeout)
        {
            if (dropPacketTimeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(dropPacketTimeout));
            RTPReorderBuffer = new RTPReorderBuffer(dropPacketTimeout);
            _explicitReceiveWait = dropPacketTimeout;
            _fecReceiveBuffer?.SetRepairWait(dropPacketTimeout);
        }

        public void RemoveBuffer(TimeSpan dropPacketTimeout)
        {
            RTPReorderBuffer = null;
            _explicitReceiveWait = null;
            _fecReceiveBuffer?.SetRepairWait(FecConfig.MaxReceiveRepairWait);
        }

        public bool UseBuffer()
        {
            return RTPReorderBuffer != null;
        }

        public RTPReorderBuffer GetBuffer()
        {
            return RTPReorderBuffer;
        }

        public void SetSecurityContext(ProtectRtpPacket protectRtp, ProtectRtpPacket unprotectRtp, ProtectRtpPacket protectRtcp, ProtectRtpPacket unprotectRtcp)
        {
            if (SecureContext != null)
            {
                logger.LogTrace("Tried adding new SecureContext for media type {MediaType}, but one already existed", MediaType);
            }

            SecureContext = new SecureContext(protectRtp, unprotectRtp, protectRtcp, unprotectRtcp);

            DispatchPendingPackages();
        }

        public SecureContext GetSecurityContext()
        {
            return SecureContext;
        }

        public bool IsSecurityContextReady()
        {
            return SecureContext != null;
        }

        private (bool, byte[]) UnprotectBuffer(byte[] buffer)
        {
            if (SecureContext != null)
            {
                var unprotectRtpPacket = SecureContext.UnprotectRtpPacket;

                int res = 0;
                int outBufLen = 0;

                res = unprotectRtpPacket(buffer, buffer.Length, out outBufLen);

                if (res == 0)
                {
                    return (true, buffer.AsSpan(0, outBufLen).ToArray());
                }
                else
                {
                    logger.LogWarning("SRTP unprotect failed for {MediaType}, result {Result}.", MediaType, res);
                }
            }
            return (false, buffer);
        }

        public bool EnsureBufferUnprotected(byte[] buf, RTPHeader header, out RTPPacket packet)
        {
            if (RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation)
            {
                var (succeeded, newBuffer) = UnprotectBuffer(buf);
                if (!succeeded)
                {
                    packet = null;
                    return false;
                }
                packet = new RTPPacket(newBuffer);
            }
            else
            {
                packet = new RTPPacket(buf);
            }
            packet.Header.ReceivedTime = header.ReceivedTime;
            return true;
        }

        public SrtpHandler GetOrCreateSrtpHandler()
        {
            if (SrtpHandler == null)
            {
                SrtpHandler = new SrtpHandler();
            }
            return SrtpHandler;
        }

        public void AddRtpChannel(RTPChannel rtpChannel)
        {
            this.rtpChannel = rtpChannel;
        }

        public bool HasRtpChannel()
        {
            return rtpChannel != null;
        }

        public RTPChannel GetRTPChannel()
        {
            return rtpChannel;
        }

        protected bool CheckIfCanSendRtpRaw()
        {
            if (IsClosed)
            {
                ReportSendOnClosedStream();
                return false;
            }

            if (LocalTrack == null)
            {
                logger.LogWarning("SendRtpRaw was called for a {MediaType} packet on an RTP session without a local track.", MediaType);
                return false;
            }

            if ((LocalTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly) || (LocalTrack.StreamStatus == MediaStreamStatusEnum.Inactive))
            {
                logger.LogWarning("SendRtpRaw was called for a {MediaType} packet on an RTP session with a Stream Status set to {StreamStatus}", MediaType, LocalTrack.StreamStatus);
                return false;
            }

            if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && SecureContext?.ProtectRtpPacket == null)
            {
                logger.LogWarning("SendRtpPacket cannot be called on a secure session before calling SetSecurityContext.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reports a send that was dropped because the stream is closed.
        /// </summary>
        /// <remarks>
        /// Closing is a normal lifecycle event and a media source cannot be stopped synchronously
        /// with it. Close sets IsClosed and raises its events on the closing thread while the
        /// source is still producing samples on its own, so a short tail of sends afterwards is
        /// expected and says nothing is wrong. Sends still arriving well after the close are a
        /// different thing: the source was never stopped, which is an application bug worth
        /// reporting because it holds on to a capture device and burns CPU encoding frames that
        /// are thrown away.
        ///
        /// So the first drop is logged once at debug and the rest of the tail is silent. Past
        /// <seealso cref="CLOSED_SEND_GRACE_PERIOD_MS"/> a warning is raised instead, rate limited
        /// to one every <seealso cref="CLOSED_SEND_WARNING_INTERVAL_MS"/> and carrying the running
        /// total so the scale of the leak is obvious.
        /// </remarks>
        private void ReportSendOnClosedStream()
        {
            long sendCount = Interlocked.Increment(ref _sendsAfterClose);

            if (sendCount == 1)
            {
                logger.LogDebug("RTP packet for {MediaType} not sent, the RTP session is closed.", MediaType);
                return;
            }

            // Gate the clock behind a cheap counter, this runs at the media frame rate.
            if (sendCount % CLOSED_SEND_CLOCK_CHECK_INTERVAL != 0)
            {
                return;
            }

            long closedAtTicks = Interlocked.Read(ref _closedAtTicks);

            if (closedAtTicks == 0)
            {
                // Re-opened underneath us, nothing meaningful to measure against.
                return;
            }

            long nowTicks = DateTime.Now.Ticks;
            long sinceClosedTicks = nowTicks - closedAtTicks;

            if (sinceClosedTicks < CLOSED_SEND_GRACE_PERIOD_MS * TimeSpan.TicksPerMillisecond)
            {
                // Still the expected shutdown tail.
                return;
            }

            long lastWarningTicks = Interlocked.Read(ref _lastClosedSendWarningTicks);

            if (lastWarningTicks != 0 &&
                (nowTicks - lastWarningTicks) < CLOSED_SEND_WARNING_INTERVAL_MS * TimeSpan.TicksPerMillisecond)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastClosedSendWarningTicks, nowTicks, lastWarningTicks) != lastWarningTicks)
            {
                // Another thread is reporting this round.
                return;
            }

            logger.LogWarning("{SendCount} {MediaType} packets have been sent on the RTP session in the {SecondsSinceClosed:0.##}s since it closed, the media source does not appear to have been stopped.",
                sendCount, MediaType, new TimeSpan(sinceClosedTicks).TotalSeconds);
        }

        private static byte[] Combine(params byte[][] arrays)
        {
            byte[] rv = new byte[arrays.Sum(a => a.Length)];
            int offset = 0;
            foreach (byte[] array in arrays)
            {
                System.Buffer.BlockCopy(array, 0, rv, offset, array.Length);
                offset += array.Length;
            }
            return rv;
        }

        protected void SendRtpRaw(ArraySegment<byte> data, uint timestamp, int markerBit, int payloadType, Boolean checkDone, ushort? seqNum = null)
        {
            if (HasRtpChannel() && (checkDone || CheckIfCanSendRtpRaw()))
            {
                ProtectRtpPacket protectRtpPacket = SecureContext?.ProtectRtpPacket;
                int srtpProtectionLength = (protectRtpPacket != null) ? RTPSession.SRTP_MAX_PREFIX_LENGTH : 0;

                ushort assignedSeqNum = seqNum ?? LocalTrack.GetNextSeqNum();

                RTPPacket rtpPacket = new RTPPacket(data, srtpProtectionLength);

                rtpPacket.Header.SyncSource = LocalTrack.Ssrc;
                rtpPacket.Header.SequenceNumber = assignedSeqNum;
                rtpPacket.Header.Timestamp = timestamp;
                rtpPacket.Header.MarkerBit = markerBit;
                rtpPacket.Header.PayloadType = payloadType;

                /*  https://datatracker.ietf.org/doc/html/rfc5285#section-4.2
                    
                    An example header extension, with three extension elements, some
                    padding, and including the required RTP fields, follows:

                    0                   1                   2                   3
                    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |       0xBE    |    0xDE       |           length=3            |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |  ID   | L=0   |     data      |  ID   |  L=1  |   data...     |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |     ...data   |    0 (pad)    |    0 (pad)    |  ID   | L=3   |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |                          data                                 |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                */
                if (LocalTrack?.HeaderExtensions?.Values.Count > 0)
                {
                    byte[] payload = null;
                    foreach (var ext in LocalTrack.HeaderExtensions.Values)
                    {
                        // We support up to 14 extensions .... Not clear at all how to manage more ...
                        if ((ext.Id < 1) || (ext.Id > 14))
                        {
                            continue;
                        }

                        // Get extension payload and combine it to global payload
                        var extPayLoad = ext.Marshal();
                        if (payload == null)
                        {
                            payload = extPayLoad;
                        }
                        else
                        {
                            payload = Combine(payload, extPayLoad);
                        }
                    }

                    if (payload?.Length > 0)
                    {
                        // Need to round to 4 bytes boundaries
                        var roundedExtSize = payload.Length % 4;
                        if (roundedExtSize > 0)
                        {
                            var padding = Enumerable.Repeat((byte)0, 4 - roundedExtSize).ToArray();
                            payload = Combine(payload, padding);
                        }

                        rtpPacket.Header.HeaderExtensionFlag = 1; // We have at least one extension
                        rtpPacket.Header.ExtensionLength = (ushort)(payload.Length / 4);  // payload length / 4 
                        rtpPacket.Header.ExtensionProfile = RTPHeader.ONE_BYTE_EXTENSION_PROFILE; // We support up to 14 extensions .... Not clear at all how to manage more ...
                        rtpPacket.Header.ExtensionPayload = payload;
                    }
                }
                else
                {
                    rtpPacket.Header.HeaderExtensionFlag = 0;
                }

                if (seqNum == null && FecEncoder != null && FecEncoder.IsActive)
                {
                    SendRtpRawWithFec(rtpPacket, data, protectRtpPacket, srtpProtectionLength);
                    return;
                }

                SendFinalizedRtpPacket(rtpPacket, protectRtpPacket, srtpProtectionLength);
            }
        }

        private void SendFinalizedRtpPacket(RTPPacket rtpPacket, ProtectRtpPacket protectRtpPacket, int srtpProtectionLength)
        {
            var rtpBuffer = rtpPacket.GetBytes();

            if (protectRtpPacket != null)
            {
                int rtperr = 0;
                int outBufLen = 0;

                rtperr = protectRtpPacket(rtpBuffer, rtpBuffer.Length - srtpProtectionLength, out outBufLen);

                if (rtperr != 0)
                {
                    logger.LogError("SendRTPPacket protection failed, result {RtpError}.", rtperr);
                    FecMetrics.RecordTransportError();
                    return;
                }
                else
                {
                    rtpBuffer = rtpBuffer.AsSpan(0, outBufLen).ToArray();
                }
            }

            try
            {
                if (IsUsingRelayEndPoint)
                {
                    rtpChannel.SendRelay(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer, RtpRelayEndPoint.RelayServerEndPoint);
                }
                else
                {
                    rtpChannel.Send(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer);
                }

                RtcpSession?.RecordRtpPacketSend(rtpPacket);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception sending RTP packet on {MediaType}.", MediaType);
                FecMetrics.RecordTransportError();
            }
        }

        private void SendRtpRawWithFec(RTPPacket primaryPacket, ArraySegment<byte> data, ProtectRtpPacket protectRtpPacket, int srtpProtectionLength)
        {
            // 1. Add canonical media packet to FEC encoder
            FecEncoder.AddMediaPacket(primaryPacket, () => LocalTrack.GetNextSeqNum(), out var repairPackets);

            // 2. Wrap primary packet in RFC 2198 RED format for transmission
            byte[] rawMediaBytes = data.ToArray();
            byte[] redPayload = RtpUlpfecEncoder.WrapInRed(rawMediaBytes, (byte)primaryPacket.Header.PayloadType);

            RTPPacket redMediaPacket = new RTPPacket(new ArraySegment<byte>(redPayload), srtpProtectionLength);
            redMediaPacket.Header.SyncSource = primaryPacket.Header.SyncSource;
            redMediaPacket.Header.SequenceNumber = primaryPacket.Header.SequenceNumber;
            redMediaPacket.Header.Timestamp = primaryPacket.Header.Timestamp;
            redMediaPacket.Header.MarkerBit = primaryPacket.Header.MarkerBit;
            redMediaPacket.Header.PayloadType = FecEncoder.RedPayloadType;

            redMediaPacket.Header.HeaderExtensionFlag = primaryPacket.Header.HeaderExtensionFlag;
            redMediaPacket.Header.ExtensionProfile = primaryPacket.Header.ExtensionProfile;
            redMediaPacket.Header.ExtensionLength = primaryPacket.Header.ExtensionLength;
            redMediaPacket.Header.ExtensionPayload = primaryPacket.Header.ExtensionPayload;

            SendFinalizedRtpPacket(redMediaPacket, protectRtpPacket, srtpProtectionLength);
            FecMetrics.RecordMediaPacket(redPayload.Length);

            // 3. Send any generated repair packets
            if (repairPackets != null && repairPackets.Count > 0)
            {
                foreach (var repairPkt in repairPackets)
                {
                    FecMetrics.RecordRepairGenerated();

                    RTPPacket wireRepairPacket = srtpProtectionLength > 0
                        ? new RTPPacket(new ArraySegment<byte>(repairPkt.GetPayloadBytes()), srtpProtectionLength)
                        {
                            Header = repairPkt.Header
                        }
                        : repairPkt;

                    SendFinalizedRtpPacket(wireRepairPacket, protectRtpPacket, srtpProtectionLength);
                    FecMetrics.RecordRepairSent((int)wireRepairPacket.GetPayloadLength());
                }
            }
        }

        protected void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType, Boolean checkDone, ushort? seqNum = null)
        {
            SendRtpRaw(new ArraySegment<byte>(data), timestamp, markerBit, payloadType, checkDone, seqNum);
        }

        /// <summary>
        /// To set a new value to a RTP Header extension.
        /// 
        /// According the extension the Object expected as value is different - check on each extension
        /// </summary>
        /// <param name="uri">The URI of the extension to use</param>
        /// <param name="value">Object to set on the extension (check extension to know object type) </param>
        public void SetRtpHeaderExtensionValue(String uri, Object value)
        {
            try
            {
                var ext = LocalTrack?.HeaderExtensions?.Values?.FirstOrDefault(ext => ext.Uri == uri);
                if (ext != null)
                {
                    switch (uri)
                    {
                        case CVOExtension.RTP_HEADER_EXTENSION_URI:
                            if (ext is CVOExtension cvoExtension)
                            {
                                cvoExtension.Set(value);
                            }
                            break;

                        case AudioLevelExtension.RTP_HEADER_EXTENSION_URI:
                            if (ext is AudioLevelExtension audioLevelExtension)
                            {
                                audioLevelExtension.Set(value);
                            }
                            break;

                        case TransportWideCCExtension.RTP_HEADER_EXTENSION_URI:
                            //case TransportWideCCExtension.RTP_HEADER_EXTENSION_URI_ALT:
                            if (ext is TransportWideCCExtension transportWideCCExtension)
                            {
                                transportWideCCExtension.Set(_twccPacketCount++);
                            }
                            break;

                        // Not necessary to set something in AbsSendTimeExtension - just to be coherent here
                        case AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI:
                            if (ext is AbsSendTimeExtension absSendTimeExtension)
                            {
                                absSendTimeExtension.Set(value);
                            }
                            break;
                    }
                }
            }
            catch
            {

            }
        }

        /// <summary>
        /// Allows additional control for sending raw RTP payloads. No framing or other processing is carried out.
        /// </summary>
        /// <param name="data">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadType">The payload ID to set in the RTP header.</param>
        /// <param name="seqNum"> The RTP sequence number </param>
        public void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType, ushort seqNum)
        {
            SendRtpRaw(data, timestamp, markerBit, payloadType, false, seqNum);
        }

        /// <summary>
        /// Allows additional control for sending raw RTP payloads. No framing or other processing is carried out.
        /// </summary>
        /// <param name="data">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadType">The payload ID to set in the RTP header.</param>
        public void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType)
        {
            SendRtpRaw(data, timestamp, markerBit, payloadType, false);
        }

        /// <summary>
        /// Allows additional control for sending raw RTCP payloads
        /// </summary>
        /// <param name="rtcpBytes">Raw RTCP report data to send.</param>
        public void SendRtcpRaw(byte[] rtcpBytes)
        {
            if (SendRtcpReport(rtcpBytes))
            {
                RTCPCompoundPacket rtcpCompoundPacket = null;
                try
                {
                    rtcpCompoundPacket = new RTCPCompoundPacket(rtcpBytes);
                }
                catch (Exception excp)
                {
                    logger.LogWarning("Can't create RTCPCompoundPacket from the provided RTCP bytes. {Message}", excp.Message);
                }

                if (rtcpCompoundPacket != null)
                {
                    OnSendReportByIndex?.Invoke(Index, MediaType, rtcpCompoundPacket);
                }
            }
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="reportBuffer">The serialised RTCP report to send.</param>
        /// <returns>True if report was sent</returns>
        private bool SendRtcpReport(byte[] reportBuffer)
        {
            if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && !IsSecurityContextReady())
            {
                logger.LogWarning("SendRtcpReport cannot be called on a secure session before calling SetSecurityContext.");
                return false;
            }
            else if (HasRtpChannel() && ControlDestinationEndPoint != null)
            {
                //logger.LogDebug("SendRtcpReport: {ReportBytes}", reportBytes.HexStr());

                var sendOnSocket = RtpSessionConfig.IsRtcpMultiplexed ? RTPChannelSocketsEnum.RTP : RTPChannelSocketsEnum.Control;

                var protectRtcpPacket = SecureContext?.ProtectRtcpPacket;

                if (protectRtcpPacket == null)
                {
                    logger.LogDebug("Sending key {MediaType} RTCP packet size {Size} to {EndPoint}.",
                        MediaType, reportBuffer.Length, ControlDestinationEndPoint);

                    rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, reportBuffer);
                }
                else
                {
                    byte[] sendBuffer = new byte[reportBuffer.Length + RTPSession.SRTP_MAX_PREFIX_LENGTH];
                    Buffer.BlockCopy(reportBuffer, 0, sendBuffer, 0, reportBuffer.Length);

                    int rtperr = 0;
                    int outBufLen = 0;

                    rtperr = protectRtcpPacket(sendBuffer, sendBuffer.Length - RTPSession.SRTP_MAX_PREFIX_LENGTH, out outBufLen);

                    if (rtperr != 0)
                    {
                        //logger.LogWarning("SRTP RTCP packet protection failed, result {RtpError}.", rtperr);
                    }
                    else
                    {

                        //logger.LogDebug("Sending key {MediaType} RTCP packet size {Size} to {EndPoint}.",
                        //    MediaType, outBufLen, ControlDestinationEndPoint);

                        rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, sendBuffer.AsSpan(0, outBufLen).ToArray());
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="report">RTCP report to send.</param>
        public void SendRtcpReport(RTCPCompoundPacket report)
        {
            if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && !IsSecurityContextReady() && report.Bye != null)
            {
                // Do nothing. The RTCP BYE gets generated when an RTP session is closed.
                // If that occurs before the connection was able to set up the secure context
                // there's no point trying to send it.
            }
            else
            {
                var reportBytes = report.GetBytes();
                SendRtcpReport(reportBytes);
                OnSendReportByIndex?.Invoke(Index, MediaType, report);
            }
        }

        /// <summary>
        /// Allows sending of RTCP feedback reports.
        /// </summary>
        /// <param name="feedback">The feedback report to send.</param>
        public void SendRtcpFeedback(RTCPFeedback feedback)
        {
            var reportBytes = feedback.GetBytes();
            SendRtcpReport(reportBytes);
        }

        /// <summary>
        /// Allows sending of RTCP TWCC feedback reports.
        /// </summary>
        /// <param name="feedback">The feedback report to send.</param>
        public void SendRtcpTWCCFeedback(RTCPTWCCFeedback feedback)
        {
            var reportBytes = feedback.GetBytes();
            SendRtcpReport(reportBytes);
        }

        public void OnReceiveRTPPacket(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            RTPPacket rtpPacket;
            if (MediaType == SDPMediaTypesEnum.audio && NegotiatedRtpEventPayloadID != 0 && hdr.PayloadType == NegotiatedRtpEventPayloadID)
            {
                if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
                {
                    // Cache pending packages to use it later to prevent missing frames
                    // when DTLS was not completed yet as a Server bt already completed as a client
                    AddPendingPackage(hdr, localPort, remoteEndPoint, buffer);
                    return;
                }

                RaiseOnRtpEventByIndex(remoteEndPoint, new RTPEvent(rtpPacket.GetPayloadBytes()), rtpPacket.Header);
                return;
            }

            // Set the remote track SSRC so that RTCP reports can match the media type.
            if (RemoteTrack != null && RemoteTrack.Ssrc == 0 && DestinationEndPoint != null && !IsUsingRelayEndPoint)
            {
                bool isValidSource = AdjustRemoteEndPoint(hdr.SyncSource, remoteEndPoint);

                if (isValidSource)
                {
                    logger.LogDebug("Set remote track ({MediaType} - index={Index}) SSRC to {SyncSource} remote RTP endpoint {rtpep}.", MediaType, Index, hdr.SyncSource, remoteEndPoint);
                    RemoteTrack.Ssrc = hdr.SyncSource;
                }
            }

            if (RemoteTrack != null)
            {
                if (FecDecoder == null || RemoteTrack.Ssrc == hdr.SyncSource)
                {
                    LogIfWrongSeqNumber($"{MediaType}", hdr, RemoteTrack);
                }
                ProcessHeaderExtensions(hdr, remoteEndPoint);
            }

            if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
            {
                return;
            }

            var format = LocalTrack?.GetFormatForPayloadID(rtpPacket.Header.PayloadType);
            bool isVideo = MediaType == SDPMediaTypesEnum.video;
            bool isRed = isVideo && (rtpPacket.Header.PayloadType == FecDecoder?.RedPayloadType || format?.IsRed == true);
            bool isUlpfec = isVideo && (rtpPacket.Header.PayloadType == FecDecoder?.UlpfecPayloadType || format?.IsUlpfec == true);

            if (isVideo && FecDecoder != null && (isRed || isUlpfec || format?.IsVideoMediaCodec == true))
            {
                ReceiveWithFec(remoteEndPoint, rtpPacket, isRed, isUlpfec);
                return;
            }

            if (isRed)
            {
                RtcpSession?.RecordRtpPacketReceived(rtpPacket);
                if (RtpRedPayload.TryDecode(rtpPacket.GetPayloadBytes(), out _, out var primary))
                {
                    var mediaFormat = LocalTrack?.GetFormatForPayloadID(primary.PayloadType);
                    if (mediaFormat?.IsVideoMediaCodec == true)
                    {
                        var media = CreateRedBlockPacket(rtpPacket.Header, primary);
                        DispatchReceivedMediaPacket(remoteEndPoint, media, mediaFormat.Value);
                    }
                }
                return;
            }
            if (isUlpfec)
            {
                RtcpSession?.RecordRtpPacketReceived(rtpPacket);
                return;
            }
            if (!format.HasValue || format.Value.IsEmpty())
            {
                logger.LogWarning("Received RTP packet with unknown payload type {PayloadType} for {MediaType} stream from {RemoteEndPoint}.",
                    rtpPacket.Header.PayloadType, MediaType, remoteEndPoint);
                return;
            }
            DispatchReceivedMediaPacket(remoteEndPoint, rtpPacket, format.Value);
            RtcpSession?.RecordRtpPacketReceived(rtpPacket);
        }

        private void ReceiveWithFec(IPEndPoint remoteEndPoint, RTPPacket wirePacket, bool isRed, bool isUlpfec)
        {
            var media = new List<RTPPacket>();
            var protectedSequences = new List<ushort>();
            bool repairOnly = isUlpfec;
            RtpFecReceiveBuffer receiver;
            lock (_fecReceiveLock)
            {
                receiver = _fecReceiveBuffer;
                if (IsClosed || receiver == null || FecDecoder == null) return;
                bool sourceChanged = _fecReceiveSsrc.HasValue && _fecReceiveSsrc.Value != wirePacket.Header.SyncSource;

                // RTCP observes network arrivals, never reconstructed packets or transformed headers.
                if (!sourceChanged) RtcpSession?.RecordRtpPacketReceived(wirePacket);
                var repairs = new List<RTPPacket>();
                RTPPacket primaryMedia = null;
                if (isRed)
                {
                    if (!RtpRedPayload.TryDecode(wirePacket.GetPayloadBytes(), out var redundant, out var primary)) return;
                    foreach (var block in redundant)
                    {
                        if (block.PayloadType == FecDecoder.UlpfecPayloadType)
                        {
                            repairs.Add(CreateRedBlockPacket(wirePacket.Header, block));
                        }
                    }
                    repairOnly = primary.PayloadType == FecDecoder.UlpfecPayloadType;
                    if (repairOnly)
                    {
                        repairs.Add(CreateRedBlockPacket(wirePacket.Header, primary));
                    }
                    else if (LocalTrack?.GetFormatForPayloadID(primary.PayloadType)?.IsVideoMediaCodec == true)
                    {
                        primaryMedia = CreateRedBlockPacket(wirePacket.Header, primary);
                    }
                }
                else if (isUlpfec)
                {
                    repairs.Add(wirePacket);
                }
                else
                {
                    primaryMedia = wirePacket;
                }

                if (sourceChanged)
                {
                    // A primary from an admitted peer selects the next source. Foreign
                    // repairs and malformed/unknown RED cannot retire the active source.
                    if (primaryMedia == null || primaryMedia.GetPayloadLength() == 0) return;
                    uint previousSsrc = _fecReceiveSsrc.Value;
                    ResetFecReceiveState();
                    receiver = _fecReceiveBuffer;
                    if (RemoteTrack != null)
                    {
                        RemoteTrack.Ssrc = wirePacket.Header.SyncSource;
                        RemoteTrack.LastRemoteSeqNum = wirePacket.Header.SequenceNumber;
                    }
                    RtcpSession?.RemoveReceptionReport(previousSsrc);
                    RtcpSession?.RecordRtpPacketReceived(wirePacket);
                    logger.LogInformation("Video receive SSRC changed from {PreviousSsrc} to {NewSsrc}; reset FEC receive state.",
                        previousSsrc, wirePacket.Header.SyncSource);
                }

                if (primaryMedia != null)
                {
                    if (FecDecoder.TryAddReceivedMedia(primaryMedia, out var recovered)) media.Add(primaryMedia);
                    AddRecoveredMedia(media, recovered);
                }
                foreach (var repair in repairs)
                {
                    if (!RtpUlpfecDecoder.TryParseFecPayload(repair.GetPayloadBytes(), out var info)) continue;
                    // A piggybacked repair may cover its primary media at the same wire
                    // sequence. A repair-only packet cannot protect itself or future media.
                    if (info.ProtectedSequenceNumbers.Any(sequence =>
                        unchecked((short)(sequence - wirePacket.Header.SequenceNumber)) > 0 ||
                        (sequence == wirePacket.Header.SequenceNumber && primaryMedia == null))) continue;
                    if (!FecDecoder.TryAddReceivedFec(repair, out var recovered)) continue;
                    protectedSequences.AddRange(info.ProtectedSequenceNumbers);
                    AddRecoveredMedia(media, recovered);
                }
                // Malformed repair-only packets cannot establish sequence positions or startup coverage.
                if (repairOnly && protectedSequences.Count == 0) return;
                if (primaryMedia == null && protectedSequences.Count == 0) return;
                _fecReceiveSsrc = wirePacket.Header.SyncSource;
                _fecRemoteEndPoint = remoteEndPoint;
            }
            // The coordinator serializes timer/transport delivery and invokes consumers outside its state lock.
            receiver.AddBatch(media, wirePacket.Header.SequenceNumber, repairOnly, protectedSequences);
        }

        private void AddRecoveredMedia(List<RTPPacket> media, List<RTPPacket> recovered)
        {
            foreach (var packet in recovered)
            {
                FecMetrics.RecordPacketRecovered();
                media.Add(packet);
            }
        }

        private static RTPPacket CreateRedBlockPacket(RTPHeader header, RtpRedBlock block)
        {
            var packet = new RTPPacket(new ArraySegment<byte>(block.Data), 0) { Header = CloneRtpHeader(header) };
            packet.Header.PayloadType = block.PayloadType;
            return packet;
        }

        internal static RTPHeader CloneRtpHeader(RTPHeader source)
        {
            return new RTPHeader
            {
                Version = source.Version,
                PaddingFlag = source.PaddingFlag,
                HeaderExtensionFlag = source.HeaderExtensionFlag,
                CSRCCount = source.CSRCCount,
                MarkerBit = source.MarkerBit,
                PayloadType = source.PayloadType,
                SequenceNumber = source.SequenceNumber,
                Timestamp = source.Timestamp,
                SyncSource = source.SyncSource,
                CSRCList = source.CSRCList == null ? null : (int[])source.CSRCList.Clone(),
                ExtensionProfile = source.ExtensionProfile,
                ExtensionLength = source.ExtensionLength,
                ExtensionPayload = source.ExtensionPayload == null ? null : (byte[])source.ExtensionPayload.Clone(),
                PayloadSize = source.PayloadSize,
                PaddingCount = source.PaddingCount,
                ReceivedTime = source.ReceivedTime
            };
        }

        private void DispatchReceivedMediaPacket(IPEndPoint remoteEndPoint, RTPPacket packet, SDPAudioVideoMediaFormat format)
        {
            if (UseBuffer())
            {
                var reorderBuffer = GetBuffer();
                reorderBuffer.Add(packet);
                while (reorderBuffer.Get(out var bufferedPacket))
                {
                    if (RemoteTrack != null)
                    {
                        LogIfWrongSeqNumber($"{MediaType}", bufferedPacket.Header, RemoteTrack);
                        RemoteTrack.LastRemoteSeqNum = bufferedPacket.Header.SequenceNumber;
                    }
                    ProcessRtpPacket(remoteEndPoint, bufferedPacket, format);
                }
            }
            else
            {
                if (RemoteTrack != null)
                {
                    RemoteTrack.LastRemoteSeqNum = packet.Header.SequenceNumber;
                }
                ProcessRtpPacket(remoteEndPoint, packet, format);
            }
        }

        /// <summary>
        /// Do any additional processing for the RTP packet. For vidoe streams this method will be overridden to handle video packetisation.
        /// Audio and other media types typially don't use framing but have other processing they'd like to do.
        /// </summary>
        /// <param name="remoteEndPoint">The remote peer the RTP pakcet was received from.</param>
        /// <param name="rtpPacket">The RTP apcet received.</param>
        /// <param name="format">The SDP format for the payload ID in the RTP header.</param>
        protected virtual void ProcessRtpPacket(IPEndPoint remoteEndPoint, RTPPacket rtpPacket, SDPAudioVideoMediaFormat format)
        {
            // If not overridden the default behaviour is to raise an event to inform the owner of the RTP transport
            // that a new RTP packet has been received.
            RaiseOnRtpPacketReceivedByIndex(remoteEndPoint, rtpPacket);
        }

        public void RaiseOnReceiveReportByIndex(IPEndPoint ipEndPoint, RTCPCompoundPacket rtcpPCompoundPacket)
        {
            OnReceiveReportByIndex?.Invoke(Index, ipEndPoint, MediaType, rtcpPCompoundPacket);
        }

        protected void RaiseOnRtpEventByIndex(IPEndPoint ipEndPoint, RTPEvent rtpEvent, RTPHeader rtpHeader)
        {
            OnRtpEventByIndex?.Invoke(Index, ipEndPoint, rtpEvent, rtpHeader);
        }

        protected void RaiseOnRtpPacketReceivedByIndex(IPEndPoint ipEndPoint, RTPPacket rtpPacket)
        {
            OnRtpPacketReceivedByIndex?.Invoke(Index, ipEndPoint, MediaType, rtpPacket);
        }

        private void RaiseOnTimeoutByIndex(SDPMediaTypesEnum mediaType)
        {
            OnTimeoutByIndex?.Invoke(Index, mediaType);
        }

        // Submit all previous cached packages to self
        protected virtual void DispatchPendingPackages()
        {
            PendingPackages[] pendingPackagesArray = null;

            var isContextValid = SecureContext != null && !IsClosed;

            lock (_pendingPackagesLock)
            {
                if (isContextValid)
                {
                    pendingPackagesArray = _pendingPackagesBuffer.ToArray();
                }
                _pendingPackagesBuffer.Clear();
            }
            if (isContextValid)
            {
                foreach (var pendingPackage in pendingPackagesArray)
                {
                    if (pendingPackage != null)
                    {
                        OnReceiveRTPPacket(pendingPackage.hdr, pendingPackage.localPort, pendingPackage.remoteEndPoint, pendingPackage.buffer);
                    }
                }
            }
        }

        // Clear previous buffer
        protected virtual void ClearPendingPackages()
        {
            lock (_pendingPackagesLock)
            {
                _pendingPackagesBuffer.Clear();
            }
        }

        // Cache pending packages to use it later to prevent missing frames
        // when DTLS was not completed yet as a Server but already completed as a client
        protected virtual bool AddPendingPackage(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            const int MAX_PENDING_PACKAGES_BUFFER_SIZE = 32;

            if (SecureContext == null && !IsClosed)
            {
                lock (_pendingPackagesLock)
                {
                    //ensure buffer max size
                    while (_pendingPackagesBuffer.Count > 0 && _pendingPackagesBuffer.Count >= MAX_PENDING_PACKAGES_BUFFER_SIZE)
                    {
                        _pendingPackagesBuffer.RemoveAt(0);
                    }
                    _pendingPackagesBuffer.Add(new PendingPackages(hdr, localPort, remoteEndPoint, buffer));
                }
                return true;
            }
            return false;
        }

        protected void LogIfWrongSeqNumber(string trackType, RTPHeader header, MediaStreamTrack track)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                if (track.LastRemoteSeqNum != 0 &&
                    header.SequenceNumber != (track.LastRemoteSeqNum + 1) &&
                    !(header.SequenceNumber == 0 && track.LastRemoteSeqNum == ushort.MaxValue))
                {
                    logger.LogTrace("{TrackType} stream sequence number jumped from {LastRemoteSeqNum} to {SequenceNumber}.", trackType, track.LastRemoteSeqNum, header.SequenceNumber);
                }
            }
        }

        /// <summary>
        /// Adjusts the expected remote end point for a particular media type.
        /// </summary>
        /// <param name="ssrc">The SSRC from the RTP packet header.</param>
        /// <param name="receivedOnEndPoint">The actual remote end point that the RTP packet came from.</param>
        /// <returns>True if remote end point for this media type was the expected one or it was adjusted. False if
        /// the remote end point was deemed to be invalid for this media type.</returns>
        protected bool AdjustRemoteEndPoint(uint ssrc, IPEndPoint receivedOnEndPoint)
        {
            bool isValidSource = false;
            IPEndPoint expectedEndPoint = DestinationEndPoint;

            if (expectedEndPoint.Address.Equals(receivedOnEndPoint.Address) && expectedEndPoint.Port == receivedOnEndPoint.Port)
            {
                // Exact match on actual and expected destination.
                isValidSource = true;
            }
            else if (AcceptRtpFromAny || (expectedEndPoint.Address.IsPrivate() && !receivedOnEndPoint.Address.IsPrivate())
                //|| (IPAddress.Loopback.Equals(receivedOnEndPoint.Address) || IPAddress.IPv6Loopback.Equals(receivedOnEndPoint.Address
                )
            {
                // The end point doesn't match BUT we were supplied a private address in the SDP and the remote source is a public address
                // so high probability there's a NAT on the network path. Switch to the remote end point (note this can only happen once
                // and only if the SSRV is 0, i.e. this is the first RTP packet.
                // If the remote end point is a loopback address then it's likely that this is a test/development 
                // scenario and the source can be trusted.
                // AC 12 Jul 2020: Commented out the expression that allows the end point to be change just because it's a loopback address.
                // A breaking case is doing an attended transfer test where two different agents are using loopback addresses. 
                // The expression allows an older session to override the destination set by a newer remote SDP.
                // AC 18 Aug 2020: Despite the carefully crafted rules below and https://github.com/sipsorcery/sipsorcery/issues/197
                // there are still cases that were a problem in one scenario but acceptable in another. To accommodate a new property
                // was added to allow the application to decide whether the RTP end point switches should be liberal or not.
                logger.LogDebug("{MediaType} end point switched for RTP ssrc {Ssrc} from {ExpectedEndPoint} to {ReceivedOnEndPoint}.", MediaType, ssrc, expectedEndPoint, receivedOnEndPoint);

                DestinationEndPoint = receivedOnEndPoint;
                if (RtpSessionConfig.IsRtcpMultiplexed)
                {
                    ControlDestinationEndPoint = DestinationEndPoint;
                }
                else
                {
                    ControlDestinationEndPoint = new IPEndPoint(DestinationEndPoint.Address, DestinationEndPoint.Port + 1);
                }

                isValidSource = true;
            }
            else
            {
                logger.LogWarning("RTP packet with SSRC {Ssrc} received from unrecognised end point {ReceivedOnEndPoint}.", ssrc, receivedOnEndPoint);
            }

            return isValidSource;
        }

        /// <summary>
        /// Creates a new RTCP session for a media track belonging to this RTP session. The
        /// reporting session gets a CNAME of its own, which means the stream cannot be
        /// synchronised against any other stream. Prefer the overload that takes the owning RTP
        /// session's CNAME.
        /// </summary>
        /// <returns>A new RTCPSession object. The RTCPSession must have its Start method called
        /// in order to commence sending RTCP reports.</returns>
        public bool CreateRtcpSession()
        {
            return CreateRtcpSession(null);
        }

        /// <summary>
        /// Creates a new RTCP session for a media track belonging to this RTP session.
        /// </summary>
        /// <param name="cname">The CNAME for the RTP session this stream belongs to. All the
        /// streams of one RTP session must share a CNAME for the remote party to be able to
        /// synchronise them against each other. If null a CNAME unique to this stream is
        /// generated.</param>
        /// <returns>A new RTCPSession object. The RTCPSession must have its Start method called
        /// in order to commence sending RTCP reports.</returns>
        public bool CreateRtcpSession(string cname)
        {
            if (RtcpSession == null)
            {
                RtcpSession = new RTCPSession(MediaType, 0, cname);
                RtcpSession.OnTimeout += RaiseOnTimeoutByIndex;

                return true;
            }
            return false;
        }

        /// <summary>
        /// Sets the RTP clock rate on this stream's reporting session from the negotiated media
        /// format. The reporting session needs it to put an RTP timestamp in its sender reports
        /// that corresponds to the report's NTP timestamp, and to measure interarrival jitter in
        /// RTP timestamp units as RFC3550 section 6.4.1 requires.
        /// </summary>
        /// <param name="capabilities">The negotiated formats for this stream, highest priority first.
        /// The rate is taken from the first format that is a media codec rather than a telephone
        /// event, which carries its own clock rate but is not the stream's media clock.</param>
        internal void SetRtcpRtpClockRate(List<SDPAudioVideoMediaFormat> capabilities)
        {
            if (RtcpSession == null || RtcpSession.PayloadSampleRateHz != 0 || capabilities == null)
            {
                // Leave a rate that has already been set alone, it may have been set by the application.
                return;
            }

            foreach (var format in capabilities)
            {
                if (format.IsEmpty() ||
                    string.Equals(format.Name(), SDP.TELEPHONE_EVENT_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var clockRate = format.RtpClockRate();

                    if (clockRate > 0)
                    {
                        RtcpSession.PayloadSampleRateHz = clockRate;
                        return;
                    }
                }
                catch (ApplicationException excp)
                {
                    // A malformed rtpmap makes the AudioFormat, VideoFormat and TextFormat
                    // constructors throw this. It should not stop the stream being set up; if no
                    // rate can be determined the reporting session falls back to not extrapolating.
                    logger.LogWarning(excp, "Could not determine the RTP clock rate for {MediaType} format {Format}. {ErrorMessage}", MediaType, format.ID, excp.Message);
                }
            }
        }

        /// <summary>
        /// Sets the remote end points for a media type supported by this RTP session.
        /// </summary>
        /// <param name="rtpEndPoint">The remote end point for RTP packets corresponding to the media type.</param>
        /// <param name="rtcpEndPoint">The remote end point for RTCP packets corresponding to the media type.</param>
        public void SetDestination(IPEndPoint rtpEndPoint, IPEndPoint rtcpEndPoint)
        {
            DestinationEndPoint = rtpEndPoint;
            ControlDestinationEndPoint = rtcpEndPoint;
        }

        /// <summary>
        /// Attempts to get the highest priority sending format for the remote call party.
        /// </summary>
        /// <returns>The first compatible media format found for the specified media type.</returns>
        public SDPAudioVideoMediaFormat GetSendingFormat()
        {
            if (LocalTrack == null && RemoteTrack == null)
            {
                throw new ApplicationException($"Cannot get the {MediaType} sending format, missing either local or remote {MediaType} track.");
            }

            IEnumerable<SDPAudioVideoMediaFormat> formats = LocalTrack == null
                ? RemoteTrack.Capabilities
                : RemoteTrack == null
                    ? LocalTrack.Capabilities
                    : SDPAudioVideoMediaFormat.GetCompatibleFormats(RemoteTrack.Capabilities, LocalTrack.Capabilities);

            if (MediaType == SDPMediaTypesEnum.video)
            {
                formats = formats?.Where(x => x.IsVideoMediaCodec);
            }
            else if (MediaType == SDPMediaTypesEnum.audio && LocalTrack != null && RemoteTrack != null)
            {
                formats = formats?.Where(x => x.ID != NegotiatedRtpEventPayloadID);
            }

            var format = formats == null
                ? SDPAudioVideoMediaFormat.Empty
                : formats.DefaultIfEmpty(SDPAudioVideoMediaFormat.Empty).First();
            if (format.IsEmpty())
            {
                throw new ApplicationException($"No compatible sending format could be found for media {MediaType}.");
            }
            return format;
        }

        public void ProcessHeaderExtensions(RTPHeader header, IPEndPoint remoteEndPoint)
        {
            header.GetHeaderExtensions().ToList().ForEach(rtpHeaderExtensionData =>
            {
                if (RemoteTrack?.HeaderExtensions?.TryGetValue(rtpHeaderExtensionData.Id, out RTPHeaderExtension rtpHeaderExtension) == true)
                {
                    var value = rtpHeaderExtension.Unmarshal(header, rtpHeaderExtensionData.Data);
                    OnRtpHeaderReceivedByIndex?.Invoke(Index, remoteEndPoint, MediaType, rtpHeaderExtension.Uri, value);
                }
            });
        }

        /// <summary>
        /// Gets the RTP port to use in the SDP offer or answer.
        /// </summary>
        public int GetRtpPortForSessionDescription()
        {
            if (IsUsingRelayEndPoint)
            {
                return RtpRelayEndPoint.RemotePeerRelayEndPoint.Port;
            }

            return rtpChannel switch
            {
                null => 0,
                _ when rtpChannel.RTPSrflxEndPoint != null => rtpChannel.RTPSrflxEndPoint.Port,
                _ => rtpChannel.RTPPort
            };
        }
    }
}
