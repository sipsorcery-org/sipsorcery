using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// This class must remain immutable otherwise the SIP stack can develop problems. SIP end points can get
    /// passed amongst different servers for logging and forwarding SIP messages and a modification of the end point
    /// by one server can result in a problem for a different server. Instead a new SIP end point should be created
    /// wherever a modification is required.
    /// </summary>
    public class SIPEndPoint
    {
        private static ILog logger = Log.logger;

        private static string m_transportParameterKey = SIPHeaderAncillary.SIP_HEADERANC_TRANSPORT;
        private static int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static int m_defaultSIPTLSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;

        public SIPProtocolsEnum Protocol { get; private set; }
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
            IPEndPoint endPoint = IPSocket.ParseSocketString(sipURI.Host);
            Address = endPoint.Address;
            Port = (endPoint.Port == 0) ? (Protocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort : endPoint.Port;
        }

        public SIPEndPoint(SIPProtocolsEnum protocol, IPEndPoint endPoint)
        {
            Protocol = protocol;
            Address = endPoint.Address;
            Port = endPoint.Port;
        }

        public static SIPEndPoint ParseSIPEndPoint(string sipEndPointStr)
        {
            try
            {
                if (sipEndPointStr.IsNullOrBlank())
                {
                    return null;
                }

                if (sipEndPointStr.StartsWith("udp") || sipEndPointStr.StartsWith("tcp") || sipEndPointStr.StartsWith("tls"))
                {
                    return ParseSerialisedSIPEndPoint(sipEndPointStr);
                }

                string ipAddress = null;
                int port = 0;
                SIPProtocolsEnum protocol = SIPProtocolsEnum.udp;

                if (sipEndPointStr.StartsWith("sip:"))
                {
                    sipEndPointStr = sipEndPointStr.Substring(4);
                }
                else if (sipEndPointStr.StartsWith("sips:"))
                {
                    sipEndPointStr = sipEndPointStr.Substring(5);
                    protocol = SIPProtocolsEnum.tls;
                }

                int colonIndex = sipEndPointStr.IndexOf(':');
                int semiColonIndex = sipEndPointStr.IndexOf(';');
                if (colonIndex == -1 && semiColonIndex == -1)
                {
                    ipAddress = sipEndPointStr;
                }
                else if (colonIndex != -1 && semiColonIndex == -1)
                {
                    ipAddress = sipEndPointStr.Substring(0, colonIndex);
                    port = Convert.ToInt32(sipEndPointStr.Substring(colonIndex + 1));
                }
                else
                {
                    if (colonIndex != -1 && colonIndex < semiColonIndex)
                    {
                        ipAddress = sipEndPointStr.Substring(0, colonIndex);
                        port = Convert.ToInt32(sipEndPointStr.Substring(colonIndex + 1, semiColonIndex - colonIndex - 1));
                    }
                    else
                    {
                        ipAddress = sipEndPointStr.Substring(0, semiColonIndex);
                    }

                    if (protocol != SIPProtocolsEnum.tls)
                    {
                        sipEndPointStr = sipEndPointStr.Substring(semiColonIndex + 1);
                        int transportIndex = sipEndPointStr.ToLower().IndexOf(m_transportParameterKey + "=");
                        if (transportIndex != -1)
                        {
                            sipEndPointStr = sipEndPointStr.Substring(transportIndex + 10);
                            semiColonIndex = sipEndPointStr.IndexOf(';');
                            if (semiColonIndex != -1)
                            {
                                protocol = SIPProtocolsType.GetProtocolType(sipEndPointStr.Substring(0, semiColonIndex));
                            }
                            else
                            {
                                protocol = SIPProtocolsType.GetProtocolType(sipEndPointStr);
                            }
                        }
                    }
                }

                if (port == 0)
                {
                    port = (protocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort;
                }

                return new SIPEndPoint(protocol, IPAddress.Parse(ipAddress), port);
            }
            catch //(Exception excp)
            {
                //logger.Error("Exception ParseSIPEndPoint (sipEndPointStr=" + sipEndPointStr + "). " + excp.Message);
                throw;
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
            return new SIPEndPoint(SIPProtocolsType.GetProtocolType(serialisedSIPEndPoint.Substring(0, 3)), IPSocket.ParseSocketString(serialisedSIPEndPoint.Substring(4)));
        }

        public override string ToString()
        {
            return Protocol + ":" + Address + ":" + Port;
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
