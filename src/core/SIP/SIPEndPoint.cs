﻿//-----------------------------------------------------------------------------
// Filename: SIPEndPoint.cs
//
// Description: Represents what needs to be known about a SIP end point for 
// network communications.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 Oct 2019	Aaron Clauson	Added missing header.
// 07 Nov 2019  Aaron Clauson   Added ConnectionID property.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// This class is a more specific version of the SIPURI class BUT is only concerned with the network and
    /// transport properties. It contains all the information needed to determine the remote end point to
    /// deliver a SIP request or response to.
    /// 
    /// This class must remain immutable otherwise the SIP stack can develop problems. SIP end points can get
    /// passed amongst different servers for logging and forwarding SIP messages and a modification of the end point
    /// by one server can result in a problem for a different server. Instead a new SIP end point should be created
    /// wherever a modification is required.
    /// </summary>
    public class SIPEndPoint
    {
        private const string CHANNELID_ATTRIBUTE_NAME = "cid";
        private const string CONNECTIONID_ATTRIBUTE_NAME = "xid";

        public static SIPEndPoint Empty { get; } = new SIPEndPoint();

        /// <summary>
        /// The transport/application layer protocol the SIP end point is using.
        /// </summary>
        public SIPProtocolsEnum Protocol { get; private set; } = SIPProtocolsEnum.udp;

        /// <summary>
        /// The network address for the SIP end point. IPv4 and IPv6 are supported.
        /// </summary>
        public IPAddress Address { get; private set; }

        /// <summary>
        /// The network port for the SIP end point.
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// For connection oriented transport protocols such as TCP, TLS and WebSockets this
        /// ID can record the unique connection a SIP message was received on. This makes it 
        /// possible to ensure responses or subsequent request can re-use the same connection.
        /// </summary>
        public string ConnectionID { get; set; }

        /// <summary>
        /// If set represents the SIP channel ID that this SIP end point was created from.
        /// </summary>
        public string ChannelID { get; set; }

        private SIPEndPoint() { }

        /// <summary>
        /// Instantiates a new SIP end point from a network end point. Non specified properties
        /// will be set to their defaults.
        /// </summary>
        public SIPEndPoint(IPEndPoint endPoint) :
            this(SIPProtocolsEnum.udp, endPoint.Address, endPoint.Port, null, null)
        { }

        /// <summary>
        /// Instantiates a new SIP end point from a network end point. Non specified properties
        /// will be set to their defaults.
        /// </summary>
        public SIPEndPoint(SIPProtocolsEnum protocol, IPAddress address, int port) :
            this(protocol, address, port, null, null)
        { }

        public SIPEndPoint(SIPProtocolsEnum protocol, IPEndPoint endPoint) :
            this(protocol, endPoint, null, null)
        { }

        public SIPEndPoint(SIPProtocolsEnum protocol, IPEndPoint endPoint, string channelID, string connectionID) :
            this(protocol, endPoint.Address, endPoint.Port, channelID, connectionID)
        { }

        /// <summary>
        /// Instantiates a new SIP end point.
        /// </summary>
        /// <param name="protocol">The SIP transport/application protocol used for the transmission.</param>
        /// <param name="address">The network address.</param>
        /// <param name="port">The network port.</param>
        /// <param name="channelID">Optional. The unique ID of the channel that created the end point.</param>
        /// <param name="connectionID">Optional. For connection oriented protocols the unique ID of the connection.
        /// For connectionless protocols should be set to null.</param>
        public SIPEndPoint(SIPProtocolsEnum protocol, IPAddress address, int port, string channelID, string connectionID)
        {
            Protocol = protocol;
            Address = address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;
            Port = (port == 0) ? SIPConstants.GetDefaultPort(Protocol) : port;
            ChannelID = channelID;
            ConnectionID = connectionID;
        }

        public SIPEndPoint(SIPURI sipURI)
        {
            Protocol = sipURI.Protocol;

            if (!IPSocket.TryParseIPEndPoint(sipURI.Host, out var endPoint))
            {
                throw new ApplicationException($"Could not parse SIPURI host {sipURI.Host} as an IP end point.");
            }

            Address = endPoint.Address?.IsIPv4MappedToIPv6 == true ? endPoint.Address.MapToIPv4() : endPoint.Address;
            Port = (endPoint.Port == 0) ? SIPConstants.GetDefaultPort(Protocol) : endPoint.Port;
        }

        /// <summary>
        /// Parses a SIP end point from either a serialised SIP end point string, format of:
        /// (udp|tcp|tls|ws|wss):(IPEndpoint)[;connid=abcd]
        /// or from a string that represents a SIP URI.
        /// </summary>
        /// <param name="sipEndPointStr">The string to parse to extract the SIP end point.</param>
        /// <returns>If successful a SIPEndPoint object or null otherwise.</returns>
        public static SIPEndPoint ParseSIPEndPoint(string sipEndPointStr)
        {
            if (sipEndPointStr.IsNullOrBlank())
            {
                return null;
            }

            if (sipEndPointStr.ToLower().StartsWith("udp:") ||
                sipEndPointStr.ToLower().StartsWith("tcp:") ||
                sipEndPointStr.ToLower().StartsWith("tls:") ||
                sipEndPointStr.ToLower().StartsWith("ws:") ||
                sipEndPointStr.ToLower().StartsWith("wss:"))
            {
                return ParseSerialisedSIPEndPoint(sipEndPointStr);
            }
            else
            {
                var sipUri = SIPURI.ParseSIPURIRelaxed(sipEndPointStr);
                var sipEndPoint = sipUri.ToSIPEndPoint();
                if (sipEndPoint != null)
                {
                    //sipEndPoint.Scheme = sipUri.Scheme;
                    return sipEndPoint;
                }
                else
                {
                    return null;
                }
            }
        }

        public static SIPEndPoint TryParse(string sipEndPointStr)
        {
            try
            {
                return ParseSIPEndPoint(sipEndPointStr);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reverses The SIPEndPoint.ToString() method. 
        /// </summary>
        /// <param name="serialisedSIPEndPoint">The serialised SIP end point MUST be in the form protocol:socket[;connid=abcd].
        /// Valid examples are udp:10.0.0.1:5060 and ws:10.0.0.1:5060;connid=abcd. An invalid example is 10.0.0.1:5060.</param>
        private static SIPEndPoint ParseSerialisedSIPEndPoint(string serialisedSIPEndPoint)
        {
            string channelID = null;
            string connectionID = null;
            string endPointStr = null;
            string protcolStr = serialisedSIPEndPoint.Substring(0, serialisedSIPEndPoint.IndexOf(':'));

            if (serialisedSIPEndPoint.Contains(";"))
            {
                endPointStr = serialisedSIPEndPoint.Slice(':', ';');
                var paramsStr = serialisedSIPEndPoint.Substring(serialisedSIPEndPoint.IndexOf(';') + 1)?.Trim();

                var endPointParams = new SIPParameters(paramsStr, ';');

                if (endPointParams.Has(CHANNELID_ATTRIBUTE_NAME))
                {
                    channelID = endPointParams.Get(CHANNELID_ATTRIBUTE_NAME);
                }

                if (endPointParams.Has(CONNECTIONID_ATTRIBUTE_NAME))
                {
                    connectionID = endPointParams.Get(CONNECTIONID_ATTRIBUTE_NAME);
                }
            }
            else
            {
                endPointStr = serialisedSIPEndPoint.Substring(serialisedSIPEndPoint.IndexOf(':') + 1);
            }

            if (!IPSocket.TryParseIPEndPoint(endPointStr, out var endPoint))
            {
                throw new ApplicationException($"Could not parse SIPEndPoint host {endPointStr} as an IP end point.");
            }

            return new SIPEndPoint(SIPProtocolsType.GetProtocolType(protcolStr), endPoint, channelID, connectionID);
        }

        public override string ToString()
        {
            if (Address == null)
            {
                return Protocol + ":empty";
            }
            else
            {
                IPEndPoint ep = new IPEndPoint(Address, Port);
                return $"{Protocol}:{ep}";
            }
        }

        public static bool AreEqual(SIPEndPoint endPoint1, SIPEndPoint endPoint2)
        {
            return endPoint1 == endPoint2;
        }

        public override bool Equals(object obj)
        {
            if (obj is SIPEndPoint sipEndPoint)
            {
                return AreEqual(this, sipEndPoint);
            }
            return false;
        }

        public static bool operator ==(SIPEndPoint endPoint1, SIPEndPoint endPoint2)
        {
            if ((object)endPoint1 == null && (object)endPoint2 == null)
            {
                return true;
            }
            else if ((object)endPoint1 == null || (object)endPoint2 == null)
            {
                return false;
            }
            else if (endPoint1.ToString() != endPoint2.ToString())
            {
                return false;
            }
            else if (endPoint1.ChannelID != null && endPoint1.ChannelID != endPoint2.ChannelID)
            {
                return false;
            }
            else if (endPoint1.ConnectionID != null && endPoint1.ConnectionID != endPoint2.ConnectionID)
            {
                return false;
            }

            return true;
        }

        public static bool operator !=(SIPEndPoint endPoint1, SIPEndPoint endPoint2)
        {
            return !(endPoint1 == endPoint2);
        }

        public override int GetHashCode()
        {
            return Protocol.GetHashCode() +
                + (Address != null ? Address.GetHashCode() : 0)
                + Port.GetHashCode()
                + (ChannelID != null ? ChannelID.GetHashCode() : 0)
                + (ConnectionID != null ? ConnectionID.GetHashCode() : 0);
        }

        public SIPEndPoint CopyOf()
        {
            return new SIPEndPoint(Protocol, Address != null ? new IPAddress(Address.GetAddressBytes()) : null, Port, ChannelID, ConnectionID);
        }

        /// <summary>
        /// Get the IP end point from the SIP end point
        /// </summary>
        /// <param name="mapIpv4ToIpv6">Set to true if a resultant IPv4 end point should be mapped to IPv6.
        /// This is required in some cases when using dual mode sockets. For example Mono requires that a destination IP
        /// end point for a dual mode socket is set as IPv6.</param>
        /// <returns>An IP end point.</returns>
        public IPEndPoint GetIPEndPoint(bool mapIpv4ToIpv6 = false)
        {
            if (mapIpv4ToIpv6 && Address.AddressFamily == AddressFamily.InterNetwork)
            {
                return new IPEndPoint(Address.MapToIPv6(), Port);
            }
            else
            {
                return new IPEndPoint(Address, Port);
            }
        }

        /// <summary>
        /// Determines whether the socket destination for two different SIP end points are equal.
        /// </summary>
        /// <param name="endPoint1">First end point to compare.</param>
        /// <param name="endPoint2">Second end point to compare.</param>
        /// <returns>True if the end points both resolve to the same protocol and IP end point.</returns>
        public static bool AreSocketsEqual(SIPEndPoint endPoint1, SIPEndPoint endPoint2)
        {
            if (endPoint1 == Empty || endPoint2 == Empty)
            {
                return false;
            }
            else
            {
                var ep1Address = (endPoint1.Address.IsIPv4MappedToIPv6) ? endPoint1.Address.MapToIPv4() : endPoint1.Address;
                var ep2Address = (endPoint2.Address.IsIPv4MappedToIPv6) ? endPoint2.Address.MapToIPv4() : endPoint2.Address;

                return endPoint1.Protocol == endPoint2.Protocol &&
                    endPoint1.Port == endPoint2.Port &&
                    ep1Address.Equals(ep2Address) ;
            }
        }

        public bool IsSocketEqual(SIPEndPoint endPoint)
        {
            return AreSocketsEqual(this, endPoint);
        }
    }
}
