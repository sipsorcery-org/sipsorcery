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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPUDPChannel : SIPChannel
    {
        private const string THREAD_NAME = "sipchanneludp-";

        // Channel sockets.
        private UdpClient m_sipConn = null;

        /// <summary>
        /// Creates a SIP channel to listen for and send SIP messages over UDP.
        /// </summary>
        /// <param name="endPoint">The IP end point to listen on and send from.</param>
        public SIPUDPChannel(IPEndPoint endPoint)
        {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, endPoint);
            Initialise();
        }

        public SIPUDPChannel(IPAddress listenAddress, int listenPort) : this(new IPEndPoint(listenAddress, listenPort))
        { }

        /// <summary>
        /// Starts the UDP listener.
        /// </summary>
        private void Initialise()
        {
            try
            {
                m_sipConn = new UdpClient(m_localSIPEndPoint.GetIPEndPoint());
                // TODO 26 Oct 2019: Look into why UDP sockets don't allow dual mode to be set.
                //if (m_localSIPEndPoint.GetIPEndPoint().AddressFamily == AddressFamily.InterNetworkV6) m_sipConn.Client.DualMode = true;

                if (m_localSIPEndPoint.Port == 0)
                {
                    m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, (IPEndPoint)m_sipConn.Client.LocalEndPoint);
                }

                Thread listenThread = new Thread(new ThreadStart(Listen));
                listenThread.Name = THREAD_NAME + Crypto.GetRandomString(4);
                listenThread.Start();

                logger.LogDebug($"SIPUDPChannel listener created {m_localSIPEndPoint.GetIPEndPoint()}.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPUDPChannel Initialise. " + excp.Message);
                throw excp;
            }
        }

        private void Listen()
        {
            try
            {
                byte[] buffer = null;

                logger.LogDebug("SIPUDPChannel socket on " + m_localSIPEndPoint.ToString() + " listening started.");

                while (!Closed)
                {
                    IPEndPoint inEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    try
                    {
                        buffer = m_sipConn.Receive(ref inEndPoint);
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
                        inEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        continue;
                    }

                    if (buffer == null || buffer.Length == 0)
                    {
                        // No need to care about zero byte packets.
                        //string remoteEndPoint = (inEndPoint != null) ? inEndPoint.ToString() : "could not determine";
                        //logger.LogError("Zero bytes received on SIPUDPChannel " + m_localSIPEndPoint.ToString() + ".");
                    }
                    else
                    {
                        SIPMessageReceived?.Invoke(this, new SIPEndPoint(SIPProtocolsEnum.udp, inEndPoint), buffer);
                    }
                }

                logger.LogDebug("SIPUDPChannel socket on " + m_localSIPEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPUDPChannel Listen. " + excp.Message);
                //throw excp;
            }
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

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        public override Task<SocketError> SendAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        public override bool IsConnectionEstablished(IPEndPoint remoteEndPoint)
        {
            throw new NotSupportedException("The SIP UDP channel does not support connections.");
        }

        protected override Dictionary<string, SIPStreamConnection> GetConnectionsList()
        {
            throw new NotSupportedException("The SIP UDP channel does not support connections.");
        }

        public override void Close()
        {
            try
            {
                logger.LogDebug("Closing SIP UDP Channel " + SIPChannelEndPoint + ".");

                Closed = true;
                m_sipConn.Close();
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
