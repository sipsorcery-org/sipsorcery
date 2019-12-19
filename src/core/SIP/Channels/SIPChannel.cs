//-----------------------------------------------------------------------------
// Filename: SIPChannel.cs
//
// Description: Generic items for SIP channels.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 19 Apr 2008	Aaron Clauson	Created (split from original SIPUDPChannel), Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

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
        protected static int CHANNEL_ID_LENGTH = 3;         // Length of the random numeric string to use for channel ID's.
        private static int CREATE_CHANNELID_ATTEMPTS = 10; // Number of attempts to make at creating a random channel ID.

        private static ConcurrentDictionary<int, int> _inUseChannelIDs = new ConcurrentDictionary<int, int>(); // Make sure we don't create dulpicate channel ID's.

        protected ILogger logger = Log.Logger;

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
        public static IPAddress InternetDefaultAddress { get; private set; }

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
        /// If the underlying transport channel is reliable, such as TCP, this will be set to true.
        /// </summary>
        public bool IsReliable { get; protected set; } = false;

        /// <summary>
        /// If the underlying transport channel is using transport layer security (e.g. TLS or WSS) this will be set to true.
        /// </summary>
        public bool IsSecure { get; protected set; } = false;

        /// <summary>
        /// The type of SIP protocol (udp, tcp, tls or web socket) for this channel.
        /// </summary>
        public SIPProtocolsEnum SIPProtocol { get; protected set; }

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

        public SIPChannel()
        {
            for (int i = 0; i < CREATE_CHANNELID_ATTEMPTS; i++)
            {
                int id = Crypto.GetRandomInt(CHANNEL_ID_LENGTH);
                if (!_inUseChannelIDs.ContainsKey(id))
                {
                    _inUseChannelIDs.TryAdd(id, 0);
                    ID = id.ToString();
                    break;
                }
            }

            if (ID == null)
            {
                throw new ApplicationException("Failed to create a random channel ID in SIPChannel constructor.");
            }
        }

        /// <summary>
        /// Checks whether the host string corresponds to a socket address that this SIP channel is listening on.
        /// </summary>
        /// <param name="host">The host string to check.</param>
        /// <returns>True if the host is a socket this channel is listening on. False if not.</returns>
        internal bool IsChannelSocket(string host)
        {
            if (IPSocket.TryParseIPEndPoint(host, out var ep))
            {
                if (ListeningIPAddress != IPAddress.Any)
                {
                    return ep.Address.Equals(ListeningIPAddress) && ep.Port == ListeningEndPoint.Port;
                }
                else
                {
                    return ep.Port == ListeningEndPoint.Port && LocalIPAddresses.Any(x => x.Equals(ep.Address));
                }
            }

            return false;
        }

        /// <summary>
        /// Synchronous wrapper for <see cref="SendAsync"/>
        /// </summary>
        public abstract void Send(SIPEndPoint destinationEndPoint, byte[] buffer, string connectionIDHint = null);

        /// <summary>
        /// Synchronous wrapper for <see cref="SendSecureAsync"/>
        /// </summary>
        //public abstract void SendSecure(IPEndPoint destinationEndPoint, byte[] buffer, string serverCertificateName, string connectionIDHint = null);

        /// <summary>
        /// Asynchronous SIP message send to a remote end point.
        /// </summary>
        /// <param name="dstEndPoint">The remote end point to send the message to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <param name="connectionID">Optional ID of the specific client connection that the message should be sent on. It's only
        /// a hint so if the connection has been closed a new one will be attempted.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public abstract Task<SocketError> SendAsync(SIPEndPoint dstEndPoint, byte[] buffer, string connectionIDHint = null);

        /// <summary>
        /// Asynchronous SIP message send over a secure TLS connetion to a remote end point.
        /// </summary>
        /// <param name="dstEndPoint">The remote end point to send the message to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <param name="serverCertificateName">If the send is over SSL the required common name of the server's X509 certificate.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public abstract Task<SocketError> SendSecureAsync(SIPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName, string connectionIDHint = null);

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
        public abstract bool HasConnection(SIPEndPoint remoteEndPoint);

        /// <summary>
        /// Checks whether a web socket based SIP channel has an existing connection to a server URI.
        /// </summary>
        /// <param name="serverUri">The remote server URI to check for an existing connection.</param>
        /// <returns>True if a match is found or false if not.</returns>
        public abstract bool HasConnection(Uri serverUri);

        /// <summary>
        /// Returns true if the channel supports the requested address family.
        /// </summary>
        public abstract bool IsAddressFamilySupported(AddressFamily addresFamily);

        /// <summary>
        /// Returns true if the channel supports the requested transport layer protocol.
        /// </summary>
        public abstract bool IsProtocolSupported(SIPProtocolsEnum protocol);

        /// <summary>
        /// Gets the local IP address this SIP channel will use for communicating with the destination
        /// IP address.
        /// </summary>
        /// <param name="dst">The destination IP address.</param>
        /// <returns>The local IP address this channel selects to use for connecting to the destination.</returns>
        private IPAddress GetLocalIPAddressForDestination(IPAddress dst)
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
        internal SIPEndPoint GetLocalSIPEndPointForDestination(IPAddress dst)
        {
            IPAddress localAddress = GetLocalIPAddressForDestination(dst);
            return new SIPEndPoint(SIPProtocol, localAddress, Port, ID, null);
        }

        /// <summary>
        /// The contact SIP URI to be used for contacting this SIP channel WHEN sending to the destination IP address.
        /// The contact URI can change based on the destination. For example if the SIP channel is listening on IPAddress.Any
        /// a destination address of 127.0.0.1 will result in a contact of sip:127.0.0.1:X. Using the same channel to
        /// send to a desintation address on the internet will result in a different URI.
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

        #region Obsolete methods.

        [Obsolete("Please use alternative GetLocalSIPEndPointForDestination, it takes into account the dst which is important for IPAddress.Any listeners.", true)]
        public SIPEndPoint LocalSIPEndPoint;

        [Obsolete("Please use alternative GetContactURI, it takes into account the dst which is important for IPAddress.Any listeners.", true)]
        public string SIPChannelContactURI
        {
            get { return LocalSIPEndPoint.ToString(); }
        }

        [Obsolete("Please use alternative GetLocalSIPEndPointForDestination, it takes into account the dst which is important for IPAddress.Any listeners.", true)]
        public SIPEndPoint DefaultSIPChannelEndPoint
        {
            get { return new SIPEndPoint(SIPProtocol, ListeningIPAddress, Port, ID, null); }
        }

        #endregion.
    }
}
