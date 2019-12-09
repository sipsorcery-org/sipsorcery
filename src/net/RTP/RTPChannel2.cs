//-----------------------------------------------------------------------------
// Filename: RTPChannel.cs
//
// Description: Communications channel to send and receive RTP and RTCP packets
// and whatever else happens to be multiplexed.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Feb 2012	Aaron Clauson	Created, Hobart, Australia.
// 06 Dec 2019  Aaron Clauson   Simplify by removing all frame logic and reduce responsibility
//                              to only managing sending and receiving of packets.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate void PacketReceivedDelegate(UdpReceiver receiver, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, byte[] packet);

    public class UdpReceiver
    {
        private const int RECEIVE_BUFFER_SIZE = 2048;   // MTU is 1452 bytes so this should be heaps.

        private static ILogger logger = Log.Logger;

        private readonly Socket m_udpSocket;
        private byte[] m_recvBuffer;
        private bool m_isClosed;

        /// <summary>
        /// Fires when a new packet has been received in the UDP socket.
        /// </summary>
        public event PacketReceivedDelegate OnPacketReceived;

        /// <summary>
        /// Fires when there is an error attempting to receive on the UDP socket.
        /// </summary>
        public event Action<Exception> OnReceiveError;

        public UdpReceiver(Socket udpSocket)
        {
            m_udpSocket = udpSocket;
            m_recvBuffer = new byte[RECEIVE_BUFFER_SIZE];
        }

        /// <summary>
        /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
        /// return any data received.
        /// </summary>
        public void BeginReceive()
        {
            try
            {
                EndPoint recvEndPoint = (m_udpSocket.LocalEndPoint.AddressFamily == AddressFamily.InterNetwork) ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                m_udpSocket.BeginReceiveMessageFrom(m_recvBuffer, 0, m_recvBuffer.Length, SocketFlags.None, ref recvEndPoint, EndReceiveMessageFrom, null);
            }
            catch (ObjectDisposedException) { } // Thrown when socket is closed. Can be safely ignored.
            catch (Exception excp)
            {
                // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3056
                // the BeginReceiveMessageFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
                // case the sopcket can be considered to be unusable and there's no point trying another receive.
                logger.LogError($"Exception UdpReceiver.Receive. {excp.Message}");
                OnReceiveError?.Invoke(excp);
            }
        }

        /// <summary>
        /// Closes the socket and stops any new receives from being initiated.
        /// </summary>
        public void Close()
        {
            if (!m_isClosed)
            {
                m_isClosed = true;
                m_udpSocket?.Close();
            }
        }

        /// <summary>
        /// Handler for end of the begin receive call.
        /// </summary>
        /// <param name="ar">Contains the results of the receive.</param>
        private void EndReceiveMessageFrom(IAsyncResult ar)
        {
            try
            {
                SocketFlags flags = SocketFlags.None;
                EndPoint remoteEP = (m_udpSocket.LocalEndPoint.AddressFamily == AddressFamily.InterNetwork) ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);

                int bytesRead = m_udpSocket.EndReceiveMessageFrom(ar, ref flags, ref remoteEP, out var packetInfo);

                if (bytesRead > 0)
                {
                    IPEndPoint localEndPoint = new IPEndPoint(packetInfo.Address, (m_udpSocket.LocalEndPoint as IPEndPoint).Port);
                    byte[] packetBuffer = new byte[bytesRead];
                    Buffer.BlockCopy(m_recvBuffer, 0, packetBuffer, 0, bytesRead);
                    OnPacketReceived?.Invoke(this, localEndPoint, remoteEP as IPEndPoint, packetBuffer);
                }
            }
            catch (SocketException sockExcp)
            {
                // Pretty sure the only cause of these exceptions is when an ICMP message comes back indicating there is no listening
                // socket on the other end. Noe there may be cases where this is valid, for example we start sending before the other end
                // starts listening. Bubble it up and let the caller deal with it.
                logger.LogWarning($"SocketException UdpReceiver.EndReceiveMessageFrom ({sockExcp.ErrorCode}). {sockExcp.Message}");
                OnReceiveError?.Invoke(sockExcp);
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception UdpReceiver.EndReceiveMessageFrom. {excp.Message}");
                OnReceiveError?.Invoke(excp);
            }
            finally
            {
                if (!m_isClosed)
                {
                    BeginReceive();
                }
            }
        }
    }

    public enum RTPChannelSocketsEnum
    {
        RTP = 0,
        Control = 1
    }

    public class RTPChannel2
    {
        private const int RTP_KEEP_ALIVE_INTERVAL = 30;         // The interval at which to send RTP keep-alive packets to keep the RTSP server from closing the connection.
        private const int RTP_TIMEOUT_SECONDS = 60;             // If no RTP packets are received during this interval then assume the connection has failed.

        private const int MEDIA_PORT_START = 10000;             // Arbitrary port number to start allocating RTP and control ports from.
        private const int MEDIA_PORT_END = 40000;               // Arbitrary port number that RTP and control ports won't be allocated above.

        private static ILogger logger = Log.Logger;

        private Socket _rtpSocket;
        private UdpReceiver _rtpReceiver;
        private Socket _controlSocket;
        private UdpReceiver _controlReceiver;
        private bool _isClosed;

        /// <summary>
        /// The remote end point RTP packets are being sent.
        /// </summary>
        public IPEndPoint RemoteRTPEndPoint { get; set; }

        /// <summary>
        /// The remote end point RTCP packets are being sent.
        /// </summary>
        public IPEndPoint RemoteControlEndPoint { get; set; }

        /// <summary>
        /// The local port we are listening for RTP (and whatever else is multiplexed) packets on.
        /// </summary>
        public int RTPPort
        {
            get
            {
                if (_rtpSocket != null)
                {
                    return (_rtpSocket.LocalEndPoint as IPEndPoint).Port;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// The local end point the RTP socket is listening on.
        /// </summary>
        public IPEndPoint RTPLocalEndPoint
        {
            get
            {
                if (_rtpSocket != null)
                {
                    return (_rtpSocket.LocalEndPoint as IPEndPoint);
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// The local port we are listening for RTCP packets on.
        /// </summary>
        public int ControlPort
        {
            get
            {
                if (_controlSocket != null)
                {
                    return (_controlSocket.LocalEndPoint as IPEndPoint).Port;
                }
                else
                {
                    return 0;
                }
            }
        }

        public DateTime CreatedAt { get; private set; }
        public DateTime StartedAt { get; private set; }
        public DateTime RTPLastActivityAt { get; private set; }
        public DateTime ControlLastActivityAt { get; private set; }

        //public bool DontTimeout { get; set; }           // If set to true means a server should not timeout this session even if no activity is received on the RTP socket.

        public bool IsClosed
        {
            get { return _isClosed; }
        }

        // Stats and diagnostic variables.
        //private int _lastRTPReceivedAt;
        //private int _tickNow;
        //private int _tickNowCounter;
        //private int _lastFrameSize;
        //private int _lastBWCalcAt;
        //private int _bytesSinceLastBWCalc;
        //private int _framesSinceLastCalc;
        //private double _lastBWCalc;
        //private double _lastFrameRate;

        public event Action<IPEndPoint, byte[]> OnRTPDataReceived;
        public event Action OnRTPSocketDisconnected;
        public event Action<IPEndPoint, byte[]> OnControlDataReceived;
        public event Action OnControlSocketDisconnected;

        /// <summary>
        /// Creates a new RTP channel. The RTP and optionally RTCP sockets will be bound in the constructor.
        /// They do not start reciving until the Start method is called.
        /// </summary>
        /// <param name="createControlSocket">Set to true if a separate RTCP control socket should be created. If RTP and
        /// RTCP are being multiplexed (as they are for WebRTC) there's no need to a separate control socket.</param>
        public RTPChannel2(IPAddress localAddress, bool createControlSocket)
        {
            CreatedAt = DateTime.Now;
            NetServices.CreateRtpSocket(localAddress, MEDIA_PORT_START, MEDIA_PORT_END, createControlSocket, out _rtpSocket, out _controlSocket);
        }

        /// <summary>
        /// Creates a new RTP channel and allows the initial remote end points to be set.
        /// </summary>
        /// <param name="createControlSocket">Set to true if a separate RTCP control socket should be created.</param>
        /// <param name="remoteEndPoint">The remote end point that the </param>
        public RTPChannel2(IPAddress localAddress, bool createControlSocket, IPEndPoint rtpRemoteEndPoint, IPEndPoint controlEndPoint)
            : this(localAddress, createControlSocket)
        {
            RemoteRTPEndPoint = rtpRemoteEndPoint;
            RemoteControlEndPoint = controlEndPoint;
        }

        /// <summary>
        /// Starts listenting on the RTP and control ports.
        /// </summary>
        public void Start()
        {
            StartedAt = DateTime.Now;

            _rtpReceiver = new UdpReceiver(_rtpSocket);
            _rtpReceiver.OnPacketReceived += OnRTPPacketRecived;
            _rtpReceiver.OnReceiveError += OnRTPReceiveError;
            _rtpReceiver.BeginReceive();

            if (_controlSocket != null)
            {
                _controlReceiver = new UdpReceiver(_controlSocket);
                _controlReceiver.OnPacketReceived += OnControlPacketRecived;
                _controlReceiver.OnReceiveError += OnControlReceiveError;
                _controlReceiver.BeginReceive();
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
                    logger.LogDebug("RTPChannel closing, RTP receiver on port " + RTPPort + ".");

                    _isClosed = true;
                    _rtpReceiver?.Close();
                    _controlReceiver?.Close();
                }
                catch (Exception excp)
                {
                    logger.LogError("Exception RTChannel.Close. " + excp);
                }
            }
        }

        public SocketError SendAsync(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
        {
            if (dstEndPoint == null)
            {
                throw new ArgumentException("dstEndPoint", "An empty destination was specified to SendAsync in RTPChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for SendAsync in RTPChannel.");
            }

            try
            {
                Socket sendSocket = _rtpSocket;
                if (sendOn == RTPChannelSocketsEnum.Control)
                {
                    if (_controlSocket == null)
                    {
                        throw new ApplicationException("RTPChannel was asekd to send on the control socket but none exists.");
                    }
                    else
                    {
                        sendSocket = _controlSocket;
                    }
                }

                sendSocket.BeginSendTo(buffer, 0, buffer.Length, SocketFlags.None, dstEndPoint, EndSendTo, sendSocket);
                return SocketError.Success;
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            {
                return SocketError.Disconnecting;
            }
            catch (SocketException sockExcp)
            {
                return sockExcp.SocketErrorCode;
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception RTPChannel.SendAsync. {excp}");
                return SocketError.Fault;
            }
        }

        /// <summary>
        /// Ends an async send on one of the channel's sockets.
        /// </summary>
        /// <param name="ar">The async result to compelete the send with.</param>
        private void EndSendTo(IAsyncResult ar)
        {
            try
            {
                Socket sendSocket = (Socket)ar.AsyncState;
                int bytesSent = sendSocket.EndSendTo(ar);
            }
            catch (SocketException sockExcp)
            {
                // ToDo. Pretty sure these exceptions get thrown when an ICMP message comes back indicating there is no listening
                // socket on the other end. It would be nice to be able to relate that back to the socket that the data was sent to
                // so that we know to stop sending.
                logger.LogWarning($"SocketException RTPChannel EndSendTo ({sockExcp.ErrorCode}). {sockExcp.Message}");
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception RTPChannel EndSendTo. {excp.Message}");
            }
        }

        /// <summary>
        /// Handler for receive errors on the RTP socket.
        /// </summary>
        /// <param name="excp">The exception caught by the receiver.</param>
        private void OnRTPReceiveError(Exception excp)
        {
            if (!_isClosed)
            {
                if (excp is SocketException && (excp as SocketException).SocketErrorCode == SocketError.Interrupted)
                {
                    // If the receive has been interrupted it means the socket has been closed.
                    OnRTPSocketDisconnected?.Invoke();
                }
                else
                {
                    logger.LogWarning($"Receive error on RTP socket. {excp.Message}");
                }
            }
        }

        /// <summary>
        /// Event handler for packets received on the RTP UDP socket.
        /// </summary>
        /// <param name="receiver">The UDP receiver the packet was received on.</param>
        /// <param name="localEndPoint">The local end point it was received on.</param>
        /// <param name="remoteEndPoint">The remote end point of the sender.</param>
        /// <param name="packet">The raw packet received (note this may not be RTP if other protocols are being multiplexed).</param>
        private void OnRTPPacketRecived(UdpReceiver receiver, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, byte[] packet)
        {
            RTPLastActivityAt = DateTime.Now;

            OnRTPDataReceived?.Invoke(remoteEndPoint, packet);

            // TODO: No activity checks need to account for RTP media status for being on hold.
            //if (_tickNowCounter++ > 100)
            //{
            //    _tickNow = Environment.TickCount;
            //    _tickNowCounter = 0;
            //}

            //if ((_tickNow >= _lastRTPReceivedAt && _tickNow - _lastRTPReceivedAt > 1000 * RTP_TIMEOUT_SECONDS)
            //  || (_tickNow < _lastRTPReceivedAt && Int32.MaxValue - _lastRTPReceivedAt + 1 - (Int32.MinValue - _tickNow) > 1000 * RTP_TIMEOUT_SECONDS))
            //{
            //    logger.LogWarning("No RTP packets were received on local port " + RTPPort + " for " + RTP_TIMEOUT_SECONDS + ". The session will now be closed.");
            //    Close();
            //}
        }

        /// <summary>
        /// Handler for receive errors on the Control socket.
        /// </summary>
        /// <param name="excp">The exception caught by the receiver.</param>
        private void OnControlReceiveError(Exception excp)
        {
            if (!_isClosed)
            {
                if (excp is SocketException && (excp as SocketException).SocketErrorCode == SocketError.Interrupted)
                {
                    // If the receive has been interrupted it means the socket has been closed.
                    OnControlSocketDisconnected?.Invoke();
                }
                else
                {
                    logger.LogWarning($"Receive error on Control socket. {excp.Message}");
                }
            }
        }

        /// <summary>
        /// Event handler for packets received on the control UDP socket.
        /// </summary>
        /// <param name="receiver">The UDP receiver the packet was received on.</param>
        /// <param name="localEndPoint">The local end point it was received on.</param>
        /// <param name="remoteEndPoint">The remote end point of the sender.</param>
        /// <param name="packet">The raw packet received which should always be an RTCP packet.</param>
        private void OnControlPacketRecived(UdpReceiver receiver, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, byte[] packet)
        {
            ControlLastActivityAt = DateTime.Now;

            OnControlDataReceived?.Invoke(remoteEndPoint, packet);
        }
    }
}
