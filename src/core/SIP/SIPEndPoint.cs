//-----------------------------------------------------------------------------
// Filename: SIPEndPoint.cs
//
// Description: Represents what needs to be known about a SIP end point for network communications.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 OCt 2019	Aaron Clauson	Added mssing header.
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
    /// transport properties. It contains all the information need to deliver a SIP request or response to
    /// a remote end point.
    /// 
    /// This class must remain immutable otherwise the SIP stack can develop problems. SIP end points can get
    /// passed amongst different servers for logging and forwarding SIP messages and a modification of the end point
    /// by one server can result in a problem for a different server. Instead a new SIP end point should be created
    /// wherever a modification is required.
    /// </summary>
    public class SIPEndPoint
    {
        private static int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static int m_defaultSIPTLSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;

        public SIPSchemesEnum Scheme { get; private set; } = SIPSchemesEnum.sip;
        public SIPProtocolsEnum Protocol { get; private set; } = SIPProtocolsEnum.udp;
        public IPAddress Address { get; private set; }
        public int Port { get; private set; }

        private SIPEndPoint() { }

        public SIPEndPoint(IPEndPoint endPoint)
        {
            Protocol = SIPProtocolsEnum.udp;
            Address = endPoint.Address;
            Port = endPoint.Port;
        }

        public SIPEndPoint(SIPProtocolsEnum protocol, IPAddress address, int port)
        {
            Protocol = protocol;
            Address = address;
            Port = (port == 0) ? (Protocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort : port;
        }

        public SIPEndPoint(SIPURI sipURI)
        {
            Protocol = sipURI.Protocol;

            if(!IPSocket.TryParseIPEndPoint(sipURI.Host, out var endPoint))
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

        /// <summary>
        /// Parses a SIP end point from either a serialised SIP end point string, format of:
        /// (udp|tcp|tls):(IPEndpoint)
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

            if (sipEndPointStr.ToLower().StartsWith("udp:") || sipEndPointStr.ToLower().StartsWith("tcp:") || sipEndPointStr.ToLower().StartsWith("tls:"))
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
        /// Reverses ToString().
        /// </summary>
        /// <param name="serialisedSIPEndPoint">The serialised SIP end point MUST be in the form protocol:socket and protocol must
        /// be exactly 3 characters. Valid examples are udp:10.0.0.1:5060, invalid example is 10.0.0.1:5060.</param>
        private static SIPEndPoint ParseSerialisedSIPEndPoint(string serialisedSIPEndPoint)
        {
            if (!IPSocket.TryParseIPEndPoint(serialisedSIPEndPoint.Substring(4), out var endPoint))
            {
                throw new ApplicationException($"Could not parse SIPURI host {serialisedSIPEndPoint.Substring(4)} as an IP end point.");
            }

            return new SIPEndPoint(SIPProtocolsType.GetProtocolType(serialisedSIPEndPoint.Substring(0, 3)), endPoint);
        }

        public override string ToString()
        {
            IPEndPoint ep = new IPEndPoint(Address, Port);
            return Protocol + ":" + ep.ToString();
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
            return Protocol.GetHashCode() + Address.GetHashCode() + Port.GetHashCode();
        }

        public SIPEndPoint CopyOf()
        {
            SIPEndPoint copy = new SIPEndPoint(Protocol, new IPAddress(Address.GetAddressBytes()), Port);
            return copy;
        }

        public IPEndPoint GetIPEndPoint()
        {
            return new IPEndPoint(Address, Port);
        }
    }
}
