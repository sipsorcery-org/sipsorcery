//-----------------------------------------------------------------------------
// Filename: SIPEndPoint.cs
//
// Description: Represents what needs to be known about a SIP end point for network communications.
//
// History:
// 14 OCt 2019	Aaron Clauson	Added mssing header.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com)
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
        /// Or from a string that represents a SIP URI.
        /// </summary>
        /// <param name="sipEndPointStr">The string to parse to extract the SIP end point.</param>
        /// <returns>If successful a SIPEndPoint object or null otherwise.</returns>
        public static SIPEndPoint ParseSIPEndPoint(string sipEndPointStr)
        {
            if (sipEndPointStr.IsNullOrBlank())
            {
                return null;
            }

            if (sipEndPointStr.StartsWith("udp") || sipEndPointStr.StartsWith("tcp") || sipEndPointStr.StartsWith("tls"))
            {
                return ParseSerialisedSIPEndPoint(sipEndPointStr);
            }
            else
            {
                var sipUri = SIPURI.ParseSIPURIRelaxed(sipEndPointStr);
                var sipEndPoint = sipUri.ToSIPEndPoint();
                sipEndPoint.Scheme = sipUri.Scheme;
                return sipEndPoint;
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
