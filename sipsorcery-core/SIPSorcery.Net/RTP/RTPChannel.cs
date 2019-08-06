//-----------------------------------------------------------------------------
// Filename: RTPChannel.cs
//
// Description: Communications channel to send and receive RTP packets.
//
// History:
// 27 Feb 2012	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007-2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd. 
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
using log4net;

namespace SIPSorcery.Net
{
    public enum FrameTypesEnum
    {
        Audio = 0,
        JPEG = 1,
        H264 = 2,
        VP8 = 3
    }

    public class RTPChannel
    {
        public const int H264_RTP_HEADER_LENGTH = 2;
        public const int JPEG_RTP_HEADER_LENGTH = 8;
        //public const int VP8_RTP_HEADER_LENGTH = 3;
        public const int VP8_RTP_HEADER_LENGTH = 1;

        private const int MAX_FRAMES_QUEUE_LENGTH = 1000;
        private const int RTP_KEEP_ALIVE_INTERVAL = 30;         // The interval at which to send RTP keep-alive packets to keep the RTSP server from closing the connection.
        private const int RTP_TIMEOUT_SECONDS = 60;             // If no RTP pakcets are received during this interval then assume the connection has failed.

        private const int RFC_2435_FREQUENCY_BASELINE = 90000;
        private const int RTP_MAX_PAYLOAD = 1400; //1452;
        private const int RECEIVE_BUFFER_SIZE = 2048;
        private const int MEDIA_PORT_START = 10000;             // Arbitrary port number to start allocating RTP and control ports from.
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

        //private static IPEndPoint _wiresharkEP = new IPEndPoint(IPAddress.Parse("10.1.1.3"), 10001);

        private Socket _rtpSocket;
        private SocketError _rtpSocketError = SocketError.Success;
        private Socket _controlSocket;
        private SocketError _controlSocketError = SocketError.Success;
        private byte[] _controlSocketBuffer;
        private bool _isClosed;
        private Queue<RTPPacket> _packets = new Queue<RTPPacket>();

