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
	internal class IncomingMessage
	{
        /// <summary>
        /// The SIP channel we received the message on.
        /// </summary>
    	public SIPChannel LocalSIPChannel;

        /// <summary>
        /// The local end point that the message was received on. If a SIP channel
        /// is listening on IPAddress.Any then this property will hold the actual 
        /// IP address that was used for the receive.
        /// </summary>
        public SIPEndPoint LocalEndPoint;

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

        public IncomingMessage(SIPChannel sipChannel, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, byte[] buffer)
		{
            LocalSIPChannel = sipChannel;
            LocalEndPoint = localEndPoint;
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
        protected static int CHANNEL_ID_LENGTH = 3; // Length of the random numeric string to use for channel ID's.

        protected ILogger logger = Log.Logger;

        [Obsolete("Please use alternative DefaultSIPChannelEndPoint.", true)]
        public SIPEndPoint LocalSIPEndPoint;

        [Obsolete("Please use alternative GetDefaultContactURI.", true)]
        public string SIPChannelContactURI
        {
            get { return LocalSIPEndPoint.ToString(); }
        }

        /// <summary>
        /// A unique ID for the channel. Useful for ensuring a transmission can occur
        /// on a specific channel without having to match listening addresses.
        /// </summary>
        public string ID { get; protected set; }

        /// <summary>
        /// The list of IP addresses that this SIP channel is listening on. The only mechansim
        /// for a channel to have mutliple addresses is if it's socket address is set to 
        /// IPAddress.Any.
        /// </summary>
        public static List<IPAddress> LocalIPAddresses { get; private set; }

        /// <summary>
        /// The local IP address this machine uses to communicate with the Internet.
        /// </summary>
        public static IPAddress InternetDefaultAddress { get; private set;}

        /// <summary>
        /// The IP address the channel is listening on. Can be IPAddress.Any so cannot
        /// be used directly in SIP Headers, SIP URIs etc. Instead call GetContactURI and
        /// provdie the desintation address.
        /// </summary>
        public IPAddress ListeningIPAddress { get; protected set; }

        /// <summary>
        /// The port that this SIP channel is listening on.
        /// </summary>
        public int Port { get; protected set; }

        /// <summary>
        /// The IP end point this channel is listening on. Note it can contain
        /// IPAddress.Any which means it can match multiple IP addresses.
        /// </summary>
        public IPEndPoint ListeningEndPoint 
        {
            get { return new IPEndPoint(ListeningIPAddress, Port); }
        }

        /// <summary>
        /// The SIP end point this channel is listening on. Note it can contain
        /// IPAddress.Any which means it can match multiple IP addresses.
        /// </summary>
        public SIPEndPoint ListeningSIPEndPoint
        {
            get { return new SIPEndPoint(SIPProtocol, ListeningIPAddress, Port, ID, null); }
        }

        /// <summary>
        /// The default local SIP end point that the channel is listening on and sending from.
        /// A single SIP channel can potentially be listening on multiple IP addresses if
        /// IPAddress.Any is used. One of the addresses will be chosen as the default.
        /// </summary>
        public SIPEndPoint DefaultSIPChannelEndPoint
        {
            get { return new SIPEndPoint(SIPProtocol, ListeningIPAddress, Port, ID, null); }
        }

        /// <summary>
        /// If the underlying transport channel is reliable, such as TCP, this will be set to true.
        /// </summary>
        public bool IsReliable { get; protected set; } = false;

        /// <summary>
        /// If the underlying transport channel is using transport layer security (e.g. TLS or WSS) this will be set to true.
        /// </summary>
        public bool IsSecure { get; protected set; } = false;

        /// <summary>
        /// Returns true if the sole IP address the SIP channel is listening on is the IPv4 or IPv6 loopback address.
        /// </summary>
        public bool IsLoopbackAddress
        {
            get { return LocalIPAddresses.Count == 1 && IPAddress.IsLoopback(ListeningIPAddress); }
        }
           
        /// <summary>
        /// The type of SIP protocol (udp, tcp, tls or web socket) for this channel.
        /// </summary>
        public SIPProtocolsEnum SIPProtocol { get; protected set; }

        /// <summary>
        /// Whether the channel is IPv4 or IPv6.
        /// </summary>
        public AddressFamily AddressFamily
        {
            get { return ListeningIPAddress.AddressFamily; }
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

        static SIPChannel()
        {
            LocalIPAddresses = NetServices.GetAllLocalIPAddresses();

            // When using IPAddress.Any a default end point is still needed for placing in SIP headers and payloads.
            // Using 0.0.0.0 in SIP headers causes issues for some SIP software stacks.
            InternetDefaultAddress = NetServices.GetLocalAddressForInternet();
        }

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
        /// Gets the local IP address this SIP channel will use for communicating with the destination
        /// IP address.
        /// </summary>
        /// <param name="dst">The destination IP address.</param>
        /// <returns>The local IP address this channel selects to use for connecting to the destination.</returns>
        public IPAddress GetLocalIPAddressForDestination(IPAddress dst)
        {
            IPAddress localAddress = ListeningIPAddress;

            if (IPAddress.Any.Equals(ListeningIPAddress) || IPAddress.IPv6Any.Equals(ListeningIPAddress))
            {
                // This channel is listening on IPAddress.Any.
                localAddress = NetServices.GetLocalAddressForRemote(dst);
            }

            return localAddress;
        }

        /// <summary>
        /// Get the local SIPEndPoint this channel will use for communicating with the destination IP address,
        /// </summary>
        /// <param name="dst">The destination IP address.</param>
        /// <returns>The local SIP end points this channel selects to use for connecting to the destination.</returns>
        public SIPEndPoint GetLocalSIPEndPointForDestination(IPAddress dst)
        {
            IPAddress localAddress = GetLocalIPAddressForDestination(dst);
            return new SIPEndPoint(SIPProtocol, localAddress, Port, ID, null);
        }

        /// <summary>
        /// The default URI to be used for contacting this SIP channel.
        /// </summary>
        /// <param name="scheme">The SIP scheme to use for the Contact URI.</param>
        /// <param name="dst">The destination address the Contact URI is for. For a SIPChannel using
        /// IPAddress.Any the desintation needs to be known so it can select the correct local address.</param>
        public SIPURI GetContactURI(SIPSchemesEnum scheme, IPAddress dst)
        {
            return new SIPURI(scheme, GetLocalSIPEndPointForDestination(dst));
        }

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
