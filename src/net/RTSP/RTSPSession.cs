//-----------------------------------------------------------------------------
// Filename: RTSPSession.cs
//
// Description: Represents an active RTSP session for an RTSP server process that is sending RTP packets
// to a client.
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Jan 2014	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com). 
//                              Borrowed bits from https://net7mma.codeplex.com (license @ https://net7mma.codeplex.com/license).
//
// Notes:
//
// Timestamp Note from http://www.cs.columbia.edu/~hgs/rtp/faq.html#timestamp-computed:
// Practically speaking, how is the timestamp computed?
// For audio, the timestamp is incremented by the packetization interval times the sampling rate. For example, for audio packets 
// containing 20 ms of audio sampled at 8,000 Hz, the timestamp for each block of audio increases by 160, even if the block is not 
// sent due to silence suppression. Also, note that the actual sampling rate will differ slightly from this nominal rate, but the 
// sender typically has no reliable way to measure this divergence.
// For video, time clock rate is fixed at 90 kHz. The timestamps generated depend on whether the application can determine the 
// frame number or not. If it can or it can be sure that it is transmitting every frame with a fixed frame rate, the timestamp is 
// governed by the nominal frame rate. Thus, for a 30 f/s video, timestamps would increase by 3,000 for each frame, for a 25 f/s 
// video by 3,600 for each frame. If a frame is transmitted as several RTP packets, these packets would all bear the same timestamp. 
// If the frame number cannot be determined or if frames are sampled aperiodically, as is typically the case for software codecs, 
// the timestamp has to be computed from the system clock (e.g., gettimeofday()).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTSPSession
    {
        public const int H264_RTP_HEADER_LENGTH = 2;
        public const int JPEG_RTP_HEADER_LENGTH = 8;
        public const int VP8_RTP_HEADER_LENGTH = 3;

        private const int RFC_2435_FREQUENCY_BASELINE = 90000;
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int RECEIVE_BUFFER_SIZE = 2048;
        private const int MEDIA_PORT_START = 30000;             // Arbitrary port number to start allocating RTP and control ports from.
        private const int MEDIA_PORT_END = 40000;               // Arbitrary port number that RTP and control ports won't be allocated above.
        private const int RTP_PACKETS_MAX_QUEUE_LENGTH = 1000;   // The maximum number of RTP packets that will be queued.
        private const int RTP_RECEIVE_BUFFER_SIZE = 100000000;
        private const int RTP_SEND_BUFFER_SIZE = 100000000;
        //private const double DEFAULT_INITAL_FRAME_RATE = 10.0;            // Set the default initial frame rate to 10 frames per second.
        //private const int INITIAL_FRAME_RATE_CALCULATION_SECONDS = 10;    // Do an initial frame rate calculation after this many seconds.
        //private const int FRAME_RATE_CALCULATION_SECONDS = 60;            // Re-calculate the frame rate with this period in seconds.
        //private const int MINIMUM_SAMPLES_FOR_FRAME_RATE = 20;            // The minimum number of samples before a new frame rate calculation will be made.
        private const int MAXIMUM_RTP_PORT_BIND_ATTEMPTS = 3;               // The maximum number of re-attempts that will be made when trying to bind the RTP port.
        private const int RTCP_SENDER_REPORT_INTERVAL_SECONDS = 5;          // Interval at which to send RTCP sender reports.

        private static DateTime UtcEpoch2036 = new DateTime(2036, 2, 7, 6, 28, 16, DateTimeKind.Utc);
        private static DateTime UtcEpoch1900 = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static DateTime UtcEpoch1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ILogger logger = Log.Logger;

        private static int _nextMediaPort = MEDIA_PORT_START;
        private static Mutex _allocatePortsMutex = new Mutex();

        private Socket _rtpSocket;
        private SocketError _rtpSocketError = SocketError.Success;
        private Socket _controlSocket;
        private SocketError _controlSocketError = SocketError.Success;
        private byte[] _controlSocketBuffer;
        private bool _closed;
        private Queue<RTPPacket> _packets = new Queue<RTPPacket>();
        //private double _frameRate = DEFAULT_INITAL_FRAME_RATE;              // The most recently calculated frame rate.
        //private bool _isInitialFrameRateCalculation = true;
        //private int _frameRateSampleCount;                                  // Counts the packets since the frame rate calculation.
        //private DateTime _lastFrameRateCalculationAt = DateTime.MinValue;   // Time the frame rate was last calculated.
        //private uint _timestampStep = Convert.ToUInt32(1 / DEFAULT_INITAL_FRAME_RATE * RFC_2435_FREQUENCY_BASELINE);    // The step that will be applied to the RTP timestamp. It gets re-calculated when the frame rate is adjusted.

        private IPEndPoint _remoteEndPoint;
        public IPEndPoint RemoteEndPoint
        {
            get { return _remoteEndPoint; }
            set { _remoteEndPoint = value; }
        }

        private IPEndPoint _rtcpRemoteEndPoint;
        public IPEndPoint RtcpRemoteEndPoint
        {
            get { return _rtcpRemoteEndPoint; }
            set { _rtcpRemoteEndPoint = value; }
        }

        private string _sessionID;
        public string SessionID
        {
            get { return _sessionID; }
            set { _sessionID = value; }
        }

        private int _rtpPort;
        public int RTPPort
        {
            get { return _rtpPort; }
        }

        private DateTime _createdAt;
        public DateTime CreatedAt
        {
            get { return _createdAt; }
        }

        private DateTime _startedAt;
        public DateTime StartedAt
        {
            get { return _startedAt; }
        }

        private DateTime _rtpLastActivityAt;
        public DateTime RTPLastActivityAt
        {
            get { return _rtpLastActivityAt; }
        }

        private int _controlPort;
        public int ControlPort
        {
            get { return _controlPort; }
        }

        private DateTime _controlLastActivityAt;
        public DateTime ControlLastActivityAt
        {
            get { return _controlLastActivityAt; }
        }

        private int _rtpPayloadHeaderLength = 0;    // Some RTP media types use a payload header to carry information about the encoded media. Typically this header needs to be stripped off before passing to a decoder.
        public int RTPPayloadHeaderLength
        {
            get { return _rtpPayloadHeaderLength; }
            set { _rtpPayloadHeaderLength = value; }
        }

        //private ICEState _iceState;
        //public ICEState ICEState
        //{
        //    get { return _iceState; }
        //}

        public bool DontTimeout { get; set; }           // If set to true means a server should not timeout this session even if no activity is received on the RTP socket.

        public bool IsClosed
        {
            get { return _closed; }
        }

        // Fields that track the RTP stream being managed in this session.
        private ushort _sequenceNumber = 1;
        private uint _timestamp = 0;
        private uint _syncSource = 0;

        // RTCP fields.
        private uint _senderPacketCount = 0;
        private uint _senderOctetCount = 0;
        private DateTime _senderLastSentAt = DateTime.MinValue;

        //public event Action<string, byte[]> OnRTPDataReceived;
        public event Action OnRTPQueueFull;                         // Occurs if the RTP queue fills up and needs to be purged.
        public event Action<string> OnRTPSocketDisconnected;
        public event Action<string, byte[]> OnControlDataReceived;
        public event Action<string> OnControlSocketDisconnected;

        // Hooks for DTLS and SRTP.
        public Action<byte[], int, Action<byte[]>> OnDtlsReceive;   // [buffer, length, Raw send callback method] Handler for any DTLS packets received.                
        public Func<byte[], byte[]> RtpProtect;                     // [out encrypted buffer, in buffer] Set this for SRTP protection of RTP packets.

        public RTSPSession()
        {
            _createdAt = DateTime.Now;
        }

        public RTSPSession(string sessionID, IPEndPoint remoteEndPoint, IPEndPoint rtcpRemoteEndPoint)
            : this()
        {
            _sessionID = sessionID;
            _remoteEndPoint = remoteEndPoint;
            _rtcpRemoteEndPoint = rtcpRemoteEndPoint;
            _syncSource = Convert.ToUInt32(Crypto.GetRandomInt(0, 9999999));
        }

        //public void SetICEState(ICEState iceState)
        //{
        //    try
        //    {
        //        _iceState = iceState;
        //    }
        //    catch (Exception excp)
        //    {
        //        logger.LogError("Exception SetICEState. " + excp);
        //    }
        //}

        /// <summary>
        /// Attempts to reserve the RTP and control ports for the RTP session.
        /// </summary>
        public void ReservePorts()
        {
            lock (_allocatePortsMutex)
            {
                if (_nextMediaPort >= MEDIA_PORT_END)
                {
                    // The media port range has been used go back to the start. Some connections have most likely been closed.
                    _nextMediaPort = MEDIA_PORT_START;
                }

                var inUseUDPPorts = (from p in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners() where p.Port >= _nextMediaPort select p.Port).OrderBy(x => x).ToList();

                _rtpPort = 0;
                _controlPort = 0;

                if (inUseUDPPorts.Count > 0)
                {
                    // Find the first two available for the RTP socket.
                    for (int index = _nextMediaPort; index <= MEDIA_PORT_END; index++)
                    {
                        if (!inUseUDPPorts.Contains(index))
                        {
                            _rtpPort = index;
                            break;
                        }
                    }

                    // Find the next available for the control socket.
                    for (int index = _rtpPort + 1; index <= MEDIA_PORT_END; index++)
                    {
                        if (!inUseUDPPorts.Contains(index))
                        {
                            _controlPort = index;
                            break;
                        }
                    }
                }
                else
                {
                    _rtpPort = MEDIA_PORT_START;
                    _controlPort = MEDIA_PORT_START + 1;
                }

                if (_rtpPort != 0 && _controlPort != 0)
                {
                    bool bindSuccess = false;

                    for (int bindAttempts = 0; bindAttempts <= MAXIMUM_RTP_PORT_BIND_ATTEMPTS; bindAttempts++)
                    {
                        try
                        {
                            // The potential ports have been found now try and use them.
                            _rtpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                            _rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                            _rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                            _rtpSocket.Bind(new IPEndPoint(IPAddress.Any, _rtpPort));

                            _controlSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                            _controlSocket.Bind(new IPEndPoint(IPAddress.Any, _controlPort));

                            logger.LogDebug("RTSP session " + _sessionID + " allocated RTP port of " + _rtpPort + " and control port of " + _controlPort + ".");

                            bindSuccess = true;

                            break;
                        }
                        catch (System.Net.Sockets.SocketException sockExcp)
                        {
                            logger.LogWarning("RTSP session " + _sessionID + " failed to bind to RTP port " + _rtpPort + " and/or control port of " + _controlPort + ", attempt " + bindAttempts + ". " + sockExcp);

                            // Jump up the port range in case there is an OS/network issue closing/cleaning up already used ports.
                            _rtpPort += 100;
                            _controlPort += 100;
                        }
                    }

                    if (!bindSuccess)
                    {
                        throw new ApplicationException("An RTSP session could not bind to the RTP and/or control ports within the range of " + MEDIA_PORT_START + " to " + MEDIA_PORT_END + ".");
                    }
                }
                else
                {
                    throw new ApplicationException("An RTSP session could not allocate the RTP and/or control ports within the range of " + MEDIA_PORT_START + " to " + MEDIA_PORT_END + ".");
                }
            }
        }

        /// <summary>
        /// Starts listenting on the RTP and control ports.
        /// </summary>
        public void Start()
        {
            if (_rtpSocket != null && _controlSocket != null)
            {
                _startedAt = DateTime.Now;

                ThreadPool.QueueUserWorkItem(delegate { RTPReceive(); });

                _controlSocketBuffer = new byte[RECEIVE_BUFFER_SIZE];
                _controlSocket.BeginReceive(_controlSocketBuffer, 0, _controlSocketBuffer.Length, SocketFlags.None, out _controlSocketError, ControlSocketReceive, null);
            }
            else
            {
                logger.LogWarning("An RTSP session could not start as either RTP or control sockets were not available.");
            }
        }

        /// <summary>
        /// Closes the session's RTP and control ports.
        /// </summary>
        public void Close()
        {
            if (!_closed)
            {
                try
                {
                    logger.LogDebug("Closing RTP and control sockets for RTSP session " + _sessionID + ".");

                    _closed = true;

                    if (_rtpSocket != null)
                    {
                        _rtpSocket.Close();
                    }

                    if (_controlSocket != null)
                    {
                        _controlSocket.Close();
                    }
                }
                catch (Exception excp)
                {
                    logger.LogError("Exception RTSPSession.Close. " + excp);
                }
            }
        }

        public bool HasRTPPacket()
        {
            return _packets.Count() > 0;
        }

        public RTPPacket GetNextRTPPacket()
        {
            try
            {
                lock (_packets)
                {
                    return _packets.Dequeue();
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RTSPSession.GetNextRTPPacket. " + excp);
                return null;
            }
        }

        private void RTPReceive()
        {
            try
            {
                Thread.CurrentThread.Name = "rtspsess-rtprecv";

                byte[] buffer = new byte[2048];

                while (!_closed)
                {
                    try
                    {
                        EndPoint remoteEP = (EndPoint)new IPEndPoint(IPAddress.Any, 0);

                        //int bytesRead = _rtpSocket.Receive(buffer);
                        int bytesRead = _rtpSocket.ReceiveFrom(buffer, ref remoteEP);

                        IPEndPoint remoteIPEndPoint = remoteEP as IPEndPoint;

                        //logger.LogDebug("RTPReceive from " + remoteEP + ".");

                        //if (((IPEndPoint)remoteEP).Address.ToString() != _remoteEndPoint.Address.ToString())
                        //{
                        //    var oldEndPoint = _remoteEndPoint;
                        //    _remoteEndPoint = remoteEP as IPEndPoint;
                        //    logger.LogWarning("RtspSession " + _sessionID + " switched to new remote endpoint at " + _remoteEndPoint + " (old end point " + oldEndPoint + ").");
                        //}

                        if (bytesRead > 0)
                        {
                            _rtpLastActivityAt = DateTime.Now;

                            if (bytesRead > RTPHeader.MIN_HEADER_LEN)
                            {
                                if ((buffer[0] >= 20) && (buffer[0] <= 64))
                                {
                                    // DTLS.
                                    if (OnDtlsReceive != null)
                                    {
                                        try
                                        {
                                            OnDtlsReceive(buffer, bytesRead, SendRTPRaw);
                                        }
                                        catch (Exception dtlsExcp)
                                        {
                                            logger.LogError("Exception RTSPSession.RTPReceive DTLS. " + dtlsExcp);
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("RTSPSession.RTPReceive received a DTLS packet from " + _remoteEndPoint + "but bo DTLS handler has been set.");
                                    }
                                }
                                else if ((buffer[0] == 0) || (buffer[0] == 1))
                                {
                                    // STUN.
                                    //if (_iceState != null)
                                    //{
                                    //    try
                                    //    {
                                    //        STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(buffer, bytesRead);

                                    //        //logger.LogDebug("STUN message received from Receiver Client @ " + stunMessage.Header.MessageType + ".");

                                    //        if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
                                    //        {
                                    //            //logger.LogDebug("Sending STUN response to Receiver Client @ " + remoteEndPoint + ".");

                                    //            STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                                    //            stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                                    //            stunResponse.AddXORMappedAddressAttribute(remoteIPEndPoint.Address, remoteIPEndPoint.Port);
                                    //            byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(_iceState.SenderPassword, true);
                                    //            _rtpSocket.SendTo(stunRespBytes, remoteIPEndPoint);

                                    //            //logger.LogDebug("Sending Binding request to Receiver Client @ " + remoteEndPoint + ".");

                                    //            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                    //            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                    //            stunRequest.AddUsernameAttribute(_iceState.ReceiverUser + ":" + _iceState.SenderUser);
                                    //            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                    //            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                                    //            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(_iceState.ReceiverPassword, true);
                                    //            _rtpSocket.SendTo(stunReqBytes, remoteIPEndPoint);

                                    //            _iceState.LastSTUNMessageReceivedAt = DateTime.Now;
                                    //        }
                                    //        else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
                                    //        {
                                    //            if (!_iceState.IsSTUNExchangeComplete)
                                    //            {
                                    //                _iceState.IsSTUNExchangeComplete = true;
                                    //                logger.LogDebug("WebRTC client STUN exchange complete for " + remoteIPEndPoint.ToString() + " and ICE ufrag " + _iceState.ReceiverUser + ".");

                                    //                _remoteEndPoint = remoteIPEndPoint;
                                    //            }
                                    //        }
                                    //        else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
                                    //        {
                                    //            //logger.LogWarning("A STUN binding error response was received from " + remoteIPEndPoint + ".");
                                    //        }
                                    //        else
                                    //        {
                                    //            //logger.LogWarning("An unrecognised STUN request was received from " + remoteIPEndPoint + ".");
                                    //        }
                                    //    }
                                    //    catch (SocketException sockExcp)
                                    //    {
                                    //        logger.LogDebug("RTPSession.RTPReceive STUN processing (" + remoteIPEndPoint + "). " + sockExcp.Message);
                                    //        continue;
                                    //    }
                                    //    catch (Exception stunExcp)
                                    //    {
                                    //        logger.LogWarning("Exception RTPSession.RTPReceive STUN processing (" + remoteIPEndPoint + "). " + stunExcp);
                                    //        continue;
                                    //    }
                                    //}
                                    //else
                                    //{
                                    //    //logger.LogWarning("A STUN reponse was received on RTP socket from " + remoteIPEndPoint + " but no ICE state was set.");
                                    //}
                                }
                                else if ((buffer[0] >= 128) && (buffer[0] <= 191))
                                {
                                    if (buffer[1] == 0xC8 /* RTCP SR */ || buffer[1] == 0xC9 /* RTCP RR */)
                                    {
                                        // RTCP packet.
                                    }
                                    else
                                    {
                                        // RTP Packet.

                                        RTPPacket rtpPacket = new RTPPacket(buffer.Take(bytesRead).ToArray());

                                        //System.Diagnostics.Debug.WriteLine("RTPReceive ssrc " + rtpPacket.Header.SyncSource + ", seq num " + rtpPacket.Header.SequenceNumber + ", timestamp " + rtpPacket.Header.Timestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");
                                        //logger.LogDebug("RTPReceive remote " + remoteIPEndPoint + ", ssrc " + rtpPacket.Header.SyncSource + ", seq num " + rtpPacket.Header.SequenceNumber + ", timestamp " + rtpPacket.Header.Timestamp + ", bytes " + bytesRead + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                        lock (_packets)
                                        {
                                            if (_packets.Count > RTP_PACKETS_MAX_QUEUE_LENGTH)
                                            {
                                                System.Diagnostics.Debug.WriteLine("RTSPSession.RTPReceive packets queue full, clearing.");
                                                logger.LogWarning("RTSPSession.RTPReceive packets queue full, clearing.");

                                                _packets.Clear();

                                                if (OnRTPQueueFull != null)
                                                {
                                                    OnRTPQueueFull();
                                                }
                                            }
                                            else
                                            {
                                                _packets.Enqueue(rtpPacket);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                logger.LogWarning("RTSPSession.RTPReceive an unrecognised packet was received for session ID " + SessionID + " and " + remoteIPEndPoint + ".");
                            }
                        }
                        else
                        {
                            logger.LogWarning("Zero bytes read from RTSPSession RTP socket for session ID " + SessionID + " and " + remoteIPEndPoint + ".");
                            break;
                        }
                    }
                    catch (SocketException sockExcp)
                    {
                        if (!_closed)
                        {
                            _rtpSocketError = sockExcp.SocketErrorCode;

                            if (_rtpSocketError == SocketError.Interrupted)
                            {
                                // If the receive has been interrupted it means the socket has been closed most likely as a result of an RTSP TEARDOWN request.
                                if (OnRTPSocketDisconnected != null)
                                {
                                    OnRTPSocketDisconnected(_sessionID);
                                }
                                break;
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                    catch (Exception excp)
                    {
                        if (!_closed)
                        {
                            logger.LogError("Exception RTSPSession.RTPReceive receiving. " + excp);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.LogError("Exception RTSPSession.RTPReceive. " + excp);

                    if (OnRTPSocketDisconnected != null)
                    {
                        OnRTPSocketDisconnected(_sessionID);
                    }
                }
            }
        }

        private void ControlSocketReceive(IAsyncResult ar)
        {
            try
            {
                int bytesRead = _controlSocket.EndReceive(ar);

                if (_controlSocketError == SocketError.Success)
                {
                    _controlLastActivityAt = DateTime.Now;

                    System.Diagnostics.Debug.WriteLine(bytesRead + " bytes read from Control socket for RTSP session " + _sessionID + ".");

                    if (OnControlDataReceived != null)
                    {
                        OnControlDataReceived(_sessionID, _controlSocketBuffer.Take(bytesRead).ToArray());
                    }

                    _controlSocket.BeginReceive(_controlSocketBuffer, 0, _controlSocketBuffer.Length, SocketFlags.None, out _controlSocketError, ControlSocketReceive, null);
                }
                else
                {
                    if (!_closed)
                    {
                        logger.LogWarning("A " + _controlSocketError + " occurred receiving on Control socket for RTSP session " + _sessionID + ".");

                        if (OnControlSocketDisconnected != null)
                        {
                            OnControlSocketDisconnected(_sessionID);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.LogError("Exception RTSPSession.ControlSocketReceive. " + excp);

                    if (OnControlSocketDisconnected != null)
                    {
                        OnControlSocketDisconnected(_sessionID);
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to send a low quality JPEG image over RTP. This method supports a very abbreviated version of RFC 2435 "RTP Payload Format for JPEG-compressed Video".
        /// It's intended as a quick convenient way to send something like a test pattern image over an RTSP connection. More than likely it won't be suitable when a high
        /// quality image is required since the header used in this method does not support quantization tables.
        /// </summary>
        /// <param name="jpegBytes">The raw encoded bytes of the JPEG image to transmit.</param>
        /// <param name="jpegQuality">The encoder quality of the JPEG image.</param>
        /// <param name="jpegWidth">The width of the JPEG image.</param>
        /// <param name="jpegHeight">The height of the JPEG image.</param>
        public void SendJpegFrame(byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight)
        {
            try
            {
                if (_closed)
                {
                    logger.LogWarning("SendJpegFrame cannot be called on a closed session.");
                }
                else if (_rtpSocketError != SocketError.Success)
                {
                    logger.LogWarning("SendJpegFrame was called for an RTP socket in an error state of " + _rtpSocketError + ".");
                }
                else if (_remoteEndPoint == null)
                {
                    logger.LogWarning("SendJpegFrame frame not sent as remote end point is not yet set.");
                }
                else
                {
                    //_timestamp = (_timestamp == 0) ? DateTimeToNptTimestamp32(DateTime.Now) : (_timestamp + (uint)(RFC_2435_FREQUENCY_BASELINE / DEFAULT_INITAL_FRAME_RATE)) % UInt32.MaxValue;

                    //RecalculateTimestampStep();

                    //_timestamp += _timestampStep;

                    //_timestamp = DateTimeToNptTimestamp32(DateTime.Now);

                    _timestamp = DateTimeToNptTimestamp90K(DateTime.Now);

                    //System.Diagnostics.Debug.WriteLine("Sending " + jpegBytes.Length + " encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ", image dimensions " + jpegWidth + " x " + jpegHeight + ".");

                    for (int index = 0; index * RTP_MAX_PAYLOAD < jpegBytes.Length; index++)
                    {
                        uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                        int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? RTP_MAX_PAYLOAD : jpegBytes.Length - index * RTP_MAX_PAYLOAD;

                        byte[] jpegHeader = CreateLowQualityRtpJpegHeader(offset, jpegQuality, jpegWidth, jpegHeight);

                        List<byte> packetPayload = new List<byte>();
                        packetPayload.AddRange(jpegHeader);
                        packetPayload.AddRange(jpegBytes.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength));

                        RTPPacket rtpPacket = new RTPPacket(packetPayload.Count);
                        rtpPacket.Header.SyncSource = _syncSource;
                        rtpPacket.Header.SequenceNumber = _sequenceNumber++;
                        rtpPacket.Header.Timestamp = _timestamp;
                        rtpPacket.Header.MarkerBit = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? 0 : 1;
                        rtpPacket.Header.PayloadType = (int)SDPMediaFormatsEnum.JPEG;
                        rtpPacket.Payload = packetPayload.ToArray();

                        byte[] rtpBytes = rtpPacket.GetBytes();

                        //System.Diagnostics.Debug.WriteLine(" offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ".");

                        //Stopwatch sw = new Stopwatch();
                        //sw.Start();

                        _rtpSocket.SendTo(rtpBytes, _remoteEndPoint);

                        //sw.Stop();

                        //if (sw.ElapsedMilliseconds > 15)
                        //{
                        //    logger.LogWarning(" SendJpegFrame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        //}
                    }

                    //sw.Stop();
                    //System.Diagnostics.Debug.WriteLine("SendJpegFrame took " + sw.ElapsedMilliseconds + ".");
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.LogWarning("Exception RTPSession.SendJpegFrame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);
                    //_rtpSocketError = SocketError.SocketError;

                    if (OnRTPSocketDisconnected != null)
                    {
                        OnRTPSocketDisconnected(_sessionID);
                    }
                }
            }
        }

        /// <summary>
        /// H264 frames need a two byte header when transmitted over RTP.
        /// </summary>
        /// <param name="frame">The H264 encoded frame to transmit.</param>
        /// <param name="payloadType">The payload type to set on the RTP packet.</param>
        public void SendH264Frame(byte[] frame, int payloadType)
        {
            try
            {
                if (_closed)
                {
                    logger.LogWarning("SendH264Frame cannot be called on a closed session.");
                }
                else if (_rtpSocketError != SocketError.Success)
                {
                    logger.LogWarning("SendH264Frame was called for an RTP socket in an error state of " + _rtpSocketError + ".");
                }
                else if (_remoteEndPoint == null)
                {
                    logger.LogWarning("SendH264Frame frame not sent as remote end point is not yet set.");
                }
                else
                {
                    //RecalculateTimestampStep();

                    //_timestamp += _timestampStep;

                    //_timestamp = DateTimeToNptTimestamp32(DateTime.Now);

                    DateTime packetTimestamp = DateTime.Now;
                    _timestamp = DateTimeToNptTimestamp90K(packetTimestamp);

                    _senderPacketCount++;
                    _senderOctetCount += (uint)frame.Length;

                    if (_rtcpRemoteEndPoint != null && DateTime.Now.Subtract(_senderLastSentAt).TotalSeconds > RTCP_SENDER_REPORT_INTERVAL_SECONDS)
                    {
                        Console.WriteLine(packetTimestamp.ToUniversalTime().ToString("hh:mm:ss:fff"));
                        SendRtcpSenderReport(DateTimeToNptTimestamp(packetTimestamp), _timestamp);
                    }

                    Console.WriteLine("Sending " + frame.Length + " H264 encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ".");

                    for (int index = 0; index * RTP_MAX_PAYLOAD < frame.Length; index++)
                    {
                        uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                        int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < frame.Length) ? RTP_MAX_PAYLOAD : frame.Length - index * RTP_MAX_PAYLOAD;

                        RTPPacket rtpPacket = new RTPPacket(payloadLength + H264_RTP_HEADER_LENGTH);
                        rtpPacket.Header.SyncSource = _syncSource;
                        rtpPacket.Header.SequenceNumber = _sequenceNumber++;
                        rtpPacket.Header.Timestamp = _timestamp;
                        rtpPacket.Header.MarkerBit = 0;
                        rtpPacket.Header.PayloadType = payloadType;

                        // Start RTP packet in frame 0x1c 0x89
                        // Middle RTP packet in frame 0x1c 0x09
                        // Last RTP packet in frame 0x1c 0x49

                        byte[] h264Header = new byte[] { 0x1c, 0x09 };

                        if (index == 0 && frame.Length < RTP_MAX_PAYLOAD)
                        {
                            // First and last RTP packet in the frame.
                            h264Header = new byte[] { 0x1c, 0x49 };
                            rtpPacket.Header.MarkerBit = 1;
                        }
                        else if (index == 0)
                        {
                            h264Header = new byte[] { 0x1c, 0x89 };
                        }
                        else if ((index + 1) * RTP_MAX_PAYLOAD > frame.Length)
                        {
                            h264Header = new byte[] { 0x1c, 0x49 };
                            rtpPacket.Header.MarkerBit = 1;
                        }

                        var h264Stream = frame.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength).ToList();
                        h264Stream.InsertRange(0, h264Header);
                        rtpPacket.Payload = h264Stream.ToArray();

                        byte[] rtpBytes = rtpPacket.GetBytes();

                        //System.Diagnostics.Debug.WriteLine(" offset " + (index * RTP_MAX_PAYLOAD) + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header .MarkerBit + ".");

                        //Stopwatch sw = new Stopwatch();
                        //sw.Start();

                        //_rtpSocket.SendTo(rtpBytes, rtpBytes.Length, SocketFlags.None,  _remoteEndPoint);
                        //SocketAsyncEventArgs socketSendArgs = new SocketAsyncEventArgs();
                        //socketSendArgs.SetBuffer(rtpBytes, 0, rtpBytes.Length);
                        //socketSendArgs.RemoteEndPoint = _remoteEndPoint;
                        //_rtpSocket.SendToAsync(socketSendArgs);

                        _rtpSocket.BeginSendTo(rtpBytes, 0, rtpBytes.Length, SocketFlags.None, _remoteEndPoint, null, null);

                        //sw.Stop();

                        //if (sw.ElapsedMilliseconds > 15)
                        //{
                        //    logger.LogWarning(" SendH264Frame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        //}
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.LogWarning("Exception RTSPSession.SendH264Frame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);

                    if (OnRTPSocketDisconnected != null)
                    {
                        OnRTPSocketDisconnected(_sessionID);
                    }
                }
            }
        }

        /// <summary>
        /// Sends a dynamically sized frame. The RTP marker bit will be set for the last transmitted packet in the frame.
        /// </summary>
        /// <param name="frame">The frame to transmit.</param>
        /// <param name="payloadType">The payload type to set on the RTP packet.</param>
        public void SendVP8Frame(byte[] frame, int payloadType)
        {
            try
            {
                if (_closed)
                {
                    logger.LogWarning("SendVP8Frame cannot be called on a closed session.");
                }
                else if (_rtpSocketError != SocketError.Success)
                {
                    logger.LogWarning("SendVP8Frame was called for an RTP socket in an error state of " + _rtpSocketError + ".");
                }
                else if (_remoteEndPoint == null)
                {
                    // logger.LogWarning("SendVP8Frame frame not sent as remote end point is not yet set.");
                }
                else
                {
                    //RecalculateTimestampStep();

                    //_timestamp += _timestampStep;

                    _timestamp = DateTimeToNptTimestamp90K(DateTime.Now);

                    System.Diagnostics.Debug.WriteLine("Sending " + frame.Length + " encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ".");

                    for (int index = 0; index * RTP_MAX_PAYLOAD < frame.Length; index++)
                    {
                        byte[] vp8HeaderBytes = (index == 0) ? new byte[VP8_RTP_HEADER_LENGTH] { 0x90, 0x80, (byte)(_sequenceNumber % 128) } : new byte[VP8_RTP_HEADER_LENGTH] { 0x80, 0x80, (byte)(_sequenceNumber % 128) };

                        int offset = index * RTP_MAX_PAYLOAD;
                        int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < frame.Length) ? RTP_MAX_PAYLOAD : frame.Length - index * RTP_MAX_PAYLOAD;

                        //RTPPacket rtpPacket = new RTPPacket(payloadLength + VP8_RTP_HEADER_LENGTH + ((_srtp != null) ? SRTP_SIGNATURE_LENGTH : 0));
                        RTPPacket rtpPacket = new RTPPacket(payloadLength + VP8_RTP_HEADER_LENGTH);
                        rtpPacket.Header.SyncSource = _syncSource;
                        rtpPacket.Header.SequenceNumber = _sequenceNumber++;
                        rtpPacket.Header.Timestamp = _timestamp;
                        rtpPacket.Header.MarkerBit = (offset + payloadLength >= frame.Length) ? 1 : 0;
                        rtpPacket.Header.PayloadType = payloadType;

                        Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                        Buffer.BlockCopy(frame, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                        byte[] rtpBytes = rtpPacket.GetBytes();

                        if (RtpProtect != null)
                        {
                            rtpBytes = RtpProtect(rtpBytes);
                        }

                        //System.Diagnostics.Debug.WriteLine(" offset " + (index * RTP_MAX_PAYLOAD) + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header .MarkerBit + ".");

                        //Stopwatch sw = new Stopwatch();
                        //sw.Start();

                        // _rtpSocket.SendTo(rtpBytes, rtpBytes.Length, SocketFlags.None, _remoteEndPoint);

                        //SocketAsyncEventArgs socketSendArgs = new SocketAsyncEventArgs();
                        //socketSendArgs.SetBuffer(rtpBytes, 0, rtpBytes.Length);
                        //socketSendArgs.RemoteEndPoint = _remoteEndPoint;
                        //_rtpSocket.SendToAsync(socketSendArgs);

                        _rtpSocket.BeginSendTo(rtpBytes, 0, rtpBytes.Length, SocketFlags.None, _remoteEndPoint, null, null);
                        //sw.Stop();

                        //if (sw.ElapsedMilliseconds > 15)
                        //{
                        //    logger.LogWarning(" SendVP8Frame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        //}
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.LogWarning("Exception RTSPSession.SendVP8Frame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);

                    if (OnRTPSocketDisconnected != null)
                    {
                        OnRTPSocketDisconnected(_sessionID);
                    }
                }
            }
        }

        /// <summary>
        /// Sends a packet to the RTSP server on the RTP socket.
        /// </summary>
        public void SendRTPRaw(byte[] payload)
        {
            try
            {
                if (!_closed && _rtpSocket != null && _remoteEndPoint != null && _rtpSocketError == SocketError.Success)
                {
                    //_rtpSocket.SendTo(payload, _remoteEndPoint);

                    //SocketAsyncEventArgs socketSendArgs = new SocketAsyncEventArgs();
                    //socketSendArgs.SetBuffer(payload, 0, payload.Length);
                    //socketSendArgs.RemoteEndPoint = _remoteEndPoint;
                    //_rtpSocket.SendToAsync(socketSendArgs);

                    _rtpSocket.BeginSendTo(payload, 0, payload.Length, SocketFlags.None, _remoteEndPoint, SendRtpCallback, _rtpSocket);
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.LogError("Exception RTSPSession.SendRTPRaw attempting to send to " + _remoteEndPoint + ". " + excp);

                    if (OnRTPSocketDisconnected != null)
                    {
                        OnRTPSocketDisconnected(_sessionID);
                    }
                }
            }
        }

        private void SendRtcpSenderReport(ulong ntpTimestamp, uint rtpTimestamp)
        {
            try
            {
                Console.WriteLine("Sending RTCP sender report to remote, ntp timestamp " + ntpTimestamp + ", rtp timestamp " + rtpTimestamp + ", packet count " + _senderPacketCount + ".");

                RTCPPacket senderReport = new RTCPPacket(_syncSource, ntpTimestamp, rtpTimestamp, _senderPacketCount, _senderOctetCount);
                var bytes = senderReport.GetBytes();

                _controlSocket.BeginSendTo(bytes, 0, bytes.Length, SocketFlags.None, _rtcpRemoteEndPoint, SendRtcpCallback, _controlSocket);

                _senderLastSentAt = DateTime.Now;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SendRtcpSenderReport. " + excp);
            }
        }

        private void SendRtpCallback(IAsyncResult ar)
        {
            try
            {
                _rtpSocket.EndSend(ar);
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.LogError("Exception RTSPSession.SendRtpCallback attempting to send to " + _remoteEndPoint + ". " + excp);

                    if (OnRTPSocketDisconnected != null)
                    {
                        OnRTPSocketDisconnected(_sessionID);
                    }
                }
            }
        }

        private void SendRtcpCallback(IAsyncResult ar)
        {
            try
            {
                _controlSocket.EndSend(ar);
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.LogError("Exception RTSPSession.SendRtcpCallback attempting to send to " + _rtcpRemoteEndPoint + ". " + excp);

                    //if (OnRTPSocketDisconnected != null)
                    //{
                    //    OnRTPSocketDisconnected(_sessionID);
                    //}
                }
            }
        }

        /// <summary>
        /// Recalculates the step that should be applied to the RTP timestamp based on the frame rate of the incoming samples.
        /// </summary>
        //private void RecalculateTimestampStep()
        //{
        //    _frameRateSampleCount++;

        //    if (_lastFrameRateCalculationAt == DateTime.MinValue)
        //    {
        //        _lastFrameRateCalculationAt = DateTime.Now;
        //        _timestamp = DateTimeToNptTimestamp32(DateTime.Now);
        //    }
        //    else if (_frameRateSampleCount > MINIMUM_SAMPLES_FOR_FRAME_RATE && DateTime.Now.Subtract(_lastFrameRateCalculationAt).TotalSeconds > ((_isInitialFrameRateCalculation) ? INITIAL_FRAME_RATE_CALCULATION_SECONDS : FRAME_RATE_CALCULATION_SECONDS))
        //    {
        //        // Re-calculate the frame rate.
        //        _isInitialFrameRateCalculation = false;
        //        _frameRate = _frameRateSampleCount / DateTime.Now.Subtract(_lastFrameRateCalculationAt).TotalSeconds;
        //        _timestampStep = Convert.ToUInt32((1 / _frameRate) * RFC_2435_FREQUENCY_BASELINE);
        //        _frameRateSampleCount = 0;
        //        _lastFrameRateCalculationAt = DateTime.Now;
        //    }
        //}

        public static uint DateTimeToNptTimestamp32(DateTime value) { return (uint)((DateTimeToNptTimestamp(value) >> 16) & 0xFFFFFFFF); }

        /// <summary>
        /// Converts specified DateTime value to long NPT time.
        /// </summary>
        /// <param name="value">DateTime value to convert. This value must be in local time.</param>
        /// <returns>Returns NPT value.</returns>
        /// <notes>
        /// Wallclock time (absolute date and time) is represented using the
        /// timestamp format of the Network Time Protocol (NPT), which is in
        /// seconds relative to 0h UTC on 1 January 1900 [4].  The full
        /// resolution NPT timestamp is a 64-bit unsigned fixed-point number with
        /// the integer part in the first 32 bits and the fractional part in the
        /// last 32 bits. In some fields where a more compact representation is
        /// appropriate, only the middle 32 bits are used; that is, the low 16
        /// bits of the integer part and the high 16 bits of the fractional part.
        /// The high 16 bits of the integer part must be determined independently.
        /// </notes>
        public static ulong DateTimeToNptTimestamp(DateTime value)
        {
            //DateTime baseDate = value >= UtcEpoch2036 ? UtcEpoch2036 : UtcEpoch1900;

            //TimeSpan elapsedTime = value > baseDate ? value.ToUniversalTime() - baseDate.ToUniversalTime() : baseDate.ToUniversalTime() - value.ToUniversalTime();

            //long elapsedTicks = elapsedTime.Ticks;

            //long integerPart = elapsedTicks / TimeSpan.TicksPerSecond;
            //long fractionPart = elapsedTicks - integerPart;

            //ulong ntpTS = (ulong)(integerPart & 0xFFFF0000 | fractionPart);

            //Console.WriteLine("NTP timestamp: int=" + integerPart + ", fraction=" + fractionPart + ", ntp ts=" + ntpTS + ".");

            //return ntpTS;

            //return ((ulong)(elapsedTime.TotalSeconds / 1) << 32) | ((uint)(elapsedTime.TotalSeconds) << 32 & 0x0000FFFF);

            DateTime baseDate = value >= UtcEpoch2036 ? UtcEpoch2036 : UtcEpoch1900;

            TimeSpan elapsedTime = value > baseDate ? value.ToUniversalTime() - baseDate.ToUniversalTime() : baseDate.ToUniversalTime() - value.ToUniversalTime();

            long ticks = elapsedTime.Ticks;

            //return (ulong)(elapsedTime.Ticks / TimeSpan.TicksPerSecond << 32) | (ulong)(elapsedTime.Ticks % TimeSpan.TicksPerSecond * 0xFFFFL);
            return (ulong)(elapsedTime.Ticks / TimeSpan.TicksPerSecond << 32) | (ulong)(elapsedTime.Ticks % TimeSpan.TicksPerSecond);
        }

        public static uint DateTimeToNptTimestamp90K(DateTime value)
        {
            DateTime baseDate = value >= UtcEpoch2036 ? UtcEpoch2036 : UtcEpoch1900;

            TimeSpan elapsedTime = value > baseDate ? value.ToUniversalTime() - baseDate.ToUniversalTime() : baseDate.ToUniversalTime() - value.ToUniversalTime();

            var ticks90k = elapsedTime.TotalMilliseconds * 90;

            return (uint)(ticks90k % UInt32.MaxValue);
        }

        /// <summary>
        /// Utility function to create RtpJpegHeader either for initial packet or template for further packets
        /// 
        /// 0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | Type-specific |              Fragment Offset                  |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |      Type     |       Q       |     Width     |     Height    |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// </summary>
        /// <param name="fragmentOffset"></param>
        /// <param name="quality"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        private static byte[] CreateLowQualityRtpJpegHeader(uint fragmentOffset, int quality, int width, int height)
        {
            byte[] rtpJpegHeader = new byte[8] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };

            // Byte 0: Type specific
            //http://tools.ietf.org/search/rfc2435#section-3.1.1

            // Bytes 1 to 3: Three byte fragment offset
            //http://tools.ietf.org/search/rfc2435#section-3.1.2

            if (BitConverter.IsLittleEndian) fragmentOffset = NetConvert.DoReverseEndian(fragmentOffset);

            byte[] offsetBytes = BitConverter.GetBytes(fragmentOffset);
            rtpJpegHeader[1] = offsetBytes[2];
            rtpJpegHeader[2] = offsetBytes[1];
            rtpJpegHeader[3] = offsetBytes[0];

            // Byte 4: JPEG Type.
            //http://tools.ietf.org/search/rfc2435#section-3.1.3

            //Byte 5: http://tools.ietf.org/search/rfc2435#section-3.1.4 (Q)
            rtpJpegHeader[5] = (byte)quality;

            // Byte 6: http://tools.ietf.org/search/rfc2435#section-3.1.5 (Width)
            rtpJpegHeader[6] = (byte)(width / 8);

            // Byte 7: http://tools.ietf.org/search/rfc2435#section-3.1.6 (Height)
            rtpJpegHeader[7] = (byte)(height / 8);

            return rtpJpegHeader;
        }
    }
}