        private IPEndPoint _remoteEndPoint;
        public IPEndPoint RemoteEndPoint
        {
            get { return _remoteEndPoint; }
            set { _remoteEndPoint = value; }
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

        //private int _rtpPayloadHeaderLength = 0;    // Some RTP media types use a payload header to carry information about the encoded media. Typically this header needs to be stripped off before passing to a decoder.
        //public int RTPPayloadHeaderLength
        //{
        //    get { return _rtpPayloadHeaderLength; }
        //    set { _rtpPayloadHeaderLength = value; }
        //}

        //private ICEState _iceState;
        //public ICEState ICEState
        //{
        //    get { return _iceState; }
        //}

        public bool DontTimeout { get; set; }           // If set to true means a server should not timeout this session even if no activity is received on the RTP socket.

        public bool IsClosed
        {
            get { return _isClosed; }
        }

        // Fields that track the RTP stream being managed in this channel.
        private ushort _sequenceNumber = 1;
        private uint _timestamp = 0;
        private uint _syncSource = 0;

        // Frame variables.
        private List<RTPFrame> _frames = new List<RTPFrame>();
        private uint _lastCompleteFrameTimestamp;
        private FrameTypesEnum _frameType;

        // Stats and diagnostic variables.
        private DateTime _lastRTPReceivedAt;
        private int _lastFrameSize;
        private DateTime _lastBWCalcAt;
        private int _bytesSinceLastBWCalc;
        private int _framesSinceLastCalc;
        private double _lastBWCalc;
        private double _lastFrameRate;

        //public event Action<string, byte[]> OnRTPDataReceived;
        public event Action OnRTPQueueFull;                         // Occurs if the RTP queue fills up and needs to be purged.
        public event Action OnRTPSocketDisconnected;
        public event Action<byte[]> OnControlDataReceived;
        public event Action OnControlSocketDisconnected;
        public event Action<RTPFrame> OnFrameReady;

        public RTPChannel()
        {
            _createdAt = DateTime.Now;
        }

        public RTPChannel(IPEndPoint remoteEndPoint)
            : this()
        {
            _remoteEndPoint = remoteEndPoint;
            _syncSource = Convert.ToUInt32(Crypto.GetRandomInt(0, 9999999));
        }

        //public void SetICEState(ICEState iceState)
        //{
        //    try
        //    {
        //        _iceState = iceState;

        //        //if (_iceState != null && _iceState.SRTPKey != null)
        //        //{
        //        //   _srtp = new SRTPManaged(Convert.FromBase64String(_iceState.SRTPKey), true);
        //        //}
        //    }
        //    catch (Exception excp)
        //    {
        //        logger.Error("Exception SetICEState. " + excp);
        //    }
        //}

        public void ReservePorts()
        {
            ReservePorts(MEDIA_PORT_START, MEDIA_PORT_END);
        }

        /// <summary>
        /// Attempts to reserve the RTP and control ports for the RTP session.
        /// </summary>
        public void ReservePorts(int startPort, int endPort)
        {
            lock (_allocatePortsMutex)
            {
                var inUseUDPPorts = (from p in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners() where p.Port >= startPort select p.Port).OrderBy(x => x).ToList();

                _rtpPort = 0;
                _controlPort = 0;

                if (inUseUDPPorts.Count > 0)
                {
                    // Find the first two available for the RTP socket.
                    for (int index = startPort; index <= endPort; index++)
                    {
                        if (!inUseUDPPorts.Contains(index))
                        {
                            _rtpPort = index;
                            break;
                        }
                    }

                    // Find the next available for the control socket.
                    for (int index = _rtpPort + 1; index <= endPort; index++)
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
                    _rtpPort = startPort;
                    _controlPort = startPort + 1;
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

                    logger.Debug("RTPChannel allocated RTP port of " + _rtpPort + " and control port of " + _controlPort + ".");
                }
                else
                {
                    throw new ApplicationException("An RTPChannel could not allocate the RTP and/or control ports within the range of " + startPort + " to " + endPort + ".");
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
                ThreadPool.QueueUserWorkItem(delegate { ProcessRTPPackets(); });

                _controlSocketBuffer = new byte[RECEIVE_BUFFER_SIZE];
                _controlSocket.BeginReceive(_controlSocketBuffer, 0, _controlSocketBuffer.Length, SocketFlags.None, out _controlSocketError, ControlSocketReceive, null);
            }
            else
            {
                logger.Warn("An RTPChannel could not start as either RTP or control sockets were not available.");
            }
        }

        /// <summary>
        /// Closes the session's RTP and control ports.
        /// </summary>
        public void Close()
        {
            if (!_isClosed)
            {
                try
                {
                    logger.Debug("RTPChannel closing, RTP port " + _rtpPort + ".");

                    _isClosed = true;

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
                    logger.Error("Exception RTChannel.Close. " + excp);
                }
            }
        }

        /// <summary>
        /// Video frames use a header at the start of the RTP payload in order to break up a single video
        /// frame into multiple RTP packets. The RTP channel will need to know the type of header being
        /// used in order to determine when a full frame has been received.
        /// </summary>
        public void SetFrameType(FrameTypesEnum frameType)
        {
            _frameType = frameType;
        }

        private void RTPReceive()
        {
            try
            {
                Thread.CurrentThread.Name = "rtpchanrecv-" + _rtpPort;

                byte[] buffer = new byte[2048];

                while (!_isClosed)
                {
                    try
                    {
                        int bytesRead = _rtpSocket.Receive(buffer);

                        if (bytesRead > 0)
                        {
                            //_rtpSocket.SendTo(buffer, bytesRead, SocketFlags.None, _wiresharkEP);

                            _rtpLastActivityAt = DateTime.Now;

                            if (bytesRead > RTPHeader.MIN_HEADER_LEN)
                            {
                                if ((buffer[0] & 0x80) == 0)
                                {
                                    #region STUN Packet.

                                    //if (_iceState != null)
                                    //{
                                    //    try
                                    //    {
                                    //        STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(buffer, bytesRead);

                                    //        //logger.Debug("STUN message received from Receiver Client @ " + stunMessage.Header.MessageType + ".");

                                    //        if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
                                    //        {
                                    //            //logger.Debug("Sending STUN response to Receiver Client @ " + remoteEndPoint + ".");

                                    //            STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                                    //            stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                                    //            stunResponse.AddXORMappedAddressAttribute(_remoteEndPoint.Address, _remoteEndPoint.Port);
                                    //            byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(_iceState.SenderPassword, true);
                                    //            _rtpSocket.SendTo(stunRespBytes, _remoteEndPoint);

                                    //            //logger.Debug("Sending Binding request to Receiver Client @ " + remoteEndPoint + ".");

                                    //            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                    //            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                    //            stunRequest.AddUsernameAttribute(_iceState.ReceiverUser + ":" + _iceState.SenderUser);
                                    //            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                    //            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(_iceState.ReceiverPassword, true);
                                    //            _rtpSocket.SendTo(stunReqBytes, _remoteEndPoint);

                                    //            _iceState.LastSTUNMessageReceivedAt = DateTime.Now;
                                    //        }
                                    //        else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
                                    //        {
                                    //            if (!_iceState.IsSTUNExchangeComplete)
                                    //            {
                                    //                _iceState.IsSTUNExchangeComplete = true;
                                    //                logger.Debug("WebRTC client STUN exchange complete for " + _remoteEndPoint.ToString() + ".");
                                    //            }
                                    //        }
                                    //        else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
                                    //        {
                                    //            logger.Warn("A STUN binding error response was received from " + _remoteEndPoint + ".");
                                    //        }
                                    //        else
                                    //        {
                                    //            logger.Warn("An unrecognised STUN request was received from " + _remoteEndPoint + ".");
                                    //        }
                                    //    }
                                    //    catch (SocketException sockExcp)
                                    //    {
                                    //        logger.Debug("RTPChannel.RTPReceive STUN processing (" + _remoteEndPoint + "). " + sockExcp.Message);
                                    //        continue;
                                    //    }
                                    //    catch (Exception stunExcp)
                                    //    {
                                    //        logger.Warn("Exception RTPChannel.RTPReceive STUN processing (" + _remoteEndPoint + "). " + stunExcp);
                                    //        continue;
                                    //    }
                                    //}
                                    //else
                                    //{
                                    //    logger.Warn("A STUN reponse was received on RTP socket from " + _remoteEndPoint + " but no ICE state was set.");
                                    //}

                                    #endregion
                                }
                                else
                                {
                                    RTPPacket rtpPacket = new RTPPacket(buffer.Take(bytesRead).ToArray());

                                    //System.Diagnostics.Debug.WriteLine("RTPReceive ssrc " + rtpPacket.Header.SyncSource + ", seq num " + rtpPacket.Header.SequenceNumber + ", timestamp " + rtpPacket.Header.Timestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                    lock (_packets)
                                    {
                                        if (_packets.Count > RTP_PACKETS_MAX_QUEUE_LENGTH)
                                        {
                                            System.Diagnostics.Debug.WriteLine("RTPChannel.RTPReceive packets queue full, clearing.");
                                            logger.Warn("RTPChannel.RTPReceive packets queue full, clearing.");

                                            _packets.Clear();

                                            OnRTPQueueFull?.Invoke();
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
                            logger.Warn("Zero bytes read from RTPChannel RTP socket connected to " + _remoteEndPoint + ".");
                            //break;
                        }
                    }
                    catch (SocketException sockExcp)
                    {
                        if (!_isClosed)
                        {
                            _rtpSocketError = sockExcp.SocketErrorCode;

                            if (_rtpSocketError == SocketError.Interrupted)
                            {
                                // If the receive has been interrupted it means the socket has been closed.
                                OnRTPSocketDisconnected?.Invoke();
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
                        if (!_isClosed)
                        {
                            logger.Error("Exception RTPChannel.RTPReceive receiving. " + excp);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_isClosed)
                {
                    logger.Error("Exception RTPChannel.RTPReceive. " + excp);

                    OnRTPSocketDisconnected?.Invoke();
                }
            }
        }

        private void ProcessRTPPackets()
        {
            try
            {
                Thread.CurrentThread.Name = "rtpchanproc-" + _rtpPort;

                _lastRTPReceivedAt = DateTime.Now;
                _lastBWCalcAt = DateTime.Now;

                while (!_isClosed)
                {
                    while (_packets.Count() > 0)
                    {
                        RTPPacket rtpPacket = null;

                        lock (_packets)
                        {
                            try
                            {
                                rtpPacket = _packets.Dequeue();
                            }
                            catch { }
                        }

                        if (rtpPacket != null)
                        {
                            _lastRTPReceivedAt = DateTime.Now;
                            _bytesSinceLastBWCalc += RTPHeader.MIN_HEADER_LEN + rtpPacket.Payload.Length;

                            //if (_rtpTrackingAction != null)
                            //{
                            //    double bwCalcSeconds = DateTime.Now.Subtract(_lastBWCalcAt).TotalSeconds;
                            //    if (bwCalcSeconds > BANDWIDTH_CALCULATION_SECONDS)
                            //    {
                            //        _lastBWCalc = _bytesSinceLastBWCalc * 8 / bwCalcSeconds;
                            //        _lastFrameRate = _framesSinceLastCalc / bwCalcSeconds;
                            //        _bytesSinceLastBWCalc = 0;
                            //        _framesSinceLastCalc = 0;
                            //        _lastBWCalcAt = DateTime.Now;
                            //    }

                            //    var abbrevURL = (_url.Length <= 50) ? _url : _url.Substring(0, 50);
                            //    string rtpTrackingText = String.Format("Url: {0}\r\nRcvd At: {1}\r\nSeq Num: {2}\r\nTS: {3}\r\nPayoad: {4}\r\nFrame Size: {5}\r\nBW: {6}\r\nFrame Rate: {7}", abbrevURL, DateTime.Now.ToString("HH:mm:ss:fff"), rtpPacket.Header.SequenceNumber, rtpPacket.Header.Timestamp, ((SDPMediaFormatsEnum)rtpPacket.Header.PayloadType).ToString(), _lastFrameSize + " bytes", _lastBWCalc.ToString("0.#") + "bps", _lastFrameRate.ToString("0.##") + "fps");
                            //    _rtpTrackingAction(rtpTrackingText);
                            //}

                            if (rtpPacket.Header.Timestamp < _lastCompleteFrameTimestamp)
                            {
                                System.Diagnostics.Debug.WriteLine("Ignoring RTP packet with timestamp " + rtpPacket.Header.Timestamp + " as it's earlier than the last complete frame.");
                            }
                            else if (_frameType == FrameTypesEnum.Audio)
                            {
                                var frame = RTPFrame.MakeSinglePacketFrame(rtpPacket);

                                try
                                {
                                    //System.Diagnostics.Debug.WriteLine("RTP audio frame ready for timestamp " + frame.Timestamp + ".");
                                    OnFrameReady?.Invoke(frame);
                                }
                                catch (Exception frameReadyExcp)
                                {
                                    logger.Error("Exception RTPChannel.ProcessRTPPackets OnFrameReady Audio. " + frameReadyExcp);
                                }
                            }
                            else
                            {
                                while (_frames.Count > MAX_FRAMES_QUEUE_LENGTH)
                                {
                                    var oldestFrame = _frames.OrderBy(x => x.Timestamp).First();
                                    _frames.Remove(oldestFrame);
                                    System.Diagnostics.Debug.WriteLine("Receive queue full, dropping oldest frame with timestamp " + oldestFrame.Timestamp + ".");
                                }

                                //int frameHeaderLength = 0;

                                //if (_frameType == FrameTypesEnum.VP8)
                                //{
                                //    var vp8Header = RTPVP8Header.GetVP8Header(rtpPacket.Payload);

                                //    // For a VP8 packet only the Payload descriptor part of the header is not part of the encoded bit stream.
                                //    frameHeaderLength = vp8Header.PayloadDescriptorLength;
                                //}

                                var frame = _frames.Where(x => x.Timestamp == rtpPacket.Header.Timestamp).SingleOrDefault();

                                if (frame == null)
                                {
                                    frame = new RTPFrame() { Timestamp = rtpPacket.Header.Timestamp, HasMarker = rtpPacket.Header.MarkerBit == 1, FrameType = _frameType };
                                    frame.AddRTPPacket(rtpPacket);
                                    _frames.Add(frame);
                                }
                                else
                                {
                                    frame.HasMarker = rtpPacket.Header.MarkerBit == 1;
                                    frame.AddRTPPacket(rtpPacket);
                                }

                                if (frame.IsComplete())
                                {
                                    // The frame is ready for handing over to the UI.
                                    byte[] imageBytes = frame.GetFramePayload();

                                    _lastFrameSize = imageBytes.Length;
                                    _framesSinceLastCalc++;

                                    _lastCompleteFrameTimestamp = rtpPacket.Header.Timestamp;
                                    //System.Diagnostics.Debug.WriteLine("Frame ready " + frame.Timestamp + ", sequence numbers " + frame.StartSequenceNumber + " to " + frame.EndSequenceNumber + ",  payload length " + imageBytes.Length + ".");
                                    _frames.Remove(frame);

                                    // Also remove any earlier frames as we don't care about anything that's earlier than the current complete frame.
                                    foreach (var oldFrame in _frames.Where(x => x.Timestamp <= rtpPacket.Header.Timestamp).ToList())
                                    {
                                        System.Diagnostics.Debug.WriteLine("Discarding old frame for timestamp " + oldFrame.Timestamp + ".");
                                        _frames.Remove(oldFrame);
                                    }

                                    if (OnFrameReady != null)
                                    {
                                        try
                                        {
                                            //System.Diagnostics.Debug.WriteLine("RTP frame ready for timestamp " + frame.Timestamp + ".");
                                            OnFrameReady(frame);
                                        }
                                        catch (Exception frameReadyExcp)
                                        {
                                            logger.Error("Exception RTPChannel.ProcessRTPPackets OnFrameReady. " + frameReadyExcp);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (DateTime.Now.Subtract(_lastRTPReceivedAt).TotalSeconds > RTP_TIMEOUT_SECONDS)
                    {
                        logger.Warn("No RTP packets were receoved on local port " + _rtpPort + " for " + RTP_TIMEOUT_SECONDS + ". The session will now be closed.");
                        Close();
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTPChannel.ProcessRTPPackets. " + excp);
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

                    if (bytesRead > 0)
                    {
                        OnControlDataReceived?.Invoke(_controlSocketBuffer.Take(bytesRead).ToArray());
                    }

                    _controlSocket.BeginReceive(_controlSocketBuffer, 0, _controlSocketBuffer.Length, SocketFlags.None, out _controlSocketError, ControlSocketReceive, null);
                }
                else
                {
                    if (!_isClosed)
                    {
                        logger.Warn("A " + _controlSocketError + " occurred.");

                        OnControlSocketDisconnected?.Invoke();
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_isClosed)
                {
                    logger.Error("Exception RTPChannel.ControlSocketReceive. " + excp);

                    OnControlSocketDisconnected?.Invoke();
                }
            }
        }

        /// <summary>
        /// Sends an audio frame where the payload size is less than the maximum RTP packet payload size.
        /// </summary>
        /// <param name="payload">The audio payload to transmit.</param>
        /// <param name="frameSpacing">The increment to add to the RTP timestamp for each new frame.</param>
        /// <param name="payloadType">The payload type to set on the RTP packet.</param>
        public void SendAudioFrame(byte[] payload, uint frameSpacing, int payloadType)
        {
            try
            {
                if (_isClosed)
                {
                    logger.Warn("SendAudioFrame cannot be called on a closed RTP channel.");
                }
                else if (_rtpSocketError != SocketError.Success)
                {
                    logger.Warn("SendAudioFrame was called for an RTP socket in an error state of " + _rtpSocketError + ".");
                }
                else
                {
                    _timestamp = (_timestamp == 0) ? DateTimeToNptTimestamp32(DateTime.Now) : (_timestamp + frameSpacing) % UInt32.MaxValue;

                    RTPPacket rtpPacket = new RTPPacket(payload.Length);
                    rtpPacket.Header.SyncSource = _syncSource;
                    rtpPacket.Header.SequenceNumber = _sequenceNumber++;
                    rtpPacket.Header.Timestamp = _timestamp;
                    rtpPacket.Header.MarkerBit = 1;
                    rtpPacket.Header.PayloadType = payloadType;

                    Buffer.BlockCopy(payload, 0, rtpPacket.Payload, 0, payload.Length);

                    byte[] rtpBytes = rtpPacket.GetBytes();

                    //Stopwatch sw = new Stopwatch();
                    //sw.Start();

                    _rtpSocket.SendTo(rtpBytes, rtpBytes.Length, SocketFlags.None, _remoteEndPoint);

                    //sw.Stop();

                    //if (sw.ElapsedMilliseconds > 15)
                    //{
                    //    logger.Warn(" SendAudioFrame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                    //}
                }
            }
            catch (Exception excp)
            {
                if (!_isClosed)
                {
                    logger.Warn("Exception RTPChannel.SendAudioFrame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);

                    OnRTPSocketDisconnected?.Invoke();
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
                if (_isClosed)
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

                        //Stopwatch sw = new Stopwatch();
                        //sw.Start();

                        _rtpSocket.SendTo(rtpBytes, _remoteEndPoint);

                        //sw.Stop();

                        //if (sw.ElapsedMilliseconds > 15)
                        //{
                        //    logger.Warn(" SendJpegFrame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        //}
                    }

                    //sw.Stop();
                    //System.Diagnostics.Debug.WriteLine("SendJpegFrame took " + sw.ElapsedMilliseconds + ".");
                }
            }
            catch (Exception excp)
            {
                if (!_isClosed)
                {
                    logger.Warn("Exception RTPChannel.SendJpegFrame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);
                    //_rtpSocketError = SocketError.SocketError;

                    OnRTPSocketDisconnected?.Invoke();
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
                if (_isClosed)
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

                        //Stopwatch sw = new Stopwatch();
                        //sw.Start();

                        _rtpSocket.SendTo(rtpBytes, rtpBytes.Length, SocketFlags.None, _remoteEndPoint);

                        //sw.Stop();

                        //if (sw.ElapsedMilliseconds > 15)
                        //{
                        //    logger.Warn(" SendH264Frame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        //}
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_isClosed)
                {
                    logger.Warn("Exception RTPChannel.SendH264Frame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);

                    OnRTPSocketDisconnected?.Invoke();
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
                if (_isClosed)
                {
                    logger.Warn("SendVP8Frame cannot be called on a closed RTP channel.");
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
                        //byte[] vp8HeaderBytes = (index == 0) ? new byte[VP8_RTP_HEADER_LENGTH] { 0x90, 0x80, (byte)(_sequenceNumber % 128) } : new byte[VP8_RTP_HEADER_LENGTH] { 0x80, 0x80, (byte)(_sequenceNumber % 128) };
                        byte[] vp8HeaderBytes = (index == 0) ? new byte[VP8_RTP_HEADER_LENGTH] { 0x10 } : new byte[VP8_RTP_HEADER_LENGTH] { 0x00 };

                        int offset = index * RTP_MAX_PAYLOAD;
                        int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < frame.Length) ? RTP_MAX_PAYLOAD : frame.Length - index * RTP_MAX_PAYLOAD;

                        // RTPPacket rtpPacket = new RTPPacket(payloadLength + VP8_RTP_HEADER_LENGTH + ((_srtp != null) ? SRTP_SIGNATURE_LENGTH : 0));
                        RTPPacket rtpPacket = new RTPPacket(payloadLength + VP8_RTP_HEADER_LENGTH);
                        rtpPacket.Header.SyncSource = _syncSource;
                        rtpPacket.Header.SequenceNumber = _sequenceNumber++;
                        rtpPacket.Header.Timestamp = _timestamp;
                        rtpPacket.Header.MarkerBit = (offset + payloadLength >= frame.Length) ? 1 : 0;
                        rtpPacket.Header.PayloadType = payloadType;

                        Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                        Buffer.BlockCopy(frame, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                        byte[] rtpBytes = rtpPacket.GetBytes();

                        //if (_srtp != null)
                        //{
                        //    int rtperr = _srtp.ProtectRTP(rtpBytes, rtpBytes.Length - SRTP_SIGNATURE_LENGTH);
                        //    if (rtperr != 0)
                        //    {
                        //        logger.Warn("An error was returned attempting to sign an SRTP packet for " + _remoteEndPoint + ", error code " + rtperr + ".");
                        //    }
                        //}

                        //System.Diagnostics.Debug.WriteLine(" offset " + (index * RTP_MAX_PAYLOAD) + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header .MarkerBit + ".");

                        //Stopwatch sw = new Stopwatch();
                        //sw.Start();

                        _rtpSocket.SendTo(rtpBytes, rtpBytes.Length, SocketFlags.None, _remoteEndPoint);

                        //sw.Stop();

                        //if (sw.ElapsedMilliseconds > 15)
                        //{
                        //    logger.Warn(" SendVP8Frame offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ", took " + sw.ElapsedMilliseconds + "ms.");
                        //}
                    }
                }
            }
            catch (Exception excp)
            {
                if (!_isClosed)
                {
                    logger.Warn("Exception RTPChannel.SendVP8Frame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);

                    OnRTPSocketDisconnected?.Invoke();
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
                if (!_isClosed && _rtpSocket != null && _remoteEndPoint != null && _rtpSocketError == SocketError.Success)
                {
                    _rtpSocket.SendTo(payload, _remoteEndPoint);
                }
            }
            catch (Exception excp)
            {
                if (!_isClosed)
                {
                    logger.Error("Exception RTPChannel.SendRTPRaw attempting to send to " + _remoteEndPoint + ". " + excp);

                    OnRTPSocketDisconnected?.Invoke();
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
