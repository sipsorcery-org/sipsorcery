//-----------------------------------------------------------------------------
// Filename: SIPUDPChannel.cs
//
// Description: SIP transport for UDP.
// 
// History:
// 17 Oct 2005	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
	public class SIPUDPChannel : SIPChannel
	{
        private const string THREAD_NAME = "sipchanneludp-";

        //private ILog logger = AssemblyState.logger;

		// Channel sockets.
        private Guid m_socketId = Guid.NewGuid();
		private UdpClient m_sipConn = null;
		//private bool m_closed = false;

        public SIPUDPChannel(IPEndPoint endPoint) {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, endPoint);
            Initialise();
        }

        private void Initialise() {
            try {
                m_sipConn = new UdpClient(m_localSIPEndPoint.GetIPEndPoint());

                Thread listenThread = new Thread(new ThreadStart(Listen));
                listenThread.Name = THREAD_NAME + Crypto.GetRandomString(4);
                listenThread.Start();

                logger.Debug("SIPUDPChannel listener created " + m_localSIPEndPoint.GetIPEndPoint() + ".");
            }
            catch (Exception excp) {
                logger.Error("Exception SIPUDPChannel Initialise. " + excp.Message);
                throw excp;
            }
        }

        private void Dispose(bool disposing) {
            try {
                this.Close();
            }
            catch (Exception excp) {
                logger.Error("Exception Disposing SIPUDPChannel. " + excp.Message);
            }
        }
			
		private void Listen()
		{
			try
			{
				byte[] buffer = null;

                logger.Debug("SIPUDPChannel socket on " + m_localSIPEndPoint.ToString() + " listening started.");

				while(!Closed)
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
                        //logger.Warn("SocketException SIPUDPChannel Receive (" + sockExcp.ErrorCode + "). " + sockExcp.Message);

                        //inEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
                        continue;
                    }
                    catch(Exception listenExcp)
                    {
                        // There is no point logging this as without processing the ICMP message it's not possible to know which socket the rejection came from.
                        logger.Error("Exception listening on SIPUDPChannel. " + listenExcp.Message);

                        inEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        continue;
                    }

					if(buffer == null || buffer.Length == 0)
					{
                        // No need to care about zero byte packets.
                        //string remoteEndPoint = (inEndPoint != null) ? inEndPoint.ToString() : "could not determine";
                        //logger.Error("Zero bytes received on SIPUDPChannel " + m_localSIPEndPoint.ToString() + ".");
					}
					else
					{
						if(SIPMessageReceived != null)
						{
                            SIPMessageReceived(this, new SIPEndPoint(SIPProtocolsEnum.udp, inEndPoint), buffer);
						}
					}
				}

                logger.Debug("SIPUDPChannel socket on " + m_localSIPEndPoint + " listening halted.");
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPUDPChannel Listen. " + excp.Message);
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
                logger.Error("Exception (" + excp.GetType().ToString() + ") SIPUDPChannel Send (sendto=>" + IPSocket.GetSocketString(destinationEndPoint) + "). " + excp.Message);
                throw excp;
            }
		}

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            throw new ApplicationException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        public override bool IsConnectionEstablished(IPEndPoint remoteEndPoint)
        {
            throw new NotSupportedException("The SIP UDP channel does not support connections.");
        }

        protected override Dictionary<string, SIPConnection> GetConnectionsList()
        {
            throw new NotSupportedException("The SIP UDP channel does not support connections.");
        }

		public override void Close()
		{
			try
			{
				logger.Debug("Closing SIP UDP Channel " + SIPChannelEndPoint + ".");
				
				Closed = true;
				m_sipConn.Close();
			}
			catch(Exception excp)
			{
				logger.Warn("Exception SIPUDPChannel Close. " +excp.Message);
			}
		}
	}
}
