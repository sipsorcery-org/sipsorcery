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
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{

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
        protected readonly SocketUdpConnection? m_controlConnection;
        private bool m_rtpReceiverStarted = false;
        private bool m_controlReceiverStarted = false;
        private bool m_isClosed;

        public SocketUdpConnection RtpConnection { get; }

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
        /// This end point can be set by the application if it is able to determine the external endoint of the RTP socket
        /// in a NAT environment. This end point will be used when sending SDP offers and answers to indicate the RTP socket's
        /// public end point. This end point will be used when setting the conenction information in the SDP for VoIP scenarios,
        /// it's not used for WebRTC scenarios since the ICE negotiation does a much better job of determining the public end point.
        /// Gerenally this property should not be set and it's only provided for some specialist cases and diagnostic purposes. If
        /// the RTP channel is behind anythng but a full cone NAT then setting this property is likely to cause more harm than good.
        /// </summary>
        public IPEndPoint RTPDynamicNATEndPoint { get; set; }

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
                if (RtpConnection.Socket != null && RtpConnection.Socket.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return RtpConnection.Socket.DualMode;
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

        /// <summary>
        /// int localPort, IPEndPoint remoteEndPoint, byte[] packet.
        /// </summary>
        public event Action<int, IPEndPoint, ReadOnlyMemory<byte>> OnRTPDataReceived;
        public event Action<int, IPEndPoint, ReadOnlyMemory<byte>> OnControlDataReceived;
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
            NetServices.CreateRtpSocket(createControlSocket, bindAddress, bindPort, rtpPortRange, out var rtpSocket, out var controlSocket);

            if (rtpSocket == null)
            {
                throw new ApplicationException("The RTP channel was not able to create an RTP socket.");
            }
            else if (createControlSocket && controlSocket == null)
            {
                throw new ApplicationException("The RTP channel was not able to create a Control socket.");
            }

            RTPLocalEndPoint = rtpSocket.LocalEndPoint as IPEndPoint;
            RTPPort = RTPLocalEndPoint.Port;
            ControlLocalEndPoint = (controlSocket != null) ? controlSocket.LocalEndPoint as IPEndPoint : null;
            ControlPort = (controlSocket != null) ? ControlLocalEndPoint.Port : 0;

            RtpConnection = new SocketUdpConnection(rtpSocket);
            RtpConnection.OnPacketReceived += OnRTPPacketReceived;
            RtpConnection.OnClosed += Close;

            if (controlSocket is not null)
            {
                m_controlConnection = new SocketUdpConnection(controlSocket);
                m_controlConnection.OnPacketReceived += OnControlPacketReceived;
                m_controlConnection.OnClosed += Close;
            }
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
            if (!m_rtpReceiverStarted)
            {
                m_rtpReceiverStarted = true;

                logger.LogRtpChannelStarted(RtpConnection.Socket.LocalEndPoint);

                RtpConnection.BeginReceiveFrom();
            }
        }

        /// <summary>
        /// Starts the UDP receiver that listens for RTCP (control) packets.
        /// </summary>
        public void StartControlReceiver()
        {
            if (!m_controlReceiverStarted && m_controlConnection != null)
            {
                m_controlReceiverStarted = true;

                m_controlConnection.BeginReceiveFrom();
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
                    var closeReason = reason ?? "normal";

                    if (m_controlConnection == null)
                    {
                        logger.LogRtpChannelClosingRtpOnly(RTPPort, closeReason);
                    }
                    else
                    {
                        logger.LogRtpChannelClosing(RTPPort, ControlPort, closeReason);
                    }

                    m_isClosed = true;
                    RtpConnection?.Close(null);
                    m_controlConnection?.Close(null);

                    OnClosed?.Invoke(closeReason);
                }
                catch (Exception excp)
                {
                    logger.LogRtpSessionClose(excp.Message, excp);
                }
            }
        }

        /// <summary>
        /// The send method for the RTP channel.
        /// </summary>
        /// <param name="sendOn">The socket to send on. Can be the RTP or Control socket.</param>
        /// <param name="dstEndPoint">The destination end point to send to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <param name="memoryOwner">The onwer of the <paramref name="buffer"/> memory.</param>
        /// <returns>The result of initiating the send. This result does not reflect anything about
        /// whether the remote party received the packet or not.</returns>
        public virtual SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, ReadOnlyMemory<byte> buffer, IDisposable? memoryOwner = null)
        {
            if (m_isClosed)
            {
                return SocketError.Disconnecting;
            }
            else if (dstEndPoint == null)
            {
                throw new ArgumentException("An empty destination was specified to Send in RTPChannel.", nameof(dstEndPoint));
            }
            else if (buffer.IsEmpty)
            {
                throw new ArgumentException("The buffer must be set and non empty for Send in RTPChannel.", nameof(buffer));
            }
            else if (IPAddress.Any.Equals(dstEndPoint.Address) || IPAddress.IPv6Any.Equals(dstEndPoint.Address))
            {
                logger.LogRtpDestinationAddressInvalid(dstEndPoint.Address);
                return SocketError.DestinationAddressRequired;
            }
            else
            {
                try
                {
                    var connection = RtpConnection;
                    if (sendOn == RTPChannelSocketsEnum.Control)
                    {
                        LastControlDestination = dstEndPoint;
                        if (m_controlConnection == null)
                        {
                            throw new ApplicationException("RTPChannel was asked to send on the control socket but none exists.");
                        }
                        else
                        {
                            connection = m_controlConnection;
                        }
                    }
                    else
                    {
                        LastRtpDestination = dstEndPoint;
                    }

                    return connection.SendTo(dstEndPoint, buffer, memoryOwner);

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
                    logger.LogRtpChannelGeneralException(excp);
                    return SocketError.Fault;
                }
            }
        }

        /// <summary>
        /// Event handler for packets received on the RTP UDP socket.
        /// </summary>
        /// <param name="receiver">The UDP receiver the packet was received on.</param>
        /// <param name="localPort">The local port it was received on.</param>
        /// <param name="remoteEndPoint">The remote end point of the sender.</param>
        /// <param name="packet">The raw packet received (note this may not be RTP if other protocols are being multiplexed).</param>
        protected virtual void OnRTPPacketReceived(SocketConnection receiver, int localPort, IPEndPoint remoteEndPoint, ReadOnlyMemory<byte> packet)
        {
            if (!packet.IsEmpty)
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
        private void OnControlPacketReceived(SocketConnection receiver, int localPort, IPEndPoint remoteEndPoint, ReadOnlyMemory<byte> packet)
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
