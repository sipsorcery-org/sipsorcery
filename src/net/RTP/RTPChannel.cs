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
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    internal delegate void PacketReceivedDelegate(UdpReceiver receiver, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, byte[] packet);

    /// <summary>
    /// A basic UDP socket manager. The RTP channel may need both an RTP and Control socket. This class encapsulates
    /// the common logic for UDP socket management.
    /// </summary>
    internal class UdpReceiver
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
        public event Action<string> OnClosed;

        public UdpReceiver(Socket udpSocket)
        {
            m_udpSocket = udpSocket;
            m_recvBuffer = new byte[RECEIVE_BUFFER_SIZE];
        }

        // ToDo: Supposedly the Event Asynchronous Pattern (EAP) can be turned into the Task Asynchronous Pattern (TAP)
        // with one line. Couldn't make it work as yet.
        //public Task<int> ReceiveAsync(byte[] buffer, int offset, int count, SocketFlags flags)
        //{
        //    return Task<int>.Factory.FromAsync(m_udpSocket.BeginReceive, m_udpSocket.EndReceive,
        //        buffer, offset, count, flags, null, TaskCreationOptions.None);
        //}

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
                // case the socket can be considered to be unusable and there's no point trying another receive.
                logger.LogError($"Exception UdpReceiver.BeginReceive. {excp.Message}");
                Close(excp.Message);
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
                // When socket is closed the object will be disposed of in the middle of a receive.
                if (!m_isClosed)
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
            }
            catch (SocketException)
            {
                // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                // normal RTP operation. For example:
                // - the RTP connection may start sending before the remote socket starts listening,
                // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                //   or new socket during the transition.
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception UdpReceiver.EndReceiveMessageFrom. {excp.Message}");
                Close(excp.Message);
            }
            finally
            {
                if (!m_isClosed)
                {
                    BeginReceive();
                }
            }
        }

        /// <summary>
        /// Closes the socket and stops any new receives from being initiated.
        /// </summary>
        public void Close(string reason)
        {
            if (!m_isClosed)
            {
                m_isClosed = true;
                m_udpSocket?.Close();

                OnClosed?.Invoke(reason);
            }
        }
    }

    public enum RTPChannelSocketsEnum
    {
        RTP = 0,
        Control = 1
    }

    public class RTPChannel : IDisposable
    {
        private const int MEDIA_PORT_START = 10000;             // Arbitrary port number to start allocating RTP and control ports from.
        private const int MEDIA_PORT_END = 40000;               // Arbitrary port number that RTP and control ports won't be allocated above.
        private const int RTCP_SENDER_REPORT_PERIOD_MILLISECONDS = 10000;
        private const int RTCP_RECEIVER_REPORT_PERIOD_MILLISECONDS = 10000;
        private const int NO_RTP_TIMEOUT_SECONDS = 35;          // Number of seconds after which to close a channel if no RTP pakcets are received.

        private static ILogger logger = Log.Logger;

        private Socket m_rtpSocket;
        private UdpReceiver m_rtpReceiver;
        private Socket m_controlSocket;
        private UdpReceiver m_controlReceiver;
        private bool m_isClosed;

        /// <summary>
        /// The last remote end point an RTP packet was sent to or received from. Used for 
        /// reporting purposes only.
        /// </summary>
        private IPEndPoint m_remoteRTPEndPoint { get; set; }

        /// <summary>
        /// The last remote end point an RTCP packet was sent to or received from. Used for
        /// reporting purposes only.
        /// </summary>
        private IPEndPoint m_remoteRtcpEndPoint { get; set; }

        /// <summary>
        /// Number of RTP packets sent to the remote party.
        /// </summary>
        public int PacketsSentCount { get; private set; }

        /// <summary>
        /// Number of RTP bytes sent to the remote party.
        /// </summary>
        public int OctetsSentCount { get; private set; }

        /// <summary>
        /// Number of RTP packets received from the remote party.
        /// </summary>
        public int PacketsReceivedCount { get; private set; }

        /// <summary>
        /// Number of RTP bytes received from the remote party.
        /// </summary>
        public int OctetsReceivedCount { get; private set; }

        /// <summary>
        /// The local port we are listening for RTP (and whatever else is multiplexed) packets on.
        /// </summary>
        public int RTPPort { get; private set; }

        /// <summary>
        /// The local end point the RTP socket is listening on.
        /// </summary>
        public IPEndPoint RTPLocalEndPoint { get; private set; }

        /// <summary>
        /// The local port we are listening for RTCP packets on.
        /// </summary>
        public int ControlPort { get; private set; }

        /// <summary>
        /// The local end point the control socket is listening on.
        /// </summary>
        public IPEndPoint ControlLocalEndPoint { get; private set; }

        public DateTime CreatedAt { get; private set; }
        public DateTime StartedAt { get; private set; }
        public DateTime RTPLastActivityAt { get; private set; }
        public DateTime ControlLastActivityAt { get; private set; }

        //public bool DontTimeout { get; set; }           // If set to true means a server should not timeout this session even if no activity is received on the RTP socket.

        public bool IsClosed
        {
            get { return m_isClosed; }
        }

        /// <summary>
        /// Time to schedule the delivery of RTCP sender reports.
        /// </summary>
        private Timer m_rtcpSenderReportTimer;

        /// <summary>
        /// Time to schedule the delivery of RTCP receiver reports.
        /// </summary>
        private Timer m_rtcpReceiverReportTimer;

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
        public event Action<IPEndPoint, byte[]> OnControlDataReceived;
        public event Action<string> OnClosed;

        /// <summary>
        /// Creates a new RTP channel. The RTP and optionally RTCP sockets will be bound in the constructor.
        /// They do not start receiving until the Start method is called.
        /// </summary>
        /// <param name="createControlSocket">Set to true if a separate RTCP control socket should be created. If RTP and
        /// RTCP are being multiplexed (as they are for WebRTC) there's no need to a separate control socket.</param>
        /// <param name="rtpRemoteEndPoint">The remote end point that the RTP socket is sending to.</param>
        /// <param name="controlEndPoint">The remote end point that the RTCP control socket is sending to.</param>
        /// <param name="mediaStartPort">The media start port.</param>
        /// <param name="mediaEndPort">The media end port.</param>
        public RTPChannel(IPAddress localAddress,
                          bool createControlSocket,
                          IPEndPoint rtpRemoteEndPoint = null,
                          IPEndPoint controlEndPoint = null,
                          int mediaStartPort = MEDIA_PORT_START,
                          int mediaEndPort = MEDIA_PORT_END)
        {
            CreatedAt = DateTime.Now;
            NetServices.CreateRtpSocket(localAddress, mediaStartPort, mediaEndPort, createControlSocket, out m_rtpSocket, out m_controlSocket);

            RTPLocalEndPoint = m_rtpSocket.LocalEndPoint as IPEndPoint;
            RTPPort = RTPLocalEndPoint.Port;
            ControlLocalEndPoint = m_controlSocket.LocalEndPoint as IPEndPoint;
            ControlPort = ControlLocalEndPoint.Port;

            m_remoteRTPEndPoint = rtpRemoteEndPoint;
            m_remoteRtcpEndPoint = controlEndPoint;

            m_rtcpSenderReportTimer = new Timer(SendRtcpSenderReport, null, Crypto.GetRandomInt(1000, RTCP_SENDER_REPORT_PERIOD_MILLISECONDS), RTCP_SENDER_REPORT_PERIOD_MILLISECONDS);
            m_rtcpReceiverReportTimer = new Timer(SendRtcpReceiverReport, null, Crypto.GetRandomInt(1000, RTCP_RECEIVER_REPORT_PERIOD_MILLISECONDS), RTCP_SENDER_REPORT_PERIOD_MILLISECONDS);
        }

        /// <summary>
        /// Starts listenting on the RTP and control ports.
        /// </summary>
        public void Start()
        {
            StartedAt = DateTime.Now;

            m_rtpReceiver = new UdpReceiver(m_rtpSocket);
            m_rtpReceiver.OnPacketReceived += OnRTPPacketRecived;
            m_rtpReceiver.OnClosed += Close;
            m_rtpReceiver.BeginReceive();

            if (m_controlSocket != null)
            {
                m_controlReceiver = new UdpReceiver(m_controlSocket);
                m_controlReceiver.OnPacketReceived += OnControlPacketRecived;
                m_controlReceiver.OnClosed += Close;
                m_controlReceiver.BeginReceive();
            }
        }

        /// <summary>
        /// Closes the session's RTP and control ports.
        /// </summary>
        public void Close(string reason)
        {
            if (!m_isClosed)
            {
                try
                {
                    string closeReason = reason ?? "normal";

                    logger.LogDebug($"RTPChannel closing, RTP receiver on port {RTPPort}. Reason: {closeReason}.");

                    m_isClosed = true;
                    m_rtpReceiver?.Close(null);
                    m_controlReceiver?.Close(null);
                    m_rtcpSenderReportTimer?.Dispose();
                    m_rtcpReceiverReportTimer?.Dispose();

                    OnClosed?.Invoke(closeReason);
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
                Socket sendSocket = m_rtpSocket;
                if (sendOn == RTPChannelSocketsEnum.Control)
                {
                    m_remoteRtcpEndPoint = dstEndPoint;
                    if (m_controlSocket == null)
                    {
                        throw new ApplicationException("RTPChannel was asked to send on the control socket but none exists.");
                    }
                    else
                    {
                        sendSocket = m_controlSocket;
                    }
                }
                else
                {
                    m_remoteRTPEndPoint = dstEndPoint;
                    PacketsSentCount++;
                    OctetsSentCount += buffer.Length; // TODO: Think this needs to adjusted to be only the data and not include RTP headers.
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
        /// Handler for receive errors on the RTP socket. We need to be very specific about
        /// which conditions result in the RTP Channel being closed. For example if our 
        /// RTP socket starts sending first before the other end is ready we can get a socket 
        /// error. In that case the channel should not be closed as the remote socket is still
        /// likely to become available.
        /// </summary>
        /// <param name="excp">The exception caught by the receiver.</param>
        private void OnRTPReceiveError(Exception excp)
        {
            if (!m_isClosed)
            {
                if (excp is SocketException)
                {
                    // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                    // normal RTP operation. For example:
                    // - the RTP connection may start sending before the remote socket starts listening,
                    // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                    //   or new socket during the transition.
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
            if (packet?.Length > 0)
            {
                m_remoteRTPEndPoint = remoteEndPoint;
                RTPLastActivityAt = DateTime.Now;

                PacketsReceivedCount++;
                OctetsReceivedCount += packet.Length; // TODO: Think this needs to adjusted to be only the data and not include RTP headers.

                OnRTPDataReceived?.Invoke(remoteEndPoint, packet);
            }

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
            if (!m_isClosed)
            {
                if (excp is SocketException)
                {
                    // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                    // normal RTP operation. For example:
                    // - the RTP connection may start sending before the remote socket starts listening,
                    // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                    //   or new socket during the transition.
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
            m_remoteRtcpEndPoint = remoteEndPoint;

            OnControlDataReceived?.Invoke(remoteEndPoint, packet);
        }

        private void SendRtcpSenderReport(Object stateInfo)
        {
            try
            {
                if (m_rtpSocket != null)
                {
                    SIPSorcery.Sys.Log.Logger.LogDebug($"SendRtcpSenderReport {m_rtpSocket.LocalEndPoint}->{m_remoteRTPEndPoint} pkts {PacketsSentCount} bytes {OctetsSentCount}");
                }
                else
                {
                    Close(null);
                }
            }
            catch (ObjectDisposedException)  // The RTP socket can disappear between the null check and the report send.
            {
                m_rtcpSenderReportTimer?.Dispose();
            }
            catch (Exception excp)
            {
                // RTCP reports are not crticial enough to buuble the exception up to the application.
                logger.LogError($"Exception SendRtcpSenderReport. {excp.Message}");
                m_rtcpSenderReportTimer?.Dispose();
            }
        }

        private void SendRtcpReceiverReport(Object stateInfo)
        {
            try
            {
                if (m_rtpSocket != null)
                {
                    if ((RTPLastActivityAt != DateTime.MinValue && DateTime.Now.Subtract(RTPLastActivityAt).TotalSeconds > NO_RTP_TIMEOUT_SECONDS) ||
                        (RTPLastActivityAt == DateTime.MinValue && DateTime.Now.Subtract(CreatedAt).TotalSeconds > NO_RTP_TIMEOUT_SECONDS))
                    {
                        logger.LogDebug($"RTP channel on {m_rtpSocket.LocalEndPoint} has not had any activity for over {NO_RTP_TIMEOUT_SECONDS} seconds, closing.");
                        Close("No activity timeout.");
                    }
                    else
                    {
                        logger.LogDebug($"SendRtcpReceiverReport {m_rtpSocket.LocalEndPoint}->{m_remoteRTPEndPoint} pkts {PacketsReceivedCount} bytes {OctetsReceivedCount}");
                    }
                }
                else
                {
                    Close(null);
                }
            }
            catch (ObjectDisposedException) // The RTP socket can disappear between the null check and the report send.
            {
                m_rtcpReceiverReportTimer?.Dispose();
            } 
            catch (Exception excp)
            {
                // RTCP reports are not crticial enough to buuble the exception up to the application.
                logger.LogError($"Exception SendRtcpReceiverReport. {excp.Message}");
                m_rtcpReceiverReportTimer?.Dispose();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            Close(null);
        }

        public void Dispose()
        {
            Close(null);
        }
    }
}
