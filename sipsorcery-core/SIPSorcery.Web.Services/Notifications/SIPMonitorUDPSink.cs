//-----------------------------------------------------------------------------
// Filename: SIPMonitorUDPSink.cs
//
// Description: A sink for SIP monitor events that sets up and manages event sessions using
// a WCF channel but receives the event data on a UDP socket.
// 
// History:
// 11 Mar 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// A sink for SIP monitor events that sets up and manages event sessions using a WCF channel but 
    /// receives the event data on a UDP socket.
    /// </summary>
    public class SIPMonitorUDPSink : ISIPMonitorPublisher
    {
        public const string UDP_LISTENER_THREAD_NAME = "sipmonitorudpsink-listen";

        private static ILog logger = AppState.logger;

        private MonitorProxyManager m_monitorProxyManager;
        private IPEndPoint m_eventReceiverEndPoint;

        public bool Exit = false;

        public event Action<string> NotificationReady;  // Not used.
        public event Func<SIPMonitorEvent, bool> MonitorEventReady;

        public SIPMonitorUDPSink(string udpSocket)
        {
            m_monitorProxyManager = new MonitorProxyManager();
            m_eventReceiverEndPoint = IPSocket.ParseSocketString(udpSocket);

            ThreadPool.QueueUserWorkItem(delegate { Listen(); });
        }

        private void Listen()
        {
            try
            {
                Thread.CurrentThread.Name = UDP_LISTENER_THREAD_NAME;

                UdpClient udpClient = new UdpClient(m_eventReceiverEndPoint);

                byte[] buffer = null;

                logger.Debug("SIPMonitorUDPSink socket on " + m_eventReceiverEndPoint.ToString() + " listening started.");

                while (!Exit)
                {
                    SIPEndPoint inEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));

                    try
                    {
                        buffer = udpClient.Receive(ref inEndPoint.SocketEndPoint);
                    }
                    catch (SocketException)
                    {
                        continue;
                    }
                    catch (Exception listenExcp)
                    {
                        // There is no point logging this as without processing the ICMP message it's not possible to know which socket the rejection came from.
                        logger.Error("Exception listening on SIPMonitorUDPSink. " + listenExcp.Message);

                        inEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
                        continue;
                    }

                    if (buffer != null && buffer.Length > 0)
                    {
                        if (MonitorEventReady != null)
                        {
                            SIPMonitorEvent monitorEvent = SIPMonitorEvent.ParseEventCSV(Encoding.UTF8.GetString(buffer));
                            MonitorEventReady(monitorEvent);
                        }
                    }
                }

                logger.Debug("SIPMonitorUDPSink socket on " + m_eventReceiverEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorUDPSink Listen. " + excp.Message);
                //throw excp;
            }
        }

        public bool IsAlive()
        {
            return true;
        }

        public string Subscribe(string customerUsername, string adminId, string address, string sessionID, string subject, string filter, int expiry, string udpSocket, out string subscribeError)
        {
            subscribeError = null;
            return m_monitorProxyManager.Subscribe(customerUsername, adminId, address, sessionID, subject, filter, expiry, m_eventReceiverEndPoint.ToString(), out subscribeError);
        }

        public List<string> GetNotifications(string address, out string sessionID, out string sessionError)
        {
            sessionID = null;
            sessionError = null;
            return null;
        }

        public bool IsNotificationReady(string address)
        {
            return false;
        }

        public string ExtendSession(string address, string sessionID, int expiry)
        {
            return m_monitorProxyManager.ExtendSession(address, sessionID, expiry);
        }

        public void CloseSession(string address, string sessionID)
        {
            m_monitorProxyManager.CloseSession(address, sessionID);
        }

        public void CloseConnection(string address)
        {
            m_monitorProxyManager.CloseConnection(address);   
        }

        public void MonitorEventReceived(SIPMonitorEvent monitorEvent)
        {
            throw new NotImplementedException();
        }
    }
}
