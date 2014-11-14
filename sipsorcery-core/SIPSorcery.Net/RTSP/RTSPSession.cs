//-----------------------------------------------------------------------------
// Filename: RTSPSession.cs
//
// Description: Represents an active RTSP session for an RTSP server process that is sending RTP packets
// to a client.
// 
// History:
// 23 Jan 2014	Aaron Clauson	Created. Borrowed bits from https://net7mma.codeplex.com (license @ https://net7mma.codeplex.com/license).
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SIPSorcery.Sys;
using SIPSorceryRTP;
using log4net;

namespace SIPSorcery.Net
{
    public class RTSPSession
    {
        public const int H264_RTP_HEADER_LENGTH = 2;
        public const int JPEG_RTP_HEADER_LENGTH = 8;
        public const int VP8_RTP_HEADER_LENGTH = 0;

        private const int RFC_2435_FREQUENCY_BASELINE = 90000;
        private const int RTP_MAX_PAYLOAD = 1400; //1452;
        private const int RECEIVE_BUFFER_SIZE = 2048;
        private const int MEDIA_PORT_START = 30000;             // Arbitrary port number to start allocating RTP and control ports from.
        private const int MEDIA_PORT_END = 40000;               // Arbitrary port number that RTP and control ports won't be allocated above.
        private const int RTP_PACKETS_MAX_QUEUE_LENGTH = 100;   // The maximum number of RTP packets that will be queued.
        private const int RTP_RECEIVE_BUFFER_SIZE = 100000000;
        private const int RTP_SEND_BUFFER_SIZE = 100000000;
        private const int SRTP_SIGNATURE_LENGTH = 10;           // If SRTP is being used this many extra bytes need to be added to the RTP payload to hold the authentication signature.

        private static DateTime UtcEpoch2036 = new DateTime(2036, 2, 7, 6, 28, 16, DateTimeKind.Utc);
        private static DateTime UtcEpoch1900 = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static DateTime UtcEpoch1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ILog logger = AppState.logger;

        private static Mutex _allocatePortsMutex = new Mutex();

        private Socket _rtpSocket;
        private SocketError _rtpSocketError = SocketError.Success;
        private Socket _controlSocket;
        private SocketError _controlSocketError = SocketError.Success;
        private byte[] _controlSocketBuffer;
        private bool _closed;
        private Queue<RTPPacket> _packets = new Queue<RTPPacket>();
        private SRTPManaged _srtp;

