﻿//-----------------------------------------------------------------------------
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

        /// <summary>
        /// int localPort, IPEndPoint remoteEndPoint, byte[] packet.
        /// </summary>
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

                logger.LogDebug("RTPChannel for {LocalEndPoint} started.", RtpSocket.LocalEndPoint);

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
                        logger.LogDebug("RTPChannel closing, RTP receiver on port {RTPPort}. Reason: {closeReason}.", RTPPort, closeReason);
                    }
                    else
                    {
                        logger.LogDebug("RTPChannel closing, RTP receiver on port {RTPPort}, Control receiver on port {ControlPort}. Reason: {closeReason}.", RTPPort, ControlPort, closeReason);
                    }

                    m_isClosed = true;
                    m_rtpReceiver?.Close(null);
                    m_controlReceiver?.Close(null);

                    OnClosed?.Invoke(closeReason);
                }
                catch (Exception excp)
                {
                    logger.LogError(excp, "Exception RTPChannel.Close. {ErrorMessage}", excp);
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
        public virtual SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
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
                logger.LogWarning("The destination address for Send in RTPChannel cannot be {Address}.", dstEndPoint.Address);
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

                    //Fix ReceiveFrom logic if any previous exception happens
                    if (!m_rtpReceiver.IsRunningReceive && !m_rtpReceiver.IsClosed)
                    {
                        m_rtpReceiver.BeginReceiveFrom();
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
                    logger.LogError(excp, "Exception RTPChannel.Send. {ErrorMesssage}", excp.Message);
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
                logger.LogWarning(sockExcp, "SocketException RTPChannel EndSendTo ({SocketErrorCode}). {Message}", sockExcp.ErrorCode, sockExcp.Message);
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception RTPChannel EndSendTo. {Message}", excp.Message);
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
