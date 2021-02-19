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
    public delegate void PacketReceivedDelegate(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet);

    /// <summary>
    /// A basic UDP socket manager. The RTP channel may need both an RTP and Control socket. This class encapsulates
    /// the common logic for UDP socket management.
    /// </summary>
    /// <remarks>
    /// .NET Framework Socket source:
    /// https://referencesource.microsoft.com/#system/net/system/net/Sockets/Socket.cs
    /// .NET Core Socket source:
    /// https://github.com/dotnet/runtime/blob/master/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.cs
    /// Mono Socket source:
    /// https://github.com/mono/mono/blob/master/mcs/class/System/System.Net.Sockets/Socket.cs
    /// </remarks>
    public sealed class UdpReceiver
    {
        /// <summary>
        /// MTU is 1452 bytes so this should be heaps.
        /// TODO: What about fragmented UDP packets that are put back together by the OS?
        /// </summary>
        private const int RECEIVE_BUFFER_SIZE = 2048;

        private static ILogger logger = Log.Logger;

        private readonly Socket m_udpSocket;
        private byte[] m_recvBuffer;
        private bool m_isClosed;
        private IPEndPoint m_localEndPoint;
        private AddressFamily m_addressFamily;

        /// <summary>
        /// Fires when a new packet has been received on the UDP socket.
        /// </summary>
        public event PacketReceivedDelegate OnPacketReceived;

        /// <summary>
        /// Fires when there is an error attempting to receive on the UDP socket.
        /// </summary>
        public event Action<string> OnClosed;

        public UdpReceiver(Socket udpSocket)
        {
            m_udpSocket = udpSocket;
            m_localEndPoint = m_udpSocket.LocalEndPoint as IPEndPoint;
            m_recvBuffer = new byte[RECEIVE_BUFFER_SIZE];
            m_addressFamily = m_udpSocket.LocalEndPoint.AddressFamily;
        }

        /// <summary>
        /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
        /// return any data received.
        /// </summary>
        public void BeginReceiveFrom()
        {
            try
            {
                EndPoint recvEndPoint = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                m_udpSocket.BeginReceiveFrom(m_recvBuffer, 0, m_recvBuffer.Length, SocketFlags.None, ref recvEndPoint, EndReceiveFrom, null);
            }
            catch (ObjectDisposedException) { } // Thrown when socket is closed. Can be safely ignored.
            // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
            // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the 
            // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.
            catch (SocketException sockExcp)
            {
                logger.LogWarning($"Socket error {sockExcp.SocketErrorCode} in UdpReceiver.BeginReceiveFrom. {sockExcp.Message}");
                //Close(sockExcp.Message);
            }
            catch (Exception excp)
            {
                // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
                // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
                // case the socket can be considered to be unusable and there's no point trying another receive.
                logger.LogError(excp, $"Exception UdpReceiver.BeginReceiveFrom. {excp.Message}");
                Close(excp.Message);
            }
        }

        /// <summary>
        /// Handler for end of the begin receive call.
        /// </summary>
        /// <param name="ar">Contains the results of the receive.</param>
        private void EndReceiveFrom(IAsyncResult ar)
        {
            try
            {
                // When socket is closed the object will be disposed of in the middle of a receive.
                if (!m_isClosed)
                {
                    EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                    int bytesRead = m_udpSocket.EndReceiveFrom(ar, ref remoteEP);

                    if (bytesRead > 0)
                    {
                        // During experiments IPPacketInformation wasn't getting set on Linux. Without it the local IP address
                        // cannot be determined when a listener was bound to IPAddress.Any (or IPv6 equivalent). If the caller
                        // is relying on getting the local IP address on Linux then something may fail.
                        //if (packetInfo != null && packetInfo.Address != null)
                        //{
                        //    localEndPoint = new IPEndPoint(packetInfo.Address, localEndPoint.Port);
                        //}

                        byte[] packetBuffer = new byte[bytesRead];
                        // TODO: When .NET Frmework support is dropped switch to using a slice instead of a copy.
                        Buffer.BlockCopy(m_recvBuffer, 0, packetBuffer, 0, bytesRead);
                        OnPacketReceived?.Invoke(this, m_localEndPoint.Port, remoteEP as IPEndPoint, packetBuffer);
                    }
                }

                // If there is still data available it should be read now. This is more efficient than calling
                // BeginReceiveFrom which will incur the overheaed of creating the callback and then immediately firing it.
                // It also avoids the situation where if the application cannot keep up with the network then BeginReceiveFrom
                // will be called synchronously (if data is available it calls the callback method immediately) which can
                // create a very nasty stack.
                if (!m_isClosed && m_udpSocket.Available > 0)
                {
                    EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);

                    while (!m_isClosed && m_udpSocket.Available > 0)
                    {
                        int bytesReadSync = m_udpSocket.ReceiveFrom(m_recvBuffer, 0, m_recvBuffer.Length, SocketFlags.None, ref remoteEP);

                        if (bytesReadSync > 0)
                        {
                            byte[] packetBufferSync = new byte[bytesReadSync];
                            // TODO: When .NET Frmework support is dropped switch to using a slice instead of a copy.
                            Buffer.BlockCopy(m_recvBuffer, 0, packetBufferSync, 0, bytesReadSync);
                            OnPacketReceived?.Invoke(this, m_localEndPoint.Port, remoteEP as IPEndPoint, packetBufferSync);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            catch (SocketException resetSockExcp) when (resetSockExcp.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Thrown when close is called on a socket from this end. Safe to ignore.
            }
            catch (SocketException sockExcp)
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
                logger.LogWarning(sockExcp,$"SocketException UdpReceiver.EndReceiveFrom ({sockExcp.SocketErrorCode}). {sockExcp.Message}");
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception UdpReceiver.EndReceiveFrom. {excp}");
                Close(excp.Message);
            }
            finally
            {
                if (!m_isClosed)
                {
                    BeginReceiveFrom();
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
        private static ILogger logger = Log.Logger;
        protected UdpReceiver m_rtpReceiver;
        private Socket m_controlSocket;
        protected UdpReceiver m_controlReceiver;
        private bool m_started = false;
        private bool m_isClosed;

        public Socket RtpSocket { get; private set; }

        /// <summary>
        /// The last remote end point an RTP packet was sent to or received from. Used for 
        /// reporting purposes only.
        /// </summary>
        protected IPEndPoint LastRtpDestination { get; set; }

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

        /// <summary>
        /// Returns true if the RTP socket supports dual mode IPv4 and IPv6. If the control
        /// socket exists it will be the same.
        /// </summary>
        public bool IsDualMode
        {
            get
            {
                if (RtpSocket != null && RtpSocket.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return RtpSocket.DualMode;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool IsClosed
        {
            get { return m_isClosed; }
        }

        public event Action<int, IPEndPoint, byte[]> OnRTPDataReceived;
        public event Action<int, IPEndPoint, byte[]> OnControlDataReceived;
        public event Action<string> OnClosed;

        /// <summary>
        /// Creates a new RTP channel. The RTP and optionally RTCP sockets will be bound in the constructor.
        /// They do not start receiving until the Start method is called.
        /// </summary>
        /// <param name="createControlSocket">Set to true if a separate RTCP control socket should be created. If RTP and
        /// RTCP are being multiplexed (as they are for WebRTC) there's no need to a separate control socket.</param>
        /// <param name="bindAddress">Optional. An IP address belonging to a local interface that will be used to bind
        /// the RTP and control sockets to. If left empty then the IPv6 any address will be used if IPv6 is supported
        /// and fallback to the IPv4 any address.</param>
        /// <param name="bindPort">Optional. The specific port to attempt to bind the RTP port on.</param>
        public RTPChannel(bool createControlSocket, IPAddress bindAddress, int bindPort = 0)
        {
            NetServices.CreateRtpSocket(createControlSocket, bindAddress, bindPort, out var rtpSocket, out m_controlSocket);

            if (rtpSocket == null)
            {
                throw new ApplicationException("The RTP channel was not able to create an RTP socket.");
            }
            else if (createControlSocket && m_controlSocket == null)
            {
                throw new ApplicationException("The RTP channel was not able to create a Control socket.");
            }

            RtpSocket = rtpSocket;
            RTPLocalEndPoint = RtpSocket.LocalEndPoint as IPEndPoint;
            RTPPort = RTPLocalEndPoint.Port;
            ControlLocalEndPoint = (m_controlSocket != null) ? m_controlSocket.LocalEndPoint as IPEndPoint : null;
            ControlPort = (m_controlSocket != null) ? ControlLocalEndPoint.Port : 0;
        }

        /// <summary>
        /// Starts listening on the RTP and control ports.
        /// </summary>
        public void Start()
        {
            if (!m_started)
            {
                m_started = true;

                logger.LogDebug($"RTPChannel for {RtpSocket.LocalEndPoint} started.");

                m_rtpReceiver = new UdpReceiver(RtpSocket);
                m_rtpReceiver.OnPacketReceived += OnRTPPacketReceived;
                m_rtpReceiver.OnClosed += Close;
                m_rtpReceiver.BeginReceiveFrom();

                if (m_controlSocket != null)
                {
                    m_controlReceiver = new UdpReceiver(m_controlSocket);
                    m_controlReceiver.OnPacketReceived += OnControlPacketReceived;
                    m_controlReceiver.OnClosed += Close;
                    m_controlReceiver.BeginReceiveFrom();
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

                    if (m_controlReceiver == null)
                    {
                        logger.LogDebug($"RTPChannel closing, RTP receiver on port {RTPPort}. Reason: {closeReason}.");
                    }
                    else
                    {
                        logger.LogDebug($"RTPChannel closing, RTP receiver on port {RTPPort}, Control receiver on port {ControlPort}. Reason: {closeReason}.");
                    }

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

        /// <summary>
        /// The send method for the RTP channel.
        /// </summary>
        /// <param name="sendOn">The socket to send on. Can be the RTP or Control socket.</param>
        /// <param name="dstEndPoint">The destination end point to send to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <returns>The result of initiating the send. This result does not reflect anything about
        /// whether the remote party received the packet or not.</returns>
        internal virtual SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
        {
            if (m_isClosed)
            {
                return SocketError.Disconnecting;
            }
            else if (dstEndPoint == null)
            {
                throw new ArgumentException("dstEndPoint", "An empty destination was specified to Send in RTPChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in RTPChannel.");
            }
            else if (IPAddress.Any.Equals(dstEndPoint.Address) || IPAddress.IPv6Any.Equals(dstEndPoint.Address))
            {
                logger.LogWarning($"The destination address for Send in RTPChannel cannot be {dstEndPoint.Address}.");
                return SocketError.DestinationAddressRequired;
            }
            else
            {
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

                    //Prevent Send to IPV4 while socket is IPV6 (Mono Error)
                    if (dstEndPoint.AddressFamily == AddressFamily.InterNetwork && sendSocket.AddressFamily != dstEndPoint.AddressFamily)
                    {
                        dstEndPoint = new IPEndPoint(dstEndPoint.Address.MapToIPv6(), dstEndPoint.Port);
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
                    logger.LogError($"Exception RTPChannel.Send. {excp}");
                    return SocketError.Fault;
                }
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
                logger.LogWarning(sockExcp, $"SocketException RTPChannel EndSendTo ({sockExcp.ErrorCode}). {sockExcp.Message}");
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
        /// <param name="localPort">The local port it was received on.</param>
        /// <param name="remoteEndPoint">The remote end point of the sender.</param>
        /// <param name="packet">The raw packet received (note this may not be RTP if other protocols are being multiplexed).</param>
        protected virtual void OnRTPPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
        {
            if (packet?.Length > 0)
            {
                LastRtpDestination = remoteEndPoint;
                OnRTPDataReceived?.Invoke(localPort, remoteEndPoint, packet);
            }
        }

        /// <summary>
        /// Event handler for packets received on the control UDP socket.
        /// </summary>
        /// <param name="receiver">The UDP receiver the packet was received on.</param>
        /// <param name="localPort">The local port it was received on.</param>
        /// <param name="remoteEndPoint">The remote end point of the sender.</param>
        /// <param name="packet">The raw packet received which should always be an RTCP packet.</param>
        private void OnControlPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
        {
            LastControlDestination = remoteEndPoint;
            OnControlDataReceived?.Invoke(localPort, remoteEndPoint, packet);
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