        private IPEndPoint _remoteEndPoint;
        public IPEndPoint RemoteEndPoint
        {
            get { return _remoteEndPoint; }
            set { _remoteEndPoint = value; }
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

        private ICEState _iceState;
        public ICEState ICEState
        {
            get { return _iceState; }
        }

        public bool DontTimeout { get; set; }           // If set to true means a server should not timeout this session even if no activity is received on the RTP socket.

        public bool IsClosed
        {
            get { return _closed; }
        }

        // Fields that track the RTP stream being managed in this session.
        private ushort _sequenceNumber = 1;
        private uint _timestamp = 0;
        private uint _syncSource = 0;

        //public event Action<string, byte[]> OnRTPDataReceived;
        public event Action OnRTPQueueFull;                         // Occurs if the RTP queue fills up and needs to be purged.
        public event Action<string> OnRTPSocketDisconnected;
        public event Action<string, byte[]> OnControlDataReceived;
        public event Action<string> OnControlSocketDisconnected;

        public RTSPSession()
        { }

        public RTSPSession(string sessionID, IPEndPoint remoteEndPoint)
        {
            _sessionID = sessionID;
            _remoteEndPoint = remoteEndPoint;
            _syncSource = Convert.ToUInt32(Crypto.GetRandomInt(0, 9999999));
        }

        public RTSPSession(string sessionID, IPEndPoint remoteEndPoint, ICEState iceState)
            : this(sessionID, remoteEndPoint)
        {
            _iceState = iceState;

            if(_iceState.SRTPKey != null)
            {
                _srtp = new SRTPManaged(Convert.FromBase64String(_iceState.SRTPKey));
            }
        }

        /// <summary>
        /// Attempts to reserve the RTP and control ports for the RTP session.
        /// </summary>
        public void ReservePorts()
        {
            lock (_allocatePortsMutex)
            {
                var inUseUDPPorts = (from p in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners() where p.Port >= MEDIA_PORT_START select p.Port).OrderBy(x => x).ToList();

                _rtpPort = 0;
                _controlPort = 0;

                if (inUseUDPPorts.Count > 0)
                {
                    // Find the first two available for the RTP socket.
                    for (int index = MEDIA_PORT_START; index <= MEDIA_PORT_END; index++)
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
                    // The potential ports have been found now try and use them.
                    _rtpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    _rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                    _rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                    _rtpSocket.Bind(new IPEndPoint(IPAddress.Any, _rtpPort));

                    _controlSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    _controlSocket.Bind(new IPEndPoint(IPAddress.Any, _controlPort));

                    logger.Debug("RTSP session " + _sessionID + " allocated RTP port of " + _rtpPort + " and control port of " + _controlPort + ".");
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
                logger.Warn("An RTSP session could not start as either RTP or control sockets were not available.");
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
                    logger.Debug("Closing RTP and control sockets for RTSP session " + _sessionID + ".");

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
                    logger.Error("Exception RTSPSession.Close. " + excp);
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
                logger.Error("Exception RTSPSession.GetNextRTPPacket. " + excp);
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
                        int bytesRead = _rtpSocket.Receive(buffer);

                        if (bytesRead > 0)
                        {
                            _rtpLastActivityAt = DateTime.Now;

                            if (bytesRead > RTPHeader.MIN_HEADER_LEN)
                            {
                                if ((buffer[0] & 0x80) == 0 && _iceState != null)
                                {
                                    try
                                    {
                                        STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(buffer, buffer.Length);

                                        //logger.Debug("STUN message received from Receiver Client @ " + stunMessage.Header.MessageType + ".");

                                        if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
                                        {
                                            //logger.Debug("Sending STUN response to Receiver Client @ " + remoteEndPoint + ".");

                                            STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                                            stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                                            stunResponse.AddXORMappedAddressAttribute(_remoteEndPoint.Address, _remoteEndPoint.Port);
                                            byte[] stunRespBytes = stunResponse.ToByteBuffer(_iceState.SenderPassword, true);
                                            _rtpSocket.SendTo(stunRespBytes, _remoteEndPoint);

                                            //logger.Debug("Sending Binding request to Receiver Client @ " + remoteEndPoint + ".");

                                            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                            stunRequest.AddUsernameAttribute(_iceState.ReceiverUser + ":" + _iceState.SenderUser);
                                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                            byte[] stunReqBytes = stunRequest.ToByteBuffer(_iceState.ReceiverPassword, true);
                                            _rtpSocket.SendTo(stunReqBytes, _remoteEndPoint);

                                            _iceState.LastSTUNMessageReceivedAt = DateTime.Now;
                                        }
                                        else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
                                        {
                                            _iceState.IsSTUNExchanggeComplete = true;
                                            logger.Debug("WebRTC client STUN exchange complete for " + _remoteEndPoint.ToString() + ".");
                                        }
                                        else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
                                        {
                                            logger.Warn("A STUN binding error response was received from " + _remoteEndPoint + ".");
                                        }
                                        else
                                        {
                                            logger.Warn("An unrecognised STUN request was received from " + _remoteEndPoint + ".");
                                        }
                                    }
                                    catch (SocketException sockExcp)
                                    {
                                        logger.Debug("RTPSession.RTPReceive STUN processing (" + _remoteEndPoint + "). " + sockExcp.Message);
                                        continue;
                                    }
                                    catch (Exception stunExcp)
                                    {
                                        logger.Warn("Exception RTPSession.RTPReceive STUN processing (" + _remoteEndPoint + "). " + stunExcp);
                                        continue;
                                    }
                                }
                                else
                                {
                                    logger.Debug("A non-STUN packet was received Receiver Client.");
                                }

                                RTPPacket rtpPacket = new RTPPacket(buffer.Take(bytesRead).ToArray());

                                //System.Diagnostics.Debug.WriteLine("RTPReceive ssrc " + rtpPacket.Header.SyncSource + ", seq num " + rtpPacket.Header.SequenceNumber + ", timestamp " + rtpPacket.Header.Timestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                lock (_packets)
                                {
                                    if (_packets.Count > RTP_PACKETS_MAX_QUEUE_LENGTH)
                                    {
                                        System.Diagnostics.Debug.WriteLine("RTSPSession.RTPReceive packets queue full, clearing.");
                                        logger.Warn("RTSPSession.RTPReceive packets queue full, clearing.");

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
                        else
                        {
                            logger.Warn("Zero bytes read from RTSPSession RTP socket for session ID " + SessionID + " and " + _remoteEndPoint + ".");
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
                            logger.Error("Exception RTSPSession.RTPReceive receiving. " + excp);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.Error("Exception RTSPSession.RTPReceive. " + excp);

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
                        logger.Warn("A " + _controlSocketError + " occurred receiving on Control socket for RTSP session " + _sessionID + ".");

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
                    logger.Error("Exception RTSPSession.ControlSocketReceive. " + excp);

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
        /// <param name="jpegBytes">The raw encoded bytes of teh JPEG image to transmit.</param>
        /// <param name="jpegQuality">The encoder quality of the JPEG image.</param>
        /// <param name="jpegWidth">The width of the JPEG image.</param>
        /// <param name="jpegHeight">The height of the JPEG image.</param>
        /// <param name="framesPerSecond">The rate at which the JPEG frames are being transmitted at. used to calculate the timestamp.</param>
        public void SendJpegFrame(byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight, int framesPerSecond)
        {
            try
            {
                if (_closed)
                {
                    logger.Warn("SendJpegFrame cannot be called on a closed session.");
                }
                else if (_rtpSocketError != SocketError.Success)
                {
                    logger.Warn("SendJpegFrame was called for an RTP socket in an error state of " + _rtpSocketError + ".");
                }
                else
                {
                    _timestamp = (_timestamp == 0) ? DateTimeToNptTimestamp32(DateTime.Now) : (_timestamp + (uint)(RFC_2435_FREQUENCY_BASELINE / framesPerSecond)) % UInt32.MaxValue;

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

                        Stopwatch sw = new Stopwatch();
                        sw.Start();

                        _rtpSocket.SendTo(rtpBytes, _remoteEndPoint);

                        sw.Stop();

                        if (sw.ElapsedMilliseconds > 15)
                        {
                            logger.Warn(" SendJpegFrame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        }
                    }

                    //sw.Stop();
                    //System.Diagnostics.Debug.WriteLine("SendJpegFrame took " + sw.ElapsedMilliseconds + ".");
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.Warn("Exception RTPSession.SendJpegFrame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);
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
        /// <param name="frameSpacing">The increment to add to the RTP timestamp for each new frame.</param>
        /// <param name="payloadType">The payload type to set on the RTP packet.</param>
        public void SendH264Frame(byte[] frame, uint frameSpacing, int payloadType)
        {
            try
            {
                if (_closed)
                {
                    logger.Warn("SendH264Frame cannot be called on a closed session.");
                }
                else if (_rtpSocketError != SocketError.Success)
                {
                    logger.Warn("SendH264Frame was called for an RTP socket in an error state of " + _rtpSocketError + ".");
                }
                else
                {
                    _timestamp = (_timestamp == 0) ? DateTimeToNptTimestamp32(DateTime.Now) : (_timestamp + frameSpacing) % UInt32.MaxValue;

                    //System.Diagnostics.Debug.WriteLine("Sending " + frame.Length + " H264 encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ".");

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

                        Stopwatch sw = new Stopwatch();
                        sw.Start();

                        _rtpSocket.SendTo(rtpBytes, rtpBytes.Length, SocketFlags.None, _remoteEndPoint);

                        sw.Stop();

                        if (sw.ElapsedMilliseconds > 15)
                        {
                            logger.Warn(" SendH264Frame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.Warn("Exception RTSPSession.SendH264Frame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);

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
        /// <param name="frameSpacing">The increment to add to the RTP timestamp for each new frame.</param>
        /// <param name="payloadType">The payload type to set on the RTP packet.</param>
        public void SendVP8Frame(byte[] frame, uint frameSpacing, int payloadType)
        {
            try
            {
                if (_closed)
                {
                    logger.Warn("SendVP8Frame cannot be called on a closed session.");
                }
                else if (_rtpSocketError != SocketError.Success)
                {
                    logger.Warn("SendVP8Frame was called for an RTP socket in an error state of " + _rtpSocketError + ".");
                }
                else
                {
                    _timestamp = (_timestamp == 0) ? DateTimeToNptTimestamp32(DateTime.Now) : (_timestamp + frameSpacing) % UInt32.MaxValue;

                    //System.Diagnostics.Debug.WriteLine("Sending " + frame.Length + " encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ".");

                    for (int index = 0; index * RTP_MAX_PAYLOAD < frame.Length; index++)
                    {
                        byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                        int offset = index * RTP_MAX_PAYLOAD;
                        int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < frame.Length) ? RTP_MAX_PAYLOAD : frame.Length - index * RTP_MAX_PAYLOAD;

                        RTPPacket rtpPacket = new RTPPacket(payloadLength + vp8HeaderBytes.Length + ((_iceState.SRTPKey != null) ? SRTP_SIGNATURE_LENGTH : 0));
                        rtpPacket.Header.SyncSource = _syncSource;
                        rtpPacket.Header.SequenceNumber = _sequenceNumber++;
                        rtpPacket.Header.Timestamp = _timestamp;
                        rtpPacket.Header.MarkerBit = 0;
                        rtpPacket.Header.PayloadType = payloadType;

                        if ((index + 1) * RTP_MAX_PAYLOAD > frame.Length)
                        {
                            // Last packet in the frame.
                            rtpPacket.Header.MarkerBit = 1;
                        }

                        Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                        Buffer.BlockCopy(frame, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);
                        //var dataStream = frame.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength).ToList();
                        //rtpPacket.Payload = dataStream.ToArray();

                        byte[] rtpBytes = rtpPacket.GetBytes();

                        if (_iceState.SRTPKey != null && _srtp != null)
                        {
                            int rtperr = _srtp.ProtectRTP(rtpBytes, rtpBytes.Length - SRTP_SIGNATURE_LENGTH);
                            if (rtperr != 0)
                            {
                                logger.Warn("An error was returned attempting to sign an SRTP packet for " + _remoteEndPoint + ", error code " + rtperr + ".");
                            }
                        }

                        //System.Diagnostics.Debug.WriteLine(" offset " + (index * RTP_MAX_PAYLOAD) + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header .MarkerBit + ".");

                        Stopwatch sw = new Stopwatch();
                        sw.Start();

                        _rtpSocket.SendTo(rtpBytes, rtpBytes.Length, SocketFlags.None, _remoteEndPoint);

                        sw.Stop();

                        if (sw.ElapsedMilliseconds > 15)
                        {
                            logger.Warn(" SendVP8Frame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.Warn("Exception RTSPSession.SendVP8Frame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);

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
                    _rtpSocket.SendTo(payload, _remoteEndPoint);
                }
            }
            catch (Exception excp)
            {
                if (!_closed)
                {
                    logger.Error("Exception RTSPSession.SendRTPRaw attempting to send to " + _remoteEndPoint + ". " + excp);

                    if (OnRTPSocketDisconnected != null)
                    {
                        OnRTPSocketDisconnected(_sessionID);
                    }
                }
            }
        }

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
        private static ulong DateTimeToNptTimestamp(DateTime value)
        {
            DateTime baseDate = value >= UtcEpoch2036 ? UtcEpoch2036 : UtcEpoch1900;

            TimeSpan elapsedTime = value > baseDate ? value.ToUniversalTime() - baseDate.ToUniversalTime() : baseDate.ToUniversalTime() - value.ToUniversalTime();

            return ((ulong)(elapsedTime.Ticks / TimeSpan.TicksPerSecond) << 32) | (uint)(elapsedTime.Ticks / TimeSpan.TicksPerSecond * 0x100000000L);
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
