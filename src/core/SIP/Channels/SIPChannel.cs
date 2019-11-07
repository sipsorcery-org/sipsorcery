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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// Represents a message received on a SIP channel prior to any attempt to identify
    /// whether it represents a SIP request, response or something else.
    /// </summary>
	public class IncomingMessage
	{
        /// <summary>
        /// The SIP channel we received the message on.
        /// </summary>
    	public SIPChannel LocalSIPChannel;

        /// <summary>
        /// The next hop remote SIP end point the message came from.
        /// </summary>
        public SIPEndPoint RemoteEndPoint;

        /// <summary>
        /// The message data.
        /// </summary>
		public byte[] Buffer;

        /// <summary>
        /// The time at which the message was received.
        /// </summary>
        public DateTime ReceivedAt;

        public IncomingMessage(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer)
		{
            LocalSIPChannel = sipChannel;
            RemoteEndPoint = remoteEndPoint;
			Buffer = buffer;
            ReceivedAt = DateTime.Now;

        }
	}

    /// <summary>
    /// The SIPChannel abstract class encapsulates the common properties and methods required of a SIP channel.
    /// A SIP channel's primary responsibility is sending and receiving messages from the network.
    /// </summary>
    public abstract class SIPChannel : IDisposable
    {
        protected ILogger logger = Log.Logger;

        protected SIPEndPoint m_localSIPEndPoint;

        /// <summary>
        /// The local SIP end point that the channel is listening on and sending from.
        /// </summary>
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

        protected bool m_isReliable;

        /// <summary>
        /// If the underlying transport channel is reliable, such as TCP, this will be set to true.
        /// </summary>
        public bool IsReliable
        {
            get { return m_isReliable; }
        }

        protected bool m_isSecure;

        /// <summary>
        /// If the underlying transport channel is using transport layer security (e.g. TLS or WSS) this will be set to true.
        /// </summary>
        public bool IsSecure {
            get { return m_isSecure; }
        }

        /// <summary>
        /// Returns true if the IP address the SIP channel is listening on is the IPv4 or IPv6 loopback address.
        /// </summary>
        public bool IsLoopbackAddress
        {
            get { return IPAddress.IsLoopback(m_localSIPEndPoint.Address); }
        }

        /// <summary>
        /// The type of SIP protocol (udp, tcp, tls or web socket) for this channel.
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

        /// <summary>
        /// Indicates whether close has been called on the SIP channel. Once closed a SIP channel can no longer be used
        /// to send or receive messages. It should generally only be called at the same time the SIP tranpsort class using it
        /// is shutdown.
        /// </summary>
        protected bool Closed;

        /// <summary>
        /// The function delegate that will be called whenever a new SIP message is received on the SIP channel.
        /// </summary>
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

        /// <summary>
        /// Sends a SIP message asynchronously on a specific stream connection.
        /// </summary>
        /// <param name="connectionID">The ID of the specific client connection that the messgae must be sent on.</param>
        /// <param name="buffer">The data to send.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public abstract Task<SocketError> SendAsync(string connectionID, byte[] buffer);

        /// <summary>
        /// Checks whether the SIP channel has a connection matching a unique connection ID.
        /// </summary>
        /// <param name="connectionID">The connection ID to check for a match on.</param>
        /// <returns>True if a match is found or false if not.</returns>
        public abstract bool HasConnection(string connectionID);

        /// <summary>
        /// Checks whether the SIP channel has an existing connection for a remote end point.
        /// Existing connections include connections that have been accepted by this channel's listener
        /// and connections that have been initiated due to sends from this channel.
        /// </summary>
        /// <param name="remoteEndPoint">The remote end point to check for an existing connection.</param>
        /// <returns>True if a match is found or false if not.</returns>
        public abstract bool HasConnection(IPEndPoint remoteEndPoint);

        /// <summary>
        /// Closes the SIP channel. Closing stops the SIP channel from receiving or sending and typically
        /// should only be done at the same time the parent SIP transport layer is shutdown.
        /// </summary>
        public abstract void Close();

        /// <summary>
        /// Calls close on the SIP channel when the object is disposed.
        /// </summary>
        public abstract void Dispose();
    }
}
