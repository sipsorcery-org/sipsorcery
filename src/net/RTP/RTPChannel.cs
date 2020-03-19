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
// 28 Dec 2019  Aaron Clauson   Added RTCP reporting as per RFC3550.
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
            // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
            // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the 
            // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.
            catch (SocketException) 
            {
                //logger.LogWarning($"Socket error {sockExcp.SocketErrorCode} in UdpReceiver.BeginReceive. {sockExcp.Message}");
                //Close(sockExcp.Message);
            }
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
                // It also seems that once a UDP socket pair have exchanged packets and the remote party closes the socket exception will occur
                // in the BeginReceive method (very handy). Follow-up, this doesn't seem to be the case, the socket exception can occur in 
                // BeginReceive before any packets have been exchanged. This means it's not safe to close if BeginReceive gets an ICMP 
                // error since the remote party may not have initialised their socket yet.
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception UdpReceiver.EndReceiveMessageFrom. {excp}");
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

    /// <summary>
    /// A communications channel for transmitting and receiving Real-time Protocol (RTP) and
    /// Real-time Control Protocol (RTCP) packets. This class performs the socket management
    /// functions.
    /// </summary>
    public class RTPChannel : IDisposable
    {
        private const int RTP_PORT_START = 10000;             // Arbitrary port number for the start of the range allocate RTP and control ports from.
        private const int RTP_PORT_END = 20000;               // Arbitrary port number for the end of the range to allocate RTP and control ports from.

        private static ILogger logger = Log.Logger;

        public Socket RtpSocket { get; private set; }
        private UdpReceiver m_rtpReceiver;
        private Socket m_controlSocket;
        private UdpReceiver m_controlReceiver;
        private bool m_started = false;
        private bool m_isClosed;

        /// <summary>
        /// The last remote end point an RTP packet was sent to or received from. Used for 
        /// reporting purposes only.
        /// </summary>
        internal IPEndPoint LastRtpDestination { get; private set; }

        /// <summary>
        /// The last remote end point an RTCP packet was sent to or received from. Used for
        /// reporting purposes only.
        /// </summary>
        internal IPEndPoint LastControlDestination { get; private set; }

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

        public bool IsClosed
        {
            get { return m_isClosed; }
        }

        public event Action<IPEndPoint, IPEndPoint, byte[]> OnRTPDataReceived;
        public event Action<IPEndPoint, IPEndPoint, byte[]> OnControlDataReceived;
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
                          int mediaStartPort = RTP_PORT_START,
                          int mediaEndPort = RTP_PORT_END)
        {
            int startFrom = Crypto.GetRandomInt(RTP_PORT_START, RTP_PORT_END);
            NetServices.CreateRtpSocket(localAddress, mediaStartPort, mediaEndPort, startFrom, createControlSocket, out var rtpSocket, out m_controlSocket);

            RtpSocket = rtpSocket;
            RTPLocalEndPoint = RtpSocket.LocalEndPoint as IPEndPoint;
            RTPPort = RTPLocalEndPoint.Port;
            ControlLocalEndPoint = (m_controlSocket != null) ? m_controlSocket.LocalEndPoint as IPEndPoint : null;
            ControlPort = (m_controlSocket != null) ? ControlLocalEndPoint.Port : 0;

            LastRtpDestination = rtpRemoteEndPoint;
            LastControlDestination = controlEndPoint;
        }

        /// <summary>
        /// Starts listening on the RTP and control ports.
        /// </summary>
        public void Start()
        {
            if (!m_started)
            {
                m_started = true;

                m_rtpReceiver = new UdpReceiver(RtpSocket);
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
                    
                    OnClosed?.Invoke(closeReason);
                }
                catch (Exception excp)
                {
                    logger.LogError("Exception RTPChannel.Close. " + excp);
                }
            }
        }

        public SocketError SendAsync(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
        {
            if(m_isClosed)
            {
                return SocketError.Disconnecting;
            }
            else if (dstEndPoint == null)
            {
                throw new ArgumentException("dstEndPoint", "An empty destination was specified to SendAsync in RTPChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for SendAsync in RTPChannel.");
            }

            try
            {
                Socket sendSocket = RtpSocket;
                if (sendOn == RTPChannelSocketsEnum.Control)
                {
                    LastControlDestination = dstEndPoint;
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
                    LastRtpDestination = dstEndPoint;
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
        /// <param name="ar">The async result to complete the send with.</param>
        private void EndSendTo(IAsyncResult ar)
        {
            try
            {
                Socket sendSocket = (Socket)ar.AsyncState;
                int bytesSent = sendSocket.EndSendTo(ar);
            }
            catch (SocketException sockExcp)
            {
                // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                // normal RTP operation. For example:
                // - the RTP connection may start sending before the remote socket starts listening,
                // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                //   or new socket during the transition.
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
                LastRtpDestination = remoteEndPoint;
                OnRTPDataReceived?.Invoke(localEndPoint, remoteEndPoint, packet);
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
            LastControlDestination = remoteEndPoint;
            OnControlDataReceived?.Invoke(localEndPoint, remoteEndPoint, packet);
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
