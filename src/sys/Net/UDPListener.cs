//-----------------------------------------------------------------------------
// Filename: UDPListener.cs
//
// Description: Generic listener for UDP based protocols.
// 
// History:
// 27 Feb 2012	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2012 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    public class UDPListener
    {
        private const string THREAD_NAME = "udplistener-";

        private static ILogger logger = Log.Logger;

        private IPEndPoint m_localEndPoint;
        private Guid m_socketId = Guid.NewGuid();
        private UdpClient m_udpClient;
        private bool m_closed;

        public Action<UDPListener, IPEndPoint, IPEndPoint, byte[]> PacketReceived;

        public UDPListener(IPEndPoint endPoint)
        {
            m_localEndPoint = endPoint;
            Initialise();
        }

        private void Initialise()
        {
            try
            {
                m_udpClient = new UdpClient(m_localEndPoint);

                Thread listenThread = new Thread(new ThreadStart(Listen));
                listenThread.Name = THREAD_NAME + Crypto.GetRandomString(4);
                listenThread.Start();

                logger.LogDebug("UDPListener listener created " + m_localEndPoint + ".");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UDPListener Initialise. " + excp.Message);
                throw excp;
            }
        }

        private void Dispose(bool disposing)
        {
            try
            {
                this.Close();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception Disposing UDPListener. " + excp.Message);
            }
        }

        private void Listen()
        {
            try
            {
                byte[] buffer = null;

                logger.LogDebug("UDPListener socket on " + m_localEndPoint + " listening started.");

                while (!m_closed)
                {
                    IPEndPoint inEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    try
                    {
                        buffer = m_udpClient.Receive(ref inEndPoint);
                    }
                    catch (SocketException)
                    {
                        // ToDo. Pretty sure these exceptions get thrown when an ICMP message comes back indicating there is no listening
                        // socket on the other end. It would be nice to be able to relate that back to the socket that the data was sent to
                        // so that we know to stop sending.
                        continue;
                    }
                    catch (Exception listenExcp)
                    {
                        // There is no point logging this as without processing the ICMP message it's not possible to know which socket the rejection came from.
                        logger.LogError("Exception listening on UDPListener. " + listenExcp.Message);

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
                        if (PacketReceived != null)
                        {
                            PacketReceived(this, m_localEndPoint, inEndPoint, buffer);
                        }
                    }
                }

                logger.LogDebug("UDPListener socket on " + m_localEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UDPListener Listen. " + excp.Message);
                //throw excp;
            }
        }

        public void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer);
        }

        public void Send(IPEndPoint destinationEndPoint, byte[] buffer)
        {
            Send(destinationEndPoint, buffer, buffer.Length);
        }

        public void Send(IPEndPoint destinationEndPoint, byte[] buffer, int length)
        {
            try
            {
                if (destinationEndPoint == null)
                {
                    throw new ApplicationException("An empty destination was specified to Send in SIPUDPChannel.");
                }
                else
                {
                    if (m_udpClient != null && m_udpClient.Client != null)
                    {
                        m_udpClient.Send(buffer, length, destinationEndPoint);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception (" + excp.GetType().ToString() + ") UDPListener Send (sendto=>" + IPSocket.GetSocketString(destinationEndPoint) + "). " + excp.Message);
                throw excp;
            }
        }

        public void Close()
        {
            try
            {
                logger.LogDebug("Closing UDPListener " + m_localEndPoint + ".");

                m_closed = true;
                m_udpClient.Close();
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception UDPListener Close. " + excp.Message);
            }
        }
    }
}
