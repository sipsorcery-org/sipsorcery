//-----------------------------------------------------------------------------
// Filename: SIPChannel.cs
//
// Description: Generic items for SIP channels.
// 
// History:
// 19 Apr 2008	Aaron Clauson	Created (split from original SIPUDPChannel).
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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
	public class IncomingMessage
	{
    	public SIPChannel LocalSIPChannel;
        public SIPEndPoint RemoteEndPoint;
		public byte[] Buffer;
        public DateTime ReceivedAt;

        public IncomingMessage(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer)
		{
            LocalSIPChannel = sipChannel;
            RemoteEndPoint = remoteEndPoint;
			Buffer = buffer;
            ReceivedAt = DateTime.Now;
		}
	}

    public abstract class SIPChannel
    {
        private const int INITIALPRUNE_CONNECTIONS_DELAY = 60000;   // Wait this long before starting the prune checks, there will be no connections to prune initially and the CPU is needed elsewhere.
        private const int PRUNE_CONNECTIONS_INTERVAL = 60000;        // The period at which to prune the connections.
        private const int PRUNE_NOTRANSMISSION_MINUTES = 70;         // The number of minutes after which if no transmissions are sent or received a connection will be pruned.

        protected ILog logger = AssemblyState.logger;

        public static List<string> LocalTCPSockets = new List<string>(); // Keeps a list of TCP sockets this process is listening on to prevent it establishing TCP connections to itself.

        protected SIPEndPoint m_localSIPEndPoint = null;

        public event EventHandler SendComplete;
        public SIPEndPoint SIPChannelEndPoint
        {
            get { return m_localSIPEndPoint; }
        }

        /// <summary>
        /// This is the URI to be used for contacting this SIP channel.
        /// </summary>
        public string SIPChannelContactURI
        {
            get { return m_localSIPEndPoint.ToString(); }
        }

        protected bool m_isReliable;    //If the underlying transport channel is reliable, such as TCP, this will be set to true;
        public bool IsReliable
        {
            get { return m_isReliable; }
        }

        protected bool m_isTLS;
        public bool IsTLS {
            get { return m_isTLS; }
        }

        protected bool Closed;

        public SIPMessageReceivedDelegate SIPMessageReceived;

        public abstract void Send(IPEndPoint destinationEndPoint, string message);
        public abstract void Send(IPEndPoint destinationEndPoint, byte[] buffer);
        public abstract void Send(IPEndPoint destinationEndPoint, byte[] buffer, string serverCertificateName);
        public abstract void Close();
        public abstract bool IsConnectionEstablished(IPEndPoint remoteEndPoint);
        protected abstract Dictionary<string, SIPConnection> GetConnectionsList();

        /// <summary>
        /// Periodically checks the established connections and closes any that have not had a transmission for a specified 
        /// period or where the number of connections allowed per IP address has been exceeded. Only relevant for connection
        /// oriented channels such as TCP and TLS.
        /// </summary>
        protected void PruneConnections(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                Thread.Sleep(INITIALPRUNE_CONNECTIONS_DELAY);

                while (!Closed)
                {
                    bool checkComplete = false;

                    while (!checkComplete)
                    {
                        try
                        {
                            SIPConnection inactiveConnection = null;
                            Dictionary<string, SIPConnection> connections = GetConnectionsList();

                            lock (connections)
                            {
                                var inactiveConnectionKey = (from connection in connections
                                                             where connection.Value.LastTransmission < DateTime.Now.AddMinutes(PRUNE_NOTRANSMISSION_MINUTES * -1)
                                                             select connection.Key).FirstOrDefault();

                                if (inactiveConnectionKey != null)
                                {
                                    inactiveConnection = connections[inactiveConnectionKey];
                                    connections.Remove(inactiveConnectionKey);
                                }
                            }

                            if (inactiveConnection != null)
                            {
                                logger.Debug("Pruning inactive connection on " + SIPChannelContactURI + " to remote end point " + inactiveConnection.RemoteEndPoint.ToString() + ".");
                                inactiveConnection.Close();
                            }
                            else
                            {
                                checkComplete = true;
                            }
                        }
                        catch (SocketException)
                        {
                            // Will be thrown if the socket is already closed.
                        }
                        catch (Exception pruneExcp)
                        {
                            logger.Error("Exception PruneConnections (pruning). " + pruneExcp.Message);
                            checkComplete = true;
                        }
                    }

                    Thread.Sleep(PRUNE_CONNECTIONS_INTERVAL);
                    checkComplete = false;
                }

                logger.Debug("SIPChannel socket on " + m_localSIPEndPoint.ToString() + " pruning connections halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPChannel PruneConnections. " + excp.Message);
            }
        }

        protected virtual void OnSendComplete(EventArgs args)
        {
            SendComplete?.Invoke(this, args);
        }
    }
}
