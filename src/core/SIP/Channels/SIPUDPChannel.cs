//-----------------------------------------------------------------------------
// Filename: SIPUDPChannel.cs
//
// Description: SIP transport for UDP.
//
// Author(s):
// Aaron Clauson
//
// History:
// 17 Oct 2005	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
// 14 Oct 2019  Aaron Clauson   Added IPv6 support.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPUDPChannel : SIPChannel
    {
        private readonly Task m_mainLoop;

        // Channel sockets.
        private readonly UdpClient m_sipConn = null;

        /// <summary>
        /// Creates a SIP channel to listen for and send SIP messages over UDP.
        /// </summary>
        /// <param name="endPoint">The IP end point to listen on and send from.</param>
        public SIPUDPChannel(IPEndPoint endPoint)
        {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, endPoint);

            m_sipConn = new UdpClient(m_localSIPEndPoint.GetIPEndPoint());
            if (m_localSIPEndPoint.Port == 0)
            {
                m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, (IPEndPoint)m_sipConn.Client.LocalEndPoint);
            }
            logger.LogDebug("SIPUDPChannel listener created " + m_localSIPEndPoint.GetIPEndPoint() + ".");

            m_mainLoop = Task.Run(Listen);
        }

        public SIPUDPChannel(IPAddress listenAddress, int listenPort) : this(new IPEndPoint(listenAddress, listenPort))
        { }

        private async Task Listen()
        {
            logger.LogDebug("SIPUDPChannel socket on " + m_localSIPEndPoint.ToString() + " listening started.");

            while (!Closed)
            {
                try
                {
                    var receiveResult = await m_sipConn.ReceiveAsync();
                    if (receiveResult.Buffer?.Length > 0)
                    {
                        SIPMessageReceived?.Invoke(this, new SIPEndPoint(SIPProtocolsEnum.udp, receiveResult.RemoteEndPoint), receiveResult.Buffer);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // it's ok to be here after invoking Close()
                    break;
                }
                catch (SocketException)
                {
                    // ToDo. Pretty sure these exceptions get thrown when an ICMP message comes back indicating there is no listening
                    // socket on the other end. It would be nice to be able to relate that back to the socket that the data was sent to
                    // so that we know to stop sending.
                    //logger.LogWarning("SocketException SIPUDPChannel Receive (" + sockExcp.ErrorCode + "). " + sockExcp.Message);

                    //inEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
                    continue;
                }
                catch (Exception listenExcp)
                {
                    logger.LogError("Exception listening on SIPUDPChannel. " + listenExcp.Message);
                    continue;
                }
            }

            logger.LogDebug("SIPUDPChannel socket on " + m_localSIPEndPoint + " listening halted.");
        }

        public override void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer);
        }

        public override void Send(IPEndPoint destinationEndPoint, byte[] buffer)
        {
            try
            {
                if (destinationEndPoint == null)
                {
                    throw new ApplicationException("An empty destination was specified to Send in SIPUDPChannel.");
                }
                else
                {
                    m_sipConn.Send(buffer, buffer.Length, destinationEndPoint);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception (" + excp.GetType().ToString() + ") SIPUDPChannel Send (sendto=>" + IPSocket.GetSocketString(destinationEndPoint) + "). " + excp.Message);
                throw excp;
            }
        }

        public override async Task<SocketError> SendAsync(IPEndPoint dstEndPoint, byte[] buffer)
        {
            if (dstEndPoint == null)
            {
                throw new ArgumentException("dstEndPoint", "An empty destination was specified to Send in SIPUDPChannel.");
            }
            else if(buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in SIPUDPChannel.");
            }

            try
            {
                int bytesSent = await m_sipConn.SendAsync(buffer, buffer.Length, dstEndPoint);
                return (bytesSent > 0) ? SocketError.Success : SocketError.ConnectionReset;
            }
            catch(SocketException sockExcp)
            {
                return sockExcp.SocketErrorCode;
            }
        }

        /// <summary>
        /// This method is not implemented for the SIP UDP channel.
        /// </summary>
        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        /// <summary>
        /// This method is not implemented for the SIP UDP channel.
        /// </summary>
        public override Task<SocketError> SendAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        /// <summary>
        /// This method is not implemented for the SIP UDP channel.
        /// </summary>
        public override Task<SocketError> SendAsync(string connectionID, byte[] buffer)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        /// <summary>
        /// The UDP channel does not support connections. Always returns false.
        /// </summary>
        public override bool HasConnection(string connectionID)
        {
            return false;
        }

        /// <summary>
        /// The UDP channel does not support connections. Always returns false.
        /// </summary>
        public override bool HasConnection(IPEndPoint remoteEndPoint)
        {
            return false;
        }

        public override void Close()
        {
            try
            {
                logger.LogDebug("Closing SIP UDP Channel " + SIPChannelEndPoint + ".");

                Closed = true;
                m_sipConn.Close();
                m_mainLoop.GetAwaiter().GetResult();
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception SIPUDPChannel Close. " + excp.Message);
            }
        }

        public override void Dispose()
        {
            this.Close();
        }
    }
}
