//-----------------------------------------------------------------------------
// Filename: SIPEndPoint.cs
//
// Description: Represents what needs to be known about a SIP end point for network communications.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 Oct 2019	Aaron Clauson	Added mssing header.
// 07 Nov 2019  Aaron Clauson   Added ConnectionID property.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// This class is a more specific verions of the SIPURI class BUT is only concerned with the network and
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
        private const string CONNECTIONID_ATTRIBUTE_NAME = "connid";

        private static int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static int m_defaultSIPTLSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;

        /// <summary>
        /// The scheme the SIP end point is using. Note that some schemes and protocols are mutually exclusive.
        /// For example sips cannot be sent over UDP.
        /// </summary>
        public SIPSchemesEnum Scheme { get; private set; } = SIPSchemesEnum.sip;

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

        private SIPEndPoint() { }

        /// <summary>
        /// Instantiates a new SIP end point from a network end point. Non specified properties
        /// will be set to their defaults.
        /// </summary>
        public SIPEndPoint(IPEndPoint endPoint)
        {
            Protocol = SIPProtocolsEnum.udp;
            Address = endPoint.Address;
            Port = endPoint.Port;
        }

        /// <summary>
        /// Instantiates a new SIP end point.
        /// </summary>
        /// <param name="protocol">The SIP transport/application protocol used for the transmission.</param>
        /// <param name="address">The network address.</param>
        /// <param name="port">The network port.</param>
        /// <param name="connectionID">For connection oriented protocols the unique ID of the connection.
        /// For connectionless protocols should be set to null.</param>
        public SIPEndPoint(SIPProtocolsEnum protocol, IPAddress address, int port, string connectionID)
        {
            Protocol = protocol;
            Address = address;
            Port = (port == 0) ? (Protocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort : port;
            ConnectionID = connectionID;
        }

        public SIPEndPoint(SIPURI sipURI)
        {
            Protocol = sipURI.Protocol;

            if (!IPSocket.TryParseIPEndPoint(sipURI.Host, out var endPoint))
            {
                throw new ApplicationException($"Could not parse SIPURI host {sipURI.Host} as an IP end point.");
            }

            Address = endPoint.Address;
            Port = (endPoint.Port == 0) ? (Protocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort : endPoint.Port;
        }

        public SIPEndPoint(SIPProtocolsEnum protocol, IPEndPoint endPoint)
        {
            Protocol = protocol;
            Address = endPoint.Address;
            Port = (endPoint.Port == 0) ? (Protocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort : endPoint.Port;
        }

        public SIPEndPoint(SIPProtocolsEnum protocol, IPEndPoint endPoint, string connectionID)
        {
            Protocol = protocol;
            Address = endPoint.Address;
            Port = (endPoint.Port == 0) ? (Protocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort : endPoint.Port;
            ConnectionID = connectionID;
        }

        /// <summary>
        /// Parses a SIP end point from either a serialised SIP end point string, format of:
        /// (udp|tcp|tls|ws|wss):(IPEndpoint)
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
                    sipEndPoint.Scheme = sipUri.Scheme;
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
            string connectionID = null;
            string endPointStr = null;
            string protcolStr = serialisedSIPEndPoint.Substring(0, serialisedSIPEndPoint.IndexOf(':'));

            if (serialisedSIPEndPoint.Contains(";"))
            {
                endPointStr = serialisedSIPEndPoint.Slice(':', ';');
                connectionID = serialisedSIPEndPoint.Substring(serialisedSIPEndPoint.IndexOf(';'));
            }
            else
            {
                endPointStr = serialisedSIPEndPoint.Substring(serialisedSIPEndPoint.IndexOf(':') + 1);
            }
            
            if (!IPSocket.TryParseIPEndPoint(endPointStr, out var endPoint))
            {
                throw new ApplicationException($"Could not parse SIPEndPoint host {endPointStr} as an IP end point.");
            }

            return new SIPEndPoint(SIPProtocolsType.GetProtocolType(protcolStr), endPoint, connectionID);
        }

        public override string ToString()
        {
            IPEndPoint ep = new IPEndPoint(Address, Port);
            return Protocol + ":" + ep.ToString() + (!String.IsNullOrEmpty(ConnectionID) ? ";" + CONNECTIONID_ATTRIBUTE_NAME + "=" + ConnectionID : null);
        }

        public static bool AreEqual(SIPEndPoint endPoint1, SIPEndPoint endPoint2)
        {
            return endPoint1 == endPoint2;
        }

        public override bool Equals(object obj)
        {
            return AreEqual(this, (SIPEndPoint)obj);
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

            return true;
        }

        public static bool operator !=(SIPEndPoint endPoint1, SIPEndPoint endPoint2)
        {
            return !(endPoint1 == endPoint2);
        }

        public override int GetHashCode()
        {
            return Protocol.GetHashCode() + Address.GetHashCode() + Port.GetHashCode() + (ConnectionID != null ? ConnectionID.GetHashCode() : 0);
        }

        public SIPEndPoint CopyOf()
        {
            SIPEndPoint copy = new SIPEndPoint(Protocol, new IPAddress(Address.GetAddressBytes()), Port, ConnectionID);
            return copy;
        }

        public IPEndPoint GetIPEndPoint()
        {
            return new IPEndPoint(Address, Port);
        }
    }
}
