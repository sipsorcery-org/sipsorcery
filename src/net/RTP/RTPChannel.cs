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
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate void PacketReceivedDelegate(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, ReadOnlySpan<byte> packet);

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
    public class UdpReceiver
    {
        /// <summary>
        /// MTU is 1452 bytes so this should be heaps.
        /// TODO: What about fragmented UDP packets that are put back together by the OS?
        /// </summary>
        protected const int RECEIVE_BUFFER_SIZE = 2048;

        protected static ILogger logger = Log.Logger;

        protected readonly Socket m_socket;
        protected readonly byte[] m_recvBuffer;
        protected bool m_isClosed;
        protected bool m_isRunningReceive;
        protected readonly IPEndPoint m_localEndPoint;
        protected readonly AddressFamily m_addressFamily;

        public virtual bool IsClosed
        {
            get
            {
                return m_isClosed;
            }
            protected set
            {
                if (m_isClosed == value)
                {
                    return;
                }
                m_isClosed = value;
            }
        }

        public virtual bool IsRunningReceive
        {
            get
            {
                return m_isRunningReceive;
            }
            protected set
            {
                if (m_isRunningReceive == value)
                {
                    return;
                }
                m_isRunningReceive = value;
            }
        }

        /// <summary>
        /// Fires when a new packet has been received on the UDP socket.
        /// </summary>
        public event PacketReceivedDelegate OnPacketReceived;

        /// <summary>
        /// Fires when there is an error attempting to receive on the UDP socket.
        /// </summary>
        public event Action<string> OnClosed;

        public UdpReceiver(Socket socket, int mtu = RECEIVE_BUFFER_SIZE)
        {
            m_socket = socket;
            m_localEndPoint = m_socket.LocalEndPoint as IPEndPoint;
            m_recvBuffer = new byte[mtu];
            m_addressFamily = m_socket.LocalEndPoint.AddressFamily;
            endReceiveFrom = EndReceiveFrom;
        }

        static readonly IPEndPoint IPv4AnyEndPoint = new(IPAddress.Any, 0);
        static readonly IPEndPoint IPv6AnyEndPoint = new(IPAddress.IPv6Any, 0);
        /// <summary>
        /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
        /// return any data received.
        /// </summary>
        public virtual void BeginReceiveFrom()
        {
            //Prevent call BeginReceiveFrom if it is already running
            if(m_isClosed && m_isRunningReceive)
            {
                m_isRunningReceive = false;
            }
            if (m_isRunningReceive || m_isClosed)
            {
                return;
            }

            try
            {
                m_isRunningReceive = true;
                EndPoint recvEndPoint = m_addressFamily == AddressFamily.InterNetwork ? IPv4AnyEndPoint : IPv6AnyEndPoint;
#if FALSE // NET6_0_OR_GREATER bandwidth test falters at some point if this is enabled
                var recive = m_socket.ReceiveFromAsync(m_recvBuffer.AsMemory(), SocketFlags.None, recvEndPoint);
                if (recive.IsCompleted)
                {
                    try
                    {
                        var result = recive.GetAwaiter().GetResult();
                        EndReceiveFrom(result);
                    }
                    catch (Exception excp)
                    {
                        EndReceiveFrom(excp);
                    }
                }
                else
                {
                    recive.AsTask().ContinueWith(t =>
                    {
                        try
                        {
                            EndReceiveFrom(t.GetAwaiter().GetResult());
                        }
                        catch (Exception excp)
                        {
                            EndReceiveFrom(excp);
                        }
                    });
                }
#else
                m_socket.BeginReceiveFrom(m_recvBuffer, 0, m_recvBuffer.Length, SocketFlags.None, ref recvEndPoint, endReceiveFrom, null);
#endif
            }
            // Thrown when socket is closed. Can be safely ignored.
            // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
            // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the 
            // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.
            catch (ObjectDisposedException) 
            {
                m_isRunningReceive = false;
            } 
            catch (SocketException sockExcp)
            {
                m_isRunningReceive = false;
                logger.LogWarning($"Socket error {sockExcp.SocketErrorCode} in UdpReceiver.BeginReceiveFrom. {sockExcp.Message}");
                //Close(sockExcp.Message);
            }
            catch (Exception excp)
            {
                m_isRunningReceive = false;
                // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
                // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
                // case the socket can be considered to be unusable and there's no point trying another receive.
                logger.LogError(excp, $"Exception UdpReceiver.BeginReceiveFrom. {excp.Message}");
                Close(excp.Message);
            }
        }

#if NET6_0_OR_GREATER
        protected virtual void EndReceiveFrom(SocketReceiveFromResult result)
        {
            OnBytesRead(result.RemoteEndPoint, result.ReceivedBytes);

            try
            {
                Drain();
            }
            catch (Exception error)
            {
                EndReceiveFrom(error);
            }
            finally
            {
                m_isRunningReceive = false;
                if (!m_isClosed)
                {
                    BeginReceiveFrom();
                }
            }
        }
#endif

        readonly AsyncCallback endReceiveFrom;
        /// <summary>
        /// Handler for end of the begin receive call.
        /// </summary>
        /// <param name="ar">Contains the results of the receive.</param>
        protected virtual void EndReceiveFrom(IAsyncResult ar)
        {
            try
            {
                // When socket is closed the object will be disposed of in the middle of a receive.
                if (!m_isClosed)
                {
                    EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? IPv4AnyEndPoint : IPv6AnyEndPoint;
                    int bytesRead = m_socket.EndReceiveFrom(ar, ref remoteEP);
                    OnBytesRead(remoteEP, bytesRead);
                }

                Drain();
            }
            catch (Exception error)
            {
                EndReceiveFrom(error);
            }
            finally
            {
                m_isRunningReceive = false;
                if (!m_isClosed)
                {
                    BeginReceiveFrom();
                }
            }
        }

        private void EndReceiveFrom(Exception excp)
        {
            switch(excp)
            {
            case SocketException resetSockExcp when (resetSockExcp.SocketErrorCode == SocketError.ConnectionReset):
                // Thrown when close is called on a socket from this end. Safe to ignore.
                break;
            case SocketException sockExcp:
                // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                // normal RTP operation. For example:
                // - the RTP connection may start sending before the remote socket starts listening,
                // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                //   or new socket during the transition.
                // It also seems that once a UDP socket pair have exchanged packets and the remote party closes the socket exception will occur
                // in the BeginReceive method (very handy). Follow-up, this doesn't seem to be the case, the socket exception can occur in 
                // BeginReceive before any packets have been exchanged. This means it's not safe to close if BeginReceive gets an ICMP 
                // error since the remote party may not have initialised their socket yet.
                logger.LogWarning(sockExcp, $"SocketException UdpReceiver.EndReceiveFrom ({sockExcp.SocketErrorCode}). {sockExcp.Message}");
                break;
            case ObjectDisposedException: // Thrown when socket is closed. Can be safely ignored.
                break;
            case AggregateException:
                foreach (var innerExcp in (excp as AggregateException).InnerExceptions)
                {
                    EndReceiveFrom(innerExcp);
                }
                break;
            default:
                logger.LogError($"Exception UdpReceiver.EndReceiveFrom. {excp}");
                Close(excp.Message);
                break;
            }
        }

        private void OnBytesRead(EndPoint remoteEP, int bytesRead)
        {
            if (bytesRead > 0)
            {
                // During experiments IPPacketInformation wasn't getting set on Linux. Without it the local IP address
                // cannot be determined when a listener was bound to IPAddress.Any (or IPv6 equivalent). If the caller
                // is relying on getting the local IP address on Linux then something may fail.
                //if (packetInfo != null && packetInfo.Address != null)
                //{
                //    localEndPoint = new IPEndPoint(packetInfo.Address, localEndPoint.Port);
                //}

                if (bytesRead < 256 * 1024)
                {
                    Span<byte> packetBuffer = stackalloc byte[bytesRead];
                    CallOnPacketReceivedCallback(m_localEndPoint.Port, remoteEP as IPEndPoint, m_recvBuffer.AsSpan().Slice(0, bytesRead));
                }
                else
                {
                    logger.LogCritical("UDP packet received was larger than 256KB and was ignored.");
                }
            }
        }

        private void Drain()
        {
            // If there is still data available it should be read now. This is more efficient than calling
            // BeginReceiveFrom which will incur the overhead of creating the callback and then immediately firing it.
            // It also avoids the situation where if the application cannot keep up with the network then BeginReceiveFrom
            // will be called synchronously (if data is available it calls the callback method immediately) which can
            // create a very nasty stack.
            while (!m_isClosed && m_socket.Available > 0)
            {
                EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? IPv4AnyEndPoint : IPv6AnyEndPoint;
                int bytesReadSync = m_socket.ReceiveFrom(m_recvBuffer, 0, m_recvBuffer.Length, SocketFlags.None, ref remoteEP);

                if (bytesReadSync > 0)
                {
                    byte[] packetBufferSync = new byte[bytesReadSync];
                    // TODO: When .NET Framework support is dropped switch to using a slice instead of a copy.
                    Buffer.BlockCopy(m_recvBuffer, 0, packetBufferSync, 0, bytesReadSync);
                    CallOnPacketReceivedCallback(m_localEndPoint.Port, remoteEP as IPEndPoint, packetBufferSync);
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Closes the socket and stops any new receives from being initiated.
        /// </summary>
        public virtual void Close(string reason)
        {
            if (!m_isClosed)
            {
                m_isClosed = true;
                m_socket?.Close();

                OnClosed?.Invoke(reason);
            }
        }

        protected virtual void CallOnPacketReceivedCallback(int localPort, IPEndPoint remoteEndPoint, ReadOnlySpan<byte> packet)
        {
            OnPacketReceived?.Invoke(this, localPort, remoteEndPoint, packet);
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
        private static readonly bool isMono = Type.GetType("Mono.Runtime") != null;
        private static ILogger logger = Log.Logger;
        protected UdpReceiver m_rtpReceiver;
        private Socket m_controlSocket;
        protected UdpReceiver m_controlReceiver;
        private bool m_rtpReceiverStarted = false;
        private bool m_controlReceiverStarted = false;
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

        public delegate void DataReceivedDelegate(int localPort, IPEndPoint remoteEndPoint, ReadOnlySpan<byte> buffer);

        public event DataReceivedDelegate OnRTPDataReceived;
        public event DataReceivedDelegate OnControlDataReceived;
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
        public RTPChannel(bool createControlSocket, IPAddress bindAddress, int bindPort = 0, PortRange rtpPortRange = null)
        {
            NetServices.CreateRtpSocket(createControlSocket, bindAddress, bindPort, rtpPortRange, out var rtpSocket, out m_controlSocket);

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
            endSendTo = EndSendTo;
        }

        /// <summary>
        /// Starts listening on the RTP and control ports.
        /// </summary>
        public void Start()
        {
            StartRtpReceiver();
            StartControlReceiver();
        }

        /// <summary>
        /// Starts the UDP receiver that listens for RTP packets.
        /// </summary>
        public void StartRtpReceiver()
        {
            if(!m_rtpReceiverStarted)
            {
                m_rtpReceiverStarted = true;

                logger.LogDebug($"RTPChannel for {RtpSocket.LocalEndPoint} started.");

                m_rtpReceiver = new UdpReceiver(RtpSocket);
                m_rtpReceiver.OnPacketReceived += OnRTPPacketReceived;
                m_rtpReceiver.OnClosed += Close;
                m_rtpReceiver.BeginReceiveFrom();
            }
        }


        /// <summary>
        /// Starts the UDP receiver that listens for RTCP (control) packets.
        /// </summary>
        public void StartControlReceiver()
        {
            if(!m_controlReceiverStarted && m_controlSocket != null)
            {
                m_controlReceiverStarted = true;

                m_controlReceiver = new UdpReceiver(m_controlSocket);
                m_controlReceiver.OnPacketReceived += OnControlPacketReceived;
                m_controlReceiver.OnClosed += Close;
                m_controlReceiver.BeginReceiveFrom();
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
        /// <param name="onFailure">If supplied and given an exception returns <c>true</c>, disables further error processing.</param>
        /// <returns>The result of initiating the send. This result does not reflect anything about
        /// whether the remote party received the packet or not.</returns>
        public virtual SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, ReadOnlySpan<byte> buffer, Func<Exception, bool>? onFailure = null)
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
                    if (isMono && dstEndPoint.AddressFamily == AddressFamily.InterNetwork && sendSocket.AddressFamily != dstEndPoint.AddressFamily)
                    {
                        dstEndPoint = new IPEndPoint(dstEndPoint.Address.MapToIPv6(), dstEndPoint.Port);
                    }

                    //Fix ReceiveFrom logic if any previous exception happens
                    if (!m_rtpReceiver.IsRunningReceive && !m_rtpReceiver.IsClosed)
                    {
                        m_rtpReceiver.BeginReceiveFrom();
                    }

#if NET6_0_OR_GREATER
                    var tmp = ArrayPool<byte>.Shared.Rent(buffer.Length);
                    buffer.CopyTo(tmp);
                    ValueTask<int> send;
                    try
                    {
                        send = sendSocket.SendToAsync(tmp.AsMemory(0, buffer.Length), SocketFlags.None, dstEndPoint);
                    }
                    catch
                    {
                        ArrayPool<byte>.Shared.Return(tmp);
                        throw;
                    }
                    if (send.IsCompleted)
                    {
                        try
                        {
                            send.GetAwaiter().GetResult();
                        }
                        catch (Exception excp)
                        {
                            EndSendTo(excp, dstEndPoint, sendSocket, onFailure);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(tmp);
                        }
                    }
                    else
                    {
                        send.AsTask().ContinueWith(t =>
                        {
                            ArrayPool<byte>.Shared.Return(tmp);
                            if (t.IsFaulted)
                            {
                                EndSendTo(t.Exception, dstEndPoint, sendSocket, onFailure);
                            }
                        });
                    }

#else
                    sendSocket.BeginSendTo(buffer.ToArray(), 0, buffer.Length, SocketFlags.None, dstEndPoint, endSendTo, sendSocket);
#endif
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

        readonly AsyncCallback endSendTo;
        /// <summary>
        /// Ends an async send on one of the channel's sockets.
        /// </summary>
        /// <param name="ar">The async result to complete the send with.</param>
        private void EndSendTo(IAsyncResult ar)
        {
            Socket sendSocket = (Socket)ar.AsyncState;
            try
            {
                int bytesSent = sendSocket.EndSendTo(ar);
            }
            catch (Exception excp)
            {
                EndSendTo(excp, socket: sendSocket);
            }
        }

        private void EndSendTo(Exception exception, IPEndPoint? endPoint = null, Socket? socket = null, Func<Exception, bool>? onFailure = null)
        {
            string kind = socket switch
            {
                null => "Unknown",
                { } m when m == m_controlSocket => "Control",
                _ => "RTP",
            };
            if (onFailure?.Invoke(exception) == true)
            {
                return;
            }
            switch (exception)
            {
            case SocketException sockExcp:
                // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                // normal RTP operation. For example:
                // - the RTP connection may start sending before the remote socket starts listening,
                // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                //   or new socket during the transition.
                logger.LogWarning("SocketException RTPChannel EndSendTo {Kind} {EndPoint} ({ErrorCode}). {Message}", kind, endPoint, sockExcp.ErrorCode, sockExcp.Message);
                break;

            case ObjectDisposedException: // Thrown when socket is closed. Can be safely ignored.
                break;

            case AggregateException aggExcp:
                foreach (var innerExcp in aggExcp.InnerExceptions)
                {
                    EndSendTo(innerExcp, endPoint, socket, onFailure);
                }
                break;

            default:
                logger.LogError("Exception RTPChannel EndSendTo. {Kind} {EndPoint} {Message}", kind, endPoint, exception.Message);
                break;
            }
        }

        /// <summary>
        /// Event handler for packets received on the RTP UDP socket.
        /// </summary>
        /// <param name="receiver">The UDP receiver the packet was received on.</param>
        /// <param name="localPort">The local port it was received on.</param>
        /// <param name="remoteEndPoint">The remote end point of the sender.</param>
        /// <param name="packet">The raw packet received (note this may not be RTP if other protocols are being multiplexed).</param>
        protected virtual void OnRTPPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, ReadOnlySpan<byte> packet)
        {
            if (packet.Length > 0)
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
        private void OnControlPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, ReadOnlySpan<byte> packet)
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
