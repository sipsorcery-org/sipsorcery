//-----------------------------------------------------------------------------
// Filename: SIPChannel.cs
//
// Description: Generic items for SIP channels.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 19 Apr 2008	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
//                              (split from original SIPUDPChannel).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

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

    public abstract class SIPChannel : IDisposable
    {
        private const int INITIALPRUNE_CONNECTIONS_DELAY = 60000;   // Wait this long before starting the prune checks, there will be no connections to prune initially and the CPU is needed elsewhere.
        private const int PRUNE_CONNECTIONS_INTERVAL = 60000;        // The period at which to prune the connections.
        private const int PRUNE_NOTRANSMISSION_MINUTES = 70;         // The number of minutes after which if no transmissions are sent or received a connection will be pruned.

        protected ILogger logger = Log.Logger;

        public static List<string> LocalTCPSockets = new List<string>(); // Keeps a list of TCP sockets this process is listening on to prevent it establishing TCP connections to itself.

        protected SIPEndPoint m_localSIPEndPoint = null;

        //public event EventHandler SendComplete;

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

        /// <summary>
        /// Returns true if the IP address the SIP channel is listening on is the IPv4 or IPv6 loopback address.
        /// </summary>
        public bool IsLoopbackAddress
        {
            get { return IPAddress.IsLoopback(m_localSIPEndPoint.Address); }
        }

        /// <summary>
        /// The type of SIP protocol (udp, tcp or tls) for this channel.
        /// </summary>
        public SIPProtocolsEnum SIPProtocol
        {
            get { return m_localSIPEndPoint.Protocol; }
        }

        /// <summary>
        /// Whether the channel is IPv4 or IPv6.
        /// </summary>
        public AddressFamily AddressFamily
        {
            get { return m_localSIPEndPoint.Address.AddressFamily; }
        }

        protected bool Closed;

        public SIPMessageReceivedDelegate SIPMessageReceived;

        /// <summary>
        /// Send a SIP message, represented as a string, to a remote end point.
        /// </summary>
        /// <param name="destinationEndPoint">The remote end point to send the message to.</param>
        /// <param name="message">The message to send.</param>
        public abstract void Send(IPEndPoint destinationEndPoint, string message);
        public abstract void Send(IPEndPoint destinationEndPoint, byte[] buffer);
        public abstract void Send(IPEndPoint destinationEndPoint, byte[] buffer, string serverCertificateName);

        /// <summary>
        /// Asynchronous SIP message send to a remote end point.
        /// </summary>
        /// <param name="destinationEndPoint">The remote end point to send the message to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public abstract Task<SocketError> SendAsync(IPEndPoint destinationEndPoint, byte[] buffer);

        /// <summary>
        /// Asynchronous SIP message send to a remote end point.
        /// </summary>
        /// <param name="destinationEndPoint">The remote end point to send the message to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <param name="serverCertificateName">If the send is over SSL the required common name of the server's X509 certificate.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public abstract Task<SocketError> SendAsync(IPEndPoint destinationEndPoint, byte[] buffer, string serverCertificateName);

        public abstract void Close();
        public abstract bool IsConnectionEstablished(IPEndPoint remoteEndPoint);
        protected abstract Dictionary<string, SIPStreamConnection> GetConnectionsList();

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
                            SIPStreamConnection inactiveConnection = null;
                            Dictionary<string, SIPStreamConnection> connections = GetConnectionsList();

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
                                logger.LogDebug($"Pruning inactive connection on {SIPChannelContactURI}to remote end point {inactiveConnection.RemoteEndPoint}.");
                                inactiveConnection.StreamSocket.Close();
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
                            logger.LogError("Exception PruneConnections (pruning). " + pruneExcp.Message);
                            checkComplete = true;
                        }
                    }

                    Thread.Sleep(PRUNE_CONNECTIONS_INTERVAL);
                    checkComplete = false;
                }

                logger.LogDebug("SIPChannel socket on " + m_localSIPEndPoint.ToString() + " pruning connections halted.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPChannel PruneConnections. " + excp.Message);
            }
        }

        public abstract void Dispose();
    }
}
